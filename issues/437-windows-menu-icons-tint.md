# Issue #437 — Windows: menu icons are rendered black regardless of theme

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/437
**Branch:** issue/437-windows-menu-icons-tint
**Status:** Completed (needs verification on Windows: #501)

## Plan
`SpineExtensions.BuildIcon` (`Platforms/Windows/MenuExtensions.Windows.cs`) rendered every menu icon as a black 16 px PNG and showed it in an `ImageIcon`, which draws the bitmap as it is. In the dark theme the icons would be black on a dark flyout; a destructive item's `Firebrick` and a disabled item's dimming did not reach the icon; and the bitmap was rendered at scale 1.

`BuildIcon` is the one icon path for every Windows menu:
- menu buttons (`MenuButton.Items`, #318) through `ApplyMenu` → `FillFlyout`,
- context menus (#306) through `ContextMenuState.OnOpening` → `FillContextFlyout` → `FillFlyout`,
- action sheets (`ActionSheetPresenter.Windows.cs`), which call `BuildIcon(services, svg)` directly.

Approach, as the issue proposes: let WinUI tint the icon, the way the template image does on Apple.
1. Return a `BitmapIcon` with `ShowAsMonochrome = true`. It keeps only the bitmap's alpha and fills it with the inherited foreground, which the `MenuFlyoutItem` template sets per visual state (normal, pointer over, pressed, disabled) from theme resources. A destructive item's `Foreground` reaches it by inheritance.
2. Render the PNG at `16 × XamlRoot.RasterizationScale` pixels (falling back to `DeviceDisplay.MainDisplayInfo.Density` when the element is not in a tree yet). The template's 16 × 16 `Viewbox` around the icon scales it back to 16 effective pixels.
3. `BitmapIcon` takes only a `UriSource`, so write each PNG once to `FileSystem.CacheDirectory/spine-menu-icons/<content hash>.png` and point the icon at that file — the same pattern the Windows tray menu already uses for its shortcut icons (`SpineApplication.Windows.cs`).

Nobody can run Windows here; the change is compiled on the Mac only. Windows verification is tracked in #501.

## Changes
- `Platforms/Windows/MenuExtensions.Windows.cs`: `BuildIcon` returns a monochrome `BitmapIcon` instead of an `ImageIcon` with a black bitmap. It takes the `XamlRoot` to read the raster scale from; the handler overload passes the platform view's. `MenuIconFile` writes the PNG under the cache directory, named by the first 32 hex digits of its SHA-256, through a temporary file and a rename, and remembers the path per process. The async `BitmapImage.SetSourceAsync` helper is gone.
- `Presentation/ActionSheetPresenter.Windows.cs`: passes the target element's `XamlRoot` to `BuildIcon`.
- Context menus need no change of their own: they build through `FillFlyout` on `Opening`, when the host is in the tree, so they get the right scale.

## Verification
- `dotnet build src/Plugin.Maui.Spine/Plugin.Maui.Spine.csproj -f net10.0-windows10.0.19041.0 -p:EnableWindowsTargeting=true "-p:SpineMauiTargetFrameworks=net10.0-windows10.0.19041.0" -p:AppxGeneratePriEnabled=false -p:EnableMsixTooling=false` (with `/usr/local/share/dotnet/dotnet`, SDK 10.0.201) compiles on the Mac with no new warnings.
- `net10.0-android`, `net10.0-ios` and `net10.0-maccatalyst` still build (only Windows files changed).
- Not run on Windows. What to check is listed in #501.

## Decisions
- **`BitmapIcon` with `ShowAsMonochrome` over re-tinting an `ImageIcon`.** Re-rendering the bitmap in the item's colour would mean resolving the template's theme brushes per visual state and rebuilding on `ActualThemeChanged`, hover, press and `IsEnabled`. The monochrome icon gets all of these from the template, live, like `UIImageRenderingMode.AlwaysTemplate` on Apple. A `PathIcon` would be crisp at any scale but needs a single path geometry, which arbitrary SVGs (strokes, several shapes) do not give.
- **Multi-colour SVGs become single-colour in menus.** Monochrome uses only the alpha, so an icon with its own colours is drawn in the item's foreground. Apple's template images do the same, and menu icons in the Fluent style are single-colour.
- **A file per icon, named after its contents.** `BitmapIcon` has no `Source` that takes a stream or a `BitmapImage`. Hashing the PNG instead of the SVG name means a changed SVG in a new app version, or a different raster scale, gets a new file without any invalidation. Files are small and are not cleaned up.
- **Scale at build time.** A menu button's flyout is built when the menu is mapped, possibly before the button is loaded; it then takes the main display's density. A window moved to a monitor with a different scale keeps the icons of the scale they were built at until the menu is rebuilt (an item change rebuilds it). Context menus rebuild on every open, so they always match.
- **A failed write is not swallowed.** Only the race where another writer created the same file first is caught; any other I/O error surfaces instead of leaving a blank icon.
- **`SegmentedControl.Windows.cs` is left alone.** It also uses an `ImageIcon`, but already picks its tint from the theme and is not a menu; it can move to the same approach separately.
