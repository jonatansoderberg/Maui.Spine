using Android.App;
using Android.Content.PM;
using Microsoft.Identity.Client;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// MSAL's <see cref="BrowserTabActivity"/> under a fixed Java name, so the build targets can give it the intent filter for
/// <c>msauth://{package}</c> without the app writing Android code.
/// </summary>
[Activity(Name = JavaName, Exported = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class SpineMicrosoftCallbackActivity : BrowserTabActivity
{
    internal const string JavaName = "plugin.maui.spine.authentication.SpineMicrosoftCallbackActivity";
}
