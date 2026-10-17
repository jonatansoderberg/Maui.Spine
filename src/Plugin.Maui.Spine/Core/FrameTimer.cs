#if IOS || MACCATALYST
using CoreAnimation;
using Foundation;
#elif ANDROID
using Android.Views;
#endif

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// A timer that ticks on the display's frames, at most once per <see cref="Interval"/>: <c>CADisplayLink</c> on iOS and
/// Mac Catalyst, <c>Choreographer</c> on Android, <c>CompositionTarget.Rendering</c> on Windows. Used on the UI thread
/// in place of <see cref="IDispatcherTimer"/>, whose loop landed at 52–56 fps at a 60 cap on an iPhone and at 56 on the
/// simulator; a display link holds 60 (spike S4 of docs/plans/spine-avatar.md).
/// </summary>
internal sealed class FrameTimer
{
    // Frame timestamps are vsync-aligned; a frame this early still counts as on time.
    private const double Tolerance = 0.002;

    private double _due;

    /// <summary>The shortest time between two ticks; the display's own rate when that is longer.</summary>
    public TimeSpan Interval
    {
        get;
        set
        {
            field = value;
#if IOS || MACCATALYST
            Prefer();
#endif
        }
    } = TimeSpan.FromSeconds(1.0 / 60);

    public bool IsRunning { get; private set; }

    public event EventHandler? Tick;

    public void Start()
    {
        if (IsRunning)
            return;
        IsRunning = true;
        _due = 0;
#if IOS || MACCATALYST
        _link = CADisplayLink.Create(() => OnFrame(_link!.Timestamp));
        Prefer();
        _link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
#elif ANDROID
        _callback ??= new FrameCallback(this);
        Choreographer.Instance!.PostFrameCallback(_callback);
#elif WINDOWS
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
#endif
    }

    public void Stop()
    {
        if (!IsRunning)
            return;
        IsRunning = false;
#if IOS || MACCATALYST
        // The link holds its target until invalidated.
        _link?.Invalidate();
        _link = null;
#elif ANDROID
        Choreographer.Instance!.RemoveFrameCallback(_callback);
#elif WINDOWS
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
#endif
    }

    private void OnFrame(double seconds)
    {
        if (!IsRunning)
            return;

        var interval = Interval.TotalSeconds;
        // The first frame, or one after a stall, ticks at once rather than catching up.
        if (_due == 0 || seconds - _due > interval)
            _due = seconds;
        if (seconds < _due - Tolerance)
            return;
        _due += interval;
        Tick?.Invoke(this, EventArgs.Empty);
    }

#if IOS || MACCATALYST
    private CADisplayLink? _link;

    // The link itself fires at the rate, so the display can run slower for it (ProMotion) instead of waking for frames
    // that are skipped.
    private void Prefer()
    {
        if (_link is null)
            return;
        var rate = (float)Math.Round(1 / Interval.TotalSeconds);
        _link.PreferredFrameRateRange = new CAFrameRateRange { Minimum = rate, Maximum = rate, Preferred = rate };
    }
#elif ANDROID
    private FrameCallback? _callback;

    private sealed class FrameCallback(FrameTimer owner) : Java.Lang.Object, Choreographer.IFrameCallback
    {
        public void DoFrame(long frameTimeNanos)
        {
            if (!owner.IsRunning)
                return;
            Choreographer.Instance!.PostFrameCallback(this);
            owner.OnFrame(frameTimeNanos / 1e9);
        }
    }
#elif WINDOWS
    private void OnRendering(object? sender, object e) =>
        OnFrame(e is Microsoft.UI.Xaml.Media.RenderingEventArgs args
            ? args.RenderingTime.TotalSeconds
            : System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
#endif
}
