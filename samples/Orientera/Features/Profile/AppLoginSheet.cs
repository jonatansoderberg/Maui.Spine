using Orientera.Services.Eventor;

namespace Orientera.Features.Profile;

/// <summary>
/// Kept beside <see cref="EventorLoginSheet"/> rather than replacing it, so the two ways of
/// logging in can be judged against each other on a real phone.
/// </summary>
[NavigableSheet(
    Title = "Logga in med Eventor-konto",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium],
    InitialDetent = SheetDetent.Medium)]
public partial class AppLoginSheet : INavigableWithResult<EventorWebSession>
{
    public AppLoginSheet() => InitializeComponent();
}
