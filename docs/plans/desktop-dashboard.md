# Plan: what a Mac dashboard app taught Spine

**Started:** 2026-10-20
**Last updated:** 2026-10-23 (step 8: a busy action no longer spins unless `SpinsWhenBusy` is set; steps 1–9 merged in #646–#649 and released in 0.1.16, except Charts, which was missing from `Spine.Packages.slnf` and ships in 0.1.17)

AIDash Connect (a Mac Catalyst menu-bar dashboard on Spine) grew its own window, layout, chart and widget code while
it was built. This plan moves what any Spine app would want into Spine, and leaves the app only what is its own.

Stacked on #646 (backdrop), #647 (one-row title bar, several trailing actions, window sizes) and #648 (Reorder on
bindable layouts). The app is built against local packages (`artifacts/local`, version `0.1.16-dev.N`) until a release.

Status values: **Proposal**, **Ready**, **In progress**, **In review**, **Done**, **Parked**.

## Steps

| # | Step | From the app | To Spine | Status |
|---|---|---|---|---|
| 1 | Window colours and backdrop | `Platforms/MacCatalyst/MacWindow.cs` (title bar, NSVisualEffectView, NSAppearance, tinted root), the translucent page style in `App.xaml.cs` | `options.MacOS.Backdrop` (#646) and `UnifiedTitleBar` (#647); nothing new | Done |
| 2 | Margins: scroll only when needed | `Controls/FitScrollView.cs` | A page's scroll view under the header bar scrolls only when its content is taller than the room under the bar | Done |
| 3 | Margins: content under a one-row title bar | `BodyMargin` in the app's view model (−6 pt) | With `UnifiedTitleBar` the content starts where the title bar row ends, so the space above the title equals the space below it | Done |
| 4 | Window sizes from the content | `MainPage.Chrome()` and the height sums in `SuggestWindowSizes` | `ViewModelBase.ContentHeight(width)`: the window height that shows the page's content at a width, measured by Spine | Done |
| 5 | A masonry of cards | `Controls/MasonryLayout.cs` | `MasonryLayout` in the core, next to `Reorder` for layouts (#648) | Done |
| 6 | Charts | `Controls/RingView.cs`, `BarView.cs`, `DayBarsView.cs` | New package `Plugin.Maui.Spine.Controls.Charts`: `RingChart`, `BarChart` (one value), `DayBars` (values per day) | Done |
| 7 | Widgets: rings and alignment | `RingPictures` (rings stored as PNGs) and the invisible strut images that centre a ring tile | `W.Ring(value, color)` drawn natively; `VStack(...).Centered()`, `HStack(...).Evenly()` | Done |
| 8 | Busy header action | `Platforms/MacCatalyst/HeaderSpinner.cs` (reflection into `PageActionView`) | `PageAction.IsBusy` (#644) with `SpinsWhenBusy = true`, opt-in since the spin stopped being the default | Done |
| 9 | Checked context menu items | `Controls/CheckMenuItem.cs` and its `UIAction.State` mapper | Spine's `ContextMenu` with `MenuAction.IsChecked`; nothing new | Done |

What stays in the app: the providers and their sign-ins, the device protocol, `Presentation` (how a value is
written), the widget content and the app's own page layout.

## Decisions

- One branch and one PR for every step: they were verified together in one app, and the widget renderers' change is
  small (a ring node and two stack options).
- Charts are a package of their own, as the other controls are; the core keeps layouts and window behaviour.

## Verification backlog

- Windows and Android for steps 2–4 (the shared code changes; the behaviour is written for Apple first).
- Android widgets for step 7: `W.Ring` as a bitmap, `CenterItems` as gravity, `Evenly` as spacers. Built, not run.
- macOS widgets for step 7 on the desktop: built into the app's extension, not looked at.
- An unscaled ("Optimize for Mac") Catalyst app for steps 3–4.
