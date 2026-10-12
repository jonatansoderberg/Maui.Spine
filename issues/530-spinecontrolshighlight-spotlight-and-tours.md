# Issue #530 — Spine.Controls.Highlight: Spotlight and tours

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/530
**Branch:** issue/530-spinecontrolshighlight-spotlight-and-tours
**Status:** Completed

Part of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505), step 3.2 in `docs/plans/ai-and-security.md`. The design is in `docs/proposals/spine-highlight.md` §5, §6.1 (hook 3) and §8, with the owner's answers Q13 (a tap anywhere goes to the next step), Q16 (tips outside Spotlight are announced only) and Q17. It builds on #509 (PR #532): the package, `HighlightState`, keys, and `SpineOverlay` from #508.

## Plan

### 1. Core hook: `SpineTargets` (`Plugin.Maui.Spine`)

`SpineTargets.TryGetFrame(object target, out Rect frame)` (`Core/SpineTargets.cs` + platform parts) returns a target's frame in window coordinates, the coordinates a `SpineOverlay` view lays out in:

- **A `VisualElement`:**
  - Apple: `ConvertRectToView(bounds, null)`.
  - Android: `GetLocationInWindow` divided by the density.
  - Windows: `TransformToVisual(window content)`.
  - False when it is not loaded, hidden or has no size.
- **A `PageAction`:** the `PageActionView` that shows it. `PageActionView` stays internal and keeps a weak list of its live instances; the view whose `Action` is the target and is visible wins. That covers the header bar on every platform and the Windows title bar.
- **A tab, given as the tab page's `Type`:** the item in the tab bar.
  - Apple: the `UITabBar`'s button views, found among its subviews and sorted by x. They are private classes, so iOS 26's floating bar needs a check.
  - Android: `BottomNavigationView.FindViewById(itemId)`.
  - Windows: the `NavigationViewItem` container. Windows is compiled only.
- Nothing about tours in the core.

### 2. Spotlight in `Plugin.Maui.Spine.Controls.Highlight`

**The overlay (`SpotlightView`, a MAUI view shown with `SpineOverlay.Show`):**
- A `GraphicsView` dims the window (black at 60 %) with an even-odd rounded hole round the target, plus `TourStep.Padding`.
- A tip card: the optional title, the tip, "Step 2 of 3", and Skip and Next/Done buttons, all real buttons.
  - It sits below the target when there is room, otherwise above, clamped to the window with a 16-point margin.
  - It follows the theme (`SpineTheme`), and the accent for Next.
- A tap anywhere outside the buttons goes to the next step (Q13); the target's own action does not run.
- Back on Android (`SpineOverlay`'s `back`) and Escape on Windows skip the tour.
- Between steps the hole moves to the next target over 300 ms and the card cross-fades. Under Reduce Motion the hole jumps.
- `Haptic.Selection` plays on each step.
- The dim is out of the accessible tree. The card gets semantic focus, and each step is announced as "Step 2 of 3. Filter by calendar".

**`IHighlights.TourAsync(string key, params IEnumerable<TourStep> steps)`:**
- Returns `AlreadySeen` at once when the key has been seen.
- Each step's target goes through `SpineTargets.TryGetFrame`. A `VisualElement` inside a `ScrollView` that is out of sight is scrolled into view first (`ScrollToAsync(element, MakeVisible)`).
- A step whose target cannot be found is left out, reported in `TourResult.Missing` with its index and reason ("not on screen", "no view for PageAction 'Add'", "no tab for SettingsPage"), and written to the debug log, never dropped silently.
- `Completed` or `Skipped` marks the key seen.
- `Cancelled`: the shown pages change during the tour (`SpineScreen.ShownPagesChanged`, for example a push from a notification) or no step could be shown. The key is not marked.
- One tour at a time: a second `TourAsync` waits for the first.

**Types, as in the proposal §5:**
- `TourStep(object Target, string Tip)` with `Title` and `Padding` (8), and the implicit conversion from `(VisualElement, string)`.
- `TourOutcome { Completed, Skipped, AlreadySeen, Cancelled }`.
- `TourResult(TourOutcome Outcome, IReadOnlyList<(int Step, string Reason)> Missing)`.

**`Highlight.Kind="Spotlight"` on a view:** a one-step tour of that view with `Highlight.Tip` as the tip. It waits for a tap whatever `Repeat` says; `Key` and `IsActive` apply as for the other kinds (owner, 2026-10-12).

**Strings:** `Spine.Highlight.Next`, `.Skip`, `.Done` and `.Step` ("Step {0} of {1}"), in English and Swedish, in `Resources/Strings/`, registered on first use as Shimmer does.

**MAUI-free logic in its own file** (`SpotlightLayout`), linked into the tests:
- the tip's placement (below, above, clamped)
- the hole with padding and corner radius
- the interpolation between two holes

### 3. Tests

`Plugin.Maui.Spine.Controls.Tests`:
- `SpotlightLayout`: below when there is room, above otherwise, clamped at the window's edges, a hole near an edge, the interpolation.
- The step list: missing steps reported with their index.

### 4. Sample (Spine Showcase, Highlight page)

- A **Tour** example: "Take the tour" runs four steps:
  1. a card
  2. a button
  3. a page action in the header bar (the page gets a "Help" `PageAction`)
  4. a step with a view that is not on screen, to show `Missing`
- The result is shown under the button.
- A **Spotlight** example on one view with a key and "Show tips again".
- Tabs: the Showcase has no tabs, so a tab step is checked in a scratch tabbed app (the recipe in the plan), not in the sample.

### 5. Docs and plan

- `docs/wiki/highlight.md`: "Spotlight and tours".
- The `SpineTargets` row in `packages.md`'s hook table.
- The `spine-controls` skill and the package README.
- `docs/plans/ai-and-security.md`:
  - step 3.1 Done (#532) and step 3.2
  - Decisions, the Verification backlog, Last updated
  - Package map

### Verification

- **Builds:** core and Highlight for iOS, Android and Mac Catalyst; Windows compiled on the Mac.
- **iPhone 17 simulator and emulator-5554:**
  - a tour across a card, a button, a page action and a scrolled-away view
  - a tap anywhere, Skip, Next and Done
  - Back on Android
  - a sheet open under a tour (the overlay is above it)
  - the key seen and reset
  - Reduce Motion
  - a recording of a step change for flicker
- **A scratch tabbed app:** a tab as a target, on iOS 26's floating tab bar and Android's bottom bar.
- **Not possible here:** Windows at run time (backlog).

## Open Questions

None. The owner answered on 2026-10-12; see Decisions.

## Changes

- **Core (`Plugin.Maui.Spine`):**
  - `SpineTargets.TryGetFrame(object, out Rect)`: a `VisualElement`, a `PageAction`'s view (`PageActionView` keeps a weak list of its instances and `Showing(action)`), a tab page's `Type` (`SpineTabbedHostPage.TryGetTabFrame`, with the item found per platform: the bar's controls on Apple, one per place, the `BottomNavigationView` item on Android, the `NavigationViewItem` on Windows).
  - `SpineOverlay` on Android: the dialog draws the system bar backgrounds, so an overlay covers the status and navigation bars.
- **`Plugin.Maui.Spine.Controls.Highlight`:**
  - `TourStep`, `TourOutcome`, `TourResult`; `IHighlights.TourAsync(key, params steps)`.
  - `Tours`: one tour at a time, steps checked first and the missing ones reported with a reason and logged, views scrolled into view, ended as `Cancelled` when the shown pages change, key marked on Completed or Skipped.
  - `SpotlightView`: the dim (a `GraphicsView` with an even-odd hole of the target's shape), the card (title, tip, step counter, Skip and Next/Done), a tap anywhere goes on, Back on Android and Escape on Windows skip, the hole moves and the card cross-fades between steps (still under Reduce Motion), `Haptic.Selection` per step, focus and announcement for screen readers.
  - `SpotlightLayout` (MAUI-free): the hole, its radius, the card's place, the easing.
  - `Highlight.Kind="Spotlight"`: a one-step tour of the view.
  - Strings `Spine.Highlight.Next`, `.Skip`, `.Done`, `.Step` in English and Swedish.
- **Tests:** `SpotlightLayoutTests` (8).
- **Sample:** "A tour" and "One spotlight in XAML" on the Highlight page.
- **Docs:** `highlight.md` "Spotlight and tours", the hook table in `packages.md`, the `spine-controls` skill, the package README and description, the proposal's status, the plan.

## Verification

- **Builds:** core and Highlight for iOS, Android and Mac Catalyst; Windows compiled on the Mac. Tests: Controls 80, all green.
- **iPhone 17 simulator (iOS 26.4):**
  - the tour: the card far up scrolled into view, the button, the header's palette action, the hidden view left out ("Completed; left out: step 4 (hidden)")
  - a tap on the dim, Next, Skip and Done
  - the key stored; the XAML spotlight shown, ended by a tap and its key stored
  - a 60 fps recording of the start and a step change: an even fade and move, no flash
  - the hole follows the button's rounding
- **Android emulator-5554 (API 36):** the same tour, the dim over the system bars, a tap anywhere, Back skips.
- **A scratch tabbed app (outside the repo, removed afterwards):** a tab page's `Type` as a target, on iOS 26's floating tab bar and on Android's bottom bar.
- **Not verified:** see the plan's Verification backlog (Windows, screen readers, a sheet under a tour).

## Decisions

- **2026-10-12, owner:** The hole has the target's shape (its corner radius plus the padding), not a circle round every small target.
- **2026-10-12, Claude:** A view shown with `SpineOverlay` has no MAUI parent, so it never gets a `Width` of its own, and MAUI gives it the safe area. The spotlight reads the window's size from its platform view and turns the safe area off.
- **2026-10-12, Claude:** `SpineOverlay`'s Android dialog draws the system bar backgrounds; without it the system drew black bars over the app.
- **2026-10-12, Claude:** iOS 26's floating tab bar has each tab button twice, so tab frames take one control per place.
- **2026-10-12, Claude:** A tab's hole is a capsule on iOS 26 and rounded (16) elsewhere; a header action's is round.
- **2026-10-12, Claude:** Escape skips a tour on Windows only; on Mac Catalyst it would need a key command on a responder in the overlay's window.
- **2026-10-12, Claude:** `MauiAppBuilder` scratch apps on Android need `UseSpine(o => o.Assemblies.Add(...))`: there is no entry assembly to scan. (Test recipe, not a change.)
- **2026-10-12, owner:** `Highlight.Kind="Spotlight"` stays as a kind: a one-step tour of the view that waits for a tap whatever `Repeat` says, since a spotlight covers the screen and must not go away by itself or come back. `Key` and `IsActive` apply.
- **2026-10-12, Claude:** Screen-reader focus goes to the tip card, not the target. The overlay is modal on Apple (`AccessibilityViewIsModal`), so the target under it cannot take focus; the card names it, as the announcement does.
