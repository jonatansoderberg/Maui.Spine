# Regions

A **region** is a full-screen page that participates in Spine's stack-based navigation. Navigating forward pushes a new page onto the stack; navigating back pops it off. The header bar, back button, and slide transitions are handled automatically.

<p align="center">
  <img src="images/region-settings.png" width="210" alt="A region page with the header bar and back button">
  <img src="images/animated-label-and-page-actions.png" width="210" alt="A region page with a page action in the header bar">
  <img src="images/navigation-parameter.png" width="210" alt="A region page opened with a typed parameter">
</p>
<p align="center"><sub>Regions from the sample app: the header bar, the back button, and page actions come with the page</sub></p>

---

## Declaring a region page

Apply `[NavigableRegion]` to the code-behind class:

```csharp
namespace MyApp.Pages;

[NavigableRegion(Title = "Settings")]
public partial class SettingsPage { public SettingsPage() => InitializeComponent(); }
```

### Attribute properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Title` | `string` | `""` | Text shown in the header bar or title bar |
| `Lifetime` | `ServiceLifetime` | `Transient` | `Transient` creates a fresh instance each navigation; `Singleton` retains state |
| `IsHeaderBarVisible` | `bool` | platform default | Show/hide the Spine in-page header bar |
| `IsBackButtonVisible` | `bool` | `true` | Show/hide the back arrow in the header bar |
| `IsTitleBarVisible` | `bool` | platform default | Show/hide the native window title bar (desktop only) |
| `TitlePlacement` | `TitlePlacement` | platform default | `HeaderBar` or `TitleBar` |
| `TitleAlignment` | `TitleAlignment` | platform default | `Left` or `Center` |
| `SafeAreaEdges` | `SafeAreaEdges` | `All` | Which edges Spine pads for system bars. Exclude an edge to render edge-to-edge behind it — use `ViewModelBase.SafeAreaInsets` to offset content manually |
| `HeaderBar` | `HeaderBarMode` | `Normal` | `Overlay` floats the header bar over content that starts at the top of the screen. See [Overlay header](#overlay-header) |
| `LargeTitle` | `bool` | `false` | The page opens on its own large title, which collapses into the header bar as it scrolls. See [Large title](#large-title) |
| `HeaderBarBackground` | `HeaderBarBackground` | `Auto` | What is behind the header bar while content scrolls under it: `ScrollEdge` (the iOS 26 scroll edge effect in the system's style, a fading band elsewhere), `ScrollEdgeSoft` / `ScrollEdgeHard` (the same with a chosen style), `Solid` (transparent at the top, the page background once content passes under), `Clear`, or `Auto` (`Clear` under `Overlay`; `ScrollEdge` on iOS 26 for a list page; `Solid` otherwise). See [Scroll edge](#scroll-edge) |
| `HeaderBarForeground` | `string?` | `null` | A fixed colour (hex) for the header bar's title and action icons; `null` follows the theme |
| `StatusBarStyle` | `StatusBarStyle` | `Default` | `LightContent` or `DarkContent` for the status bar's clock and icons while the page is shown |
| `ScrollInset` | `SafeAreaEdges` | `None` | Edges on which the page's first `ScrollView` / `CollectionView` takes the safe-area inset as a native content inset, so it can scroll under an excluded bar and still reach its last row. See [Scrolling under a bar](#scrolling-under-a-bar) |

Platform defaults:

| Platform | `IsHeaderBarVisible` | `IsTitleBarVisible` | `TitlePlacement` | `TitleAlignment` |
|---|---|---|---|---|
| Mobile (Android) | `true` | `false` | `HeaderBar` | `Center` |
| Desktop (Windows) | `false` | `true` | `TitleBar` | `Left` |

> **iOS / Mac Catalyst** use the mobile defaults; Mac Catalyst is exercised less than iOS.

---

## Navigating to a region page

Inject `INavigationService` into the ViewModel and call `NavigateToAsync<TPage>()`:

```csharp
public partial class MainPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [RelayCommand]
    private async Task OpenSettings() => await _navigation.NavigateToAsync<SettingsPage>();
}
```

Spine automatically decides the presentation style — if the target page carries `[NavigableRegion]` it will be pushed onto the stack; if it carries `[NavigableSheet]` it will be presented as a bottom sheet.

---

## Navigating back

```csharp
await _navigation.BackAsync();
```

The back button in the header bar calls this automatically. Implement `OnBackRequestedAsync` to intercept or cancel it:

```csharp
public override async Task<bool> OnBackRequestedAsync()
{
    // e.g. prompt the user if there are unsaved changes
    bool confirmed = await DisplayAlert("Discard changes?", "", "Yes", "No");
    return confirmed;
}
```

Return `false` to cancel the navigation; return `true` (the default) to allow it.

---

## Singleton pages

Use `Lifetime = ServiceLifetime.Singleton` to retain state when the user navigates away and returns:

```csharp
[NavigableRegion(Title = "Settings", Lifetime = ServiceLifetime.Singleton)]
public partial class SettingsPage { public SettingsPage() => InitializeComponent(); }
```

---

## Lifecycle hooks

Override these methods in the ViewModel to react to navigation events:

```csharp
public partial class SettingsPageViewModel : ViewModelBase
{
    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        // navigationDirection == NavigateTo  ? page was pushed
        // navigationDirection == Back        ? returned to this page from a child page
        await LoadDataAsync();
        await base.OnAppearingAsync(navigationDirection);
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        // Called just before the page leaves the screen
        return base.OnDisappearingAsync(navigationDirection);
    }

    public override async Task OnResumedAsync()
    {
        // Called when the app comes back to the foreground while this page is shown
        await LoadDataAsync();
        await base.OnResumedAsync();
    }
}
```

`NavigationDirection` values:

| Value | Meaning |
|---|---|
| `None` | Page set as root (first shown) |
| `NavigateTo` | Page was pushed onto the stack |
| `Back` | Returned to this page because a child was popped |

### Coming back to the app

`OnResumedAsync` is called when the app returns to the foreground, or its window is activated again, on the pages that are **shown**: the current page of the region (or of the selected tab), and of an open sheet together with the page under it. Pages covered by another page on the stack, or on another tab, are not called; they get `OnAppearingAsync` when they are shown again. The page does not subscribe to window events itself; Spine owns the window.

It is not called at launch, because the first `OnAppearingAsync` covers that. It is called only after the app was deactivated, and anything that takes the window out of the foreground or out of focus counts: going to the background, but also the notification shade or a system dialog on a phone, and another window on the desktop. Keep the override cheap, or check whether anything actually changed.

Spine does not follow the calendar day. A page that shows "today" compares the date when it comes back, and runs its own timer to midnight if it must turn while it is on screen:

```csharp
public override async Task OnResumedAsync()
{
    var today = DateOnly.FromDateTime(DateTime.Now);

    if (Day != today)
        await ShowDayAsync(today);

    await base.OnResumedAsync();
}
```

---

## Overlay header

A page that opens on a photo, a map or a hero wants the content to start at the top of the screen with the header floating over it, in a colour that reads on the image:

```csharp
[NavigableRegion(Title = "Trip",
                 HeaderBar = HeaderBarMode.Overlay,
                 HeaderBarForeground = "#FFFFFF",
                 StatusBarStyle = StatusBarStyle.LightContent,
                 SafeAreaEdges = SafeAreaEdges.Left | SafeAreaEdges.Right)]
```

- `HeaderBar = Overlay`: the content host is not padded at the top; the title row and the actions sit over the content, pushed down by the status bar. Page actions keep their glass on iOS 26, which is what makes them readable over imagery.
- `HeaderBarForeground`: the title and the action icons take this colour instead of the theme's.
- `StatusBarStyle`: applied when the page appears and again on a theme change. On Android it sets the window's light/dark status bar appearance. On iOS it needs `<key>UIViewControllerBasedStatusBarAppearance</key><false/>` in `Info.plist`; without it Spine logs a hint and leaves the bar alone.
- `ViewModelBase.SafeAreaInsets.Top` reports status bar plus header height (`HeaderBarConstants.BarHeight`), so a list can take it with `SafeArea.ScrollInset="Top"` and still draw behind the bar. `HeaderBarConstants` is public for anything that needs the numbers.

---

## Header bar height

The header bar is a row of items (`HeaderBarConstants.Height`: the back button, the title, the page actions) with the bar below it. `HeaderBarConstants.BarHeight` is the whole bar, measured from under the status bar. Everything that depends on the header's height uses it: where content below the bar starts, `SafeAreaInsets.Top` and the scroll inset under a floating bar, a `Solid` background, the Android/Windows scroll edge band, and the element UIKit sizes the scroll edge effect to.

| Platform | `Height` (items) | `BarHeight` (bar) |
|---|---|---|
| iOS / Mac Catalyst 26 and later | 44 | 54: the items at the top, 10 points of bar below, as `UINavigationBar` |
| iOS / Mac Catalyst before 26 | 44 | 44 |
| Android | 48 | 48 |
| Windows | 32 | 32 |

The title's text centres on the item row, not on the whole bar, so it lines up with the buttons. Measured against a `UINavigationController` on the iOS 26 simulator: the bar is 54 points in a region and in a sheet (where it starts 16 points below the sheet's top edge), and a large title's 52-point row starts at the bar's bottom edge. The scroll edge effect differs by case. The soft style fades a little past the bar's bottom edge. The hard band ends at the bar's bottom edge under a large title and in a sheet. Under an inline title in a region, UIKit's hard band stops at the bottom of the items, 10 points above the bar's edge, and Spine does the same.

---

## Large title

The iOS large title and the Material 3 medium top app bar: the page opens on its own large title, and as that title scrolls away under the bar the bar's own title fades in and the bar turns from transparent to the page's background.

```csharp
[NavigableRegion(Title = "Inbox", LargeTitle = true)]
```

```xml
<CollectionView ItemsSource="{Binding Messages}" SafeArea.ScrollInset="Bottom">
    <CollectionView.Header>
        <HeaderBarLargeTitle />
    </CollectionView.Header>
    …
```

`HeaderBarLargeTitle` shows the page's title (set `Text` to show something else) in the platform's large-title size, weight, row height and margin, in the header's `HeaderBarForeground` when the page fixes one and in the app's label style otherwise. Spine measures it to decide when the bar's title fades in, wherever it sits in the scroll content. For a title of your own, the numbers are public on `HeaderBarConstants` (`LargeTitleFontSize`, `LargeTitleFontAttributes`, `LargeTitleHeight`, `LargeTitleMargin`); set `HeaderBar.CollapseDistance` on the page to where its text has gone.

Three settings, each on the attribute and as an app-wide default in `options.RegionDefaults` / `TabDefaults` / `SheetDefaults`, and independent of each other:

| Setting | Decides |
|---|---|
| `HeaderBar` (`Normal`, `Overlay`) | Layout: whether the page draws its own top under the bar (a photo, a hero) |
| `LargeTitle` | Whether the page opens on a large title that collapses into the bar |
| `HeaderBarBackground` (`Auto`, `ScrollEdge`, `ScrollEdgeSoft`, `ScrollEdgeHard`, `Solid`, `Clear`) | What is behind the bar while content scrolls under it |

- A large title always has content scrolling under the bar, so it lays out like the overlay: content starts at the top of the screen and `SafeAreaInsets.Top` is status bar plus header. Spine adds `Top` to the scroll source's `SafeArea.ScrollInset`, so the large title starts right under the bar without the page doing anything.
- `Overlay` and `LargeTitle` combine: a hero with a greeting in it, `HeaderBar.CollapseDistance` set to where the greeting has gone, and `HeaderBarBackground = Solid` so the bar closes over the photo once the list passes under it. `Overlay` with `Solid` and no large title gives the photo page whose bar turns solid on scroll; mind that a fixed `HeaderBarForeground` stays fixed on the solid bar.
- `HeaderBar.ScrollSource` (on the page) names the `ScrollView` or `CollectionView` to follow. Without it Spine follows the page's first one, and waits for it when the page builds it later (a state view that swaps in its body).
- The bar's title fades in over the last `HeaderBarConstants.LargeTitleFadeLength` points before the collapse distance: `HeaderBar.CollapseDistance` when the page sets it, otherwise the offset at which the `HeaderBarLargeTitle`'s text has gone under the bar (its top in the scroll content plus half its row and half its font size), otherwise `HeaderBarConstants.LargeTitleCollapseDistance`. A page whose large text is its own, a greeting in a hero for instance, sets the distance at which that text has gone.
- With `Solid`, the background is in after `HeaderBarConstants.ScrollEdgeFadeLength` points, as soon as rows start passing under the bar. It is the page's own background when it has an opaque one, otherwise what the platform paints behind pages (the system background on iOS, the window background on Android).
- `ViewModelBase.HeaderBarCollapseProgress` (0 to 1) follows the title's fade, for a page that fades something of its own with it.
- With Reduce Motion on (iOS), animations removed (Android) or animation effects off (Windows) there is no fade: the title and the background switch at the middle of their ranges.
- The large-title constants are per platform: 34-point bold in a 52-point row on iOS, 24 sp in 56 dp on Android (Material 3 medium top app bar), 28 on Windows, all with 16 points of side margin.

---

## Scroll edge

On iOS 26, content in system apps scrolls under the navigation bar and stays half visible behind it. Spine gives a list page the same effect without any page code:

```csharp
[NavigableRegion(Title = "Inbox")]   // HeaderBarBackground.Auto: ScrollEdge on iOS 26
```

- With `Auto`, a region or tab page gets `ScrollEdge` on iOS and Mac Catalyst 26 when its header bar is visible and its scroll view fills the page from the top. Its scroll view is `HeaderBar.ScrollSource`, or else the first `ScrollView` / `CollectionView`. "Fills from the top" means every container between the page and the list holds only the list, or is a grid in which the list spans all rows (a list with a floating button). A page with fixed content above its list keeps the solid bar, so nothing that does not scroll ends up under the header. Sheets keep their own header.
- The page is laid out as under `Overlay`, and the scroll view gets the top inset, so the first row starts below the bar at rest. The header's title row gets a `UIScrollEdgeElementContainerInteraction` pointing at the scroll view, and UIKit draws the edge effect as it does behind a `UINavigationBar`: from the top of the screen over the status bar and the whole bar. The glass page actions stay as they are.
- UIKit sizes the effect to the elements in the container view (labels, images, controls), not to the container itself: an empty view in the container does not count, and the effect stops below the lowest element. That is why the title label fills the bar's height. A page with a visible header bar but an empty title has nothing for UIKit to size the effect to. Under an inline title with `ScrollEdgeHard` the label ends with the 44-point item row instead, because that is where UIKit's own hard band stops behind an inline navigation bar title (see [Header bar height](#header-bar-height)).
- The style follows UIKit's `UIScrollEdgeEffectStyle`:

| Value | iOS / Mac Catalyst 26 | Android, Windows |
|---|---|---|
| `ScrollEdge` | UIKit's automatic style, as behind a navigation bar: soft on iPhone; the system may choose hard elsewhere (the Mac) | As `ScrollEdgeSoft` |
| `ScrollEdgeSoft` | Content fades and blurs into the bar, a little past its bottom edge | The page colour, slightly see-through behind the bar, fading out over 24 points below it |
| `ScrollEdgeHard` | A frosted, nearly opaque band down to the bar's bottom edge, with a hairline there; for a bar with more in it than a title | The page colour, nearly opaque, down to the bar's bottom edge, with a hairline there |

- Set one of them on the attribute to ask for it on any page and platform; `Auto` gives `ScrollEdge`. The Android and Windows stand-ins show as rows pass under. iOS and Mac Catalyst before 26 show `Solid`.
- Reduce Transparency (iOS/Mac) or transparency effects off (Windows) turn every scroll edge value into `Solid`. The setting is read when the page is navigated to, and again when the page changes its background.
- The background can change while the page is shown: set `HeaderBarBackground` (or `HeaderBarMode`) on the page's view model. Spine resolves it again (`Auto` and the fallbacks included) and lays the page out again, under the bar for a scroll edge value or `Overlay`, below it otherwise. The Scroll edge sample page switches between every value this way.

```csharp
// In the page's view model, e.g. from a setting:
HeaderBarBackground = HeaderBarBackground.ScrollEdgeHard;
```
- It combines with `LargeTitle`: the large title slides under the edge while the bar's title fades in.
- Opt out app-wide with `options.RegionDefaults.HeaderBarBackground = HeaderBarBackground.Solid` (and the same on `TabDefaults`), or per page on the attribute.

---

## Scrolling under a bar

Excluding an edge from `SafeAreaEdges` lets content draw behind that bar — the home indicator, the gesture bar, or the floating tab bar inside the tab host. A list that does this still has to let its last row scroll clear of the bar. Give the list the inset as a *content inset* instead of a spacer:

```xml
<CollectionView ItemsSource="{Binding Rows}" SafeArea.ScrollInset="Bottom" />
```

`SafeArea.ScrollInset` works on `ScrollView`, `CollectionView` and `HeroCollectionView`. It reads the page's `ViewModelBase.SafeAreaInsets` (non-zero only on the edges Spine is not padding; inside the tab host the bottom value includes the tab bar) and applies it natively: `UIScrollView.ContentInset` on iOS and Mac Catalyst, padding with `clipToPadding` off on Android, `Padding` on Windows. The rows keep drawing through the bar; only the scrollable range grows. Rotation and tab-bar changes flow through automatically.

To apply it without touching the list, set it on the attribute — or once for every page through `options.RegionDefaults.ScrollInset` (and `TabDefaults` / `SheetDefaults`):

```csharp
[NavigableRegion(SafeAreaEdges = SafeAreaEdges.Top | SafeAreaEdges.Left | SafeAreaEdges.Right,
                 ScrollInset = SafeAreaEdges.Bottom)]
```

Spine then sets `SafeArea.ScrollInset` on the page's first `ScrollView` or `CollectionView`. A view that sets its own value keeps it.

---

## Work that lives with the page

Three members of `ViewModelBase` take care of the bookkeeping a live page otherwise repeats: a timer with its own cancellation, subscribe-on-appear/unsubscribe-on-disappear with a dispatch to the UI thread, and a refresh on resume.

```csharp
public LiveViewModel(IScoreService scores)
{
    // Runs on the UI thread when the page appears, then every 15 s; pauses in the background and
    // while the page is hidden; runs again at once when the page or the app is back.
    Poll(TimeSpan.FromSeconds(15), ct => RefreshAsync(ct));

    // Subscribed while the page shows, handler marshalled to the UI thread. Overloads for
    // Action, Action<T> and EventHandler<T>; a raw (subscribe, unsubscribe) pair for anything else.
    WhileVisible(h => scores.Changed += h, h => scores.Changed -= h, OnScoresChanged);
}

// A load that should not outlive the page: PageLifetime cancels when the page is left.
private Task RefreshAsync(CancellationToken ct) => _scores.LoadAsync(PageLifetime);
```

| Member | Starts | Stops |
|---|---|---|
| `PageLifetime` | a new token when the page appears | cancelled when the page disappears (not on backgrounding) |
| `Poll(interval, work)` | when the page appears, and again at once on activation | while the page is hidden or the window is deactivated |
| `WhileVisible(...)` | subscribes when the page appears | unsubscribes when the page disappears |

Register them once, from the constructor or `OnAppearingAsync`; a registration lives as long as the view model, so a singleton tab page polls every time it is shown. Spine's own service events (`ILiveActivityService.ActivitiesChanged`, `IPushNotificationService.RegistrationChanged`, `IWidgetService.PushTokenChanged`) are raised on the UI thread.

---

## Interactive back-swipe gesture

On mobile, the user can swipe from the left edge to go back, matching the native iOS behavior. This is built into `NavigationRegion` and requires no extra configuration.

The gesture only claims a drag that starts at the leading edge, runs rightward and more sideways than up or down, and only while there is a page to go back to. Anything else — a vertical drag in a list, a drag inside a canvas that handles its own touches, a drag on the root page — is left to the content. On iOS and Mac Catalyst that is enforced on the native pan recognizer before it begins, since a `UIPanGestureRecognizer` that has begun cancels the touches of the views under it.

---

## Overriding global defaults per page

Properties not set on the attribute fall back to `SpineOptions.RegionDefaults`. Set them on the attribute to override for a specific page:

```csharp
[NavigableRegion(
    Title = "Detail",
    IsHeaderBarVisible = true,
    TitleAlignment = TitleAlignment.Left)]
public partial class DetailPage { public DetailPage() => InitializeComponent(); }
```
