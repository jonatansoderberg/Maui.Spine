# Issue #754 — Widgets: controls on SVG icons

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/754
**Branch:** issue/754-widgets-controls-on-svg-icons
**Status:** Completed

Step W11 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), decision D8, on the spike W0 (#743).

## Changes

- `tools/SpineSymbols` is now `src/Plugin.Maui.Spine.Symbols.Tool`, a .NET tool (`Plugin.Maui.Spine.Symbols.Tool`, command `spine-symbols`) in `Spine.slnx`. An SVG that draws nothing is refused ("nothing is drawn") instead of throwing.
- `src/Plugin.Maui.Spine.Widgets/native/ios/SpineIcons.xcassets`: all 245 of `Plugin.Maui.Spine.Svg.Icons` as variable symbol templates (3.7 MB, 1.8 MB compiled). Bedroom and None draw nothing and are left out.
- `spine-widgets-build.sh` takes several `--assets`; the targets pass Spine's catalog before the app's `SpineWidgetsAssetCatalog` when the app declares a `<SpineControl>`.
- `ControlState.Symbol` is `[Obsolete]`; `Icon` is an SVG name on both platforms.
- Sample: its hand-made `Platforms/iOS/SpineWidgets.xcassets` and `SpineWidgetsAssetCatalog` are gone; the goal control uses `Plus`.
- Packaging: in `Spine.Packages.slnf`, with a README; `packages.md`.
- Docs: `widgets.md` controls (icons, the tool, troubleshooting); the plan's D8 and W11 row.

## Verification

- The sample built for the iPhone simulator: the extension's `Assets.car` (1.8 MB) holds all of Spine's symbols, `Plus`, `Bell` and `ArrowUpRight` among them, with no catalog of the sample's own.
- `dotnet pack` of the tool, `dotnet tool install` from it, and `spine-symbols` on `Bell.svg` wrote a symbolset.
- Control Center drawing these symbols was proven in W0 (#743); not looked at again on a device.
- `Plugin.Maui.Spine.Server.Tests`: 308 passed; Widgets builds for Android.
