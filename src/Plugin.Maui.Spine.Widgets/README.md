# Plugin.Maui.Spine.Widgets

Home-screen widgets and Live Activities for .NET MAUI, built from C#. The app builds a small view tree, Spine serializes it, and a generic native renderer draws it: SwiftUI in a WidgetKit extension on iOS (compiled with `swiftc` during the app's build, no Xcode project) and `RemoteViews` on Android. No app-specific Swift, no Android platform code.

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

On iOS the app needs an App Group entitlement shared with the extension; the package's build targets write it. The documentation covers the developer-portal steps device builds need.

Platforms: iOS 17+ (home-screen and Lock Screen widgets, Live Activities) and Android 5+ (home-screen widgets; Live Activities on Android 16+ as Live Updates). Every other platform, Mac Catalyst included, gets no-op services and `IWidgetService.IsSupported == false`.

## Documentation

- [Widgets and Live Activities](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/widgets.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
