using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// The message types and the subscribers that take them, as declared with <see cref="MessagingBuilder"/>. Publisher and
/// consumers must declare the same topology: Storage Queues route on the publishing side.
/// </summary>
public sealed class MessageTopology
{
    private readonly Dictionary<string, MessageType> _byName;
    private readonly Dictionary<Type, MessageType> _byClrType;
    private readonly Dictionary<string, Subscriber> _subscribers;

    internal MessageTopology(IReadOnlyList<MessageType> types, IReadOnlyList<Subscriber> subscribers)
    {
        Types = types;
        Subscribers = subscribers;
        _byName = types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        _byClrType = types.ToDictionary(t => t.ClrType);
        _subscribers = subscribers.ToDictionary(s => s.Name, StringComparer.Ordinal);
    }

    /// <summary>Every registered message type.</summary>
    public IReadOnlyList<MessageType> Types { get; }

    /// <summary>Every subscriber.</summary>
    public IReadOnlyList<Subscriber> Subscribers { get; }

    /// <summary>Every lane of every subscriber.</summary>
    public IEnumerable<MessageLane> Lanes => Subscribers.SelectMany(s => s.Lanes);

    /// <summary>The type registered under <paramref name="name"/>, or <see langword="null"/>.</summary>
    public MessageType? FindType(string name) => _byName.GetValueOrDefault(name);

    /// <summary>The subscriber named <paramref name="name"/>.</summary>
    /// <exception cref="InvalidOperationException">No subscriber has that name.</exception>
    public Subscriber GetSubscriber(string name) =>
        _subscribers.TryGetValue(name, out var subscriber)
            ? subscriber
            : throw new InvalidOperationException($"No subscriber is named '{name}'. Declare it with MessagingBuilder.Subscriber.");

    /// <summary>The lanes a message goes to: the matching lane of every subscriber that takes it.</summary>
    public IReadOnlyList<MessageLane> Route(string type, IReadOnlyDictionary<string, string> metadata, MessagePriority priority) =>
        [.. Subscribers.Where(s => s.Accepts(type, metadata)).Select(s => s.LaneFor(priority))];

    internal MessageType TypeOf(Type actual, Type declared) =>
        _byClrType.TryGetValue(actual, out var type) ? type : TypeOf(declared);

    internal MessageType TypeOf(Type clrType) =>
        _byClrType.TryGetValue(clrType, out var type)
            ? type
            : throw new InvalidOperationException($"{clrType.Name} is not a registered message type. Register it with MessagingBuilder.Message<{clrType.Name}>().");
}

/// <summary>A registered message type: its CLR type, its stable name on the wire, and how it is serialised.</summary>
public sealed class MessageType
{
    private readonly Func<object, byte[]> _serialize;
    private readonly Func<ReadOnlyMemory<byte>, object> _deserialize;
    private readonly Func<IServiceProvider, object, MessageContext, CancellationToken, Task> _handle;

    private MessageType(string name, Type clrType, Func<object, byte[]> serialize, Func<ReadOnlyMemory<byte>, object> deserialize,
        Func<IServiceProvider, object, MessageContext, CancellationToken, Task> handle)
    {
        Name = name;
        ClrType = clrType;
        _serialize = serialize;
        _deserialize = deserialize;
        _handle = handle;
    }

    /// <summary>The name on the wire, which subscribers filter on. Keep it stable: messages in flight carry it.</summary>
    public string Name { get; }

    /// <summary>The payload's CLR type.</summary>
    public Type ClrType { get; }

    internal byte[] Serialize(object message) => _serialize(message);

    internal object Deserialize(ReadOnlyMemory<byte> json) => _deserialize(json);

    internal Task HandleAsync(IServiceProvider services, object message, MessageContext context, CancellationToken cancellationToken) =>
        _handle(services, message, context, cancellationToken);

    internal static MessageType Create<T>(string name, JsonTypeInfo<T>? typeInfo, JsonSerializerOptions options) where T : notnull
    {
        var info = typeInfo ?? (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

        return new MessageType(
            name,
            typeof(T),
            message => JsonSerializer.SerializeToUtf8Bytes((T)message, info),
            json => JsonSerializer.Deserialize(json.Span, info) ?? throw new JsonException("The payload is null."),
            (services, message, context, cancellationToken) =>
            {
                var handler = services.GetService<IMessageHandler<T>>()
                    ?? throw new UnrecoverableMessageException(
                        $"No IMessageHandler<{typeof(T).Name}> is registered in this host. Register one with AddMessageHandler, or run subscriber '{context.Subscriber}' where it is.");
                return handler.HandleAsync((T)message, context, cancellationToken);
            });
    }
}

/// <summary>A subscriber: the message types and metadata it takes, its lanes, and how it handles failure.</summary>
public sealed class Subscriber
{
    private readonly HashSet<string> _types;

    internal Subscriber(string name, IReadOnlyList<MessageType> types, IReadOnlyList<SubscriberFilter> filters, bool prioritized,
        int maxConcurrency, RetryPolicy retry)
    {
        Name = name;
        Types = types;
        Filters = filters;
        IsPrioritized = prioritized;
        MaxConcurrency = maxConcurrency;
        Retry = retry;
        _types = [.. types.Select(t => t.Name)];
        Lanes = prioritized
            ? [new(name, MessagePriority.High, $"{name}-high"), new(name, MessagePriority.Normal, name), new(name, MessagePriority.Low, $"{name}-low")]
            : [new(name, MessagePriority.Normal, name)];
    }

    /// <summary>The subscriber's name, which is also its normal lane's name.</summary>
    public string Name { get; }

    /// <summary>The message types it takes.</summary>
    public IReadOnlyList<MessageType> Types { get; }

    /// <summary>Metadata a message must carry for it to take it; all must match.</summary>
    public IReadOnlyList<SubscriberFilter> Filters { get; }

    /// <summary>Whether it has a lane per priority.</summary>
    public bool IsPrioritized { get; }

    /// <summary>How many messages one pump handles at a time.</summary>
    public int MaxConcurrency { get; }

    /// <summary>How failing messages are retried.</summary>
    public RetryPolicy Retry { get; }

    /// <summary>Its lanes, highest priority first.</summary>
    public IReadOnlyList<MessageLane> Lanes { get; }

    /// <summary>Whether it takes a message of <paramref name="type"/> with <paramref name="metadata"/>.</summary>
    public bool Accepts(string type, IReadOnlyDictionary<string, string> metadata) =>
        _types.Contains(type) && Filters.All(f => metadata.TryGetValue(f.Key, out var value) && value == f.Value);

    /// <summary>The lane a message of <paramref name="priority"/> goes to.</summary>
    public MessageLane LaneFor(MessagePriority priority) =>
        IsPrioritized ? Lanes.First(l => l.Priority == priority) : Lanes[0];
}

/// <summary>A metadata value a message must carry for a subscriber to take it.</summary>
/// <param name="Key">The metadata key.</param>
/// <param name="Value">The value it must equal.</param>
public sealed record SubscriberFilter(string Key, string Value);

/// <summary>One queue of a subscriber: its only one, or one per priority.</summary>
/// <param name="Subscriber">The subscriber's name.</param>
/// <param name="Priority">The priority it carries; <see cref="MessagePriority.Normal"/> for a subscriber without priorities.</param>
/// <param name="Name">The queue's name: the subscriber's, with <c>-high</c> or <c>-low</c> for those lanes.</param>
public sealed record MessageLane(string Subscriber, MessagePriority Priority, string Name);
