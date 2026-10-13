namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>How one message is published. Every property is optional.</summary>
public sealed class PublishOptions
{
    internal static readonly PublishOptions Default = new();

    /// <summary>The message's priority; see <see cref="MessagePriority"/>.</summary>
    public MessagePriority Priority { get; set; }

    /// <summary>
    /// When the message is handled. It stays dormant in the queue until then. A time in the past delivers at once. Wins
    /// over <see cref="Delay"/>.
    /// </summary>
    public DateTimeOffset? DeliverAt { get; set; }

    /// <summary>How long the message stays dormant before it is handled.</summary>
    public TimeSpan? Delay { get; set; }

    /// <summary>
    /// Strings that travel with the message. Subscribers can filter on them with <see cref="SubscriberBuilder.Where"/>,
    /// and handlers read them from <see cref="MessageContext.Metadata"/>. Keys are letters, digits, <c>_</c>, <c>.</c> and
    /// <c>-</c>, and may not start with <c>spine-</c>.
    /// </summary>
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The message's id. Unset, a new time-ordered id.</summary>
    public string? MessageId { get; set; }

    /// <summary>An id that ties messages of one flow together; it reaches <see cref="MessageContext.CorrelationId"/>.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Publishes the message only if no message with the same id was published within <see cref="DeduplicationWindow"/>.
    /// A repeat returns <see cref="PublishResult.IsDuplicate"/> and sends nothing.
    /// </summary>
    public string? DeduplicationId { get; set; }

    /// <summary>How long a <see cref="DeduplicationId"/> holds. One hour by default.</summary>
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long the message may wait, counted from when it is due. A message that is not handled in time is dropped.
    /// Unset, the retention period (<see cref="MessagingBuilder.Retention"/>).
    /// </summary>
    public TimeSpan? TimeToLive { get; set; }
}

/// <summary>What a publish did.</summary>
/// <param name="MessageId">The message's id.</param>
/// <param name="DeliverAt">When it is due, for a scheduled message; pass the result to <see cref="IMessagePublisher.CancelAsync(PublishResult, CancellationToken)"/> to cancel it.</param>
/// <param name="Subscribers">The subscribers it went to. Empty when none takes it, which is logged as a warning.</param>
/// <param name="IsDuplicate">Whether it was dropped as a repeat of its <see cref="PublishOptions.DeduplicationId"/>.</param>
public sealed record PublishResult(string MessageId, DateTimeOffset? DeliverAt, IReadOnlyList<string> Subscribers, bool IsDuplicate);
