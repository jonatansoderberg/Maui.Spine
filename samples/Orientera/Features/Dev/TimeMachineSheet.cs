namespace Orientera.Features.Dev;

[NavigableSheet(
    Title = "Tidsmaskin",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen],
    InitialDetent = SheetDetent.Medium)]
public partial class TimeMachineSheet { public TimeMachineSheet() => InitializeComponent(); }
