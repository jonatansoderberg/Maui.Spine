using Plugin.Maui.Spine.Biometrics;

namespace MauiSpineSampleApp.Pages.Biometrics;

[NavigableRegion(Title = "PIN"), RequiresUnlock("Show the PIN")]
public partial class PinPage { public PinPage() => InitializeComponent(); }
