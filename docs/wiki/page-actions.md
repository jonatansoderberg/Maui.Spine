# Page Actions (Header Bar Buttons)

**Page actions** are buttons rendered in Spine's built-in header bar. They appear in the trailing (right) slot of the bar, next to the back/close button. Each action shows a text label or an SVG icon, and can carry a small badge.

<p align="center">
  <img src="images/animated-label-and-page-actions.png" width="210" alt="An SVG page action (gear) in the header bar">
  <img src="images/sheet-detents-medium.png" width="210" alt="A text page action (Save) in a sheet header">
  <img src="images/sheet-simple-blur.png" width="210" alt="Close as the secondary action, top right">
</p>
<p align="center"><sub>Page actions: an SVG icon, a text label, and close moved to the trailing slot</sub></p>

---

## Declaring page actions

Put `[PageAction]` on a `[RelayCommand]` method. Spine creates the `PageAction` once, before the page first appears, and wires it to the generated command:

```csharp
public partial class MainPageViewModel(INavigationService _navigation) : ViewModelBase
{
    // Icon-only button (SVG from Resources/Images)
    [PageAction(Svg = "settings.svg")]
    [RelayCommand]
    private async Task OpenSettings() => await _navigation.NavigateToAsync<SettingsPage>();

    // Text button
    [PageAction("Save")]
    [RelayCommand]
    private async Task Save() { /* ... */ }
}
```

The command property is found by the toolkit's naming rule (`SaveAsync` and `Save` both give `SaveCommand`). The attribute also works on a property of type `ICommand`, and `Command = "MyCommand"` names the property explicitly when neither applies.

| Attribute property | Default | Description |
|---|---|---|
| `Text` (constructor) | `null` | Label; leave it out for an icon-only button |
| `Svg` | `null` | SVG resource name, e.g. `"settings.svg"` |
| `Placement` | `Secondary` | `Primary` (left) or `Secondary` (right) |
| `Order` | `0` | Order among the page's declared actions |
| `Badge` | `null` | Initial badge text |
| `IsVisible` | `true` | Initial visibility |
| `Command` | `null` | Name of the command property, when not derived from the member |

### Adding actions by hand

`PageActions` is an ordinary observable collection. Add to it from the constructor, or at any time while the page shows; the header bar follows both additions and removals:

```csharp
PageActions.Add(new PageAction(text: "Cancel", command: CancelCommand) { Placement = PageActionPlacement.Primary });
PageActions.Remove(cancel);
```

Declared actions are added before the page first appears and stay for the life of the view model, so there is nothing to guard against in `OnAppearingAsync`.

---

## Changing an action while the page shows

`PageAction` is observable. Set a property on the instance and the button updates in place:

```csharp
private PageAction FilterAction => PageActions.First(a => a.Command == FilterCommand);

void OnFilterChanged(int count)
{
    FilterAction.Text = count > 0 ? $"Filter ({count})" : "Filter";
    FilterAction.Badge = count > 0 ? count.ToString() : null;
}

void OnEditingChanged(bool editing) => FilterAction.IsVisible = !editing;
```

| Property | Type | Default | Description |
|---|---|---|---|
| `Text` | `string?` | — | Label shown on the button. `null` for icon-only buttons |
| `Svg` | `string?` | `null` | SVG resource name from `Resources/Images`; changing it cross-fades |
| `Badge` | `string?` | `null` | Short text in a small pill over the button, e.g. `"3"`; `null` hides it |
| `IsEnabled` | `bool` | `true` | Whether the button responds to taps |
| `IsVisible` | `bool` | `true` | Whether the button is shown; the next visible action in the slot takes over |
| `Command` | `ICommand` | — | Command executed when the button is tapped (fixed at creation) |
| `CommandParameter` | `object?` | `null` | Optional parameter forwarded to the command |
| `Placement` | `PageActionPlacement` | `Secondary` | `Primary` (left) or `Secondary` (right) slot (fixed at creation) |

---

## An action that opens a menu

`new PageAction(null, menu) { Svg = "more.svg" }` opens a native menu instead of running a command: sections, a picker with checkmarks, submenus, toggles and destructive rows, from one `MenuItems` declaration. See [Menu buttons](menus.md).

## Liquid Glass on iOS 26

On iOS 26 and Mac Catalyst 26 the header bar renders its back button and page actions as Liquid Glass, the way a `UINavigationBar` shows its items. The SVG keeps the tint `PageActionView` gives it (black in light theme, white in dark); a text action keeps the app's accent (`Primary`, or `IThemeService.Accent`). Turn it off in `UseSpine` with `options.Apple.GlassHeaderActions = false`. See [Glass buttons](glass-buttons.md) for the attached property behind it.

---

## Placement

| Value | Position | Typical use |
|---|---|---|
| `Secondary` | Trailing / right side | Page-specific actions (Save, Edit, Share) |
| `Primary` | Leading / left side | Navigation-level actions (Menu, Cancel) |

Each slot shows the **first visible** action with that placement. The back button occupies the `Primary` slot implicitly; an explicit `Primary` action replaces it for that page.

```csharp
[PageAction("Cancel", Placement = PageActionPlacement.Primary)]
[RelayCommand]
private async Task Cancel() => await _navigation.BackAsync();
```

---

## SVG icons

SVG images require **Plugin.Maui.Spine.Svg** (included transitively with Spine). Place `.svg` files in `Resources/Images/` and reference them by file name:

```csharp
[PageAction(Svg = "settings.svg")]
[RelayCommand]
private Task OpenSettings() { /* ... */ }
```

---

## Async commands

A `[RelayCommand]` on an async method gives an `IAsyncRelayCommand`; the action exposes it as `AsyncCommand` so the UI can bind to its busy state:

```csharp
[PageAction("Save")]
[RelayCommand]
private async Task SaveAsync() => await _dataService.SaveAsync();
```

---

## Desktop (Windows)

On Windows, when the native title bar is shown (`IsTitleBarVisible = true`), page actions are rendered inside the title bar chrome using `TitleBar.LeadingContent` and `TitleBar.TrailingContent`. The same `PageActions` collection drives both header bar and title bar contexts, including changes made while the page shows — no extra code needed.
