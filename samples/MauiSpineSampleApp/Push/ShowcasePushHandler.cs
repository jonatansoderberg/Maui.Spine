using MauiSpineSampleApp.Pages.Push;
using MauiSpineSampleApp.Widgets;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.PushNotifications;

namespace MauiSpineSampleApp.Push;

/// <summary>
/// Writes everything to the push log and shows nothing while the app is in front, so the log is the
/// proof rather than a banner covering it. A real app would answer with what it wants shown.
/// </summary>
/// <param name="log">Where the entries go.</param>
/// <param name="navigation">Used when the user opens a notification that carries a route.</param>
/// <param name="content">What the push widget shows, when a silent message brings new content.</param>
/// <param name="widgets">Asked to rebuild the widget once the content has changed.</param>
public sealed class ShowcasePushHandler(
    PushLog log, INavigationService navigation, PushWidgetContent content, IWidgetService widgets) : IPushNotificationHandler
{
    /// <summary>The route the Showcase's notifications carry: the push log.</summary>
    public const string LogRoute = "log";

    /// <inheritdoc />
    public Task<PushPresentation> OnReceivedAsync(PushMessage message, PushContext context)
    {
        var answer = context.IsForeground ? PushPresentation.None : PushPresentation.Banner | PushPresentation.Sound | PushPresentation.List;

        // A widget push means "rebuild" and carries no content; content arrives as a silent message,
        // which is the half of the pair the app owns. See PushWidgetContent for why it is split that way.
        if (message.Kind == PushKind.Silent && message.Data.ContainsKey(PushWidgetContent.TitleData))
            UpdateWidget(message, context);

        log.Add(new PushLogEntry(
            context.ReceivedAt.ToLocalTime(),
            message.IsLocal ? $"{message.Kind} (local)" : message.Kind.ToString(),
            context.IsColdStart ? "cold start" : context.IsForeground ? "foreground" : "background",
            message.Title,
            message.Route,
            answer.ToString(),
            message.Data));

        return Task.FromResult(answer);
    }

    /// <summary>
    /// Stores what the message brought and asks for a rebuild. Fire-and-forget on purpose: the
    /// handler answers what to present, and a widget rebuild must not hold that up.
    /// </summary>
    private void UpdateWidget(PushMessage message, PushContext context)
    {
        content.Set(
            message.Data.GetValueOrDefault(PushWidgetContent.TitleData),
            message.Data.GetValueOrDefault(PushWidgetContent.BodyData),
            context.ReceivedAt);

        _ = widgets.RefreshAsync(PushWidget.Kind, context.Deadline);
    }

    /// <summary>
    /// A button that did not open the app, or a reply. Nothing to navigate to — the app may not even be
    /// on screen — so the log line is the whole proof it arrived.
    /// </summary>
    public Task OnActionAsync(PushMessage message, string action, string? text)
    {
        log.Note(message.IsLocal ? "action (local)" : "action", text is null ? action : $"{action}: “{text}”");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task OnOpenedAsync(PushMessage message, string? action)
    {
        log.Note(message.IsLocal ? "opened (local)" : "opened", $"route: {message.Route ?? "none"}{(action is null ? "" : $", action: {action}")}");

        // The route is a string the app decides the meaning of; Spine only carries it.
        if (message.Route is LogRoute) await navigation.ShowAsync<PushLogPage>();
    }
}
