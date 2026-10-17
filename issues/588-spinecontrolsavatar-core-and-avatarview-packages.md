# Issue #588 — Spine.Controls.Avatar: Core and AvatarView packages

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/588
**Branch:** issue/588-spinecontrolsavatar-core-and-avatarview-packages (B1.1), then a second branch for B1.2
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), steps B1.1–B1.2.

## Plan

Two PRs, each leaving master green.

- **B1.1** `Plugin.Maui.Spine.Controls.Avatar.Core` (net10.0): `git mv` of the lab's `Avatar.Core/**` (with `Gltf/` and `Schemas/`) into `src/`, namespace `Plugin.Maui.Spine.Controls.Avatar` (D8), the schemas embedded under their existing resource names. The Core tests and the lab use a project reference; the tests keep compiling the lab's `Avatar.Skia` (it moves into the Avatar package in B1.2). In `Spine.slnx` and `Spine.Packages.slnf`; icon `avatar.png` (and `speech.png` for B2), README, `packages.md`, a first `avatars.md`.
- **B1.2** `Plugin.Maui.Spine.Controls.Avatar` (MAUI, plus a net10.0 build of the Skia renderer alone for tests): the lab's `Avatar.Skia` copied in as internal types, `AvatarView` rewritten on Core's `AvatarRuntime` with the lab-only API left out, the Skia (Metal on Apple) and SceneKit surfaces, a poster surface, `UseSpineAvatar()` and its `build/*.props`, README, docs. The lab keeps its own copies until B1.3.

## Changes

### B1.1
- `src/Plugin.Maui.Spine.Controls.Avatar.Core/`: the moved sources, `Plugin.Maui.Spine.Controls.Avatar.Core.csproj`, `README.md`. Namespace `Plugin.Maui.Spine.Controls.Avatar` (was `….Avatar.Core`); the lab, the tests and the Gltf folder follow.
- The public surface narrowed to the format, the timeline and the drivers; renderer internals are `internal` with `InternalsVisibleTo` for the Avatar packages, the tests and the lab: the spine2d scene model, `AvatarPathData`, `AvatarShaderContract`, the glTF model and rig, `AvatarSecondaryMotion`, `AvatarLevelMeter`, `AvatarAudioAnalysis`, `AvatarJson` and the cue fixture, `AvatarValidator`, `AvatarGlb`, `AvatarPackage.GetScene`/`GetGlb`.
- XML docs on every public member; the package builds with no warnings.
- `SpineAvatarLab.csproj` and the Core tests reference the package project; duplicate usings in the lab removed; `AvatarPcmPlayer.Play` internal (its parameter type is).
- `assets/icons/avatar.png`, `speech.png` and their 1254 px originals in `assets/logo-src/` (PIL, from the Chat original).
- `docs/wiki/packages.md` (row and graph), `docs/wiki/avatars.md` (first version).

### B1.2
- `src/Plugin.Maui.Spine.Controls.Avatar/`: `AvatarView` (bindables `Source`, `Driver`, `State`, `IsMuted`, `Expression`, `ExpressionIntensity`, `MotionMode`, `MaxFramesPerSecond`, `AccentColor`, `AccessibilityText`; `SetExpression`, `PlayGestureAsync`, `Interrupt`, `LookAt`, `LoadAsync`; `LoadState`, `LoadError`, `Package`, `Representation`, `RendererName`, `LipSyncQuality`, `Gestures`), `AvatarSource`, `AvatarLoadState`, `SpineAvatarOptions` and `UseSpineAvatar`, the experimental renderer door (`AvatarRenderer`, `AvatarSurface`, `SPINEAVATAR001`).
- `Skia/`: the lab's spine2d renderer, shader renderer and sheets, internal; compiled alone on net10.0, and the Core tests now draw with it instead of the lab's copy.
- `Surfaces/`: `SkiaAvatarSurface` (`SKMetalView` on Apple, `SKCanvasView` elsewhere), `SceneKitAvatarSurface` (`RendersContinuously = false` with `SetNeedsDisplay` per tick; only changed transforms and morph weights written), `PosterAvatarSurface`, `NativeViewHost`.
- Core: `AvatarInput.State` reads the scheduler's state, so a state the scheduler moved to by itself (out of Interrupted) is kept across a source change.
- Docs: `avatars.md` (the view), `packages.md`, README.

## Decisions

- **Renderer internals are internal.** A public renderer contract (S2's `o.Renderers.Add(...)`) needs `AvatarRenderFrame`, which stays public; the glTF model and rig, the spine2d scene and the shader contract are implementation details of Spine's own renderers, reachable from Spine's packages through `InternalsVisibleTo`. Exposing them would freeze them as API.
- **The schema resource names keep `….Avatar.Core.Schemas.*`**: they follow the assembly's name, not the namespace, and are public constants already.
- **The lab references the package now** (B1.1's done-criterion) and keeps its own `Avatar.Skia`, `Avatar.View` and SceneKit surface until B1.3, after #542.

- **The view runs on Core's `AvatarRuntime`**: a driver ticks, then the scheduler; `State` follows the runtime's state (driver or scheduler) and, without a driver, sets it.
- **Lab-only API stays out** (`MirrorOf`, `ViewYaw`, `HeadTurn`, `ThreeDLook`, `ThreeDRenderer`, `SkiaOnGpu`, `Stats`, `RendererDetail`, `LoadMilliseconds`, `LastFrame`, `FrameRendered`), and so do `Seed`, `SecondaryMotion` and `IsAnimationEnabled`, which §5 does not list; `MotionMode` covers stillness. The surface's frame timing is internal, for the lab's inspector in B1.3.
- **GPU on Apple always** (the lab's `SkiaOnGpu` switch is gone): the CPU path drew a full-size shader at 8–9 fps on the iPhone.
- **The renderer door (`AvatarRenderer`, `SpineAvatarOptions.Renderers`) is `[Experimental("SPINEAVATAR001")]`** until S2 settles 3D on Android and Windows; app renderers are asked before the built-in ones, per representation in the manifest's order.
- **`MaxFramesPerSecond` defaults to 30** (the lab's 60): 30 is the plan's floor and halves the cost for a status avatar; a page asks for 60.
- **Unset `AccentColor` is the app's accent** (`SpineTheme.GetAccent`), as §4.8 says; an app without one keeps the avatar's own.
- **Announcements:** muting and Interrupted only. "End" belongs to a hosting VoiceView (session over), not to each utterance.
- **The poster surface** shows the theme's poster with the state as text and logs once per avatar id.
- **Hosting (D4)** is prepared: `IsHosted` and `TickFromHost` are internal, for Avatar.Voice in B3.
- **Naming note for B3:** an attached property class named `Avatar` in `Plugin.Maui.Spine.Controls` (D4's `avatar:Avatar.Source`) would clash with the namespace `Plugin.Maui.Spine.Controls.Avatar`; B3 needs another name.

## Verification

- `dotnet build Spine.Packages.slnf`: exit 0; Core with no warnings.
- Core tests: 159 pass on the project reference.
- The lab builds for Mac Catalyst against the package (0 warnings, 0 errors); not run.
- `dotnet pack`: the package has the DLL, the XML docs, `README.md` and `avatar.png`.

### B1.2 (scratch Spine app, iPhone 17 Pro simulator, iOS 26.4 runtime, Debug)

| Check | Result |
| --- | --- |
| Nova | SceneKit, 28.5 fps at the 30 cap, 0.25 ms p50 / 0.51 ms p95 on our side, 96 B per frame |
| Pip, Dotling, Voice Totem | Skia on Metal; Dotling 27–29 fps, 0.10–0.17 ms p50, 0 B per frame; Voice Totem 28.7 fps, 0.09 ms; Pip 0.58 ms p50, 7.7 ms p95 in its first 1.5 s (blur; S5) |
| 100 source switches ×5 (Nova, Pip, Dotling, Voice Totem in turn) | Managed heap 13.7–13.8 MB after each run; process RSS 331, 339, 343, 338, 328 MB after runs 2–6: flat |
| Reduce Motion (`MotionMode=Reduced`) | 4.2 fps; two screenshots 1.5 s apart pixel-identical: the still pose |
| Text driver | `Mouth(...)`: Speaking with EstimatedText, then Idle |
| Screen reader text | "Nova, idle", "Dotling, speaking" |

NotMeasured: Android emulator and phone (the package compiles for Android; B1.4 runs the Showcase there), Windows, Mac Catalyst run, the owner's iPhone (B1.5).
