# Plugin.Maui.Spine.Widgets

Home-screen widgets, Live Activities and Control Center / Quick Settings controls for .NET MAUI, built from C#. The app builds a small view tree, Spine serializes it, and a generic native renderer draws it: SwiftUI in a WidgetKit extension on iOS (compiled with `swiftc` during the app's build, no Xcode project) and `RemoteViews` on Android. No app-specific Swift, no Android platform code.

```bash
dotnet add package Plugin.Maui.Spine.Widgets
```

```csharp
builder
    .UseSpine(options => options.AddAssembly(typeof(MauiProgram).Assembly));   // registers Widgets too
    // .UseSpineWidgets(o => o.OpenWith<HomePage>())                           // only to change the options
```

```xml
<!-- MyApp.csproj: the native side is generated from these items -->
<ItemGroup>
  <SpineWidget Include="next-start" DisplayName="Next start" Families="Small,Medium" />
</ItemGroup>
```

```csharp
[Widget("next-start")]
public sealed class NextStartWidget(IRaceService races, IWidgetService widgets) : IWidgetProvider
{
    public async Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var start = await races.NextStartAsync(cancellationToken);

        return WidgetTimeline
            .Single(new Dictionary<WidgetFamily, WidgetNode>
            {
                [WidgetFamily.Small] = W.VStack(6,
                    W.Text("Next start").Caption().Secondary(),
                    W.Timer(start.Time).Title().Bold()),
            })
            .Refresh(TimeSpan.FromMinutes(30))
            .OpenUrl(widgets.LinkFor(context.Kind));
    }
}
```

Controls — a toggle or a button in iOS 18's Control Center, on the Lock Screen and the Action button, and an Android Quick Settings tile — come from the same package: a `<SpineControl Include="goal-alerts" Type="Toggle" />` item and a `[Control("goal-alerts")]` class implementing `IControlProvider`. A tap runs the provider's `OnActionAsync` in the app's process without opening the app.

On iOS the app needs an App Group entitlement shared with the extension; the package's build targets write it. The documentation covers the developer-portal steps device builds need.

Platforms: iOS 17+ (home-screen and Lock Screen widgets, Live Activities; controls on iOS 18+) and Android 5+ (home-screen widgets; Live Activities on Android 16+ as Live Updates; Quick Settings tiles on Android 7+). Every other platform, Mac Catalyst included, gets no-op services and `IWidgetService.IsSupported == false`.

## Documentation

- [Widgets and Live Activities](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/widgets.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
