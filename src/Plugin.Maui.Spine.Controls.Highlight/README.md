# Plugin.Maui.Spine.Controls.Highlight

Highlights for any .NET MAUI view, and spotlight tours: a rim of moving colour while a model works on a card (`Edge`), rings that say "tap here" (`Pulse`), a sweep of light (`Sheen`), a point of light along the border (`Trace`) and a breathing halo (`Breathe`). Each is drawn by the platform's compositor above the view's content: Core Animation on Apple, an overlay drawable on Android, Composition on Windows. Under Reduce Motion each kind is still.

```bash
dotnet add package Plugin.Maui.Spine.Controls.Highlight
```

The attached properties need no registration. `UseSpine()` registers `IHighlights` for an app that references the package; without Spine, call `builder.UseHighlights()`.

```xml
<!-- a model is working on this card -->
<Border Highlight.Kind="Edge" Highlight.IsActive="{Binding IsSummarising}" Highlight.Tip="Summarising" />

<!-- until tapped, once per install -->
<Button Text="Add" Highlight.Kind="Pulse" Highlight.Repeat="UntilTapped" Highlight.Key="onboarding.add" />
```

```csharp
await highlights.PlayAsync(saveButton, HighlightKind.Sheen, HighlightRepeat.Times(2));

// a tour: a view, a header bar action, a tab
await highlights.TourAsync("onboarding",
    (addButton, "Add an event"),
    new TourStep(viewModel.FilterAction, "Filter by calendar"),
    new TourStep(typeof(SettingsPage), "Your calendars live here"));

// a glow round the whole window, following a level such as a voice session's output
using var edge = highlights.AttachEdge(window);
edge.Intensity = () => voice.Frame.OutputLevel;
highlights.Reset(); // "Show tips again"
```

Platforms: Android, iOS, Mac Catalyst, Windows.

## Documentation

- [Highlight](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/highlight.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
