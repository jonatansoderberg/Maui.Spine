# Issue #722 — Showcase: the Minis and their figures

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/722
**Branch:** issue/722-showcase-the-minis-and-their-figures
**Status:** In Progress

The owner asked for the Mini presets in the Showcase, and for Mini Blue as the chat's avatar by default (2026-10-29). This follows the Minis v2 plan, which is done ([docs/plans/spine-avatar-mini-v2.md](../docs/plans/spine-avatar-mini-v2.md)).

## Plan

- **The gallery:** a "Mini" card, with `mini.spineavatar` bundled.
- **The avatar's page:** for a package with figures, the figures (tap to show), a picker per option, colour swatches and the code.
- **The voice pages** offer the Mini figures, and the chat starts with Mini Blue.

## Open Questions

(none)

## Changes

- `MauiSpineSampleApp.csproj`: `mini.spineavatar` is a MauiAsset.
- **Gallery:** a Mini card ("nine small figures in one package of 33 KB…"), with the package's poster. The page summary counts five bundled avatars.
- **`AvatarDetailPage`**, under the big avatar for a package with presets or options:
  - the figure's name;
  - every figure as a 56 pt live avatar (a tap shows it, in its own colour);
  - a picker per option, which follows the figure;
  - eight colour swatches for `BodyColor` and an "Own colour" button;
  - a line on what it shows.
  - The example's code shows `Preset`, `BodyColor` and `SetOption`.
  - The view model builds the choices on `AvatarLoaded` from the manifest's presets and options (`FigureChoice`, `OptionChoice`, `SwatchChoice`).
- **`VoiceAvatars`:** the nine Mini figures (`AvatarChoice` gains `Preset`), Mini Blue first. The chat and the assistant pass `Avatar.Preset` to their `AvatarView`.
- **The chat** starts with Mini Blue, and its avatar menu describes a Mini figure.

## Verification

On the Mac (Mac Catalyst, signed), driven by a scratch harness:
- The gallery shows the Mini card.
- **Mini's page:**
  - shows the nine figures live, all loaded;
  - preset red gives cube with tall eyes, and the pickers follow;
  - shape drop gives a red drop;
  - the teal swatch recolours it;
  - preset blue gives a blue marshmallow with round eyes, in its own colour.
- **The chat** opens with Mini Blue (`mini:blue:Loaded`).

## Decisions

- **The figures sit under the big avatar** in the first example, not in an example of their own further down, so a tap shows its effect where it is seen.
- **A tapped figure shows in its own colour.** The page clears a swatch colour when a figure is chosen. The view keeps the app's colour over every figure, as `BodyColor` is documented to; the sample chooses to reset it.
- **The pickers follow the figure on the next dispatch.** `MainThread.BeginInvokeOnMainThread` runs at once on the main thread, before the view has taken the new preset, so the pickers lagged one figure behind.
- **`VoiceAvatars` declares the Mini fields before the list that reads them.** Static fields start in the order they are written, and the list met them as null (a `TypeInitializationException` on the chat).
- **The worktree's Mac build needs the git-ignored `MauiSpineSampleApp.csproj.user`.** Without its signing identity the app is ad hoc signed, macOS denies it the team's App Group, and the widget code throws at launch. This was not changed here.
