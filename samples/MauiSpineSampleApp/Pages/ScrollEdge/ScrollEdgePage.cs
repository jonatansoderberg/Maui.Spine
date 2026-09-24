namespace MauiSpineSampleApp.Pages.ScrollEdge;

// Nothing to set: HeaderBarBackground.Auto gives a list page the scroll edge effect on iOS 26.
// Android and Windows show the stand-in band only when a page asks for ScrollEdge, as this one does.
[NavigableRegion(Title = "Scroll edge", HeaderBarBackground = HeaderBarBackground.ScrollEdge)]
public partial class ScrollEdgePage { public ScrollEdgePage() => InitializeComponent(); }
