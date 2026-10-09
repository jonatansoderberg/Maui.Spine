# Spine Avatar: implementation plan (lab → packages → NuGet)

**Status:** Phase A started 2026-10-09 in [#524](https://github.com/jonatansoderberg/Maui.Spine/issues/524) ([changelog](../../issues/524-avatar-lab-load-render-and-drive-the-reference-ava.md)), with the decisions in [§6](#6-decisions-before-work-starts).
**Inputs:** *Maui Spine Avatar Control Specification* v1.0 (PDF, Swedish, 2026-10-09; also `spec/Spine-Avatar-Spec.md` in the model bundle) and *Spine-Avatar-Models.zip* (three reference avatars, JSON schemas, `Avatar.Contracts.cs`, `Authoring-Profiles.md`, a browser viewer and fixtures).
**Related:** [spine-voice.md](../proposals/spine-voice.md) decision 7 (avatars are a separate control library the owner specifies), #515, #516, #517, #518, roadmap #505.
**Goal:** First an app that loads, shows, drives and measures the three avatars on real devices. Then rewrite the parts that held up into packages. Then publish them to NuGet through the normal release flow.

## 1. What we start from

### 1.1 The models

| Avatar | Profile | Format | Content (from the files, not measured) |
| --- | --- | --- | --- |
| Dotling | Character | `spine2d` (Skia) | 19 nodes (group, ellipse, roundedRect, path), 18 path poses (15 visemes + rest/smile/frown), 11 clips (idle_a/b, connect, think, blink, nod, shake, lean_in, interrupt, mute, unmute), 31 binding poses, 8 theme slots light/dark |
| Voice Totem | Ambient | `spine2d` (Skia) | 9 nodes (ring, core, three "packets", mute slash), no mouth, no visemes; tests that an avatar without speech targets loads and reacts to levels |
| Pebble Bot | Character | GLB 2.0 | 6 meshes, 8 nodes, 4 materials, 13,426 triangles, no textures, 15 viseme + 8 expression morph targets, 12 clips, node-hierarchy rig (no skin) |

All three are complete `.spineavatar` ZIPs with SHA-256 tables, posters, state/expression/viseme sheets, licence (free use, including commercial) and provenance. Every device result is `NotMeasured`. The GLB passes the Khronos validator with 0 errors and 0 warnings. The fixtures give a 4 s synthetic PCM16 24 kHz WAV and hand-authored canonical viseme cues for timing tests.

### 1.2 Gaps found while reading

These do not block the lab, but each one needs an answer before the format is frozen:

1. **Binding files have no schema.** `avatar.schema.json` and `spine2d.schema.json` exist; `bindings/skia.json` and `bindings/native3d.json` do not have one. They already use fields the authoring profile never defines: `stateAnimations` (both), and `gaze`, `blink`, `muteBadge`, `headNode`, `eyesNode`, `mouthNode`, `speechMouthMesh`, `expressionMouthTargets`, `defaultTransforms` (GLB). The lab reads them as found and records each one; Phase B writes `bindings.schema.json`.
2. **Dotling's expressions write the whole mouth.** `channelMasks.expression` includes `mouthShape`, and the visemes are full-mouth path poses too. The profile says such an expression must be damped while speaking (`speech.expressionMouthScale` = 0.45) and must never open PP. The scheduler must enforce this by channel, not trust the asset.
3. **The browser viewer is a visual reference, not a behavioural one.** `viewer/lab.js` skips state transitions and channel masks, uses `Math.random` and wall-clock time, and hard-codes viseme sequences. Its pictures can be compared against; its logic should not be ported.
4. **The contracts allocate per frame.** `AvatarBands` copies 24 floats on every analysis frame. Fine for the lab; the spec's own target is near-zero allocation after warm-up, and spine-voice.md §5.1 chose a reused `VoiceFrame` class. The two have to meet in the Voice adapter (#516).
5. **Ten packages is many.** The spec lists Core, Avatar, Voice, Tts, Rive, Lottie, Avatar3D, Vrm, Web3D and a CLI tool. Only Core, Avatar, Tts and the tool are P0; the lab shows whether Tts deserves its own package.

## 2. Phase A: the Avatar Lab (proof of concept)

**Target:** about two weeks of work, iPhone and Android first, Mac Catalyst along for free, Windows after.

### 2.1 Shape

One MAUI app, `samples/SpineAvatarLab`, on its own branch. Code lives in folders that already match the future packages, so Phase B is a move rather than a rewrite:

```
samples/SpineAvatarLab/
  Avatar.Core/      net10.0-clean: no MAUI, no Skia (manifest, loader, validator, scheduler, contracts)
  Avatar.Skia/      spine2d renderer on SKCanvas
  Avatar.View/      AvatarView, the frame loop, Reduce Motion, theme
  Avatar.Feeds/     manual, fixture, platform TTS, microphone; a throwaway PCM player with a clock
  Avatar.ThreeD/    Pebble Bot host (see decision D1)
  Lab/              inspector pages
  Resources/Raw/Avatars/  dotling, voice-totem, pebble-bot .spineavatar + fixtures
tests/Plugin.Maui.Spine.Controls.Avatar.Core.Tests/   net10.0, compiles Avatar.Core/** by link
```

The test project linking `Avatar.Core/**` (as `Plugin.Maui.Spine.Controls.Tests` does for `RollingNumber.cs`) is what keeps Core free of MAUI during the lab: a stray MAUI type fails the test build.

### 2.2 Steps

| # | Step | Content | Done when |
| --- | --- | --- | --- |
| A1 | Assets | Copy the three `.spineavatar` files and the fixtures into the lab. The lab can also open any `.spineavatar` from the file picker, so a newly generated avatar is tested without a rebuild. | All three load from the bundle and from Files |
| A2 | Core: load and validate | Manifest types with System.Text.Json source generation. Safe ZIP reader with the spec's limits (§13: 50 MiB compressed, 150 MiB uncompressed, 512 entries, 32 MiB per entry, compression ratio, no absolute paths, `..`, drive prefixes, duplicate or case-colliding names). SHA-256 check against `files`. Semantic checks: every pose, clip and canonical id resolves; keyframes ordered; path topology equal for linear interpolation; finite numbers. Result is a report with Pass, Fail and NotMeasured. | The three avatars pass; hand-broken copies (traversal, bad hash, missing pose, reversed keyframes) fail with a clear message |
| A3 | Core: scheduler | Monotonic clock interface and seeded PRNG. Layers in the spec's order: default pose → state pose → idle clip → gesture → expression → speech → reflex, each writing only the channels its mask allows, conflicts settled per channel. State transitions over `transitionMs` (hard cut for speech closures and Interrupted). Expression requests: intensity clamped 0–1, duration 250–12,000 ms, falls back to the base expression. Blink 2.8–6.5 s with ~9 % double blinks, gaze modes, breathing, from the manifest. Viseme cue buffer keyed by generation: 20–50 ms attack, 50–100 ms release, flush on new generation, mouth to rest in 60–90 ms on interrupt. Output is a renderer-neutral `AvatarRenderFrame` (pose weights, levels, blink, gaze, elapsed). | Unit tests with a fake clock: deterministic output per seed, PP stays closed under every expression, stale generations ignored, out-of-order cues handled, expression expiry, pause/resume |
| A4 | Skia renderer | Compile the scene once: paths to command and float arrays (own M/L/C/Q/Z parser, not a general SVG parser), node tree with stable indices. Per frame: evaluate into preallocated node state from the stored neutral baseline (no drift), interpolate paths into a reused `SKPath`, theme slots light/dark plus `AccentColor`, fit and `safeInset` applied once. | Dotling and Voice Totem match their poster and sheet PNGs by eye; no allocations growing over time |
| A5 | AvatarView | `SKCanvasView` with the frame loop already proven in `MeshBackground`: paused off screen and in the background, `MaxFramesPerSecond` 15/30/60, throttled while idle, still pose under Reduce Motion, `MotionMode`, `IsAnimationEnabled`. `SetExpression`, `PlayGestureAsync`, `State`, `IsMuted`, `Source`, `LoadAsync` with load generations (a new Source cancels the old load and keeps the old avatar until the new one is ready). Semantic status text for screen readers with debounce. | Switch avatar mid-speech without a flash or a leak; 100 switches with flat memory |
| A6 | Feeds | **Manual:** sliders for levels and 24 bands, viseme picker. **Fixture:** WAV plus `timed-cues.json` through a minimal PCM player that reports a playback clock (iOS/Mac: `AVAudioEngine` + `AVAudioPlayerNode` sample time; Android: `AudioTrack.getTimestamp`), giving `TimedVisemes`. **Platform TTS:** MAUI `TextToSpeech` with start/stop only, giving `EstimatedText`. **Microphone:** input level and bands, giving `AudioReactive` on Listening. The player is throwaway: #515 replaces it. | The lab shows the true `LipSyncQuality` for each feed; interrupt mid-vowel closes the mouth within 90 ms |
| A7 | Pebble Bot | Per decision D1. | Pebble Bot shows states, expressions and visemes from the same scheduler frame as Dotling |
| A8 | Inspector | The spec's §23 list: seven state buttons, MicMuted separate, expression with intensity and duration, gestures, input/output sliders, 24-band generator, viseme picker, playback timeline with play, pause, interrupt and flush, side-by-side 64/128/320/512 DIP, light/dark, Reduce Motion override, seed, renderer choice, validation report, poster next to live render. Diagnostics overlay: fps, frame time p50/p95, bytes allocated per frame (`GC.GetAllocatedBytesForCurrentThread`), generation, quality. Export: snapshot sheets (state, expression, viseme) and `measured-result.json` per device. | Every control in §23 present |
| A9 | Measure | Run S1's criteria on the iPhone (iOS 27, Release) and on Android (Release): first frame after warm load < 300 ms, 60 fps on iPhone, ≥ 30 fps on Android at 320 DIP, render p95 < 4 ms, no growing allocations, no speech left after a generation reset. Anything not run on a physical device is reported as NotMeasured. | `measured-result.json` per device committed beside the lab |

### 2.3 Exit criteria for Phase A

- Dotling and Voice Totem run on iPhone and Android in Release with the S1 numbers recorded.
- Pebble Bot runs on at least one platform through the route chosen in D1, or the reason it does not is written down.
- The Core tests cover every unit-test item in the spec's §24.
- A short findings list: what in the spec, schemas and models has to change before the format is frozen (start with §1.2).

## 3. Phase B: rewrite into packages

1. **Issue and ADR.** One issue per package through `/spine-issue`, and an ADR recording the package split, the 3D route, and how the Avatar contract meets `VoiceFrame`.
2. **Projects.** `Plugin.Maui.Spine.Controls.Avatar.Core` (net10.0, no MAUI, no Skia), `Plugin.Maui.Spine.Controls.Avatar` (`AvatarView` + Skia renderer, `UseSpineAvatar(o => o.AddSkia())`), `Plugin.Maui.Spine.Controls.Avatar.Tts` if the lab shows a second TTS implementation worth separating, otherwise inside Avatar until one arrives. Core tests move to a real project reference.
3. **API pass.** Line the public surface up with spec §6 and §7, cut what the lab never used, XML docs, `[Experimental]` on anything that waits for #515/#516.
4. **Format.** Freeze `avatar.schema.json` 1.0, add `bindings.schema.json`, publish the authoring profile in English under `docs/proposals/spine-avatar.md` (the spec itself, translated).
5. **Repo conventions.** `Directory.Packages.props`, `Spine.slnx`, `Spine.Packages.slnf`, `build/*.props` declaring the package to `UseSpine()`, README and package icon, a Showcase gallery page (Dotling only; the lab stays the heavy tool), the `spine-controls` skill and the wiki.
6. **CLI.** `Spine.Avatar.Tool` as a .NET tool: `inspect`, `validate --profile`, `pack`, `render-sheet --renderer skia`, `report`. It reuses Core and the Skia renderer, so it is cheap once they exist.
7. **Later packages.** `.Avatar.Voice` when #516 lands. `.Avatar3D`, `.Rive`, `.Lottie` only after their spikes (S2–S4) pass; VRM and Web3D last.

## 4. Phase C: NuGet

- Same release flow as every other Spine package: a `v*` tag, Trusted Publishing, monorepo version. New package IDs are the first thing to check on the first release, since nuget.org has never seen them.
- Release gates from the spec: iOS Release/AOT and Android Release on physical devices, flat memory over 100 navigations, posters and fallbacks working when a renderer is missing.
- Ship Core and Avatar first; the rest follow when their own gates pass.

## 5. Proposed issues

| Issue | Phase |
| --- | --- |
| Avatar Lab: load, render and drive the reference avatars (Phase A, A1–A9) | A |
| Spike: Pebble Bot 3D route (D1) | A |
| Avatar Core and Skia renderer as packages | B |
| Avatar text-to-speech | B |
| Spine.Avatar.Tool | B |
| Avatar Voice adapter (needs #516) | later |

## 6. Decisions before work starts

Answered by the owner on 2026-10-09: **D1** (a), three.js in `HybridWebView` for the lab, a native spike afterwards; **D2** folders in one app; **D3** wait, cloud TTS with timed visemes comes in Phase B; **D4** as proposed.

- **D1, 3D in the lab.** (a) `HybridWebView` with three.js bundled locally, reusing the model bundle's viewer pieces: Pebble Bot on every platform within days, but this is the spec's P3 route. (b) A native spike now: Filament with its glTF loader on Android and GLTFKit2 into SceneKit on iOS, both needing .NET bindings (not verified that current versions bind cleanly), 5–8 days. (c) Leave 3D out of the lab and run S4 separately. Recommendation: (a) for the lab, (b) as its own spike issue afterwards.
- **D2, lab shape.** Folders in one app (recommended, §2.1) or real projects from the first commit.
- **D3, TTS with real visemes.** Platform TTS only gives `EstimatedText`. Real speech with timed visemes needs a cloud voice that emits them (Azure Speech does; the realtime APIs do not), which by #517 means a server in between. Leave for Phase B, or add a small dev server to the lab?
- **D4, what goes into the repo.** The three `.spineavatar` files (~1.3 MB) and the fixtures, yes. The Python generator, validator and three.js: only if D1 picks (a), and then only the three.js files the lab loads.

## 7. Better assets (2026-10-09)

The round-1 models are procedural (a Python generator: ellipses and scaled spheres, flat colours), and spine2d P0 allowed only solid fills; that, more than the renderers, is why they look like clip-art next to the concept sheets. Round 2 has a generation prompt ([spine-avatar-model-prompt.md](spine-avatar-model-prompt.md)) and spine2d 1.1 (gradients, blur, strokes, blend modes) in the lab.

Ready-made models, researched on 2026-10-09 (licences and availability change; check again before use):

- **three.js `RobotExpressive.glb`** (CC0, Tomás Laulhé / Quaternius, morphs added by Don McCurdy): cute low-poly robot with Idle, Yes (nod), No (shake) and other clips and three face morphs; no visemes or blink, which the runtime could add by code. The best ready-made start. https://github.com/mrdoob/three.js/tree/dev/examples/models/gltf/RobotExpressive
- **Microsoft Rocketbox** (MIT, archived 2026-10-02, still downloadable): realistic humans with 15 visemes, FACS and ARKit shapes, FBX only (FBX2glTF to convert). A full lip-sync test subject, not the target style.
- **TalkingHead sample `mpfb.glb`** (CC0 per its README): semi-realistic MakeHuman/MPFB human with ARKit 52 and the 15 Oculus visemes, all as sparse morph targets, plus 1–4 k textures. In the lab since 2026-10-10 as `mpfb.spineavatar` (textures at 1024 px, 13 MB); a lip-sync test subject rather than the target style.
- Not usable: three.js `facecap.glb` (no licence stated), Ready Player Me (shut down 2026-01-31), Avaturn and MetaPerson (paid, realistic), VRoid (anime style, per-model terms).
- Image-to-3D services (Meshy, Tripo, Hyper3D Rodin) produce textured GLB from a concept image and body rigs, but no facial morph targets; paid tiers give private, commercial output.
- Without Blender: `gltf-transform` (resize, compress, prune; morph target names through a short script), the three.js editor, FBX2glTF. Adding facial blendshapes automatically: Polywink (paid, status unclear) or Reallusion tools; Faceit and KeenTools need Blender, UniVRM needs Unity.
