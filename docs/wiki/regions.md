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
