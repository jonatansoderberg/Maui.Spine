using Orientera.Domain;

namespace Orientera.Features.Events;

/// <summary>
/// Eventor's own entry form, inside the app.
/// </summary>
/// <remarks>
/// Not <c>Launcher.OpenAsync</c>. Safari has its own cookie jar; the Eventor session the runner
/// logged in for lives in the app's web view store, so an entry page opened externally greeted
/// them with "Du behöver vara inloggad för att anmäla dig till en tävling" — measured. Shown here
/// instead, in the same web view store the login wrote to, the page opens already signed in.
/// </remarks>
[NavigableSheet(
    Title = "Anmälan",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.FullScreen],
    InitialDetent = SheetDetent.FullScreen)]
public partial class EventorEntrySheet : INavigableWithParameter<CompetitionId>
{
    public EventorEntrySheet() => InitializeComponent();
}
