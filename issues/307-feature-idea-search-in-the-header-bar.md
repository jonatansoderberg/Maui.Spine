# Issue #307 — Feature idea: Search in the header bar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/307
**Branch:** issue/307-feature-idea-search-in-the-header-bar
**Status:** In Progress

## Plan

This follows the study `docs/proposals/spine-header-search.md` and Jonatan's decisions of 2026-09-30 recorded there: Spine draws the field around MAUI's `SearchBar`, `[PageSearch]` on a string property in the view model, an observable `PageSearch` on `ViewModelBase.Search`, no results view, scopes and `UISearchTab` are separate issues, and the iOS 26 spike runs before the bottom capsule is built.

The study's delivery plan has eight steps. Step 1 (#269, the keyboard observer) is merged (PR #458). This issue delivers **step 3 (API + the field at the top)** and **step 6 (sample and docs)** as v1. The rest is listed under follow-ups below and in the PR.

### 1. API (`Plugin.Maui.Spine.Core`)
- `SearchPlacement { Automatic, Top }`. `Button` from the study waits for step 4, so the enum carries no value that does nothing yet.
- `Suggestions` from the study waits for step 5 on every platform, Windows included (see Decisions).
- `PageSearchAttribute` on a `string` property (or a toolkit `[ObservableProperty]` field): `Placeholder`, `Submit` (name of a command run on the return key, with the text), `Placement`.
- `PageSearch : ObservableObject`: `Text`, `Placeholder`, `Placement`, `IsActive` (focus, two-way), `IsVisible`, `SubmitCommand`.
- `ViewModelBase.Search` (observable), set by discovery or by hand.

### 2. Discovery (`Services/PageSearchDiscovery.cs`)
- Runs next to `PageActionDiscovery.Populate` in `NavigableMeta.Apply`, once per view model.
- Two-way sync between `PageSearch.Text` and the view model's property through `INotifyPropertyChanged` and reflection, cached per type.
- Errors are visible: a missing property, a property that is not a string, or a `Submit` that names no command throw with the type and member in the message.
- MAUI-free, so `tests/Plugin.Maui.Spine.Core.Tests` can link it and test it.

### 3. Placement
- `Automatic` resolves to:
  - **iPad and Mac Catalyst, region pages at least 600 points wide:** a field at the trailing end of the header bar, left of the trailing action (in `HeaderBarView`).
  - **Everywhere else, and in sheets:** a row below the header bar, in the page's own title row (`PagePresenter`), so it moves with the page in transitions like the title.
- On iPhone with iOS 26 `Automatic` is the row for now; the bottom capsule and the search button are step 4 and need the spike first (decision of 2026-09-30).
- `Top` always gives the row.

### 4. The row (`PagePresenter`)
- The title row grows by the search row's height and the field sits at its bottom. Everything that is sized to the title row (the solid background, the scroll edge band, UIKit's scroll edge interaction, iOS 27's stretched soft edge) then covers the field too.
- `NavigationRegion.SafeAreaInsetsFor` adds the row's height to the top inset under a floating header, so a list scrolls under both.
- A change of `Search`, `IsVisible` or `Placement` while the page is shown re-resolves the header through `ReapplyHeaderBar`.

### 5. The field (`Presentation/SearchField.cs` + platform parts)
- MAUI's `SearchBar` bound to `PageSearch`: two-way text, placeholder, the return key runs `SubmitCommand`, focus ↔ `IsActive`.
- iOS/Mac: `UISearchBar` in the minimal style, so it draws the system's own search field for the OS version.
- Android: a Material 3 capsule (fully rounded, the theme's surface-container colour) around MAUI's `SearchView`, without its underline.
- Windows: MAUI's `AutoSuggestBox` in the row.

### 6. Sample and docs
- Showcase page **Search** (`Pages/Search`): a list filtered by `[PageSearch]`, a submit that reports what was searched, a switch to hide the field, and the same page as a sheet.
- `docs/wiki/search.md`, a row in `page-actions.md`/README, the `/spine-page` skill, the study's status line.

### Follow-ups (not in this issue)
- Step 2 + 4: the iOS 26 spike against a native reference, then the bottom capsule, `SearchPlacement.Button` (the search button for tab and footer pages), the lift above the keyboard and the close circle.
- Step 5: suggestions under the field (iOS, Android; Windows through `AutoSuggestBox.ItemsSource`), ⌘F on Mac Catalyst.
- Windows: the field in `TitleBar.Content`.
- A search row that slides away on scroll; the row below a large title instead of above it.
- Scopes as a menu button next to the field, and a group of two buttons in the header bar (issues of their own per the decisions); `UISearchTab` (its own issue).

## Open Questions

None that block v1; see Decisions.

## Changes

- `Core/PageSearch.cs`: `SearchPlacement` and the observable `PageSearch` (`Text`, `Placeholder`, `Placement`, `IsActive`, `IsVisible`, `SubmitCommand`).
- `Core/PageSearchAttribute.cs`: `[PageSearch(Placeholder, Submit, Placement)]` on a string property or a toolkit field.
- `Services/PageSearchDiscovery.cs`: builds the `PageSearch` and syncs its text with the declared property both ways; visible errors for a non-string property, two declarations and an unknown `Submit`. Called from `PageActionDiscovery.Populate` (`Search ??= …`), so a hand-set `Search` wins.
- `Services/ToolkitNames.cs`: the toolkit's naming rules (`[RelayCommand]` → `…Command`, `_field` → `Field`), shared by both discoveries.
- `Core/ViewModelBase.cs`: observable `Search`; internal `InSheet`, `IsCompactWidth`, `SearchLayout` and `SearchRowHeight`. A change of `Search`, its `IsVisible`/`Placement`, the header bar's visibility or the region's width re-resolves the header (`ReapplyHeaderBar`).
- `Presentation/SearchField.cs` (+ `.Apple.cs`, `Platforms/Android/SearchField.Android.cs`): MAUI's `SearchBar` bound to `PageSearch`; the search key runs `SubmitCommand`, focus ↔ `IsActive`. Apple: minimal `UISearchBar`, cancel shown only while focused, and cancel also ends the search. Android: a fully rounded capsule (a tint of the foreground) around `SearchView`, without its underline.
- `Presentation/PagePresenter.cs`: the field at the bottom of the title row, which grows by `HeaderBarConstants.SearchRowHeight`; the title label covers the row too, so UIKit's scroll edge effect, the stand-in band and a solid background reach below the field.
- `Presentation/NavigationRegion.cs`: `SafeAreaInsetsFor` adds the row under a floating header; the header page's trailing field goes to `HeaderBarView`; the region reports `IsCompactWidth` (< 600 points).
- `Presentation/HeaderBarView.cs`: the trailing field (240 points, at most two fifths of the bar) left of the trailing action; the title's slot keeps clear of it.
- `Presentation/HeaderBarConstants.cs`: public `SearchRowHeight` (iOS/Mac 52, 60 from 26; Android 64; Windows 48).
- Showcase page **Search** (`Pages/Search`): a list of towns filtered by `[PageSearch]`, switches for `IsVisible` and `Top`, `IsActive` from code, clear from code, the submitted text, and the same page as a sheet.
- `tests/Plugin.Maui.Spine.Core.Tests/PageSearchDiscoveryTests.cs`: discovery, both directions of the sync, submit, toolkit names and the three errors.
- Docs: `docs/wiki/search.md` with iOS and Mac screenshots, README rows, the package README, `page-actions.md`, the `/spine-page` skill and the study's status line.

## Decisions

- **v1 is step 3 + step 6 of the study.** Steps 2 and 4 (the iOS 26 spike, then the bottom capsule, the search button and the lift above the keyboard) are follow-ups: the decision of 2026-09-30 says the spike runs before the bottom capsule is built. Until then `Automatic` on iPhone with iOS 26 is the row below the bar.
- **`SearchPlacement` has no `Button` yet.** A value that does nothing would mislead; adding it with step 4 is not a breaking change.
- **No suggestions in v1, Windows included.** The study puts Windows' suggestions in step 3, but `AutoSuggestBox.ItemsSource` could only be compiled tonight, not run, and an API that shows suggestions on one platform of four reads as a bug on the others. `Suggestions` comes with step 5 on all platforms.
- **Windows gets the row, not `TitleBar.Content`.** The title bar belongs to the window and would have to be swapped on every navigation and for sheets; that cannot be tried without a Windows machine. The study already names the row as Windows' fallback.
- **The row lives in the page's title row, not in a row of its own.** Everything sized to the title row (solid background, scroll edge band, UIKit's edge interaction, iOS 27's stretched soft edge) then covers the field with no new code, and the field moves with the page in transitions.
- **The trailing field lives in `HeaderBarView`**, not in the page's title row: the header bar lies over the title row and takes its touches.
- **A region narrower than 600 points gets the row on iPad and Mac.** At ~430 points (a small Mac window) the trailing field left the centred title no room at all. 600 is about where iPad's Split View turns compact; the study's "compact width like iPhone".
- **Apple: the system's `UISearchBar` in its minimal style, no glass capsule of Spine's own for the row.** On iOS 26 it already draws a capsule field and a glass close circle; a Spine capsule around it would be glass on glass. The glass capsule belongs to the bottom placement (step 4).
- **Cancel on Apple shows only while the field has the focus, and ends the search.** MAUI shows it whenever there is text, where UIKit then draws it disabled and grey, and MAUI's cancel only clears the text.
- **Android capsule colour:** a tint of the foreground (black 6 %, white 10 %) rather than Material's `colorSurfaceContainerHigh`, which MAUI's MaterialComponents theme does not define; the tint sits on any page background.
- **No header bar, no field.** The row lives in the title row, which a page without a header bar does not have.
- **A large title page gets the row above the large title** (the large title is page content); UIKit puts it below. Listed as a follow-up.
