# Plugin.Maui.Spine.Controls.Avatar.Core

The core of Spine's avatars, with no MAUI and no Skia. Most apps reference [`Plugin.Maui.Spine.Controls.Avatar`](https://www.nuget.org/packages/Plugin.Maui.Spine.Controls.Avatar), which brings this package and the view; reference this one alone for a tool, a server or a test that reads or checks avatars.

- **The format.** `AvatarArchive.Read` opens a `.spineavatar` without trusting it (names, sizes, compression ratio and every file's SHA-256 checked before use), applies the defaults of format 1.0 and validates it against the embedded schemas (`AvatarSchemas`) and the semantic rules. `AvatarValidationReport` lists every check with its result.
- **The timeline.** `AvatarScheduler` mixes states, expressions, idle variants, gestures, blinks, gaze, springs and viseme cues on a playback clock into one `AvatarRenderFrame` per tick, deterministically and without allocating per frame.
- **Drivers.** `AvatarDriver`, `AvatarInput` and `AvatarRuntime`: one driver at a time pushes state, levels and visemes on each tick. `TextAvatarDriver` mouths text with no audio (EstimatedText).
- **Visemes.** `AvatarVisemes`: the 15 canonical visemes of `oculus15-v1` and the map from Azure Speech's 22.
- **glTF.** `GltfModel` and `GltfAvatarRig` read a GLB and pose it from a frame, for renderers outside MAUI.

```csharp
var report = new AvatarValidationReport();
var package = AvatarArchive.TryRead(File.OpenRead("nova.spineavatar"), report)
    ?? throw new InvalidOperationException(report.Summary());
var runtime = new AvatarRuntime { Driver = new TextAvatarDriver() };
runtime.Load(new AvatarScheduler(package, package.Manifest.Representations[0]));
```

The format is described in [the authoring profile](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/proposals/spine-avatar.md); the wiki page is [Avatars](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/avatars.md).
