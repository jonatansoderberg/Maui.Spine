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
- **Measure, then change.** Give every round a number before and after: loose components (`hairparts.py`), gaps and head-through-hair (`hairview.py`), scalp/core pixels inside a silhouette (`seethrough.py`), hair pixels round the eyes (`eyecheck.py`), flakes over the skin (`cheekcheck.py`), hairline roughness (`hairline.py`). Three "fixes" in a row that leave the number unchanged mean the cause is somewhere else.
- **An artefact that survives changes to one layer is not in that layer.** Hide the layer (`solo.py hide=…`) or colour-code the materials (`solo.py flat=…`) and look again: a staircase along the hairline survived four hair rounds and was the face grid against the skull; fragments "in the hair" were the envelope's inner shell seen through the eye holes; a bald patch came from a face rule, not the hair.
- **The owner watches the lab window live.** A diagnostic package (hidden or flat materials) in the app bundle, or a half-finished round, is what they see. Render experiments in a burst and end every chain script by copying the last good package back (`cp … Avatar\ Lab.app/Contents/Resources/Avatars/kairo.spineavatar`), then reload (`start.sh`; `load kairo`): a `cp` alone does not change what is shown.
- **Do not turn a rule off to see what happens** on a model the owner is looking at. Every rule in `kairo_scan.py` has a comment saying what it guards against; a rule turned off brings that artefact back (the face-mask drop rule: inner shell through the eyes, slab flakes over the cheek).
- **Edits by script must assert their anchors.** A multi-replacement script that fails on the last anchor writes nothing; check the grep count of the new text before starting a build on it.
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

Lab limits: the camera has `yaw` only (no pitch: views from above come from `hairview.py` offline); `size 1200` gives 2400 px captures; `shotview` is transparent (gaps to the room are alpha 0). A package can be swapped straight into the app bundle (`bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/Avatar Lab.app/Contents/Resources/Avatars/`) without a dotnet build; `zoom.sh`/`earzoom.sh` never return inside a pipe, run them from a nohup script with a done-file. Pose x view grids: `~/.cache/spine-avatar-lab/kairo/tools/matrix-x.sh` (`POSES="yaw,pitch …" VIEWS="…" EXTRA NOBUILD=1`) and the wrappers in `tools/lab-scripts/` (`run-lab.sh PKG NAME [POSES] [VIEWS]`, `run-matrix.sh` = 13 poses x 6 views, `sideclose.sh`/`frontclose.sh` = 1200 px captures, `env-proj.sh`/`proj-build.sh` = rebuild and render a hair round; they name a scratch root `SC=` to edit).

Tests: `dotnet test tests/Plugin.Maui.Spine.Controls.Avatar.Core.Tests` validates every bundled package, renders sheets for skia avatars (`bin/…/sheets/`) and checks rig and shader behaviour.

## 5. Recipes

### Procedural 3D (Nova, plush)
1. Model with SDFs (`superellipsoid`, `ellipsoid`, `smin`/`smax`), sample along rays from a centre (`ray_surface`) on a UV sphere; star-shaped bodies only.
2. Faces of light (Nova): eyes and mouth are bands projected onto the visor; each shape is a morph target (rest pills, blink, happy arch, wide, closed, thinking-asymmetric, look L/R/U/D, mouth shapes for visemes). Keep rest geometry solid — glow layers at different depths read as ghosted eyes; bloom gives the glow.
3. Plush faces: eyes sunk into the surface; gaze as morph targets that re-project each eye onto the body at shifted spots (rotating about a centre sinks them).
4. Parts that animate are their own nodes (ears, connect rings, alert mark); show/hide by scale in poses; mic-off badge as a morph from a point.
5. Clips start and end at rest (they add onto poses). Default framing straight at the camera; turn the body with `gaze.turn`, not a rotated rest pose.

### From the owner's images (Kairo)
Inputs: a large, straight-on front image (neutral, eyes open, mouth closed), a **bald** version of it (same framing), profile/three-quarter/back and expression/blink sheets, and for the hair the owner's generated views (right/left profile, back, back three-quarters, crown; `~/.cache/spine-avatar-lab/kairo/assets-hair/`). Pixal3D runs locally (`~/.cache/spine-avatar-lab/pixal3d`, isolated venv, `pixal3d.cpp` built with Metal using `DEVELOPER_DIR=/Applications/Xcode-26.6.0.app/Contents/Developer` and its `SDKROOT`; Q8 weights: `models/pixal3d-sv` 8.4 GB single-view, `models/pixal3d-mv` the multiview flows with the shared files symlinked).

**Reconstructions** (each pixel-aligned to its own crop; a second run of a same-framed image is offset, fit `BALD_OY` by image matching):
- Haired front: `trellis-cli --bg-only` then `--sv-image cut.png --pixal3d-weights sv --res 1024 --webp off` (≈10 min to 2.5 h) → `kairo-s42.glb`.
- Bald front: the same → `bald/kairo-bald-s42.glb`: **the head**. The head never changes when hair goes on (owner's rule): the face grid shows the bald picture, nothing on it is filled or toned for the hair, and the grid's depth samples the head's faces only (sampled with the hair in, the forehead became a plate drawn to the bangs).
- Hair volume: `tools/mvstage.py OUT front right back left` stages four matted views in one crop with Pixal3D's canonical cameras (front camera at -y, "right (azim 90)" at +x sees her LEFT side with the nose pointing left in the image, back at +y, left at -x, z up, distance 3.119, FOV 20°; mirrored sides put the face on the back of the head), then `trellis-cli --views OUT --models pixal3d-mv --res 1024 --webp off` (≈50 min). Check with `pixal3d/src/tools/silhouette_iou.py GLB OUT --res=256` (IoU 0.9-0.96 per view is good). Fit into the scan's frame with `tools/mvfit.py` by **front silhouette** (scale + x/y; depth by the noses); face ICP put the hair 27 px aside and 11 % small.

**Generator**: `KAIRO_HAIR=proj python Tools/kairo_scan.py <work-dir> <out-dir>` (≈1.5 min; `KAIRO_BALD=1` builds the head alone, `KAIRO_HAIR` unset the old scan-hair route). The parts that matter:
- `compose()`: the bald scan is skull, ears, neck, turtleneck (neck chain with harmonic weights, pivot at the ear canals); the hair source is the fitted multiview mesh's smooth **envelope** from `tools/hairenv.py` (hair faces by texture colour and, near the front, by the photo's hair mask with the face mask excluded; the single-view scan's front strands thickened and added so the hairline follows the photo; voxelised at 2.5 mm, closed 8, marching cubes, Taubin-smoothed, 40 000 faces, front vertices pulled to 4 mm in front of the head; P35 used `res=0.0025 close=8 smooth=15 faces=40000 front_thick=4 pull=0.004`). Loose pieces (`loose_pieces`), hair inside the head's volume and hair 2-60 mm behind the face grid within the face are dropped.
- Texture: `hair_projector()` colours every point from the owner's views through Pixal3D's cameras (a depth buffer per view, weight (normal·camera)^4, each view's luminance matched to the photo's hair), fallback the palette's dark with strands where no view sees; baked by `hair_charts()` into six box charts plus a hairline chart (never a per-triangle atlas: at each triangle's UV jump the sampler takes the coarsest mip level and draws the mesh as grain), RGBA PNG, material MASK at 0.5 and nine tenths emissive like the front. Alpha 0 where a view sees a point squarely and unoccluded over skin (the front view also where the hair hugs the skin within 8 mm): the hairline is the photo's hair mask, the ear sits in a pocket.
- The face: a dense height-field grid over the bald picture (thin-plate spline through trusted depths), eye openings with eyeballs and round irises behind, lids and mouth as their own meshes with chains of morph targets; the face-mask drop rule removes hair faces over the face (keep it on: it costs the bangs' lock's thin end but guards the eyes and the cheek).
- Scalp under the hair is hair-coloured (a ray test against the hair volume plus the photo's envelope; the skull's front under the grid stays skin). `skin_bake`'s stray rule must spare it (it once wiped the whole scalp back to skin: beige between the strands for a day).
- Landmarks (eyes, lids, brows, mouth, jaw, collar) are measured by hand on the source image; re-measure for a new character.

**Verification** for a hair round: `run-lab.sh` grids at head 0,0 x views 0/±60/±90/180, the full matrix (13 poses x 6 views), `frontclose.sh` + `eyecheck.py` (P35: 102 px), `q45` views + `cheekcheck.py`, ear cells (poses 70,35 / -70,-35 / ±50,0 x views ±60/±90), hair-only and head-only variants from `solo.py`. Open as of P35: the bob's side in front of the ear reads ragged from three quarters (the side views show cheek there); the lock's thin end at the parting is chopped by the face-mask rule.

### SkSL shader (Aurora Flow)
Write the shader in `Tools/<name>.sksl` using only contract uniforms; package with a small script (empty poses for every state/expression/viseme). Taper everything to nothing at its ends (nothing clipped), keep idle subtle, let `energy`/bands drive amplitude, avoid rotation on state changes. Skia draws on Metal on Apple (full resolution); on the CPU path the shader renders into a ≤112 px buffer.

### spine2d (Voice Totem, Pip)
Generate with ChatGPT using `docs/plans/spine-avatar-model-prompt.md`, repack with `Tools/repack_avatar.py`. spine2d 1.1 clips are offsets/factors, 1.0 absolute. Audio reactivity via `parameters` (`outputLow/Mid/High` per node). Blur is the expensive part on the CPU path.

## 6. Finish

1. Tests pass; the package loads on both renderers (`renderer native`, `renderer web`) where relevant.
2. Ship it with the `spine-avatar` tool (`src/Plugin.Maui.Spine.Avatar.Tool`, step C1; `dotnet run --project src/Plugin.Maui.Spine.Avatar.Tool -- …` in the repo): `pack <lab file> -o <out> --strip` renders 512 px posters with the Skia renderers (vector, SkSL, glTF without alpha-masked materials), drops `source/` and rewrites the hash table; a model only SceneKit draws (Kairo's hair cards) takes posters captured on Apple with a transparent background (`SCNView.Snapshot()` under Reduce Motion, light and dark) through `--poster-light`/`--poster-dark`. Then `validate <out> --profile shipping` and `render-sheet <out> -o <dir>` to look at every state and expression. What an app bundles or downloads is always the packed file (the Showcase's `Resources/Raw/Avatars`); the lab keeps its source.
3. Changelog entry in `issues/524-…md` (what, why, what failed on the way), commit with explicit paths, push to PR #525.
4. The pose x view matrix in the native renderer, with the numbers from step 0's tools before and after.
5. `Tools/lab/phone.sh` and copy the Mac app to `~/Applications/Avatar Lab.app`; tell the owner what to look at. Ask what they see; do not interpret their screenshots on their behalf. For their own screenshots, a hair-on-grey-head package (`solo.py … flat=<every material but rest>:0.3,0.3,0.3`) in the lab shows the hair alone.
