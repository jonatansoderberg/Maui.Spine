using MauiSpinePushSampleApp.Services;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Push;

namespace MauiSpinePushSampleApp;

/// <summary>
/// Writes everything to the log and shows nothing while the app is in front, so the Log page is the
/// proof rather than a banner covering it. A real app would answer with what it wants shown.
/// </summary>
/// <param name="log">Where the entries go.</param>
/// <param name="navigation">Used when the user opens a notification that carries a route.</param>
/// <param name="content">What the widget shows, when a silent message brings new content.</param>
/// <param name="widgets">Asked to rebuild the widget once the content has changed.</param>
public sealed class SamplePushHandler(
    PushLog log, INavigationService navigation, WidgetContent content, IWidgetService widgets) : IPushHandler
{
    /// <inheritdoc />
    public Task<PushPresentation> OnReceivedAsync(PushMessage message, PushContext context)
    {
        var answer = context.IsForeground ? PushPresentation.None : PushPresentation.Banner | PushPresentation.Sound | PushPresentation.List;

        // A widget push means "rebuild" and carries no content; content arrives as a silent message,
        // which is the half of the pair the app owns. See WidgetContent for why it is split that way.
        if (message.Kind == PushKind.Silent && message.Data.ContainsKey(WidgetContent.TitleData))
            UpdateWidget(message, context);

        log.Add(new PushLogEntry(
            context.ReceivedAt.ToLocalTime(),
            // Whether the device scheduled it itself is worth seeing in the log; nothing else in the
            // handler has to care, which is the point of the flag being one property on the message.
            message.IsLocal ? $"{message.Kind} (lokal)" : message.Kind.ToString(),
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
            message.Data.GetValueOrDefault(WidgetContent.TitleData),
            message.Data.GetValueOrDefault(WidgetContent.BodyData),
            context.ReceivedAt);

        _ = widgets.RefreshAsync("sample", context.Deadline);
    }

    /// <inheritdoc />
    public async Task OnOpenedAsync(PushMessage message, string? action)
    {
        log.Note(message.IsLocal ? "opened (lokal)" : "opened", $"route: {message.Route ?? "none"}{(action is null ? "" : $", action: {action}")}");

        // The route is a string the app decides the meaning of; Spine only carries it.
        if (message.Route is "log") await navigation.NavigateToAsync<Pages.LogPage>();
    }
}
