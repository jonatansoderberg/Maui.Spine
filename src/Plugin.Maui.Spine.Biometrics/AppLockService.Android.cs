#if ANDROID
namespace Plugin.Maui.Spine.Biometrics;

internal sealed partial class AppLockService
{
    private bool _recentsHidden;

    /// <remarks>
    /// Only ever turned off by Spine while the app lock is on, so an app that set it itself keeps its choice.
    /// A <see cref="PrivacyShieldAttribute"/> page needs none of this: <c>FLAG_SECURE</c> blanks the thumbnail.
    /// </remarks>
    partial void UpdateRecents(bool hide)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) || hide == _recentsHidden || Platform.CurrentActivity is not { } activity)
            return;

        activity.SetRecentsScreenshotEnabled(!hide);
        _recentsHidden = hide;
    }
}
#endif
