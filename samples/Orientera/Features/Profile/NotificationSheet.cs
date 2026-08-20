namespace Orientera.Features.Profile;

[NavigableSheet(
    Title = "Notiser",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen],
    InitialDetent = SheetDetent.Medium,
    // The list runs all the way to the sheet's bottom edge; its footer pays for the
    // home indicator, so the last row scrolls clear instead of ending above a dead band.
    // Qualified: MAUI 10 has a SafeAreaEdges of its own, and both are in scope here.
    SafeAreaEdges = Plugin.Maui.Spine.Core.SafeAreaEdges.Top
        | Plugin.Maui.Spine.Core.SafeAreaEdges.Left
        | Plugin.Maui.Spine.Core.SafeAreaEdges.Right)]
public partial class NotificationSheet { public NotificationSheet() => InitializeComponent(); }
