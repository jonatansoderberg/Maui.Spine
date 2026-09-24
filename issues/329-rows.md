# Issue #329 — Plugin.Maui.Spine.Controls.Rows: Tap.Command, merged semantics and a SpineRow

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/329
**Branch:** issue/329-rows
**Status:** In Progress
**Stage:** 3 of the app-review plan (#333)

## Plan

Three pieces, split over two packages:

- **`Tap` (core, `Plugin.Maui.Spine.Extensions`)**: attached `Tap.Command`, `Tap.CommandParameter` and `Tap.HighlightColor` on any view. Whole-area hit test and native press feedback per platform, attached from the view's `HandlerChanged` so no builder call is needed:
  - iOS / Mac Catalyst: a `UIGestureRecognizer` on the platform view that recognises together with scroll views, fails on movement, ignores touches that start on a `UIControl` (a switch in the row) or inside a nested tap target, and shows a `systemFill` overlay layer after a short delay (so a scroll does not flash rows); a hover recognizer draws a lighter overlay on Mac Catalyst and iPad pointers.
  - Android: `Clickable` plus a click listener and a bounded `RippleDrawable` as the view's foreground (corner radius taken from a `Border`'s `RoundRectangle`), so press state, scroll-container delay and TalkBack activation are the platform's own.
  - Windows: `Tapped` plus pointer states driving a composition overlay (hover and pressed fills).
  - The command runs only when the view is enabled and `CanExecute` is true; `CanExecuteChanged` updates the platform state.
- **`Semantic.Merge` (core)**: children leave the accessibility tree (`AutomationProperties.IsInAccessibleTree=false`), their texts (a child's own `SemanticProperties.Description`, else a `Label`'s text) join into the container's `SemanticProperties.Description`, kept up to date as texts and children change. A `Switch` inside makes the merged element a toggle (VoiceOver toggle-button trait with its value, TalkBack checkable). A merged view with `Tap.Command` reads as a button.
- **`SpineRow` (new package `Plugin.Maui.Spine.Controls.Rows`)**: `Icon` (SVG), `Title`, `Detail` (optionally the `AnimatedLabel` marquee), `Value`, `Accessory` (any view: a switch, a button), a chevron, `Command`/`CommandParameter`. Merged semantics always on; a row whose accessory is a `Switch` and has no command toggles the switch when tapped. Colours through `SpineRowStyleOptions` (`SpineStyleOptions`) repainted with `SpineTheme.Track`. The chevron is the header bar's own back glyph (`arrowleft.svg` in the core), mirrored.
- **Registration (#355)**: none. `Tap`/`Semantics` attach from `HandlerChanged`; the Rows package has no strings.
- **Sample**: `Pages/Rows/RowsPage` (settings rows with icon, detail, value, switch, chevron, disabled row; key/value rows; a card with `Tap.Command`; a merged custom row; code). `ContextItem` rebuilt on `Tap.Command` + `Semantic.Merge`. Push sample's label+switch grids become `SpineRow`s.
- **Docs**: `docs/wiki/rows.md`, packages tables, `/spine-controls` skill, package icon.

## Open Questions

None.

## Changes

- Core: `Tap` (`Command`, `CommandParameter`, `HighlightColor`) with `TapState` following the view's handler; iOS/Mac Catalyst `PressRecognizer` + hover recognizer + overlay `CALayer` (`Tap.Apple.cs`), Android click listener + bounded `RippleDrawable` foreground (`Tap.Android.cs`), Windows `Tapped` + pointer states + composition child visual (`Tap.Windows.cs`).
- Core: `Semantic.Merge` (`Semantic.cs`): hides descendants (`AutomationProperties.IsInAccessibleTree=false`, restored when merge is turned off), writes the joined text as `SemanticProperties.Description`, follows text/visibility/toggle changes and added or removed children. Platform traits: iOS `IsAccessibilityElement`, Button, ToggleButton (iOS 17, read from UIKit with `Dlfcn`, not bound), NotEnabled, value "1"/"0"; Android screen-reader focusable, an `AccessibilityDelegateCompat` that wraps MAUI's and reports `android.widget.Button` / `android.widget.Switch` (checkable, checked) and disabled.
- A tap target whose command cannot execute reads as not enabled (iOS NotEnabled trait, Android disabled node) and takes no press (Android `Clickable=false`).
- New package `Plugin.Maui.Spine.Controls.Rows`: `SpineRow` (`Icon`, `Title`, `Detail`, `DetailMarquee`, `Value`, `Accessory`, `ShowChevron`, `Command`, `CommandParameter`, `StyleOptions`) and `SpineRowStyleOptions` on `SpineStyleOptions<T>`; README, icon `assets/icons/rows.png` (source `assets/logo-src/rows.png`).
- Sample: `Pages/Rows/RowsPage` ("Rows" in the index), `ContextItem` on `Tap.Command` + `Semantic.Merge` (its `TapCommand`/`TapCommandParameter` properties and `TapGestureRecognizer` removed; the marquee description carries a `SemanticProperties.Description`). Push sample: the seven label + switch grids on `TagsPage` and `SendPage` are `SpineRow`s, with a `DefaultSpineRowStyleOptions` (no side padding) in `App.xaml`.
- Docs: `docs/wiki/rows.md`, README and `docs/wiki/packages.md` rows (twelve packages, registration table), `agent-skills.md`, `/spine-controls` skill; `Spine.slnx`, `Spine.Packages.slnf`.

## Decisions

- **`Semantic.Merge`, not `Semantics.Merge`.** `Microsoft.Maui.Semantics` is a public class; `Semantics` made `SpineRow.cs` fail with CS0104 and would do the same in every app file that imports `Plugin.Maui.Spine.Extensions` next to MAUI's implicit usings.
- **Package split as planned**: `Tap` and `Semantic` in the core (they need per-platform code and are useful without rows), `SpineRow` in `Plugin.Maui.Spine.Controls.Rows`, which also references AnimatedLabel for the marquee detail.
- **No registration (#355)**: everything attaches from `HandlerChanged`; the Rows package has no strings (toggle state is spoken by the platform: VoiceOver's toggle-button trait with "1"/"0", TalkBack's checkable node), so it needs neither a module nor a static constructor.
- **iOS press is a custom recogniser, not UIKit's cell highlight**: it recognises simultaneously (scroll views keep scrolling), fails after 10 points of movement, ignores touches that start on a `UIControl` or inside a nested tap target, and highlights after 70 ms so a flick does not flash rows; a quick tap still flashes it. Colour `systemFill` (hover `quaternarySystemFill`), a neutral fill rather than the accent, as UIKit lists do.
- **Android uses the platform's click and ripple** so press delay in scroll containers, keyboard/TalkBack activation and the ripple colour are native; the mask follows a `Border`'s `RoundRectangle`.
- **A switch row without a command toggles on row tap**: that is what TalkBack/VoiceOver activation of the merged row must do, and matches Android settings.
- **Accessibility of disabled commands**: `CanExecute == false` reads as not enabled rather than setting `IsEnabled` (which would fight the app's own value).
- **iOS `CollectionView` clears the button trait**: MAUI's cell binding (`UpdateAccessibilityTraits` for `SelectionMode.None`) removes it after our handler ran; the trait is re-applied on the next main-loop turn when the view sits in a `UICollectionViewCell`.
- **Chevron = the header bar's `arrowleft.svg`**, rotated 180° (0° in right-to-left), tinted with the tertiary label colour. Shared shape, no font glyph, no second asset.
- **Colours** through `SpineRowStyleOptions` (`SpineTheme.Track` repaint): title in the label colour, detail/value secondary, chevron tertiary, icon `SpineTheme.GetAccent` (Transparent keeps SVG colours). Sizes follow each platform's list rows.
- **No separators or background in `SpineRow`**: groups/cards are the app's; the sample draws them with `Border` + `BoxView`.
- **Windows**: taps and hover/pressed fills (composition child visual) compile in CI but were not run; no button role is added for Narrator.
