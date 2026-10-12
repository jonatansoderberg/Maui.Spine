using Plugin.Maui.Spine.Biometrics;

namespace MauiSpineSampleApp.Pages.Biometrics;

[NavigableRegion(Title = "Statement"), PrivacyShield(CoverWhileCaptured = true), RequiresUnlock("Show the statement", Relock = true)]
public partial class StatementPage { public StatementPage() => InitializeComponent(); }
