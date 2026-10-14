namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// How soon a message is handled. Only a subscriber set up with <see cref="SubscriberBuilder.Prioritized"/> tells them
/// apart: it has a lane per priority, and its pump empties <see cref="High"/> before <see cref="Normal"/>, and
/// <see cref="Normal"/> before <see cref="Low"/>. Any other subscriber takes every priority on one queue.
/// </summary>
public enum MessagePriority
{
    /// <summary>Bulk work that can wait, such as regenerating after a version bump.</summary>
    Low = -1,

    /// <summary>The default.</summary>
    Normal = 0,

    /// <summary>Goes ahead of the queue.</summary>
    High = 1,
}
