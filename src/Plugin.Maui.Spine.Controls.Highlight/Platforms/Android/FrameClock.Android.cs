using Android.Views;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Calls back once per display frame on the main thread, through <see cref="Choreographer"/>. Unlike MAUI's
/// ticker, it keeps going when the system's animations are removed.
/// </summary>
internal sealed class FrameClock : Java.Lang.Object, Choreographer.IFrameCallback
{
    readonly Action _tick;
    bool _running = true;

    public FrameClock(Action tick)
    {
        _tick = tick;
        Choreographer.Instance?.PostFrameCallback(this);
    }

    public void DoFrame(long frameTimeNanos)
    {
        if (!_running)
            return;

        _tick();
        if (_running)
            Choreographer.Instance?.PostFrameCallback(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (_running)
        {
            _running = false;
            Choreographer.Instance?.RemoveFrameCallback(this);
        }

        base.Dispose(disposing);
    }
}
