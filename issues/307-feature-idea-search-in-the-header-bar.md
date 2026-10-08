# Issue #307 — Feature idea: Search in the header bar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/307
**Branch:** issue/307-feature-idea-search-in-the-header-bar
**Status:** In Review

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
- Docs: `docs/wiki/search.md` with iOS, Android and Mac screenshots, README rows, the package README, `page-actions.md`, the `/spine-page` skill and the study's status line.

### While searching, the header bar gives way (branch `fix/search-hides-header-bar`)

Jonatan saw too many X's on his iPhone while searching: the field's clear button, UIKit's round cancel X beside it and, in a sheet, the sheet's close X right above. His decision: do it the native way, as `UISearchController` with `hidesNavigationBarDuringPresentation`.

- `Core/ViewModelBase.cs`: internal `SearchHidesHeaderBar` (an active search in the row, not on Windows), `SearchProgress` (0–1, moved by the region) and `HeaderBarHeight` (the bar's height as the page lays out, less as a search takes its place).
- `Presentation/NavigationRegion.cs` (+ `.Apple.cs`): animates `SearchProgress` when `SearchHidesHeaderBar` changes. Apple: one `UIView` spring animation around a layout pass, so the bar's buttons, the title, the field and the list's content inset move on one curve; a cross-fade under Reduce Motion. Android/Windows: a 300 ms MAUI animation, immediate with animations removed. The header bar and the page are clipped to their bounds during the animation, so the buttons and the title slide under a sheet's top edge rather than over it. `SafeAreaInsetsFor` uses `HeaderBarHeight`, so a list under a floating header moves up with the field. A page shown again comes back as its search left it.
- `Presentation/HeaderBarView.cs`: `SearchProgress` slides the bar's content up by the bar's height and fades it; the bar's strip takes no touches meanwhile (it lay over the field).
- `Presentation/PagePresenter.cs`: the title row shrinks by the bar's height; the title goes up with it, keeping its height, and fades to 2 % (UIKit passes over a fully transparent label when it sizes the scroll edge effect, which left rows sharp under the field).
- `Presentation/SearchField.cs` (+ `.Apple.cs`, Android): `OutlastsFocus` for the row's field: focus starts the search, but losing the focus (the search key) no longer ends it; the cancel button (iOS) and a new back arrow in the capsule (Android, in the magnifier's place) show while it goes on and end it. Ending a search clears its text. iOS re-enables UIKit's cancel button, which UIKit disables when the field lets go of the keyboard.
- Android back: `NavigationRegionViewModel.TryEndSearch()` runs first in the activity's back callback (enabled while a search hides the bar) and in the sheet dialog's back handling.
- `Core/PageSearch.cs`: `IsActive` documented as "a search is going on".
- Showcase: the submit no longer ends the search; a row "End the search" (`IsActive = false`); the footer explains the behaviour.
- Docs: `docs/wiki/search.md` "While searching" with two iOS screenshots (page light, sheet dark), the `/spine-page` skill.

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

### While searching (branch `fix/search-hides-header-bar`)

- **`IsActive` now means "a search is going on", as `UISearchController.isActive`,** not "the field has the focus", where the field is in the row. The search outlasts the keyboard (after the search key the results stay with the bar hidden) and ends with the X, back or code. Following the focus would have brought the bar back, and taken the X away, every time the keyboard went down.
- **Ending a search clears the text**, as UIKit does when a search controller is dismissed, so the list is whole again when the bar comes back. The Showcase's submit no longer sets `IsActive = false`.
- **iPad and Mac Catalyst with the field in the bar: unchanged.** UIKit keeps the bar on iPad; the field is already in it. There the search still follows the focus.
- **Windows: unchanged.** Its `AutoSuggestBox` has no button to end a search with, and hiding the bar would leave only Escape/click-away, which could not be tried without a Windows machine. The search follows the focus there.
- **Android: the Material equivalent** — the bar's buttons and title slide away and the capsule takes the top app bar's place; a back arrow replaces the magnifier at the capsule's leading end, as in Material 3's search view, and system back ends the search before it navigates.
- **The animation is a single UIKit animation on Apple platforms**, not a per-frame MAUI animation: frames, alpha and the list's content inset all move on the same spring, also in Debug builds. 300 ms, critically damped.
- **Reduce Motion: a 0.2 s cross-fade of the region on iOS;** Android with animations removed is immediate (MAUI animations cannot run then).
- **The title keeps 2 % opacity while hidden** (see Changes); invisible in practice, and the scroll edge effect keeps working.
- **Clipping only during the animation:** at rest the hidden bar is fully transparent, so nothing needs clipping, and a clipped page could cut shadows at its edges.
- **A large title page:** the large title is page content and stays in the list under the field; UIKit hides it with the bar. Left as it is.

## Verification

- **iPhone 17 Pro simulator (iOS 26):** the row in light and dark; typing (simulator keyboard tool) filters the list; `Query` from code and `PageSearch.Text` sync both ways; the search key runs `Submit`; `IsActive` from code focuses; cancel shows only while focused and clears and ends the search; `IsVisible = false` removes the row; rows scroll under the bar and the field with the soft edge; the sheet's row; Solid background. The simulator showed no soft keyboard.
- **Mac Catalyst:** a ~430-point window gets the row; at 1000 points the field moves to the trailing end of the bar with the title centred.
- **emulator-5556 (a Pixel Tablet AVD, not a phone):** the capsule without the underline in light and dark; typing and the IME search key (Gboard was in its floating-toolbar mode) run `Submit`; the clear X; the sheet's row.
- **Windows:** compiled only. **Unit tests:** `PageSearchDiscoveryTests` (13), the core test project passes (42).

