namespace MauiSpineSampleApp.Pages.Collapsing;

// The page opens on its own large title; scrolled away, it collapses into the header bar.
[NavigableRegion(Title = "Collapsing header", HeaderBar = HeaderBarMode.CollapseOnScroll)]
public partial class CollapsingPage { public CollapsingPage() => InitializeComponent(); }
