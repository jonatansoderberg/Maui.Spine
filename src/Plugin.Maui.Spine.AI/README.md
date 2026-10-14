# Plugin.Maui.Spine.AI

AI in a Spine app without keys in the app. Each named feature is one `IChatClient`:
- It streams from the app's [Spine.Server.AI](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/ai-server.md) backend.
- It can try a model on the device first, such as Apple Intelligence.
- It answers "off", "budget spent" and "too many calls" in one shape.
- On-device usage is counted on the device.

It is built on Microsoft.Extensions.AI.

```bash
dotnet add package Plugin.Maui.Spine.AI
```

```csharp
builder.UseSpineAI(o =>
{
    o.Backend = new Uri("https://api.example.com/ai");          // the prefix given to MapSpineAI
    o.Feature("Assistant", f => f
        .OnDevice(_ => new AppleIntelligenceChatClient())       // your own reference to Microsoft.Maui.Essentials.AI
        .ThenServer());
    o.Feature("RaceStory", f => f.Server());
});
```

```csharp
public partial class AssistantViewModel([FromKeyedServices("Assistant")] IChatClient chat) : ViewModelBase
{
    async Task AskAsync(string prompt, CancellationToken ct)
    {
        await foreach (var update in chat.GetStreamingResponseAsync(prompt, cancellationToken: ct))
            Answer += update.Text;
    }
}
```

A feature that is off throws `AIFeatureOffException`, which `TaskState` shows as its empty state.

Images a model makes once on the server (`Plugin.Maui.Spine.Server.AI.Assets`) show on any `Image`: a placeholder while the server makes them, the image when it is ready.

```xml
<Image GeneratedImage.Feature="Postcard" GeneratedImage.Key="{Binding City}"
       GeneratedImage.Placeholder="postcard_blank.png" GeneratedImage.FailedSource="postcard_missing.png" />
```

## Documentation

- [AI in the app](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/ai-client.md)
- [AI on the server](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/ai-server.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
