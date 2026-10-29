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
| `Kind` | `Chat` (the default), `Image`, `Realtime`, `Speech` or `Transcription` |
| `Provider`, `Model` | The provider's name as registered, and the model id passed to its factory |
| `Enabled` | `false` turns the feature off without removing it |
| `MaxOutputTokens`, `Temperature`, `Reasoning` | Defaults for every call; an app's own `ChatOptions` win. `Reasoning` is `Low`, `Medium`, `High` and so on |
| `Image.Size`, `Image.Count` | An image feature's defaults, such as `1536x1024` |
| `Expose`, `SystemPrompt`, `MaxInputTokens`, `MaxTurns`, `Callers` | See [Exposed features](#exposed-features) |
| `Budget` | See [Budgets](#budgets) |
| `RequestsPerMinute`, `ConcurrentPerCaller` | See [Rate limits](#rate-limits) |
| `Cache.Ttl` | Response caching for this long, over the host's `IDistributedCache` |
| `EnableSensitiveData` | Records prompts and responses in the OpenTelemetry spans, and function arguments and results in the log. Off by default |
| `Functions`, `MaxToolRounds`, `MaxToolResultCharacters` | See [Functions](#functions) |

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

- **Only the text** of `user` and `assistant` messages is kept. The client's `system` and `tool` messages are dropped, and `SystemPrompt` goes first. The one exception is the result of a [device function](#device-functions-in-text-chat) the server itself handed to this caller.
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

A feature with `"Kind": "Realtime"` sets up a live voice session that the app connects to itself. The server asks the provider for a short-lived token with its own key and the feature's session, and the app opens the provider's WebSocket with that token. The provider's key never reaches the app. GPT-Live, which has no such token, goes through a relay on the server instead: see [GPT-Live through the relay](#gpt-live-through-the-relay); a Realtime feature can go through one too, with `"Relay": true` ([below](#the-realtime-api-through-the-relay)). The client half is `Plugin.Maui.Spine.Voice` ([Voice](voice.md)); the design is in [the voice proposal](../proposals/spine-voice.md), §6.

```csharp
builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
{
    ai.Realtime("openai", AIRealtimeProviders.OpenAI);          // ApiKey, optional Endpoint
    ai.Realtime("azure", AIRealtimeProviders.AzureOpenAI);      // ApiKey, Endpoint = https://<resource>.openai.azure.com
    …
});

app.UseWebSockets();                   // for GPT-Live's relay
app.MapSpineRealtimeSessions("/ai");   // POST /ai/realtime/{name}, and /ai/realtime/{name}/relay
```

```json
"Voice": {
  "Kind": "Realtime", "Provider": "openai", "Model": "gpt-realtime-2.1-mini", "Expose": true,
  "SystemPrompt": "You are a friendly voice assistant. Keep it short.",
  "Realtime": { "Voice": "marin", "Voices": ["marin", "cedar", "coral"],
                "TurnDetection": "semantic_vad", "NoiseReduction": "near_field",
                "TranscriptionModel": "gpt-4o-mini-transcribe", "Language": "sv",
                "Reasoning": "low", "MaxMinutes": 5, "TokenSeconds": 60 },
  "RequestsPerMinute": { "SignedIn": 3 },
  "Budget": { "DayHard": 5, "PerCaller": { "SignedIn": { "DayCalls": 20 } } }
}
```

and under `Prices`, `"openai": { "gpt-realtime-2.1-mini": { "PerMinute": 0.05 } }`.

The models are `gpt-realtime-2.1` and the cheaper `gpt-realtime-2.1-mini` (the same token prices as `gpt-realtime` and `gpt-realtime-mini`, which OpenAI removes on 2027-01-20). Both reason; `Reasoning` sets the effort (`minimal`, `low`, `medium`, `high`), and unset leaves the model's default. `low` was accepted and answered quickly in Swedish on 2026-10-20.

The request has no body. It may ask for a voice, `?voice=cedar`, such as the one the app's avatar names; the server uses it when it is one of the feature's `Voices`, otherwise the feature's `Voice`, and says which in the answer's `voice`. It may ask for a shorter limit, `?maxMinutes=1.5` (`IVoiceSession.MaxMinutes`); the server takes it when it is shorter than the feature's `MaxMinutes`, never longer, and says which in the answer's `maxMinutes`. Everything else about the session is the feature's configuration: the model (for Azure OpenAI, the deployment), `SystemPrompt` as the instructions, `MaxOutputTokens`, and the `Realtime` section. Unset entries leave the provider's defaults. `"TurnDetection": "none"` turns voice activity detection off, so the app commits each turn itself. The audio is PCM16 at 24 kHz both ways.

```http
POST /ai/realtime/Voice
Authorization: Bearer <the app's own sign-in>

200 OK
Cache-Control: no-store

{ "feature": "Voice", "protocol": "openai", "model": "gpt-realtime-2.1-mini",
  "url": "wss://api.openai.com/v1/realtime?model=gpt-realtime-2.1-mini",
  "token": "ek_…", "expiresAt": "2026-10-15T08:29:29+00:00", "maxMinutes": 5 }
```

The app opens `url` with `Authorization: Bearer {token}` before `expiresAt`. `protocol` names the events spoken on the socket: `openai` for both OpenAI and Azure OpenAI.

- **Signed-in callers only by default.** Unlike chat and image features, a realtime feature with no `Callers` answers only `AICaller.SignedIn`, because a session is paid by the minute. Installations and anonymous callers get 403 `signInRequired`. `"Callers": "Any"` lets them in, with their own rates and budgets.
- **Metering by reservation, settled by the app's report.** The server never sees a direct Realtime session. Each token therefore records the session's limit (`MaxMinutes`, or the shorter `maxMinutes` the app asked for) × `PerMinute` at once, marked incomplete in the report, and the budgets count it from then on. The token answers with a single-use `usageTicket`, and when the session ends the app posts what the provider reported in `response.done`, summed, to `POST {prefix}/realtime/{feature}/usage`: `{ "ticket": …, "usage": { "inputTokens", "cachedInputTokens", "outputTokens", "audioInputTokens", "audioOutputTokens" }, "seconds": … }`. The server then takes the reservation off and records the tokens at the model's prices (audio tokens apart under the price's `Additional`), so the budget sees the real cost; the ticket is the caller's own, good until an hour after the limit, and spent on use. `Plugin.Maui.Spine.Voice` reports by itself. A failed request to the provider records the call at no cost. A Live session, and a Realtime session through the relay, are settled by the server with what they used when they end.
- **The token is not a lock.** Over its connection the app can change the instructions, voice and tools; this was seen with `session.update` against OpenAI. Until it expires, the same token also opens further connections to the same session. A session is not cut off after `MaxMinutes` either: the app ends it. Keep `TokenSeconds` short. Rates, per-caller `DayCalls` and budgets bound what one caller can start. GPT-Live goes through a relay, which holds the configuration, sees the usage and ends the session on time; Realtime has none. Server functions need none: see [Functions in a voice session](#functions-in-a-voice-session).
- **Tools.** The answer's `tools` lists the feature's [functions](#functions) as `{ name, description, parameters, runsOn, prefer }`, and the same declarations go into the provider's session.
- **Rate limits** apply to tokens: `RequestsPerMinute` answers 429 with `Retry-After`. `ConcurrentPerCaller` covers only the request for the token, because the server cannot see when a session ends.
- **The log** has the provider's session id with the caller for each token. A provider failure answers 502 with "The model call failed.", and the provider's own message goes to the log.
- **Other providers.** `IAIRealtimeProvider` is one method, so an app can register its own with `ai.Realtime(name, section => …)`. Gemini Live, Azure Voice Live and WebRTC are in the proposal and come when an app needs them. Azure OpenAI uses its key only; Entra is not supported yet.

`IAIFeatures.RealtimeAsync(feature, caller, voice: …, maxMinutes: …)` does the same for a route of your own.

### GPT-Live through the relay

[GPT-Live](https://developers.openai.com/api/docs/guides/live) (`gpt-live-1`) is OpenAI's full-duplex voice model, which hands reasoning and functions to a backend model. It has no client token: its WebSocket takes the project key only. A feature with `"Protocol": "Live"` therefore goes through the server:

```json
"VoiceLive": {
  "Kind": "Realtime", "Provider": "openai", "Model": "gpt-live-1", "Expose": true,
  "SystemPrompt": "Talk briefly and naturally. Hand over to the backend for bookings, the time, and goodbyes.",
  "Functions": [ "get_my_bookings", "show_time" ],
  "Realtime": { "Protocol": "Live", "Voice": "marin", "MaxMinutes": 3,
                "Backend": { "Model": "gpt-6-luna", "Reasoning": "low", "HostedTools": [ "web_search" ],
                             "Instructions": "Answer briefly so it can be said aloud. Use the functions." } }
}
```

and under `Prices`, `"gpt-live-1": { "PerMinute": 0.05 }` and the backend's token prices, `"gpt-6-luna": { "InputPerMillion": 0.1, "CachedInputPerMillion": 0.01, "OutputPerMillion": 0.5 }`.

1. `POST /ai/realtime/VoiceLive` checks the caller, rates and budgets as for Realtime, reserves the session's minutes, and answers `protocol: "openai-live"` with `url: "realtime/VoiceLive/relay?ticket=…"`, relative to the prefix. The ticket is signed with ASP.NET Core Data Protection, opens one connection within `TokenSeconds`, and only for the caller it was issued to.
2. The app opens that WebSocket with its own sign-in. The relay opens `wss://api.openai.com/v1/live/sessions` (Azure: `wss://<resource>.openai.azure.com/openai/v1/live/sessions`) with the key and a hashed `OpenAI-Safety-Identifier` for the caller, and writes `session.start` from the configuration: the model, `SystemPrompt` as the voice's instructions, the voice, PCM16 at 24 kHz, and Responses delegation to `Backend` with the feature's functions, its hosted tools and `MaxOutputTokens`.
3. The app's first message carries only the conversation so far (user and assistant text, at most 128 messages and about 8,000 tokens), the functions it runs itself and, if it wants, its own audio format (8). After that the relay passes on audio, mute and unmute, the results of the app's own functions and `session.close`. Typed text arrives as `spine.user_text`, which the relay gives the voice as the user's words. Everything else an app sends is dropped, so the instructions, the model and the backend stay the server's.
4. **Server functions run in the relay**, with the caller's `SpineAIFunctionContext` (the signed-in user, the request's services) and the feature's limits per call. The relay alone asks the backend to go on (`response.create`), once every call of its response has a result. The app sees `spine.function.output` for the transcript.
5. **The limit is enforced.** At the session's limit plus 5 s (the app ends it itself on time) the relay closes it, and the app gets `session.closed` with `spine_reason: "time_limit"`. An app that disconnects has its session closed for it.
6. **The reservation is settled** when the session ends: a record takes the reserved minutes off and adds the seconds GPT-Live counted (`session.closed`), and another adds the backend's tokens, priced by `Backend:Model`. Both are marked `Adjustment`: they count toward cost and budgets but not as calls. A session that never started costs nothing; one whose close was never confirmed is charged its last count or the time since it started, whichever is longer. Hosted tools' own fees (web search) are not metered.
7. **Silence stays off the phone.** GPT-Live streams its answer at real time, silence included; the relay drops the silent pieces, most of the 64 KB/s downstream.
8. **An app may take its audio raw, at its own rate.** `session.start` may carry `"audio": { "rate": 16000, "binary": true }`, both optional. `rate` (8000–48000; 24000 unset) is the rate the app records and plays at: the relay resamples both ways by linear interpolation, enough for speech, and GPT-Live still gets PCM16 at 24 kHz. `"binary": true` carries the audio as WebSocket binary messages of raw PCM16 little-endian mono at that rate instead of base64 in JSON: each binary message from the app becomes one `session.input_audio.append`, and each `session.output_audio.delta` (silence still dropped) comes as one binary message. Every other event is JSON text as before, and so is the audio of an app that did not ask; a binary message from one is dropped. `session.started` says what the relay applies, `"spine_audio": { "rate": 16000, "binary": true }` (`24000` and `false` for an app that asked for nothing), so an app sends no binary before it has seen it. A rate outside the range closes the socket with 1008 (policy violation) before the provider is reached, and the reservation is taken off. A 16 kHz device such as an ESP32 then moves 32 KB/s each way instead of 64 KB/s of base64, and no JSON.
   A function the app offers that the feature's `Functions` does not list (or that the server has not declared with `AddDevice`) is not offered to the model: the relay logs a warning naming it and `session.started` carries `"spine_tools": { "dropped": [ "set_volume" ] }`, so an app or device sees why a function never gets called (a backend left with only `end_conversation` hung up on every question, 2026-10-28). `end_conversation` needs no listing.
   `"pauses": true` asks for the silence the relay drops: GPT-Live streams its answer at real time with the voice's own pauses as silent deltas, and a player that buffers the stream would otherwise play the words back to back and then run dry. With it, right before each audible delta that follows dropped silence, the relay sends `{ "type": "spine.audio.pause", "ms": 820 }` with the length of that silence, so the player can put the pause back and keep the stream's timing; `session.started` echoes `"pauses": true` in `spine_audio`. Silence between two answers is reported the same way, so a player ignores a pause it gets while nothing is queued, or one longer than speech ever pauses. At the end of every session the relay logs how the audio reached it ("Live audio for …": audible seconds and deltas, silence dropped and the longest run, gaps over 300 ms in the delivery and the longest, the slowest stretch of at least three seconds as audio seconds per wall second, and the longest send to the app), which places a stutter heard on the app: before the relay, on the way to the app, or on the app itself.

- **ASP.NET Core only**, with `app.UseWebSockets()` before `MapSpineRealtimeSessions`; a plain request to the relay answers 400 saying so. Azure Functions cannot host it. On Azure App Service, turn Web sockets on in the configuration; the Free tier allows few at once.
- **The connection holds one of the caller's `ConcurrentPerCaller` places** while it runs.
- **A Live feature without `Backend:Model`**, or on a provider that does not speak GPT-Live (`IAILiveProvider`), is off with the reason in the log.
- **Rate limits:** GPT-Live counts concurrent sessions per OpenAI tier (50 at the lowest paid tier); the free tier has none.
- Seen against OpenAI on 2026-10-20 from the Mac: a Swedish question and goodbye, `show_time` called in the app through the backend, 23 s settled at $0.019 for the voice and $0.0003 for the backend.

## Functions

Functions are tools the model calls. One declaration, a `SpineAIFunction` from `Plugin.Maui.Spine.AI.Functions`, serves the app and the server, so it can live in a project both reference. The server runs a function with the caller's context; the app runs the ones that read the device. A feature offers the functions its `Functions` setting lists.

```csharp
// In a project the app and the server share (net10.0, references Plugin.Maui.Spine.AI.Functions).
public static class SharedFunctions
{
    public static SpineAIFunction GetMyBookings { get; } = SpineAIFunction.Create(
        "get_my_bookings", "Lists the signed-in user's bookings in the coming days.", GetMyBookingsAsync, SpineAIFunctionSide.Server);

    public static SpineAIFunction ShowTime { get; } = SpineAIFunction.Create(
        "show_time", "Says what time it is for the user.", () => DateTime.Now.ToString("HH:mm"));

    static async Task<object> GetMyBookingsAsync(SpineAIFunctionContext context, [Description("Days ahead; 14 when unsaid.")] int days = 14)
    {
        if (await context.GetUserAsync() is not { } user) return SpineAIFunctionResult.SignInRequired();
        return await Bookings.ForAsync(user.Id, days, context.CancellationToken);
    }
}

builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
{
    …
    ai.Functions.Add(SharedFunctions.GetMyBookings);                              // runs here
    ai.Functions.Add(SharedFunctions.ShowTime, SpineAIFunctionSides.Both);        // here or in the app
    ai.Functions.Add(SharedFunctions.OpenPage, SpineAIFunctionSides.Device);      // the app runs it (AddDevice)
});
```

**The feature is the catalogue.** A function is declared once, with the sides that may run it. A feature offers what its `Functions` lists, and nothing else, in every session kind:
- typed chat through the server;
- a model on the device;
- Realtime and GPT-Live;
- a voice pipeline.
An app function the feature does not list is not offered anywhere. The catalogue is published:
- in a voice token's `tools`;
- at `GET {prefix}/features/{name}/functions`, with each function's schema, `runsOn`, `prefer`, and the feature's `maxToolRounds` and `maxResultCharacters`.

**Ending on intent is built in.** Spine's `end_conversation` (`SpineAIFunctions.EndConversation`) ends only the app's own session: a call (`IVoiceSession.EndsOnGoodbye`) or a typed chat (`ChatView.EndsOnIntent`). The server offers it to a feature's model whenever the app's request offers it, with Spine's own declaration. It needs no `AddDevice` and no `Functions` entry, and the app runs it.

**A model on the device** (Apple Intelligence through `o.Feature(…).OnDevice(…).Server(…)`) takes its tools from that catalogue:
- the app's own functions where the device runs them;
- the server's functions, forwarded to `POST {prefix}/functions/{feature}/{name}` with the user's sign-in, so the same question gets the same answer whichever model answers.

```json
"Assistant": { "Provider": "openai", "Model": "<model id>", "Expose": true,
               "Functions": [ "get_my_bookings", "show_time" ], "MaxToolRounds": 4 }
```

- **The context.** A `SpineAIFunctionContext` parameter is filled from the call and left out of the schema, so the model never sees it, and so are `CancellationToken` and `IServiceProvider`. It carries:
  - `Caller` (`AICaller`), `Feature` and `RunsOn`;
  - `User`: the request's `ClaimsPrincipal` for a signed-in caller;
  - `TimeZone` and `Locale`: what the app sent in `Spine-Time-Zone` and `Spine-Locale`, checked; an unknown zone or language is `null`;
  - `ConversationId` (`Spine-Conversation`, else the chat's provider conversation) and `CallId` (the model's id for a forwarded or relayed call);
  - `Services` (the request's) and `CancellationToken`.
- **The user's time.** `Now()` is the time in the user's zone, and `GetTimeZone()` the zone: on the server UTC when the app sent none, never the server's own zone.
- **The user.** `GetUserAsync()` answers the signed-in user's `SpineUser` (id, email, names, provider, organization) through Spine.Server.Authentication when it is registered, and otherwise from the access token's claims. Installations and anonymous callers get `null`. Server-only code that needs the whole `SpineAuthUser` resolves `IUserResolver` from `context.Services`.
- **Signed out.** Return `SpineAIFunctionResult.SignInRequired()`: the model reads `{ "error": "signInRequired", "message": … }` and tells the user to sign in. `SpineAIFunctionResult.Error(message, code)` covers other refusals.
- **Checks before it runs.** Arguments the schema does not have, and missing required ones, answer `invalidArguments` without calling the function. An exception answers `failed` (its message goes to the log only), and a result longer than `MaxToolResultCharacters` (16 000 by default) answers `tooLarge`.
- **The log.** Every call logs the function, the side, the caller and how long it took at Information; arguments and results at Debug.
- **Rounds are calls.** Function invocation sits outside the feature's gate and meter, so every round to the model is checked against the budgets, rates and `ConcurrentPerCaller` and metered on its own. A question that calls one function is two calls. `MaxToolRounds` (4 by default) caps the rounds per request.
- **No cache.** A feature with functions skips `Cache`: its answers depend on the caller, and the cache key does not.
- **`AICaller.System`.** The keyed `IChatClient` runs functions too, with a System caller and no user.
- **Unknown names.** A name in `Functions` that nothing registered is logged at startup.
- **OpenAI's reasoning models.** On chat completions, `gpt-5.4-nano` refuses function tools with a reasoning effort (400, "Function tools with reasoning_effort are not supported"): give such a feature `"Reasoning": "None"`.

### Device functions in text chat

A device function runs in the app: the device's own state, such as the time or the current page. The exposed chat still refuses client-sent tool messages; device calls go through a channel of their own:

1. The app's request names the device functions it has, never their schemas: `"functions": [ { "name": "show_time", "prefer": "device" } ]`. Spine.AI sends it from `SpineAIOptions.Functions`, and adds the functions in the call's own `ChatOptions.Tools` (a voice pipeline's `end_conversation`), which it then runs itself.
2. The server offers only names the feature lists and that it declared for the device (`Add(…, Device)` or `Both`), with its own declaration.
3. When the model calls one, the answer ends with that `FunctionCallContent`. The server keeps the call (its id, name and arguments, for this caller and feature, ten minutes) in memory, or in the host's `IDistributedCache` when one is registered.
4. The app runs the function and posts the conversation again with the call and its result.
5. The server takes a result only for a call it keeps for this caller, once, and puts its own record of the call before it. A forged, replayed or other caller's result is dropped.

Calls the server ran itself reach the app with `InformationalOnly = true`, so the app shows them but never runs them again.

**A round with both.** When the model asks for a server function and a device function in the same round, the whole round goes back to the app. The server first runs its own calls in that round, shows the app their results, and keeps them. With the app's answer the model gets the server's own result, never the app's copy of it.

**The same answers on every path.** A function always answers the model, so it can tell the user what happened:
- Arguments that are not a JSON object give `invalidArguments`, and the function never runs.
- A refusal gives `refused` with the reason: the feature is off, or too many calls.
- Sign-in gives `signInRequired`.
- An exception gives `failed`.
This holds in typed chat, in a call forwarded from a realtime session and in the GPT-Live relay. The relay runs each call in its own DI scope.

**Both sides.** A function the server runs and declares for the device runs on the side the request's `prefer` names, else on the side the function prefers (`SpineAIFunction.Prefer`). The app sets it per chat with `ChatOptions.Prefer(name, side)`.

### The Realtime API through the relay

A `Kind: Realtime` feature with `"Relay": true` under `Realtime` keeps the app off the provider: the token request answers `"protocol": "openai-relay"` with a relative `url` (`realtime/{feature}/relay?ticket=…`, like Live's), the app connects to the server with its own sign-in, and the server opens the provider's Realtime WebSocket with its key. Both sides speak the Realtime API's own events, so a client that speaks `openai` directly speaks `openai-relay` unchanged; `Plugin.Maui.Spine.Voice` does. What the relay adds:

- **The session is the feature's.** The relay writes `session.update` from the configuration (instructions, voice, audio, turn detection, transcription, reasoning) and the app's `session.update` carries only its functions, as far as the feature's `Functions` lists them (`end_conversation` always; the rest is named in the log and dropped). `conversation.item.create` passes only the user's and assistant's own messages and function results, `response.create` passes without its `response` object, and `input_audio_buffer.*`, `conversation.item.truncate` / `delete` / `retrieve` and `response.cancel` pass as they are.
- **Server functions run on the server.** A `response.function_call_arguments.done` for one of the feature's server functions never reaches the app: the relay runs it with the caller's context, answers the model (`conversation.item.create` + `response.create`) and tells the app with `spine.function.output`. The app's own functions arrive as usual and the app answers them.
- **Audio at the app's rate, raw when it asks.** `{ "type": "spine.audio", "audio": { "rate": 16000, "binary": true } }` before any audio; the relay answers with the same event carrying what it applies, `session.created` carries it as `spine_audio`, and from then on the app's binary messages are `input_audio_buffer.append` at the provider's 24 kHz and every `response.output_audio.delta` comes as one binary message at the app's rate. Silence stays in the audio (the Realtime API delivers an answer faster than real time, with its pauses inside), so there are no pause markers.
- **Usage is what the model reports.** Every `response.done` is counted: text tokens as input/output, cached tokens as cached, and audio tokens apart as `audio_input_tokens` / `audio_output_tokens` under the price's `Additional` (they cost ten and more times the text price); at the end the reservation (`MaxMinutes × PerMinute`) is taken off and the tokens are recorded, so the budget sees the real cost and a session that only listened costs nothing. `PerMinute` is then only the reservation while the session runs.
- **The limit holds.** The relay ends the session at `MaxMinutes` (plus 5 s) and closes the app's socket with "The session reached its time limit."; `session.close` from the app ends it early.

```json
"Assistant": { "Kind": "Realtime", "Provider": "openai", "Model": "gpt-realtime-2.1-mini", "Expose": true,
               "Functions": [ "get_my_bookings", "show_time" ],
               "Realtime": { "Relay": true, "Voice": "marin", "TurnDetection": "server_vad", "TranscriptionModel": "gpt-4o-transcribe",
                             "Reasoning": "low", "MaxMinutes": 60, "TokenSeconds": 60 } }
```

and under `Prices`, `"gpt-realtime-2.1-mini": { "PerMinute": 0.03, "InputPerMillion": 0.6, "CachedInputPerMillion": 0.3, "OutputPerMillion": 2.4, "Additional": { "audio_input_tokens": 10, "audio_output_tokens": 20 } }`.

A device such as the AI:Dash Desk Display (ESP32) then has one TLS connection to its own server, 32 KB/s of 16 kHz PCM each way instead of 64 KB/s of base64 at 24 kHz, no resampling, and exact metering; and since an open Realtime session that sends no audio costs no tokens, it keeps one parked between conversations and the wake word only opens the microphone.

### Functions in a voice session

A realtime session talks to the provider directly, so the server never sees its calls. The token lists the feature's functions, and the app forwards a call to a server function:

```http
POST /ai/functions/Voice/get_my_bookings
Authorization: Bearer <the app's own sign-in>

{ "callId": "call_1", "arguments": { "days": 7 } }

200 OK
{ "result": { "bookings": [ … ] } }
```

- Only an exposed realtime feature that lists the function answers, for the callers its `Callers` allows. Any other name gets the same 404 as a feature that is off.
- The calls have rate and concurrency limiters of their own with the feature's `RequestsPerMinute` and `ConcurrentPerCaller`, so a conversation's calls do not use up its caller's session starts. The feature's budgets must not be spent. A call costs nothing and is not a usage record.
- A function that needs sign-in, fails or gets arguments its schema does not have still answers 200, with the error as the result: it is for the model.
- `MapSpineAI` maps the route; `SpineAIEndpoints.FunctionAsync` is the handler for a Functions host.

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

### Streamed speech

A voice conversation reads its answer sentence by sentence and wants the first sound at once. With `Accept: audio/pcm` the endpoint answers raw PCM16 as the provider makes it, flushed piece by piece, with the rate in a header:

```http
POST /ai/speech/ReadOpenAI
Accept: audio/pcm
Content-Type: application/json

{ "text": "Klockan är tre." }
```

```http
HTTP/1.1 200 OK
Content-Type: audio/pcm
Spine-Sample-Rate: 24000

<PCM16 little-endian mono, as it is made>
```

- **OpenAI** streams (`stream_format: audio`): `AISpeechProviders.OpenAI` implements `IAIStreamingSpeechProvider`. A provider that does not stream sends its whole audio as one piece, so the app reads either the same way.
- **No visemes** in a stream; ask without the header for them.
- **Metering** is by characters as above, recorded when the stream ends. `IAIFeatures.StreamSpeechAsync` does the same for a route of your own.

## Transcription

A feature with `"Kind": "Transcription"` writes speech down for the app: a voice pipeline whose phone cannot do it itself ([Voice pipelines](voice.md#a-pipeline-any-chat-model)) sends the user's turn here.

```csharp
builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
{
    ai.Transcription("openai", AITranscriptionProviders.OpenAI);   // ApiKey, optional Endpoint
});
```

```json
"Transcribe": {
  "Kind": "Transcription", "Provider": "openai", "Model": "gpt-4o-mini-transcribe", "Expose": true,
  "Transcription": { "Language": "sv-SE", "MaxSeconds": 30 },
  "RequestsPerMinute": { "SignedIn": 30, "Installation": 20 }
}
```

and under `Prices`, `"openai": { "gpt-4o-mini-transcribe": { "PerMinute": 0.003 } }`.

```http
POST /ai/transcribe/Transcribe
Content-Type: audio/wav

<a WAV file: PCM16 mono>
```

```json
{ "feature": "Transcribe", "text": "Vad är klockan?" }
```

- **The body is a WAV file**, PCM16 mono; anything else gets 400. Audio longer than `MaxSeconds` (120 by default) gets 413, before the provider is asked.
- **`Language`** (BCP-47; OpenAI is given its language part) and **`Prompt`** (names and terms to expect) are the feature's.
- **Metering by minutes** of audio, from the WAV's header, priced at `PerMinute`. Callers, rates and budgets apply as for chat; unset `Callers` is `Any`.
- **Other providers.** `IAITranscriptionProvider` is one method: `ai.Transcription(name, section => …)`. `IAIFeatures.TranscribeAsync(feature, wav, caller)` does the same for a route of your own.

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
| `PerMinute` | A realtime session's reserved minutes, or a transcription's minutes of audio |
| `PerMillionCharacters` | The characters of a speech feature's text |
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

    [Function("AiFunction")]
    public Task<IResult> Function(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ai/functions/{feature}/{name}")] HttpRequest request, string feature, string name, CancellationToken ct) =>
        SpineAIEndpoints.FunctionAsync(request, feature, name, ct);

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

The push sample server (`samples/MauiSpinePushNotificationsSampleApp.Server`) registers a `demo` provider that needs no key, plus Anthropic and OpenAI. Its `send.http` has requests for an exposed feature (JSON and streamed), a feature the server owns, a picture, a realtime voice token (`Voice`, open to any caller), and the report. `Assistant` and `Voice` list two functions from `samples/MauiSpineSampleApp.Shared`, which the Showcase shares: `get_my_bookings` runs on the server with the signed-in user, and `show_time` runs on the phone or the server. The demo provider calls a function it is given when the question asks for one ("bokning", "klockan"), so the round trip runs without a key.

Images that should be made once and kept, rather than per call, go through [Generated assets](ai-assets.md).
