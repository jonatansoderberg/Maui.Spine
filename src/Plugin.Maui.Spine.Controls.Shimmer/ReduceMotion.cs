#if ANDROID
using Android.Database;
using Android.Provider;
#endif

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The system's Reduce Motion setting (Remove animations on Android, Animation effects off on
/// Windows) and a weak list of overlays told when it changes.
/// </summary>
internal static class ReduceMotion
{
    private static readonly List<WeakReference<SkeletonOverlay>> Listeners = [];
    private static bool _observing;
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);

    public static bool IsEnabled
    {
        get
        {
#if IOS || MACCATALYST
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
            // The setting itself, not ValueAnimator.AreAnimatorsEnabled(): the process learns the new
            // scale later than the content observer fires, so the animator's view is stale right then.
            var resolver = Android.App.Application.Context.ContentResolver;
            return resolver is not null && Settings.Global.GetFloat(resolver, Settings.Global.AnimatorDurationScale, 1f) == 0f;
#elif WINDOWS
            return !new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
            return false;
#endif
        }
    }

    /// <summary>Held weakly: a page that is never unloaded is not kept alive by this list.</summary>
    public static void Listen(SkeletonOverlay overlay)
    {
        Listeners.Add(new WeakReference<SkeletonOverlay>(overlay));
        if (_observing)
            return;

        _observing = true;
#if IOS || MACCATALYST
        Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
            new Foundation.NSString("UIAccessibilityReduceMotionStatusDidChangeNotification"), _ => NotifyListeners());
#elif ANDROID
        if (Android.App.Application.Context.ContentResolver is { } resolver
            && Settings.Global.GetUriFor(Settings.Global.AnimatorDurationScale) is { } uri)
        {
            resolver.RegisterContentObserver(uri, false, new ScaleObserver());
        }
#endif
        // Windows has no change event on the minimum SDK; the setting is read again whenever an
        // overlay (re)starts, which covers navigating back to a page.
    }

    private static void NotifyListeners()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // The setting is observed before the app's own animator scale follows it on Android; a wave
            // restarted at once is committed while MAUI's ticker still sees animations off and freezes.
            await Task.Delay(SettleDelay);

            for (var i = Listeners.Count - 1; i >= 0; i--)
            {
                if (Listeners[i].TryGetTarget(out var overlay))
                    overlay.OnReduceMotionChanged();
                else
                    Listeners.RemoveAt(i);
            }
        });
    }

#if ANDROID
    // Delivered on the main looper, where the overlays live. Qualified call: inside a Java object a bare
    // Notify() binds to java.lang.Object.notify(), which throws (and took the app down) without a lock.
    private sealed class ScaleObserver() : ContentObserver(new Android.OS.Handler(Android.OS.Looper.MainLooper!))
    {
        public override void OnChange(bool selfChange) => ReduceMotion.NotifyListeners();
    }
#endif
}
