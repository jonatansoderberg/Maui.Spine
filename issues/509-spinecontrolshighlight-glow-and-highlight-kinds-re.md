# Issue #509 — Spine.Controls.Highlight: glow and highlight kinds, repeat, once-per-install keys and tours

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/509
**Branch:** issue/509-spinecontrolshighlight-glow-and-highlight-kinds-re
**Status:** Completed

Part of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505), step 3.1 in `docs/plans/ai-and-security.md`. The design is in `docs/proposals/spine-highlight.md`, together with the owner's decisions of 2026-10-09 (Q13–Q18). It builds on `SpineOverlay` from #508 (PR #529).

## Plan

### Scope of this PR

The proposal (§9) has eight steps: four core hooks, five per-view kinds on three platforms, Spotlight with tours, and the window edge glow. That is too much to review and verify in one PR. So #509 is split into three issues, with one PR each (owner, 2026-10-12):

| PR | Issue | Contents |
|---|---|---|
| **A (this one)** | #509 | The core hooks that the per-view kinds need, the package, and the five per-view kinds Edge, Pulse, Sheen, Trace and Breathe on Apple, Android and Windows. `Repeat`, `IsActive`, `Key`, `Tip`, `Intensity`, `Colors`, `Outset`, `Ended`. `IHighlights` without tours or `AttachEdge`. A Showcase page. |
| B | [#530](https://github.com/jonatansoderberg/Maui.Spine/issues/530) Spotlight and tours | `HighlightKind.Spotlight`, `IHighlights.TourAsync`, `TourStep`, `TourResult`, and the core hook `SpineTargets.TryGetFrame` (a view, a `PageAction`, a tab) |
| C | [#531](https://github.com/jonatansoderberg/Maui.Spine/issues/531) The window edge glow | `IHighlights.AttachEdge(window)`, `IEdgeGlow` with the per-frame `Intensity`, `SpineOverlay.Show(…, passTouches: true)`, and the iOS corner-radius table (Q18) |

The rest of this plan describes PR A. Plans for B and C go into their own changelogs when they start.

### 1. Core hooks (`Plugin.Maui.Spine`)

- **`ReducedMotion` becomes public** (`Core/ReducedMotion.cs`, Q17):
  - `IsOn` stays, still read at most once a second.
  - A new `Changed` event, raised on the main thread. The listener moves in from Shimmer: the iOS `UIAccessibilityReduceMotionStatusDidChangeNotification`, Android's `ContentObserver` on `AnimatorDurationScale` with the 500 ms settle delay, and nothing on Windows (the setting is read again on each start).
  - Listening starts on the first subscription, and the once-a-second cache is cleared when a change is seen.
  - Shimmer and MeshBackground drop their own `ReduceMotion` copies and use the core's in this PR.
- **One Composition child container per element on Windows** (`Platforms/Windows/ElementVisuals.cs`). It is public, because the core gives packages small public hooks rather than `InternalsVisibleTo` (owner, #508):
  - `ElementVisuals.Add(UIElement, Visual, int order)` and `Remove(...)`.
  - The helper owns the element's child visual, a `ContainerVisual` sized to the element. Tap's press overlay and Highlight's visuals go into it in a fixed order, so neither replaces the other.
  - `Tap.Windows.cs` moves to it first.

`SpineOverlay` is not touched in this PR; `passTouches` comes with PR C.

### 2. The package `Plugin.Maui.Spine.Controls.Highlight`

- `src/Plugin.Maui.Spine.Controls.Highlight/`, namespace `Plugin.Maui.Spine.Controls`, the four MAUI frameworks, a reference to `Plugin.Maui.Spine` only. No SkiaSharp.
- `build/Plugin.Maui.Spine.Controls.Highlight.props` with `<SpineModule Register="Plugin.Maui.Spine.Controls.HighlightExtensions.UseHighlights" />`.
- **Public API**, as in the proposal §5:
  - `HighlightKind` (all seven values; `Spotlight` is declared but logs once that it comes with tours and plays nothing until PR B).
  - `HighlightRepeat` with `Once`, `Forever`, `UntilTapped`, `Times(n)`, and `HighlightRepeatConverter` ("Once", "3", "Forever", "UntilTapped").
  - `Highlight` attached properties: `Kind`, `Repeat`, `IsActive`, `Key`, `Tip`, `Intensity`, `Colors`, `Outset`; the `Ended` event with `HighlightEndedEventArgs` (the view, the kind, and whether it ended by a tap).
  - `IHighlights`: `PlayAsync`, `HasSeen`, `MarkSeen`, `Reset`.
  - `HighlightExtensions.UseHighlights()`, idempotent like `UseMeshBackground`.
- **`HighlightState`** follows the handler as `TapState` does: created when `Kind` is set to something other than `None`, connects to the platform view, stops on handler loss and detach (the Shimmer lesson), disposes when `Kind` goes back to `None`.
- **MAUI-free logic** in its own files so the tests can link them:
  - `HighlightRepeat` and its parsing.
  - `HighlightCycles`: the cycle length per kind (Edge 2.4 s, Pulse 1.6 s, Sheen 1.3 s, Trace and Breathe from the concept page), the default repeat per kind (Edge, Trace and Breathe: `Forever`), and whether a run has ended after n cycles.
  - The default spectrum colours.
- **Keys** in `Preferences` under the shared name `spine.highlight`. A key is marked seen when the highlight ends by itself or by a tap, not when the view goes away mid-play.
- **`UntilTapped`:** `Button.Clicked`, `ImageButton.Clicked`, and for other views a non-consuming recogniser on Apple and a MAUI `TapGestureRecognizer` elsewhere. Whether that coexists with Tap's Android click listener is checked on the emulator; if not, a `Tap.Executed` event in the core is the fallback (proposal §5).
- **Screen readers:** the tip is announced once with `SemanticScreenReader.Announce` when a highlight starts. For Edge, Trace and Breathe it is also the view's semantic hint while they run, and the app's own hint comes back afterwards.
- **Reduced motion:** each kind switches to its still form (proposal §1) and switches back while running on `ReducedMotion.Changed`.
- **Pause** when the view is detached, hidden, or the app is in the background; on iOS the animations are added again in `MovedToWindow` and on `WillEnterForeground`.
- Strings: none needed in PR A (the tour's "Next", "Skip" and "Done" come with B).

### 3. Rendering per platform

**Apple** (`Platforms/Apple` or `*.Apple.cs`): sublayers on the platform view's layer at `ZPosition` 9 000, below Tap's 10 000.
- **Edge:** a conic `CAGradientLayer` in a square layer rotated with `transform.rotation.z`, masked by a `CAShapeLayer` stroking the rounded rect. The soft outer glow is the same gradient masked by a blurred ring image made once per size and corner radius with Core Image.
- **Pulse:** two `CAShapeLayer` rings, scale and fade, staggered.
- **Sheen:** a linear `CAGradientLayer` moving across, masked to the shape.
- **Trace:** a `CAShapeLayer` stroke segment moving with `strokeStart`/`strokeEnd`.
- **Breathe:** `shadowRadius` and `shadowOpacity` with a `shadowPath`.
- Halos draw outside the view, so the docs say a parent that clips cuts them (as `Motion` says).

**Android:** one drawable in the view's `ViewOverlay`, driven by one `ValueAnimator` per highlight that invalidates only the drawable.
- Edge: `SweepGradient` stroke. Sheen: `LinearGradient` clipped to the shape. Trace: `PathMeasure.GetSegment`. Pulse: two rings. Breathe: a halo.
- Outer glow: `BlurMaskFilter` from API 28; below that the rim is drawn without it. `RenderEffect` (API 31) only if `BlurMaskFilter` turns out not to work in the hardware renderer.
- Halos outside the view: try the parent's `ViewGroupOverlay` at the child's offset, as the proposal says; otherwise they are clipped and the docs say so.
- Reduced motion is read from `ReducedMotion`, not left to `ValueAnimator`, which jumps to its end when animations are removed.

**Windows:** Composition visuals in the shared container from §1.
- Edge: a `CompositionSpriteShape` stroked with a `CompositionLinearGradientBrush` whose `RotationAngleInDegrees` is animated. This is close to the others but not a conic gradient.
- Trace: `TrimStart`/`TrimEnd`/`TrimOffset`. Sheen: a gradient brush offset. Pulse: two `ShapeVisual` rings. Breathe: a `DropShadow`.
- Compiled on the Mac only; run-time behaviour goes to the Verification backlog.

### 4. Tests

`tests/Plugin.Maui.Spine.Controls.Tests` links the MAUI-free files: `HighlightRepeat` parsing and the converter strings, `HighlightCycles` (default repeat per kind, end after n cycles, `Forever` never ending, `UntilTapped` ending only on a tap).

### 5. Sample (Spine Showcase)

A **Highlight** page under `Pages/Highlight/`, listed in `MainPage.ViewModel.cs`:
- each kind on a card and on a `Button`
- a repeat picker (Once, 3, Forever, Until tapped)
- an `IsActive` switch for Edge on a card ("Summarising…")
- a `Key` hint with a "Show tips again" button (`IHighlights.Reset()`)
- one highlight on a glass `Material` card and one over a `MeshBackground`
- one in a `CollectionView` template, to see recycling

### 6. Docs and plan

- `docs/wiki/highlight.md`, `README.md` and `docs/wiki/packages.md` (counts, tables, `SpineModule` row, diagram), the package README, the `spine-controls` and `spine-setup` skills, the proposal's status line.
- `ReducedMotion` in the core wiki page where public helpers are listed.
- `docs/plans/ai-and-security.md`:
  - step 1.3 Done (PR #529), phase 1 done, the Package map row for Biometrics
  - step 3.1 In review, with #530 and #531 as steps 3.2 and 3.3, and the Package map row
  - Decisions, Verification backlog, Last updated
- The package icon in `assets/icons/` and its source in `assets/logo-src/`, `Spine.slnx`, `Spine.Packages.slnf`, `Directory.Packages.props` as needed, the sample's `GlobalXmlns.cs` and `.csproj`.

### Verification

- **Builds:** the core, Highlight, Shimmer and MeshBackground for iOS, Android and Mac Catalyst; Windows compiled on the Mac.
- **iPhone 17 simulator:** every kind, each repeat mode, `IsActive` on and off, a key seen once and reset, Reduce Motion switched while running, the app in the background and back (animations still running), a highlight on glass and over a mesh, a recycled list, the tip announced (VoiceOver log or the Accessibility Inspector).
- **Android emulator-5554:** the same, plus "Remove animations" while running, `UntilTapped` on a `Tap.Command` view (the coexistence question above), Shimmer still reacting to Remove animations after its copy is gone.
- **Mac Catalyst:** the Showcase page once.
- Video and frame comparison where a highlight starts, stops or survives a background trip, since a dropped animation only shows in motion.
- **Not possible here:** Windows at run time, Tap on the shared container with Narrator. They go to the Verification backlog.

## Open Questions

None. The owner answered both on 2026-10-12; see Decisions.

## Changes

- **Core (`Plugin.Maui.Spine`):**
  - `ReducedMotion` is public, with `Changed`: a weak event raised on the main thread on iOS, Mac Catalyst and Android, half a second late on Android so the app's own animator scale has followed. The listener moved in from Shimmer.
  - `Tap.Executed`, raised after a tap ran a view's `Tap.Command` (see Decisions).
  - `ElementVisuals` on Windows: one Composition container per element, with an order per visual. `Tap.Windows.cs` adds its press fill to it (`ElementVisuals.TapOrder`).
- **Shimmer and MeshBackground** use the core's `ReducedMotion`; their own copies are gone.
- **`Plugin.Maui.Spine.Controls.Highlight`** (new):
  - `HighlightKind`, `HighlightRepeat` (+ converter), `Highlight` attached properties (`Kind`, `Repeat`, `IsActive`, `Key`, `Tip`, `Intensity`, `Colors` with a comma-separated converter, `Outset`) and `Highlight.Ended`.
  - `HighlightState`: follows the handler, the window (`Stopped`/`Resumed`), the scroll views and lists around the view, and `SpineScreen.ShownPages`; a `HighlightClock` counts only time on screen; `UntilTapped` through `Clicked`, `Tap.Executed`, a native recogniser on Apple or a MAUI one elsewhere.
  - Renderers: Core Animation layers on Apple, one drawable in the parent's overlay on Android (`TimeAnimator`), Composition visuals in `ElementVisuals` on Windows. Each kind has a still form for Reduce Motion. A new look while running keeps the phase and does not fade in again.
  - Keys in `Preferences` under `spine.highlight`; `IHighlights` (`PlayAsync`, `HasSeen`, `MarkSeen`, `Reset`), `UseHighlights()`, `<SpineModule>`.
  - Package icon (`assets/icons/highlight.png`, source in `assets/logo-src/`).
- **Tests:** `HighlightTimingTests` in `Plugin.Maui.Spine.Controls.Tests` (repeat parsing, defaults per kind, run length, the paused clock).
- **Sample:** a Highlight page in Spine Showcase.
- **Docs:** `docs/wiki/highlight.md`; README, `packages.md` (count, tables, diagram, the core's hooks for packages), `rows.md` (`Tap.Executed`), the package README, the `spine-controls` and `spine-setup` skills, the proposal's status line, the plan.
- **Issues:** #530 (Spotlight and tours) and #531 (the window edge glow) created.

## Verification

- **Builds:** core, Highlight, Shimmer and MeshBackground for iOS, Android and Mac Catalyst; Windows compiled on the Mac. Tests: Core 99, Controls 72, Biometrics 19, all green.
- **iPhone 17 simulator (iOS 26.4):**
  - every kind drawn; Edge over Liquid Glass and a mesh; Trace in a recycled list
  - Summarise: Edge on while busy, off after; a recording shows a three-frame fade in and a five-frame fade out, no dip
  - three look changes mid-run (intensity, outset) recorded at 60 fps: no dip in the rim's colour
  - a key: the hint pauses while scrolled away, a tap ends it and stores it, `Reset` plays it again, a relaunch keeps it seen
  - `UntilTapped` on a Border without a tap handler; Tap.Command on a list row still runs with Trace on it
  - `PlayAsync`: Sheen twice completed after 3.01 s
  - Reduce Motion on and off while running: still forms and back
  - background and back: the animations run again
- **Android emulator-5554 (Pixel 10 Pro, API 36):**
  - every kind drawn, with halos outside the view after the move to the parent's overlay
  - `UntilTapped` on a Tap.Command row: the highlight ends and the command runs (it did not before `Tap.Executed`)
  - `UntilTapped` on a Border (MAUI recogniser) and on a Button with a key
  - Remove animations on and off while running: still forms and back; Shimmer stops and restarts its wave through the core's `ReducedMotion`
  - background and back: animating
- **The owner's iPhone 16 Pro:** Edge over Liquid Glass looks right (2026-10-12).
- **Not verified:** see the plan's Verification backlog (Windows at run time, screen readers, Android before API 31, Mac Catalyst and an iPhone, a longer Android recording).

## Decisions

- **2026-10-12, owner:** #509 is split into three issues and PRs: #509 (core hooks, package, the five per-view kinds), #530 (Spotlight and tours) and #531 (the window edge glow with `passTouches`).
- **2026-10-12, owner:** Shimmer and MeshBackground move onto the public `ReducedMotion` in this PR.
- **2026-10-12, Claude:** `Tap.Executed` is in the core, the proposal's fallback hook. On Android a MAUI `TapGestureRecognizer` on a view with `Tap.Command` took the tap: the highlight ended, the row's command did not run.
- **2026-10-12, Claude:** On Android the drawable goes in the parent's overlay at the view's place. In the view's own overlay a `Border` clipped the glow and the rings to its shape.
- **2026-10-12, Claude:** A run's time counts only while the view can be seen: loaded, visible, in the window, on a shown page, the app in front. The first run played hints out below the fold; a key would have counted as seen with nobody looking. A view that is ready but out of the window is looked at again every 250 ms for two seconds, since nothing reports when a sliding page arrives; after that, scrolling and `ShownPagesChanged` report it.
- **2026-10-12, Claude:** A redraw for a new look keeps the phase (Apple: animations anchored to the first show's media time; Android: the time since the first show) and skips the fade.
- **2026-10-12, Claude:** Trace's head is the kind's colour; white read as a gap on a light card.
- **2026-10-12, Claude:** On Windows, Breathe's halo is the shadow of a thin ring just outside the view, and Edge's glow a one-colour shadow of the rim, as the proposal expected without Win2D.
- **2026-10-12, Claude:** `passTouches` on Android must not use `HoldUnder`: an edge glow that takes no touches should not hide a sheet that opens under it. A pass-through overlay is shown again above the later sheet instead; a frame without the glow does no harm. `HoldUnder` stays as it is for overlays that take touches. (For PR C.)
