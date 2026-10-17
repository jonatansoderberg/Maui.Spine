using Plugin.Maui.Spine.Authentication;

namespace MauiSpineSampleApp.Pages.SignIn;

[NavigableRegion(Title = "Members"), RequiresSignIn]
public partial class MembersPage { public MembersPage() => InitializeComponent(); }
