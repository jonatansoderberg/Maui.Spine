# Plugin.Maui.Spine.Server.AI

AI on the server for Spine apps. Features are named in configuration, and each one names its provider and model. Every token and image is metered and priced. Budgets and per-user limits stop runaway cost. The endpoints run on ASP.NET Core and in an Azure Functions isolated worker. It is built on Microsoft.Extensions.AI: a provider is the vendor's own adapter, registered in one line, so this package references no vendor SDK.

```bash
dotnet add package Plugin.Maui.Spine.Server.AI
```

```csharp
builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
{
    ai.Chat("anthropic", (p, model) => new AnthropicClient { ApiKey = p["ApiKey"] }.AsIChatClient(model));
    ai.Chat("openai", (p, model) => new OpenAIClient(p["ApiKey"]).GetChatClient(model).AsIChatClient());
    ai.UseAzureTableUsageStore(cfg["Storage"]);   // or UseInMemoryUsageStore()
});

app.MapSpineAI();   // POST /ai/features/{name} for exposed features, GET /ai/usage
```

```json
"Ai": {
  "Providers": { "anthropic": { "ApiKey": "" } },
  "Features": {
    "RaceStory": { "Provider": "anthropic", "Model": "<model id>", "MaxOutputTokens": 800,
                   "Budget": { "DaySoft": 2, "DayHard": 5 } }
  },
  "Prices": { "anthropic": { "<model id>": { "InputPerMillion": 3, "OutputPerMillion": 15 } } },
  "Currency": "USD"
}
```

```csharp
var caller = await ai.IdentifyAsync(request);
if (ai.Chat("RaceStory", caller) is not { } story) return Results.NotFound();   // off: no key, disabled or budget spent
```

A feature with `"Kind": "Realtime"` hands the app a short-lived token for a live voice session at `POST /ai/realtime/{name}` (`ai.Realtime("openai", AIRealtimeProviders.OpenAI)` and `app.MapSpineRealtimeSessions()`). The provider's key stays on the server, and each session reserves its minutes from the budgets; the app may ask for a shorter limit. GPT-Live (`"Protocol": "Live"`), which has no client token, goes through a relay on the server instead, which ends the session at its limit and settles the reservation with what it used (ASP.NET Core, with `app.UseWebSockets()`).

A feature with `"Kind": "Speech"` reads text aloud at `POST /ai/speech/{name}`: PCM16 with the mouth shapes and their times from Azure Speech (`ai.Speech("azure", AISpeechProviders.AzureSpeech)`, over the service's WebSocket, no native SDK), or audio alone from OpenAI (`AISpeechProviders.OpenAI`). Metered by characters (`PerMillionCharacters`). With `Accept: audio/pcm` it streams the audio as it is made (OpenAI), for a voice pipeline's answer.

A feature with `"Kind": "Transcription"` writes speech down at `POST /ai/transcribe/{name}` from a WAV body (`ai.Transcription("openai", AITranscriptionProviders.OpenAI)`), metered by minutes of audio (`PerMinute`).

Swapping provider is a change to `Provider` and `Model` in configuration, and takes effect without a deploy.

## Documentation

- [AI on the server](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/ai-server.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
