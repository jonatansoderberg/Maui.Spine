# Avatars

Animated characters and ambient visuals for a voice assistant, text-to-speech, a streamed chat answer, a guide or a status indicator. An avatar is content, a `.spineavatar` file the app bundles or downloads; Spine draws it, drives it and checks it.

> **Status:** in progress, following [the plan](../plans/spine-avatar.md). `Plugin.Maui.Spine.Controls.Avatar.Core` exists; the view (`Plugin.Maui.Spine.Controls.Avatar`), Speech and the voice driver follow. This page grows with them.

## Packages

| Package | What |
| --- | --- |
| `Plugin.Maui.Spine.Controls.Avatar.Core` | The format, the scheduler, the drivers' contract and the text driver, visemes, glTF; no MAUI |

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
