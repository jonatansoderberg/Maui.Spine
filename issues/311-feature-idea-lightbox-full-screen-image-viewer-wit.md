# Issue #311 — Feature idea: Lightbox — full-screen image viewer with zoom and swipe-to-dismiss

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/311
**Branch:** issue/311-feature-idea-lightbox-full-screen-image-viewer-wit
**Status:** Completed

## Plan

### Starting point
#304 already does most of what a lightbox's open and dismiss need. A `Transition.Tag` on the page arriving makes it a **zoom**: the page grows out of the view with that tag on the page under it, and a view inside the page with the same tag is its **focus**, the part that lines up with that view. The interactive back-swipe drives the zoom with `SharedElementFlight.Follow`, `RestoreAsync` and `ZoomAsync(push: false)`. A lightbox is a zoom page whose tag follows the image on screen and whose focus is that image. The drag-down to dismiss is a second gesture that drives the same three methods.

What is new: the page kind, the pager with pinch and double-tap zoom on each platform, the drag-down, the black backdrop that fades, the Photos-style chrome, and Share and Save.

### API (decided: a page kind, Jonatan 2026-10-07)
```csharp
[NavigableLightbox(Share = true, Save = true)]
public partial class PhotoViewerPage : SpinePage<PhotoViewerViewModel> { ... }

await _nav.NavigateToAsync<PhotoViewerPage, PhotoSet>(new(photos, index));
```
```xml
<SpinePage ...>
    <Lightbox ItemsSource="{Binding Photos}" Position="{Binding Index}" />
</SpinePage>
```
- `NavigableLightboxAttribute` (`Core/NavigableAttribute.cs`) is a region page with fixed presentation: an overlay header bar, transparent, with a white foreground and clear glass; light status bar; full-screen safe area (`SafeAreaEdges.None`); close button (X) in place of the back chevron; no edge back-swipe (it would fight paging to the previous image). It is pushed onto the current stack like a region page. `NavigableMeta` gets an `IsLightbox` flag rather than a third `NavigationPresentation`, because everything that routes by presentation (stacks, sheets, `ShowAsync`) should treat it as a region page.
- `Lightbox` (a public control in `Presentation/` or `Controls/`, with a handler per platform) takes `ItemsSource` of `LightboxImage(ImageSource Source, string? Tag = null, string? Caption = null)`, a two-way `Position`, and `CurrentItem` (read-only).
- **The tag:** Spine sets the lightbox page's own `Transition.Tag` to the current image's `Tag` whenever `Position` changes. The app puts the same tag on each thumbnail (`Transition.Tag="{Binding Key}"`), as on any zoom. A `Lightbox` with no tags opens and closes with a fade.
- **Actions:** `Share` and `Save` on the attribute add built-in header actions for the current image. The app's own actions are ordinary `[PageAction]`s on the view model (Delete, Info), so nothing new is needed for them.

### The focus: the image, not the full-screen page
The image is aspect-fitted inside a full-screen page, so the view whose bounds line up with the thumbnail is the fitted (and possibly zoomed) image, not the `Lightbox` view. `SharedElementFlight.AddZoom` measures the focus through its native view. An internal `IZoomFocus` with a `NativeFocusView` property (on Android, a rect) lets `Lightbox` hand over the current page's image view: on iOS the `UIImageView` inside the zooming `UIScrollView`, whose frame is the fitted image at its current zoom. `ConvertRectToView` already takes the scroll view's zoom into account.

The thumbnail is usually a square crop and the image is not. The zoom already handles this: the page is scaled until the focus covers the thumbnail, and the mask cuts it to the thumbnail's shape. Opening reveals the rest of the image around the crop as the mask grows, as Photos does, and the landing picture rides on the focus and crossfades as in #304.

### Black backdrop and background fade
The front layer is masked during a zoom, so a black page background would grow out of the thumbnail as a black rectangle. Instead, the region's `_backDragDimOverlay` (black, outside the masked layer) is the backdrop while the lightbox moves: it fades to 1.0 rather than `BackDimOpacity` on a lightbox zoom, and follows `1 − progress` on a drag. The lightbox page's own background is black at rest, when the back layer is empty, and transparent (`LiftFront` keeps it clear) while it moves.

### Drag-down to dismiss
- A vertical drag on an image that is not zoomed in (zoom scale at its minimum) starts a dismiss. A drag on a zoomed-in image pans it. A pinch never dismisses. This is decided, not a question: it is how Photos behaves, and with the scroll view's zoom at its minimum there is nothing for a pinch and a drag to fight over.
- `NavigationRegion` gets `BeginDismissDrag` / `UpdateDismissDrag(x, y)` / `EndDismissDragAsync(velocityY)`, sharing the state of `OnBackSwipe` (`StartInteractiveBack`, `CompleteInteractiveBackAsync`, `CancelInteractiveBack`). The lightbox finds its region through the parent chain.
- `SharedElementFlight` gets a lightbox mode for `Follow`: the page moves with the finger and shrinks only a little (to about 0.75 at a full screen's drag), with no mask corners, because only the image is visible. The backdrop fades.
- On release: past about 100 pt down, or faster than a flick, it zooms into the thumbnail of the image now showing (`ZoomAsync(push: false)`). Otherwise it springs back (`RestoreAsync`).
- **No thumbnail on screen** (no tag, or it scrolled away): `FindAsync` returns no zoom. Then a drag carries the image on downward and fades it out, and the close button and system back use the usual transition (the fallback #304 uses).
- **Keeping the thumbnail on screen:** the app can scroll its grid to `Position` when it changes (the sample does this with `CollectionView.ScrollTo` when the page comes back). Spine does not scroll the app's list itself.

### Native building blocks
| | Pager | Zoom | Drag-down taken in |
|---|---|---|---|
| iOS / Mac Catalyst | `UICollectionView` with paging (horizontal, full-width cells), so cells are reused | A `UIScrollView` per cell with `viewForZoomingInScrollView`, centred by content insets; double-tap zooms to 2.5× about the tap point | A `UIPanGestureRecognizer` on the pager whose `ShouldBegin` takes vertical drags at minimum zoom; it fails the paging and the cell's pan otherwise |
| Android | `RecyclerView` + `PagerSnapHelper` (no ViewPager2: no new AndroidX package, see the AndroidX drift note) | A zoomable `ImageView` written for Spine: `Matrix`, `ScaleGestureDetector`, `GestureDetector` for double tap and fling, and `OverScroller` | The pager's `OnInterceptTouchEvent`, as `BackSwipeHost` does for the back-swipe, because the cells take touches first |
| Windows | `FlipView` | `ScrollViewer` with `ZoomMode="Enabled"` per item | — (the usual transition, no drag; compiled on the Mac only, as in #304) |

Images are loaded through MAUI's `IImageSourceService` (`GetImageAsync`), so `ImageSource` works as it does for `Image`: files, URIs, streams. A placeholder spinner shows until the image arrives.

### Chrome (decided: like Photos, Jonatan 2026-10-07)
- Black backdrop, light status bar, overlay header bar with X and the actions, and the current image's `Caption` at the bottom in the page margin.
- A single tap hides or shows the header, the caption and the status bar (a fade). A double tap zooms, so the single tap waits for the double tap to fail.
- **The tab bar is hidden while a lightbox is the front page.** This needs new support in `SpineTabbedHostPage`: `UITabBarController.SetTabBarHidden` on iOS 18+ (animated with the zoom), and translating or hiding `BottomNavigationView` on Android. It comes back when the lightbox leaves, and follows the drag.

### Share and Save (decided: Share, Save and the app's own actions, Jonatan 2026-10-07)
- **Share:** the current image is written to a cache file and shared with MAUI's `Share.RequestAsync(ShareFileRequest)`, anchored to the header button on iPad and Mac.
- **Save:** iOS/Mac with `PHPhotoLibrary` add-only (`PHAccessLevel.AddOnly`). The app needs `NSPhotoLibraryAddUsageDescription` in its Info.plist. Spine checks the bundle for the key and leaves the button out without it, with a `[Spine]` console line once (failures stay visible). Android uses `MediaStore.Images` (API 29+ needs no permission; Spine's minimum is checked when implementing). Windows saves to the Pictures library with `FileSavePicker`. A short confirmation and a success haptic follow.

### Showcase
`samples/MauiSpineSampleApp/Pages/Lightbox/`, in the gallery pattern: an intro with `ExampleCodeSwitch` and `PackageChips`, and one `Example` ("Open a photo") with a thumbnail grid (`SafeArea.PageMargin`, `GridItemsLayout`) and Try options: with tags (zoom from the thumbnail) or without them (fade), and with or without captions and the app's own action. `LightboxPhotoPage` is the `[NavigableLightbox]` page. A `SampleIndex` entry with a fitting icon (check the set for a photo or gallery symbol; otherwise `/spine-symbol`).

**Photos (decided: drawn with Skia, Jonatan 2026-10-07):** six to eight photo-like landscapes in portrait, landscape and panorama formats, drawn once with SkiaSharp by a script in the scratchpad and committed as PNGs under `Resources/Images/` (around 1600 px on the long side), so the repo has no licensing questions.

### Docs
A wiki page `docs/wiki/lightbox.md`, linked from README (feature table and wiki index), the package README, `transitions.md` and the `spine-page` skill. It states that Save needs `NSPhotoLibraryAddUsageDescription`.

### Order of work
1. **iOS spike (iPhone 17 Pro simulator):** `NavigableLightboxAttribute` + `Lightbox` with paging and zoom, the page tag following `Position`, the image as the focus. Check that open and close zoom from and into the thumbnail of the image showing, including after paging.
2. iOS drag-down with the backdrop fade, the chrome and its tap, and the tab bar hidden.
3. Share and Save on iOS.
4. Android (check which emulators other sessions use first), then Mac Catalyst. Windows compiled on the Mac.
5. Showcase page, the Skia photos, docs, skill.

## Open Questions

- None.

## Changes

- **iOS spike (2026-10-07), open, page, zoom and close work on the iPhone 17 Pro simulator (iOS 26.4):**
  - `NavigableLightboxAttribute` (`Core/NavigableAttribute.cs`): a region page with fixed presentation (overlay header, transparent, white foreground, clear glass, light status bar, no safe-area padding). `NavigationRegistry` resolves it with the region defaults; `NavigableMeta` puts it on the view model as `ViewModelBase.Lightbox`. The implicit back action shows `close.svg` on a lightbox.
  - `Presentation/Lightbox.cs`: `LightboxImage(Source, Tag, Caption)`, `Lightbox` with `ItemsSource`, two-way `Position`, `CurrentItem` and two-way `IsChromeVisible`. It sets the tag of the image showing on itself and on its `[NavigableLightbox]` page, and is the page's `IZoomFocus`.
  - `Presentation/Lightbox.Apple.cs`: `LightboxHandler` / `LightboxView`, a paging `UIScrollView` with three reusable zooming `UIScrollView` pages (`LightboxPage`), a 20 pt gap between images, aspect-fit and centred by content insets, pinch and double-tap zoom, a tap that toggles the chrome (it waits for the double tap to fail), the caption at the foot, and a `UIPanGestureRecognizer` for the drag down that the pager and the pages wait on. Images load through `IImageSource.GetPlatformImageAsync`; a failed load prints a `[Spine] Lightbox:` line.
  - `SharedElementFlight`: `IZoomFocus` lets a view hand over a native view as its focus (the image, not the full-screen `Lightbox`). A push waits up to 300 ms for the focus's picture while the front layer is hidden. With such a focus, the thumbnail's picture rides on the crop of the image that the mask closes on, not on the whole image, and its corners are the mask's. `Follow` on a lightbox shrinks the image only to 0.6 with square corners.
  - `NavigationRegion`:
    - A lightbox's black is the dim overlay at full strength, fading in and out around the zoom; at rest it stays at 1 under a lightbox. `LiftFront` keeps the lightbox's layer clear.
    - A lightbox without a thumbnail on screen fades in and out (`FadeLightboxAsync`) instead of sliding.
    - No edge back-swipe while a lightbox is in front.
    - `OnDismissDrag`: the drag down drives `Follow` with progress over half the height, fades the black and the chrome, and on release past 100 pt or faster than 800 pt/s zooms into the thumbnail of the image showing (`ZoomAsync(push: false)`), otherwise springs back (`RestoreAsync`). Without a thumbnail the image carries on down and fades.
    - Chrome: the header bar, the page's title (`PagePresenter.TitleBar`, which lives inside the page and would otherwise ride along with the image), the caption and the status bar (`StatusBar.SetHidden`, app-level on iOS, like the style) fade together on a tap, during a drag and when the page zooms.
  - Showcase: `Pages/Photos` (namespace `Photos`, since `Pages.Lightbox` would clash with the control's type name): `PhotosPage`, a three-column grid of thumbnails with Try options (zoom from the thumbnail, or fade), and `PhotoViewerPage`, the `[NavigableLightbox]` page with a title "n of 8". Eight photo-like landscapes drawn with SkiaSharp (`lightbox_*.png`, 94–345 KB, portrait, landscape, square and a panorama), included with `Resize="False"`.
  - **The drag down now carries the image as Photos does (Jonatan, on the iPhone):** the first version shrank the image about the page's centre and let it run under the bottom edge, and it then flew all the way up into its thumbnail, which looked like a bounce. A rubber band and then a drag that drove the zoom straight towards the thumbnail were tried; Jonatan compared with Photos, where the image stays under the finger, and found that nicer. `SharedElementFlight.Carry` shrinks the page about the point where the finger came down (`Lightbox.OnDismissDrag` passes it) so that point stays under the finger, to 0.5 at a full-height drag, with square corners; let go, it flies into the thumbnail of the image showing. The black fades over half the height.
  - **A thumbnail stayed hidden after a few drags (Jonatan, on the iPhone):** each flight remembered a view's opacity when it hid it, so a flight that hid a view another flight was already hiding took 0 for its opacity and left it hidden for good. Hidden views are now counted across flights (`SharedElementFlight.HiddenViews`), and a view comes back with its own opacity when the last flight lets go of it.
  - **The thumbnail page was sometimes white during a drag on the iPhone, then appeared with a jolt at the end (Jonatan; not seen in the simulator):** the page under a lightbox left the back layer when the lightbox opened and was put back only as the drag began, and on the phone (a Debug build, interpreted) it was sometimes not drawn until the pages swapped. The page under a lightbox now stays in the back layer, behind the black, while the lightbox is in front (`NavigationRegionViewModel.RestBackView`). `BackAsync` used an empty back layer to tell a button pop from a completed back-swipe; it now asks `_completingInteractiveBack` instead. `ResetAsync` empties the back layer. Verified in the simulator: X, a short and a long drag, and a plain back to the gallery still animate.
  - Verified in recordings: opening from thumbnail 1, paging to 2, a drag down that lands in thumbnail 2; X on image 7 lands in thumbnail 7; a pinch zooms and a drag on the zoomed image pans instead of closing; a tap hides the bars, title, caption and status bar.

- **Share and Save in a toolbar (2026-10-07):**
  - Jonatan chose a toolbar at the foot of the lightbox, as in Photos, over a ⋯ menu in the header or Share alone in the header: the header has only one trailing slot.
  - `Presentation/LightboxToolbar.cs`: a row of the header's own `PageActionView` buttons (white, clear glass), Share at the leading edge and the others spread to the trailing edge. It belongs to the region, as the header bar does, so it stays put while the image zooms or is dragged, and fades with the rest of the chrome (`SetChrome`, `FadeLightboxChromeAsync`). `NavigationRegion.UpdateLightboxToolbar` fills it: Share and Save as the attribute asks, then the page's own `Secondary` actions, which a lightbox no longer shows in the header (`SecondaryPageAction` is null on a lightbox). It follows the page's `PageActionsChanged` and the system insets.
  - The caption sits above the toolbar: the region gives the lightbox `BottomInset` (the system's bottom inset plus the toolbar), because the native view's own safe area is zero under Spine. Before this the caption sat in the home indicator's area.
  - **Share:** the image showing is written to `Cache/lightbox/<caption>.png|jpg` (PNG for a PNG source, otherwise JPEG at 0.92). On iOS and Mac Catalyst Spine presents `UIActivityViewController` itself with a `UIActivityItemSource` whose `LPLinkMetadata` carries the picture, so the sheet's header shows the photo rather than MAUI `Share`'s blank document icon; a popover from the button on iPad and the Mac. Other platforms use MAUI `Share`.
  - **Save:** `PHPhotoLibrary` with `PHAccessLevel.AddOnly`. The button is shown only when Info.plist has `NSPhotoLibraryAddUsageDescription`, and otherwise a `[Spine] Lightbox:` line says so once. Saved: a success haptic and a tick in place of the button for 1.5 s. Denied or failed: an error haptic, a console line and an alert (`Spine.Lightbox.SaveDenied.*`, `Spine.Lightbox.SaveFailed.Title`, in English and Swedish).
  - Icons `share.svg` and `download.svg` copied into the core package's `Resources/Svg` from the icon set.
  - Showcase: `NSPhotoLibraryAddUsageDescription` in the iOS and Mac Info.plist, and an Info action (`[PageAction(Svg = "info.svg")]`) on `PhotoViewerPage` to show where a page's own actions go.
  - Verified in the simulator: the permission prompt, then two saves landing in the library (`IMG_0009/0010.PNG`), the tick for 1.5 s, the share sheet with the photo and its caption and "Save Image", and the Info alert.
- **The sample's photos are Jonatan's (2026-10-07):** `lightbox_skerries`, `misty_lake`, `moon_lake`, `aurora`, `autumn_rapids`, `mountain_valley`, `birch_lake` and `archipelago_dawn`, as JPEG, in the order he sent them; the Skia drawings and their `Resize="False"` PNG entry are gone (`lightbox_*.jpg` now).
- **The tab bar goes while a lightbox is in front (iOS, 2026-10-07):**
  - `NavigationRegion.CoversTabBarChanged` is raised with `true` as a lightbox is pushed and `false` as it starts to leave (button, system back or a drag let go to close); `SpineTabbedHostPage` hides or shows the bar of the active tab (`PlatformSetTabBarHidden`).
  - On iOS the bar slides down out of sight and fades by its transform alone (a no-bounce spring, 0.35 s). UIKit's `setTabBarHidden(_:animated:)` was tried first: it takes the bar out of the tab's safe area, while the region's negative container margin is measured with the bar, so the region reached about 49 pt below the screen and the toolbar sat half off it. Re-measuring during the zoom would move the page under the zoom; the transform leaves layout alone, and the region already reaches under the bar.
  - In a tab, the toolbar keeps clear of the window's bottom inset (`TabInsetsProvider.WindowInsets`), not the tab's, which includes the bar.
  - No drag-down while a push or pop is still moving the layers (`_transitioning`): under a recording the simulator was slow enough that a drag landed during the opening zoom.
  - Verified with a temporary tabbed Showcase (two `[NavigableTab]` pages built in code in the scratchpad, compiled in with `CustomBeforeMicrosoftCommonTargets`; `App.xaml` pointed at the tab for the build and restored at once): the bar slides away as the photo opens, the toolbar sits above the home indicator, and the bar comes back as a drag closes the photo.
- **Android (2026-10-07), verified on the Pixel_Tablet emulator (emulator-5556; the Pixel_10_Pro on 5554 was running another app):**
  - `Presentation/Lightbox.Android.cs`: `LightboxView`, a horizontal `RecyclerView` with a `PagerSnapHelper` (no ViewPager2), laid out half a gap past each edge so the 20 dp gap shows only between pages; each page a `ZoomImageView`, an `ImageView` with a fit matrix and a zoom matrix on top: `ScaleGestureDetector` for the pinch (1–4×), `GestureDetector` for the double tap (2.5× about the tap, animated), the single tap (chrome) and the pan of a zoomed image, an `OverScroller` fling, and `RequestDisallowInterceptTouchEvent` so a pan that reaches the image's edge goes back to the pager. The drag down is taken in `OnInterceptTouchEvent` (down, more than sideways, past the touch slop, at the full view), as `BackSwipeHostHandler` takes the back-swipe, and starts where it was taken. Images load with `GetPlatformImageAsync`; the caption is a `TextView` laid out above `BottomInset`.
  - The focus on Android is a rect: `IZoomFocus.FocusIn(ancestor)` gives where the image is drawn (its drawable's bounds through the matrix) in the front layer's pixels, and the Android flight rides the thumbnail's picture on the crop as iOS does.
  - Share uses MAUI `Share` with the image written to the cache; Save inserts into `MediaStore.Images` under `Pictures/` (pending until written), so it needs no permission and is offered from Android 10 (API 29).
  - The status bar hides with the chrome through `WindowInsetsControllerCompat` (shown again by a swipe from the edge).
  - **The tab bar:** the Material bar takes its own room beside the content, so it goes (`Gone`) as the lightbox is pushed, before the zoom measures, and comes back (faded in) only once the lightbox has gone: brought back as the lightbox started to leave, the region would shrink under the zoom (`GiveBackTabBarEarly` is iOS-only). With the bar gone, MAUI keeps the system navigation bar's inset as a bottom margin on `navigationlayout_content` (112 px on the tablet), which left a white band under the lightbox; the host sets it to 0 while the bar is hidden and puts it back with the bar. Hiding the status bar with a tap does not bring the margin back.
  - Verified in `screenrecord` videos: the zoom out of the thumbnail, paging with the gap, a drag down that shrinks the image under the finger and lands in the thumbnail, X after paging landing in the right thumbnail, Save (the photo in MediaStore as `Pictures/Sunset over the skerries.jpg`, the tick), the share sheet, and in the temporary tabbed Showcase the bar going and coming back with no band.
  - Not related to the lightbox: on the Pixel Tablet in landscape the header bar of every page, the gallery's included, sits in the status bar.
- **From a sheet, over the whole screen (2026-10-07):** first pushed onto the sheet's own stack (sheet-sized). Jonatan asked for the native experience; in Maps, Mail and Google Maps a photo opened from a sheet covers the whole screen and closes back into the sheet, so that is what Spine does now:
  - `Presentation/LightboxOverlay.cs` (+ `.Apple.cs`, `.Android.cs`): `NavigationService.NavigateRegionAsync` hands a `[NavigableLightbox]` page opened while a sheet is up to `LightboxOverlay.ShowAsync`, which builds a `NavigationRegion` of its own (`FillsWindow`, `PageUnder` = the sheet's page) whose stack is an empty page with the lightbox pushed on it, so the open, the X, the system back and the drag down all run as on any stack. When a pop brings it back to its root (`NavigationRegionViewModel.WentBackToRoot`) the overlay goes and the sheet page's status bar style is applied again.
  - **iOS / Mac:** the region's native view is added to the key window above the sheet's presentation (not a presented controller), so the thumbnail and the lightbox are measured in one window. MAUI offsets it by the safe area as it does a page, so the container's negative margin stays.
  - **Android:** a full-screen, see-through `Dialog` of its own (edge to edge, light status bar icons, `OnBackPressed` closes the lightbox), since the sheet is a dialog window too. The flight now measures with `GetLocationOnScreen`, across the two windows. MAUI never gives the dialog's root region a size (`Height` -1), which made the drag's progress 0 (no fade, no shrink), so the drag uses the screen's height then (`DragHeight`); the start point is now passed on every move.
  - `PageUnder` stands in for the region's root in `PlayTransitionAsync` and `FindZoomDragAsync`; `INavigationService.BackAsync`, `ReturnAsync` and `CloseAsync` act on the overlay's stack while it shows.
  - Removed the in-sheet special cases (the dim and the page under kept in `LiftFront`, the lightbox's pan winning over the sheet's).
  - Showcase: a second example on the Lightbox page, "Open from a sheet", with `PhotoSheetPage` (`[NavigableSheet]`, medium and full detents) opening the same `PhotoViewerPage`. The photos are now `Photo.All` / `Photo.Images()`, shared by both.
  - Jonatan saw the sheet's grid cut off in a hard line above the home indicator: the sheet page was padded at the bottom by the safe area. `PhotoSheetPage` now leaves Bottom out of `SafeAreaEdges` and takes it as `ScrollInset`, so the grid runs to the sheet's edge and scrolls its last row clear.
  - Verified on the iPhone 17 Pro simulator and the Pixel Tablet emulator: the photo opens full screen out of its thumbnail in the sheet; a drag down shrinks it under the finger with the page and the sheet showing through, and it lands in the thumbnail with the sheet still up.
- **Mac Catalyst (2026-10-07):** the iOS code runs as is: in the Showcase window the photo zooms out of its thumbnail and back into it with the close button. Jonatan saw the toolbar sitting on the window's bottom edge and its buttons further out than the header's. The toolbar now takes the header bar's side margin and button width (`HeaderBarView.SideMargin` / `IconButtonWidth`, which depend on glass), plus the side insets, so its buttons line up under the header's on every platform; with no bottom inset (a Mac window, a sheet) it stands off the bottom edge by the same margin. On the Mac: Share at x 15 under the close button, Info at 372 under the theme button, 15 from the bottom.
- **Windows (2026-10-07, compiled on the Mac only):** `Presentation/Lightbox.Windows.cs`, a `FlipView` of zooming `ScrollViewer`s (1–4×, double tap 2.5×, tap for the chrome). No zoom from the thumbnail or drag down (the lightbox fades in and out), and no Share or Save yet (`Lightbox.CanShare` and `CanSaveToPhotos` are false), so the toolbar shows only the page's own actions.
- **Docs (2026-10-07):** `docs/wiki/lightbox.md` (the page, opening from the thumbnail, drag down, Share/Save/own actions with the Info.plist key, tabs and sheets, platforms), linked from README (feature table and wiki index), the package README, `transitions.md` and the `spine-page` skill.

## Decisions

- **A `[NavigableLightbox]` page kind with a `Lightbox` control** (Jonatan, 2026-10-07), not `ShowLightboxAsync` or a bare control: the app owns the page and its view model, and its own actions are ordinary page actions.
- **Share, Save and the app's own actions** (Jonatan, 2026-10-07). Save is shown only when the app has declared `NSPhotoLibraryAddUsageDescription`.
- **Photos-style chrome with the tab bar hidden, on iOS and Android alike** (Jonatan, 2026-10-07).
- **A lightbox opened from a sheet covers the whole screen, over the sheet** (Jonatan, 2026-10-07, after first choosing the sheet's own stack): that is how the system's own apps show a photo opened from a sheet. It is a layer of its own (`LightboxOverlay`) with the sheet's page as the page under it, so the zoom, the drag down and the return to the sheet work as on any stack. It is not an option on the attribute: the same page does the right thing wherever it is opened from.
- **Sample photos: Jonatan's own generated landscapes** (2026-10-07), replacing the eight drawn with SkiaSharp that were first chosen so no third-party images entered the public repo. Eight Nordic landscapes in every format the viewer has to handle (two panoramas, landscape, portrait, two squares, a tall one), converted from PNG (24 MB) to JPEG at quality 82 (`lightbox_*.jpg`, 0.4–0.8 MB each, 5 MB in all) at their full size.
- **Built on #304's zoom, not a separate overlay:** the lightbox is a zoom page whose `Transition.Tag` follows the image showing and whose focus is that image, so open, close and system back already land on the right thumbnail, and an image scrolled out of sight falls back as any zoom does.
- **The region's dim overlay is the backdrop while the page moves:** the front layer is masked during a zoom, so a backdrop inside the page would grow out of the thumbnail as a black rectangle.
- **Drag-down only at minimum zoom; no edge back-swipe on a lightbox:** a zoomed-in image pans, and a swipe from the left edge pages to the previous image.
- **`Lightbox` lives in the core package**, not a `Plugin.Maui.Spine.Controls.*` package: the page kind is core navigation, and the control needs the region's and the flight's internals (`IZoomFocus`, `OnDismissDrag`, the chrome).
- **The chrome state is `Lightbox.IsChromeVisible`, public and two-way**, so a page can fade its own overlays with the bars.
- **No ViewPager2 on Android:** `RecyclerView` + `PagerSnapHelper` is already in MAUI's dependencies; a new AndroidX package is a binding drift risk.
