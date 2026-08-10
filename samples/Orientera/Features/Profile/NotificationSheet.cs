namespace Orientera.Features.Profile;

[NavigableSheet(
    Title = "Notiser",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen],
    InitialDetent = SheetDetent.Medium)]
public partial class NotificationSheet { public NotificationSheet() => InitializeComponent(); }
