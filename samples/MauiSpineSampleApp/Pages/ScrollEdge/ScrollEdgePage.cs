namespace MauiSpineSampleApp.Pages.ScrollEdge;

// HeaderBarBackground.Auto already gives a list page the scroll edge effect on iOS 26. Android and
// Windows show the stand-in band only when a page asks for a scroll edge value, as this one does.
// The footer changes HeaderBarBackground and HeaderBarMode on the view model while the page is shown.
[NavigableRegion(Title = "Scroll edge", HeaderBarBackground = HeaderBarBackground.ScrollEdge)]
public partial class ScrollEdgePage { public ScrollEdgePage() => InitializeComponent(); }
