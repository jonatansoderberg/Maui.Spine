using Plugin.Maui.Spine.Voice;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A visual that hosts a renderer of its own, such as a 3D scene or a Rive file, instead of drawing on <c>VoiceView</c>'s
/// canvas. <c>VoiceView</c> shows it as its content and calls <see cref="OnFrame"/> on every tick. Register it with
/// <c>AddVisualView&lt;T&gt;(name)</c>.
/// </summary>
public abstract class VoiceVisualView : ContentView
{
    /// <summary>One tick of the conversation. The frame is reused for the next tick: read it here, and copy what you keep.</summary>
    public abstract void OnFrame(VoiceFrame frame, VoiceVisualContext context);
}
