# Issue #335 — Plugin.Maui.Spine.Controls.DataGrid: responsive row grid with layouts, sorting, grouping and swipe actions

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/335
**Branch:** issue/335-data-grid
**Status:** Completed
**Stage:** 3 of the app-review plan (#333)

## Plan

Port a production-tested row-list grid (built on `CollectionView`, `RefreshView`, `SwipeView`, `Grid`, `Label`) into its own package, `Plugin.Maui.Spine.Controls.DataGrid`, namespace `Plugin.Maui.Spine.Controls`.

### Package
- `src/Plugin.Maui.Spine.Controls.DataGrid`, same shape as the AnimatedLabel package, references `Plugin.Maui.Spine` (for `SpineStyleOptions<T>`, `SpineTheme.Track`, and through it `.Common` for `SpineStrings` and `.Svg` for swipe-action icons).
- Types: `DataGrid` (partial: core, rendering, auto-fit, header tooltip, cell press), `DataGridColumn`, `DataGridLayout`, `DataGridCellPlacement`, `DataGridGroup`, `DataGridSwipeAction`, `DataGridSortDescriptor`, `DataGridStyleOptions`, enums `DataGridColumnType`, `DataGridHeaderMode`, `DataGridSortDirection`, `DataGridUngroupedMode`; internal property-path getter and format converter.
- The embedded `strings.xml` / `strings.sv.xml` (prefix `DataGrid.`) register from `DataGrid`'s static constructor; no builder call (#355).

### Adapting
- **Defaults**: theme-aware colours (neutral greys, system blue accent) through `DataGridStyleOptions : SpineStyleOptions<DataGridStyleOptions>`, nullable colours filled by `ApplyThemeDefaults`; platform font (`FontFamily = null`), bold headers via `FontAttributes`; sort carets and group chevrons are small built-in `Path` geometries stroked in the theme colour, so no font or icon package is needed. Glyph cells keep the column's `FontFamily`. Swipe actions take an `IconSvg` (Spine's `SvgImageSource`) or a glyph + font.
- **Repaint**: `SpineTheme.Track(this, OnThemeChanged)` — rebuilds the template, header, status row and empty view, and restores the scroll position.
- **Text** from `SpineStrings.Current`: loading, empty, load-more status, loaded status, copied confirmation, sort and expand/collapse accessibility text, ungrouped group title. An explicit property on the grid still wins.
- **Cell copy / row taps**: try one `PointerGestureRecognizer` per row that times the press itself and dispatches tap (row, link cell, checkbox cell) and long-press (copy) by hit-testing the cells. If the platforms deliver what it needs, copy can be on by default; otherwise keep the source's two recognizers and make copy opt-in.
- **Trimming**: check the reflection-based bindings and the property-path getter against the trim analyzer and a Release iOS build; annotate or document.
- **Auto width** measurement: Android `TextPaint`, iOS and Mac Catalyst `NSString` sizing with the `UIFont` from `IFontManager`, Windows a measured `TextBlock`.
- Drop the app-specific parts: the source's work-item history, the brand palette and fonts, the `sv-SE` price culture default (current culture instead), the diagnostics hook becomes `Debug.WriteLine`.

### Sample and docs
- `Pages/DataGrid/DataGridPage` with Wide/Narrow layouts switched by an `AdaptiveTrigger`, sortable headers, grouping toggle, swipe actions, load more, pull-to-refresh, a link cell and a checkbox cell, with code examples; index row.
- `docs/wiki/data-grid.md` (from the source's documentation minus app history, with the known limitations), README packages table and docs table, `docs/wiki/packages.md`, `/spine-controls` skill, `Spine.slnx`, `Spine.Packages.slnf`, package icon.

## Open Questions

None.

## Changes

- New package `src/Plugin.Maui.Spine.Controls.DataGrid` (references `Plugin.Maui.Spine`): `DataGrid` in five partial files (core and data flow, rendering, press handling, bubble, auto-fit), `DataGridColumn`, `DataGridLayout`, `DataGridCellPlacement`, `DataGridGroup`, `DataGridSwipeAction` (with `IconSvg`), `DataGridSortDescriptor` (a record), `DataGridStyleOptions : SpineStyleOptions<DataGridStyleOptions>`, the enums, an internal `PropertyPathGetter` and `DataGridFormatConverter`; `strings.xml` + `strings.sv.xml` with 15 `DataGrid.*` keys; README; package icon `assets/icons/data-grid.png` (source `assets/logo-src/data-grid.png`, composed from the controls icon with a table glyph).
- Ported behaviour unchanged where it was app-neutral: layouts and header modes, header wrapping rule, Material row heights, content-fit `Auto` columns and `NeverTruncate`, append/removal detection with scroll preservation, grouping as a display-list transform, source-subscription parking while detached, width-change rebuild, fresh-list measurement in the same turn.
- Changed: theme-aware default colours and the platform font (bold headers via `FontAttributes`); sort caret and group chevron drawn as `Path`s in the header colour; texts from `SpineStrings` (loading, empty, status, load more, copied, screen-reader descriptions, ungrouped title); `SpineTheme.Track` repaints header, rows, status row and empty view and restores the scroll position; `Date` defaults to `"d"` and `Price` formats with the current culture (the source defaulted to a fixed culture); diagnostics hook replaced by `Debug.WriteLine`; stable local sort that reads each value once; a "Load more" link in the status row; typed (source-generated) bindings for the grid's own types.
- New: `IsCellCopyEnabled` (instance, default on), a built-in "Copied" bubble when the app sets no `DataGrid.CellCopied` hook (skipped on Android 13+, which shows its own), `SortIndicatorSize`, group header hairline separator, `SemanticProperties` on sortable headers and group chevrons.
- Auto-width measurement: Android `TextPaint`, iOS and Mac Catalyst `NSString` sizing (`IOS || MACCATALYST`), Windows a measured WinUI `TextBlock`; the font weight follows `FontAttributes`.
- Fixes found on device: the `CollectionView` got its `ItemsSource` before the first template, so iOS realised every row with the default template, scrolled and fired load more at once; the source is now assigned together with the first template. Turning grouping on bound group headers to the row template for a frame and a typed swipe command threw (`RelayCommand<Product>` got a `DataGridGroup`): the template selector is now swapped before the grouped list is shown. An SVG on a `SwipeItemView` rendered at a third of its size on iOS through `SvgImageSource` (the swipe item is measured natively); swipe icons are rendered once at their size with `SvgBitmapLoader`.
- Sample: `Pages/DataGrid/DataGridPage` (Wide/Narrow via `AdaptiveTrigger` at 640, seven columns, sorting, grouping switch, link and checkbox columns, swipe Star/Delete, load more in pages of 30, pull-to-refresh, a theme toggle page action, code examples behind a switch); index row with `wordclock.svg`; csproj, `GlobalXmlns.cs`, project reference.
- Docs: `docs/wiki/data-grid.md` (with two screenshots), README packages and docs tables, `docs/wiki/packages.md`, `/spine-controls` skill; `Spine.slnx` and `Spine.Packages.slnf`.

## Decisions

- **One recognizer per row for tap and long press** (the issue's preferred option). Each row gets a single `PointerGestureRecognizer`; the grid times the press with one timer and dispatches a short press by hit-testing the row's cells (link cell → `CellCommand`, checkbox cell → toggle or command, otherwise `RowTappedCommand`) and a long press to copy. No `TapGestureRecognizer` on rows or link/checkbox cells at all. Verified on the iPhone 17 simulator and the Pixel 10 Pro emulator: row tap, link tap, checkbox toggle, long-press copy (and no tap after it), swipe and scroll cancelling a press. Copy is therefore on by default (`IsCellCopyEnabled`). Headers keep the source's tap + pointer pair (few views, needed so a long press on a header never sorts).
- **Trimming**: row values are read by path (classic `Binding` for cells, a cached `PropertyInfo` chain for sort, grouping and measurement). The reflective members carry `[RequiresUnreferencedCode]`, suppressed with a justification at the internal entry points; the trim analyzer (`-p:EnableTrimAnalyzer=true -p:TrimmerSingleWarn=false`, which has to be global because the iOS SDK sets it false for libraries) reports nothing else, and MAUI 10.0.50's string `Binding` is not annotated. A Release iOS simulator build of the sample (default partial trimming, `TrimmerSingleWarn=false`) has zero IL warnings and the grid sorts, groups and formats correctly in it. Documented: fine under MAUI's default trimming; full trimming / Native AOT need the row type's properties preserved. A typed value getter was not added: it would need a parallel binding path for every cell type and live updates, for a mode Spine apps do not use today.
- **Strings register lazily** from `DataGrid`'s static constructor (#355), not from a builder call. Note for the coordinator: `SpineStrings.AddDefaults` calls `Reload()`, which raises `Changed`; with `UseSpine` that stores the current culture in Preferences (`Spine.Culture`) and repaints every tracked view, the first time any grid is created. Storing the culture there pins the app's language to the system language of that moment. Not changed here (Common is shared); it affects every lazily registering control.
- **Sort caret and chevron are paths**, not SVGs from `Plugin.Maui.Spine.Svg.Icons`, so the grid needs neither that package nor a font; they take the header text colour and repaint with the theme.
- **Default colours**: neutral greys near the platform's grouped lists and the system blue, all nullable so an instance override stays themed; `SpineStyleOptions` served the grid unchanged.
- **Status row "Load more" link**: the threshold never fires on a list shorter than the screen and a screen-reader user has no scroll to trigger it.
- **"Copied" bubble** as the default confirmation (the source had none without an app hook); `DataGrid.CellCopied` (static, as in the source) replaces it; skipped on Android 13+ where the system shows its own clipboard overlay.
- **Index icon**: no table icon in the SVG set; `wordclock.svg` (a dotted grid) is the closest. Wish for a table/grid icon added to #344.
- Mac Catalyst: sample builds; the measurement path is the iOS one. Windows: compiles in CI only.
