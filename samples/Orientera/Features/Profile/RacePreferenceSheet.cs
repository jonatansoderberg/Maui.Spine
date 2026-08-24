namespace Orientera.Features.Profile;

/// <summary>
/// What the runner does, and what they would rather do. Saved as it is changed — there is no
/// "spara", because every tap here is already the answer to a question nobody has to confirm.
/// </summary>
[NavigableSheet(
    Title = "Grenar och former",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Expanded, SheetDetent.FullScreen],
    InitialDetent = SheetDetent.Expanded)]
public partial class RacePreferenceSheet { public RacePreferenceSheet() => InitializeComponent(); }
