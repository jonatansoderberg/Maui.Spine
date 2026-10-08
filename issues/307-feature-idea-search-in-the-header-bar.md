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
- Review round with Jonatan (same branch):
  - **Clear button bug:** MAUI hid UIKit's cancel button when the text went empty, leaving a search that could not be ended. Fixed, then superseded by the next point.
  - **Spine's own cancel button on Apple:** UIKit's cancel button is off for good (MAUI turns it on with text; Spine turns it off again, also asynchronously); a `PageActionView` with `close.svg` and `Spine.Header.Cancel` sits beside the field, 44 points, at the page margin, 11 points from the field (measured on a native `UISearchController`). It follows `SearchProgress`, so it comes and goes with the bar's animation and nothing else; the glyph weight is the header actions' (#493).
  - **44-point field on Apple:** `SearchField.Bar` + `BarHandler` (`MauiSearchBar` subclass) holds `UISearchTextField` to 44 points, centred, after UIKit's layout and whenever its frame changes (KVO on `frame`); the handler sets MAUI's private `_editor` field so typing, font and colours keep working (a visible console warning if MAUI renames it, and the field then keeps UIKit's height). 17-point text.
  - **Android to Material 3:** measured against a native `SearchBar` + `SearchView` built in the harness. At rest the capsule matches (56 points, 16 in, magnifier centred at 24, text at 68; SearchView's own insets removed). While searching the capsule opens into the search view header: square, full width, 72 points (the row grows by 8 on Android), divider in Material's outline colour, back arrow (AppCompat's, as Material's) at the leading edge, clear button 56 points wide with its icon at its own size, icons in on-surface-variant, 16-point text. The magnifier is put back right after SearchView's own focus handling.
  - **Leaving a page ends its search** (`SendDisappearingAsync`: back, push, tab switch; `BottomSheetCoordinator` after a sheet is dismissed), so the keyboard goes down with the page.
  - Showcase: the Top switch says what it does ("iPad, Mac: a row instead of the header bar").

### The active search on the header bar's row (branch `fix/search-reveals-hidden-field`, Jonatan 2026-10-08)

- `PagePresenter`: the field's frame follows `SearchProgress`: at rest in the row below the bar; while searching its slot is centred on the bar's item row, so on iOS the X has exactly the trailing action's frame (in a sheet the close button's) and the field runs from the page margin to it. The title row shrinks by the search row (not the bar) as the field goes up, so the list glides up into the row's place on the same animation. The title label keeps its rest padding during the animation (a padding change is not animated and dropped the title 30 points on the first frame).
- `PageSearch.ShownForSearch` (internal) and `ViewModelBase.SearchInPlace`: a field hidden at rest and shown only for the search takes the header bar's own row; no search row is added, so the content does not move. The field fades in rising from half a row below as the bar slides up and fades, and the reverse when the search ends. `SearchInPlace` holds until the region's last search animation has finished (`SearchAnimationStarted/Finished`), so the field fades out in place instead of vanishing.
- `ViewModelBase.HeaderBarHeight` removed: the bar keeps its height; only the search row's contribution changes.
- iOS: the region lays out what changed before the search animation (a field shown for the search) before animating, so it does not grow out of an empty frame.
- Android: the search header is the top app bar's row (48 points) instead of Material's 72; the back arrow is where the navigation icon is and the clear button (48 points, was 56) where the trailing action is; the divider runs to the edges.
- **The jank when a search ended** was case 3: `IsVisible = false` arrived right after the end started, removed the search row in one step and the list jumped up 54–60 points, then glided down with the bar. Case 2 measured smooth on the simulator before the change; the remaining glitch there was the title padding above, introduced and fixed in this round.
- Tests: `ShownForSearch` is set before `IsVisible` is raised and cleared after the search; not set for a field shown at rest. Docs and the `/spine-page` skill follow; the two iOS "while searching" screenshots are retaken.

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
- **Leaving a page ends its search** (Jonatan, review): UIKit keeps a search controller active under a pushed page, but a page left with the keyboard up kept the keyboard; ending it on every disappearance is simpler and what he asked for. Supersedes "a page comes back as its search left it".
- **Spine's own cancel button instead of UIKit's** (Jonatan, review): UIKit's came and went with the text and was larger than the header actions. The field in the bar (iPad, Mac) has no cancel button at all now.
- **The 44-point field needs MAUI internals** (a handler subclass that sets `SearchBarHandler._editor` by reflection): no public API sizes UIKit's field. If MAUI renames the field the bar falls back to MAUI's own and says so on the console.
- **Android's active state is Material 3's search view header, not a full-screen search view:** the page's own list is the result list, so it stays visible below the header.
- **AppCompat's back arrow on Android's search header**, as Material's search view, though Spine's header bar uses a chevron for back.

### The active search on the header bar's row

- **Deliberately less native than UIKit** (Jonatan): UIKit keeps an active search field 8 points below the bar's row; Spine centres it on the row so the X takes the trailing action's place.
- **Android's search header is 48 points, not Material's 72**, for the same reason: back arrow and clear button take the bar's button frames.
- **A field the page shows during an in-place search** moves to the row below the bar once the bar has come back (a one-step change of layout); rare, not animated.
- **The in-place field rises from half the item row (22 points on iOS, 24 on Android)** while the bar keeps its existing slide (the bar's full height) and fade.

## Verification

- **iPhone 17 Pro simulator (iOS 26):** the row in light and dark; typing (simulator keyboard tool) filters the list; `Query` from code and `PageSearch.Text` sync both ways; the search key runs `Submit`; `IsActive` from code focuses; cancel shows only while focused and clears and ends the search; `IsVisible = false` removes the row; rows scroll under the bar and the field with the soft edge; the sheet's row; Solid background. The simulator showed no soft keyboard.
- **Mac Catalyst:** a ~430-point window gets the row; at 1000 points the field moves to the trailing end of the bar with the title centred.
- **emulator-5556 (a Pixel Tablet AVD, not a phone):** the capsule without the underline in light and dark; typing and the IME search key (Gboard was in its floating-toolbar mode) run `Submit`; the clear X; the sheet's row.
- **Windows:** compiled only. **Unit tests:** `PageSearchDiscoveryTests` (13), the core test project passes (42).


### While searching (branch `fix/search-hides-header-bar`)

- **iPhone 17 Pro simulator (iOS 26.4):** page (light) and sheet (dark): focus → header bar, title, back, page actions and the sheet's close slide up and fade, the field and the list move up (recorded with `simctl io recordVideo`, frames checked: one ~300 ms spring, the cancel button slides in with the field shrinking) → X → everything comes back. Field 44 × 315 at x 16, cancel 44 × 44 at x 342 (the native measurements). Clear → type → clear: the cancel button's pixels do not change (max diff 21/255 against 255 for the field). Clear then dismiss the keyboard: the search stays and the X ends it. The search key / keyboard down keeps the search with the X enabled. Reduce Motion: cross-fade. The scroll edge effect under the field while searching. Back swipe and a back from code with an active search: the field lets go, the text is cleared. A sheet swiped down with an active search.
- **emulator-5556 (a Pixel Tablet AVD, not a phone):** Material 3 `SearchBar`/`SearchView` reference next to Spine, in pixels: rest 1536 × 112 at x 32, icon slot 40–136, text at 136 (both); searching 1600 × 144 at y 48, back 0–96, text 96–1488, clear 1488–1600 with a 28-px glyph, divider 2 px in (121,116,126) (both). Clear → type → clear keeps the back arrow; keyboard down keeps the search, the back arrow ends it; system back (the activity dispatcher) ends the search first, then goes back; leaving the page with an active search hides the keyboard; closing a sheet with an active search hides the keyboard; sheet in dark.
- **Not verified:** the slide animation on Android (MAUI animations finish at once on this emulator, a plain `Animation` in the harness too, so only the end states were seen); the sheet dialog's own back key on Android (adb key events break Gboard, see memory); iOS 27 and the physical iPhone (glass alpha while fading, the stretched soft edge); iPad/Mac (the field in the bar is unchanged apart from losing UIKit's cancel button); Windows (compiled only).

### The active search on the header bar's row

- **iPhone 17 Pro simulator, soft keyboard on**, `simctl io recordVideo` at 60 fps, content offset found per frame by matching a strip of the list: page light and dark: X 342,62 44×44 = palette 342,62 44×44; sheet (region coordinates): X 342,20 44×44 = close 342,20 44×44. Field visible at rest: list 176 → 116 (−60 pt) in 15 frames, monotonic, last five steps ≤ 1 pt, and back the same way (light and dark). Field hidden: the list's offset is 0.0 in all ~300 frames of start and end, light and dark; in the sheet (presentation-layer trace) the first row stays at 74 in every frame of both. Before the change the end of case 3 jumped −54 pt in one frame.
- **emulator-5556 (Pixel Tablet AVD):** MAUI animations finish in one frame there; end states: back arrow 4,24 48×48 = the bar's back button, field 52–796 (clear at the trailing action's place), list 136 → 72 and back; hidden field: list at 72 in every sampled frame of start and end.
- Mac Catalyst built, Windows compiled, core tests 73 passed. Not verified: physical iPhone (glass alpha while the field and the bar cross-fade), Android sheet, Reduce Motion (unchanged code path).
