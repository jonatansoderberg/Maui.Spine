using Android.Content;
using Android.Hardware;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Handlers;
using Plugin.Maui.Spine.Core;
using AView = Android.Views.View;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class MotionState
{
    // One listener for every view: registered while a view with a depth is attached to a window,
    // the app is in front and animations are not removed.
    static readonly List<MotionState> OnScreen = [];
    static readonly TiltTracker Tracker = new();
    static readonly float[] Rotation = new float[9];
    static TiltListener? _listener;
    static bool _stopped;

    AView? _host;
    AttachListener? _attach;

    static MotionState() => Platform.ActivityStateChanged += OnActivityStateChanged;

    /// <summary>
    /// MAUI sets the platform translation from <c>TranslationX</c>/<c>Y</c>; the tilt is added on top of
    /// it. Appended at startup: a mapper that has already run caches its actions, so a later append is
    /// never called.
    /// </summary>
    internal static void ConfigureMapper()
    {
        ViewHandler.ViewMapper.AppendToMapping(nameof(IView.TranslationX), Remap);
        ViewHandler.ViewMapper.AppendToMapping(nameof(IView.TranslationY), Remap);
    }

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not AView host)
            return;

        _host = host;
        _attach = new AttachListener(this);
        host.AddOnAttachStateChangeListener(_attach);

        if (host.IsAttachedToWindow)
            Join();
    }

    partial void DisconnectPlatform()
    {
        Leave();

        if (_host is { } host && _attach is not null)
            host.RemoveOnAttachStateChangeListener(_attach);

        _attach?.Dispose();
        _attach = null;
        Apply(0, 0);
        _host = null;
    }

    partial void UpdatePlatform() => Apply(Tracker.X, Tracker.Y);

    void Join()
    {
        if (!OnScreen.Contains(this))
            OnScreen.Add(this);

        Evaluate();
        Apply(Tracker.X, Tracker.Y);
    }

    void Leave()
    {
        OnScreen.Remove(this);
        Evaluate();
    }

    void Apply(double tiltX, double tiltY)
    {
        if (_host is not { } host || host.Handle == IntPtr.Zero)
            return;

        var density = host.Resources?.DisplayMetrics?.Density ?? 1;
        var depth = Depth;
        host.TranslationX = (float)((View.TranslationX + tiltX * depth) * density);
        host.TranslationY = (float)((View.TranslationY + tiltY * depth) * density);
    }

    static void Remap(IViewHandler handler, IView view)
    {
        if (view is View element && Motion.GetState(element) is { _host: not null } state)
            state.Apply(Tracker.X, Tracker.Y);
    }

    static void OnActivityStateChanged(object? sender, ActivityStateChangedEventArgs e)
    {
        if (e.State is not (ActivityState.Started or ActivityState.Resumed or ActivityState.Stopped))
            return;

        // Another activity stopping behind the one in front does not send the app to the background.
        if (e.State == ActivityState.Stopped && e.Activity != Platform.CurrentActivity)
            return;

        _stopped = e.State == ActivityState.Stopped;
        Evaluate();
    }

    static void Evaluate()
    {
        var run = OnScreen.Count > 0 && !_stopped && !ReducedMotion.IsOn;

        if (run && _listener is null)
            Start();
        else if (!run && _listener is not null)
            Stop();
    }

    static void Start()
    {
        if (TiltSensor() is not (var manager, var sensor))
            return;

        Tracker.Reset();
        _listener = new TiltListener(manager);
        // Above SensorDelay's named rates the binding passes a sampling period in microseconds: 60 Hz.
        manager.RegisterListener(_listener, sensor, (SensorDelay)16_667);
    }

    /// <summary>The sensor the tilt is read from, or <see langword="null"/> when the device has neither.</summary>
    internal static (SensorManager Manager, Sensor Sensor)? TiltSensor()
    {
        if (Android.App.Application.Context.GetSystemService(Context.SensorService) is not SensorManager manager)
            return null;

        // The game rotation vector leaves out the compass, which would make the layers drift; the
        // rotation vector is the fallback on devices without a gyroscope.
        return (manager.GetDefaultSensor(SensorType.GameRotationVector) ?? manager.GetDefaultSensor(SensorType.RotationVector)) is { } sensor
            ? (manager, sensor)
            : null;
    }

    static void Stop()
    {
        if (_listener is not { } listener)
            return;

        _listener = null;
        listener.Manager.UnregisterListener(listener);
        listener.Dispose();
        Tracker.Reset();

        foreach (var state in OnScreen.ToArray())
            state.Apply(0, 0);
    }

    static void OnSensorChanged(IList<float> values, long timestamp)
    {
        if (OnScreen.Count == 0)
            return;

        SensorManager.GetRotationMatrixFromVector(Rotation, Vector(values));

        var quarterTurns = (int)(OnScreen[0]._host?.Display?.Rotation ?? Android.Views.SurfaceOrientation.Rotation0);
        Tracker.Update(Rotation, timestamp, quarterTurns);

        foreach (var state in OnScreen)
            state.Apply(Tracker.X, Tracker.Y);
    }

    static readonly float[][] Vectors = [new float[3], new float[4], new float[5]];

    // Three values on older devices (the fourth is derived from them), four or five on newer ones;
    // getRotationMatrixFromVector tells them apart by the array's length.
    static float[] Vector(IList<float> values)
    {
        var vector = Vectors[Math.Min(values.Count, 5) - 3];
        for (var i = 0; i < vector.Length; i++)
            vector[i] = values[i];
        return vector;
    }

    sealed class TiltListener(SensorManager manager) : Java.Lang.Object, ISensorEventListener
    {
        public SensorManager Manager { get; } = manager;

        public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy)
        {
        }

        public void OnSensorChanged(SensorEvent? e)
        {
            if (e?.Values is { Count: >= 3 } values)
                MotionState.OnSensorChanged(values, e.Timestamp);
        }
    }

    sealed class AttachListener(MotionState owner) : Java.Lang.Object, AView.IOnAttachStateChangeListener
    {
        public void OnViewAttachedToWindow(AView attachedView) => owner.Join();

        public void OnViewDetachedFromWindow(AView detachedView) => owner.Leave();
    }
}
