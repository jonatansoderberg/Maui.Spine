# Plugin.Maui.Spine.Controls.Avatar

`AvatarView` for Spine: animated characters and ambient visuals from `.spineavatar` files, for a voice assistant, text read aloud, a streamed chat answer, a guide or a status indicator.

- **Renderers**: Skia for vector scenes and SkSL shaders on every platform (Metal on iOS and Mac Catalyst), SceneKit for glTF characters on iOS and Mac Catalyst, Skia on the CPU for glTF characters on Android and Windows (no textures; alpha-masked models show their poster). Where the platform has no renderer for any of an avatar's representations, the view shows its poster and its state.
- **Packages**: `.spineavatar` files are checked, packed with rendered posters and stripped of their generator's source by the `spine-avatar` tool (`Plugin.Maui.Spine.Avatar.Tool`).
- **Driving**: the view's own `State`, `IsMuted` and `Expression`, or a driver: `TextAvatarDriver` mouths text with no audio, `SpeechAvatarDriver` reads text aloud, `VoiceAvatarDriver` follows a realtime voice conversation (Spine.Voice): the state, the sound in and out, the mouth.
- **Figures**: a package with options and presets (format 1.1) shows any of its figures: `Preset="red"`, `SetOption("shape", "drop")`, and `BodyColor` for the main colour, over the preset's.
- **Built in**: `bars` and `ring` (`Source="bars"`), a voice's sound in and out: the answer's 24 bands one way, the microphone's the other.
- **Well-behaved**: no timer while detached, hidden, scrolled away or in the background; Reduce Motion gives a still pose; the app's accent and theme; one screen-reader element.

```csharp
builder.UseSpine();   // registers UseSpineAvatar() for an app that references this package
```

```xml
<AvatarView Source="{Binding Avatar}" State="Listening" Expression="Curious" HeightRequest="240" />
```

```csharp
avatar.Source = AvatarSource.FromMauiAsset("Avatars/nova.spineavatar");
var text = new TextAvatarDriver();
avatar.Driver = text;
text.Mouth("Hello, I am Nova.");
await avatar.PlayGestureAsync("nod");
```

A voice conversation drawn as its sound:

```xml
<AvatarView Source="bars" Driver="{Binding Driver}" HeightRequest="200" />
```

```csharp
Driver = new VoiceAvatarDriver(voiceSession);
```

Beyond bars and ring Spine ships no avatar; bundle small ones as `MauiAsset` (under about 2 MB) and download larger ones yourself, then use `AvatarSource.FromFile`. The format is in [the authoring profile](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/proposals/spine-avatar.md); the guide is [Avatars](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/avatars.md).
