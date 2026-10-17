using Plugin.Maui.Spine.Authentication;

namespace MauiSpineSampleApp.Pages.SignIn;

public partial class MembersPageViewModel(ISpineAuth _auth) : SampleViewModel
{
    public string Greeting => $"Signed in as {_auth.User?.DisplayName ?? _auth.User?.Email ?? _auth.User?.Id}.";
}
