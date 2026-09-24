# Packages

Spine ships as twelve NuGet packages built from this repository, one per project under `src/`. They share one version number and are released together; pick the ones the app needs.

| Group | Package | What it is | Depends on |
|---|---|---|---|
| Core | `Plugin.Maui.Spine` | Navigation, sheets, tab host, header bar, glass buttons, shortcuts, Windows windowing, theme and string stores | `.Svg`, `.Common` |
| Core | `Plugin.Maui.Spine.Svg` | Embedded SVG image sources, icon services, SVG-to-icon for tray and window icons | — |
| Core | `Plugin.Maui.Spine.Svg.Icons` | 166 ready-made SVG icons, resolved by file name once referenced | — (found by `.Svg` at startup) |
| Outside the window | `Plugin.Maui.Spine.Widgets` | Home-screen widgets and Live Activities from C# | `Plugin.Maui.Spine`, `.Common` |
| Outside the window | `Plugin.Maui.Spine.PushNotifications` | Push and local notifications | `.Common` |
| Controls | `Plugin.Maui.Spine.Controls.HeroCollectionView` | `CollectionView` with a collapsing hero header | `.Svg` |
| Controls | `Plugin.Maui.Spine.Controls.AnimatedLabel` | Marquee and fade label on SkiaSharp | — |
| Controls | `Plugin.Maui.Spine.Controls.Calendar` | Month calendar with swipe navigation, year and decade pickers and week numbers | `Plugin.Maui.Spine` |
| Controls | `Plugin.Maui.Spine.Controls.DataGrid` | Responsive row grid on `CollectionView` with layouts, sorting, grouping and swipe actions | `Plugin.Maui.Spine` |
| Controls | `Plugin.Maui.Spine.Controls.Shimmer` | Skeleton loading: `Shimmer` over placeholders, `Skeleton.IsActive` on real layouts | `Plugin.Maui.Spine` |
| Server | `Plugin.Maui.Spine.Common` | Contracts shared by app and server; no MAUI | — |
| Server | `Plugin.Maui.Spine.Server` | The push backend for ASP.NET Core and Azure Functions | `.Common` |

```
Common ◄──────────────┬──────────── Server
  ▲                   │
  │                   │
Widgets ──► Spine ──► Svg ◄── HeroCollectionView     Svg.Icons (loaded by Svg at startup)
  ▲           ▲
  │   Calendar, DataGrid, Shimmer
PushNotifications                                    AnimatedLabel
```

## Which packages for which app

| The app wants | Install |
|---|---|
| Navigation only | `Plugin.Maui.Spine` (brings `.Svg`) |
| Navigation with the built-in icon set | `Plugin.Maui.Spine`, `Plugin.Maui.Spine.Svg.Icons` |
| Widgets or Live Activities | `Plugin.Maui.Spine.Widgets` (brings the core and `.Common`) |
| Push or local notifications | `Plugin.Maui.Spine.PushNotifications` (brings `.Common`; the core is not required) |
| The push backend | `Plugin.Maui.Spine.Server` in the server project (brings `.Common`) |
| A domain or test project that builds widget trees without MAUI | `Plugin.Maui.Spine.Common` |

## Registration

`UseSpine()` registers every Spine package the app references; `MauiProgram` needs no other Spine call unless it changes a package's options. The packages declare themselves at build time (see [Build assets](#build-assets-in-the-packages)), so there is no assembly scanning or reflection at startup, and the list is trim- and AOT-safe.

| Package | With `UseSpine()` | Without `UseSpine()` |
|---|---|---|
| `Plugin.Maui.Spine.Svg` | Set up by `UseSpine`; call `UseSvgIcon(o => …)` only to change `SvgIconOptions` | `UseEmbeddedSvgImages(...)`, `UseSvgIcon()` |
| `Plugin.Maui.Spine.Svg.Icons` | Nothing | Nothing (found by `.Svg`) |
| `Plugin.Maui.Spine.Widgets` | Registered; call `UseSpineWidgets(o => …)` only to configure | Needs the core, so always with `UseSpine` |
| `Plugin.Maui.Spine.PushNotifications` | Registered; call `UseSpinePushNotifications(o => …)` to set the backend, channels, handler | `UseSpinePushNotifications(o => …)` |
| `Plugin.Maui.Spine.Controls.AnimatedLabel` | Registered | `UseAnimatedLabel()` |
| `Plugin.Maui.Spine.Controls.HeroCollectionView` | Nothing | Nothing (`UseHeroCollectionView()` still compiles, and does nothing) |
| `Plugin.Maui.Spine.Controls.Calendar`, `.DataGrid` | Nothing: strings register from the control's static constructor | Nothing |

Every `UseXxx()` is idempotent. The first call registers the package; a later call only applies its `configure` delegate to the same options instance. An explicit configuring call therefore works before or after `UseSpine()`, and the options end up with both. Settings that decide what gets registered (a widget background-refresh handler, a push handler) are applied after every call, and the platform callbacks read the options when they run, not when they are registered.

`UseSpine()` runs the package registrations last, after navigation, the SVG pipeline and strings are in place, in the order the build lists them. They do not depend on each other's order: Widgets and PushNotifications turn Live Activity push tokens on whichever of the two registers first.

`SpineTheme.Track` and `SpineStrings.Current` work without `UseSpine()` as well: the theme tracker follows `Application.RequestedThemeChanged` and `SpineStrings.Changed` itself until `SpineApplication` hands it to the `IThemeService`, and `SpineStrings` answers from the packages' built-in defaults.

### Adding a module to a package

A package that has to touch the `MauiAppBuilder` (handlers, SkiaSharp, lifecycle events, services) becomes a module; one that only needs its strings registers them from the control's static constructor with `SpineStrings.Current.AddDefaults(...)` instead, and needs nothing below.

1. Make the extension idempotent: return early when a marker (or the package's options instance) is already in `builder.Services`; with options, apply `configure` to the registered instance on every call. Read options in callbacks, not at registration.
2. Add `build/<PackageId>.props`:
   ```xml
   <Project>
   	<ItemGroup>
   		<SpineModule Include="<AssemblyName>" Register="<Namespace>.<Type>.<UseMethod>" />
   	</ItemGroup>
   </Project>
   ```
   `Include` is the assembly name (only referenced assemblies are registered); `Register` is a static method whose first parameter is the `MauiAppBuilder` and whose other parameters are optional.
3. Pack it: `<None Include="build\**" Pack="true" PackagePath="build\;buildTransitive\" />` in the csproj (already there when the package has a `build` folder).
4. Say in the package README and wiki page that `UseSpine()` registers it, and when the explicit call is still needed.

The samples reference the projects, not the packages; `samples/Directory.Build.targets` imports every `src/*/build/*.props` and the core's generator, so a new module is picked up there without further changes.

## Target frameworks

| Package | Frameworks |
|---|---|
| MAUI packages (core, Svg, Widgets, PushNotifications, the controls) | `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0` |
| `Plugin.Maui.Spine.Svg.Icons`, `Plugin.Maui.Spine.Common`, `Plugin.Maui.Spine.Server` | `net10.0` |

Platform minimums: Android API 21 (API 23 with `Plugin.Maui.Spine.PushNotifications`, which Firebase requires), iOS 15, Mac Catalyst 15, Windows 10 17763.

## Build assets in the packages

These packages carry MSBuild files that run in the consuming app's build, imported automatically through `buildTransitive/`:

| Package | What its build files do |
|---|---|
| `Plugin.Maui.Spine` | Writes `SpineModules.g.cs` into the app: a `[ModuleInitializer]` that hands each referenced package's registration to `UseSpine()` (from the `SpineModule` items below). Only in an app project; `SpineGenerateModuleRegistrations=false` turns it off |
| `Plugin.Maui.Spine.Widgets`, `.PushNotifications`, `.Controls.AnimatedLabel` | Declare their `SpineModule` in `<PackageId>.props` |
| `Plugin.Maui.Spine.Common` | Writes the app's iOS entitlements file once from the `SpineEntitlement` items the other two contribute |
| `Plugin.Maui.Spine.Widgets` | Compiles the WidgetKit extension and the bridge framework with `swiftc` on iOS; generates the manifest overlay and provider metadata on Android |
| `Plugin.Maui.Spine.PushNotifications` | Contributes the `aps-environment` entitlement; compiles the Notification Service Extension on iOS when `SpinePushNotificationsImages` is on |

The Swift sources ship under `native/` in the packages; nothing has to be added to the app for them. The native steps run only for inner iOS builds on macOS, so a Windows host and design-time builds are untouched.

## Versioning

All packages carry the same version. Releases follow [semantic versioning](https://semver.org/): a `0.x` release may change the API between minor versions; from `1.0` a breaking change means a new major version. Prereleases are `-preview.N`.

Consuming apps should reference every Spine package at the same version:

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Plugin.Maui.Spine" Version="0.1.0" />
<PackageVersion Include="Plugin.Maui.Spine.Widgets" Version="0.1.0" />
<PackageVersion Include="Plugin.Maui.Spine.PushNotifications" Version="0.1.0" />
```

How a release is made is described in [Releasing](releasing.md).
