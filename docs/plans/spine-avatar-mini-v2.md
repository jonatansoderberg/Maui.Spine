# Spine Avatar Minis v2: shapes and colour as options

**Status:** M2.0 and M2.1 done (#696, #698, merged 2026-10-28); M2.2 in progress ([#699](https://github.com/jonatansoderberg/Maui.Spine/issues/699)). Decisions M-D1–M-D9 approved 2026-10-28.
**Last updated:** 2026-10-28 (M2.2: Preset, SetOption, BodyColor in AvatarView and the lab; M2.1 merged as #698)
**Inputs:** the owner's review of v1 on 2026-10-28 (§2); Minis v1 (`912822b` on `claude/zealous-ramanujan-ipgj2p`, and `d5eacf18` on the local branch `minis-eyes-and-gestures`: eye reach, gestures for SkSL avatars); the format in [spine-avatar.md](../proposals/spine-avatar.md); the `spine-avatar` skill.
**Related:** [spine-avatar.md](spine-avatar.md) (the avatar packages, phases B–C; this plan is phase M2 beside them), `samples/SpineAvatarLab/Tools/mini_avatars.py`, `mini_avatars.sksl`, `mini_eyereach.py`.
**Goal:** One small Mini avatar whose shape and colour the app chooses: seven general shapes drawn as if by hand, any body colour, the nine v1 figures kept as named presets, upright bodies that turn as they look, a subtle 3D shading, calmer talking eyes and a livelier jump. The format gains a general way for an avatar to offer such choices (`options` and `presets`), not something only the Minis can use.

## How to continue from a new session

1. Read this file, §3 of the `spine-avatar` skill and the format's §1–§3 in [spine-avatar.md](../proposals/spine-avatar.md).
2. Pick the first step in §5 whose status is **Ready** and whose dependencies are **Done**. Create its issue if the Issue column is empty, then `/spine-issue <id>`.
3. Work in a git worktree, never a checkout in the shared main folder. Do not touch Kairo or its tools.
4. In the same PR: set the step's status and PR, record non-obvious choices under §6.1, update **Last updated**.
5. Status values: **Proposal**, **Waiting**, **Ready**, **In progress**, **In review**, **Done**, **Parked**, **Dropped**.

## 1. What exists (v1)

| Part | Where | Notes |
| --- | --- | --- |
| Nine packages `mini-<colour>.spineavatar`, about 30 KB each | `samples/Avatars` | sksl, profile `character`, eyes only. Not released, not in the Showcase or the sample server's list |
| Template and generator | `Tools/mini_avatars.sksl`, `Tools/mini_avatars.py` | One shader per figure: an outline (`SHAPES`), constants per figure (`FIGURES`: colours, tilt, resting look, eye type and size, `reach`, foot) |
| Eye reach and foot | `Tools/mini_eyereach.py` | The furthest the outer eye may turn with both eyes whole on the body, at normal and 1.3x eye size; each outline's lowest point |
| Gestures for SkSL | `AvatarShaderContract` (`gesture<Name>` uniforms, `GestureSeconds`), `AvatarPackage.GetClipDurations`, both SkSL renderers (the library's and the lab's own copy in `samples/SpineAvatarLab/Avatar.Skia`) | nod, shake, lean, hop, wiggle, jump, dance |
| Colour | theme slots `body` and `eye`, bound to `uniform:body` / `uniform:eye` | `AvatarView.SetThemeColor("body", color)` already recolours a Mini at runtime; one colour serves both light and dark |
| Tests | `AvatarArchiveTests.ReferenceAvatars`, `AvatarShaderTests` | blink and talk, both eyes when looking far away, gestures inside the frame, nod and shake move the eyes, the jump's crouch, flight and rest |

Gaps found while writing this plan:
- `minRuntimeVersion` is read into the manifest but never checked, and the runtime has no version of its own. A package that declares a uniform a runtime does not know fails to load ("not in the runtime's contract"), which is visible but says nothing about upgrading. v1's gesture uniforms already need the runtime from `d5eacf18` while the packages say `1.0.0`.
- The validator warns about a `schemaVersion` newer than 1.0 and ignores its new fields, so a 1.1 package still loads on a 1.0 runtime and draws its defaults, unless its shader declares a 1.1 uniform.

## 2. The owner's review (2026-10-28)

- The Minis turned out well overall; the eyes feel alive and carry the mimicry.
- **Shapes are general.** Brown is a ball, red a cube (a die), orange a drop, amber an upside-down bowl, green a box or bean, teal an upside-down drop, blue a marshmallow (cylinder). Purple is a slightly flatter ball and pink a ball: both can be the ball.
- **Colour should be a property** of the avatar, so the shapes generalise and the colour can change.
- **No tilt at rest.** Several Minis lean; they should stand straight. When one looks to the side, tilt with the look so the whole form seems to turn.
- **Subtle 3D:** a little shading or highlight, not much.
- **Talking eyes** should grow less and move more, expanding only a little.
- **Jump:** exaggerate it: pull together and shoot itself off.
- Keep them small.
- **Added when approving the plan:** the shapes may deviate a little and be unique, as if drawn by hand, but neat: not a geometric circle or box. It makes them feel more alive. They may tilt lightly to the sides when they "turn the head" or look in different directions.

## 3. Design

### 3.1 Shapes

Seven outlines, each with its own measured `reach` and foot (`mini_eyereach.py` runs per shape, not per figure):

| Option value | Shape | v1 source | Default eyes |
| --- | --- | --- | --- |
| `ball` | ball | brown, pink, purple | round |
| `cube` | rounded cube | red (`squircle`) | tall |
| `drop` | drop, narrow at the top | orange (`egg`) | tall |
| `bowl` | upside-down bowl | amber (`dome`) | tall |
| `bean` | bean or box | green (`pill`) | round |
| `inverted-drop` | drop, wide at the top | teal (`oval`) | round |
| `marshmallow` | marshmallow, a soft cylinder | blue (`tin`) | round |

Each outline is drawn as if by hand (§3.4, M-D9), so `mini_eyereach.py` must measure the irregular outline, not the ideal one. All seven live in one shader and are chosen by a uniform. SkSL has no dynamic indexing into functions, so `shape()` is a chain of `if` on a float (each outline is a handful of operations; the cost is one branch per pixel, uniform across the frame).

### 3.2 Colour

The `body` theme slot stays the colour. What changes:
- `AvatarView` gets a bindable way to set a slot's colour from XAML (today `SetThemeColor` is a method only).
- When the app gives one colour, the shader derives the dark-mode tone from it (a slight lift in `dark`), as v1's hand-picked dark colours do. A preset may still name both.
- The shading in §3.4 is computed from the body colour, so any colour gets it.

### 3.3 Format 1.1: `options` and `presets`

Two additive manifest sections, general for every format but bound only for `sksl` in this plan:

```json
"options": {
  "shape": { "values": ["ball", "cube", "drop", "bowl", "bean", "inverted-drop", "marshmallow"], "default": "ball", "bindings": ["uniform:shape"] },
  "eyes":  { "values": ["round", "tall"], "default": "round", "bindings": ["uniform:eyeStyle"] }
},
"presets": {
  "red":   { "displayName": "Mini Red",   "options": { "shape": "cube", "eyes": "tall" }, "themes": { "body": { "light": "#F1273A", "dark": "#FF4454" } } },
  "brown": { "displayName": "Mini Brown", "options": { "shape": "ball" },                "themes": { "body": { "light": "#9A6334", "dark": "#B07644" } } }
}
```

- An option is a named choice of string values with a default. Its `bindings` are a list (§6.1); a `uniform:<name>` binding gets the value's index as a float; the shader declares it, and the contract accepts it because the manifest binds it (as theme slots work today). Other bindings (`node:` for spine2d visibility, `material:` for glb) are reserved, not built.
- A preset is a name for a set of option values and theme colours, with a display name. The nine v1 figures become presets of one package.
- Runtime API: `AvatarView.SetOption(name, value)`, a bindable `Preset`, and an `AvatarSource` that names a preset (`mini.spineavatar#red` or a `Preset` property; decided in M2.2). Changing an option or preset sets uniforms; it never reloads the package.
- Validation: every binding's uniform is declared; values are unique; the default is one of them; a preset names only known options, values and slots. The `character` profile's requirements apply to the package, not to each preset.
- Versioning: `schemaVersion` 1.1, `minRuntimeVersion` raised, and the runtime gets a version and checks `minRuntimeVersion` with a clear message (M2.1). A 1.0 runtime refuses the package through the shader's unknown uniforms, which is acceptable for unreleased packages but should not be how a released one fails.

### 3.4 Look and motion

- **Hand-drawn outlines.** No shape is a true circle, box or ellipse. Each outline gets a fixed, shape-specific deviation of its own: a sum of two or three low harmonics of the polar angle (orders 2–5) with fixed phases, amplitude about 1.5–3 % of the size, a side that is a little fuller than the other, and on the cube unequal corner radii and slightly bowed sides. The deviation is static (a drawing, not v1's animated wobble, which stays on top of it), smooth (no notches: the outline's curvature keeps its sign wherever the ideal shape's does) and symmetric enough that the figure still stands straight at rest. Two shapes never share a deviation, and the dark and light renders are the same drawing.
- **Upright.** The per-figure `TILT` goes. Tilt comes only from the turn: the body leans lightly to the side it looks toward and shifts sideways with it, so turning the head or looking around tilts the whole figure a little. The highlight (below) slides the other way, so a look reads as the form turning, not the eyes sliding over a flat shape.
- **Subtle 3D.** Computed from the outline's signed distance and the body colour: a vertical tone (lighter top), a soft highlight up and to the side of the turn, a slightly darker rim, a faint shade at the foot. Target: the body's luminance spread stays small (measure it; §4), the figure still reads as flat colour at 64 pt.
- **Talking eyes.** Height stretch per syllable down from 0.55 to about 0.15–0.2; instead each syllable lifts the eyes a little and widens them a touch, alternating, so speech shows as movement. The v1 test that a syllable enlarges the eyes gets a lower threshold plus a test that the eyes move.
- **Jump.** A deeper crouch with the foot on the ground, a stretch at takeoff (taller and thinner, like a spring let go), a higher and quicker flight, a landing that squashes past rest and settles back. Headroom: the figure at rest gets smaller (§6, M-D4) so the flight fits inside the frame without shrinking in the air.
- Everything else from v1 stays: the eye reach that keeps both eyes on the body, blink, the states and expressions as eye shapes and body poses, nod and shake with the eyes, lean, hop, wiggle, dance.

### 3.5 Size

One package for all seven shapes and nine presets: the shader source grows from about 8 KB to an estimated 12–14 KB (seven outlines, shading), plus one poster pair for the default. Target **under 40 KB packed**, against 9 × 30 KB in v1. Per-frame cost stays at v1's (208 B allocated per frame, render p95 well under 1 ms on the Mac's GPU); measure the CPU path on the iPhone.

## 4. Verification

Every step's tests pass, and for M2.3 onwards, per shape:
- both eyes whole on the body at every look, normal and enlarged (the v1 test, run per shape);
- every gesture inside the frame; the jump's crouch keeps the foot, the flight leaves the ground by more than v1's, the end is back at rest;
- at rest the body's principal axis is vertical (no tilt), and a look to the side tilts it toward the look;
- each outline deviates from its ideal shape by 1–3 % of the size at most, with no notches (curvature sign as the ideal shape's), and no two shapes deviate alike; the owner judges whether it reads as neatly hand-drawn;
- the body's luminance spread under a threshold set from the first approved render (subtle means measurable);
- talking: eyes grow less than v1 and move more (eye centre travel per syllable);
- the lab: states, looks, gestures, `skia gpu` and `skia cpu`, light and dark, 64 pt and 320 pt, per shape and for a custom colour; the iPhone through `Tools/lab/phone.sh`;
- the owner looks at the lab before the nine v1 packages are removed.

## 5. Steps (phase M2)

| Step | What | Depends on | Issue | Status |
| --- | --- | --- | --- | --- |
| M2.0 | Land v1: PR for `912822b` + `d5eacf18` (eye reach, gestures for SkSL) and this plan, so M2 builds on master | — | — | Done ([#696](https://github.com/jonatansoderberg/Maui.Spine/pull/696)) |
| M2.1 | Format 1.1: `options` and `presets` in `avatar.schema.json`, the manifest records, the validator and the format doc; a runtime version and the `minRuntimeVersion` check | M2.0 | [#697](https://github.com/jonatansoderberg/Maui.Spine/issues/697) | Done ([#698](https://github.com/jonatansoderberg/Maui.Spine/pull/698)) |
| M2.2 | Runtime: option uniforms in both SkSL renderers, `AvatarView.SetOption`, `Preset`, bindable theme colour, the preset in `AvatarSource`; the lab's pickers (shape, preset, colour) and harness commands (`option shape cube`, `preset red`, `color body #hex`) | M2.1 | [#699](https://github.com/jonatansoderberg/Maui.Spine/issues/699) | In progress |
| M2.3 | Shader v2 and generator: one template with the seven hand-drawn shapes, upright with turn tilt, shading, calmer talking eyes, the new jump; `mini_eyereach.py` per shape; one `mini.spineavatar` with the nine presets | M2.2 | | Waiting |
| M2.4 | Tool: `pack` and `validate` for options and presets, `render-sheet` per preset or shape | M2.1 | | Waiting |
| M2.5 | Replace the nine v1 packages with `mini.spineavatar` in `samples/Avatars`, the lab's list and the tests (run per preset and shape) | M2.3, owner's review | | Waiting |
| M2.6 | Docs: options, presets and colour in `docs/wiki/avatars.md` and the format doc, the skill's §3 and Mini notes | M2.5 | | Waiting |

## 6. Decisions

All approved by the owner on 2026-10-28 as recommended (the recommended option is the decision).

- **M-D1, one package or nine.** **Recommended:** one `mini.spineavatar` with options and presets (smaller, one source of truth, any colour). Alternative: keep nine packages generated from the shared template, each a preset baked in; no format change, but no shape or colour choice at runtime beyond the theme slot.
- **M-D2, colour as a theme slot or an option.** **Recommended:** the existing `body` theme slot (already bound, has light and dark, `SetThemeColor` works today), with a bindable property and a derived dark tone. An option of type colour would duplicate theme slots.
- **M-D3, eye type.** **Recommended:** its own option (`round`, `tall`), each preset naming its own, so a cube can have round eyes. Alternative: fixed per shape.
- **M-D4, room for the jump.** **Recommended:** the figure at rest about 8–10 % smaller than v1, so the exaggerated flight fits without shrinking in the air. Alternative: keep v1's size and shrink during the flight (reads as moving away).
- **M-D5, posters.** **Recommended:** a poster pair for the default only; galleries render presets live (the shader is cheap). Alternative: small posters per preset (about 9 × 2 × 4–10 KB more).
- **M-D6, purple and pink.** **Recommended:** both the `ball`, as the owner said; purple's flatter proportions go. Alternative: a `ball` with a proportion option (then reach and foot must be measured per proportion).
- **M-D7, names.** Shape values as in §3.1 (`ball`, `cube`, `drop`, `bowl`, `bean`, `inverted-drop`, `marshmallow`) and package id `mini`; presets keep the colour names of v1.
- **M-D8, options beyond SkSL.** **Recommended:** the format reserves `node:` and `material:` bindings, but only `uniform:` is built now.
- **M-D9, hand-drawn outlines (the owner's addition).** Every shape deviates slightly and uniquely from its geometric ideal, neatly, as if drawn by hand (§3.4), and the figures tilt lightly with the look.

### 6.1 Decided during the work

- **M2.1 (#697):** an option's `bindings` is a list, as a theme slot's is (one choice may bind in several representations), not the single `binding` sketched in §3.3. A preset's colours name light and dark, like a theme slot. The runtime version is 1.1.0 from M2.1: a 1.1 package then loads and draws its default, and M2.2 lets an app choose; both land before the next tag. The Minis' `minRuntimeVersion` is 1.1.0, since their shaders declare gesture uniforms (#696).
- **M2.2 (#699):** no preset in `AvatarSource`: `AvatarView.Preset` is enough, one package shown as two figures being two views. The main colour is `BodyColor`, the `body` slot by convention, as `AccentColor` is the `accent` slot. A changed `Preset` clears the `SetOption` values but keeps the app's colours. An app colour still serves light and dark; deriving a dark tone is M2.3's shader.
