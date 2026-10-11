---
name: spine-avatar
description: Create or change a Spine Avatar (.spineavatar) in the Spine Avatar Lab — 3D characters generated as GLB with morph targets (Nova, Puff), heads reconstructed from the owner's images (Kairo), SkSL shader avatars (Aurora Flow), spine2d vector avatars (Voice Totem), packaged ready-made GLBs (Robot Expressive) — and verify them state by state against a concept sheet, headless on the Mac and on the iPhone. Use when asked for a new avatar, a change to one, a match to a sketch, or a fix to how one looks or moves. Invoke as /spine-avatar.
---

You are making or changing an avatar for the **Spine Avatar control** (spec: `docs/plans/spine-avatar.md`, issue #524, lab PR #525). Avatars are `.spineavatar` packages (zip: `avatar.json` manifest + model + bindings + posters + licence/provenance + the generating source) shown by `AvatarView` in **`samples/SpineAvatarLab`**. Everything about the avatar is data: the runtime's scheduler mixes states, expressions, visemes, blink, gaze and springs into poses; the bindings say what each pose moves.

Read before starting: `issues/524-avatar-lab-load-render-and-drive-the-reference-ava.md` (the running changelog — append to it as you work) and the memory file `avatar-lab.md` if present.

---

## 0. Ground rules learned the hard way

- **Compare against the sketch, never judge from memory.** Every round: render all states, put them next to the concept cells, look, change one thing, repeat. Zoom into the part you are fixing (eyes, mouth, hairline). Twenty rounds is normal for a character.
- **One detail at a time.** When the owner lists problems, fix and verify them one by one, each with a zoomed before/after.
- **When a capture looks wrong, check the capture before the model.** `SCNView.Snapshot()` (`shotview`) shows geometry and morphs but no bloom; a live-window crop (`shotlive`) shows bloom but can return stale morph states. A screenshot once lied about morphs not working.
- **Debug by elimination with colour coding**: retint materials (`rest` red, `under` blue, …) in a copy of the package, render, see which part is where it should not be. Render offline in Python (textured, normals as colours) to separate data bugs from renderer bugs.
- **Make failures visible**: the lab's status line shows `Last load failed: …`; read it (`dump`) before guessing.
- **Docs, changelog, commits in English**; conversation with the owner in Swedish. Stage explicit paths only (never `git add -A`). Work in the worktree, never check out in the main checkout.
- **Never copy third-party characters** (OpenAI's Dots, Clippy …): make your own in that spirit, and say so.

## 1. Where things live

| Path | What |
|---|---|
| `samples/SpineAvatarLab/Avatar.Core/` | MAUI-free core: manifest, validator, scheduler, glTF reader (`Gltf/GltfModel.cs`, sparse accessors, textures) and rig (`Gltf/GltfAvatarRig.cs`), shader contract |
| `Avatar.Skia/` | spine2d renderer, `AvatarShader` (SkSL), sheets |
| `Avatar.ThreeD/` | `SceneKitAvatarSurface` (native 3D on iOS/Mac), `ThreeDAvatarSurface` + `Resources/Raw/wwwroot/avatar3d.js` (three.js in a HybridWebView, Android and fallback) |
| `Avatar.View/` | `AvatarView`, `SkiaAvatarSurface` (Metal on Apple, CPU elsewhere) |
| `Lab/` | the lab page and its harness (`LabHarness.cs`, commands in `LabPage.RunCommandAsync`) |
| `Tools/` | one generator per avatar family (below) and `Tools/lab/` scripts |
| `Resources/Raw/Avatars/*.spineavatar` | bundled packages; register new ones in `LabPage` (`_avatars` list) and in `tests/…/AvatarArchiveTests.ReferenceAvatars` |

Generators write the package straight into `Resources/Raw/Avatars` and embed their own source in it. Regenerate, never hand-edit a package.

## 2. Pick the approach

| Want | Approach | Example | Tool |
|---|---|---|---|
| A stylised 3D character from a sketch (robot, mascot, plush, house) | Procedural GLB: SDF shapes sampled to meshes, morph targets for the face, clips for motion | **Nova**, **Puff/Mochi/Sprig/Bean/Home** | `Tools/nova.py`, `Tools/plush_avatars.py` |
| A specific human/character that must look exactly like images | Reconstruct the head from the owner's front image with Pixal3D, then rig it | **Kairo** | `Tools/kairo_scan.py` |
| Light, waves, an orb, anything that flows | One SkSL shader (`format: "sksl"`) fed from the uniform contract | **Aurora Flow** | `Tools/aurora_flow.py` + `.sksl` |
| A flat, crisp 2D look | spine2d vector scene (nodes, gradients, poses, clips) | **Voice Totem**, Pip | ChatGPT prompt in `docs/plans/spine-avatar-model-prompt.md`, `Tools/repack_avatar.py` |
| A ready-made GLB (CC0) | Package it; map its morphs/clips to poses | **Robot Expressive**, MPFB | `Tools/package_robot.py`, `Tools/package_mpfb.py` |

Procedural faces from parameters end up looking like Nintendo Miis (the owner said so of `face_styles.py`); for a real character use images (Kairo) or a hand-made model.

## 3. The package contract (what the bindings can say)

Manifest: `profile` (`character` needs all 7 states and 8 expressions, `ambient` does not), `representations[]` (`renderer` skia|native3d, `format` spine2d|sksl|glb, `capabilities`, owners for idle/blink/gaze), `states` (pose, transition, input/outputReactive, gaze mode), `expressions`, `speech` (canonical 15 visemes, `oculus15-v1`), `motions` (idle variants, gestures, blink interval), `themes.slots` (bindings `material:<i>` for GLB, `uniform:<name>` for SkSL, node paint paths for spine2d), posters.

GLB bindings (`bindings/native3d.json`), besides `poses` (lists of morph writes `{node, mesh, primitives, targetIndex, value}` or transform writes `{node, property, value}`) and `animations`:

| Key | Effect |
|---|---|
| `blink` | target(s) the scheduler drives 0→1→0; `suppressExpressionTargets` |
| `gaze` | `{node, rangeMeters}` slides a node; `{node, headRotation}` turns a bone; `{morphs: {mesh, primitives, left, right, up, down}}` drives look targets (full at ±12°); `turn: {node, factor}` turns a node toward a held look (pointer/tap) |
| `parameters.inputLevel` / `outputLevel` | level → transform or morph weight (`min`/`max`) — Nova's ear light, plush body pulse |
| `stateAnimations` | a looping clip per state (Nova `connect` = full spin, `listen` = ear pulse) |
| `springs: false` | no squash/lift/tilt (humans: the face must stay put) |
| `lighting: "portrait"` | soft front key, no coloured rim (skin) |
| `bloom: {intensity, threshold, radius}` | per-avatar glow (SceneKit threshold is luminance — blue needs high emission) |
| `muteBadge`, `headNode`, `speechMouthMesh`, `expressionMouthTargets` | as named |

SkSL: uniforms are listed in `Avatar.Core/AvatarShaderContract.cs` (time, `flow` phase, levels, `energy`, bands, mouth, `stateX`/`exprX` weights, gaze, look, springs, dark, accent, theme colours). Unknown uniforms fail validation. Animate with `flow`, not `iTime`, so speed changes stay smooth.

## 4. The lab and its harness

Build and run on the Mac (MAUI workloads live in `/usr/local/share/dotnet`):

```bash
cd samples/SpineAvatarLab && export PATH=/usr/local/share/dotnet:$PATH MD_APPLE_SDK_ROOT=/Applications/Xcode-26.2.0.app
dotnet build -f net10.0-maccatalyst && Tools/lab/start.sh
echo "load nova" >> /tmp/spine-avatar-lab/cmds        # results in /tmp/spine-avatar-lab/out (strip NULs)
```

Useful commands: `load <id>`, `state <idle|connecting|listening|thinking|speaking|interrupted|muted>`, `muted on|off`, `expr <name> <intensity> <seconds>`, `viseme <0-14>|off`, `blink <0-1>|off`, `level in|out <0-1>`, `fixture play` (speech with visemes), `gesture <name>`, `theme light|dark`, `renderer native|web`, `skia gpu|cpu`, `size <pt>`, `follow off` (the real mouse steers gaze otherwise), `lookat x y` (after `size`), `shotview <name>`, `shotlive <name>`, `shot <name>` (whole window), `dump`, `reset-peaks`, `bench`.

Scripts:
- `Tools/lab/states.sh <id> <concept.png> <out.png>` — all 7 states, light and dark, under the concept's cells (pass the cell x/y ranges for the sheet's layout).
- `REGION=x0,y0,x1,y1 Tools/lab/zoom.sh <id> <out.png> "cmd;cmd" …` — 640 pt renders, one crop per step (blink sequences, visemes, turns).
- `Tools/lab/phone.sh` — Release build with the harness, installs on the owner's iPhone (retries while it is unreachable; it must be unlocked and on the network). On the phone the harness reads `Library/harness.txt` (`xcrun devicectl device copy to … --domain-type appDataContainer --domain-identifier se.cosmomedia.spineavatarlab`).

Tests: `dotnet test tests/Plugin.Maui.Spine.Controls.Avatar.Core.Tests` validates every bundled package, renders sheets for skia avatars (`bin/…/sheets/`) and checks rig and shader behaviour.

## 5. Recipes

### Procedural 3D (Nova, plush)
1. Model with SDFs (`superellipsoid`, `ellipsoid`, `smin`/`smax`), sample along rays from a centre (`ray_surface`) on a UV sphere; star-shaped bodies only.
2. Faces of light (Nova): eyes and mouth are bands projected onto the visor; each shape is a morph target (rest pills, blink, happy arch, wide, closed, thinking-asymmetric, look L/R/U/D, mouth shapes for visemes). Keep rest geometry solid — glow layers at different depths read as ghosted eyes; bloom gives the glow.
3. Plush faces: eyes sunk into the surface; gaze as morph targets that re-project each eye onto the body at shifted spots (rotating about a centre sinks them).
4. Parts that animate are their own nodes (ears, connect rings, alert mark); show/hide by scale in poses; mic-off badge as a morph from a point.
5. Clips start and end at rest (they add onto poses). Default framing straight at the camera; turn the body with `gaze.turn`, not a rotated rest pose.

### From the owner's images (Kairo)
Inputs: a large, straight-on front image (neutral, eyes open, mouth closed), plus profile/three-quarter/back and expression/blink sheets for comparison. Pixal3D runs locally (`~/.cache/spine-avatar-lab/pixal3d`, isolated venv, `pixal3d.cpp` built with Metal using `DEVELOPER_DIR=/Applications/Xcode-26.6.0.app/Contents/Developer` and its `SDKROOT` — the Command Line Tools' SDK fails to link; Q8 weights 8.4 GB). Run: `trellis-cli --bg-only` (BiRefNet cutout) then `--sv-image cut.png --pixal3d-weights sv --res 1024 --webp off` (≈2.5 h on 24 GB). Then `python Tools/kairo_scan.py <work-dir> <out-dir> [posters]` (≈1 min):
- fit the crop transform (source → generator crop) and camera; weld, turn to +Z, keep the outer surface;
- **visibility with a coarse z-buffer** (point-sampled full-res buffers have holes that let the back of the head through);
- the face is a smooth height field textured with the picture (depth = smoothing thin-plate spline through trusted front depths); the rest gets a per-triangle atlas from the generator's texture; a closed underlayer fills gaps in the hair (not over the face);
- picture-textured materials mostly emissive (light is baked in the picture);
- morphs as image-space displacement fields at each vertex's own depth, sparse accessors; upper lids as separate fold-out surfaces with clean skin and a copied lash line;
- landmarks (eyes, lid margins, brows, mouth, jaw) are measured by hand on the source image — re-measure for a new character.
Planned improvement: reconstruct a hairless head from a bald version of the image and add the hair as its own layer.

### SkSL shader (Aurora Flow)
Write the shader in `Tools/<name>.sksl` using only contract uniforms; package with a small script (empty poses for every state/expression/viseme). Taper everything to nothing at its ends (nothing clipped), keep idle subtle, let `energy`/bands drive amplitude, avoid rotation on state changes. Skia draws on Metal on Apple (full resolution); on the CPU path the shader renders into a ≤112 px buffer.

### spine2d (Voice Totem, Pip)
Generate with ChatGPT using `docs/plans/spine-avatar-model-prompt.md`, repack with `Tools/repack_avatar.py`. spine2d 1.1 clips are offsets/factors, 1.0 absolute. Audio reactivity via `parameters` (`outputLow/Mid/High` per node). Blur is the expensive part on the CPU path.

## 6. Finish

1. Tests pass; the package loads on both renderers (`renderer native`, `renderer web`) where relevant.
2. Posters: `shotview` in light and dark at 320 px, passed to the generator.
3. Changelog entry in `issues/524-…md` (what, why, what failed on the way), commit with explicit paths, push to PR #525.
4. `Tools/lab/phone.sh` and tell the owner what to look at on the phone. Ask what they see; do not interpret their screenshots on their behalf.
