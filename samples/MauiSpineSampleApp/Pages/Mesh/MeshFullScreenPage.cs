namespace MauiSpineSampleApp.Pages.Mesh;

// The mesh fills the whole screen, behind the status bar, the header bar and the home indicator.
[NavigableRegion(Title = "Mesh", HeaderBar = HeaderBarMode.Overlay, HeaderBarBackground = HeaderBarBackground.Transparent,
    SafeAreaEdges = SafeAreaEdges.None)]
public partial class MeshFullScreenPage : INavigableWithParameter<MeshSettings>
{
    public MeshFullScreenPage() => InitializeComponent();
}
