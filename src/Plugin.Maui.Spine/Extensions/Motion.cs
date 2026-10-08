using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Gives a view depth that reacts to the device's tilt: <see cref="DepthProperty"/> moves it by up
/// to that many points as the device tilts, so layers with different depths move against each other
/// (parallax). A positive depth floats above the screen, a negative depth sits behind it.
/// iOS uses <c>UIInterpolatingMotionEffect</c>; Android the game rotation vector, through one shared
/// listener that runs only while a view with a depth is on screen and the app is in front. Mac
/// Catalyst and Windows have no tilt, and the view stays where it is.
/// </summary>
/// <remarks>
/// The view moves without being laid out again, on top of its own <c>TranslationX</c> and
/// <c>TranslationY</c>, so give it room: a view that moves 12 points needs 12 points to spare on each
/// side, or a parent that clips it, such as a <see cref="Border"/>. The motion stops under Reduce
/// Motion on iOS and when animations are removed on Android. A soft gradient inside a material
/// <see cref="Border"/> with a depth of its own makes a highlight that sweeps over the surface.
/// <c>UseSpine()</c> sets it up; nothing else to register. <see cref="IsSupported"/> says whether the
/// device can move the views at all, <see cref="IsEnabled"/> whether they move now.
/// </remarks>
/// <example>
/// <code>
/// &lt;Grid&gt;
///     &lt;Image Source="hills.png" Motion.Depth="-8" /&gt;
///     &lt;Image Source="leaf.png" Motion.Depth="12" /&gt;
/// &lt;/Grid&gt;
/// </code>
/// </example>
public static class Motion
{
    /// <summary>
    /// Attached property: how far, in points, the view moves at full tilt. Positive floats above the
    /// screen and moves toward the edge that tilts away from the viewer; negative sits behind it and
    /// moves the other way. 0 (the default) turns the motion off.
    /// </summary>
    public static readonly BindableProperty DepthProperty =
        BindableProperty.CreateAttached(
            "Depth",
            typeof(double),
            typeof(Motion),
            0d,
            propertyChanged: OnDepthChanged);

    /// <summary>
    /// Whether this device can move a view by its tilt: <see langword="true"/> on iOS and iPadOS, on
    /// Android when the device has a game rotation vector or rotation vector sensor, and
    /// <see langword="false"/> on Mac Catalyst, Windows and an iPhone app running on a Mac. Fixed for
    /// the app's lifetime, so an app can hide a setting for the effect where it would do nothing.
    /// Reduce Motion does not change it; see <see cref="IsEnabled"/>.
    /// </summary>
    public static bool IsSupported => _isSupported ??= ReadIsSupported();

    /// <summary>
    /// Whether views with a depth move now: <see cref="IsSupported"/>, and the user has not asked the
    /// system for less motion (Reduce Motion on iOS, Remove animations on Android). Read it when it is
    /// needed rather than keeping it, because the user can change the setting while the app runs.
    /// </summary>
    public static bool IsEnabled => IsSupported && !ReducedMotion.IsOn;

    static bool? _isSupported;

    static bool ReadIsSupported()
    {
#if IOS
        return !Foundation.NSProcessInfo.ProcessInfo.IsiOSApplicationOnMac;
#elif ANDROID
        return MotionState.TiltSensor() is not null;
#else
        return false;
#endif
    }

    static readonly BindableProperty StateProperty =
        BindableProperty.CreateAttached("State", typeof(MotionState), typeof(Motion), null);

    /// <summary>Gets how far <paramref name="view"/> moves at full tilt, in points.</summary>
    public static double GetDepth(BindableObject view) => (double)view.GetValue(DepthProperty);

    /// <summary>Sets how far <paramref name="view"/> moves at full tilt, in points.</summary>
    public static void SetDepth(BindableObject view, double value) => view.SetValue(DepthProperty, value);

    internal static MotionState? GetState(BindableObject view) => (MotionState?)view.GetValue(StateProperty);

    static void OnDepthChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not View view)
            return;

        var state = GetState(view);

        if (newValue is not double depth || depth == 0 || !double.IsFinite(depth))
        {
            if (state is not null)
            {
                state.Dispose();
                view.ClearValue(StateProperty);
            }

            return;
        }

        if (state is null)
            view.SetValue(StateProperty, new MotionState(view));
        else
            state.Refresh();
    }
}

/// <summary>
/// The per-view side of <see cref="Motion"/>: follows the view's handler and owns the platform's
/// motion effect or its place in the shared sensor listener.
/// </summary>
internal sealed partial class MotionState : IDisposable
{
    readonly View _view;
    bool _connected;

    public MotionState(View view)
    {
        _view = view;
        view.HandlerChanging += OnHandlerChanging;
        view.HandlerChanged += OnHandlerChanged;

        if (view.Handler is not null)
            Connect();
    }

    public View View => _view;

    public double Depth => Motion.GetDepth(_view);

    public void Refresh()
    {
        if (_connected)
            UpdatePlatform();
    }

    public void Dispose()
    {
        Disconnect();
        _view.HandlerChanging -= OnHandlerChanging;
        _view.HandlerChanged -= OnHandlerChanged;
    }

    void OnHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        if (e.OldHandler is not null)
            Disconnect();
    }

    void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_view.Handler is not null)
            Connect();
    }

    void Connect()
    {
        if (_connected || _view.Handler is not IViewHandler { PlatformView: { } platformView } handler)
            return;

        _connected = true;

        // The container, when MAUI wraps the view for a shadow or a clip, so the wrapper moves too.
        ConnectPlatform(handler.ContainerView ?? platformView);
        UpdatePlatform();
    }

    void Disconnect()
    {
        if (!_connected)
            return;

        _connected = false;
        DisconnectPlatform();
    }

    partial void ConnectPlatform(object platformView);

    partial void DisconnectPlatform();

    partial void UpdatePlatform();
}
