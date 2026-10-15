using Android.App;
using Android.Content.PM;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// Where a browser sign-in returns to on Android. Its intent filter for the app's scheme is added to the manifest by
/// build\Plugin.Maui.Spine.Authentication.targets from <c>SpineAuthenticationCallbackScheme</c>, so the app writes no
/// Android code; the fixed Java name is what that manifest entry refers to.
/// </summary>
[Activity(Name = JavaName, NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
public sealed class SpineAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
    internal const string JavaName = "plugin.maui.spine.authentication.SpineAuthenticationCallbackActivity";
}
