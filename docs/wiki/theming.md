# Theming

`IThemeService` owns the app's light/dark theme: the user's choice, what is in effect, a change
signal that fires once the palette is in place, and a repaint hook for views that colour
themselves in code.

## The user's choice

```csharp
public partial class SettingsPageViewModel(IThemeService _theme) : ViewModelBase
{
    [RelayCommand]
    private void UseDark() => _theme.Current = AppTheme.Dark;      // applied at once, stored

    [RelayCommand]
    private void FollowSystem() => _theme.Current = AppTheme.Unspecified;
}
```

`Current` is what the user picked; `Unspecified` follows the system. Spine stores it in
`Preferences` and applies it again at the next launch, in the application's constructor, before the
first page is built and on Android before the activity inflates its window, so a dark app does not
flash light. Turn the storage off with `options.Theme.Persist = false`.

`Effective` is `Light` or `Dark`, never unspecified: what is on screen right now.

## Android background

MAUI's activity declares `UiMode` among its configuration changes, so a theme switch does not
recreate it and the window keeps the background it was inflated with. Spine paints the window with
the theme's background colour on every change, for the plain host and for the tab host, so an app
no longer needs a `RequestedThemeChanged` handler that pushes a page background.

## Token dictionaries

Declare the same keys in a light and a dark `ResourceDictionary` and register both:

```csharp
.UseSpine(options =>
{
    options.Theme.UseTokens<LightTokens, DarkTokens>();
})
```

Spine merges one dictionary into the application resources and copies the right set into it on
every change. Consume tokens with `{DynamicResource}`:

```xml
<Border BackgroundColor="{DynamicResource CardBackground}" Stroke="{DynamicResource CardOutline}">
    <Label TextColor="{DynamicResource CardText}" Text="…" />
</Border>
```

A `{StaticResource}` keeps the value it captured at load, and a `DataTrigger` restores the value it
replaced, which after a switch is the old theme's colour. Use `{DynamicResource}` for tokens and
switch between two styled views for a selected state rather than triggering colours.

## Tab bar colours from keys

`SpineTabBarStyle` takes resource keys next to its fixed colours. A key is read from the application
resources when the bar is styled and again after every theme change, so a token that differs
between light and dark follows the switch; a key that resolves wins over the fixed colour.

```csharp
options.Tabs.Style = new SpineTabBarStyle
{
    SelectedColorKey = "BrandTint",
    UnselectedColorKey = "TextSecondary",
    BadgeBackgroundColorKey = "Signal",
};
```

## Reacting to a change

`Changed` is raised on the UI thread after the token dictionary has been swapped, so a handler
reads a consistent palette. `Version` is bumped before it is raised.

```csharp
public override Task OnAppearingAsync(NavigationDirection direction)
{
    _theme.Changed += OnThemeChanged;
    return Task.CompletedTask;
}

public override Task OnDisappearingAsync(NavigationDirection direction)
{
    _theme.Changed -= OnThemeChanged;
    return Task.CompletedTask;
}
```

The service is a singleton, so a transient page must unsubscribe or it is kept alive by the event.

## Views that colour themselves in code

A Skia-drawn control, or any view that assigns colours once when it builds itself, sees neither
`AppThemeBinding` nor `DynamicResource`. Ask to be told instead:

```csharp
public sealed class Gauge : SKCanvasView
{
    public Gauge()
    {
        SpineTheme.Track(this, InvalidateSurface);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var accent = (Color)Application.Current!.Resources["CardAccent"];
        …
    }
}
```

`Track` (also on `IThemeService`) runs the callback after every change while the view is attached
to a window, and once when the view is attached again if the theme changed meanwhile, so a page
that was not on screen during the switch does not come back in the old colours. It does not run
the callback at registration; paint the initial state yourself.

Nothing is unsubscribed by hand. Spine holds the subscription weakly and the view holds it through
its own `HandlerChanged` handler, so view, callback and subscription are collected together with
the page, whether or not MAUI disconnected its handler. All subscriptions are dropped when a new
window is built; a live view lists itself again on its next handler change.

`SpineTheme.Version` (or `IThemeService.Version`) is the counter behind the catch-up. A control
that keeps a per-instance options object with colours can record the version it copied the app-wide
defaults at and copy them again when the version moved, so overriding one padding value does not
opt the control out of theming.

## Spine's own parts

The header bar title, page action buttons and the Android status bar icons follow the theme on
their own. The tab bar follows it on Android (Material colours re-read from the activity theme) and
on every platform where its style uses keys.
