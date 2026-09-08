using MauiSpinePushSampleApp.Services;
using Plugin.Maui.Spine.Push;

namespace MauiSpinePushSampleApp;

/// <summary>
/// Writes everything to the log and shows nothing while the app is in front, so the Log page is the
/// proof rather than a banner covering it. A real app would answer with what it wants shown.
/// </summary>
/// <param name="log">Where the entries go.</param>
/// <param name="navigation">Used when the user opens a notification that carries a route.</param>
public sealed class SamplePushHandler(PushLog log, INavigationService navigation) : IPushHandler
{
    /// <inheritdoc />
    public Task<PushPresentation> OnReceivedAsync(PushMessage message, PushContext context)
    {
        var answer = context.IsForeground ? PushPresentation.None : PushPresentation.Banner | PushPresentation.Sound | PushPresentation.List;

        log.Add(new PushLogEntry(
            context.ReceivedAt.ToLocalTime(),
            message.Kind.ToString(),
            context.IsColdStart ? "cold start" : context.IsForeground ? "foreground" : "background",
            message.Title,
            message.Route,
            answer.ToString(),
            message.Data));

        return Task.FromResult(answer);
    }

    /// <inheritdoc />
    public async Task OnOpenedAsync(PushMessage message, string? action)
    {
        log.Note("opened", $"route: {message.Route ?? "none"}{(action is null ? "" : $", action: {action}")}");

        // The route is a string the app decides the meaning of; Spine only carries it.
        if (message.Route is "log") await navigation.NavigateToAsync<Pages.LogPage>();
    }
}
