# AI on the server

```bash
dotnet add package Plugin.Maui.Spine.Server.AI
```

`Plugin.Maui.Spine.Server.AI` lets a Spine app's backend use language and image models with the provider as configuration. Every token and image is counted and priced. Budgets and per-user limits stop runaway cost. It is a plain `net10.0` library with a framework reference to ASP.NET Core, so a Minimal API and an Azure Functions isolated worker can both host it. It does not reference `Plugin.Maui.Spine.Server`, so an app with AI and no push pulls in no Firebase.

There is no model abstraction of Spine's own. [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) is the contract: a feature is an `IChatClient` or an `IImageGenerator`, and a provider is the vendor's own adapter. Spine adds what sits around them: named features, metering, prices, budgets, limits and endpoints. The design is in [the proposal](../proposals/spine-ai.md), §3–§5. The app half is [`Plugin.Maui.Spine.AI`](ai-client.md), which calls exposed features and falls back between the device and the server.

---

## Setup

```csharp
builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
{
    ai.Chat("anthropic", (p, model) => new AnthropicClient { ApiKey = p["ApiKey"] }.AsIChatClient(model));
    ai.Chat("openai", (p, model) => new OpenAIClient(p["ApiKey"]).GetChatClient(model).AsIChatClient());
    ai.Image("openai", (p, model) => new OpenAIClient(p["ApiKey"]).GetImageClient(model).AsIImageGenerator());

    ai.UseAzureTableUsageStore(builder.Configuration["Storage"]);   // or UseInMemoryUsageStore(), UseUsageStore(...)

    ai.AuthorizeReport = request => ValueTask.FromResult(IsOwner(request));
    ai.OnBudgetThreshold = (alert, services, ct) => services.GetRequiredService<IPushSender>().SendAsync(
        PushTarget.User(ownerId), new PushNotification { Title = "AI budget", Body = alert.ToString() }, ct);
});

app.MapSpineAI("/ai");
```

- **Providers.** A provider is one line that calls the vendor's adapter: `AnthropicClient.AsIChatClient` from the `Anthropic` package, or `AsIChatClient` and `AsIImageGenerator` from `Microsoft.Extensions.AI.OpenAI`. The app references the vendor packages; Spine references none. `p` is the provider's section under `Ai:Providers`, so keys, endpoints (Azure OpenAI, a local Ollama) and managed identity stay the app's business.
- **A usage store is required.** `AddSpineAI` throws without one. In-memory usage resets with every restart, and budgets reset with it.
- **`IImageGenerator` is experimental** in Microsoft.Extensions.AI (`MEAI001`). A project that touches the type itself adds `<NoWarn>$(NoWarn);MEAI001</NoWarn>`.

## Configuration

```json
"Ai": {
  "Providers": {
    "anthropic": { "ApiKey": "" },
    "openai":    { "ApiKey": "" }
  },
  "Features": {
    "RaceStory":  { "Provider": "anthropic", "Model": "<model id>", "MaxOutputTokens": 800, "Reasoning": "Low",
                    "Budget": { "DaySoft": 2, "DayHard": 5 } },
    "ArenaImage": { "Kind": "Image", "Provider": "openai", "Model": "<image model id>",
                    "Image": { "Size": "1536x1024", "Count": 1 } },
    "Assistant":  { "Provider": "openai", "Model": "<model id>", "Expose": true,
                    "SystemPrompt": "…", "MaxInputTokens": 4000, "MaxTurns": 20,
                    "RequestsPerMinute": { "SignedIn": 20, "Installation": 6 },
                    "Budget": { "DayHard": 3, "PerCaller": { "SignedIn": { "DayHard": 0.20 }, "Installation": { "DayHard": 0.05, "DayCalls": 20 } } } }
  },
  "Prices": {
    "anthropic": { "<model id>": { "InputPerMillion": 3, "CachedInputPerMillion": 0.3, "OutputPerMillion": 15 } },
    "openai":    { "<image model id>": { "PerImage": 0.06 } }
  },
  "Currency": "USD",
  "Budget": { "MonthHard": 100 },
  "TimeZone": "Europe/Stockholm"
}
```

The section is read again when it changes, through any reloading configuration source such as App Configuration or a file. A model swap then takes effect without a deploy.

| Feature setting | What it does |
|---|---|
| `Kind` | `Chat` (the default) or `Image` |
| `Provider`, `Model` | The provider's name as registered, and the model id passed to its factory |
| `Enabled` | `false` turns the feature off without removing it |
| `MaxOutputTokens`, `Temperature`, `Reasoning` | Defaults for every call; an app's own `ChatOptions` win. `Reasoning` is `Low`, `Medium`, `High` and so on |
| `Image.Size`, `Image.Count` | An image feature's defaults, such as `1536x1024` |
| `Expose`, `SystemPrompt`, `MaxInputTokens`, `MaxTurns`, `Callers` | See [Exposed features](#exposed-features) |
| `Budget` | See [Budgets](#budgets) |
| `RequestsPerMinute`, `ConcurrentPerCaller` | See [Rate limits](#rate-limits) |
| `Cache.Ttl` | Response caching for this long, over the host's `IDistributedCache` |
| `EnableSensitiveData` | Records prompts and responses in the OpenTelemetry spans. Off by default |

**A feature is off** when it has no provider or model, when its provider is not registered, when its factory throws, or when its provider's `ApiKey` entry is there but empty. The last is the usual sign of a secret that never arrived: the placeholder in `appsettings.json` without the value from user secrets or Key Vault. The startup log names each feature that is off and why, for example *AI feature 'Story' is off (NotConfigured): provider 'anthropic' has an empty ApiKey under Ai:Providers:anthropic.*

## Using a feature

The server's own endpoints ask `IAIFeatures` for a feature's client on behalf of a caller. The client is metered and held to the feature's budgets and limits:

```csharp
app.MapPost("/races/{id}/story", async (string id, HttpRequest request, IAIFeatures ai, CancellationToken ct) =>
{
    var caller = await ai.IdentifyAsync(request);
    if (ai.Chat("RaceStory", caller) is not { } story) return Results.NotFound();

    try
    {
        var answer = await story.GetResponseAsync([new(ChatRole.System, Coach), new(ChatRole.User, facts)], cancellationToken: ct);
        return Results.Ok(answer.Text);
    }
    catch (AIFeatureException e)
    {
        return e.ToResult();   // 404 { "off": "budget" }, 403 { "off": "signInRequired" } or 429 with Retry-After
    }
});
```

- **`Chat` and `Image` return `null`** while the feature is off: not configured, disabled, or over the app's or the feature's hard budget. `StateOf(feature)` says which.
- **A call can still be refused.** The budget can run out between the lookup and the call, and the caller can be over its own allowance or rate. The call then throws an `AIFeatureException`, and `ToResult()` is the answer to return.
- **The exceptions are shared with the app.** `AIFeatureException`, `AIFeatureOffException`, `AIRateLimitedException`, `AIRequestTooLargeException` and `AIOffReason` live in `Plugin.Maui.Spine.Common`, in the namespace `Plugin.Maui.Spine.AI`. The app's [Spine.AI client](ai-client.md) throws the same types for the same answers. Server code needs `using Plugin.Maui.Spine.AI;` next to `using Plugin.Maui.Spine.Server.AI;`, which holds the `ToResult()` extension.
- **Server-side work with no user behind it** can inject the feature directly: `[FromKeyedServices("RaceStory")] IChatClient story`. It calls as `AICaller.System`, sees configuration changes, and throws `AIFeatureOffException` while the feature is off. It is a singleton, so it can go into singletons. Features added to configuration after startup are reached through `IAIFeatures`.
- **Disposing a client** that `Chat` returned does nothing to the feature, so `using` is harmless.

### Who the caller is

`IdentifyAsync` uses `SpineAIOptions.Identify` when it is set. The default is:

1. **The authenticated user's `sub`**, or their name identifier: `AICaller.SignedIn`.
2. **The `Spine-Installation` header**: `AICaller.Installation`.
3. **The remote address**: `AICaller.Anonymous`.

Installation ids and addresses are claims, not identities. A client that rotates its id gets a fresh allowance, so the feature and app budgets are what protect the bill. The per-caller limits are a courtesy on top. `"Callers": "SignedIn"` turns installations and anonymous callers away with 403.

## Exposed features

A feature with `"Expose": true` can be called straight from the app at `POST /ai/features/{name}`. Any other name gets the same 404 as a feature that is off, so the route does not reveal which features exist.

The server owns the prompt. Without these rules, an exposed feature would be a free API key for anyone who reads the app's traffic:

- **Only the text** of `user` and `assistant` messages is kept. The client's `system` and `tool` messages are dropped, and `SystemPrompt` goes first.
- **Size limits.** More user messages than `MaxTurns` answers 413, and so does more than `MaxInputTokens`, estimated at four characters a token.
- **Options.** The client's options are ignored except the conversation id. The feature's own defaults apply.

**The wire format** is Microsoft.Extensions.AI's own JSON (`AIJsonUtilities.DefaultOptions`), so a client deserialises `ChatResponse` and `ChatResponseUpdate` without types of its own:

```http
POST /ai/features/Assistant
Content-Type: application/json
Accept: text/event-stream
Spine-Installation: 3f2c…

{ "messages": [ { "role": "user", "contents": [ { "$type": "text", "text": "Hej!" } ] } ] }
```

- **Streaming.** With `Accept: text/event-stream`, the answer is server-sent events: one `data:` line per `ChatResponseUpdate`, flushed as it arrives, then `event: done`. The update carrying usage comes last. Without that header, the answer is one `ChatResponse`.
- **Refusals before the first byte.** The first update is awaited before the stream starts, so a refusal, or a provider that fails at once, still gets its status code.
- **Failures after the first byte.** A failure once the stream has started ends it with `event: error`.
- **Provider failures** answer 502, "The model call failed." The provider's own message goes to the log only, because it can name keys or internals.

## Realtime voice sessions

A feature with `"Kind": "Realtime"` sets up a live voice session that the app connects to itself. The server asks the provider for a short-lived token with its own key and the feature's session, and the app opens the provider's WebSocket with that token. The provider's key never reaches the app. The client half is `Plugin.Maui.Spine.Voice` ([Voice](voice.md)); the design is in [the voice proposal](../proposals/spine-voice.md), §6.

```csharp
builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
{
    ai.Realtime("openai", AIRealtimeProviders.OpenAI);          // ApiKey, optional Endpoint
    ai.Realtime("azure", AIRealtimeProviders.AzureOpenAI);      // ApiKey, Endpoint = https://<resource>.openai.azure.com
    …
});

app.MapSpineRealtimeSessions("/ai");   // POST /ai/realtime/{name}
```

```json
"Voice": {
  "Kind": "Realtime", "Provider": "openai", "Model": "gpt-realtime", "Expose": true,
  "SystemPrompt": "You are a friendly voice assistant. Keep it short.",
  "Realtime": { "Voice": "marin", "Voices": ["marin", "cedar", "coral"],
                "TurnDetection": "semantic_vad", "NoiseReduction": "near_field",
                "TranscriptionModel": "gpt-4o-mini-transcribe", "Language": "sv",
                "MaxMinutes": 5, "TokenSeconds": 60 },
  "RequestsPerMinute": { "SignedIn": 3 },
  "Budget": { "DayHard": 5, "PerCaller": { "SignedIn": { "DayCalls": 20 } } }
}
```

and under `Prices`, `"openai": { "gpt-realtime": { "PerMinute": 0.2 } }`.

The request has no body. It may ask for a voice, `?voice=cedar`, such as the one the app's avatar names; the server uses it when it is one of the feature's `Voices`, otherwise the feature's `Voice`, and says which in the answer's `voice`. Everything else about the session is the feature's configuration: the model (for Azure OpenAI, the deployment), `SystemPrompt` as the instructions, `MaxOutputTokens`, and the `Realtime` section. Unset entries leave the provider's defaults. `"TurnDetection": "none"` turns voice activity detection off, so the app commits each turn itself. The audio is PCM16 at 24 kHz both ways.

```http
POST /ai/realtime/Voice
Authorization: Bearer <the app's own sign-in>

200 OK
Cache-Control: no-store

{ "feature": "Voice", "protocol": "openai", "model": "gpt-realtime",
  "url": "wss://api.openai.com/v1/realtime?model=gpt-realtime",
  "token": "ek_…", "expiresAt": "2026-10-15T08:29:29+00:00", "maxMinutes": 5 }
```

The app opens `url` with `Authorization: Bearer {token}` before `expiresAt`. `protocol` names the events spoken on the socket: `openai` for both OpenAI and Azure OpenAI.

- **Signed-in callers only by default.** Unlike chat and image features, a realtime feature with no `Callers` answers only `AICaller.SignedIn`, because a session is paid by the minute. Installations and anonymous callers get 403 `signInRequired`. `"Callers": "Any"` lets them in, with their own rates and budgets.
- **Metering by reservation.** The server never sees the session's real usage. Each token therefore records `MaxMinutes × PerMinute` at once, marked incomplete in the report, and the budgets count it from then on. A failed request to the provider records the call at no cost.
- **The token is not a lock.** Over its connection the app can change the instructions, voice and tools; this was seen with `session.update` against OpenAI. Until it expires, the same token also opens further connections to the same session. A session is not cut off after `MaxMinutes` either. Keep `TokenSeconds` short. Rates, per-caller `DayCalls` and budgets bound what one caller can start. A server that has to hold the configuration, see real usage or run tools needs a relay, which is not built yet.
- **Rate limits** apply to tokens: `RequestsPerMinute` answers 429 with `Retry-After`. `ConcurrentPerCaller` covers only the request for the token, because the server cannot see when a session ends.
- **The log** has the provider's session id with the caller for each token. A provider failure answers 502 with "The model call failed.", and the provider's own message goes to the log.
- **Other providers.** `IAIRealtimeProvider` is one method, so an app can register its own with `ai.Realtime(name, section => …)`. Gemini Live, Azure Voice Live and WebRTC are in the proposal and come when an app needs them. Azure OpenAI uses its key only; Entra is not supported yet.

`IAIFeatures.RealtimeAsync(feature, caller)` does the same for a route of your own.

## Speech

A feature with `"Kind": "Speech"` reads text aloud for the app: PCM16 mono and, when the provider gives them, the mouth shapes (visemes) with their times, which an avatar plays as TimedVisemes ([Avatars](avatars.md)).

```csharp
builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
{
    ai.Speech("azure", AISpeechProviders.AzureSpeech);   // ApiKey, Region (or Endpoint): audio and visemes
    ai.Speech("openai", AISpeechProviders.OpenAI);       // ApiKey, optional Endpoint: audio only
});

app.MapSpineAI("/ai");   // POST /ai/speech/{name}, with the other AI endpoints
```

```json
"Read": {
  "Kind": "Speech", "Provider": "azure", "Model": "neural", "Expose": true,
  "Speech": { "Voice": "sv-SE-SofieNeural", "MaxCharacters": 2000 },
  "RequestsPerMinute": { "SignedIn": 20, "Installation": 5 }
}
```

and under `Prices`, `"azure": { "neural": { "PerMillionCharacters": 16 } }`.

```http
POST /ai/speech/Read
Content-Type: application/json

{ "text": "Välkommen tillbaka." }
```

```json
{ "feature": "Read", "sampleRate": 24000, "audio": "<base64 PCM16>", "durationMs": 1240,
  "visemes": [ { "id": 0, "offsetMs": 0 }, { "id": 19, "offsetMs": 50 }, … ] }
```

- **The voice is the server's.** The app sends only the text; `Speech:Voice`, `Language`, `Instructions` (models that take them, such as `gpt-4o-mini-tts`) and `Speed` are the feature's. A text over `MaxCharacters` (4000 by default) gets 413.
- **Visemes** are in Azure Speech's alphabet of 22 (0 silence … 21 p, b, m), each with its offset from the start of the audio. Azure Speech has them; OpenAI's speech has none, and the app then estimates the mouth from the text.
- **Azure Speech** is spoken over the service's WebSocket, the protocol of Microsoft's open-source JavaScript Speech SDK, because its REST endpoint returns audio only; no native SDK is needed. The voice is a neural voice name, the language comes from it unless `Language` is set, and `Model` is only what the usage report shows. A key is the only sign-in so far.
- **Metering by characters**: each call records its text's length, priced at `PerMillionCharacters`. Callers, rates, `ConcurrentPerCaller` and budgets apply as for chat; unset `Callers` is `Any`.
- **Other providers.** `IAISpeechProvider` is one method: `ai.Speech(name, section => …)`.

`IAIFeatures.SpeechAsync(feature, text, caller)` does the same for a route of your own.

## Metering and prices

Every call through a feature is recorded as an `AIUsageRecord` with these fields:
- feature, provider and model
- caller
- input tokens (cached ones included), cached input tokens, output tokens (reasoning included) and reasoning tokens
- images
- cost, duration and any error

**What is counted:**
- **Streams.** A stream's usage is the sum of its `UsageContent`, recorded when the stream ends, fails, or the caller stops reading.
- **No usage reported.** When the provider reports none, the input is estimated at four characters a token and the record is marked `Complete = false`.
- **Recording failures.** A failure to record is logged as an error. It never turns an answer into an error.

**How a call is priced:** by the row `Ai:Prices:<provider>:<model>`, with the model the call asked for.

| Price setting | Per |
|---|---|
| `InputPerMillion` | Input tokens not read from the provider's cache |
| `CachedInputPerMillion` | Cached input tokens (input price when unset) |
| `OutputPerMillion` | Output tokens, reasoning included |
| `ReasoningPerMillion` | Reasoning tokens, for a model that prices them apart from output |
| `PerImage` | Each image, on top of any tokens the provider reports |
| `Additional` | Counts in `UsageDetails.AdditionalCounts`, by name, per million |

A model without a row is recorded with no cost and logged once, and it is listed under `unpriced` in the report. Budgets then count its calls by number only (`DayCalls`). Prices live only in configuration, because they change more often than Spine is released.

## Budgets

| Level | Settings | Scope |
|---|---|---|
| App | `Ai:Budget`: `DaySoft`, `DayHard`, `MonthSoft`, `MonthHard` | Everything the app spends |
| Feature | `Ai:Features:<Name>:Budget`: the same four | Everything one feature spends |
| Caller | `Budget:PerCaller:SignedIn` and `:Installation`: `DayHard`, `MonthHard`, `DayCalls` | What one caller spends on one feature |

- **Soft and hard levels.** A soft level raises `OnBudgetThreshold` once per scope and period, across instances that share the store. It also sets `Spine-AI-Budget: soft` on the current response, so the app can say "nearly used up for today". A hard level turns the scope off for the rest of the day or month. The app's and feature's hard levels also raise `OnBudgetThreshold`.
- **The overshoot.** A call runs while the spend is under the limit, so the call that crosses it still runs: the overshoot is at most one call, bounded by `MaxOutputTokens`.
- **Per-caller limits by kind.** `Installation` covers installations and anonymous callers. Set it tighter than `SignedIn`; without an `Installation` block, the `SignedIn` limits apply to everyone. `AICaller.System` has no per-caller limits.
- **Day and month** follow `Ai:TimeZone`, UTC when unset.
- **A cached answer** never reaches the budget or the meter, so it is free and still served when the budget is spent.

## Rate limits

- **`RequestsPerMinute: { "SignedIn": 20, "Installation": 6 }`** is a sliding window per caller. An anonymous caller is limited per remote address.
- **`ConcurrentPerCaller`** is the number of calls one caller may have running. A stream counts until its last update.
- **A refused call** throws `AIRateLimitedException`, which answers 429 with `Retry-After`.

The limiters run inside Spine's handler, not as ASP.NET Core middleware, so they work the same under an Azure Functions `[Function]`. They are per instance. The budgets in the store are the backstop across instances.

## Usage store and report

`IAIUsageStore` keeps every record and the running totals per period. A budget check is a few reads of totals before each call.

| Store | |
|---|---|
| `UseInMemoryUsageStore()` | In the process. For tests and samples |
| `UseAzureTableUsageStore(connectionString, prefix)` | Two tables. `{prefix}Usage` holds the records, partitioned by UTC day. `{prefix}Totals` holds the running totals per day and month for the app, each feature and each caller of a feature. Totals are updated in a transaction with ETags, so instances can record at once |
| `UseUsageStore(sp => …)` | Your own; `InMemoryAIUsageStore` shows the contract |

`GET /ai/usage?from=&to=&feature=` sums the records by day, feature and model. It also lists the ten callers who cost the most and the models that ran without a price. `from` and `to` are dates or instants, and the default is the 30 days up to the end of today. The report answers 401 unless `AuthorizeReport` says yes; there is no open default.

## Caching

`"Cache": { "Ttl": "00:10:00" }` puts Microsoft.Extensions.AI's response cache in front of the feature, over whatever `IDistributedCache` the host registers (Redis, or memory in development). The cache key is the messages, the options, the feature and the model. It does not include the caller, so two callers asking the same thing share an entry. Without a registered `IDistributedCache`, the setting does nothing.

For a result cache in app code, such as one story per race fingerprint computed once across concurrent callers, use `HybridCache.GetOrCreateAsync`.

## Observability

Each feature's pipeline runs Microsoft.Extensions.AI's `UseOpenTelemetry` (source `Plugin.Maui.Spine.Server.AI`) and `UseLogging` next to the provider. Its spans and token metrics are therefore real provider calls, not cache hits. Prompt and response content is recorded only with `EnableSensitiveData`.

## Azure Functions

The handlers are public, so a Functions isolated worker with the ASP.NET Core integration (`ConfigureFunctionsWebApplication`) maps them in its own functions:

```csharp
public sealed class AiFunctions
{
    [Function("AiFeature")]
    public Task<IResult> Feature(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ai/features/{name}")] HttpRequest request, string name, CancellationToken ct) =>
        SpineAIEndpoints.FeatureAsync(request, name, ct);

    [Function("AiRealtime")]
    public Task<IResult> Realtime(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ai/realtime/{name}")] HttpRequest request, string name, CancellationToken ct) =>
        SpineAIEndpoints.RealtimeSessionAsync(request, name, ct);

    [Function("AiSpeech")]
    public Task<IResult> Speech(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ai/speech/{name}")] HttpRequest request, string name, CancellationToken ct) =>
        SpineAIEndpoints.SpeechAsync(request, name, ct);

    [Function("AiUsage")]
    public Task<IResult> Usage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ai/usage")] HttpRequest request, CancellationToken ct) =>
        SpineAIEndpoints.UsageAsync(request, ct);
}
```

Server-sent events stream through the Functions host one update at a time. This was checked under Aspire with the Functions Core Tools; a run on Azure has not been checked yet.

## Sample

The push sample server (`samples/MauiSpinePushNotificationsSampleApp.Server`) registers a `demo` provider that needs no key, plus Anthropic and OpenAI. Its `send.http` has requests for an exposed feature (JSON and streamed), a feature the server owns, a picture, a realtime voice token (`Voice`, open to any caller because the sample has no sign-in), and the report.

Images that should be made once and kept, rather than per call, go through [Generated assets](ai-assets.md).
