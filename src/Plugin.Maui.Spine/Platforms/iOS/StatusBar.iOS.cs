using Foundation;
using UIKit;

namespace Plugin.Maui.Spine.Core;

internal static partial class StatusBar
{
    private static bool? _appControlled;
    private static bool _warned;

    static partial void ApplyPlatform(StatusBarStyle style)
    {
        // UIKit only lets the application set the style when Info.plist turns view-controller-based
        // appearance off; MAUI's page controllers do not expose the per-controller override.
        _appControlled ??= NSBundle.MainBundle.ObjectForInfoDictionary("UIViewControllerBasedStatusBarAppearance") is NSNumber flag && !flag.BoolValue;

        if (_appControlled != true)
        {
            if (!_warned && style != StatusBarStyle.Default)
            {
                _warned = true;
                System.Diagnostics.Debug.WriteLine("[Spine] StatusBarStyle needs <key>UIViewControllerBasedStatusBarAppearance</key><false/> in Info.plist to take effect on iOS.");
            }

            return;
        }

        var uiStyle = style switch
        {
            StatusBarStyle.LightContent => UIStatusBarStyle.LightContent,
            StatusBarStyle.DarkContent => UIStatusBarStyle.DarkContent,
            _ => UIStatusBarStyle.Default,
        };

#pragma warning disable CA1422 // Deprecated, but the only application-level API; guarded by the plist flag above.
        UIApplication.SharedApplication.SetStatusBarStyle(uiStyle, animated: true);
#pragma warning restore CA1422
    }
}
