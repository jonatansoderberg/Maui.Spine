using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// Declares messaging in <see cref="ServiceCollectionExtensions.AddSpineMessaging"/>: the transport, the message types,
/// the subscribers and the shared settings.
/// </summary>
public sealed partial class MessagingBuilder
{
    private readonly List<(Type ClrType, string Name, Func<JsonSerializerOptions, MessageType> Create)> _types = [];
    private readonly List<SubscriberBuilder> _subscribers = [];
    private Func<IServiceProvider, IMessageTransport>? _transport;
    private Func<IServiceProvider, IMessageStore>? _store;

    internal MessagingBuilder(IServiceCollection services) => Services = services;

    /// <summary>The service collection, for transport packages that register their clients.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// How long failed messages, cancel markers and offloaded payloads of fanned-out messages are kept, and the default
    /// time to live of a message. 14 days by default.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Payloads larger than this many bytes are gzipped. 8 KB by default.</summary>
    public int CompressionThreshold { get; set; } = 8 * 1024;

    /// <summary>
    /// How many times a message may be handed out without completing (the handler crashed or ran past its lock) before
    /// it is kept as failed. 10 by default. Ordinary failures are counted by the subscriber's <see cref="RetryPolicy"/>.
    /// </summary>
    public int MaxDeliveries { get; set; } = 10;

    /// <summary>How long an idle pump waits at most between polls. 5 seconds by default.</summary>
    public TimeSpan MaxPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How often the janitor's hosted service runs. One hour by default.</summary>
    public TimeSpan JanitorInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The serialiser options for payloads without a <see cref="JsonTypeInfo{T}"/>. Web defaults.</summary>
    public JsonSerializerOptions JsonOptions { get; set; } = new(JsonSerializerDefaults.Web);

    /// <summary>Registers a message type.</summary>
    /// <param name="name">Its stable name on the wire. Unset, the type's name.</param>
    /// <param name="typeInfo">Source-generated serialisation, for trimming and speed. Unset, <see cref="JsonOptions"/>.</param>
    public MessagingBuilder Message<T>(string? name = null, JsonTypeInfo<T>? typeInfo = null) where T : notnull
    {
        name ??= typeof(T).Name;
        if (!TypeName().IsMatch(name))
            throw new ArgumentException($"'{name}' is not a valid message type name: use 1–100 letters, digits, '.', '_' or '-'.", nameof(name));
        if (_types.Any(t => t.ClrType == typeof(T)))
            throw new InvalidOperationException($"{typeof(T).Name} is registered twice.");
        if (_types.Any(t => t.Name == name))
            throw new InvalidOperationException($"Two message types are named '{name}'.");

        _types.Add((typeof(T), name, options => MessageType.Create(name, typeInfo, options)));
        return this;
    }

    /// <summary>Declares a subscriber: a service or function that handles the messages it takes.</summary>
    /// <param name="name">
    /// Its name, which is also its queue's or subscription's: 3–40 lowercase letters, digits and single hyphens.
    /// </param>
    /// <param name="configure">The types it takes, its filters, priority, concurrency and retries.</param>
    public MessagingBuilder Subscriber(string name, Action<SubscriberBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (!SubscriberName().IsMatch(name))
            throw new ArgumentException($"'{name}' is not a valid subscriber name: use 3–40 lowercase letters, digits and single hyphens.", nameof(name));

        var subscriber = new SubscriberBuilder(name);
        configure(subscriber);
        _subscribers.Add(subscriber);
        return this;
    }

    /// <summary>Sets the transport. Transport packages call this from their <c>Use…</c> method.</summary>
    public MessagingBuilder UseTransport(Func<IServiceProvider, IMessageTransport> transport)
    {
        _transport = transport;
        return this;
    }

    /// <summary>Sets the store for payloads, failed messages and markers. Transport packages call this too.</summary>
    public MessagingBuilder UseStore(Func<IServiceProvider, IMessageStore> store)
    {
        _store = store;
        return this;
    }

    internal MessagingSettings Build()
    {
        if (_transport is null || _store is null)
            throw new InvalidOperationException("Messaging has no transport. Call UseAzureStorage, UseServiceBus or UseInMemory in AddSpineMessaging.");
        if (Retention <= TimeSpan.Zero)
            throw new InvalidOperationException("Retention must be positive.");

        foreach (var clrType in _subscribers.SelectMany(s => s.ClrTypes).Distinct().Where(t => _types.All(r => r.ClrType != t)))
            throw new InvalidOperationException($"A subscriber handles {clrType.Name}, which is not registered. Register it with Message<{clrType.Name}>().");

        var json = JsonOptions.TypeInfoResolver is null
            ? new JsonSerializerOptions(JsonOptions) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }
            : JsonOptions;
        json.MakeReadOnly();
        var types = _types.Select(t => t.Create(json)).ToList();
        var subscribers = _subscribers.Select(s => s.Build(types)).ToList();

        var lanes = subscribers.SelectMany(s => s.Lanes).GroupBy(l => l.Name).FirstOrDefault(g => g.Count() > 1);
        if (lanes is not null)
            throw new InvalidOperationException($"Two subscribers share the queue name '{lanes.Key}'. A prioritised subscriber also uses '<name>-high' and '<name>-low'.");

        return new MessagingSettings(new MessageTopology(types, subscribers), _transport, _store, Retention, CompressionThreshold,
            MaxDeliveries, MaxPollInterval, JanitorInterval);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex TypeName();

    [GeneratedRegex("^(?=.{3,40}$)[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SubscriberName();
}

/// <summary>Declares one subscriber; see <see cref="MessagingBuilder.Subscriber"/>.</summary>
public sealed class SubscriberBuilder
{
    private readonly string _name;
    private readonly List<Type> _types = [];
    private readonly List<SubscriberFilter> _filters = [];
    private bool _prioritized;
    private int _maxConcurrency = 4;
    private RetryPolicy _retry = RetryPolicy.Default;

    internal SubscriberBuilder(string name) => _name = name;

    internal IEnumerable<Type> ClrTypes => _types;

    /// <summary>Takes messages of <typeparamref name="T"/>. Several calls take several types.</summary>
    public SubscriberBuilder Handles<T>() where T : notnull
    {
        if (!_types.Contains(typeof(T)))
            _types.Add(typeof(T));
        return this;
    }

    /// <summary>
    /// Takes only messages whose metadata has <paramref name="key"/> equal to <paramref name="value"/>. Several calls
    /// must all match.
    /// </summary>
    public SubscriberBuilder Where(string key, string value)
    {
        MetadataRules.ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        if (_filters.Any(f => f.Key == key))
            throw new ArgumentException($"Subscriber '{_name}' filters on '{key}' twice. A message carries one value per key, so both could never match.", nameof(key));
        _filters.Add(new SubscriberFilter(key, value));
        return this;
    }

    /// <summary>Gives the subscriber a lane per <see cref="MessagePriority"/>, handled high first.</summary>
    public SubscriberBuilder Prioritized()
    {
        _prioritized = true;
        return this;
    }

    /// <summary>How many messages one pump handles at a time. 4 by default. Azure Functions set this in host.json.</summary>
    public SubscriberBuilder MaxConcurrency(int messages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(messages, 1);
        _maxConcurrency = messages;
        return this;
    }

    /// <summary>How failing messages are retried; see <see cref="RetryPolicy"/>.</summary>
    /// <param name="attempts">Attempts in all, the first included.</param>
    /// <param name="first">The wait after the first failure; unset, 10 seconds.</param>
    /// <param name="max">The longest wait; unset, 10 minutes.</param>
    public SubscriberBuilder Retry(int attempts, TimeSpan? first = null, TimeSpan? max = null)
    {
        _retry = new RetryPolicy(attempts, first ?? RetryPolicy.Default.FirstDelay, max ?? RetryPolicy.Default.MaxDelay);
        _retry.Validate(_name);
        return this;
    }

    internal Subscriber Build(IReadOnlyList<MessageType> types)
    {
        if (_types.Count == 0)
            throw new InvalidOperationException($"Subscriber '{_name}' handles no message type. Call Handles<T>().");

        return new Subscriber(_name, [.. _types.Select(t => types.First(m => m.ClrType == t))], _filters, _prioritized, _maxConcurrency, _retry);
    }
}

/// <summary>What <see cref="MessagingBuilder"/> declared.</summary>
internal sealed record MessagingSettings(
    MessageTopology Topology,
    Func<IServiceProvider, IMessageTransport> Transport,
    Func<IServiceProvider, IMessageStore> Store,
    TimeSpan Retention,
    int CompressionThreshold,
    int MaxDeliveries,
    TimeSpan MaxPollInterval,
    TimeSpan JanitorInterval);

internal static partial class MetadataRules
{
    public const string ReservedPrefix = "spine-";

    /// <summary>Metadata travels in every copy and counts against the transport's inline size, so it is kept small.</summary>
    public const int MaxBytes = 4 * 1024;

    /// <summary>Roughly what metadata adds to a message: its keys and values, and a little per entry.</summary>
    public static int SizeOf(IEnumerable<KeyValuePair<string, string>> metadata) =>
        metadata.Sum(m => System.Text.Encoding.UTF8.GetByteCount(m.Key) + System.Text.Encoding.UTF8.GetByteCount(m.Value) + 8);

    public static void ValidateSize(IEnumerable<KeyValuePair<string, string>> metadata)
    {
        var size = SizeOf(metadata);
        if (size > MaxBytes)
            throw new ArgumentException($"The metadata is {size} bytes; at most {MaxBytes} are allowed. Put larger values in the message itself.", nameof(metadata));
    }

    public static void ValidateKey(string key)
    {
        if (!Key().IsMatch(key))
            throw new ArgumentException($"'{key}' is not a valid metadata key: use 1–100 letters, digits, '_', '.' or '-'.", nameof(key));
        if (key.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Metadata key '{key}' starts with '{ReservedPrefix}', which Spine keeps for itself.", nameof(key));
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex Key();
}
