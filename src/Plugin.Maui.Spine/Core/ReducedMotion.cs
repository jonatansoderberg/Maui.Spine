#if ANDROID
using Android.Database;
using Android.Provider;
#endif

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Whether the user asked the system for less motion: Reduce Motion on iOS and Mac Catalyst, Remove
/// animations on Android (animator duration scale 0) and Animation effects off on Windows.
/// </summary>
/// <example>
/// <code>
/// ReducedMotion.Changed += OnReducedMotionChanged;
/// …
/// void OnReducedMotionChanged(object? sender, EventArgs e) =&gt; Restart(still: ReducedMotion.IsOn);
/// </code>
/// </example>
public static class ReducedMotion
{
    static readonly WeakEventManager ChangedEvents = new();

    static bool _isOn;
    static bool _read;
    static long _readAt;
    static bool _observing;

    /// <summary>
    /// Whether animations should be still. Read at most once a second, because scroll handlers ask on every
    /// frame and the setting can change while the app runs.
    /// </summary>
    public static bool IsOn
    {
        get
        {
            var now = Environment.TickCount64;
            if (!_read || now - _readAt > 1000)
            {
                _isOn = Read();
                _read = true;
                _readAt = now;
            }

            return _isOn;
        }
    }

    /// <summary>
    /// Raised on the main thread when the setting changes while the app runs, on iOS, Mac Catalyst and
    /// Android. Windows raises nothing, so read <see cref="IsOn"/> again whenever an animation starts.
    /// </summary>
    /// <remarks>
    /// Handlers are held weakly, so a view that never unsubscribes is not kept alive; subscribe with a
    /// method rather than a lambda that captures, which would be collected. On Android the event comes
    /// half a second after the switch: the app's own animator scale follows the setting later, and an
    /// animation restarted at once would still see animations off and freeze.
    /// </remarks>
    public static event EventHandler Changed
    {
        add
        {
            ChangedEvents.AddEventHandler(value);
            Observe();
        }
        remove => ChangedEvents.RemoveEventHandler(value);
    }

    static bool Read()
    {
#if IOS || MACCATALYST
        return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
        // The setting itself, not ValueAnimator.AreAnimatorsEnabled(): the process learns the new scale
        // later than the content observer fires, so the animator's view is stale right then.
        var resolver = Android.App.Application.Context.ContentResolver;
        return resolver is not null && Settings.Global.GetFloat(resolver, Settings.Global.AnimatorDurationScale, 1f) == 0f;
#elif WINDOWS
        return !new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
        return false;
#endif
    }

    static void Observe()
    {
        if (_observing)
            return;

        _observing = true;
#if IOS || MACCATALYST
        Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
            new Foundation.NSString("UIAccessibilityReduceMotionStatusDidChangeNotification"), _ => Raise());
#elif ANDROID
        if (Android.App.Application.Context.ContentResolver is { } resolver
            && Settings.Global.GetUriFor(Settings.Global.AnimatorDurationScale) is { } uri)
        {
            resolver.RegisterContentObserver(uri, false, new ScaleObserver());
        }
#endif
    }

    static void Raise()
    {
#if ANDROID
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(SettleDelay);
            Notify();
        });
#else
        MainThread.BeginInvokeOnMainThread(Notify);
#endif
    }

    static void Notify()
    {
        _read = false;
        ChangedEvents.HandleEvent(null!, EventArgs.Empty, nameof(Changed));
    }

#if ANDROID
    static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);

    // Delivered on the main looper. Qualified call: inside a Java object a bare Notify() binds to
    // java.lang.Object.notify(), which throws without a lock.
    sealed class ScaleObserver() : ContentObserver(new Android.OS.Handler(Android.OS.Looper.MainLooper!))
    {
        public override void OnChange(bool selfChange) => ReducedMotion.Raise();
    }
#endif
}
