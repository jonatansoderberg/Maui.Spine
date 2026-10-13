using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.Messaging.ServiceBus;

/// <summary>
/// Creates the topic and a subscription per lane, and keeps each subscription's rules equal to the topology: one
/// correlation rule per message type it takes, plus one for messages addressed to it alone (retries and replays).
/// Rules it did not make, such as <c>$Default</c>, are removed, since they would let every message in.
/// </summary>
internal sealed class ServiceBusProvisioner(ServiceBusAdministrationClient admin, ServiceBusMessagingOptions options, ILogger logger)
{
    private const string DirectRule = "direct";

    public async Task ProvisionAsync(MessageTopology topology, CancellationToken cancellationToken)
    {
        if (!await admin.TopicExistsAsync(options.TopicName, cancellationToken))
        {
            await CreateIgnoringConflictAsync(() => admin.CreateTopicAsync(options.TopicName, cancellationToken));
            logger.LogInformation("Created Service Bus topic {Topic}.", options.TopicName);
        }

        foreach (var subscriber in topology.Subscribers)
        foreach (var lane in subscriber.Lanes)
            await ProvisionLaneAsync(subscriber, lane, cancellationToken);
    }

    private async Task ProvisionLaneAsync(Subscriber subscriber, MessageLane lane, CancellationToken cancellationToken)
    {
        var wanted = RulesFor(subscriber, lane).ToDictionary(r => r.Name, StringComparer.Ordinal);

        if (!await admin.SubscriptionExistsAsync(options.TopicName, lane.Name, cancellationToken))
        {
            var first = wanted.Values.First();
            await CreateIgnoringConflictAsync(() => admin.CreateSubscriptionAsync(
                new CreateSubscriptionOptions(options.TopicName, lane.Name)
                {
                    MaxDeliveryCount = options.MaxDeliveryCount,
                    LockDuration = options.LockDuration,
                    DeadLetteringOnMessageExpiration = false,
                },
                first,
                cancellationToken));
            logger.LogInformation("Created Service Bus subscription {Subscription} on {Topic}.", lane.Name, options.TopicName);
        }
        else
        {
            var subscription = (await admin.GetSubscriptionAsync(options.TopicName, lane.Name, cancellationToken)).Value;
            if (subscription.LockDuration != options.LockDuration || subscription.MaxDeliveryCount != options.MaxDeliveryCount)
            {
                logger.LogInformation("Service Bus subscription {Subscription} had a lock of {Lock} and {Deliveries} deliveries; set to {WantedLock} and {WantedDeliveries}.",
                    lane.Name, subscription.LockDuration, subscription.MaxDeliveryCount, options.LockDuration, options.MaxDeliveryCount);
                subscription.LockDuration = options.LockDuration;
                subscription.MaxDeliveryCount = options.MaxDeliveryCount;
                await admin.UpdateSubscriptionAsync(subscription, cancellationToken);
            }
        }

        var existing = new Dictionary<string, RuleProperties>(StringComparer.Ordinal);
        await foreach (var rule in admin.GetRulesAsync(options.TopicName, lane.Name, cancellationToken))
            existing[rule.Name] = rule;

        foreach (var (name, rule) in existing)
        {
            if (!wanted.TryGetValue(name, out var want) || Describe(rule.Filter) != Describe(want.Filter))
                await admin.DeleteRuleAsync(options.TopicName, lane.Name, name, cancellationToken);
        }

        foreach (var (name, rule) in wanted)
        {
            if (!existing.TryGetValue(name, out var have) || Describe(have.Filter) != Describe(rule.Filter))
                await CreateIgnoringConflictAsync(() => admin.CreateRuleAsync(options.TopicName, lane.Name, rule, cancellationToken));
        }
    }

    internal static IEnumerable<CreateRuleOptions> RulesFor(Subscriber subscriber, MessageLane lane)
    {
        foreach (var type in subscriber.Types)
        {
            var filter = new CorrelationRuleFilter { Subject = type.Name };
            filter.ApplicationProperties[ServiceBusEnvelopeMapper.To] = ServiceBusEnvelopeMapper.Broadcast;
            foreach (var where in subscriber.Filters)
                filter.ApplicationProperties[where.Key] = where.Value;
            if (subscriber.IsPrioritized)
                filter.ApplicationProperties[ServiceBusEnvelopeMapper.Priority] = ServiceBusEnvelopeMapper.PriorityValue(lane.Priority);
            yield return new CreateRuleOptions(RuleName(type.Name), filter);
        }

        var direct = new CorrelationRuleFilter();
        direct.ApplicationProperties[ServiceBusEnvelopeMapper.To] = subscriber.Name;
        if (subscriber.IsPrioritized)
            direct.ApplicationProperties[ServiceBusEnvelopeMapper.Priority] = ServiceBusEnvelopeMapper.PriorityValue(lane.Priority);
        yield return new CreateRuleOptions(DirectRule, direct);
    }

    /// <summary>A rule name from a type name: at most 50 characters, so a long one ends in a short hash.</summary>
    private static string RuleName(string type)
    {
        var name = $"type-{type}";
        return name.Length <= 50 ? name : $"{name[..38]}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(type)))[..11]}";
    }

    private static string Describe(RuleFilter filter) => filter switch
    {
        CorrelationRuleFilter c => $"c|{c.Subject}|{string.Join(",", c.ApplicationProperties.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"))}",
        _ => filter.ToString() ?? filter.GetType().Name,
    };

    private static async Task CreateIgnoringConflictAsync(Func<Task> create)
    {
        try
        {
            await create();
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            // Another instance got there first.
        }
        catch (Azure.Messaging.ServiceBus.ServiceBusException exception) when (exception.Reason == Azure.Messaging.ServiceBus.ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
        }
    }
}
