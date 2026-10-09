# Prompt: better reference avatars (round 2)

Paste everything below the line into ChatGPT, once per avatar, with `TARGET` set to one of `pip`, `nova`, `aurora`. Attach: the concept sheets (pages 15–19 of the spec PDF, or the five concept PNGs), `avatar.schema.json`, `spine2d.schema.json`, `Authoring-Profiles.md`, `Avatar.Contracts.cs`, and the round-1 package of the same family (`dotling.spineavatar` for `pip` and `aurora`, `pebble-bot.spineavatar` for `nova`) so it can see what to improve.

---

TARGET = pip   <!-- pip | nova | aurora -->

You made the round-1 reference avatars for the Spine Avatar control (Dotling, Voice Totem, Pebble Bot). They load and animate correctly in the native lab, but they look like clip-art: flat single-colour ellipses, ellipsoid primitives with no modelling, eyes and mouths as plain discs, stiff motion. The concept sheets attached show what the product should look like. This round is about **visual quality**, with the same technical contract.

## Why round 1 looked flat (fix all of these)

1. **Primitives instead of form.** Dotling is 19 flat-filled shapes; Pebble Bot is scaled spheres. Nothing has volume, rim light, highlight, inner shading or contact shadow.
2. **The P0 format allowed only solid fills.** That limit is lifted below (spine2d 1.1).
3. **3D surfaces were untreated:** no smoothing beyond the sphere tessellation, no baked ambient occlusion, no clearcoat, glowing parts only faintly emissive.
4. **Motion was a few pixels of linear drift.** No anticipation, overshoot, squash and stretch or follow-through.

## Target designs (match the attached concept sheets, not round 1)

- **pip** — 2D character (format `spine2d` 1.1, renderer `skia`). The "Pip / Lumi / Pixel Pet" concepts: a soft, glossy blob creature in cyan-to-periwinkle, big expressive eyes with two specular highlights each and a soft iris gradient, small brows, a tiny mouth that can open into clear shapes, rosy blush, a short antenna with a glowing tip, a soft drop shadow under it, and a faint aura that grows when listening or speaking. Reads clearly at 64 px.
- **nova** — stylized 3D robot (format `glb`, renderer `native3d`). The "Nova / Byte / Lumi 3D" concepts: a glossy white ceramic head shell, a dark smoked-glass visor face, glowing cyan eyes and mouth drawn on the visor, a small glowing antenna or halo, a rounded body below the head, soft and toy-like proportions. Should look like a product render, not a CAD primitive.
- **aurora** — ambient abstract (format `spine2d` 1.1, profile `ambient`). The "Aurora / Pulse Bloom" concepts: a breathing orb of layered translucent light, petals or rings in blue, violet and cyan with soft glow, reacting to input and output bands; no face.

All three: neutral and friendly, no text in the artwork, transparent background, distinct light and dark theme slots.

## Format additions you may use: spine2d 1.1

Declare every feature you use in the representation's `requiredFeatures`. The runtime supports exactly these:

| Feature | JSON | Notes |
| --- | --- | --- |
| `gradients` | `"fill": {"linear": {"x0":0,"y0":-80,"x1":0,"y1":80,"stops":[{"offset":0,"slot":"bodyTop"},{"offset":1,"slot":"bodyBottom","opacity":0.9}]}}` and `"fill": {"radial": {"cx":0,"cy":-20,"r":120,"stops":[...]}}` | Coordinates in the node's local space. A stop has `offset` 0–1, `slot` or `color`, optional `opacity`. 2–8 stops. |
| `blur` | `"blur": 6` on a node | Gaussian sigma in scene units, 0–40. For glow, soft shadows and soft highlights. Use sparingly: at most 6 blurred nodes. |
| `strokes` | `"stroke": {"width": 4, "slot": "ink", "cap": "round"}` on a path node | For brows, antenna, smile lines. A stroked open path needs no fill (`"fill"` may be omitted when `stroke` is set). Caps `butt`, `round`, `square`. |
| `blendModes` | `"blend": "screen"` | `normal`, `screen`, `multiply`, `plus`. For glow and light layers. |
| `easing` (no feature flag) | `"easing": "easeOut"` | `linear`, `step`, `easeIn`, `easeOut`, `easeInOut`, `backOut` (overshoot ≈10 %). |

Path data stays the restricted absolute `M L C Q Z` grammar. Path poses used for interpolation must have identical command lists. Theme slots may now hold gradient stop colours (one slot per stop colour, light and dark). Bump `schemaVersion` of the scene to `"1.1"`; the manifest stays `"1.0"`.

## Format notes for nova (GLB 2.0)

- Model smooth, organic forms. Do not ship raw `scale(sphere)` primitives. Build shapes as signed-distance fields with smooth unions (smooth-min) and extract them with marching cubes, or subdivide (Catmull–Clark / Loop) a low-poly cage; then smooth normals, decimate to budget, and check that silhouettes are clean at 64 px.
- Budget: 20–35k triangles total, ≤ 6 materials, textures optional and ≤ 1024².
- Bake ambient occlusion into `COLOR_0` vertex colours (raycast or SDF-based AO); it is the cheapest way to get contact depth without a texture.
- Materials: body `metallic 0`, `roughness ≈ 0.25`, `KHR_materials_clearcoat` (clearcoat 1, roughness 0.1); visor dark glass, `roughness ≈ 0.08`; eyes, mouth and antenna tip emissive with `KHR_materials_emissive_strength` 3–6 so they bloom. List extensions in `extensionsUsed` only, never in `extensionsRequired`.
- Eyes and mouth are shapes on the visor (thin geometry slightly in front of it), with morph targets that change their shape: happy eyes become arcs, surprised eyes grow, the mouth forms the 15 visemes. Brows are optional.
- Keep the round-1 binding conventions the runtime already reads: `poses` with `{node, mesh, primitives, targetIndex, value}` morph writes and `{node, property: rotation|translation|scale, value}` transform writes; `animations`; `parameters.outputLevel` / `inputLevel` with `min`/`max` arrays; `channelMasks`; `stateAnimations`; `framing` with `cameraPosition`, `lookAt`, `verticalFov`, `safeInset`; and the extras `gaze {node, rangeMeters}`, `blink {node, mesh, targetIndex, suppressExpressionTargets}`, `muteBadge {node, mesh, targetIndex}`, `headNode`, `speechMouthMesh`, `expressionMouthTargets`. Record each extra in `source/authoring-notes.md`.

## Motion (all targets)

The runtime composes layers like this; author for it:

- **Poses** (states, expressions, visemes) are absolute targets the runtime blends toward by weight.
- **Clips** (idle, state loops, gestures, blink) are **relative to the rest pose**: offsets for x, y and rotation, factors for scale and opacity. A clip must start and end at the rest values (except looping rotations, which may end a whole turn on).
- The runtime adds spring-based secondary motion (squash and stretch on speech onsets, a hop when speaking starts, a squash on interrupt, head tilt following gaze). Do not bake those into the idle loops; give the character room for them.

Author with the 12 principles in mind:
- Idle loops of 4–7 s with breathing (scale 1.5–3 %), a slight weight shift, and something secondary (antenna sway lagging the body by ~80 ms, aura pulsing on a different period). Two idle variants that differ clearly.
- Gestures with anticipation and overshoot: `nod` dips before rising, `shake` decays, `lean_in` stretches toward the viewer, `interrupt` is a quick flinch (squash 6–8 %) and recover within 180 ms.
- Blink: close 90–140 ms, open 110–180 ms, eyelid shape, not just vertical scale.
- Use `easeOut`, `easeInOut` and `backOut`; `linear` only for continuous rotation.
- GLB clips: sample at 30 fps or use `CUBICSPLINE`.

## Expressions and speech

- Eight expressions that are distinguishable at 64 px in silhouette and eye shape, not only in brow angle: happy (eye arcs, raised cheeks), curious (one brow up, head tilt), thinking (eyes up and to the side), concerned (inner brows up), surprised (round eyes, small "o"), apologetic (soft eyes, slight head down), confident (narrowed eyes, small smile), neutral.
- All 15 canonical visemes (0 sil … 14 U) for `pip` and `nova`, with real shape differences: PP closed lips, FF lower lip tucked, aa wide open, O round, U small round and forward, E and I wide. Closed consonants stay closed under every expression; expression mouth corners are a separate layer from the speech mouth (separate nodes in 2D, separate morph targets in 3D), listed in `channelMasks`.
- `aurora` has no mouth: `speech.mode` `"none"`, reacts through `parameters` (`inputLevel`, `outputLevel`, `inputLow/Mid/High`, `outputLow/Mid/High`).

## Quality bar and process

Work in a loop of at least three rounds per avatar:

1. Build, then render previews from the real model bytes: poster light and dark at 512 px, a states sheet, an expressions sheet, a visemes sheet, expression × viseme combinations, and 64/128/320 px sizes. For 2D, implement the spine2d 1.1 features in your preview renderer exactly as specified above (gradients, blur, strokes, blend modes). For 3D, render with a studio setup: environment lighting, a key light, a rim light, ACES or Khronos Neutral tone mapping, and bloom on emissive parts.
2. Put each preview next to the matching concept image and write a short, critical comparison: proportions, volume and shading, eye appeal, glow, silhouette at 64 px, how alive the idle loop looks (render 8 frames across the loop).
3. Fix the three biggest gaps and repeat.

Reject your own result if any of these remain: flat single-colour shapes as final art (outlines and pupils excepted), visible faceting, eyes or mouth as plain discs, a silhouette that is a featureless ellipse at 64 px, idle motion under 1 % amplitude or with no secondary element, expressions that differ only by brow angle.

## Delivery (same as round 1)

- A real `.spineavatar` per target: `avatar.json` (schema 1.0), model, bindings, previews, `LICENSE.txt`, `provenance.json`, `source/` (generator script and authoring notes), `validation/report.json` with Pass, Fail and NotMeasured.
- Every manifest-referenced pose and clip resolves; SHA-256 and sizes in `files`; no absolute paths, no `..`.
- glTF Validator: 0 errors.
- Report honestly. If a feature or quality level cannot be reached with the tools in your environment (list the Python packages you actually have), say which, and what an artist would need to do in which tool. Do not rename another file to fake a format. Device performance and lip-sync latency are NotMeasured.
- At the end, list what changed from round 1 and the remaining weaknesses you would fix next.
