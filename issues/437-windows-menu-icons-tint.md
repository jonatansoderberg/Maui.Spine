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

### PR #502 review fixes
- `MenuExtensions.Windows.cs`, `ApplyMenu`: a menu button's flyout is no longer filled at mapping time, when the button may not be in a window and `XamlRoot` is null. It is filled on `Opening`, like a context menu, and refilled on the next open after a `MenuObserver` change or when the window's `RasterizationScale` differs from the one it was filled at (a move to another monitor).
- `MenuExtensions.Windows.cs`, `BuildIcon`: the menu SVG is resolved once per resource name to an `SvgIcon` through `ISvgIconService.FromEmbeddedSvg` (as `ShortcutIcons.ForTray` does for the tray menu) and kept in a static dictionary; the PNG file comes from `SvgIcon.GetPngFilePath(round(16 × scale))`, which renders and writes only on the first request for that size. No per-call rasterizing, copying or SHA-256 any more. `MenuIconFile` and the `spine-menu-icons` folder are gone.
- `MenuExtensions.Windows.cs`: an SVG that is not embedded is logged once as a warning; an `IOException` or `UnauthorizedAccessException` while writing the PNG is logged as an error naming the SVG, the pixel size, the cache folder and the exception. Either way the item shows without an icon instead of the mapper or the `Opening` handler throwing. Category `Plugin.Maui.Spine.Menus`.
- `Plugin.Maui.Spine.Svg/SvgIcon.cs`, `AtomicWrite`: the temporary file is deleted after any failed write or move, not only after a lost race, and an `IOException` is swallowed only when the target file exists (the race); any other failure now surfaces instead of returning a path to a file that was never written.

## Verification
- `dotnet build src/Plugin.Maui.Spine/Plugin.Maui.Spine.csproj -f net10.0-windows10.0.19041.0 -p:EnableWindowsTargeting=true "-p:SpineMauiTargetFrameworks=net10.0-windows10.0.19041.0" -p:AppxGeneratePriEnabled=false -p:EnableMsixTooling=false` (with `/usr/local/share/dotnet/dotnet`, SDK 10.0.201) compiles on the Mac with no new warnings.
- `net10.0-android`, `net10.0-ios` and `net10.0-maccatalyst` still build (only Windows files changed).
- After the review fixes: the same Windows build, and `net10.0-android`, `net10.0-ios` and `net10.0-maccatalyst` of `Plugin.Maui.Spine` and `Plugin.Maui.Spine.Svg`, build with no new warnings.
- Not run on Windows. What to check is listed in #501, with the review-fix additions in a comment there.

## Decisions
- **`BitmapIcon` with `ShowAsMonochrome` over re-tinting an `ImageIcon`.** Re-rendering the bitmap in the item's colour would mean resolving the template's theme brushes per visual state and rebuilding on `ActualThemeChanged`, hover, press and `IsEnabled`. The monochrome icon gets all of these from the template, live, like `UIImageRenderingMode.AlwaysTemplate` on Apple. A `PathIcon` would be crisp at any scale but needs a single path geometry, which arbitrary SVGs (strokes, several shapes) do not give.
- **Multi-colour SVGs become single-colour in menus.** Monochrome uses only the alpha, so an icon with its own colours is drawn in the item's foreground. Apple's template images do the same, and menu icons in the Fluent style are single-colour.
- **A file per icon and pixel size, through `SvgIcon`.** `BitmapIcon` has no `Source` that takes a stream or a `BitmapImage`, so the PNG has to be a file. `SvgIcon.GetPngFilePath(int size)` already writes `{name}_{svg hash}_{size}px.png` atomically into `SvgIconOptions.CacheDirectory` and is what the Windows tray menu uses, so menus reuse it instead of a second cache. The SVG's content hash in the name means a changed SVG in a new app version gets a new file without invalidation; the raster scale is in the size suffix. The cache is shared with the tray icons and, like theirs, is not cleaned: one small file per icon and display scale.
- **Menu icons follow `SvgIconOptions`.** Going through `SvgIcon` means `PaddingPercent` and `LineWidthScale` apply to menu icons as they do to tray-menu icons (defaults: none and 1, the same as before). The tint options do not matter: a monochrome icon uses only the alpha.
- **Filled on open, refilled only when stale.** A menu button's flyout is filled on its first `Opening`, when the button is in a window and has a `XamlRoot`, and refilled on a later open only after an item change (`MenuObserver` marks it stale) or a change of `RasterizationScale`. Refilling on every open, as context menus do, would also work; keeping the observer avoids rebuilding an unchanged menu each time. An already open flyout is not refilled, as before. Before the first open, nothing is rendered.
- **A failed icon is logged, not thrown.** An icon is decoration; an unwritable cache or a missing SVG should not crash the app from a handler mapper or an `Opening` handler. The failure is logged with the SVG, size, folder and exception so it is visible, and the item shows without an icon. Only I/O failures are caught; an SVG that cannot be parsed still throws, as it does on the other platforms.
- **A failed write is not swallowed in `AtomicWrite`.** Only the race where another writer created the same file first is caught; any other I/O error surfaces (and is logged by the menu code) instead of leaving a path to a missing file. The tray menu and tray icon share this helper; they already threw on a failed write of the temporary file, now also on a failed move that was not a race.
- **`SegmentedControl.Windows.cs` is left alone.** It also uses an `ImageIcon`, but already picks its tint from the theme and is not a menu; it can move to the same approach separately.
