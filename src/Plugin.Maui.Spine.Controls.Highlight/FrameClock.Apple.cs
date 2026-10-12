#if IOS || MACCATALYST
using CoreAnimation;
using Foundation;

namespace Plugin.Maui.Spine.Controls;

/// <summary>Calls back once per display frame on the main thread, through <see cref="CADisplayLink"/>.</summary>
internal sealed class FrameClock : IDisposable
{
    readonly CADisplayLink _link;

    public FrameClock(Action tick)
    {
        _link = CADisplayLink.Create(tick);
        _link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
    }

    public void Dispose() => _link.Invalidate();
}
#endif
