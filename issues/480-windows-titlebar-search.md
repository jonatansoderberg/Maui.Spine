# Issue #480 — Search: the field in the Windows title bar (TitleBar.Content)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/480
**Branch:** issue/480-windows-titlebar-search
**Status:** In Progress

## Plan

Follow-up to #307 (PR #473). The study (`docs/proposals/spine-header-search.md`, §3.5 and §5) puts the search field on Windows in MAUI's `TitleBar.Content`, the centre slot of the title bar Spine already builds (`SpineApplication.Windows.cs`: `LeadingContent`/`TrailingContent` hold the page actions). v1 used the row below the header bar on Windows as well.

Nothing can be run on Windows here; the code is only compiled (`EnableWindowsTargeting` on the Mac). The design is therefore chosen to be easy to reason about and to fall back to v1's row whenever the title bar cannot take the field. Runtime verification is tracked in #501.

1. **One more place for the field.** `SearchLayout` gets a fourth value, `TitleBar`. The rule that picks it moves to a MAUI-free resolver (`Presentation/SearchLayout.cs`) so it can be unit tested: `Automatic`, not in a sheet, the page shows the window's title bar (`IsTitleBarVisible`) and Spine's title bar takes search (`SpineOptions.Windows.SearchInTitleBar`, default `true`) → `TitleBar`. Otherwise the v1 rules apply unchanged (no header bar → none; iPad/Mac wide → trailing; everything else → the row). The row is only drawn for `SearchLayout.Row`, so a field in the title bar hides it by itself.
2. **The title bar follows the shown page.** A Windows-only partial of `SpineApplication` (`SpineApplication.Search.Windows.cs`) owns one `SearchField` (the same control as the row, so text, submit, `IsActive` and placeholder behave exactly as in v1) and puts it in `TitleBar.Content` when the current page of the root region (the selected tab's, with tabs) has `SearchLayout.TitleBar` and no sheet is open. It re-evaluates on: the region's `CurrentRegionViewModel` (navigation), the page's `SearchLayout` (search added/removed/hidden/placement/title bar visibility), `ISpineHost.ActiveRegionChanged` (tab switch), `Window.Page` (host swap), a sheet opening or closing (a new `BottomSheetCoordinator.SheetActiveChanged`), and `Window.TitleBar`.
3. **The app's own content.** Whatever is in `TitleBar.Content` when a searching page shows is kept and put back when no page searches. Content the app sets while a field is there becomes what comes back later; the field stays.
4. **Opt-out.** `options.Windows.SearchInTitleBar = false` leaves `TitleBar.Content` to the app and keeps v1's row.
5. **Failures are visible.** If the app has replaced `Window.TitleBar` with its own while `SearchInTitleBar` is on and a page wants the title bar, Spine throws an `InvalidOperationException` naming the page and the option, instead of silently showing no field.
6. Docs: `docs/wiki/search.md`, `docs/wiki/windows-options.md`, the study's status line, the `/spine-page` skill. Sample: the Search page shows the title bar on Windows so it exercises the new path.

## Changes

- `Presentation/SearchLayout.cs` (new): `SearchLayout` moved here from `SearchField.cs` and gains `TitleBar`; `SearchLayoutRules.Resolve(placement, inSheet, headerBar, titleBar, trailing)` holds the placement rules without MAUI types, so the core test project compiles it in.
- `Presentation/SearchField.cs`: `Resolve` replaced by `FitsTrailing(compact)` (the iPad/Mac check) and the static `UsesTitleBar`, set by the Windows application from the option before the first page.
- `Core/ViewModelBase.cs`: `SearchLayout` resolves through `SearchLayoutRules`; the title bar counts when `UsesTitleBar`, the page shows it (`IsTitleBarVisible`) and it is not a lightbox. A header bar is no longer required for that one case. `IsTitleBarVisible` changing re-raises `SearchLayout`.
- `Core/SpineOptions.cs`: `Windows.SearchInTitleBar` (default `true`).
- `Presentation/BottomSheetCoordinator.cs`: `IsSheetActive` raises the static `SheetActiveChanged` when it changes.
- `Platforms/Windows/SpineApplication.Search.Windows.cs` (new): one `SearchField` (max 360 wide, 32 tall, centred) that goes in `TitleBar.Content` for the root region's current page with `SearchLayout.TitleBar` while no sheet is open; follows navigation, the page's `SearchLayout`, tab switches (`ActiveRegionChanged`), host swaps (`Window.Page`), sheets and `Window.TitleBar`; keeps and restores the app's content; throws if `Window.TitleBar` was replaced while a page wants it. Hooked from `InitializeWindowsTitleBar`.
- `Core/PageSearch.cs`: docs for `Automatic`, `Top` and `IsActive` mention the title bar.
- `tests/Plugin.Maui.Spine.Core.Tests/SearchLayoutRulesTests.cs` (new, 7 tests) and the test project links `SearchLayout.cs`.
- Sample: the Search page shows the title bar (`IsTitleBarVisible = true`, Windows only) so it exercises the title bar on Windows; its texts mention Windows.
- Docs: `docs/wiki/search.md` (table, a section *In Windows' title bar*, "Not yet"), `docs/wiki/windows-options.md` (`SearchInTitleBar`), the study's status line, the `/spine-page` skill.
- Verified: the Windows TFM of `Plugin.Maui.Spine` compiles on the Mac (`EnableWindowsTargeting`); Android, iOS and Mac Catalyst build; the sample builds for Mac Catalyst; core tests pass (80). The sample cannot be compiled for Windows on the Mac (the WinUI XAML compiler is a Windows executable). Nothing was run on Windows; see #501.

## Decisions

- **Relation to the `Spine.Windows` options.** The field lives in the title bar Spine already creates (`InitializeWindowsTitleBar`), so it needs no new chrome and nothing changes for the tray, single instance, backdrop or window size. It is governed by one new option, `SearchInTitleBar` (default `true`, so a Windows app with the desktop defaults — title bar shown, header bar hidden — finally shows its search field; in v1 such a page showed none because the row needs a header bar). Per page, the existing `IsTitleBarVisible` decides: a page that hides the title bar (as every page of the sample does by default) keeps v1's row. `SearchPlacement.Top` also keeps the row, as its doc already promised "on every platform".
- **The app's own `TitleBar.Content`.** Remembered when a searching page shows, put back when none does. If the app sets new content while the field is there, that content is what comes back and the field stays (re-installed on the next dispatcher turn): a page that searches owns the slot while it shows. Apps that need the slot for themselves set `SearchInTitleBar = false` and get v1's row. Chosen over "the app's content wins" because that would leave a page with search and no visible field.
- **A replaced `Window.TitleBar` throws** (`InvalidOperationException` naming the page and the option) instead of silently dropping the field, per the "make failures visible" rule. Only when a page actually wants the title bar, so an app with its own title bar and no search is unaffected.
- **Sheets** keep their row below the sheet's header bar (they never show the title bar, and `InSheet` resolves to the row). While a sheet is open the title bar shows no field — the page underneath is covered, and typing into it would filter a list nobody sees — and the app's content comes back; closing the sheet puts the field back.
- **Tabs and host swaps.** The title bar follows `ISpineHost.RootNavigationRegion`, which is the selected tab's region, re-read on `ActiveRegionChanged`, and the installed host from `SpineHostProvider`, re-read when `Window.Page` changes (logout → login swaps the tab host for a plain host). A tab that is not realized yet has no current page, so the field is cleared until its page arrives.
- **One field, the same control as the row.** The title bar uses a `SearchField` with `OutlastsFocus = false` (the default), so the search follows the focus exactly as v1's Windows row did, and text, placeholder, the search key (`QuerySubmitted` → `SubmitCommand`) and `IsActive` from code go through the same code on every platform.
- **32 points tall.** The field keeps the title bar at its 32-point height, which `SetTitleBarVisibilityAsync` assumes when it slides the title bar away (`Padding.Top = -32`). WinUI's taller 48-point title bar with a search box would need that code to change too; left for when it can be run.
- **No suggestions.** As in v1, `Suggestions` (`AutoSuggestBox.ItemsSource`) waits for step 5 on all platforms.
- **The rules got a MAUI-free home** (`SearchLayoutRules`) so the fallback cases (option off, title bar hidden, sheet, `Top`, no header bar) are unit tested, since nothing on Windows can be run here.
- **Not changed:** the existing title bar visibility and page-action code subscribes only to the root region of the host resolved at startup, so with tabs (or after a host swap) the title bar's visibility and its page actions do not follow the selected tab. That predates this issue and is listed in #501 rather than changed blind.
