---
name: spine-widgets
description: Build home-screen widgets, Lock Screen widgets and Live Activities with Plugin.Maui.Spine.Widgets — the [Widget] provider, the W tree vocabulary and its limits, timelines with future entries and per-entry surfaces, stored pictures (rotating asset slots, pictures drawn with Skia), buttons that run without opening the app, Live Activity layouts for the lock screen and Dynamic Island, and the iOS App Group / MSBuild setup. Use when adding or changing a widget or Live Activity. Invoke as /spine-widgets.
---

You are building a widget or a Live Activity with **Plugin.Maui.Spine.Widgets**. The app builds a small view tree in C#; Spine serializes it and a native renderer draws it — SwiftUI in a WidgetKit extension on iOS (compiled during the app's build, no Xcode project) and `RemoteViews` on Android. **No C# runs in the widget**: the renderer draws what the app last wrote. Full docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/widgets.md.

Check `/spine-setup` §7 first: the package (registered by `UseSpine`; `UseSpineWidgets(o => …)` only for options), the `<SpineWidget>` items, and on iOS the App Group entitlement.

Platforms: iOS 17+ (home screen, Lock Screen, Live Activities) and Android 5+ (home screen; Live Activities on Android 16+ as Live Updates). Mac Catalyst and Windows get no-op services — check `IWidgetService.IsSupported`.

---

## 1. Declare the widget to the build

The native side exists before any C# runs, so every widget is an MSBuild item. `Include` is the **kind** and must match the attribute exactly.

```xml
<ItemGroup>
  <SpineWidget Include="next-event" DisplayName="Next event" Description="The countdown to your next event." Families="Small,Medium" />
</ItemGroup>
```

| Family | iOS | Android |
|---|---|---|
| `Small` / `Medium` / `Large` | Home screen | 2×2 / 4×2 / 4×4 cells |
| `ExtraLarge` | iPad only | 5×4 cells |
| `AccessoryCircular` / `AccessoryRectangular` / `AccessoryInline` | Lock Screen | Ignored (an accessory-only kind still shows as 2×2 in the picker) |

At most nine widgets (WidgetKit holds ten; Spine reserves one for Live Activities; Android carries nine receivers).

Android's widget picker shows the widget itself from Android 15, once the app has built its timeline. Below 15 it shows the app icon unless the item has `PreviewImage="Widgets/Previews/<kind>.png"`, a cropped screenshot of the widget.

## 2. Write the provider

Constructed through DI every time it runs, so inject services as in a ViewModel. It runs **only in the app's process**: at launch, when the app goes to the background, in background runs, after a button tap, on `RefreshAsync`, and on Android at the `Refresh(after)` alarm.

```csharp
[Widget("next-event")]
public sealed class NextEventWidget(IEventService _events, IWidgetService _widgets) : IWidgetProvider
{
    public async Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var next = await _events.NextAsync(cancellationToken);

        return WidgetTimeline
            .Single(new Dictionary<WidgetFamily, WidgetNode>
            {
                [WidgetFamily.Small] = W.VStack(6,
                    W.Text("Next event").Caption().Secondary(),
                    W.Timer(next.StartsAt).Title().Bold().Color(WidgetColor.Green)),

                [WidgetFamily.Medium] = W.VStack(6,
                    W.HStack(W.Icon("calendar", WidgetColor.Green), W.Text(next.Name).Headline().Bold(), W.Spacer()),
                    W.Text(next.Place).Caption().Secondary(),
                    W.Timer(next.StartsAt).Title().Bold()),
            })
            .Refresh(TimeSpan.FromMinutes(30))
            .OpenUrl(_widgets.LinkFor(context.Kind));
    }
}
```

A per-family dictionary must have a tree for **every declared family** — iOS shows "—" for a family without one. Or build one tree and vary parts of it with `W.Adaptive(fallback, perFamily)`.

### The tree

| Node | Renders as |
|---|---|
| `W.VStack(spacing, …)` / `W.HStack` / `W.ZStack` | Stacks: VStack leading-aligned, HStack vertically centered, ZStack centered; default spacing 4. Only stacks take `.Padding()`, `.Background()`, `.CornerRadius()` |
| `W.Text(s)` | Text; style with `.Title() .Headline() .Body() .Caption() .Bold() .Secondary() .Color(c)` — the platform's type ramp, no point sizes, no custom fonts |
| `W.Timer(until)` / `W.Relative(date, compact)` | A countdown / "3 min ago", redrawn by the **system** — never compute these as text |
| `W.Icon(name, color)` | A monochrome SVG by name (dots as underscores: `figure_run.svg`), fixed size (18 pt iOS, 20 dp Android); an SF Symbol of that name is the iOS fallback |
| `W.Image(assetId, height)` | A picture stored with `IWidgetService.StoreAssetAsync`, aspect kept; `.FullColor()` keeps its colors in iOS's Tinted/Clear appearances |
| `W.Progress(0..1, color)` / `W.Spacer()` / `W.Divider()` | |
| `W.Button(actionId, child)` | A tap that reaches `IWidgetActionHandler` **without opening the app**; mark what changes `.Pending()` |
| `W.Adaptive(fallback, perFamily)` | One subtree that differs per family |

No alignment options, no fixed widths, no shapes, no custom fonts. When the design needs more, draw it as a picture (§3).

Colors: prefer the semantic `WidgetColor.Primary / Secondary / Accent / Surface / OnAccent / Green / Red / …` (they follow light and dark); `WidgetColor.FromHex("#1B5E3F")` for a brand surface, and then give the text on it fixed colors too.

### The surface

`timeline.Background(color | gradient).BackgroundImage("cover.png")` paints every entry. `timeline.Add(date, tree, new WidgetSurface(color | gradient, "holiday.png"))` gives one entry a surface of its own, which **replaces the timeline's whole surface** and switches with the entry. A `WidgetSurface("pic.png")` alone draws over the platform's background, not the timeline's color.

### The timeline

Content known in advance goes in the timeline as dated entries; the platform switches them **without the app** and it costs no budget:

```csharp
var timeline = new WidgetTimeline().Background(paper);
for (var i = 0; i < 60; i++)
{
    var day = today.AddDays(i);
    timeline.Add(i == 0 ? DateTimeOffset.Now : new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue)), Page(day), SurfaceFor(day));  // null = timeline's surface
}
```

- Start with an entry for now; the platform shows the latest entry whose date has passed.
- `.Refresh(after)` counts from the **last** entry. iOS budgets reloads at ~40–70 a day, so keep it at 15 minutes or more on a short timeline, and leave it out of a long one — launches, backgrounding and background runs rebuild it anyway.
- `IWidgetService.RefreshAsync(kind)` / `RefreshAsync<TProvider>()` / `RefreshAllAsync()` from the app when data changed.
- An empty timeline is logged and changes nothing.

## 3. Pictures

```csharp
await _widgets.StoreAssetAsync("leaf-12.png", pngStream, cancellationToken);   // before the timeline that names it
W.Image("leaf-12.png")
```

- The id is a file name; the same id overwrites, and **assets are never deleted** (no API). Never use an id per day or per item: use a fixed number of **rotating slots**, more than the timeline has entries — `$"leaf-{day.DayNumber % 64}.png"` for a 60-day timeline — so a rebuild only overwrites slots of entries already past.
- Overwriting an id changes every entry that uses it; entries that differ need ids that differ.
- Size pictures at the widget's points × 3 (a small widget ≈ 510 px square, a circular accessory ≈ 228 px). The iOS extension is killed at ~30 MB, and a decoded picture costs w × h × 4 bytes.
- **Draw with SkiaSharp** (already a dependency through Spine's SVG pipeline) when the vocabulary is not enough: `SKSurface.Create`, scale the canvas to points, draw, `Snapshot().Encode(SKEncodedImageFormat.Png, 100).AsStream()`. Embed the typeface (`SKTypeface.FromStream`). Keep the painter free of MAUI so it can be checked from a test or console app. Keep anything that ticks as `W.Timer` / `W.Relative` beside the picture.

## 4. Lock Screen widgets (iOS)

iOS draws accessory families **in one tint** over the wallpaper: colors are discarded and the timeline's surface is not drawn.

- Design pictures as **white on transparent**; cut text out of a white shape with `SKBlendMode.Clear` so the wallpaper shows through.
- `.FullColor()` does not help on the Lock Screen (it is for the home screen's Tinted/Clear appearances).
- `AccessoryInline` is one line after iOS's own date — give it a `W.Text` that starts with `"· "`.
- `AccessoryRectangular`: two or three lines of text; timers tick there too.

## 5. Buttons and links

```csharp
W.Button("done", W.HStack(W.Text("Done").Caption().Bold().Color(WidgetColor.OnAccent)).Background(WidgetColor.Accent).Padding(6).CornerRadius(8))

[Widget("next-event")]
public sealed class NextEventWidget(…) : IWidgetProvider, IWidgetActionHandler, IWidgetLinkHandler
{
    public async Task OnActionAsync(WidgetAction action)            // action.Kind, action.ActionId, action.At
    { … }                                                             // main thread; the widget is rebuilt when this returns

    public Task OnWidgetOpenedAsync(WidgetLink link) =>               // link.Kind, link.Url (query values from OpenUrl)
        _navigation.NavigateToAsync<EventPage>();
}
```

- The handler runs in the app's process, which iOS and Android launch in the background when it is not running. Use `action.At` (the tap's time), not `DateTimeOffset.Now`. Keep it short: iOS gives the tap 30 seconds, launch included.
- `.OpenUrl(_widgets.LinkFor(kind))` makes a tap on the widget open the app; add query values to carry what to open.

## 6. Live Activities

Same vocabulary, eight named regions. **Start it from a button the user pressed** — iOS only starts an activity while the app is in the foreground, and Android asks for the notification permission there.

```csharp
var layout = new LiveActivityLayout
{
    LockScreen       = W.HStack(8, W.Icon("box"), W.VStack(2, W.Text(order.Name).Headline().Bold(), W.Text("Out for delivery").Caption().Secondary()), W.Spacer(), W.Timer(order.Eta).Title()),
    ExpandedLeading  = W.Icon("box"),
    ExpandedTrailing = W.Timer(order.Eta).Headline(),
    ExpandedCenter   = W.Text(order.Name).Headline().Bold(),
    ExpandedBottom   = W.VStack(W.Text($"{order.StopsLeft} stops away").Caption().Secondary(), W.Progress(order.Progress, WidgetColor.Green)),
    CompactLeading   = W.Icon("box"),
    CompactTrailing  = W.Text($"{order.Eta:HH:mm}").Caption(),   // a clock here stretches the Dynamic Island
    Minimal          = W.Icon("box"),
    Background       = WidgetColor.FromHex("#1B5E3F"),           // or SystemBackground = true for the lock screen's own material
};

var activity = await _liveActivities.StartAsync($"delivery:{order.Id}", layout, staleAt: order.Eta.AddMinutes(30));
await activity!.UpdateAsync(layout with { ExpandedCenter = W.Text("Delivered").Headline() });
await activity.EndAsync();
```

- Lifecycle: start (foreground, or push-to-start) → update (replaces the whole layout) → stale past `staleAt` (dimmed on iOS) → end (`EndAsync`/`EndAllAsync`, the user's swipe, a push, or iOS after 8 hours).
- Put clocks on `LockScreen` and the expanded regions; compact and minimal get a plain text or an icon.
- `StartAsync` returns `null` when refused (activities off in Settings, app not in foreground, Android below 16); `AreActivitiesEnabled` tells you whether to show the button at all.
- `Active` lists running activities, including ones started before the app was last killed — ask it instead of keeping a handle in a ViewModel. `ActivitiesChanged` / `ActivityEnded` report ends the app did not make (swipe, push, stale date); subscribe right after `builder.Build()` to hear about ends while the app was closed.
- A `W.Button` in an activity reaches the `[Widget]` provider whose kind equals the **activity's kind**; with no such provider the tap is dropped. Android Live Updates have no buttons.
- An activity lives at most 8 hours on iOS. For around-the-clock subjects, let the server restart it with the push-to-start token (see the wiki, "Keeping one on screen around the clock").
- Push updates: `UseSpineWidgets(o => o.LiveActivityPushTokens = true)`, send `activity.GetPushTokenAsync()` and `GetPushToStartTokenAsync()` to the backend at every launch and foreground; `Plugin.Maui.Spine.Server`'s `IPushSender` has `StartLiveActivityAsync`, `UpdateLiveActivityAsync` and `BroadcastLiveActivityAsync`.

## 7. Build and test

- iOS: macOS with Xcode; the extension compiles with `swiftc` in the iOS build (simulator builds sign ad hoc by themselves). Device builds need a second App ID (`<ApplicationId>.SpineWidgets`) and profile, App Group on both.
- Android: nothing extra; Live Activities need Android 16.
- Launch the app once after install so it writes the first timeline, then add the widget: long-press the home screen → Edit → Add Widget. Lock Screen: wake with Home, long-press the wallpaper → Customize. `rm -rf obj/spinewidgets` when the extension seems stale.
- Test timeline turns with entries two minutes apart and the app terminated.
- In the simulator, dismiss a Live Activity on the Lock Screen with a **slow** swipe left (a quick one unlocks). Logs: `xcrun simctl spawn booted log show --last 3m --predicate 'eventMessage CONTAINS "SpineWidgetBridge"'`; the extension logs `[SpineWidgets]`, Android logcat tag `SpineWidgets`.
- `xcrun simctl push` never runs a notification service extension; use a real APNs push for that. Background runs cannot be triggered in the simulator.

## Do / Don't

- Do put the subject in the `kind` string (`$"delivery:{id}"`); it is how activities are told apart.
- Do use `W.Timer` and `W.Relative` for anything time-based.
- Do store pictures before writing the timeline, in a fixed set of rotating slots.
- Don't start a Live Activity from a page's constructor or `OnAppearingAsync`; iOS refuses outside a user action.
- Don't rely on colors staying fixed in dark mode unless you fixed both the surface and the text.
- Don't design Lock Screen widgets in color.

## Documentation

- Widgets and Live Activities: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/widgets.md (see "Best practice", "Limits" and "Testing")
- Server-side push for activities and widgets: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications-server.md
- Samples: `samples/MauiSpineSampleApp/Widgets/SampleWidget.cs`, `samples/MauiSpinePushNotificationsSampleApp/Widgets/` in https://github.com/jonatansoderberg/Maui.Spine
