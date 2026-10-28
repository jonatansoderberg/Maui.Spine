# Issue #697 — Avatar format 1.1: options, presets and a runtime version

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/697
**Branch:** issue/697-avatar-format-11-options-presets-and-a-runtime-ver
**Status:** Completed (merged as [#698](https://github.com/jonatansoderberg/Maui.Spine/pull/698) on 2026-10-28)

Step M2.1 of [docs/plans/spine-avatar-mini-v2.md](../docs/plans/spine-avatar-mini-v2.md) (decisions M-D1–M-D9 approved 2026-10-28).

## Plan

### Format 1.1 (`Schemas/avatar.schema.json`)
Two optional top-level sections:

```json
"options": {
  "shape": { "displayName": "Shape", "values": ["ball", "cube"], "default": "ball", "bindings": ["uniform:shape"] }
},
"presets": {
  "red": { "displayName": "Mini Red", "options": { "shape": "cube" }, "themes": { "body": { "light": "#F1273A", "dark": "#FF4454" } } }
}
```

- **Option names:** `^[a-z][a-zA-Z0-9]{0,31}$`.
- **Values and preset names:** `^[a-z0-9][a-z0-9-]{0,31}$`.
- **Value lists:** 2–64 unique values.
- **Bindings:** a list, as theme slots have. `uniform:<name>` is built. `node:` and `material:` are reserved: they warn and are ignored.

The schema's title and `$id` move to 1.1, and `schemaVersion` accepts 1.0 and 1.1.

### Records (`AvatarManifest.cs`, `AvatarDefaults.cs`)
- `AvatarOption(Values, Default, Bindings, DisplayName)` with `IndexOf(value)`.
- `AvatarPreset(DisplayName, Options, Themes)`, where Themes maps a slot to `AvatarColors(Light, Dark)`.
- `AvatarManifest.Options` and `Presets`, resolved to empty dictionaries when absent (as `Voices` is).
- The document records in `AvatarDefaults.cs` get the optional fields.

### Validation (`AvatarValidator.cs`)
- **Schema version:** 1.0 and 1.1 are known. A newer 1.x warns that its fields newer than 1.1 are ignored. Options or presets in a manifest that says 1.0 warn.
- **Options:** the default is one of the values. A binding's prefix is `uniform:`, `node:` or `material:`; anything else fails.
- **Presets:** a preset names only known options, values the option offers, and known theme slots. Its colours are `#RRGGBB(AA)`.
- **SkSL:** an option's `uniform:` binding must be a declared `float`. Such a uniform counts as part of the contract, as a theme slot's `float3` does today. A `uniform:` binding on a package without an `sksl` representation warns.

### Runtime version
- `AvatarRuntime.Version = "1.1.0"`.
- A manifest whose `minRuntimeVersion` is newer fails with a message that names both versions and says to update `Plugin.Maui.Spine.Controls.Avatar`.
- Comparison is major.minor.patch. A pre-release suffix counts as below its release.

### Renderers
Both SkSL renderers: the library's `Skia/AvatarShader.cs` and the lab's own copy `samples/SpineAvatarLab/Avatar.Skia/AvatarShader.cs`.
- Accept option-bound uniforms at compile.
- Set each option uniform to its default's index, so a package with options loads and draws its default.
- Choosing another value (`SetOption`, `Preset`) is M2.2.

### The Minis
- `minRuntimeVersion` 1.1.0, since their shaders declare the gesture uniforms from #696.
- Set in `Tools/mini_avatars.py` and repacked. The shaders do not change.

### Tool
- `pack` keeps `options` and `presets` as written (`AvatarSlimming` edits only the sections it knows); a test proves it.
- `validate` reports them.

### Docs
- `docs/proposals/spine-avatar.md` §2: options, presets, `minRuntimeVersion` and the runtime version.
- The plan's step status.

### Tests
- A manifest with options and presets loads and resolves them, and an option uniform gets the default's index.
- Each validation rule above fails or warns as stated: unknown option or value in a preset, default outside the values, an undeclared or non-float uniform, an unknown binding prefix, `node:` warns.
- `minRuntimeVersion` newer than the runtime fails with both versions named; equal or older passes.
- Slimming keeps options and presets.
- The existing 279 tests still pass.

## Open Questions

(none: the design is in the plan's §3.3, approved with M-D1–M-D9)

## Changes

- **`avatar.schema.json`, format 1.1:**
  - `options` and `presets` added.
  - The `choice` pattern added for values and preset names.
  - `schemaVersion` and `minRuntimeVersion` described.
  - The title and `$id` moved to 1.1.
  - `propertyNames` patterns are written inline, because the subset checker reads only `pattern` there, not `$ref`.
- **`AvatarManifest`:**
  - New `Options` and `Presets` (never null; only `AvatarDefaults.Resolve` constructs the record).
  - New records `AvatarOption` (with `IndexOf`), `AvatarPreset` and `AvatarColors`.
  - The document records and `Resolve` in `AvatarDefaults`.
- **`AvatarRuntime.Version` = 1.1.0.**
- **`AvatarValidator`:**
  - Schema versions 1.0 and 1.1 are known, and a 1.0 manifest with options or presets warns.
  - `minRuntimeVersion` is checked with `CompareVersions`, a semver order: a pre-release sorts below its release, build metadata is ignored.
  - `ValidateOptions` checks that the default is among the values, the binding kinds, and preset references.
  - The SkSL check accepts option-bound `float` uniforms, and fails a binding to an undeclared uniform or to one the runtime or a theme slot fills.
- **`AvatarShaderContract`:** `OptionType` and `OptionUniforms(manifest)`.
- **Both SkSL renderers** (the library's and the lab's copy): option uniforms are accepted at compile and start at the default's index.
- **Minis:** `minRuntimeVersion` 1.1.0 in `Tools/mini_avatars.py`. All nine repacked: the shaders are byte for byte the same, and all validate as `shipping`.
- **Tests:** `AvatarOptionsTests` adds 21, covering loading and default index, every failure and warning, version order, the runtime check and slimming. `BothSchemasAreEmbedded` now expects the 1.1 title. 300 pass.
- **Docs:** the format doc (§2 and the uniform table, title and status), the skill's §3, and the plan's status and §6.1.

## Decisions

- **`bindings` is a list**, not the plan's single `binding`. Theme slots already bind a list, and one choice may bind in more than one representation (a uniform in sksl, a material in glb).
- **A preset's colours name both light and dark**, like a theme slot. When an app gives a single colour at runtime, the dark tone is derived (M2.2/M2.3, M-D2). That does not apply to presets, which have v1's chosen pairs.
- **The runtime version is 1.1.0 in M2.1**, although only M2.2 lets an app pick a non-default option. Both land before the next tag. A 1.1 package on this runtime then draws its default, which is a correct, if partial, rendering.
