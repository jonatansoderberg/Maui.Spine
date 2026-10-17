---
name: spine-setup
description: Set up Plugin.Maui.Spine in a .NET MAUI app from NuGet — which packages to install, how to register them in MauiProgram, the SpineApplication root, project-file entries, platform minimums, and the iOS/Android setup that widgets, push and the camera scanner need. Use when adding Spine to an app, upgrading it, or when a Spine build or startup fails. Invoke as /spine-setup.
---

You are adding or configuring **Plugin.Maui.Spine** (a code-first navigation framework for .NET MAUI: regions, bottom sheets, tab host, header bar, widgets, push notifications) in the app the user is working on. The packages come from nuget.org; the source and the full documentation are at https://github.com/jonatansoderberg/Maui.Spine.

Work through the steps below in order, skipping the ones the app already has. Read the existing `MauiProgram.cs`, `App.xaml`, the csproj and `Platforms/` before changing anything.

---

## 1. Pick the packages

All Spine packages share one version. Reference every Spine package the app uses at the **same version**, and look up the current one on nuget.org (`https://www.nuget.org/packages/Plugin.Maui.Spine`) rather than guessing.

| The app needs | Install | Brings in |
|---|---|---|
| Navigation: regions, sheets, tabs, header bar, glass buttons, shortcuts, Windows windowing | `Plugin.Maui.Spine` | `Plugin.Maui.Spine.Svg` |
| The built-in icon set (233 SVG glyphs, resolved by file name) | `Plugin.Maui.Spine.Svg.Icons` | — |
| Home-screen widgets, Live Activities, Control Center controls and Quick Settings tiles from C# | `Plugin.Maui.Spine.Widgets` | the core and `Plugin.Maui.Spine.Common` |
| Push and local notifications | `Plugin.Maui.Spine.PushNotifications` | `Plugin.Maui.Spine.Common` (not the core) |
| Background work while the app is closed (`[BackgroundTask]`), widgets kept fresh by a sync | `Plugin.Maui.Spine.BackgroundTasks` | the core and `Plugin.Maui.Spine.Common` |
| Sign-in against the app's own server: Sign in with Apple's sheet, email and password in a ready-made sheet, `[RequiresSignIn]` on a page or command, an `HttpClient` handler with the access token (`o.AddAuthentication(a => …)` in `UseSpine`, `ISpineAuth`, `AddSpineAuthentication()`) | `Plugin.Maui.Spine.Authentication` | the core and `Plugin.Maui.Spine.Common` |
| Sign in with Google: Credential Manager on Android, the server's broker in the browser elsewhere (`a.AddGoogle(g => g.ServerClientId = …)`) | `Plugin.Maui.Spine.Authentication.Google` | `Plugin.Maui.Spine.Authentication` |
| Passkeys through the platform's sheet (iOS, Mac Catalyst): `a.AddPasskeys()`, `<SpinePasskeyDomain>` in the project, `Passkeys(p => p.RelyingPartyId = …)` and `MapSpinePasskeyAssociations()` on the server | `Plugin.Maui.Spine.Authentication.Passkeys` | `Plugin.Maui.Spine.Authentication` |
| Sign in with Microsoft through MSAL and the broker: via the Spine server (`a.AddMicrosoft(m => m.ClientId = …)`) or straight to an Entra-protected API (`a.UseMicrosoft(m => { m.ClientId = …; m.Scopes.Add(…); })`) | `Plugin.Maui.Spine.Authentication.Microsoft` | `Plugin.Maui.Spine.Authentication` |
| Face ID, Touch ID, fingerprint or Windows Hello before a page or a command (`[RequiresUnlock]`, `IUnlock`), an app lock, or a page kept out of screenshots (`[PrivacyShield]`) | `Plugin.Maui.Spine.Biometrics` | the core and `Plugin.Maui.Spine.Common` |
| The push backend, in an ASP.NET Core or Azure Functions project | `Plugin.Maui.Spine.Server` | `Plugin.Maui.Spine.Common` |
| A spoken conversation with a realtime model on the app's server: barge-in, a transcript for `ChatView`, client tools, no key in the app (`UseSpineVoice`, `IVoiceSession`); under it the full-duplex engine with echo cancellation, levels and bands (`IVoiceAudio`, `VoiceFrame`; iOS and Mac Catalyst so far) | `Plugin.Maui.Spine.Voice` | `Plugin.Maui.Spine.Common`, Microsoft.Extensions.AI.Abstractions |
| An AI feature in the app: one `IChatClient` per named feature, on the device first (Apple Intelligence) and then the app's server, no keys in the app (`UseSpineAI`, `[FromKeyedServices("Name")] IChatClient`, `IAIUsage`; generated images from the server with `IAIAssets` and `GeneratedImage.Feature`/`.Key` on any `Image`) | `Plugin.Maui.Spine.AI`, plus `Microsoft.Maui.Essentials.AI` for an on-device model | `Plugin.Maui.Spine.Common` (not the core) |
| Language or image models from the server: named features in configuration, metering, prices, budgets, per-user limits (`AddSpineAI`, `IAIFeatures`, `MapSpineAI`) | `Plugin.Maui.Spine.Server.AI`, plus the vendor's Microsoft.Extensions.AI adapter (`Anthropic`, `Microsoft.Extensions.AI.OpenAI`) | — |
| Sign-in against the app's own server: native ID tokens (Apple, Google, Microsoft, OpenID Connect), email and password, Spine's own access and rotating refresh tokens, account deletion (`AddSpineAuthentication`, `MapSpineAuthentication`, `ISpineAuthEmailSender`, `IUserResolver`) | `Plugin.Maui.Spine.Server.Authentication` | `Plugin.Maui.Spine.Common` |
| Generated images made once and served from blob storage: a lookup that orders what is missing, a handler that generates through a Server.AI image feature, checks and archives (`AddSpineAIAssets`, `IGeneratedAssetPrompt<TKey>`, `IGeneratedAssetCheck<TKey>`, `AddGeneratedAssets`, `MapSpineAIAssets`) | `Plugin.Maui.Spine.Server.AI.Assets`, plus a messaging transport | `Plugin.Maui.Spine.Server.AI`, `Plugin.Maui.Spine.Server.Messaging` |
| Queues on the server: typed messages handled by Azure Functions triggers or a hosted pump, with routing, priority lanes, scheduling, retries, large payloads and cleanup (`AddSpineMessaging`, `IMessagePublisher`, `IMessageHandler<T>`, `MessageDispatcher`) | `Plugin.Maui.Spine.Server.Messaging.AzureStorage` (Storage Queues) or `Plugin.Maui.Spine.Server.Messaging.ServiceBus` | `Plugin.Maui.Spine.Server.Messaging` |
| `HeroCollectionView` (collapsing hero header) | `Plugin.Maui.Spine.Controls.HeroCollectionView` | `Plugin.Maui.Spine.Svg` |
| `AnimatedLabel` (marquee) | `Plugin.Maui.Spine.Controls.AnimatedLabel` | — |
| `Calendar` (month calendar, marked days) | `Plugin.Maui.Spine.Controls.Calendar` | the core |
| `DataGrid` (responsive row grid) | `Plugin.Maui.Spine.Controls.DataGrid` | the core |
| `SpineRow` / `SpineSection` (settings and key/value rows) | `Plugin.Maui.Spine.Controls.Rows` | the core and `Plugin.Maui.Spine.Controls.AnimatedLabel` |
| `Shimmer` and `Skeleton.IsActive` (skeleton loading) | `Plugin.Maui.Spine.Controls.Shimmer` | the core |
| `MeshBackground` (mesh gradients behind glass) | `Plugin.Maui.Spine.Controls.MeshBackground` | the core |
| `Highlight.Kind` (a glow while a model works, onboarding hints once per install) | `Plugin.Maui.Spine.Controls.Highlight` | the core |
| `MarkdownView` (markdown on native views: help, release notes, chat answers) | `Plugin.Maui.Spine.Controls.Markdown` | the core and Markdig |
| `ChatView` and `ChatComposer` (a conversation with any `IChatClient`) | `Plugin.Maui.Spine.Controls.Chat` | the core, `.Controls.Markdown`, `.Controls.Shimmer`, Microsoft.Extensions.AI.Abstractions |
| `VoiceView` (a voice conversation drawn: orb, bars, ring, avatar) | `Plugin.Maui.Spine.Controls.Voice` | the core, `Plugin.Maui.Spine.Voice`, SkiaSharp |
| QR codes and other barcodes on screen (`BarcodeView`, `Barcode.Encode`, SVG) | `Plugin.Maui.Spine.Barcodes` | — |
| Scanning codes with the camera (`BarcodeScannerView`, the scan sheet) | `Plugin.Maui.Spine.Scanner` | the core and `Plugin.Maui.Spine.Barcodes` |
| Remote images in lists: memory and disk cache, decoding at the view's size, prefetching, BlurHash placeholders | `Plugin.Maui.Spine.Images` | `Plugin.Maui.Spine.Common` (not the core) |
| A domain or test project that builds widget trees without MAUI | `Plugin.Maui.Spine.Common` | — |
| A server or test project that encodes barcodes or reads a light grid | `Plugin.Maui.Spine.Barcodes` (its `net10.0` build) | — |

```bash
dotnet add package Plugin.Maui.Spine
dotnet add package Plugin.Maui.Spine.Widgets            # if widgets
dotnet add package Plugin.Maui.Spine.PushNotifications  # if notifications
dotnet add package Plugin.Maui.Spine.BackgroundTasks    # if background tasks
dotnet add package Plugin.Maui.Spine.Biometrics         # if unlock with Face ID, fingerprint or Windows Hello
dotnet add package Plugin.Maui.Spine.AI                 # if AI features through the app's Spine.Server.AI backend
```

With central package management, add matching `<PackageVersion>` lines to `Directory.Packages.props` instead of versions in the csproj.

The MAUI packages target `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst` and `net10.0-windows10.0.19041.0`; `Common`, `Server`, `Server.AI`, `Server.AI.Assets`, `Server.Authentication`, the `Server.Messaging` packages and `Svg.Icons` target `net10.0`; `Barcodes`, `Images`, `AI` and `Authentication` target `net10.0` as well as the MAUI frameworks. The app needs .NET 10 and the `maui` workload.

## 2. Register in `MauiProgram.cs`

`UseSpine` registers every Spine package the app references — the SVG pipeline, Widgets, PushNotifications, AnimatedLabel, DataGrid, Scanner, Images; Calendar, HeroCollectionView and Barcodes need no registration at all. The list is generated at build time by `Plugin.Maui.Spine`'s build targets (no scanning at startup). Call a package's `UseXxx(o => …)` only to set its options; the order does not matter — every call configures the same options instance, before or after `UseSpine`.

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

Without `UseSpine` (a control package on its own) nothing is registered automatically: call `UseAnimatedLabel()`, `UseSpinePushNotifications(…)`, `UseSpineScanner()`, `UseSpineImages()` or `UseEmbeddedSvgImages(…)` yourself (without Spine the scanner view works, the scan sheet does not). If a package seems unregistered under `UseSpine`, look for `obj/<config>/<tfm>/SpineModules.g.cs` in the app: it lists what the build found. Referencing Spine as projects instead of packages means importing `Plugin.Maui.Spine`'s `build/Plugin.Maui.Spine.targets` and the packages' `build/*.props` yourself (the repo's `samples/Directory.Build.targets` shows how).

`options.AddAssembly` is where Spine scans for `[NavigableRegion]`, `[NavigableSheet]`, `[NavigableTab]`, `[Widget]` and `[Control]` classes and for embedded SVGs. Add every assembly that holds pages or widget providers.

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

Add one line per page namespace (`MyApp.Pages.Settings`, …) and, when used, `"Plugin.Maui.Spine.Controls", AssemblyName = "Plugin.Maui.Spine.Controls.HeroCollectionView"` / `"…AnimatedLabel"`, `"Plugin.Maui.Spine.Barcodes", AssemblyName = "Plugin.Maui.Spine.Barcodes"` (`BarcodeView`) and `"Plugin.Maui.Spine.Scanner", AssemblyName = "Plugin.Maui.Spine.Scanner"` (`BarcodeScannerView`).

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
| Android `SupportedOSPlatformVersion` | 21; **23 with `Plugin.Maui.Spine.PushNotifications` or `Plugin.Maui.Spine.Scanner`** | Firebase Messaging and CameraX 1.6 declare 23; the build says so if you forget |
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

The extension's bundle id is `$(ApplicationId).SpineWidgets` (override with `SpineWidgetsExtensionName`); device builds need an App ID and a profile for it too, with the App Group on both. The extension is compiled with `swiftc` during the iOS and Mac Catalyst builds, so both need macOS with Xcode; Android needs nothing extra. On Mac Catalyst the App Group must be `<TeamID>.<name>` (or listed in a profile) and the build signed with a real certificate; see the widgets wiki, "Mac Catalyst". Simulator builds sign ad hoc: the targets set `CodesignKey=-` themselves when none is configured.

### Scanner

`NSCameraUsageDescription` in `Platforms/iOS/Info.plist` and `Platforms/MacCatalyst/Info.plist`; without it the scanner view reports the missing key instead of scanning. A sandboxed Mac Catalyst app also needs `com.apple.security.device.camera` in its entitlements. On Android the package declares `android.permission.CAMERA` itself; only the API 23 minimum above is needed. Windows has no scanner.

```xml
<key>NSCameraUsageDescription</key>
<string>Scans codes with the camera.</string>
```

### Biometrics

An iOS app needs `NSFaceIDUsageDescription` in `Platforms/iOS/Info.plist`; the build stops with a message until it is there (`<SpineBiometricsCheckFaceIdUsage>false</SpineBiometricsCheckFaceIdUsage>` skips the check). The text is the app's own:

```xml
<key>NSFaceIDUsageDescription</key>
<string>Unlocks your private pages with Face ID.</string>
```

Android needs nothing (the AndroidX Biometric library brings `USE_BIOMETRIC`); Windows uses Windows Hello. Register nothing: `UseSpine` registers the package; `builder.UseSpineBiometrics(o => o.UnlockValidFor = TimeSpan.FromMinutes(5))` only to change the options, and `o.AppLock(a => a.LockAfter = …)` to lock the whole app at launch and on return. See https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/biometrics.md.

### Background tasks

Nothing to declare by hand. On iOS the build adds `UIBackgroundModes: fetch` and one task identifier, `<ApplicationId>.spine.refresh`, to `Info.plist` (merged with the app's own modes and identifiers); `<SpineBackgroundTasksProcessing>true</SpineBackgroundTasksProcessing>` adds `processing` and `.spine.processing` for tasks marked `Long`. On Android the package declares its `JobService`, `RECEIVE_BOOT_COMPLETED` and `ACCESS_NETWORK_STATE`. Register nothing: `UseSpine` finds `[BackgroundTask]` classes in the assemblies it scans. With Widgets in the app, the widgets' background refresh becomes the task `spine.widgets`. Tasks never run on a clock: iOS decides, Android runs a job at most every 15 minutes, and Windows/Mac Catalyst run tasks only while the app is open. BGTaskScheduler does not run in the simulator (booking logs `BGTaskSchedulerErrorDomain error 1`). See https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/background-tasks.md.

```csharp
[BackgroundTask("standings", IntervalMinutes = 30, RequiresNetwork = true, Widgets = ["team"])]
public sealed class StandingsTask(IStandingsApi api, StandingsStore store) : IBackgroundTask
{
    public async Task RunAsync(BackgroundTaskRun run, CancellationToken ct) =>
        await store.SaveAsync(await api.FetchAsync(ct), ct);   // honour ct: ~30 s on iOS
}
```

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

## Feature availability

Ask before showing UI for a feature the device may lack. One rule across Spine: `IsSupported` = the platform and device can do it at all (fixed for the app's lifetime, synchronous, no permission prompt; static on static/attached-property classes such as `Motion.IsSupported`, `Haptics.IsSupported`, `BarcodeScannerView.IsSupported`, `Material.IsSupported(kind)`; instance on services such as `IWidgetService`, `ILiveActivityService`, `IControlService`, `ISearchIndex`, `IPushNotificationService`, `ILocalNotificationService`). `IsEnabled` (or a specific name such as `AreActivitiesEnabled`) = state the user or system can change (Reduce Motion, Live Activities off in Settings); read it when needed and never fold it into `IsSupported`. Table: docs/wiki/packages.md#feature-availability.

## Gotchas

- `Assembly.GetEntryAssembly()` is `null` on Android — always `typeof(MauiProgram).Assembly`.
- Do not mix Spine versions; every `Plugin.Maui.Spine*` reference at the same version.
- A bare `<Compile Include="…ViewModel.cs">` next to the `<DependentUpon>` one gives duplicate-compilation errors.
- Two `[NavigableTab]` pages with the same `Order` fail startup validation; set `Order` on every tab.
- `NU1608` warnings about AndroidX `LiveData.Core` / `Fragment` are the normal state of a MAUI app with Firebase in it.
- Strings: embed `Resources/Strings/strings.xml` and `strings.<culture>.xml` (`<EmbeddedResource Include="Resources\Strings\*.xml" WithCulture="false" />`; without `WithCulture="false"` MSBuild moves `strings.sv.xml` into a satellite assembly), read them with `{String Key}` in XAML or `ISpineStrings` in C#, switch with `ISpineStrings.Culture` (stored). Override a Spine key such as `Spine.Header.Back` by defining it in the app's document. See docs/wiki/strings.md.
- Theme: set `IThemeService.Current` (stored, applied at the next launch), never `Application.UserAppTheme`; no `RequestedThemeChanged` handler for the Android page background, Spine paints the window. Views that colour themselves in code call `SpineTheme.Track(this, Repaint)`. Accent: `IThemeService.Accent = new SpineAccent(light, dark)` (stored; null = the app's `Primary`/`PrimaryDark`) writes `Primary`, `PrimaryDark`, `Accent` and `OnAccent` and repaints like a theme switch; style with `{DynamicResource Accent}` / `{DynamicResource OnAccent}`, not `AppThemeBinding` over `StaticResource Primary`; code reads `SpineTheme.GetAccent(theme)`. See docs/wiki/theming.md.
- Spine.Voice: `UseSpineVoice(o => o.SessionEndpoint = …)` points at the server's `MapSpineRealtimeSessions` prefix (a realtime feature answers signed-in callers unless it sets `Callers`); `IVoiceSession` is transient, one per page; `Transcript` binds to `ChatView.Messages`; client tools are `AIFunction`s in `ClientTools`. A phone reaching a Mac's server needs the Mac's LAN address, the server on `0.0.0.0`, and `NSLocalNetworkUsageDescription`. The app writes `NSMicrophoneUsageDescription` (the engine fails at start naming the key, rather than ending the app); a Mac Catalyst app with its own Entitlements.plist adds `com.apple.security.device.audio-input`. One `IVoiceAudio` per conversation (transient). `SpineVoiceBackground=true` adds the iOS `audio` background mode; it is off by default. Android and Windows answer `VoiceAudioProblem.Unavailable` until #562 and #563. See docs/wiki/voice.md.
- Spine.AI: set `Backend` to the prefix given to `MapSpineAI` (a wrong one is an `HttpRequestException` naming the URL, not "off"); the server feature needs `"Expose": true`. A feature that is off throws `AIFeatureOffException` (in `Plugin.Maui.Spine.Common`, namespace `Plugin.Maui.Spine.AI`), which `TaskState` shows as empty. `AppleIntelligenceChatClient` needs `<NoWarn>$(NoWarn);MAUIAI0001</NoWarn>` and iOS/macOS 26; it throws at once where the model is unavailable (the simulator), and the chain falls back. `GeneratedImage` shows other views through `PendingView`/`FailedView` (`{x:Reference}`), because MAUI bindings cannot follow an attached property; `AssetsBackend` points at `MapSpineAIAssets` when images come from another service. Android talks to a server on the Mac through `adb reverse tcp:5100 tcp:5100` and `localhost`. See docs/wiki/ai-client.md.
- Server.AI: a feature is off (and logged at startup) when its provider's `ApiKey` entry is empty, its provider is not registered with `ai.Chat(...)`/`ai.Image(...)`, or it has no `Model`. `AddSpineAI` needs a usage store (`UseInMemoryUsageStore()` resets budgets on restart). Prices are `Ai:Prices:<provider>:<model>`. See docs/wiki/ai-server.md.
- Authentication (app): configured inside `UseSpine(o => o.AddAuthentication(a => { a.Server = new Uri(".../auth"); a.AddEmailPassword(); }))`; `UseSpine` throws without `Server` or a way to sign in. Sign in with Apple needs `<SpineSignInWithApple>true</SpineSignInWithApple>` and the capability on the App ID (new provisioning profile). Browser sign-in (`AddOpenIdConnect`, Apple on Android/Windows, Google off Android) needs a lowercase `a.CallbackScheme` and the same `<SpineAuthenticationCallbackScheme>` in the project. Google (`.Authentication.Google`) needs `ServerClientId` (the Web client id, also in the server's `Google(o => o.ClientIds)`), and on Apple targets `AddApple()` too (Guideline 4.8) unless `AppleNotRequired`. Microsoft (`.Authentication.Microsoft`) needs the Entra redirect URIs per platform and, on iOS, `msauthv2`/`msauthv3` in `LSApplicationQueriesSchemes` plus a `msauth.{bundle id}` URL type in Info.plist (the build stops without them; see the package README). `[RequiresSignIn]` on a command asks only through `{PageCommand}`, page actions, menus and `Tap.Command`, not a plain `{Binding}`. See docs/wiki/authentication.md.
- Server.Authentication: `AddSpineAuthentication` throws without `Issuer`, an EC P-256 `SigningKey` (PKCS#8 PEM), a store, and `ClientIds` on every provider; the host stops when `EmailPassword()` has no `ISpineAuthEmailSender` registered. Call `app.UseAuthentication()` before the endpoints. Apple needs `TeamId`/`KeyId`/`PrivateKey` to revoke on account deletion (a startup warning says so). A refresh token presented twice revokes that sign-in everywhere, so the client refreshes one at a time. See docs/wiki/authentication-server.md.
- Server.AI.Assets: needs `AddSpineAI` (the feature's `Kind` is `Image`), `m.AddGeneratedAssets()` inside `AddSpineMessaging`, and a pump or trigger for `spine-ai-assets`; the host stops at startup when one is missing. Keys are `IParsable<TKey>` whose `ToString()` parses back (letters, digits, `.`, `_`, `-`, `/` between segments). The blob container needs public blob access (or a CDN in `PublicBaseUrl`). Only `Asset:Expose` features answer `GET /ai/assets/...`. See docs/wiki/ai-assets.md.
- Server.Messaging: declare it once in a method every publishing and handling host calls (Storage Queues route on the publishing side). A subscriber name is 3–40 lowercase letters, digits and single hyphens, and a prioritised one also uses `<name>-high` and `<name>-low`. In Functions, one trigger per lane calls `dispatcher.DispatchAsync("<subscriber>", message, ct)`. Blob storage (`ConnectionStrings:blobs`) is needed with every transport. On the Service Bus emulator under Aspire, pass `servicebus.GetEndpoint("emulatorhealth")` as `EmulatorManagementEndpoint`. See docs/wiki/messaging.md.
- Windows builds only on Windows; iOS and Mac Catalyst class libraries build on Windows, but an iOS *app* (and the widget extension) needs a Mac.

## Documentation

- Getting started: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/getting-started.md
- Packages and dependencies: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/packages.md
- Widgets setup: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/widgets.md#setup
- Push setup and platform requirements: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications.md
- AI in the app (features, fallback, on-device usage): https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/ai-client.md
- AI on the server (features, providers, budgets): https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/ai-server.md
- Scanner setup: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/barcodes.md#setup
- Sample apps: https://github.com/jonatansoderberg/Maui.Spine/tree/master/samples
