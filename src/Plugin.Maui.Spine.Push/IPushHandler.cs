using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Push;

/// <summary>What a received message is.</summary>
public enum PushKind
{
    /// <summary>A user-visible notification.</summary>
    Alert,

    /// <summary>A silent message; the handler runs, nothing is shown.</summary>
    Silent,

    /// <summary>A Live Activity start, update or end.</summary>
    LiveActivity,

    /// <summary>A request to rebuild one widget kind, or all of them.</summary>
    Widget,
}

/// <summary>What the system should show while the app is in the foreground.</summary>
[Flags]
public enum PushPresentation
{
    /// <summary>Nothing — the app shows it in its own UI.</summary>
    None = 0,

    /// <summary>A banner over the app.</summary>
    Banner = 1,

    /// <summary>An entry in the Notification Center.</summary>
    List = 2,

    /// <summary>The notification's sound.</summary>
    Sound = 4,

    /// <summary>The badge on the app icon.</summary>
    Badge = 8,
}

/// <summary>A message as it reached the app.</summary>
/// <param name="Kind">What the message is.</param>
/// <param name="Title">The first line, when there is one.</param>
/// <param name="Body">The body text, when there is one.</param>
/// <param name="Route">The page to open, when the sender named one.</param>
/// <param name="Data">Everything the payload carried, Spine's own keys included.</param>
/// <param name="Channel">The channel or thread the sender put it in.</param>
/// <param name="CollapseId">The id an undelivered message was replaced by.</param>
public sealed record PushMessage(
    PushKind Kind,
    string? Title,
    string? Body,
    string? Route,
    IReadOnlyDictionary<string, string> Data,
    string? Channel,
    string? CollapseId)
{
    /// <summary>
    /// Whether this device scheduled the notification itself through
    /// <see cref="ILocalNotificationService"/>, rather than a server sending it. Read from the data
    /// bag rather than kept beside it, because that is what survives the trip out to the platform's
    /// notification and back when the user opens it.
    /// </summary>
    public bool IsLocal => Data.GetValueOrDefault(PushKeys.Source) == PushKeys.Sources.Local;

    /// <summary>Reads a payload's data bag into a message.</summary>
    /// <param name="data">The flattened payload, as the platform delivered it.</param>
    /// <returns>The message.</returns>
    public static PushMessage From(IReadOnlyDictionary<string, string> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return new PushMessage(
            Kind: data.GetValueOrDefault(PushKeys.Kind) switch
            {
                PushKeys.Kinds.Silent => PushKind.Silent,
                PushKeys.Kinds.LiveActivity => PushKind.LiveActivity,
                PushKeys.Kinds.Widget => PushKind.Widget,
                _ => PushKind.Alert,
            },
            Title: data.GetValueOrDefault(PushKeys.Title),
            Body: data.GetValueOrDefault(PushKeys.Body),
            Route: data.GetValueOrDefault(PushKeys.Route),
            Data: data,
            Channel: data.GetValueOrDefault(PushKeys.Channel),
            CollapseId: data.GetValueOrDefault(PushKeys.Collapse));
    }
}

/// <summary>Where and when a message arrived.</summary>
/// <param name="IsForeground">Whether the app was in front.</param>
/// <param name="IsColdStart">Whether the app was started by this message.</param>
/// <param name="ReceivedAt">When it arrived.</param>
/// <param name="Deadline">
/// Cancelled when the platform's budget for handling a background message runs out — about 25
/// seconds. Pass it on to whatever the handler does.
/// </param>
public sealed record PushContext(
    bool IsForeground,
    bool IsColdStart,
    DateTimeOffset ReceivedAt,
    CancellationToken Deadline);

/// <summary>
/// The app's side of a received message. Register one with
/// <see cref="SpinePushOptions.UseHandler{THandler}"/>; it is resolved through DI for every message,
/// so constructor injection works as in a view model.
/// </summary>
public interface IPushHandler
{
    /// <summary>
    /// A message arrived: in the foreground, or silently in the background. The return value decides
    /// what the system shows while the app is in front.
    /// </summary>
    /// <param name="message">What arrived.</param>
    /// <param name="context">Where and when.</param>
    /// <returns>What to show. <see cref="PushPresentation.None"/> to show it in the app instead.</returns>
    Task<PushPresentation> OnReceivedAsync(PushMessage message, PushContext context);

    /// <summary>
    /// The user opened the notification, or tapped one of its buttons. Runs on the main thread, and
    /// on a cold start after the Spine host has been created, so navigating from here is safe.
    /// </summary>
    /// <param name="message">What was opened.</param>
    /// <param name="action">The button's id, or <see langword="null"/> when the notification itself was tapped.</param>
    Task OnOpenedAsync(PushMessage message, string? action);
}
