# Issue #586 — Spine.Avatar: format 1.0, bindings schema and the driver contract

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/586
**Branch:** issue/586-spineavatar-format-10-bindings-schema-and-the-driv
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), steps B0.1–B0.3, decision D9.

## Plan

The code still lives in `samples/SpineAvatarLab/Avatar.Core` (it moves into the Core package in B1.1), so B0 changes it there and the Core tests compile it as before. #542 is open: neither `Tools/` nor a committed package changes; the rewritten packages are made in memory by the tests and, for the measurements below, as copies outside the repository.

- **B0.1** `avatar.json` is read into a document whose D9 fields are optional, and `AvatarDefaults.Resolve` turns it into the `AvatarManifest` the scheduler and renderers already use. Bindings get the conventions in `AvatarPackage.GetBindings` (clips named like their animation, GLB morph targets named like a pose), `extensions` with `spine.*` keys, and `Find(name)` for the rig and the SceneKit surface. `AvatarCapabilities` computes each representation's capabilities. `avatar.schema.json` and `bindings.schema.json` are embedded resources, applied by `AvatarSchemas.Check`. `AvatarSlimming` rewrites a package with the defaults (the tool's `pack` in C1 will use it).
- **B0.2** `docs/proposals/spine-avatar.md`, the authoring profile.
- **B0.3** `AvatarContracts.cs` trimmed to the enums, `AvatarExpressionRequest`, `IAvatarPlaybackClock`, `AvatarPlaybackSnapshot` and the cue structs the scheduler takes; `AvatarDriver`, `AvatarInput`, `AvatarRuntime`, `AvatarVisemes` and `TextAvatarDriver` in Core.

## Changes

- `AvatarManifest.cs`: resolved records; `AvatarRepresentation.Bindings` nullable, `Platforms` gone, `DeclaredCapabilities` added; `Declared` flags on states, expressions, the mapping and reduced motion so a default pose may be absent while a named one must resolve; `AvatarBindings.Poses`/`Animations` optional, `Extensions`, `Find`; vocabulary for idle variants and gestures.
- `AvatarDefaults.cs`: the manifest document and the defaults (states, expressions, speech, motions, reduced motion, owners); `platforms` warns.
- `AvatarConventions.cs`, `AvatarCapabilities.cs`, `AvatarSchemas.cs`, `Schemas/avatar.schema.json`, `Schemas/bindings.schema.json`, `AvatarSlimming.cs`: new.
- `AvatarPackage`: `GetBindings` with conventions, `GetDeclaredBindings`, `GetMotions` per representation; `AvatarArchive` runs the manifest schema, the defaults, the capabilities, then the validator.
- `AvatarValidator`: the schema replaces the "fields outside the authoring profile" warnings; missing states are defaults, not failures; a missing default expression warns and falls back to neutral; viseme 0 may be empty; shaders skip pose checks.
- `AvatarScheduler`: motions from `GetMotions`; an expression without a pose shows neutral.
- `GltfAvatarRig`, `SceneKitAvatarSurface`: binding keys through `Find`, so `extensions.spine.lighting` and the old top-level `lighting` both work. `ThreeDAvatarSurface` (lab only) sends `{}` when a representation has no bindings file.
- `AvatarContracts.cs` trimmed; `AvatarTextRequest` moved to the lab's feeds.
- `AvatarDriver.cs` (`AvatarDriver`, `AvatarInput`, `AvatarRuntime`), `AvatarVisemes.cs`, `TextAvatarDriver.cs`: new.
- Tests: `AvatarFormatTests` (schemas on all 16 reference packages, the slim round trip of the five kept ones, Nova's size, Aurora Flow without bindings, Kairo's extensions, defaults, the neutral fallback, declared capabilities, schema errors with paths), `AvatarDriverTests` (22→15 and back, tick order, driver replacement, state across a source change, the Text driver's timeline and `Clear`). 159 tests pass.
- `docs/proposals/spine-avatar.md`: the authoring profile.

## Decisions

- **Defaults beyond the plan's list:** `interrupted` 90 ms and `surprised` 100 ms, because all five kept packages use them; without them each package would repeat them.
- **No default paths for posters, licence and provenance.** D9 lists defaults for states, expressions, mapping, motions and reduced motion; the container's fields stay required so the format changes no further than D9.
- **`speech` absent means mode `none`;** `canonicalProfile: oculus15-v1` alone means `canonicalVisemes` with the standard mapping.
- **A declared capability list is used as written,** with warnings both ways against the computed one, rather than intersected: "a fully explicit manifest means exactly what it says". Aurora Flow's list omits `gaze` though its shader reads it; the slim rewrite keeps the list so the avatar behaves the same.
- **The schema check is a subset validator in Core** (the keywords the two schemas use), not a package dependency. Unknown keys warn so a 1.x file loads; other violations fail with the file and the JSON path.
- **The morph-name convention is GLB only.** spine2d is frozen and has no reference model that would use one.
- **`viseme_sil` may be empty:** silence is the rest mouth, so its absence does not warn.
- **"Nova's manifest under 1 KB" is measured without the `files` table:** the authored part is 695 bytes compact (1022 indented); the hash table of its seven files is another 1.4 KB and is written by the tool.
- **The driver contract lives in Core, with `AvatarRuntime`** (driver tick, then scheduler update), so a fake driver's tick order and the Text driver are tested in the Core tests. `OnAttached()` takes no view (§4.2 of the plan had `OnAttached(AvatarView)`): Core has no MAUI, and the drivers that need the view (Script's `LookAt(view)`) get it in the Avatar package. **`TextAvatarDriver` is in Core** too (the plan put it in the Avatar package); the Avatar package depends on Core, so apps see no difference.
- **`AvatarInput` keeps the state, mute, base expression, clock, quality and look** and gives them to each new scheduler, so a source switch does not disturb the driver; cues and levels are moment to moment and are not kept.
- **`TextAvatarDriver.Begin()`** is an addition: "a reply is coming", Thinking until the first text, which the chat wiring needs before the first delta.
- The Text driver's clock advances by the tick's delta, not wall time, so its timeline is deterministic under test; cues go a second ahead at strength 0.8, as the lab's TTS feed did.

## Measurements (copies outside the repository, Mac, `AvatarSlimming`)

| Package | `avatar.json` | Bindings (compact JSON) | Loads | Warnings after |
| --- | --- | --- | --- | --- |
| Kairo | 4543 → 2909 B (authored 1176 B) | 7805 → 7635 B, extensions moved | yes | none |
| Nova | 4972 → 2107 B (authored 695 B) | 5104 → 4834 B | yes | none |
| Aurora Flow | 5082 → 3743 B (authored 1622 B) | 795 B → no file | yes | `gaze` supported but not declared (kept off on purpose) |
| Puff | 5165 → 3347 B (authored 1362 B) | 4441 → 4128 B | yes | none |
| Voice Totem | 6018 → 4331 B (authored 1369 B) | 3223 → 2970 B | yes | none |

Unchanged, the five load with only these warnings: `platforms` is no longer read (all), Kairo's four and Nova's one top-level extension keys, Aurora Flow's undeclared `gaze`.

## Verification

- Core tests: 159 pass (`dotnet test tests/Plugin.Maui.Spine.Controls.Avatar.Core.Tests`).
- The lab builds for Mac Catalyst. **Not run:** the lab's three.js page reads the bindings file as written and knows neither the conventions nor `extensions`; it is lab-only and no committed package depends on them yet.
- Nothing ran on a device in this step.
