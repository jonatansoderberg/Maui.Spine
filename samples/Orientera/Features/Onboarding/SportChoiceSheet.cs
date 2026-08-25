namespace Orientera.Features.Onboarding;

/// <summary>
/// The second thing the app asks, and the last. Half a screen, because it is one question with
/// six answers — a full screen for that reads as a form.
/// </summary>
[NavigableSheet(
    Title = "Grenar",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium],
    InitialDetent = SheetDetent.Medium)]
public partial class SportChoiceSheet { public SportChoiceSheet() => InitializeComponent(); }
