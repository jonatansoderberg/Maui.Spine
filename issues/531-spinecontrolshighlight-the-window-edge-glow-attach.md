# Issue #531 — Spine.Controls.Highlight: the window edge glow (AttachEdge)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/531
**Branch:** issue/531-spinecontrolshighlight-the-window-edge-glow-attach
**Status:** Completed

Part of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505), step 3.3 in `docs/plans/ai-and-security.md`. The design is in `docs/proposals/spine-highlight.md` §4, §6.1 (hook 2) and §7, with the owner's answer Q18 (a table of corner radii per device class on iOS). It builds on #509 (PR #532, the Edge renderer) and #530 (PR #533).

## Plan

### 1. Core: `SpineOverlay.Show(content, back, passTouches)`

A new optional `passTouches` parameter (default `false`) makes the overlay a decoration that takes no touches and no focus.

| Platform | Pass-through overlay | A sheet that opens later |
|---|---|---|
| Apple | `UserInteractionEnabled = false`; not `AccessibilityViewIsModal`, no `ScreenChanged`; hidden from accessibility | Raised above it, as today |
| Android | The dialog's window gets `FLAG_NOT_TOUCHABLE` and `FLAG_NOT_FOCUSABLE` | Not held under (`HoldUnder` only counts overlays that take touches). The pass-through dialog is shown again above the sheet; a frame without a glow does no harm (decision of 2026-10-12) |
| Windows | The popup's host is not hit-test visible | Raised above it, as today |

On Android the sheet code calls `SpineOverlay.OnSheetShown(window)` instead of `HoldUnder`, so both rules live in `SpineOverlay`.

### 2. `IHighlights.AttachEdge(Window window)` returns `IEdgeGlow`

**The view:** an `EdgeGlowView`, shown with `passTouches: true`, filling the window with the safe area off.
- It carries the Edge kind through the per-view renderer from #509, with the display's corner radius and `Outset` 24. The rim lies just inside the screen edge, and the outer half of the glow falls off screen.

**The display's corner radius** (`DisplayCorners`):
- **iOS:** Q18's table by screen size in points. Home-button sizes get 0; the notched sizes get the published radii (39, 41.5, 44, 47.33, 53.33, 55, 62). An unknown size with a home indicator gets 55, else 0. iPad with rounded corners gets 18.
- **Mac Catalyst:** 0. The window's own corners are drawn by macOS.
- **Android 12+:** `WindowInsets.GetRoundedCorner(top left)`, else 0.
- **Windows:** 0. A window is square at the edge Composition draws to; the rounding of Windows 11 windows is outside the content.
- The table lives in a MAUI-free file, linked into the tests.

**`IEdgeGlow`:**
- `Intensity`: a `Func<float>?`.
  - `null` runs Highlight's own Edge animation, the "model is working" look over the whole window.
  - With a function, a per-frame clock calls it on the main thread and eases the value (`level += (target − level) × 0.25` per frame, as on the concept page). Then it sets the rim width, the glow's opacity and the overall opacity, all without rebuilding layers.
  - The clock is `CADisplayLink` on Apple, `Choreographer` on Android and `CompositionTarget.Rendering` on Windows. These are not MAUI's ticker, which stops when Android's animations are removed, and the level must still show then.
  - An exception from the function stops the glow and is written to the debug log with the exception, not swallowed.
- `Colors`: the spectrum by default.
- `Dispose`: fades out over 250 ms and removes the overlay.

**Reduce Motion:** the colours stand still, as Edge's still form does, and the level drives only the opacity.

**Renderer additions:** each platform's `HighlightRenderer` gets `SetLevel(double level)`.
- **Apple:** the rim mask's `lineWidth` and the glow layer's opacity, inside a transaction without actions.
- **Android:** fields on the drawable plus `InvalidateSelf`.
- **Windows:** the stroke thickness and the shadow opacity.

### 3. Tests

`Plugin.Maui.Spine.Controls.Tests`:
- `DisplayCorners`: known sizes, home-button sizes, an unknown size with and without a home indicator.
- `EdgeLevel`: easing towards the target, clamping to 0–1, and a non-finite value treated as 0.

### 4. Sample (Highlight page)

"The window's edge":
- "Model working" attaches the glow with no intensity.
- "Speaking" drives it from a simulated voice level, the concept page's syllable noise.
- "Stop" disposes it.
- A sheet can be opened while it runs, to see the glow stay above the sheet and the sheet still take touches.

### 5. Docs and plan

- `highlight.md`: "The window's edge", with the one-line wiring for Spine.Voice: `edge.Intensity = () => voice.Frame.OutputLevel;`.
- The `SpineOverlay` row in `guards.md` and `packages.md` (`passTouches`).
- The skill and the README.
- The plan:
  - step 3.2 Done (#533) and step 3.3
  - Decisions, the Verification backlog, Last updated
  - Package map: Highlight done

### Verification

- **Builds:** all four frameworks; Windows compiled on the Mac.
- **iPhone 17 simulator:**
  - both modes
  - the corners checked against `simctl io screenshot --mask=black`, whose mask is the device's own corner
  - touches pass through to the page
  - a sheet opened under the glow
  - Reduce Motion
  - a recording for flicker when attaching, disposing and opening a sheet
- **Android emulator:** the same, plus the rounded-corner insets, and the re-show above a sheet recorded frame by frame.
- **Not possible here:** Windows at run time, and other iPhone sizes than the 17's. These go to the backlog.

## Open Questions

None. Q18 (2026-10-09) settles the iOS corners, and the Android sheet rule was decided on 2026-10-12.

## Changes

- **Core:**
  - `SpineOverlay.Show(content, back, passTouches)`.
    - **Apple:** no interaction, not modal, hidden from accessibility.
    - **Android:** `FLAG_NOT_TOUCHABLE` and `FLAG_NOT_FOCUSABLE`. `OnSheetShown` replaces `HoldUnder`: a later sheet is held back only while an overlay that takes touches shows; pass-through overlays are raised above the sheet by a new dialog.
    - **Windows:** a host that is not hit-test visible.
- **`Plugin.Maui.Spine.Controls.Highlight`:**
  - `IHighlights.AttachEdge(Window)` and `IEdgeGlow` (`Intensity`, `Colors`, `Dispose`).
  - `EdgeGlow`: a `Border` the window's size with Edge on it, `Outset` 36, shown with `passTouches`.
  - `DisplayCorners`: the iPhone and iPad table; Android's rounded-corner insets.
  - `EdgeLevel`: easing at 0.25 per frame, clamped, NaN as 0.
  - `FrameClock` per platform.
  - `SetLevel` on each renderer and on `HighlightState`.
  - Edge's rim and glow scale with `Outset` above 12.
  - A rebuild mid fade-in continues the fade.
  - A highlight on a view outside any Spine page (an overlay) counts as shown.
- **Tests:** `EdgeGlowTests` (9).
- **Sample:** "The window's edge" on the Highlight page: Model working, Speaking (a simulated voice level), Stop, Open a sheet.
- **Docs:** `highlight.md` "The window's edge", `passTouches` in `guards.md` and `packages.md`, the skill, the README, the package description, the proposal's status, the plan.

## Verification

- **Builds:** core and Highlight for iOS, Android and Mac Catalyst; Windows compiled on the Mac. Tests: Controls 89.
- **iPhone 17 simulator:**
  - both modes
  - the rim follows the device's corner, against a masked screenshot
  - a tap through the glow opens a sheet; the sheet's checkbox takes taps under the glow
  - Reduce Motion: the colours stay put and the strength follows the level
  - recordings: a nine-frame fade in and a fifteen-frame fade out, no jump after the fix
  - the bolder edge after the owner found it subtle
- **Android emulator (API 36):**
  - the edge with the emulator's rounded corners
  - the overlay window is `NOT_TOUCHABLE` and lies above a later sheet
  - the glow is out for eight frames (0.27 s) while the sheet opens
- **Not verified:** see the plan's Verification backlog.

## Decisions

- **2026-10-12, owner feedback:** the window's edge was too subtle. Edge's rim and glow now scale with `Outset` above 12, and the window uses 36; a view's default Edge is unchanged.
- **2026-10-12, Claude:** On Android raising the pass-through overlay above a sheet leaves the glow out for about 0.27 s (measured): the view leaves one dialog for a new one. Accepted, as decided for #531 before.
- **2026-10-12, Claude:** A rebuild mid fade-in continues the fade, after a recording showed the edge jump in at full strength.
- **2026-10-12, Claude:** A highlight on a view outside any Spine page counts as shown, so Edge works on an overlay.
- **2026-10-12, Claude:** The level is sampled by a platform frame clock, not MAUI's animation ticker. **Why:** MAUI's Android ticker stops when animations are removed, and under Reduce Motion the level must still show, as opacity.
- **2026-10-12, Claude:** The window edge reuses the per-view Edge renderer on a view that fills the window, with `Outset` 24. **Why:** the rim then lies inside the screen's edge and the outer half of the glow is off screen, which is the concept's screen-edge look, with no second renderer.
