namespace Plugin.Maui.Spine.Common;

/// <summary>
/// The data keys Spine puts in a push payload. The server writes them and the app's handler reads
/// them, so they are the contract between the two halves of Spine.Push and are never localized or
/// renamed.
/// </summary>
public static class PushKeys
{
    /// <summary>What the message is: one of the values in <see cref="Kinds"/>.</summary>
    public const string Kind = "spine.kind";

    /// <summary>
    /// The notification's first line. Present because Android is sent data-only — the package draws
    /// the notification itself so foreground and background behave alike and Spine picks the channel.
    /// </summary>
    public const string Title = "spine.title";

    /// <summary>The notification's body text. Present for the same reason as <see cref="Title"/>.</summary>
    public const string Body = "spine.body";

    /// <summary>The page to navigate to when the notification is opened.</summary>
    public const string Route = "spine.route";

    /// <summary>The Android notification channel, and the thread id on iOS.</summary>
    public const string Channel = "spine.channel";

    /// <summary>The Live Activity kind a <see cref="Kinds.LiveActivity"/> message applies to.</summary>
    public const string Activity = "spine.activity";

    /// <summary>A serialized <see cref="LiveActivityLayout"/>, for platforms that render it in the app's process.</summary>
    public const string Layout = "spine.layout";

    /// <summary>The widget kind a <see cref="Kinds.Widget"/> message should refresh; all of them when absent.</summary>
    public const string Widget = "spine.widget";

    /// <summary>
    /// The id an undelivered message is replaced by. On APNs this is the <c>apns-collapse-id</c>
    /// header, which the device does not pass on, so it travels as data too.
    /// </summary>
    public const string Collapse = "spine.collapse";

    /// <summary>The values <see cref="Kind"/> takes.</summary>
    public static class Kinds
    {
        /// <summary>A user-visible notification.</summary>
        public const string Alert = "alert";

        /// <summary>A silent message; the handler runs, nothing is shown.</summary>
        public const string Silent = "silent";

        /// <summary>Start, update, or end a Live Activity.</summary>
        public const string LiveActivity = "liveactivity";

        /// <summary>Rebuild one widget kind, or all of them.</summary>
        public const string Widget = "widget";
    }
}
