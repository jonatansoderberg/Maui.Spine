# Issue #699 — Avatar options at runtime: SetOption, Preset and theme colours from XAML

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/699
**Branch:** issue/699-avatar-options-at-runtime-setoption-preset-and-the
**Status:** Completed (merged as [#700](https://github.com/jonatansoderberg/Maui.Spine/pull/700) on 2026-10-28)

Step M2.2 of [docs/plans/spine-avatar-mini-v2.md](../docs/plans/spine-avatar-mini-v2.md), after format 1.1 (#697, PR #698).

## Plan

### What an app writes

```xml
<avatar:AvatarView Source="mini.spineavatar" Preset="red" />
<avatar:AvatarView Source="mini.spineavatar" Preset="red" BodyColor="{StaticResource Brand}" />
```
```csharp
avatar.SetOption("shape", "drop");      // over the preset; null gives the option back to the preset or default
avatar.GetOption("shape");              // "drop"
```

### `AvatarView` (`src/Plugin.Maui.Spine.Controls.Avatar/AvatarView.cs`)
- **`Preset`**, a bindable `string?`:
  - It applies the preset's option values and theme colours.
  - Changing it clears the option values set with `SetOption`, so the new figure shows as authored.
- **`SetOption(name, value)` and `GetOption(name)`.** `GetOption` returns the shown value: the app's value, else the preset's, else the default.
- **`BodyColor`**, a bindable `Color?` for the `body` theme slot. It works the way `AccentColor` does for `accent`: by convention, `body` is an avatar's main colour, which is how the Minis are coloured. It goes through `SetThemeColor`, so the app's colours stay above the preset's.
- **Precedence for colours:** the app's (`SetThemeColor`, `BodyColor`, `AccentColor`) over the preset's, over the manifest's.
- **Wrong names fail visibly.**
  - Once a package is loaded, an unknown option, value or preset throws `ArgumentException`, which lists what the avatar has.
  - Before it loads, values are kept. At load, the ones the package does not have are logged as warnings (`Plugin.Maui.Spine.Controls.Avatar`, as the poster warning is) and dropped.
- **Where the logic lives:** in a small internal `AvatarChoices` class (in Core, see Changes), so it can be tested without a view. It merges the values (defaults ← preset ← app), and the theme colours for the current mode (manifest ← preset ← app).

### Surfaces and renderers
- **`AvatarSurface.Options`**: option name → shown value. It is set like `ThemeColors`, replaced as a whole, never changed in place.
- **`SkiaAvatarSurface`** hands options to `ISkiaAvatarRenderer.Options`.
- **`AvatarShaderRenderer`** writes each option's `uniform:` bindings as the value's index. It does this in `Evaluate`, the first time it sees a new dictionary, so no uniform changes while a frame draws, the same way theme colours are applied.
- **spine2d and glb renderers** ignore options. Their `node:` and `material:` bindings are reserved.

### The lab (`samples/SpineAvatarLab`, its own view, surface and renderer copies)
- **`Avatar.View`:** the same `SetOption`, `Preset` and `SetThemeColor` on its `AvatarView`.
  - The surface interface gets default members for options and theme colours.
  - The 64 and 128 pt mirrors use the main view's choices.
  - The lab's `AvatarShaderRenderer` gets theme colours, which it lacks today, and options.
- **`LabPage`:** when the loaded package has them, a preset picker, a picker per option and a body colour field (hex).
- **Harness commands:**
  - `preset <name>|off`
  - `option <name> <value>|off`
  - `color <slot> <#hex>|off`
  - `dump` lists the shown options and preset.

### Tests
- `AvatarChoices`:
  - defaults, then the preset, then app values;
  - clearing on a preset change;
  - unknown names;
  - colours per light and dark with the precedence above.
- The shader renderer: a new options dictionary moves the uniform to the value's index, using the mini-red package with an option from #697's tests.
- The existing 300 tests.

### Docs
- `docs/wiki/avatars.md`: `Preset`, `SetOption`, `BodyColor`.
- The control's README.
- The format doc's options paragraph names the API.
- The plan: M2.1 Done, M2.2 In review.
- The skill's harness command list.

## Open Questions

(none)

## Changes

- **`AvatarChoices`** (Core, internal): merges each option's value (default ← preset ← app) and a preset's colours per mode, and names what is wrong (`CheckOption`, `CheckPreset`). It went to Core, not the control package, because the control's net10.0 build compiles only `Skia/` and `Drivers/`, and it needs no MAUI.
- **`AvatarView`:**
  - New: `Preset`, `BodyColor` (the `body` slot), `SetOption` and `GetOption`.
  - `SetThemeColor` and the choices are applied in `Tick` for the current mode, since a preset names light and dark colours.
  - At load, a preset or option the avatar lacks is logged and dropped. After load, `SetOption` throws with what the avatar has.
- **`AvatarSurface.Options`; `SkiaAvatarSurface`** hands options to `ISkiaAvatarRenderer.Options`. That is a default interface member, so spine2d and glb ignore it.
- **`AvatarShaderRenderer`** (library and lab) writes option uniforms in `Evaluate` when the dictionary changes.
- **The lab:**
  - Its renderer now has theme colours too, with slot names in its model.
  - `IAvatarSurface.SetChoices`.
  - Its `AvatarView` has `Preset`, `SetOption`, `GetOption` and `SetThemeColor`; the 64 and 128 pt mirrors draw the leader's choices.
  - `LabPage` has a "Figure (format 1.1)" card (preset and option chips, body colour), harness commands `preset`, `option` and `color`, and the choices in `dump`.
- **Tests:** `AvatarOptionsTests` adds 4 (the merge, a preset's colours per mode, the messages, the shader following new options between frames). 304 pass.
- **Verified in the lab on the Mac** with a temporary package: mini-red whose option `size` scales the body, with presets `tiny-blue` and `big-green`. It was not committed and was removed from the bundle afterwards. Each step was measured from the capture:
  - the default `medium` is 413 px wide and `huge` 567 px;
  - preset `tiny-blue` gives 340 px in #1C7FF5;
  - `option size large` over it gives 490 px, still blue;
  - preset `big-green` gives 565 px in #0AC16D, with the option cleared;
  - `color body #FF00FF` gives magenta, and `off` gives green again;
  - dark mode gives #2AD884;
  - the mirrors followed.
- **Not checked:** the Figure card itself was not looked at in the window. The harness drives the same calls.
- **Docs:** `docs/wiki/avatars.md` (properties and a "Figures, options and colours" section), the control README, the format doc, the skill's harness commands, the plan (M2.1 Done, M2.2 In progress, §6.1), and #697's changelog (Completed).

## Decisions

- **No preset in `AvatarSource`.** The plan left this open. A view property is enough: one package shown as two figures is two views with different `Preset`, and the parsed package is shared through the source cache either way.
- **`BodyColor` by convention**, not a general bindable collection of slots. `AccentColor` already maps a property to the `accent` slot. One more, for the slot that is an avatar's main colour, covers the Minis without inventing XAML collection syntax. `SetThemeColor` stays for any other slot.
- **A changed `Preset` clears the `SetOption` values but keeps the app's colours.** A preset is a whole figure, while colours set by the app are explicit choices, like `AccentColor`, that should not be lost when the figure changes.
- **An app colour still serves both light and dark.** Deriving a dark tone from a single colour is the shader's job in M2.3 (M-D2), where the Minis' shading is written.
