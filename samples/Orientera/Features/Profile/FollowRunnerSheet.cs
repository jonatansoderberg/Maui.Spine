namespace Orientera.Features.Profile;

[NavigableSheet(
    Title = "Följ löpare",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen],
    InitialDetent = SheetDetent.Medium)]
public partial class FollowRunnerSheet { public FollowRunnerSheet() => InitializeComponent(); }
