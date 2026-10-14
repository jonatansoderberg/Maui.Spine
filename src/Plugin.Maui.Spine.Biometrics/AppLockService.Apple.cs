#if IOS
using Foundation;
using Plugin.Maui.Spine.Core;
using UIKit;

namespace Plugin.Maui.Spine.Biometrics;

internal sealed partial class AppLockService
{
    private UIView? _shield;
    private UIView? _captureCover;
    private bool _captureWatched;

    /// <remarks>
    /// Native and synchronous: the snapshot follows right after the app resigns active, and a MAUI view
    /// would wait for a layout pass.
    /// </remarks>
    partial void ShowShield()
    {
        if (_shield is null && Shield() is { } shield)
            _shield = shield;
    }

    partial void HideShield()
    {
        _shield?.RemoveFromSuperview();
        _shield = null;
    }

    partial void UpdateCaptureCover(bool wanted)
    {
        if (!OperatingSystem.IsIOSVersionAtLeast(17) || PlatformWindow() is not { } window)
            return;

        if (wanted && !_captureWatched)
        {
            _captureWatched = true;
            window.RegisterForTraitChanges<UITraitSceneCaptureState>((_, _) => UpdateCaptureCover(SpineScreen.ShownPages.Any(p => AttributesOf(p.Page).Shield is { CoverWhileCaptured: true })));
        }

        var captured = wanted && window.TraitCollection.SceneCaptureState == UISceneCaptureState.Active;

        if (captured && _captureCover is null)
            _captureCover = Shield();
        else if (!captured && _captureCover is not null)
        {
            _captureCover.RemoveFromSuperview();
            _captureCover = null;
        }
    }

    /// <summary>The app's blurred window with its icon in the middle, as the system's own app lock looks.</summary>
    private static UIView? Shield()
    {
        if (PlatformWindow() is not { } window)
            return null;

        var shield = new UIVisualEffectView(UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemMaterial))
        {
            Frame = window.Bounds,
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions,
        };

        if (AppIcon() is { } icon)
        {
            var image = new UIImageView(icon)
            {
                Frame = new CoreGraphics.CGRect(0, 0, 80, 80),
                Center = new CoreGraphics.CGPoint(window.Bounds.Width / 2, window.Bounds.Height / 2),
                AutoresizingMask = UIViewAutoresizing.FlexibleMargins,
                ClipsToBounds = true,
            };
            image.Layer.CornerRadius = 18;
            image.Layer.CornerCurve = CoreAnimation.CACornerCurve.Continuous;
            shield.ContentView.AddSubview(image);
        }

        window.AddSubview(shield);
        return shield;
    }

    private static UIWindow? PlatformWindow() =>
        Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as UIWindow;

    /// <summary>The app icon named in Info.plist, from the asset catalog or the bundle.</summary>
    private static UIImage? AppIcon()
    {
        if (NSBundle.MainBundle.InfoDictionary?["CFBundleIcons"] is not NSDictionary icons
            || icons["CFBundlePrimaryIcon"] is not NSDictionary primary)
            return null;

        if (primary["CFBundleIconName"] is NSString name && UIImage.FromBundle(name) is { } named)
            return named;

        return primary["CFBundleIconFiles"] is NSArray { Count: > 0 } files
            ? UIImage.FromBundle(files.GetItem<NSString>(files.Count - 1))
            : null;
    }
}
#endif
