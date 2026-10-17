# Issue #588 — Spine.Controls.Avatar: Core and AvatarView packages

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/588
**Branch:** issue/588-spinecontrolsavatar-core-and-avatarview-packages (B1.1), then a second branch for B1.2
**Status:** In Progress
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), steps B1.1–B1.2.

## Plan

Two PRs, each leaving master green.

- **B1.1** `Plugin.Maui.Spine.Controls.Avatar.Core` (net10.0): `git mv` of the lab's `Avatar.Core/**` (with `Gltf/` and `Schemas/`) into `src/`, namespace `Plugin.Maui.Spine.Controls.Avatar` (D8), the schemas embedded under their existing resource names. The Core tests and the lab use a project reference; the tests keep compiling the lab's `Avatar.Skia` (it moves into the Avatar package in B1.2). In `Spine.slnx` and `Spine.Packages.slnf`; icon `avatar.png` (and `speech.png` for B2), README, `packages.md`, a first `avatars.md`.
- **B1.2** `Plugin.Maui.Spine.Controls.Avatar` (MAUI): see below once it starts.

## Changes

### B1.1
- `src/Plugin.Maui.Spine.Controls.Avatar.Core/`: the moved sources, `Plugin.Maui.Spine.Controls.Avatar.Core.csproj`, `README.md`. Namespace `Plugin.Maui.Spine.Controls.Avatar` (was `….Avatar.Core`); the lab, the tests and the Gltf folder follow.
- The public surface narrowed to the format, the timeline and the drivers; renderer internals are `internal` with `InternalsVisibleTo` for the Avatar packages, the tests and the lab: the spine2d scene model, `AvatarPathData`, `AvatarShaderContract`, the glTF model and rig, `AvatarSecondaryMotion`, `AvatarLevelMeter`, `AvatarAudioAnalysis`, `AvatarJson` and the cue fixture, `AvatarValidator`, `AvatarGlb`, `AvatarPackage.GetScene`/`GetGlb`.
- XML docs on every public member; the package builds with no warnings.
- `SpineAvatarLab.csproj` and the Core tests reference the package project; duplicate usings in the lab removed; `AvatarPcmPlayer.Play` internal (its parameter type is).
- `assets/icons/avatar.png`, `speech.png` and their 1254 px originals in `assets/logo-src/` (PIL, from the Chat original).
- `docs/wiki/packages.md` (row and graph), `docs/wiki/avatars.md` (first version).

## Decisions

- **Renderer internals are internal.** A public renderer contract (S2's `o.Renderers.Add(...)`) needs `AvatarRenderFrame`, which stays public; the glTF model and rig, the spine2d scene and the shader contract are implementation details of Spine's own renderers, reachable from Spine's packages through `InternalsVisibleTo`. Exposing them would freeze them as API.
- **The schema resource names keep `….Avatar.Core.Schemas.*`**: they follow the assembly's name, not the namespace, and are public constants already.
- **The lab references the package now** (B1.1's done-criterion) and keeps its own `Avatar.Skia`, `Avatar.View` and SceneKit surface until B1.3, after #542.

## Verification

- `dotnet build Spine.Packages.slnf`: exit 0; Core with no warnings.
- Core tests: 159 pass on the project reference.
- The lab builds for Mac Catalyst against the package (0 warnings, 0 errors); not run.
- `dotnet pack`: the package has the DLL, the XML docs, `README.md` and `avatar.png`.
