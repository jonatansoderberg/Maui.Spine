# Spine Avatar: the authoring profile, format 1.0

**Status:** Frozen as format 1.0 in [#586](https://github.com/jonatansoderberg/Maui.Spine/issues/586) (step B0 of the [implementation plan](../plans/spine-avatar.md), decisions D7 and D9). The schemas are embedded in `Plugin.Maui.Spine.Controls.Avatar.Core` (`AvatarSchemas`), the defaults in `AvatarDefaults`, the conventions in the loader; this document says the same in words.
**Audience:** someone writing a generator, an exporter or a package by hand, who needs nothing but this page to make a `.spineavatar` that validates.

A Spine Avatar is content: a ZIP named `*.spineavatar` holding a manifest, one or more models, their bindings, posters, a licence and a provenance note. The runtime mixes states, expressions, visemes, blinks, gaze and springs into poses; the bindings say what each pose moves. The format has **defaults for everything a typical avatar shares**, so an author writes only what differs.

## 1. Conventions and defaults first

Write these names and you need no declaration for them.

| What | The default | Override with |
| --- | --- | --- |
| The seven states `idle`, `connecting`, `listening`, `thinking`, `speaking`, `interrupted`, `muted` | Pose = the state's name; `transitionMs` 220, `interrupted` 90; `listening` input-reactive, `speaking` output-reactive; gaze `processing` while `thinking`, `engaged` otherwise | `states.<state>.{pose, animation, inputReactive, outputReactive, gaze, transitionMs}` |
| The eight expressions `neutral`, `happy`, `curious`, `thinking`, `concerned`, `surprised`, `apologetic`, `confident` | Pose `expr_<name>`; `transitionMs` 220, `surprised` 100; channels `face, body` (`ambientPose` for the ambient profile) | `expressions.<name>.{pose, channels, transitionMs}` |
| A missing expression | **A warning**, and the avatar shows `neutral` for it | — |
| Speech | `"speech": { "canonicalProfile": "oculus15-v1" }` means mode `canonicalVisemes` with the mapping `0 → viseme_sil … 14 → viseme_U` (§5); `expressionMouthScale` 0.45, `releaseMs` 80. No `speech` at all: mode `none` (the avatar may still follow the output level) | `speech.{mode, mapping, expressionMouthScale, releaseMs}` |
| Idle variants | The clips `idle_a` and `idle_b`, those the model has | `motions.idleVariants` |
| Gestures | `nod`, `shake`, `lean` (clip `lean` or `lean_in`), `interrupt`, `hop`, `wiggle`, `thumbsUp`, `jump`, `dance`: each one the model has a clip for | `motions.gestures` (gesture → animation) |
| Blink | Every 2.8–6.5 s | `motions.blinkIntervalSeconds` |
| Reduced motion | Pose `reduced_motion`, speech kept, 180 ms | `reducedMotion.{pose, retainSpeech, transitionMs}` |
| Who owns idle, blink and gaze | `scheduler` | `representations[].idleOwner`, `blinkOwner`, `gazeOwner` (`scheduler`, `model`, `none`) |
| Capabilities | **Computed by the validator** from the bindings and the model, and written to the report (§3) | `representations[].capabilities`: a declared list is used and checked against the computed one |
| Animations in the bindings | A clip whose name is the animation's needs no entry | `animations` maps a name to a clip only where they differ |
| Poses in a GLB | A morph target named like the pose (`viseme_PP`, `expr_happy`) is that pose: weight 1 on every node and primitive that has it | `poses.<name>` |
| A pose nobody wrote | Empty: it moves nothing. Do not write empty poses | — |
| An `sksl` representation | Needs **no bindings file**: states, expressions and visemes reach the shader as uniforms | — |

A manifest that writes every field explicitly, as the lab's packages from before format 1.0 do, stays valid and means exactly what it says; defaults fill only what is absent. `platforms` is no longer read (the renderer decides where a representation can be drawn) and warns.

## 2. The manifest, `avatar.json`

Required: `schemaVersion` (`"1.0"`), `id` (`^[a-z0-9][a-z0-9-]{1,63}$`), `displayName`, `assetVersion` and `minRuntimeVersion` (semantic versions), `profile` (`character`: a face that speaks and shows the eight expressions; `ambient`: an orb, waves, light; `humanoidAdvanced` is reserved), `representations` (1–8, best first), `posters` (`light`, `dark`), `license`, `provenance`, `files`. Everything else has the defaults of §1. The full schema: `avatar.schema.json` (`AvatarSchemas.ManifestResourceName`).

A representation names its `renderer` and `format`, its `model` and, when it has one, its `bindings` file:

| Renderer | Format | Model | Drawn by |
| --- | --- | --- | --- |
| `native3d` | `glb` | glTF 2.0 binary with morph targets, skins and clips; no required extensions | SceneKit (iOS, Mac Catalyst); Skia on the CPU on Android and Windows (S2), for models without alpha-masked materials |
| `skia` | `sksl` | One SkSL shader, at most 64 KB | Skia on every platform |
| `skia` | `spine2d` | A vector scene, `*.avatar2d.json` (§7, frozen) | Skia on every platform |

The view takes the first representation whose renderer the platform has, and shows the poster and the state as text when there is none. A new renderer id (a 3D route for Android, say) is an addition to the format, not a new version.

`themes.slots` names colours the app may restyle: `{ "light": "#RRGGBB", "dark": "#RRGGBB", "bindings": [...] }`, where a binding is `material:<index>` (glb), `uniform:<name>` (sksl, a `float3` the shader declares) or `<node>.fill`, `<node>.fill.linear|radial|sweep.stops[<i>]`, `<node>.stroke` (spine2d).

`extensions` (manifest and bindings) holds keys `<namespace>.<name>` for tools and renderers outside the format. The schema is strict everywhere else: a key it does not know warns, so a 1.x file still loads, and nothing outside `extensions` is read.

## 3. Capabilities

The validator computes them per representation; the report lists them (`computed: …`).

| Capability | When |
| --- | --- |
| `ambient`, `reducedMotion` | Always: the scheduler provides them |
| `expressions` | An expression pose with writes; a shader that declares an `expr*` uniform |
| `audioReactive` | A level parameter with writes; a shader that declares `inLevel`, `outLevel`, `energy` or the bands |
| `speechArticulation` | Speech mode `canonicalVisemes` and a viseme pose with writes; a shader that declares `mouth` |
| `gaze` | A `gaze` binding; a shader that declares `gaze` or `look` |
| `blink` | A `blink` binding or clip; a shader that declares `blink` |
| `gestures` | At least one gesture resolves to a clip |
| `themeSlots` | At least one theme slot |

A declared list is used as written. Where it differs from the computed one, the report warns in both directions ("declares X, which the bindings and model do not support"; "supports Y without declaring it"), so an author can drop the list or keep a deliberate subset (Aurora Flow keeps `gaze` off that way).

## 4. Bindings, `bindings.schema.json`

A bindings file belongs to one representation and says what poses, animations and levels move. Keys:

| Key | For | Meaning |
| --- | --- | --- |
| `schemaVersion`, `renderer` | all | Required; `renderer` must be the representation's |
| `poses` | glb, spine2d | Pose name → list of writes. glb: `{node, mesh, primitives, targetIndex, value}` (a morph weight; `expectedName` checks the target's name) or `{node, property: translation|rotation|scale, value}`; spine2d: `{node, property, value}` with `x`, `y`, `scaleX`, `scaleY`, `rotation`, `opacity` or `pathPose` |
| `animations` | glb, spine2d | Name → the model's clip, only where they differ |
| `stateAnimations` | glb, spine2d | A looping clip per state, replacing the idle loop while it shows (`thinking: think`) |
| `parameters` | glb, spine2d | `inputLevel`, `outputLevel` and, in spine2d, `inputLow|Mid|High`, `outputLow|Mid|High` and one per band, `inputBand0`–`inputBand23` and `outputBand0`–`outputBand23` (low to high; the built-in bars and ring): writes from `min` at level 0 to `max` at 1. **Values are absolute** |
| `channelMasks` | glb, spine2d | The channels the `expression`, `speech`, `idle` and `reflex` layers may write; a write outside its layer's mask is dropped |
| `framing` | all | `safeInset`, `fit`; glb also `cameraPosition`, `lookAt`, `verticalFov` |
| `clipBlend` | glb | `override` for skinned characters, whose clips must not add to the rest pose |
| `gaze` | glb | `{node, rangeMeters}` slides a node; `{node, headRotation}` turns a bone; `{morphs: {mesh, primitives, left, right, up, down}}` drives look targets (full at ±12°); `turn: {node, factor}` turns a node toward a held look |
| `blink` | glb | The target or targets the scheduler drives 0 → 1 → 0; `suppressExpressionTargets`, lid frames as `steps` with `closures` |
| `muteBadge`, `headNode`, `eyesNode`, `mouthNode`, `speechMouthMesh`, `expressionMouthTargets`, `defaultTransforms` | glb | As named; `expressionMouthTargets` are damped while speaking |
| `voices` | all | The voice the avatar speaks with, per provider: `openai` (realtime, such as `marin`), `azure` (Azure Speech), or a key an app reads. `VoiceAvatarDriver` asks the server for the `openai` one; `spine-avatar pack --voice openai=marin` writes it |
| `extensions` | all | Spine's renderers read `spine.lighting` (`"portrait"`: a soft front key and no coloured rim, for skin), `spine.bloom` (`{intensity, threshold, radius}`), `spine.springs` (`false`: no squash, lift or tilt), `spine.neckChain` and `spine.blinkFrames`. Files from before format 1.0 wrote these at the top level; they still work there and warn |

**Composition.** A pose moves a property toward its target by its weight; poses in one layer crossfade from the layer's starting values. A clip or level parameter is relative to the baseline in spine2d 1.1 (offset for `x`, `y`, `rotation`; factor for `scale*` and `opacity`, override when the baseline is 0); in spine2d 1.0 clip values were absolute. Level parameters are absolute in every version. The microphone-muted flag applies the `muted` pose masked to the `muteIndicator` channel, so the badge can show during speech.

## 5. Visemes

The mouth vocabulary is the 15 canonical visemes of `oculus15-v1` (`AvatarVisemes`):

| Id | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Name | sil | PP | FF | TH | DD | kk | CH | SS | nn | RR | aa | E | I | O | U |
| Default pose | `viseme_sil` | `viseme_PP` | `viseme_FF` | `viseme_TH` | `viseme_DD` | `viseme_kk` | `viseme_CH` | `viseme_SS` | `viseme_nn` | `viseme_RR` | `viseme_aa` | `viseme_E` | `viseme_I` | `viseme_O` | `viseme_U` |

`viseme_sil` may be left empty (the rest mouth). `PP` is a closure: the scheduler holds it fully over its interval, so neither an expression nor the next cue's lead-in can open it.

Drivers convert what they receive. Azure Speech's 22 ids (Spine.Voice's `Visemes`) map with `AvatarVisemes.FromAzure`:

| Azure | 0 | 1 æ ə ʌ | 2 ɑ | 3 ɔ | 4 ɛ ʊ | 5 ɝ | 6 j i ɪ | 7 w u | 8 o | 9 aʊ | 10 ɔɪ | 11 aɪ | 12 h | 13 ɹ | 14 l | 15 s z | 16 ʃ tʃ dʒ ʒ | 17 ð | 18 f v | 19 d t n θ | 20 k g ŋ | 21 p b m |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Canonical | sil | aa | aa | O | E | E | I | U | O | aa | O | I | kk | RR | nn | SS | CH | TH | FF | DD | kk | PP |

Text with no timing is estimated from its letters (`AvatarTextVisemes`) and reported as `EstimatedText`, never as visemes.

## 6. Worked examples

These are the reference packages rewritten with the defaults (`AvatarSlimming`, the tool's `pack`); the `files` table is left out.

**Nova** (`glb` on `native3d`, a character). 695 bytes; the 31 poses and 12 clips are in `bindings/native3d.json`, where the empty poses and the clips named like their animation are gone:

```json
{
  "schemaVersion": "1.0", "id": "nova", "displayName": "Nova",
  "assetVersion": "1.0.0", "minRuntimeVersion": "1.0.0", "profile": "character",
  "representations": [
    { "id": "native3d-primary", "renderer": "native3d", "format": "glb", "model": "models/nova.glb", "bindings": "bindings/native3d.json" }
  ],
  "speech": { "canonicalProfile": "oculus15-v1" },
  "motions": { "blinkIntervalSeconds": [3.0, 6.5], "breathPeriodSeconds": [3.6, 5.0] },
  "themes": { "slots": {
    "badge": { "light": "#1E2430", "dark": "#E8ECF2", "bindings": ["material:7"] },
    "shell": { "light": "#E4E9F0", "dark": "#C4CEDC", "bindings": ["material:0"] }
  } },
  "posters": { "light": "previews/poster-light.png", "dark": "previews/poster-dark.png" },
  "license": "LICENSE.txt", "provenance": "provenance.json"
}
```

**Aurora Flow** (`sksl` on `skia`, ambient). No bindings file; its states and expressions differ only in their timing, and its declared capability list keeps the shader's gaze off:

```json
{
  "schemaVersion": "1.0", "id": "aurora-flow", "displayName": "Aurora Flow",
  "assetVersion": "1.0.0", "minRuntimeVersion": "1.0.0", "profile": "ambient",
  "representations": [
    { "id": "skia-shader", "renderer": "skia", "format": "sksl", "model": "models/aurora-flow.sksl",
      "capabilities": ["ambient", "expressions", "audioReactive", "reducedMotion", "themeSlots", "speechArticulation"], "blinkOwner": "none" }
  ],
  "states": { "idle": { "transitionMs": 320 }, "connecting": { "transitionMs": 320 }, "listening": { "transitionMs": 320 },
              "thinking": { "transitionMs": 320 }, "speaking": { "transitionMs": 320 }, "muted": { "transitionMs": 320 } },
  "expressions": { "neutral": { "channels": ["color"], "transitionMs": 400 }, "happy": { "channels": ["color"], "transitionMs": 400 },
                   "curious": { "channels": ["color"], "transitionMs": 400 }, "thinking": { "channels": ["color"], "transitionMs": 400 },
                   "concerned": { "channels": ["color"], "transitionMs": 400 }, "surprised": { "channels": ["color"], "transitionMs": 400 },
                   "apologetic": { "channels": ["color"], "transitionMs": 400 }, "confident": { "channels": ["color"], "transitionMs": 400 } },
  "speech": { "canonicalProfile": "oculus15-v1", "expressionMouthScale": 1, "releaseMs": 90 },
  "themes": { "slots": {
    "cyan": { "light": "#00B8D9", "dark": "#41EFFF", "bindings": ["uniform:cyan"] },
    "blue": { "light": "#3676E8", "dark": "#5097FF", "bindings": ["uniform:blue"] },
    "violet": { "light": "#8F5BDF", "dark": "#A075FF", "bindings": ["uniform:violet"] },
    "pink": { "light": "#E255B8", "dark": "#FF6BD5", "bindings": ["uniform:pink"] },
    "warm": { "light": "#F08A3C", "dark": "#FFA05C", "bindings": ["uniform:warm"] }
  } },
  "posters": { "light": "previews/poster-light.png", "dark": "previews/poster-dark.png" },
  "license": "LICENSE.txt", "provenance": "provenance.json"
}
```

The shader declares only the uniforms it reads, from this contract (`AvatarShaderContract`); any other uniform fails validation, so a shader written for a newer runtime is refused rather than drawn with zeros:

| Uniform | Type | Meaning |
| --- | --- | --- |
| `iResolution` | float2 | Pixels of the drawing rectangle; the origin is its corner |
| `iTime`, `flow` | float | Seconds since start, and a phase that runs faster while thinking, speaking or connecting and slows under Reduce Motion: **animate with `flow`** so speed changes stay smooth |
| `inLevel`, `outLevel`, `energy` | float | Smoothed levels 0–1, and the one the current state reacts to |
| `bands` | float[8] | That level's spectrum in eight log bands, low to high; `bandLow`, `bandMid`, `bandHigh` the same in three |
| `mouth` | float | Mouth openness from the visemes, 0 closed to 1 for `aa` |
| `stateIdle` … `stateMuted`, `exprNeutral` … `exprConfident` | float | Crossfade weights of the seven states and eight expressions |
| `blink`, `micMuted` | float | 0–1 |
| `gaze`, `look` | float2 | Gaze with saccades, and the eased look direction (radians, x to the viewer's right, y up) |
| `spring` | float3 | Squash, tilt, lift |
| `dark` | float | 1 in dark mode |
| `accent` | float4 | The app's accent, alpha 0 when it has none |
| `gestureNod`, `gestureShake`, `gestureLean`, `gestureInterrupt`, `gestureHop`, `gestureWiggle`, `gestureThumbsUp`, `gestureJump`, `gestureDance` | float | A playing gesture's progress, 0 to 1, else 0. Declaring one gives the avatar that gesture, with the runtime's length for it (`AvatarShaderContract.GestureSeconds`) |
| A theme slot's `uniform:<name>` | float3 | The slot's colour for the current theme |

**Voice Totem** (`spine2d` on `skia`, ambient). States override only their transition (180 ms) and gaze (`none`); it has no viseme poses (speech mode `none`) and follows the output level through its parameters:

```json
{
  "schemaVersion": "1.0", "id": "voice-totem", "displayName": "Voice Totem",
  "assetVersion": "1.0.0", "minRuntimeVersion": "1.0.0", "profile": "ambient",
  "representations": [
    { "id": "skia-primary", "renderer": "skia", "format": "spine2d", "model": "models/voice-totem.avatar2d.json",
      "bindings": "bindings/skia.json", "blinkOwner": "none", "gazeOwner": "none" }
  ],
  "states": { "idle": { "transitionMs": 180, "gaze": "none" }, "connecting": { "transitionMs": 180, "gaze": "none" },
              "listening": { "transitionMs": 180, "gaze": "none" }, "thinking": { "transitionMs": 180 },
              "speaking": { "transitionMs": 180, "gaze": "none" }, "interrupted": { "gaze": "none" },
              "muted": { "transitionMs": 180, "gaze": "none" } },
  "speech": { "releaseMs": 90 },
  "motions": { "breathPeriodSeconds": [3.8, 5.8] },
  "themes": { "slots": { "accent": { "light": "#087F90", "dark": "#67E1E2", "bindings": ["ring.fill", "packet0.fill", "packet1.fill", "packet2.fill"] },
                         "surface": { "light": "#F7FAFC", "dark": "#101A26", "bindings": ["cutout.fill"] },
                         "core": { "light": "#32B1B9", "dark": "#40C5CF", "bindings": ["core.fill"] },
                         "halo": { "light": "#54C3CE", "dark": "#1E646F", "bindings": ["halo.fill"] },
                         "muted": { "light": "#65828D", "dark": "#94A6B5", "bindings": ["muteSlash.fill"] } } },
  "posters": { "light": "previews/poster-light.png", "dark": "previews/poster-dark.png",
               "states": "previews/states.png", "expressions": "previews/expressions.png", "visemes": "previews/visemes.png" },
  "license": "LICENSE.txt", "provenance": "provenance.json"
}
```

## 7. spine2d 1.1 (frozen)

A spine2d scene is `{schemaVersion, bounds: {width, height}, nodes, pathPoses, animations, defaultPose}`. Nodes (1–128, parents declared before children) are `group`, `ellipse`, `roundedRect` or `path` (SVG path data), each with a `channel`, an optional `transform` (`x`, `y`, `scaleX`, `scaleY`, `rotation` in radians), `opacity` and a `fill` (`color` or theme `slot`). Path poses are named path data, `linear` (same commands as the node, interpolated) or `crossfade`. Clips are tracks of keyframes with easings `linear`, `step`, `easeIn`, `easeOut`, `easeInOut`, `backOut`; a looping clip must end where it began (rotations modulo a turn).

Version 1.1 adds features a representation must list in `requiredFeatures`, so an older runtime refuses the scene instead of drawing it wrong: `gradients` (linear, radial and sweep fills, 2–8 stops), `blur` (0–40), `strokes` (width, cap) and `blendModes` (`screen`, `multiply`, `plus`).

spine2d is **frozen at 1.1**: it stays supported, including its character features (path poses, viseme path poses, blur, blend modes), but no reference model uses them and none is planned. New characters are GLB; new ambient avatars are SkSL.

## 8. The container

| Rule | Value |
| --- | --- |
| Archive | ZIP, `avatar.json` at the root (case-sensitive) |
| Size | 50 MB compressed, 150 MB uncompressed, 512 entries, 32 MB per entry, 200× compression ratio for entries over 64 KB (`AvatarArchiveLimits`; a trusted asset may be given larger ones) |
| Names | Relative, forward slashes; no `..`, `.`, empty segments, backslashes, colons, NUL, absolute paths or symbolic links; no two names that differ only in case |
| Integrity | `files` lists every entry but `avatar.json` with its lowercase SHA-256 and size; an unlisted, missing, resized or changed file fails the package |
| Never | Nothing in the archive is executed or extracted to disk |

`source/` may carry the generator and notes during development (the lab's packages embed their generator there). A **shipped** package is stripped of `source/` (`spine-avatar strip`, which rewrites the hash table); Spine ships no avatar in a NuGet package.

**Posters are mandatory**: `previews/poster-light.png` and `previews/poster-dark.png`, shown when no renderer is available, in galleries and while a package loads. They are made by the tool (`spine-avatar render-sheet`, step C1), not by hand. The state, expression and viseme sheets (`posters.states`, `.expressions`, `.visemes`) are optional.

`LICENSE.txt` and `provenance.json` say who may use the avatar and how it was made (the generator, the source images, the licences of anything third-party). Never package a third-party character.

## 9. Validation

Loading runs, in order: the container rules, the manifest schema, the defaults, the integrity table, the computed capabilities, then per representation the bindings schema and the semantic checks (every pose and animation the manifest names resolves, morph targets and nodes exist, path poses parse and match their node's topology, keyframes are in order, theme bindings point at real paints, shader uniforms are in the contract). Every check is in the report with its result; nothing is a bare yes or no. A key the schema does not know, an absent default expression and a mismatched capability list are warnings; everything else that fails, fails the package with the file and the JSON path.
