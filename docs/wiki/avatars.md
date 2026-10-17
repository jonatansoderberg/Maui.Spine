# Avatars

Animated characters and ambient visuals for a voice assistant, text-to-speech, a streamed chat answer, a guide or a status indicator. An avatar is content, a `.spineavatar` file the app bundles or downloads; Spine draws it, drives it and checks it.

> **Status:** in progress, following [the plan](../plans/spine-avatar.md). Core and the view exist; Speech, the voice driver, the presenter and scripts follow. This page grows with them.

## Packages

| Package | What |
| --- | --- |
| `Plugin.Maui.Spine.Controls.Avatar` | `AvatarView`: Skia (vector scenes, SkSL shaders) everywhere, SceneKit (glTF) on iOS and Mac Catalyst, the poster where neither applies |
| `Plugin.Maui.Spine.Controls.Avatar.Core` | The format, the scheduler, the drivers' contract and the text driver, visemes, glTF; no MAUI |

## The view

```xml
<AvatarView Source="{Binding Avatar}" State="Listening" Expression="Curious" HeightRequest="240" />
```

```csharp
avatar.Source = AvatarSource.FromMauiAsset("Avatars/nova.spineavatar");   // or FromFile for a downloaded one
avatar.SetExpression(new(AvatarExpression.Happy, 0.8f, TimeSpan.FromSeconds(3)));
await avatar.PlayGestureAsync("nod");
avatar.LookAt(point);       // a point in the view's coordinates, or null to look back at the viewer
avatar.Interrupt();
```

`UseSpine()` registers the package for an app that references it. The view picks the first representation of the avatar a renderer here can draw: Skia for `spine2d` and `sksl` (on the GPU through Metal on iOS and Mac Catalyst), SceneKit for `glb` on iOS and Mac Catalyst. Where none applies (a glTF avatar on Android and Windows today) it shows the avatar's poster with its state underneath and logs once.

| Property | What |
| --- | --- |
| `Source` | The avatar. A new one cancels the load before it; the avatar on screen stays until the next is ready |
| `Driver` | What drives it; null for the view's own `State`, `IsMuted` and `Expression` |
| `State`, `IsMuted`, `Expression`, `ExpressionIntensity` | The manual state, the microphone-off badge, the base expression. With a driver, `State` follows the driver |
| `MotionMode` | `System` follows Reduce Motion: a still pose, four frames a second, a slow mouth. `Reduced` and `Full` override it |
| `MaxFramesPerSecond` | 15, 30 (default) or 60 |
| `AccentColor` | The colour of the avatar's `accent` slot; unset, the app's accent |
| `AccessibilityText` | What a screen reader says; unset, the avatar's name and state |
| `LoadState`, `LoadError`, `Package`, `RendererName`, `LipSyncQuality`, `Gestures` | What is shown and how |

The view runs no timer while it is detached, hidden (itself or an ancestor), scrolled out of sight or the app is in the background. A screen reader hears one element; muting and an interruption are announced, other state changes are not.

## The format

A `.spineavatar` is a ZIP with a manifest, models, bindings, posters, a licence and a provenance note. Format 1.0 has defaults for what avatars share, so a manifest names only what differs: Nova's is 695 bytes. The [authoring profile](../proposals/spine-avatar.md) describes it in full; the schemas are embedded in Core (`AvatarSchemas`).

Loading never trusts the file: names, sizes and the compression ratio are checked before anything is read, every file must match its SHA-256, nothing is executed or extracted to disk, and every check is in the report.

```csharp
var report = new AvatarValidationReport();
var package = AvatarArchive.TryRead(stream, report);
if (package is null)
    foreach (var failure in report.Failures)
        Console.WriteLine($"{failure.Area}/{failure.Name}: {failure.Detail}");
```

## Driving an avatar

One driver at a time pushes state, levels and visemes into the avatar on each tick (`AvatarDriver`, `AvatarInput`). Every driver states what its mouth timing is worth (`AvatarLipSyncQuality`): `EstimatedText` for text, `AudioReactive` for a level, `TimedVisemes` for a voice with viseme timings.

`TextAvatarDriver` mouths text with no audio, for a streamed chat answer or a caption:

```csharp
var text = new TextAvatarDriver();
text.Begin();               // a reply is coming: Thinking
text.Append("Hello, ");     // Speaking while it mouths
text.Append("I am Nova.");
text.Complete();            // Idle once it is mouthed
```

Visemes are the 15 of `oculus15-v1`; `AvatarVisemes.FromAzure` maps Azure Speech's 22 (Spine.Voice's `Visemes`).
