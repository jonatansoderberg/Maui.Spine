# Page Actions (Header Bar Buttons)

**Page actions** are buttons rendered in Spine's built-in header bar. They appear in the trailing (right) slot of the bar, next to the back/close button. Each action can show a text label, an SVG icon, or both.

---

## Adding page actions

Populate `PageActions` inside `OnAppearingAsync`. Guard with `PageActions.Count == 0` to avoid adding duplicates when the user navigates back to the page:

```csharp
public partial class MainPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [RelayCommand]
    private async Task OpenSettings() => await _navigation.NavigateToAsync<SettingsPage>();

    [RelayCommand]
    private async Task Save() { /* ... */ }

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
        {
            // Icon-only button (SVG from Resources/Images)
            PageActions.Add(new PageAction(text: null, command: OpenSettingsCommand)
            {
                Svg = "settings.svg"
            });

            // Text button
            PageActions.Add(new PageAction(text: "Save", command: SaveCommand));
        }

        return base.OnAppearingAsync(navigationDirection);
    }
}
```

---

## `PageAction` properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Text` | `string?` | — | Label shown on the button. `null` for icon-only buttons |
| `Svg` | `string?` | `null` | SVG resource name (e.g. `"settings.svg"`) from `Resources/Images` |
| `Command` | `ICommand` | — | Command executed when the button is tapped |
| `CommandParameter` | `object?` | `null` | Optional parameter forwarded to the command |
| `Placement` | `PageActionPlacement` | `Secondary` | `Primary` (left) or `Secondary` (right) slot |
| `IsVisible` | `bool` | `true` | Whether the button is visible |

---

## Placement

| Value | Position | Typical use |
|---|---|---|
| `Secondary` | Trailing / right side | Page-specific actions (Save, Edit, Share) |
| `Primary` | Leading / left side | Navigation-level actions (Menu, Cancel) |

The back button always occupies the `Primary` slot implicitly. If you add an explicit `Primary` action, it replaces the implicit back button for that page.

```csharp
// Custom primary (left) action — replaces the back button
PageActions.Add(new PageAction(text: "Cancel", command: CancelCommand)
{
    Placement = PageActionPlacement.Primary
});
```

---

## SVG icons

SVG images require **Plugin.Maui.Spine.Svg** (included transitively with Spine). Place `.svg` files in `Resources/Images/` and reference them by file name:

```csharp
PageActions.Add(new PageAction(text: null, command: OpenSettingsCommand)
{
    Svg = "settings.svg"
});
```

---

## Dynamic visibility

To show or hide an action conditionally, set `IsVisible` when building the list:

```csharp
PageActions.Add(new PageAction(text: "Edit", command: EditCommand)
{
    IsVisible = _canEdit
});
```

To update visibility after the page has appeared, clear and repopulate the collection:

```csharp
PageActions.Clear();
// re-add with updated IsVisible values
```

---

## Async commands

Pass an `IAsyncRelayCommand` (from `CommunityToolkit.Mvvm`) to get automatic busy-state handling:

```csharp
[RelayCommand]
private async Task SaveAsync()
{
    await _dataService.SaveAsync();
}

// In OnAppearingAsync:
PageActions.Add(new PageAction(text: "Save", command: SaveAsyncCommand));
```

---

## Desktop (Windows)

On Windows, when the native title bar is shown (`IsTitleBarVisible = true`), page actions are rendered inside the title bar chrome using `TitleBar.LeadingContent` and `TitleBar.TrailingContent`. The same `PageActions` collection drives both header bar and title bar contexts — no extra code needed.
