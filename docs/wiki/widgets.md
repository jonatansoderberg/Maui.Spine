# Widgets and Live Activities

Spine widgets let an app build a **home-screen widget** and a **Live Activity** from C#, with no Xcode project, no app-specific Swift and no Android platform code. The app builds a small view tree, Spine serializes it, and a generic native renderer draws it — with SwiftUI in a widget extension on iOS, with `RemoteViews` in the app's own process on Android.

The reasoning behind the design — why C# cannot run inside a WidgetKit extension, and why a serialized tree is the answer — is in the [Spine.Widgets proposal](../proposals/spine-widgets.md).

---

## Platforms

| Platform | Home-screen widget | Live Activity |
|---|---|---|
| iOS 17+ | ✅ WidgetKit extension built at compile time | ✅ Lock Screen and Dynamic Island (all regions) |
| Mac Catalyst | ⚠️ Same mechanism, not yet verified | ⚠️ macOS 26 mirrors an iPhone activity by itself |
| Android 5+ | ✅ `AppWidgetProvider` receivers drawn from the tree with `RemoteViews`; size- and theme-adaptive from Android 12 | ✅ Android 16+ as a **Live Update** (promoted notification); `StartAsync` returns `null` below |
| Windows | ❌ Planned, MSIX-packaged apps only | — |

On every platform without an implementation the services are still injectable and every call is a no-op; `IWidgetService.IsSupported` says which you are on.

---

## Setup

### 1. Reference and register

```csharp
builder
    .UseSpine(options => options.AddAssembly(typeof(MauiProgram).Assembly))
    .UseSpineWidgets();
```

`UseSpineWidgets` registers `IWidgetService` and `ILiveActivityService`, discovers `[Widget]` providers in the assemblies `UseSpine` was given, and routes the widget's open URL back into the app. Call it after `UseSpine`.

### 2. Declare each widget to the build

The native side is generated from MSBuild items, not from the attribute — the attribute is C#, and the widget has to exist in the app's bundle or manifest before any C# runs:

```xml
<ItemGroup>
  <SpineWidget Include="next-start"
               DisplayName="Nästa start"
               Description="The countdown to your next start."
               Families="Small,Medium" />
</ItemGroup>
```

| Metadata | Meaning |
|---|---|
| `Include` | The kind. Must match `[Widget("…")]` exactly; Spine logs a warning at startup for a kind with no provider. |
| `DisplayName` | The name in the widget gallery. Defaults to the kind. |
| `Description` | The gallery's subtitle. |
| `Families` | `Small`, `Medium`, `Large`, `ExtraLarge`, `AccessoryCircular`, `AccessoryRectangular`, `AccessoryInline`. Defaults to `Small`. |

A WidgetKit bundle holds at most ten widgets and Spine reserves one for Live Activities, so nine `<SpineWidget>` items is the limit. Android has the same cap: the package carries nine fixed receivers and the build wires the items to them in declaration order.

On iOS the items become the widget extension; on Android they become manifest entries and the picker's metadata (see [Android](#android)). The properties below are iOS-only except `SpineWidgetsLiveActivities` and `SpineWidgetsEnabled`.

| Property | Default | Meaning |
|---|---|---|
| `SpineWidgetsAppGroup` | `group.$(ApplicationId)` | The App Group the app and the extension share. |
| `SpineWidgetsLiveActivities` | `true` | Whether the bundle includes the Live Activity and the app declares `NSSupportsLiveActivities`. |
| `SpineWidgetsMinimumOSVersion` | `17.0` | Deployment target of the extension. |
| `SpineWidgetsExtensionName` | `SpineWidgets` | Bundle name of the appex. |
| `SpineWidgetsEnabled` | `true` | Set to `false` to build the app without the extension. |

### 3. Give the app the App Group entitlement

The app and the extension talk through an App Group container, so the **app** must carry the entitlement. The extension's own is generated.

```xml
<!-- Platforms/iOS/Entitlements.plist -->
<key>com.apple.security.application-groups</key>
<array><string>group.com.companyname.myapp</string></array>
```

```xml
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">
  <CodesignEntitlements>Platforms\iOS\Entitlements.plist</CodesignEntitlements>
</PropertyGroup>
```

The build fails with a named error if the group is missing from that file. With no `CodesignEntitlements` at all the build falls back to a generated one that carries only the group.

A sample that uses `<ProjectReference>` rather than the NuGet package also has to import the targets explicitly:

```xml
<Import Project="..\..\src\Plugin.Maui.Spine.Widgets\build\Plugin.Maui.Spine.Widgets.targets" />
```

### Build requirements

- **macOS with Xcode for iOS.** The extension and the bridge are compiled with `swiftc` during the iOS build (a few seconds); everything else is untouched. The Android build needs nothing beyond the SDK and runs on any host.
- **A real App Group in the provisioning profile** for device and TestFlight builds. Simulator builds sign ad hoc and need no identity — the targets set `CodesignKey=-` themselves when none is configured.
- Only iOS **inner** builds (those with a `RuntimeIdentifier`) run the native step. Design-time builds, other platforms and Windows hosts skip it entirely.

---

## Building a widget

A provider is a class with `[Widget]`. It is constructed through DI on every refresh, so inject services the way a view model does.

```csharp
[Widget("next-start")]
public sealed class NextStartWidget(IRaceService _races, IWidgetService _widgets) : IWidgetProvider
{
    public async Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var start = await _races.NextStartAsync(cancellationToken);

        return WidgetTimeline
            .Single(new Dictionary<WidgetFamily, WidgetNode>
            {
                [WidgetFamily.Small] = W.VStack(6,
                    W.Text("Nästa start").Caption().Secondary(),
                    W.Timer(start.Time).Title().Bold().Color(WidgetColor.Green)),

                [WidgetFamily.Medium] = W.VStack(6,
                    W.HStack(W.Icon("figure.run", WidgetColor.Green), W.Text(start.Event).Headline().Bold(), W.Spacer()),
                    W.Text(start.Class).Caption().Secondary(),
                    W.Timer(start.Time).Title().Bold()),
            })
            .Refresh(TimeSpan.FromMinutes(30))
            .OpenUrl(_widgets.LinkFor(context.Kind));
    }
}
```

### The tree

`W` builds every node; each call returns a node, so a tree reads the way it renders.

| Node | Renders as |
|---|---|
| `W.VStack` / `W.HStack` / `W.ZStack` | Stacks, with optional spacing in points |
| `W.Text(string)` | A run of text |
| `W.Timer(until)` | A countdown the **system** redraws every second |
| `W.Relative(date)` | Relative text ("3 min ago"), also system-drawn |
| `W.Icon(sfSymbolName, color)` | A platform symbol — an SF Symbol on iOS, an embedded SVG named after it on Android (see [Icons](#icons-on-android)) |
| `W.Image(assetId, height)` | A bitmap the app stored with `StoreAssetAsync` |
| `W.Progress(value, color)` | A linear bar, 0 to 1 |
| `W.Spacer()` / `W.Divider()` | Flexible space, separator |

Text-like nodes take fluent styling: `.Title()`, `.Headline()`, `.Body()`, `.Caption()`, `.Bold()`, `.Secondary()`, `.Color(…)`. Each call returns a new node, so a styled node can be reused.

`WidgetColor` is either one of the platform's semantic colors (`Primary`, `Secondary`, `Accent`, `Green`, `Red`, `Orange`, `Yellow`, `Blue`), which adapt to light and dark, or a fixed value from `WidgetColor.FromHex("#2E8B57")` / `WidgetColor.From(mauiColor)`. Prefer semantic colors for anything but a brand accent — a fixed color is a fixed color in dark mode too.

### The timeline

A widget is a series of snapshots, not a live view. `WidgetTimeline` can hold several dated entries, and the platform switches between them **without waking the app**:

```csharp
var timeline = new WidgetTimeline();
timeline.Add(now, Countdown(start));      // shown until the start
timeline.Add(start, Running(start));      // the platform switches here by itself
timeline.Refresh(TimeSpan.FromMinutes(30));
```

Pre-computing entries is nearly free; reloads are not (see below). `Refresh(after)` asks the platform to call the provider again that long after the last entry — a request, not a promise.

### Refreshing from the app

```csharp
await _widgets.RefreshAsync<NextStartWidget>();  // one widget
await _widgets.RefreshAsync("next-start");       // by kind
await _widgets.RefreshAllAsync();                // all of them
```

Every widget is also rebuilt automatically when the app moves to the background, so the home screen shows the state the user just left. Turn that off with `UseSpineWidgets(o => o.RefreshOnBackground = false)`. On Android `Refresh(after)` is honoured by the app itself: an alarm wakes the widget receiver, which runs the provider in the background without any UI.

### Opening the app from the widget

`OpenUrl` sets the URL the widget is tapped with. `IWidgetService.LinkFor(kind)` gives the canonical one — `<ApplicationId>://widget/<kind>` — and the scheme is registered by the build (in `Info.plist` on iOS, as an intent filter on Android), so nothing has to be chosen or kept in sync. Add query values to carry what the tap should open:

```csharp
.OpenUrl(new Uri($"{_widgets.LinkFor(context.Kind)}?competition={competition.Id}"))
```

A provider that also implements `IWidgetLinkHandler` is called on the main thread when the app is opened that way, cold start included:

```csharp
public Task OnWidgetOpenedAsync(WidgetLink link) =>
    HttpUtility.ParseQueryString(link.Url.Query)["competition"] is { Length: > 0 } id
        ? _navigation.NavigateToAsync<ParticipantsPage, ParticipantsTarget>(new(CompetitionId.From(id)))
        : _navigation.SwitchToTabAsync<HomePage>();
```

### Images

SF Symbols are free on iOS (Android draws them from an SVG, see below); a bitmap has to be placed in the shared container first:

```csharp
await _widgets.StoreAssetAsync("arena", pngStream);
// …later, in a tree:
W.Image("arena", height: 64)
```

Keep them small. The extension is killed at roughly 30 MB.

---

## Live Activities

A Live Activity is the same tree vocabulary in eight named regions. Unlike a widget it is updated **directly by the app**, with no budget while the app is running.

```csharp
var activity = await _liveActivities.StartAsync("din-start", new LiveActivityLayout
{
    LockScreen       = W.HStack(8, W.Icon("figure.run"), W.Text(race.Name).Headline().Bold(), W.Spacer(), W.Timer(start).Title()),
    ExpandedLeading  = W.Icon("figure.run"),
    ExpandedTrailing = W.Timer(start).Headline(),
    ExpandedCenter   = W.Text(race.Name).Headline().Bold(),
    ExpandedBottom   = W.Text($"Din start {start:HH:mm} · {race.Place}").Caption().Secondary(),
    CompactLeading   = W.Icon("figure.run"),
    CompactTrailing  = W.Timer(start).Caption(),
    Minimal          = W.Icon("figure.run"),
}, staleAt: race.LastFinish);

await activity!.UpdateAsync(layout with { ExpandedCenter = W.Text("I mål").Headline() });
await activity.EndAsync();
```

- `StartAsync` returns `null` when the platform refused — activities turned off in Settings, or the app not in the foreground. **iOS only starts an activity while the app is in the foreground**, so it belongs behind a button the user pressed, never in a page's build. On Android the same call asks for the notification permission the first time, which is another reason to keep it behind a button.
- `AreActivitiesEnabled` says whether the user allows them at all; hide the button when they do not. On Android it means "Android 16 or later" — the notification permission cannot be told apart from "not asked yet", so `StartAsync` asks.
- `Active` lists the activities this app has running, **including any it started before it was last killed** — an activity outlives the process. Ask it rather than holding a handle in a view model. The `kind` is how you tell them apart — put whatever identifies the subject in it (`$"din-start:{competitionId}"`).
- `staleAt` is when the content should be presented as out of date if no update arrived; the renderer dims it.
- An activity lives at most **8 hours**, then iOS ends it. Android has no such limit, but keeps one activity per kind: starting a second one with the same kind replaces the first.

---

## Update budgets

Three separate mechanisms decide what the user actually sees, and only one of them is budgeted. Designing against the wrong one is the single most common way a widget ends up looking broken.

| Mechanism | Frequency | Costs budget? |
|---|---|---|
| **System-drawn time text** — `W.Timer`, `W.Relative` | Every second, drawn by the system without running the extension | No |
| **Timeline entries** — several `(date, tree)` pairs the app pre-computed | Exactly when the entry says, in practice no closer than ~5 min | No; the switch is free |
| **Reloads** — `RefreshAsync`, `Refresh(after)`, background work, push | The system grants roughly **40–70 per day** for a frequently seen widget, so every 15–60 min | Yes, except when the app comes to the foreground, the user interacts with the widget, or the widget was just added |

The consequence for Spine: **anything that must tick has to be a `W.Timer` or `W.Relative` node, never text the app computed.** A widget showing `$"Starts in {span:mm\\:ss}"` stands still until the next reload; the same value as a `W.Timer` ticks every second for free.

Live Activities have a different model: the app updates the content directly, as often as it likes while it runs, and timer text is system-drawn there too. That is why a countdown in the Dynamic Island never needs a reload.

One caveat when the app fakes its clock (a demo mode, a time machine): timer nodes are drawn against the **device** clock, so a simulated date renders as a countdown that has already expired. That is the platform, not Spine.

---

## Real-time data

Consider a widget showing a value a cloud API refreshes every five minutes. **The app alone cannot reach that interval on iOS**, for the widget or the activity. What works in this class of app is a **server** that polls and pushes; the app itself gets a handful of background runs per hour.

| Surface | App only | With server push |
|---|---|---|
| iOS widget | The extension could fetch in its timeline provider, but the system grants ~40–70 reloads a day — every 15–60 min | iOS 26 can trigger a reload by push (`WidgetPushHandler`); as far as is known it still counts against the same budget |
| iOS Live Activity | Only updates while the app runs. `BGAppRefreshTask` gives a few runs an hour, irregularly, ~30 s at a time | An APNs `liveactivity` push every 5 min works. `NSSupportsLiveActivitiesFrequentUpdates` raises the budget |
| Android widget | `Refresh(after)` runs the provider from an inexact alarm, in practice every 15 min or so under Doze; a foreground service can update freely | An FCM data message every 5 min; the service updates widget and Live Update at once |

Push and remote sources are v2 of the plugin. In v1 the honest options are a pre-computed timeline, a background refresh, and system-drawn timers — which between them cover "next start", today's schedule, and a countdown, but not a live sensor reading.

---

## Android

The same C# tree renders on Android without changes; the difference is what the platform gives to draw with, and the section is here so nobody has to discover it in the emulator.

### How it maps

| Spine | Android |
|---|---|
| `<SpineWidget>` item | An `AppWidgetProvider` receiver in the manifest (one of nine the package carries), `appwidget-provider` metadata, and the picker's name and description — all generated into `obj/` by the build |
| `WidgetFamily` | Launcher cells: `Small` 2×2, `Medium` 4×2, `Large` 4×4, `ExtraLarge` 5×4. The smallest declared is the minimum size; from Android 12 the launcher picks the tree for the size the user resized to. Accessory families have no counterpart and are ignored |
| Stacks | `LinearLayout` / `FrameLayout`, nested with `RemoteViews.AddView` |
| `W.Text`, `W.Timer`, `W.Relative` | `TextView` and `Chronometer`; `Title` 22 sp, `Headline` 16 sp, `Body` 14 sp, `Caption` 12 sp |
| `WidgetColor` | Semantic colors resolve in the launcher's theme (light and dark) from Android 12; `Green` … `Blue` are the iOS system palette in both variants; hex is hex |
| `WidgetTimeline` entries | The entry that applies now is drawn; an inexact alarm redraws at the next entry's date, another runs the provider `Refresh(after)` the last one |
| `OpenUrl` | A `PendingIntent` to a small activity in the package that forwards the URL to the app's own main activity; the scheme is registered on it by the build |
| `IWidgetLinkHandler` | Called from `OnCreate` (cold start) or `OnNewIntent` (warm), exactly as on iOS |

The receivers run in the app's own process, so there is no shared container and no separate memory budget; the timeline documents live under the app's files directory.

### Icons on Android

There are no SF Symbols. `W.Icon("figure.run")` is drawn from an **embedded SVG named after the symbol, with dots as underscores** — `figure_run.svg` — found through the same resource cache as `SvgImageSource`, so it just has to be an `EmbeddedResource` in an assembly `UseSpine` was given:

```xml
<EmbeddedResource Include="Resources\Svg\*.svg" />
```

The shape is rendered white and tinted with the node's color, so one monochrome SVG serves light and dark. Do not name the file `figure.run.svg`: the .NET SDK reads `.run` as a culture (Kirundi) and moves the file to a satellite assembly where nothing finds it. An icon with no SVG is left out and a warning is logged under the `SpineWidgets` tag.

### Live Updates

A Live Activity on Android 16 is a **promoted ongoing notification**, and the layout's regions map onto the notification template rather than being drawn as a tree:

| Region | Becomes |
|---|---|
| `LockScreen` (and `ExpandedBottom`) | Title from the first headline or title text, content text from the next text; the first `W.Timer` becomes the header chronometer, the first `W.Progress` the `ProgressStyle` bar in its color |
| `CompactLeading` / `Minimal` / `ExpandedLeading` | The first `W.Icon` is the small icon, its color the accent |
| `CompactTrailing` | A `W.Text` there becomes the status-bar chip's text; a `W.Timer` leaves the chip to the system's chronometer |
| `Link` | The notification's tap, through the same activity as the widget |

`staleAt` is not visualised on Android. Below Android 16 `AreActivitiesEnabled` is `false` and `StartAsync` returns `null`; a plain ongoing notification would not be a Live Activity, so Spine does not pretend.

The build adds `POST_NOTIFICATIONS` and `POST_PROMOTED_NOTIFICATIONS` to the manifest when `SpineWidgetsLiveActivities` is on; the first is requested at `StartAsync`, the second is granted by the user's per-app Live Updates setting.

### Known gaps

- `W.Relative` is a chronometer counting up (`03:12`), not "3 min ago" — `RemoteViews` has no system-drawn relative text, and a text the app computed would stand still.
- `W.Spacer` only stretches inside a stack that is wider than its content; stacks are full-width, so the usual `HStack(text, Spacer(), timer)` works, a spacer in a nested vertical stack does not.
- No custom fonts and no `ZStack` alignment beyond centered: `RemoteViews` cannot set a typeface.
- Nine widget kinds, as on iOS.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| The widget is not in the gallery | The kind has no `<SpineWidget>` item, or the app was never launched after install. |
| The widget renders but a node is missing | A tree the renderer does not understand, an SF Symbol name that does not exist, or (Android) no `figure_run.svg`-style resource for the icon. |
| The widget shows only "—" (Android) | It was placed before the app ever built it; the receiver has asked the provider, and the next launch or background pass fills it. |
| The countdown stands still | Text the app computed instead of a `W.Timer` node. |
| `StartAsync` returns `null` | Live Activities are off in Settings, or the app was not in the foreground. On Android: the notification permission was denied, or the device is older than Android 16. |
| Build error about the App Group | The app's `CodesignEntitlements` does not list the group; see [setup](#3-give-the-app-the-app-group-entitlement). |
| `SIGKILL (Code Signature Invalid)` at launch | A stale app bundle. Delete `bin/…/<App>.app` and `obj/…/<rid>/codesign` and build again. |

---

## Related

- [Spine.Widgets proposal](../proposals/spine-widgets.md) — the architecture, the platform survey, and the spike this grew out of
- Samples: `samples/MauiSpineSampleApp/Widgets/SampleWidget.cs` and `samples/Orientera/Widgets/`
