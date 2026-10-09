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
- Round 2 (after the owner found the avatars clip-art-like): `AvatarSecondaryMotion` adds spring-driven squash and stretch on speech onsets, a hop when speaking starts, a squash on interrupt and a head tilt following gaze and thinking, as three numbers in the frame (`Squash`, `Tilt`, `Lift`) that both renderers apply; off under Reduce Motion, independent of frame rate, adjustable 0–2 in the lab.
- The 3D page has a "studio" look (default; "basic" kept for comparison): three.js `RoomEnvironment` reflections at reduced intensity, key and cyan rim lights, Khronos Neutral tone mapping, bloom on emissive parts through a separate composer and a mix pass that keeps the view transparent (the stock bloom pass writes alpha 1 everywhere), a contact shadow, faint reflections on dark glossy materials. three.js 0.180.0 addons for this are bundled under `wwwroot/three/addons`.
- spine2d 1.1 in validator and Skia renderer: linear and radial gradients with theme-slot stops, Gaussian blur (scene units), strokes with caps, blend modes (screen, multiply, plus), easings `easeIn`, `easeOut`, `backOut`. Each must be declared in `requiredFeatures`; shaders rebuild only on theme or accent change, so drawing stays allocation-free.
- `docs/plans/spine-avatar-model-prompt.md`: a round-2 generation prompt for better avatars (pip, nova, aurora) against the concept sheets, using spine2d 1.1 and a modelled, AO-baked, clearcoat GLB.
- The harness can stay in a Release build with `-p:LabHarness=true`, for measurements on the iPhone.
- 70 tests.
- Round 3: the owner's round-2 generation (`pip`, `aurora`, spine2d 1.1, made with the prompt) is in the lab and the tests. Both load and validate unchanged, and our Skia sheets match their reference compositor closely. The validator now accepts theme bindings that point at gradient stops and strokes (`body.fill.radial.stops[0]`, `stem.stroke`) and warns when the named paint uses another slot.
- `robot-expressive.spineavatar`: three.js r180 `RobotExpressive.glb` (CC0, Quaternius; morphs by Don McCurdy), unmodified, packaged by `Tools/package_robot.py` as an ambient avatar: Idle, gestures nod (Yes), shake (No), lean (Wave), thumbsUp, jump, dance; expressions on its Angry/Surprised/Sad morphs plus head-bone deltas; AudioReactive mouth through the Surprised morph; no visemes, blink or gaze, because the model has none. Posters captured from the lab's 3D surface.
- The 3D page: `clipBlend: "override"` for skeletal clips (they replace each other and a gesture fades the idle out over 0.25 s; additive clips on a skinned rig would leave a T-pose), morph targets driven by level parameters, the clip layer sent with each clip.
- Harness `shotview` captures only the avatar view with a transparent background (correct for the 3D web view; a lone SKCanvasView is captured at the wrong scale, so 2D posters come from `AvatarSheet` instead).
- 83 tests.

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
- Skeletal GLB rigs need a declared clip blend (`clipBlend: override`): additive composition, right for node rigs, breaks skinned characters.
- Manifest `motions.gestures` with names beyond nod/shake/lean/interrupt (thumbsUp, jump, dance) are useful and should be allowed by the schema.
- Theme bindings for gradient stops and strokes (`<node>.fill.radial.stops[i]`, `<node>.stroke`) belong in the authoring profile; round 2 used them.

## Measurements (Debug unless noted; none on a physical device yet)

| Where | Result |
| --- | --- |
| Mac Catalyst (Debug) | Dotling: render p50 0.19 ms, p95 0.26–0.32 ms, 0 B allocated per frame in steady state (peaks 128–296 B while levels change), load 509–520 ms, first frame 581–631 ms. Pebble Bot (three.js): bridge p50 0.34 ms, 3–24 KB allocated per frame, first frame 433–724 ms. 100 loads ×2: managed heap back to 14.7 MB before each run, working set 286 → 296 MiB on the second run. |
| Android emulator Pixel 10 Pro (Release) | render p50 0.63 ms, p95 2.89 ms, 0 B per frame, load 1108 ms, first frame 1760 ms; but 15–19 fps whatever the avatar size: `gfxinfo` shows 65 ms median frames and a 4.95 s GPU 90th percentile, so the emulator's compositor is the limit. Not a result. |
| iOS simulator, iPhone 17 Pro, iOS 26.4 runtime (Debug) | Dotling: 57 fps at a 60 cap, render p50 0.15 ms, p95 0.38 ms, 0 B per frame in steady state, load 700 ms, first frame 797 ms; the fixture plays as TimedVisemes with 10 ms reported output latency. Pebble Bot: 55 fps, bridge p50 0.34 ms, ≈3.4 KB per frame, first frame 2.1 s. The 26.2 SDK builds against the 26.4 runtime through `xcrun simctl runtime match set iphoneos26.2 23E244` (owner's choice, 2026-10-09). |
| iPhone 16 Pro, iOS 27.0.1 (Release, harness on) | Dotling: 55.5 fps at a 60 cap, render p50 0.84 ms, p95 1.48 ms, 0 B per frame (peak 128 B), load 283 ms and first frame 364 ms on a cold start (15 ms load when switching back); fixture TimedVisemes with 20 ms reported output latency. Pebble Bot (basic look): 54.6 fps, bridge 0.36 ms, ≈2.2 KB per frame, first frame 231 ms. Voice Totem: 54 fps, 0.84 ms. The 60 fps target is not reached: the `IDispatcherTimer` loop lands at 52–56 fps; a display-linked loop (`CADisplayLink`, `Choreographer`) is the Phase B candidate. The 100-load stress run ended with the app no longer running and the device connection lost (developer disk image not mountable); whether it crashed is unknown. |
| Android phone | NotMeasured. |
