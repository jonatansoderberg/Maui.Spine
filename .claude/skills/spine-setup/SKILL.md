---
name: spine-setup
description: Set up Plugin.Maui.Spine in a .NET MAUI app from NuGet — which packages to install, how to register them in MauiProgram, the SpineApplication root, project-file entries, platform minimums, and the iOS/Android setup that widgets and push need. Use when adding Spine to an app, upgrading it, or when a Spine build or startup fails. Invoke as /spine-setup.
---

You are adding or configuring **Plugin.Maui.Spine** (a code-first navigation framework for .NET MAUI: regions, bottom sheets, tab host, header bar, widgets, push notifications) in the app the user is working on. The packages come from nuget.org; the source and the full documentation are at https://github.com/jonatansoderberg/Maui.Spine.

Work through the steps below in order, skipping the ones the app already has. Read the existing `MauiProgram.cs`, `App.xaml`, the csproj and `Platforms/` before changing anything.

---

## 1. Pick the packages

All Spine packages share one version. Reference every Spine package the app uses at the **same version**, and look up the current one on nuget.org (`https://www.nuget.org/packages/Plugin.Maui.Spine`) rather than guessing.

| The app needs | Install | Brings in |
|---|---|---|
| Navigation: regions, sheets, tabs, header bar, glass buttons, shortcuts, Windows windowing | `Plugin.Maui.Spine` | `Plugin.Maui.Spine.Svg` |
| The built-in icon set (219 SVG glyphs, resolved by file name) | `Plugin.Maui.Spine.Svg.Icons` | — |
| Home-screen widgets and Live Activities from C# | `Plugin.Maui.Spine.Widgets` | the core and `Plugin.Maui.Spine.Common` |
| Push and local notifications | `Plugin.Maui.Spine.PushNotifications` | `Plugin.Maui.Spine.Common` (not the core) |
| The push backend, in an ASP.NET Core or Azure Functions project | `Plugin.Maui.Spine.Server` | `Plugin.Maui.Spine.Common` |
| `HeroCollectionView` (collapsing hero header) | `Plugin.Maui.Spine.Controls.HeroCollectionView` | `Plugin.Maui.Spine.Svg` |
| `AnimatedLabel` (marquee) | `Plugin.Maui.Spine.Controls.AnimatedLabel` | — |
| A domain or test project that builds widget trees without MAUI | `Plugin.Maui.Spine.Common` | — |

```bash
dotnet add package Plugin.Maui.Spine
dotnet add package Plugin.Maui.Spine.Widgets            # if widgets
dotnet add package Plugin.Maui.Spine.PushNotifications  # if notifications
```

With central package management, add matching `<PackageVersion>` lines to `Directory.Packages.props` instead of versions in the csproj.

The MAUI packages target `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst` and `net10.0-windows10.0.19041.0`; `Common`, `Server` and `Svg.Icons` target `net10.0`. The app needs .NET 10 and the `maui` workload.

## 2. Register in `MauiProgram.cs`

`UseSpine` registers every Spine package the app references — the SVG pipeline, Widgets, PushNotifications, AnimatedLabel; Calendar, DataGrid and HeroCollectionView need no registration at all. The list is generated at build time by `Plugin.Maui.Spine`'s build targets (no scanning at startup). Call a package's `UseXxx(o => …)` only to set its options; the order does not matter — every call configures the same options instance, before or after `UseSpine`.

```csharp
using Plugin.Maui.Spine.Extensions;

var builder = MauiApp.CreateBuilder();
builder
    .UseMauiApp<App>()
    .UseSpine(options =>
    {
        options.AddAssembly(typeof(MauiProgram).Assembly);   // never Assembly.GetEntryAssembly(): null on Android
        options.AppTitle = "My App";
        options.Theme.UseTokens<LightTokens, DarkTokens>();  // optional: two ResourceDictionaries with the same keys, swapped on theme change
    })
    // Widgets, AnimatedLabel, … are registered by UseSpine; these calls only configure:
    .UseSpineWidgets(o => o.OpenWith<HomePage>())             // optional: Plugin.Maui.Spine.Widgets options
    .UseSpinePushNotifications(o =>                           // Plugin.Maui.Spine.PushNotifications: backend, handler
    {
        o.Backend = new Uri("https://api.example.com/push/"); // leave unset for local-only notifications
        o.UseHandler<MyPushHandler>();
    });

return builder.Build();
```

Without `UseSpine` (a control package on its own) nothing is registered automatically: call `UseAnimatedLabel()`, `UseSpinePushNotifications(…)` or `UseEmbeddedSvgImages(…)` yourself. If a package seems unregistered under `UseSpine`, look for `obj/<config>/<tfm>/SpineModules.g.cs` in the app: it lists what the build found. Referencing Spine as projects instead of packages means importing `Plugin.Maui.Spine`'s `build/Plugin.Maui.Spine.targets` and the packages' `build/*.props` yourself (the repo's `samples/Directory.Build.targets` shows how).

`options.AddAssembly` is where Spine scans for `[NavigableRegion]`, `[NavigableSheet]`, `[NavigableTab]` and `[Widget]` classes and for embedded SVGs. Add every assembly that holds pages or widget providers.

## 3. The application root

Replace MAUI's `Application` with `SpineApplication<TRootPage>`. The type argument is the first page — with tabs, one of the `[NavigableTab]` pages.

```xml
<!-- App.xaml -->
<SpineApplication
    xmlns="http://schemas.microsoft.com/dotnet/maui/global"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    x:TypeArguments="MainPage"
    x:Class="MyApp.App">
    <SpineApplication.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Resources/Styles/Colors.xaml" />
                <ResourceDictionary Source="Resources/Styles/Styles.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </SpineApplication.Resources>
</SpineApplication>
```

```csharp
// App.xaml.cs
public partial class App { public App() => InitializeComponent(); }
```

Delete MAUI's `AppShell.xaml` and any `MainPage = …` assignment; Spine owns the window.

## 4. Global usings and XAML namespaces

`GlobalUsings.cs`:

```csharp
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;
global using Plugin.Maui.Spine.Core;
```

`GlobalXmlns.cs` puts Spine's types and the app's page namespaces on MAUI's global xmlns, so XAML needs no prefixes:

```csharp
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "Plugin.Maui.Spine.Core", AssemblyName = "Plugin.Maui.Spine")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "Plugin.Maui.Spine.Presentation", AssemblyName = "Plugin.Maui.Spine")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "Plugin.Maui.Spine.Extensions", AssemblyName = "Plugin.Maui.Spine")]   // Glass.Style
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "Plugin.Maui.Spine.Svg", AssemblyName = "Plugin.Maui.Spine.Svg")]       // SvgImageSource.*
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "MyApp.Pages")]
```

Add one line per page namespace (`MyApp.Pages.Settings`, …) and, when used, `"Plugin.Maui.Spine.Controls", AssemblyName = "Plugin.Maui.Spine.Controls.HeroCollectionView"` / `"…AnimatedLabel"`.

## 5. Project-file entries for pages

Every page is three files (`MyPage.cs`, `MyPage.View.xaml`, `MyPage.ViewModel.cs`; see `/spine-page`). `*.ViewModel.cs` files are excluded by default, so each page needs two csproj entries — and no bare `<Compile Include>` duplicate, which Visual Studio likes to add:

```xml
<ItemGroup>
  <Compile Remove="**\*.ViewModel.cs" />
</ItemGroup>

<ItemGroup>
  <MauiXaml Update="Pages\MainPage.View.xaml">
    <Generator>MSBuild:Compile</Generator>
    <DependentUpon>MainPage.cs</DependentUpon>
  </MauiXaml>
  <Compile Include="Pages\MainPage.ViewModel.cs">
    <DependentUpon>MainPage.cs</DependentUpon>
  </Compile>
</ItemGroup>
```

## 6. Embedded SVGs

The app's own icons are embedded resources, resolved by short file name anywhere Spine takes an SVG (`SvgImageSource.Svg="tab_home.svg"`, `PageAction.Svg`, `[NavigableTab(Icon = …)]`):

```xml
<EmbeddedResource Include="Resources\Svg\*.svg" />
```

Reference `Plugin.Maui.Spine.Svg.Icons` for a ready-made set instead; nothing else is needed, its files resolve the same way. The header bar's own back and close glyphs ship with the core.

## 7. Platform minimums and manifests

| | Value | Why |
|---|---|---|
| Android `SupportedOSPlatformVersion` | 21; **23 with `Plugin.Maui.Spine.PushNotifications`** | Firebase Messaging declares 23; the build says so if you forget |
| iOS / Mac Catalyst `SupportedOSPlatformVersion` | 15.0 (widgets need iOS 17 at runtime; the extension targets 17 by default) | |
| Windows | 10.0.17763.0 minimum, `net10.0-windows10.0.19041.0` target | |

### Widgets (iOS)

```xml
<!-- MyApp.csproj -->
<ItemGroup>
  <SpineWidget Include="next-event" DisplayName="Next event" Description="…" Families="Small,Medium" />
</ItemGroup>
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">
  <CodesignEntitlements>Platforms\iOS\Entitlements.plist</CodesignEntitlements>
</PropertyGroup>
```

```xml
<!-- Platforms/iOS/Entitlements.plist: the app shares a container with the widget extension -->
<key>com.apple.security.application-groups</key>
<array><string>group.com.example.myapp</string></array>
```

The extension's bundle id is `$(ApplicationId).SpineWidgets` (override with `SpineWidgetsExtensionName`); device builds need an App ID and a profile for it too, with the App Group on both. The extension is compiled with `swiftc` during the iOS build, so iOS needs macOS with Xcode; Android needs nothing extra. Simulator builds sign ad hoc: the targets set `CodesignKey=-` themselves when none is configured.

### Push notifications

```csharp
// Platforms/iOS/Program.cs — before UIApplication.Main
SpinePushNotifications.Install();
UIApplication.Main(args, null, typeof(AppDelegate));
```

Android: `Platforms/Android/google-services.json` from a Firebase project whose package name is the app's `ApplicationId`, and

```xml
<GoogleServicesJson Include="Platforms\Android\google-services.json"
                    Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'" />
```

Spine's build contributes `aps-environment` on Apple (`development` in Debug, `production` otherwise; override with `SpinePushNotificationsEnvironment`). Local-only apps set `<SpinePushNotificationsRemote>false</SpinePushNotificationsRemote>` and leave `Backend` unset.

The packages' MSBuild targets are imported automatically through `buildTransitive`; nothing to import by hand when using NuGet.

## 8. Verify

```bash
dotnet build -f net10.0-android
dotnet build -f net10.0-ios -r iossimulator-arm64 -p:CodesignKey=-   # macOS
```

Then run and check the log for Spine's startup warnings: a `[Widget]` kind with no `<SpineWidget>` item, a missing `google-services.json`, a page attribute problem. Spine fails fast and names what is wrong; read the message before changing configuration.

## Gotchas

- `Assembly.GetEntryAssembly()` is `null` on Android — always `typeof(MauiProgram).Assembly`.
- Do not mix Spine versions; every `Plugin.Maui.Spine*` reference at the same version.
- A bare `<Compile Include="…ViewModel.cs">` next to the `<DependentUpon>` one gives duplicate-compilation errors.
- Two `[NavigableTab]` pages with the same `Order` fail startup validation; set `Order` on every tab.
- `NU1608` warnings about AndroidX `LiveData.Core` / `Fragment` are the normal state of a MAUI app with Firebase in it.
- Strings: embed `Resources/Strings/strings.xml` and `strings.<culture>.xml` (`<EmbeddedResource Include="Resources\Strings\*.xml" WithCulture="false" />`; without `WithCulture="false"` MSBuild moves `strings.sv.xml` into a satellite assembly), read them with `{String Key}` in XAML or `ISpineStrings` in C#, switch with `ISpineStrings.Culture` (stored). Override a Spine key such as `Header.Back` by defining it in the app's document. See docs/wiki/strings.md.
- Theme: set `IThemeService.Current` (stored, applied at the next launch), never `Application.UserAppTheme`; no `RequestedThemeChanged` handler for the Android page background, Spine paints the window. Views that colour themselves in code call `SpineTheme.Track(this, Repaint)`. Accent: `IThemeService.Accent = new SpineAccent(light, dark)` (stored; null = the app's `Primary`/`PrimaryDark`) writes `Primary`, `PrimaryDark`, `Accent` and `OnAccent` and repaints like a theme switch; style with `{DynamicResource Accent}` / `{DynamicResource OnAccent}`, not `AppThemeBinding` over `StaticResource Primary`; code reads `SpineTheme.GetAccent(theme)`. See docs/wiki/theming.md.
- Windows builds only on Windows; iOS and Mac Catalyst class libraries build on Windows, but an iOS *app* (and the widget extension) needs a Mac.

## Documentation

- Getting started: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/getting-started.md
- Packages and dependencies: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/packages.md
- Widgets setup: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/widgets.md#setup
- Push setup and platform requirements: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications.md
- Sample apps: https://github.com/jonatansoderberg/Maui.Spine/tree/master/samples
