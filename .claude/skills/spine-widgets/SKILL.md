---
name: spine-widgets
description: Build home-screen widgets and Live Activities with Plugin.Maui.Spine.Widgets — the [Widget] provider, the W tree vocabulary, timelines and refresh, buttons that run without opening the app, Live Activity layouts for the lock screen and Dynamic Island, and the iOS App Group / MSBuild setup. Use when adding or changing a widget or Live Activity. Invoke as /spine-widgets.
---

You are building a widget or a Live Activity with **Plugin.Maui.Spine.Widgets**. The app builds a small view tree in C#; Spine serializes it and a native renderer draws it — SwiftUI in a WidgetKit extension on iOS (compiled during the app's build, no Xcode project) and `RemoteViews` on Android. Full docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/widgets.md.

Check `/spine-setup` §7 first: the package, `UseSpineWidgets()` after `UseSpine`, the `<SpineWidget>` items, and on iOS the App Group entitlement.

---

## 1. Declare the widget to the build

The native side exists before any C# runs, so every widget is an MSBuild item. `Include` is the **kind** and must match the attribute exactly.

```xml
<ItemGroup>
  <SpineWidget Include="next-event" DisplayName="Next event" Description="The countdown to your next event." Families="Small,Medium" />
</ItemGroup>
```

Families: `Small`, `Medium`, `Large`, `ExtraLarge`, `AccessoryCircular`, `AccessoryRectangular`, `AccessoryInline`. At most nine widgets (WidgetKit holds ten; Spine reserves one for Live Activities).

## 2. Write the provider

Constructed through DI on every refresh, so inject services as in a ViewModel. Return a timeline; give each family its own tree when the sizes differ.

```csharp
[Widget("next-event")]
public sealed class NextEventWidget(IEventService _events, IWidgetService _widgets) : IWidgetProvider
{
    public async Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken ct)
    {
        var next = await _events.NextAsync(ct);

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

### The tree

| Node | Renders as |
|---|---|
| `W.VStack(spacing, …)` / `W.HStack` / `W.ZStack` | Stacks; only stacks take `.Padding()`, `.Background()`, `.CornerRadius()` |
| `W.Text(s)` | Text; style with `.Title() .Headline() .Body() .Caption() .Bold() .Secondary() .Color(c)` |
| `W.Timer(until)` / `W.Relative(date)` | A countdown / "3 min ago", redrawn by the **system** — never compute these as text |
| `W.Icon(name, color)` | A monochrome SVG by name (embedded in the app or `Svg.Icons`); an SF Symbol of that name is the iOS fallback |
| `W.Image(assetId, height)` | A bitmap stored with `IWidgetService.StoreAssetAsync` |
| `W.Progress(0..1, color)` / `W.Spacer()` / `W.Divider()` | |
| `W.Button(actionId, child)` | A tap that reaches `IWidgetActionHandler` **without opening the app**; mark what changes `.Pending()` |
| `W.Adaptive(fallback, perFamily)` | One subtree that differs per family |

Colors: prefer the semantic `WidgetColor.Primary / Secondary / Accent / Green / Red / …` (they follow light and dark); `WidgetColor.FromHex("#1B5E3F")` for a brand surface, and then give the text on it fixed colors too. `WidgetTimeline.Single(tree).Background(color | gradient).BackgroundImage("cover.png")` paints the surface; `timeline.Add(date, tree, new WidgetSurface(color | gradient, "holiday.png"))` gives one entry a surface of its own, which replaces the timeline's whole surface and switches with the entry.

### Refresh

- `.Refresh(TimeSpan)` asks the system to rebuild; iOS gives a widget a budget of roughly 40–70 refreshes a day, so keep it at 15 minutes or more and let `W.Timer` do the seconds.
- `IWidgetService.RefreshAsync(kind)` from the app when data changed (after a sync, a push, a user action).
- `WidgetTimeline.Entries(…)` for content the app already knows in advance (a schedule).

### Buttons

```csharp
W.Button("done", W.HStack(W.Text("Done").Caption().Bold()).Background(WidgetColor.Accent).Padding(6).CornerRadius(8))

public sealed class NextEventWidget : IWidgetProvider, IWidgetActionHandler
{
    public async Task OnActionAsync(WidgetAction action, CancellationToken ct)   // action.Kind, action.Id, action.At
    { … }   // the widget is rebuilt when this returns
}
```

### Opening the app

`.OpenUrl(_widgets.LinkFor(kind))` makes the whole widget tap open the app; implement `IWidgetLinkHandler` to turn the link into a `NavigateToAsync<…>()`.

## 3. Live Activities

Same vocabulary, eight named regions. **Start it from a button the user pressed** — iOS only starts an activity while the app is in the foreground, and Android asks for the notification permission there.

```csharp
var activity = await _liveActivities.StartAsync($"delivery:{order.Id}", new LiveActivityLayout
{
    LockScreen       = W.HStack(8, W.Icon("box"), W.VStack(2, W.Text(order.Name).Headline().Bold(), W.Text("Out for delivery").Caption().Secondary()), W.Spacer(), W.Timer(order.Eta).Title()),
    ExpandedLeading  = W.Icon("box"),
    ExpandedTrailing = W.Timer(order.Eta).Headline(),
    ExpandedCenter   = W.Text(order.Name).Headline().Bold(),
    ExpandedBottom   = W.VStack(W.Text($"{order.StopsLeft} stops away").Caption().Secondary(), W.Progress(order.Progress, WidgetColor.Green)),
    CompactLeading   = W.Icon("box"),
    CompactTrailing  = W.Timer(order.Eta).Caption(),
    Minimal          = W.Icon("box"),
    Background       = WidgetColor.FromHex("#1B5E3F"),   // or SystemBackground = true for the lock screen's own material
}, staleAt: order.Eta.AddMinutes(30));

await activity!.UpdateAsync(layout with { ExpandedCenter = W.Text("Delivered").Headline() });
await activity.EndAsync();
```

- `StartAsync` returns `null` when refused (activities off in Settings, app not in foreground); `AreActivitiesEnabled` tells you whether to show the button at all.
- `Active` lists running activities, including ones started before the app was last killed — ask it instead of keeping a handle in a ViewModel. `ActivitiesChanged` / `ActivityEnded` report ends the app did not make (swipe, push, stale date).
- An activity lives at most 8 hours on iOS. For around-the-clock subjects, let the server restart it with the push-to-start token (see the wiki, "Keeping one on screen around the clock").
- Push updates: `UseSpineWidgets(o => o.LiveActivityPushTokens = true)`, send `activity.GetPushTokenAsync()` and `GetPushToStartTokenAsync()` to the backend at every launch and foreground; `Plugin.Maui.Spine.Server` has `UpdateLiveActivityAsync` and `StartLiveActivityAsync`.

## 4. Build and test

- iOS: macOS with Xcode; the extension compiles with `swiftc` in the iOS build (simulator: `-p:CodesignKey=-`). Device builds need a second App ID (`<ApplicationId>.SpineWidgets`) and profile, App Group on both.
- Android: nothing extra; Live Activities need Android 16 (`StartAsync` returns `null` below).
- Launch the app once after install so the extension registers, then add the widget from the home-screen gallery. `rm -rf obj/spinewidgets` when the extension seems stale.
- The extension is killed at roughly 30 MB: keep images small.

## Do / Don't

- Do put the subject in the `kind` string (`$"delivery:{id}"`); it is how activities are told apart.
- Do use `W.Timer` and `W.Relative` for anything time-based.
- Don't start a Live Activity from a page's constructor or `OnAppearingAsync`; iOS refuses outside a user action.
- Don't rely on colors staying fixed in dark mode unless you fixed both the surface and the text.

## Documentation

- Widgets and Live Activities: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/widgets.md
- Server-side push for activities and widgets: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications-server.md
- Samples: `samples/MauiSpineSampleApp/Widgets/SampleWidget.cs`, `samples/MauiSpinePushNotificationsSampleApp/Widgets/` in https://github.com/jonatansoderberg/Maui.Spine
