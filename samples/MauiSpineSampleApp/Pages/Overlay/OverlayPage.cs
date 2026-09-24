namespace MauiSpineSampleApp.Pages.Overlay;

// The header floats over the content, white on the photo; the status bar's clock turns light too
// (iOS needs UIViewControllerBasedStatusBarAppearance = false in Info.plist for that part).
[NavigableRegion(Title = "Overlay header",
                 HeaderBar = HeaderBarMode.Overlay,
                 HeaderBarForeground = "#FFFFFF",
                 StatusBarStyle = StatusBarStyle.LightContent,
                 SafeAreaEdges = SafeAreaEdges.Left | SafeAreaEdges.Right)]
public partial class OverlayPage { public OverlayPage() => InitializeComponent(); }
