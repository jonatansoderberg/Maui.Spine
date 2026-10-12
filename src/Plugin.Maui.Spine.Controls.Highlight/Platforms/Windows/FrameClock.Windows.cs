using Microsoft.UI.Xaml.Media;

namespace Plugin.Maui.Spine.Controls;

/// <summary>Calls back once per rendered frame on the UI thread, through <see cref="CompositionTarget.Rendering"/>.</summary>
internal sealed class FrameClock : IDisposable
{
    readonly Action _tick;

    public FrameClock(Action tick)
    {
        _tick = tick;
        CompositionTarget.Rendering += OnRendering;
    }

    void OnRendering(object? sender, object e) => _tick();

    public void Dispose() => CompositionTarget.Rendering -= OnRendering;
}
