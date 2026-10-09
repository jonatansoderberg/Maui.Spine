# Issue #524 — Avatar Lab: load, render and drive the reference avatars

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/524
**Branch:** issue/524-avatar-lab-load-render-and-drive-the-reference-ava
**Status:** In Progress

## Plan

Phase A of [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), with the owner's decisions of 2026-10-09: 3D through `HybridWebView` and local three.js (D1a), folders in one app (D2), cloud TTS with timed visemes later (D3), the reference avatars, fixtures and the three.js files the lab loads go into the repo (D4).

1. `samples/SpineAvatarLab`, a plain MAUI app (SkiaSharp, `HybridWebView`), with folders that mirror the future packages:
   - `Avatar.Core/` — no MAUI, no Skia: manifest and scene types (System.Text.Json source generation), the safe `.spineavatar` reader (spec §13 limits, SHA-256), semantic validation, path grammar, the deterministic frame scheduler (`AvatarScheduler` → `AvatarRenderFrame`), the spec's proposed contracts.
   - `Avatar.Skia/` — the `spine2d` renderer: scene compiled once, per-instance node state, path interpolation into a reused `SKPath`, theme slots, fit and safe inset. No MAUI.
   - `Avatar.View/` — `AvatarView`: frame loop as in `MeshBackground` (paused off screen, in the background, capped rate), Reduce Motion, theme, load generations.
   - `Avatar.ThreeD/` — Pebble Bot in a `HybridWebView` with three.js bundled under `Resources/Raw/wwwroot`; the C# scheduler's frame is sent to the page each tick.
   - `Avatar.Feeds/` — manual levels and visemes, the fixture WAV with timed cues through a small PCM player that reports a playback clock (iOS/Mac `AVAudioEngine`, Android `AudioTrack`), platform TTS as `EstimatedText`.
   - `Lab/` — the inspector of spec §23, diagnostics, snapshot sheets and `measured-result.json`.
2. `tests/Plugin.Maui.Spine.Controls.Avatar.Core.Tests` (net10.0) compiles `Avatar.Core/**` and `Avatar.Skia/**` by link, so a MAUI type in either fails the build. Loader limits, validation of the three avatars and broken copies, scheduler determinism, generations, PP closed under every expression, offscreen sheets.
3. Verify on the iOS simulator and Mac Catalyst first, then the iPhone and Android in Release; record what was not measured as NotMeasured.

## Open Questions

- The iPhone needs a development profile for `se.cosmomedia.spineavatarlab` (a new App ID in the team account).
- Not done yet from the plan: the microphone feed (input level and bands from the mic). Manual input levels and the band generator cover Listening until then.

## Changes

- `samples/SpineAvatarLab`: a plain MAUI app with the three reference avatars, the WAV/cue fixture and three.js 0.180.0 (MIT, licence beside it) under `Resources/Raw`. Added to `Spine.slnx`.
- `Avatar.Core/`: manifest, binding and `spine2d` scene types (System.Text.Json source generation, required parameters and nullability enforced); `AvatarArchive` (spec §13 limits, names checked before any decompression, bounded reads that ignore the ZIP header's sizes, SHA-256 and size per file, unlisted files fail); `AvatarValidator` (references, ids, parent order, path grammar and topology, keyframe order, loop jumps, mask conformance, theme bindings, GLB node/mesh/primitive/morph-target indices); `AvatarPathData` (M/L/C/Q/Z only); `AvatarScheduler` → `AvatarRenderFrame` (activity and expression crossfades, idle variants at loop boundaries, gestures, seeded blink with double blinks, gaze saccades, level smoothing with staleness, viseme cues by generation with 35 ms lead-in and the manifest's release, PP priority, underrun and pause handling, interrupt fade); `AvatarAudioAnalysis` (level and 24 bands per 10 ms hop, profile `spine-lab-v1`); `AvatarTextVisemes` (letter estimate for EstimatedText); the spec's proposed contracts unchanged.
- `Avatar.Skia/`: `Spine2dModel` (compiled once, shared) and `Spine2dRenderer` (per view, evaluated from the baseline every frame, no allocation in `Evaluate` + `Draw`); `AvatarSheet` renders state, expression, viseme, combination and size sheets.
- `Avatar.View/`: `AvatarView` with the spec's bindable surface (Source, State, IsMuted, Expression, ExpressionIntensity, MotionMode, IsAnimationEnabled, MaxFramesPerSecond, AccentColor, AccessibilityText), load generations that keep the old avatar until the new one is ready, `MirrorOf` for previews at other sizes, the `MeshBackground` frame loop (paused off screen and in the background), Reduce Motion, load and first-frame timing.
- `Avatar.ThreeD/`: Pebble Bot in a transparent `HybridWebView` with local three.js; the C# scheduler's frame goes to the page as one JSON message per tick; clips are made additive so idle, gestures and poses add up as in Skia.
- `Avatar.Feeds/`: `AvatarPcmPlayer` with a playback clock (iOS/Mac `AVAudioPlayerNode` render time minus output latency; Android `AudioTrack.getTimestamp`, presentation time); `AvatarFixtureFeed` streams cues 1.5 s ahead and levels from the analysis at the playback position; `AvatarTextSpeechFeed` speaks with MAUI `TextToSpeech` and an estimated mouth.
- `Lab/`: the §23 inspector, a diagnostics overlay, sheet export, `measured-result.json`, a 100-load stress test, and a Debug-only harness (stdin on Mac Catalyst, `harness.txt` on iOS/Android) for screenshots and measurements without taps.
- `tests/Plugin.Maui.Spine.Controls.Avatar.Core.Tests`: 64 tests; Core and Skia compiled by link, so a MAUI type in either fails the build.

## Decisions

- The lab is a plain MAUI app, not a Spine app: it is a test harness, and the avatar code must not lean on Spine's shell. Phase B decides whether `AvatarView` uses `SpineTheme`.
- Layer composition, which the authoring profile leaves open: a pose moves a property toward its target by its weight (poses in one layer crossfade from the layer's starting values); a clip or level parameter is relative to the baseline (offset for x, y, rotation; factor for scale and opacity, override when the baseline is 0). Without this, Dotling's blink clip (absolute `scaleY` 1 → 0.04) overwrote Happy's 0.72 eyes instead of closing them.
- The binding's `channelMasks` decide what a layer may write, not the manifest expression's `channels`: Dotling's manifest names `mouthCorners`, which no node has, while its expression poses write mouth, cheeks and body.
- The microphone-muted flag applies the `muted` pose masked to the `muteIndicator` channel, so it can show during speech without the rest of the muted state.
- Gestures play at full weight; their clips start and end at the baseline, so no fade is needed.
- Speech damping of expression mouth writes starts at once with speech activity and eases off over 60 ms after it.
- The 3D bridge allocates per frame (a JSON string to `SendRawMessage`); accepted for the lab, measured in the overlay.

## Findings (for the spec, schemas and models)

- Binding files have no schema, and the bundle uses fields outside the authoring profile: `stateAnimations` (both renderers); `gaze`, `blink`, `muteBadge`, `headNode`, `eyesNode`, `mouthNode`, `speechMouthMesh`, `expressionMouthTargets`, `defaultTransforms` and framing `cameraPosition`, `lookAt`, `verticalFov` (GLB). The validator lists them as warnings.
- Dotling claims `gaze` with `gazeOwner: scheduler` but has no gaze binding; gaze comes only from poses.
- Dotling's visemes and expressions both write the whole mouth path, so expressions cannot shape mouth corners while speaking; only damping is possible.
- Voice Totem's ring is an accent disc with a `surface`-filled disc on top, so the avatar is not transparent where the spec asks for it; an even-odd ring path would be.
- Voice Totem's `think` loop turns a full 2π; the validator first flagged it as a loop jump and now compares rotations modulo a turn.
- Clip composition (relative vs absolute), the micMuted mask and how a pathPose track crossfades between topologies need to be written into the authoring profile.

## Measurements (Debug unless noted; none on a physical device yet)

| Where | Result |
| --- | --- |
| Mac Catalyst (Debug) | Dotling: render p50 0.19 ms, p95 0.26–0.32 ms, 0 B allocated per frame in steady state (peaks 128–296 B while levels change), load 509–520 ms, first frame 581–631 ms. Pebble Bot (three.js): bridge p50 0.34 ms, 3–24 KB allocated per frame, first frame 433–724 ms. 100 loads ×2: managed heap back to 14.7 MB before each run, working set 286 → 296 MiB on the second run. |
| Android emulator Pixel 10 Pro (Release) | render p50 0.63 ms, p95 2.89 ms, 0 B per frame, load 1108 ms, first frame 1760 ms; but 15–19 fps whatever the avatar size: `gfxinfo` shows 65 ms median frames and a 4.95 s GPU 90th percentile, so the emulator's compositor is the limit. Not a result. |
| iOS simulator, iPhone 17 Pro, iOS 26.4 runtime (Debug) | Dotling: 57 fps at a 60 cap, render p50 0.15 ms, p95 0.38 ms, 0 B per frame in steady state, load 700 ms, first frame 797 ms; the fixture plays as TimedVisemes with 10 ms reported output latency. Pebble Bot: 55 fps, bridge p50 0.34 ms, ≈3.4 KB per frame, first frame 2.1 s. The 26.2 SDK builds against the 26.4 runtime through `xcrun simctl runtime match set iphoneos26.2 23E244` (owner's choice, 2026-10-09). |
| iPhone, Android phone | NotMeasured. |
