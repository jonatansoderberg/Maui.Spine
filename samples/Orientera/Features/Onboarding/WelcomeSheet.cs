namespace Orientera.Features.Onboarding;

/// <summary>
/// The first thing the app says. Full screen, because it is the only thing on it.
/// </summary>
/// <remarks>
/// It exists because the login now answers a question the app used to ask with a form: who are
/// you. Demo data stops being where a new user lands by accident and becomes something they
/// choose by skipping this (#123, #75).
/// </remarks>
[NavigableSheet(
    Title = "Välkommen",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.FullScreen],
    InitialDetent = SheetDetent.FullScreen)]
public partial class WelcomeSheet : INavigableWithResult<WelcomeChoice>
{
    public WelcomeSheet() => InitializeComponent();
}
