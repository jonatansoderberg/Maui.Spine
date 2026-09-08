using Orientera.Domain;
using Orientera.Features.Events;
using Orientera.Services.Notifications;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Push;

namespace Orientera.Services.Push;

/// <summary>
/// Orientera's side of a received notification: what to show while the app is open, and where to go
/// when it is tapped.
/// </summary>
/// <remarks>
/// This handles local notifications too. The system hands both to the same delegate, and a local
/// one carries no route — which is exactly the "show it normally" case.
/// </remarks>
public sealed class OrienteraPushHandler(INavigationService _navigation, OnScreen _onScreen) : IPushHandler
{
    public Task<PushPresentation> OnReceivedAsync(PushMessage message, PushContext context)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        // Banner over the very page that already shows the news is noise. The page is live; it will
        // pick the results up on its own.
        if (context.IsForeground &&
            PushRoute.Parse(message.Route) is { } route &&
            _onScreen.Competition == route.Competition)
        {
            return Task.FromResult(PushPresentation.None);
        }

        return Task.FromResult(message.Kind == PushKind.Alert
            ? PushPresentation.Banner | PushPresentation.Sound | PushPresentation.List
            : PushPresentation.None);
    }

    public async Task OnOpenedAsync(PushMessage message, string? action)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Every route Orientera sends today is about one competition, and the competition page is
        // where its PM, its live and its results all are. Separate pages would mean separate routes.
        if (PushRoute.Parse(message.Route) is { } route)
            await _navigation.NavigateToAsync<EventDetailsPage, CompetitionId>(route.Competition);
    }
}
