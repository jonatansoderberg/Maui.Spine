# Packages

Spine ships as thirty-four NuGet packages built from this repository, one per project under `src/`. They share one version number and are released together; pick the ones the app needs.

| Group | Package | What it is | Depends on |
|---|---|---|---|
| Core | `Plugin.Maui.Spine` | Navigation, sheets, tab host, header bar, glass buttons, shortcuts, Windows windowing, theme and string stores | `.Svg`, `.Common` |
| Core | `Plugin.Maui.Spine.Svg` | Embedded SVG image sources, icon services, SVG-to-icon for tray and window icons | — |
| Core | `Plugin.Maui.Spine.Svg.Icons` | 233 ready-made SVG icons, resolved by file name once referenced | — (found by `.Svg` at startup) |
| Outside the window | `Plugin.Maui.Spine.Widgets` | Home-screen widgets and Live Activities from C# | `Plugin.Maui.Spine`, `.Common` |
| Outside the window | `Plugin.Maui.Spine.PushNotifications` | Push and local notifications | `.Common` |
| Security | `Plugin.Maui.Spine.Authentication` | [Sign-in in the app](authentication.md): `ISpineAuth`, Sign in with Apple's sheet, email and password, `[RequiresSignIn]`, the `HttpClient` handler | `Plugin.Maui.Spine`, `.Common`, Microsoft.Extensions.Http |
| Security | `Plugin.Maui.Spine.Authentication.Google` | [Sign in with Google](authentication.md#sign-in-with-google): Credential Manager on Android, the broker elsewhere | `.Authentication`; on Android Xamarin.AndroidX.Credentials (+ PlayServicesAuth, GoogleId) |
| Security | `Plugin.Maui.Spine.Authentication.Microsoft` | [Sign in with Microsoft](authentication.md#sign-in-with-microsoft): MSAL with the broker, via the Spine server or directly | `.Authentication`, Microsoft.Identity.Client (+ `.Broker`, `.Extensions.Msal` on Windows) |
| Security | `Plugin.Maui.Spine.Authentication.Passkeys` | [Passkeys](authentication.md#passkeys) through the platform's sheet, iOS and Mac Catalyst | `.Authentication` |
| Security | `Plugin.Maui.Spine.Biometrics` | `IBiometricAuth` (Face ID, Touch ID, fingerprint, Windows Hello), `IUnlock`, `[RequiresUnlock]`, `Unlock.Command`, the app lock and `[PrivacyShield]`. Brings `Xamarin.AndroidX.Biometric` on Android | `Plugin.Maui.Spine`, `.Common` |
| Outside the window | `Plugin.Maui.Spine.BackgroundTasks` | `[BackgroundTask]` classes on BGTaskScheduler (iOS) and JobScheduler (Android), a timer while the app runs elsewhere | `Plugin.Maui.Spine`, `.Common` |
| Controls | `Plugin.Maui.Spine.Controls.HeroCollectionView` | `CollectionView` with a collapsing hero header | `.Svg` |
| Controls | `Plugin.Maui.Spine.Controls.AnimatedLabel` | Marquee and fade label on SkiaSharp | — |
| Controls | `Plugin.Maui.Spine.Controls.Calendar` | Month calendar with swipe navigation, year and decade pickers, week numbers and days marked from your own source | `Plugin.Maui.Spine` |
| Controls | `Plugin.Maui.Spine.Controls.DataGrid` | Responsive row grid on `CollectionView` with layouts, sorting, grouping and swipe actions | `Plugin.Maui.Spine` |
| Controls | `Plugin.Maui.Spine.Controls.Shimmer` | Skeleton loading: `Shimmer` over placeholders, `Skeleton.IsActive` on real layouts | `Plugin.Maui.Spine` |
| Controls | `Plugin.Maui.Spine.Controls.MeshBackground` | `MeshBackground`: mesh gradients from the accent, a preset or your own colours, drifting slowly | `Plugin.Maui.Spine` |
| Controls | `Plugin.Maui.Spine.Controls.Chat` | [`ChatView` and `ChatComposer`](chat.md): a conversation with any `IChatClient` | `Plugin.Maui.Spine`, `.Controls.Markdown`, `.Controls.Shimmer`, Microsoft.Extensions.AI.Abstractions |
| Controls | `Plugin.Maui.Spine.Controls.Markdown` | [`MarkdownView`](markdown.md): markdown on native views, rebuilt block by block while it streams | `Plugin.Maui.Spine`, Markdig |
| Controls | `Plugin.Maui.Spine.Controls.Voice` | [`VoiceView`](voice.md#drawing-the-conversation): a voice conversation drawn with SkiaSharp (orb, bars, ring, avatar), visuals of your own, visemes | `Plugin.Maui.Spine`, `.Voice`, SkiaSharp |
| Controls | `Plugin.Maui.Spine.Controls.Avatar` | [`AvatarView`](avatars.md): animated characters and ambient visuals from `.spineavatar` files, drawn with SkiaSharp and SceneKit, driven by the app, by text or by a driver | `Plugin.Maui.Spine`, `.Controls.Avatar.Core`, `.Speech`, SkiaSharp |
| Controls | `Plugin.Maui.Spine.Controls.Avatar.Voice` | [`VoiceAvatarDriver`](avatars.md#with-a-voice-conversation) and VoiceView's `character`: an avatar following a realtime voice conversation | `.Controls.Avatar`, `.Voice`, `.Controls.Voice` |
| Tool | `Plugin.Maui.Spine.Avatar.Tool` | The `spine-avatar` .NET tool for [`.spineavatar` files](avatars.md#making-a-package): inspect, validate (with the shipping profile), pack with rendered posters, strip `source/`, render sheets, report | `.Controls.Avatar.Core`, `.Controls.Avatar` (its Skia renderers), SkiaSharp |
| Controls | `Plugin.Maui.Spine.Controls.Avatar.Core` | The core of [avatars](avatars.md) with no MAUI: the `.spineavatar` format 1.0 (safe reader, schemas, defaults, validation), the scheduler, the driver contract with a text driver, the canonical visemes, a glTF reader and rig | — |
| Controls | `Plugin.Maui.Spine.Controls.Highlight` | `Highlight.Kind` on any view (Edge, Pulse, Sheen, Trace, Breathe), `Highlight.Repeat` and `.Key`, `IHighlights` | `Plugin.Maui.Spine` |
| Controls | `Plugin.Maui.Spine.Controls.Rows` | `SpineRow`: settings and key/value rows with icon, detail, value, accessory and chevron | `Plugin.Maui.Spine`, `.AnimatedLabel` |
| Controls | `Plugin.Maui.Spine.Barcodes` | QR, Data Matrix, Aztec, PDF417 and linear codes as a matrix, SVG or `BarcodeView`, with fixed sizes; `LightGridReader` for a code shown by lamps | — |
| Controls | `Plugin.Maui.Spine.Scanner` | Camera scanning: `BarcodeScannerView` and the `BarcodeScannerPage` sheet; Vision on Apple, ML Kit on Android | `Plugin.Maui.Spine`, `.Barcodes` |
| Controls | `Plugin.Maui.Spine.Images` | Every `UriImageSource` through a memory and disk cache, decoded at the view's size; `IImageCache`; BlurHash placeholders. Its `net10.0` build holds only the disk cache | `.Common` |
| Server | `Plugin.Maui.Spine.Common` | Contracts shared by app and server, and `BlurHash`; no MAUI | — |
| Server | `Plugin.Maui.Spine.Server` | The push backend for ASP.NET Core and Azure Functions | `.Common` |
| AI | `Plugin.Maui.Spine.Speech` | [Speech](speech.md): text-to-speech with the installed voices and their quality, and each word as the voice reaches it | — |
| AI | `Plugin.Maui.Spine.Voice` | [Voice](voice.md): spoken conversations with a realtime model (`IVoiceSession`) on the full-duplex audio engine with the platform's echo cancellation | `.Common`, Microsoft.Extensions.AI.Abstractions |
| AI | `Plugin.Maui.Spine.AI` | [AI in the app](ai-client.md): one `IChatClient` per named feature, on the device or the server, with fallback and on-device usage; generated images from the server on any `Image` | `.Common` |
| Server | `Plugin.Maui.Spine.Server.AI` | [AI on the server](ai-server.md): named features, metering, prices, budgets and limits on Microsoft.Extensions.AI | `.Common` |
| Server | `Plugin.Maui.Spine.Server.Authentication` | [Sign-in on the server](authentication-server.md): ID tokens checked, own access and refresh tokens, email and password, account deletion | `.Common`, Microsoft.AspNetCore.Authentication.JwtBearer, Azure.Data.Tables |
| Server | `Plugin.Maui.Spine.Server.AI.Assets` | [Generated assets](ai-assets.md): images from a Server.AI feature made once through messaging, archived in blob storage | `.Server.AI`, `.Server.Messaging`, Azure.Storage.Blobs |
| Server | `Plugin.Maui.Spine.Server.Messaging` | [Messaging](messaging.md): typed queues with routing, priority, scheduling, retries, large payloads and cleanup; in-memory transport | — |
| Server | `Plugin.Maui.Spine.Server.Messaging.AzureStorage` | Storage Queues as the transport, and blob storage for payloads and failed messages | `.Server.Messaging`, Azure.Storage.Queues, Azure.Storage.Blobs |
| Server | `Plugin.Maui.Spine.Server.Messaging.ServiceBus` | Azure Service Bus as the transport | `.Server.Messaging`, `.Server.Messaging.AzureStorage`, Azure.Messaging.ServiceBus |

```
Common ◄──────────────┬──────────── Server
  ▲                   │
  │                   │
Widgets ──► Spine ──► Svg ◄── HeroCollectionView     Svg.Icons (loaded by Svg at startup)
  ▲           ▲  ▲
  │           │  ├── BackgroundTasks (and Common; runs Widgets' refresh through Common)
  │           │  ├── Biometrics (and Common)
  │           │  └── Authentication (and Common) ◄── Authentication.Google (and AndroidX Credentials), Authentication.Microsoft (and MSAL), Authentication.Passkeys
  │   Calendar, DataGrid, Shimmer, Rows, MeshBackground, Highlight, Markdown, Scanner ──► Barcodes
  │   Chat ──► Markdown, Shimmer
PushNotifications                                    AnimatedLabel
  Images ──► Common
  Voice ──► Common, Speech
  Controls.Voice ──► Voice, Spine (and SkiaSharp)
  Controls.Avatar ──► Controls.Avatar.Core, Speech, Spine (and SkiaSharp)
  Speech (no Spine dependency)
  Controls.Avatar.Voice ──► Controls.Avatar, Voice, Controls.Voice
  Avatar.Tool ──► Controls.Avatar (net10.0), Controls.Avatar.Core (a .NET tool, not a library)
  AI ──► Common ◄── Server.AI (and Microsoft.Extensions.AI; Server.AI also Azure.Data.Tables)
  Common ◄── Server.Authentication (and JwtBearer, Azure.Data.Tables)
  Server.Messaging ◄── Server.Messaging.AzureStorage ◄── Server.Messaging.ServiceBus
  Server.AI, Server.Messaging ◄── Server.AI.Assets (and Azure.Storage.Blobs)
```

## Which packages for which app

| The app wants | Install |
|---|---|
| Navigation only | `Plugin.Maui.Spine` (brings `.Svg`) |
| Navigation with the built-in icon set | `Plugin.Maui.Spine`, `Plugin.Maui.Spine.Svg.Icons` |
| Widgets or Live Activities | `Plugin.Maui.Spine.Widgets` (brings the core and `.Common`) |
| Push or local notifications | `Plugin.Maui.Spine.PushNotifications` (brings `.Common`; the core is not required) |
| Work while the app is closed, or widgets kept fresh by a sync | `Plugin.Maui.Spine.BackgroundTasks` (brings the core and `.Common`) |
| Sign-in against the app's own server, a page or command behind it | `Plugin.Maui.Spine.Authentication` in the app (and `.Authentication.Google`, `.Authentication.Microsoft`, `.Authentication.Passkeys`), `Plugin.Maui.Spine.Server.Authentication` on the server |
| Face ID, fingerprint or Windows Hello before a page or a command | `Plugin.Maui.Spine.Biometrics` (brings the core and `.Common`) |
| QR codes and other barcodes on screen | `Plugin.Maui.Spine.Barcodes` (no camera, no permissions) |
| Scanning codes with the camera | `Plugin.Maui.Spine.Scanner` (brings the core and `.Barcodes`) |
| Lists of remote photos, prefetching, BlurHash placeholders | `Plugin.Maui.Spine.Images` (brings `.Common`; the core is not required) |
| A server that computes BlurHashes on upload | `Plugin.Maui.Spine.Common` (`BlurHash.Encode`) |
| The push backend | `Plugin.Maui.Spine.Server` in the server project (brings `.Common`) |
| A chat with a model, on any `IChatClient` | `Plugin.Maui.Spine.Controls.Chat` (brings the core, Markdown and Shimmer); with `Plugin.Maui.Spine.AI` for features on the app's server |
| Markdown text: help, release notes, chat answers | `Plugin.Maui.Spine.Controls.Markdown` (brings the core and Markdig) |
| A spoken conversation with a realtime model on the app's server, or the microphone and speaker at once with echo cancellation | `Plugin.Maui.Spine.Voice` (brings `.Common`), plus `NSMicrophoneUsageDescription` |
| That conversation drawn: an orb, bars, a ring or a face | `Plugin.Maui.Spine.Controls.Voice` (brings the core and `.Voice`) |
| An animated character or ambient visual from a `.spineavatar`: for an assistant, a guide, text mouthed or read aloud | `Plugin.Maui.Spine.Controls.Avatar` (brings the core, `.Controls.Avatar.Core` and `.Speech`) |
| Text read aloud with the best installed voice, word by word | `Plugin.Maui.Spine.Speech` |
| A voice assistant with a face: an avatar following a realtime conversation | `Plugin.Maui.Spine.Controls.Avatar.Voice` (brings the avatar and voice packages) |
| Language models in the app, on the device or through the app's server | `Plugin.Maui.Spine.AI` (brings `.Common`; the core is not required), plus the app's own on-device client such as Microsoft.Maui.Essentials.AI |
| Language or image models from the server, with costs and budgets | `Plugin.Maui.Spine.Server.AI` in the server project, plus the vendor's Microsoft.Extensions.AI adapter |
| Sign-in against the app's own server: Apple, Google, Microsoft, OpenID Connect, email and password | `Plugin.Maui.Spine.Server.Authentication` in the server project |
| Generated images made once and served from storage, ordered when missing | `Plugin.Maui.Spine.Server.AI.Assets` beside `.Server.AI` and a messaging transport |
| Queues on the server: work handled by Azure Functions or a hosted pump | `Plugin.Maui.Spine.Server.Messaging.AzureStorage` (or `.ServiceBus`) in each server project that publishes or handles |
| A domain or test project that builds widget trees without MAUI | `Plugin.Maui.Spine.Common` |
| A server or test project that encodes barcodes or reads a light grid | `Plugin.Maui.Spine.Barcodes` (its `net10.0` build has no MAUI) |

## Registration

`UseSpine()` registers every Spine package the app references; `MauiProgram` needs no other Spine call unless it changes a package's options. The packages declare themselves at build time (see [Build assets](#build-assets-in-the-packages)), so there is no assembly scanning or reflection at startup, and the list is trim- and AOT-safe.

| Package | With `UseSpine()` | Without `UseSpine()` |
|---|---|---|
| `Plugin.Maui.Spine.Svg` | Set up by `UseSpine`; call `UseSvgIcon(o => …)` only to change `SvgIconOptions` | `UseEmbeddedSvgImages(...)`, `UseSvgIcon()` |
| `Plugin.Maui.Spine.Svg.Icons` | Nothing | Nothing (found by `.Svg`) |
| `Plugin.Maui.Spine.Widgets` | Registered; call `UseSpineWidgets(o => …)` only to configure | Needs the core, so always with `UseSpine` |
| `Plugin.Maui.Spine.PushNotifications` | Registered; call `UseSpinePushNotifications(o => …)` to set the backend, channels, handler | `UseSpinePushNotifications(o => …)` |
| `Plugin.Maui.Spine.BackgroundTasks` | Registered; call `UseSpineBackgroundTasks(o => …)` only to change intervals or the catch-up run | Needs the core, so always with `UseSpine` |
| `Plugin.Maui.Spine.Authentication` | Registered; configured with `o.AddAuthentication(a => …)` inside `UseSpine` | Needs the core, so always with `UseSpine` |
| `Plugin.Maui.Spine.Authentication.Google` | Nothing to register: `a.AddGoogle(g => …)` inside `o.AddAuthentication(...)` | Needs `.Authentication` |
| `Plugin.Maui.Spine.Authentication.Microsoft` | Registered (passes MSAL the broker's answers); `a.AddMicrosoft(m => …)` or `a.UseMicrosoft(m => …)` inside `o.AddAuthentication(...)` | Needs `.Authentication` |
| `Plugin.Maui.Spine.Authentication.Passkeys` | `a.AddPasskeys()` inside `o.AddAuthentication(...)`, and `<SpinePasskeyDomain>` in the project | Needs `.Authentication` |
| `Plugin.Maui.Spine.Biometrics` | Registered; call `UseSpineBiometrics(o => …)` only to change how long an unlock counts, what happens without a screen lock, or to turn on the app lock | Needs the core, so always with `UseSpine` |
| `Plugin.Maui.Spine.Controls.AnimatedLabel` | Registered | `UseAnimatedLabel()` |
| `Plugin.Maui.Spine.Controls.HeroCollectionView` | Nothing | Nothing (`UseHeroCollectionView()` still compiles, and does nothing) |
| `Plugin.Maui.Spine.Controls.Calendar` | Nothing: strings register from the control's static constructor | Nothing |
| `Plugin.Maui.Spine.Controls.DataGrid` | Registered | `UseDataGrid()` (on Android, keeps row swipes out of scrolls; the grid works without it) |
| `Plugin.Maui.Spine.Controls.Rows` | Nothing (the marquee detail uses AnimatedLabel, which `UseSpine` registers) | Nothing; `UseAnimatedLabel()` for `DetailMarquee` |
| `Plugin.Maui.Spine.Barcodes` | Nothing | Nothing |
| `Plugin.Maui.Spine.Scanner` | Registered: the camera handlers, the scan sheet and its strings | `UseSpineScanner()`; the view works, the scan sheet needs Spine |
| `Plugin.Maui.Spine.Images` | Registered; call `UseSpineImages(o => …)` only to change `SpineImagesOptions` | `UseSpineImages()` |
| `Plugin.Maui.Spine.Controls.Voice` | Registered (SkiaSharp included); call `UseSpineVoiceView(o => …)` only to add visuals | `UseSpineVoiceView()` |

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

### Hooks in the core for packages

A package gets nothing internal from the core. It uses these small public hooks instead:

| Hook | What it gives a package |
|---|---|
| `SpineOverlay.Show(view, back, passTouches)` | A view over the whole window, sheets included (see [Guards](guards.md#the-whole-screen)); with `passTouches`, decoration that takes no touches, such as a glow |
| `SpineScreen` | The pages on screen and a change event, when the app can first show something, keeping the window out of captures (see [Guards](guards.md#the-whole-screen)) |
| `ReducedMotion.IsOn`, `ReducedMotion.Changed` | Reduce Motion, Remove animations or Animation effects off, and a weak event when it changes on iOS, Mac Catalyst and Android (Android's comes half a second late, once the app's own animator scale has followed) |
| `SpineTargets.TryGetFrame(target, out frame)` | The frame of a view, a `PageAction`'s button or a tab's item in window coordinates, the ones an overlay lays out in |
| `Tap.Executed` | Raised after a tap ran a view's `Tap.Command`: a way to react to taps without adding a second tap handler, which would take the tap from Tap on Android |
| `IEmptyResultException` (in `.Common`) | A failure that means "nothing to show": `TaskState` gives `Empty` instead of `Error`. Spine.AI's `AIFeatureOffException` is one, so a feature that is off shows as empty |
| `ElementVisuals.Add(element, visual, order)` (Windows) | Composition visuals above an element's content, shared: an element takes one child visual, so Tap and other packages add theirs to one container in a fixed order |

## Target frameworks

| Package | Frameworks |
|---|---|
| MAUI packages (core, Svg, Widgets, PushNotifications, BackgroundTasks, Biometrics, Authentication.Google, Authentication.Microsoft, Authentication.Passkeys, the controls, Scanner) | `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0` |
| `Plugin.Maui.Spine.Barcodes` | `net10.0` (encoding, `LightGridReader`) and the four MAUI frameworks (adding `BarcodeView`) |
| `Plugin.Maui.Spine.Images` | `net10.0` (the disk cache only, for tests) and the four MAUI frameworks |
| `Plugin.Maui.Spine.Avatar.Tool` | `net10.0`, as a .NET tool (`dotnet tool install -g Plugin.Maui.Spine.Avatar.Tool`, command `spine-avatar`) |
| `Plugin.Maui.Spine.Controls.Voice` | `net10.0` (the visuals' drawing, for tests) and the four MAUI frameworks (adding `VoiceView` and `UseSpineVoiceView`) |
| `Plugin.Maui.Spine.Voice` | `net10.0` (the meter, the queue, the resampler and the shared engine, for tests) and the four MAUI frameworks (adding the platform engines and `UseSpineVoice`) |
| `Plugin.Maui.Spine.AI` | `net10.0` (the server client, the fallback chain and the meter, for tests and non-MAUI clients) and the four MAUI frameworks (adding `UseSpineAI`) |
| `Plugin.Maui.Spine.Authentication` | `net10.0` (the server client, the session and the `HttpClient` handler, for tests) and the four MAUI frameworks (adding `o.AddAuthentication`, the sheet, `[RequiresSignIn]` and Apple) |
| `Plugin.Maui.Spine.Svg.Icons`, `Plugin.Maui.Spine.Common`, `Plugin.Maui.Spine.Server`, `Plugin.Maui.Spine.Server.AI`, `Plugin.Maui.Spine.Server.AI.Assets`, `Plugin.Maui.Spine.Server.Authentication`, `Plugin.Maui.Spine.Server.Messaging` and its two transports | `net10.0` |

Platform minimums: Android API 21 (API 23 with `Plugin.Maui.Spine.PushNotifications`, which Firebase requires, and with `Plugin.Maui.Spine.Scanner`, which CameraX requires), iOS 15, Mac Catalyst 15, Windows 10 17763.

## Feature availability

Some features depend on the platform or the device. Spine answers the question the same way everywhere, so an app can hide or adapt the UI for a feature that would do nothing:

- **`IsSupported`**: the platform and the device can do this at all. Fixed for the app's lifetime, synchronous, cheap, and it never asks the user for a permission. A static property on the static or attached-property class, an instance property on a service.
- **`IsEnabled`**, or an existing specific name such as `AreActivitiesEnabled`: a state the user or the system can change while the app runs (Live Activities switched off in Settings, Reduce Motion). Read it when you need it. It is never folded into `IsSupported`; a permission has its own status, such as `IPushNotificationService.Status`.

| Feature | Supported | Enabled now |
|---|---|---|
| [Motion](motion.md#availability) | `Motion.IsSupported` | `Motion.IsEnabled` (not under Reduce Motion) |
| [Haptics](haptics.md#availability) | `Haptics.IsSupported` | |
| [Materials](materials.md#availability) | `Material.IsSupported(MaterialKind)` | |
| [Scanner](barcodes.md#availability) | `BarcodeScannerView.IsSupported` | `IsTorchSupported` per camera, once it runs |
| [Search index](searchable-items.md#platform-behavior) | `ISearchIndex.IsSupported` | |
| [Widgets](widgets.md#platforms) | `IWidgetService.IsSupported` | |
| [Live Activities](widgets.md#platforms) | `ILiveActivityService.IsSupported` | `ILiveActivityService.AreActivitiesEnabled` |
| [Controls](widgets.md#platforms) | `IControlService.IsSupported` | |
| [Push](push-notifications.md#permission-tags-registration) | `IPushNotificationService.IsSupported` | `IPushNotificationService.Status` |
| [Local notifications](push-notifications.md#local-notifications) | `ILocalNotificationService.IsSupported` | |
| [Background tasks](background-tasks.md) | Every platform; `IBackgroundTasks.RunsWhileClosed` says whether the system runs them while the app is closed | |

## Build assets in the packages

These packages carry MSBuild files that run in the consuming app's build, imported automatically through `buildTransitive/`:

| Package | What its build files do |
|---|---|
| `Plugin.Maui.Spine` | Writes `SpineModules.g.cs` into the app: a `[ModuleInitializer]` that hands each referenced package's registration to `UseSpine()` (from the `SpineModule` items below). Only in an app project; `SpineGenerateModuleRegistrations=false` turns it off |
| `Plugin.Maui.Spine.Widgets`, `.PushNotifications`, `.BackgroundTasks`, `.Biometrics`, `.Controls.AnimatedLabel`, `.Controls.DataGrid`, `.Controls.Highlight`, `.Controls.Voice`, `.Scanner`, `.Images`, `.AI`, `.Voice`, `.Authentication`, `.Authentication.Microsoft` | Declare their `SpineModule` in `<PackageId>.props` |
| `Plugin.Maui.Spine.Common` | Writes the app's iOS entitlements file once from the `SpineEntitlement` items the other packages contribute, and one partial `Info.plist` with `UIBackgroundModes` and `BGTaskSchedulerPermittedIdentifiers` from the `SpineBackgroundMode` and `SpineBackgroundTaskIdentifier` items, merged with the app's own |
| `Plugin.Maui.Spine.Widgets` | Compiles the WidgetKit extension and the bridge framework with `swiftc` on iOS; generates the manifest overlay and provider metadata on Android |
| `Plugin.Maui.Spine.PushNotifications` | Contributes the `aps-environment` entitlement; compiles the Notification Service Extension on iOS when `SpinePushNotificationsImages` is on |
| `Plugin.Maui.Spine.Authentication` | Adds the `com.apple.developer.applesignin` entitlement when the app sets `SpineSignInWithApple=true` |
| `Plugin.Maui.Spine.Biometrics` | Stops an iOS app build whose Info.plist lacks `NSFaceIDUsageDescription` (`SpineBiometricsCheckFaceIdUsage=false` skips the check) |
| `Plugin.Maui.Spine.BackgroundTasks` | Contributes `fetch` (and `processing` with `SpineBackgroundTasksProcessing`) and the `<ApplicationId>.spine.refresh` / `.spine.processing` task identifiers on iOS |

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
