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

    /// <summary>
    /// The channel a Live Activity started by an FCM message follows. Android subscribes to its topic
    /// while the activity runs; see <see cref="LiveActivityChannels"/>.
    /// </summary>
    public const string ActivityChannel = "spine.activity-channel";

    /// <summary>A serialized <see cref="LiveActivityLayout"/>, for platforms that render it in the app's process.</summary>
    public const string Layout = "spine.layout";

    /// <summary>The widget kind a <see cref="Kinds.Widget"/> message should refresh; all of them when absent.</summary>
    public const string Widget = "spine.widget";

    /// <summary>
    /// The id an undelivered message is replaced by. On APNs this is the <c>apns-collapse-id</c>
    /// header, which the device does not pass on, so it travels as data too.
    /// </summary>
    public const string Collapse = "spine.collapse";

    /// <summary>
    /// The buttons a notification shows: the id of a category the app declared with
    /// <c>SpinePushOptions.AddCategory</c>. iOS reads the same id from <c>aps.category</c>, which the
    /// server writes beside this. An id the app never declared arrives without buttons.
    /// </summary>
    public const string Category = "spine.category";

    /// <summary>
    /// A picture to show with the notification: an <c>https</c> URL, or for a local notification a
    /// file on the device. A pushed one on iOS needs the Notification Service Extension that
    /// <c>SpinePushImages=true</c> adds; without it the notification arrives as text.
    /// </summary>
    public const string Image = "spine.image";

    /// <summary>
    /// Where the notification came from: one of the values in <see cref="Sources"/>. Absent means a
    /// server sent it, which is the case a payload from outside Spine also lands in.
    /// </summary>
    public const string Source = "spine.source";

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

    /// <summary>The values <see cref="Source"/> takes.</summary>
    public static class Sources
    {
        /// <summary>The device scheduled it itself; no server was involved.</summary>
        public const string Local = "local";
    }
}
