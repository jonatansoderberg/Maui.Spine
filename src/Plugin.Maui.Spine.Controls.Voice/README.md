# Plugin.Maui.Spine.Controls.Voice

`VoiceView` for Spine.Voice: a voice conversation drawn as it happens. It reads the frame of an `IVoiceSession` (or a bare `IVoiceAudio`) on its own tick and draws it with a named visual: `orb` (the default), duplex `bars`, a `ring` round a button or a picture, or a simple `avatar` whose mouth follows the answer. It stops drawing while it is hidden, scrolled away or the app is in the background, draws four times a second under Reduce Motion, and screen readers hear the conversation's state.

```bash
dotnet add package Plugin.Maui.Spine.Controls.Voice
```

```xml
<VoiceView Session="{Binding Voice}" Kind="orb" HeightRequest="240" />
<VoiceView Session="{Binding Voice}" Kind="bars" InputColor="SteelBlue" OutputColor="Coral" />
```

A visual of your own, drawn with SkiaSharp, or a view of your own (3D, Rive) that gets each frame:

```csharp
builder.UseSpineVoiceView(o =>
{
    o.AddVisual<PulseVisual>("pulse");          // : VoiceVisual, Draw(canvas, bounds, frame, context)
    o.AddVisualView<MascotView>("mascot");      // : VoiceVisualView, OnFrame(frame, context)
});
```

The avatar's mouth follows the level, or the words when the app gives Spine.Voice an `IVisemeSource`. Glow the screen's edge with the answer through Spine.Controls.Highlight:

```csharp
edge = highlights.AttachEdge(window);
edge.Intensity = () => voice.UpdateFrame().OutputLevel;
```

Documentation: [Voice](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/voice.md#drawing-the-conversation).
