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

## Target frameworks

| Package | Frameworks |
|---|---|
| MAUI packages (core, Svg, Widgets, PushNotifications, the controls) | `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0` |
| `Plugin.Maui.Spine.Svg.Icons`, `Plugin.Maui.Spine.Common`, `Plugin.Maui.Spine.Server` | `net10.0` |

Platform minimums: Android API 21 (API 23 with `Plugin.Maui.Spine.PushNotifications`, which Firebase requires), iOS 15, Mac Catalyst 15, Windows 10 17763.

## Build assets in the packages

Three packages carry MSBuild targets that run in the consuming app's build, imported automatically through `buildTransitive/`:

| Package | What its targets do |
|---|---|
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
