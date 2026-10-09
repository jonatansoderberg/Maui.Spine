# Spine.AI: named AI features on the server, metering and budgets, a client and a chat view (proposal)

**Status:** Proposal, 2026-10-09, with the owner's answers in [Decisions](#decisions-2026-10-09). Not started. Roadmap: [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505). Issues: [#510](https://github.com/jonatansoderberg/Maui.Spine/issues/510) (`Plugin.Maui.Spine.Server.AI`: features, providers, metering, cost, budgets, §3–§5), [#511](https://github.com/jonatansoderberg/Maui.Spine/issues/511) (generated-asset pipeline, §6), [#512](https://github.com/jonatansoderberg/Maui.Spine/issues/512) (`Plugin.Maui.Spine.AI` client, §7), [#513](https://github.com/jonatansoderberg/Maui.Spine/issues/513) (`Plugin.Maui.Spine.Controls.Chat`, §8), [#514](https://github.com/jonatansoderberg/Maui.Spine/issues/514) (spike: on-device `IChatClient` for Android and Windows, outside the repo, §9). Realtime voice sessions also live in `Server.AI` ([#517](https://github.com/jonatansoderberg/Maui.Spine/issues/517), [spine-voice.md](spine-voice.md) §6). Live visual concepts: https://claude.ai/artifact/J5TCSDk8Rk1vZNti9MBM3J (private until the owner shares it). Nothing here has been built or run. Package versions and API names were checked by downloading the packages from nuget.org on 2026-10-09 and reading their XML docs; that is marked "verified". The rest comes from documentation and source code.
**Question:** How can Spine apps use language and image models with the provider as configuration, with every token and image counted and priced, with budgets and per-user limits that stop runaway cost, and with a client and a chat view that hide whether the model runs on the device or on the server, without Spine inventing its own model abstractions?
**Answer:** Build on Microsoft.Extensions.AI (M.E.AI) and add only what it lacks. `Plugin.Maui.Spine.Server.AI` reads named features from `Ai:Features:<Name>`, builds one keyed `IChatClient` or `IImageGenerator` per feature with `ChatClientBuilder`, and inserts two pieces of middleware of its own: a budget gate and a meter that prices `UsageDetails` from a price table in configuration and writes to an `IAIUsageStore` (in memory or Azure Table, like the push register). Providers come from the vendors' own M.E.AI adapters, one line each, so Spine ships no package per provider. A feature without a key, or whose hard budget is spent, is "off", which is the answer Orientera's app already handles. Generated images get a look-up, order, generate, archive pipeline lifted from Orientera. On the device, `Plugin.Maui.Spine.AI` gives an `IChatClient` per named feature that streams from the server and can try an on-device model first; `Plugin.Maui.Spine.Controls.Chat` renders any `IChatClient` with streaming markdown (Markdig, in that package only). Android and Windows on-device models are a spike outside the repo.


## Decisions (2026-10-09)

The owner answered the open questions in §12. Where an answer differs from the text below, the answer wins.

1. Exposed features allow the installation id with tighter limits, and signed-in users get higher limits.
2. **Changed from the proposal:** Azure Tables, Blobs and Queues stay **inside `Plugin.Maui.Spine.Server.AI`**. There is no separate `.Azure` package.
3. **Changed from the proposal:** `MarkdownView` becomes its own package, **`Plugin.Maui.Spine.Controls.Markdown`**, and `Controls.Chat` references it.
4. One currency per app.
5. On-device usage stays on the device.
6. The samples pin the July maui-labs preview of Microsoft.Maui.Essentials.AI for .NET 10.
7. "At most one call over budget" is accepted for chat and images.

---

## 1. The conclusion in short

| Question | Answer | Evidence |
|---|---|---|
| Own model abstractions? | **No.** `IChatClient`, `IImageGenerator`, `ChatClientBuilder`, `UsageDetails`, `UseOpenTelemetry`, `UseDistributedCache`, `AddKeyedChatClient`, `AddKeyedImageGenerator` all exist in M.E.AI 10.10. | Verified: `Microsoft.Extensions.AI` 10.10.0, `.Abstractions` 10.10.1 |
| Is `IImageGenerator` stable? | **No, experimental:** `[Experimental("MEAI001")]`. `Server.AI` suppresses MEAI001 inside the package; apps that touch the type directly see the warning. | `IImageGenerator.cs`, `DiagnosticIds.cs` in dotnet/extensions `main` |
| Do the vendors ship M.E.AI adapters? | **Yes, all three checked.** Anthropic's official SDK (`Anthropic` 12.54.1, MIT, anthropics/anthropic-sdk-csharp) has `AsIChatClient` built in, and so does 12.40, the version Orientera uses. OpenAI: `Microsoft.Extensions.AI.OpenAI` 10.10.1 (on `OpenAI` 2.14.0) has `AsIChatClient` and `AsIImageGenerator`. Google: `Google.GenAI` 1.25.0 has both. | Verified in the packages' XML docs |
| One Spine package per provider? | **No.** A provider is one registration line in the app's server that calls the vendor's adapter. | §3.2 |
| Where does cost come from? | `UsageDetails` (input, output, cached input, reasoning tokens) on chat responses and on `ImageGenerationResponse.Usage`, priced from `Ai:Prices` in configuration. Prices are never in code. | §4 |
| How is a budget enforced? | Before each call: spent so far in the feature's and the caller's day and month against soft and hard limits. A call can overshoot by at most one call's worst case, bounded by `MaxOutputTokens`. | §5.1 |
| Who is "the user"? | The signed-in user from `Plugin.Maui.Spine.Server.Authentication` (#520) when present, otherwise the push installation id, otherwise a per-install key. The last two are claimed by the client, so a global feature budget is the backstop. | §5.2 |
| Azure Functions? | **Yes**, the same way as push: `SpineAIEndpoints.HandleAsync(request, ct)` is callable from a `[Function]`. ASP.NET Core rate-limiting middleware does not run there, so Spine applies its limiter inside the handler. | `SpinePushNotificationsEndpoints.cs:24`, §5.3 |
| Orientera's first? | **Yes.** Both of its features map one to one onto named features, and its Anthropic SDK already has the adapter. It consumes Spine via NuGet, so it moves after a release. | §2.3, §11 |
| On-device on Android and Windows? | **Not in Spine.** `Microsoft.Maui.Essentials.AI` covers Apple only; Android and Windows say "coming soon". A spike outside the repo tells the client's fallback chain what it can rely on. | §9 |

---

## 2. What exists

### 2.1 In Spine

| Part | Where | What it means here |
|---|---|---|
| Server package shape | `Server/ServiceCollectionExtensions.cs:19` (`AddSpinePushNotifications`, options validated at startup), `Server/EndpointRouteBuilderExtensions.cs:17` (`MapSpinePushNotifications`) | `AddSpineAI` / `MapSpineAI` copy it: options object, `Validate()`, singletons, endpoints under a prefix. |
| Functions support | `Server/SpinePushNotificationsEndpoints.cs:7-24` (`HandleAsync(HttpRequest, ct)` for both hosts), `Server/Plugin.Maui.Spine.Server.csproj:16-23` (one `FrameworkReference` to ASP.NET Core serves both), `docs/wiki/push-notifications-server.md:282-289` | The same split: the endpoint body is a static method a `[Function]` can call. |
| Store pattern | `Server/IPushInstallationStore.cs:13`, `InMemoryPushInstallationStore.cs`, `AzureTablePushInstallationStore.cs:16`, chosen by `UseInMemoryStore()` / `UseAzureTableStore(conn, prefix)` (`Server/SpinePushNotificationsOptions.cs:164-188`) | `IAIUsageStore` gets the same three options and the same table prefix convention. `Azure.Data.Tables` is already a dependency of `.Server` (`.csproj:31`). |
| Gate hook | `Authenticate` (`Server/SpinePushNotificationsOptions.cs:125`): unset means open | AI endpoints default to the opposite: they cost money, so anonymous access has to be turned on (as spine-voice.md §6.2 also proposes). |
| Installation id | `PushNotificationService.GetInstallationIdAsync` (`PushNotifications/Services/PushNotificationService.cs:96-104`, a GUID in `SecureStorage`); the client's `Backend` and `AuthorizationHeader` (`PushNotifications/SpinePushNotificationsOptions.cs:95,101`) | The stable per-install key the AI client can send when nobody is signed in, and the header pattern it copies. |
| Users in push | `PushInstallation.UserId` (`Common/Push/PushInstallation.cs:76`), `PushTarget.User` (`Server/PushTarget.cs:46`), `IPushSender.SendAsync` (`Server/IPushSender.cs:16`) | The budget-threshold alert to the owner is one `SendAsync` to `PushTarget.User(ownerId)`, wired by the app (§5.4). |
| Keyboard | `SoftKeyboard.Top` (`Core/SoftKeyboard.cs:13`), `KeyboardAvoidance` on `[Navigable…]` (`Core/NavigableAttribute.cs:177`) and `SpineOptions` (`:51`), `ViewModelBase.KeyboardInset` (`Core/ViewModelBase.cs:86`), PR #458 | The chat composer sits in the page's footer and rides above the keyboard with no code of its own. |
| Loading and "thinking" | `Skeleton.IsActive` (`Controls.Shimmer/Skeleton.cs:24`), `Shimmer` (`Shimmer.cs:25`); Highlight `Edge` is proposed in [spine-highlight.md](spine-highlight.md) (#509) | The waiting bubble before the first token. |
| Feature off as UI | `TaskState` with `Empty` (`Core/TaskState.cs:22,32`), `StateView` (`Presentation/StateView.cs:34`) | An off feature is shown as the empty state, not an error. |
| Haptics | `Haptics.Play` (`Extensions/Haptics.cs:123`) | A selection tick on send, nothing per token. |
| Module registration | `build/<Package>.props` with `<SpineModule>` (e.g. `Images/build/Plugin.Maui.Spine.Images.props`), `docs/wiki/packages.md` "Adding a module" | `Spine.AI` and `Controls.Chat` register themselves when referenced. |

Spine has no AI code today, no reference to M.E.AI and no markdown rendering.

### 2.2 In the ecosystem (verified on nuget.org, 2026-10-09)

| Package | Version | What matters |
|---|---|---|
| `Microsoft.Extensions.AI` / `.Abstractions` | 10.10.0 / 10.10.1 | `ChatClientBuilder`; middleware `UseOpenTelemetry`, `UseDistributedCache`, `UseLogging`, `UseFunctionInvocation`, `UseChatReducer`; `AddKeyedChatClient`, `AddKeyedImageGenerator`; `UsageDetails` (`InputTokenCount`, `OutputTokenCount`, `CachedInputTokenCount`, `ReasoningTokenCount`, `AdditionalCounts`), `UsageContent` in streaming updates. |
| `Anthropic` (official) | 12.54.1 | `AnthropicClientExtensions.AsIChatClient(IAnthropicClient, model, defaultMaxTokens)`; depends on `.Abstractions` ≥ 10.5.1. No image generator (Anthropic has no image model). |
| `OpenAI` + `Microsoft.Extensions.AI.OpenAI` | 2.14.0 + 10.10.1 | `AsIChatClient` (Chat and Responses clients), `AsIImageGenerator(ImageClient)`. The adapter pins `OpenAI` 2.14.0, so Orientera's 2.13 moves up one minor. |
| `Google.GenAI` | 1.25.0 | `AsIChatClient`, `AsIImageGenerator`. |
| `Microsoft.Extensions.Caching.Hybrid` | 10.10.0 | `HybridCache`: L1 in process, L2 any `IDistributedCache`, stampede protection (one factory call per key). |
| `Microsoft.Maui.Essentials.AI` | 11.0.0-preview.6.26360.8 (`net11.0-*`, from dotnet/maui); 11.0.0-preview.5.26358.7 (`net10.0-*`, from dotnet/maui-labs) | `AppleIntelligenceChatClient : IChatClient` and `NLEmbeddingGenerator`. Experimental, always `-preview`. Chat on iOS, Mac Catalyst and macOS 26+; Android and Windows "coming soon" (dotnet/maui#32539). Active: image input landed in maui-labs on 2026-10-07. |
| `Markdig` | 1.4.0, BSD-2-Clause, `netstandard2.0`/`net8.0`/`net10.0`, no dependencies | The markdown parser for §8. |

### 2.3 What Orientera does, and what is general

Orientera's two AI features run only in its Azure Functions backend (Aspire locally) and call vendor SDKs directly: Anthropic 12.40 writes a race story from split times, OpenAI 2.13 (gpt-image) makes arena images. There is no M.E.AI, no token or cost tracking, and the endpoints are anonymous without rate limits or per-user quotas.

| Pattern in Orientera | Keep? | Where it goes |
|---|---|---|
| Facts computed deterministically; the model only phrases them ("facts in, prose out") | Yes, as guidance | Wiki pattern, not code. It is why its features are cheap and correct. |
| Requested lazily when a tab opens | Yes | App side; `TaskState` already does it. |
| Single-flight TTL cache keyed on a fingerprint of the input, failures evicted | Yes, but distributed | `HybridCache` (§5.5). Orientera's cache is in memory per instance and is lost on scale-out. |
| Feature off when its key is missing (`IsConfigured`); null → 404 → app treats as off | Yes, as the contract | §3.3: the same answer for "no key", "disabled" and "hard budget spent". |
| Queue → worker → blob under `v{Version}/` with `?v={lastModified}`; ordering deduplicated; a failed quality check throws so the queue retries | Yes | §6, nearly as is. |
| Image-edit wrapper that retries without a parameter the model rejects | Partly | A provider quirk. Belongs in the app's provider line or in M.E.AI, not in Spine. |
| Provider switch requires a code change | Fix | §3 |
| No metering | Fix | §4 |

---

## 3. Named features and provider plug-and-play (#510)

### 3.1 Configuration

```json
"Ai": {
  "Providers": {
    "anthropic": { "ApiKey": "<secret>" },
    "openai":    { "ApiKey": "<secret>" }
  },
  "Features": {
    "RaceStory":  { "Provider": "anthropic", "Model": "<model id>", "MaxOutputTokens": 800,
                    "Budget": { "DaySoft": 2, "DayHard": 5, "UserDayHard": 0.05 } },
    "ArenaImage": { "Kind": "Image", "Provider": "openai", "Model": "<image model id>",
                    "Image": { "Size": "1536x1024", "Version": 3 } },
    "Assistant":  { "Provider": "openai", "Model": "<model id>", "Expose": true,
                    "SystemPrompt": "…", "MaxInputTokens": 4000, "RequestsPerMinute": 6 }
  },
  "Prices": {
    "anthropic/<model id>":    { "InputPerMillion": 3.00, "CachedInputPerMillion": 0.30, "OutputPerMillion": 15.00 },
    "openai/<image model id>": { "PerImage": 0.06 }
  },
  "Currency": "USD"
}
```

The figures are examples, not current prices. Swapping RaceStory to another vendor is a change to `Provider` and `Model` (and a price row), nothing else. Features are re-read through `IOptionsMonitor`, so a model change in App Configuration takes effect without a deploy; the pipeline for a feature is rebuilt when its section changes.

### 3.2 Providers: one line each

```csharp
builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
{
    ai.Chat("anthropic", (p, model) => new AnthropicClient { ApiKey = p["ApiKey"] }.AsIChatClient(model));
    ai.Chat("openai", (p, model) => new OpenAIClient(p["ApiKey"]).GetChatClient(model).AsIChatClient());
    ai.Image("openai", (p, model) => new OpenAIClient(p["ApiKey"]).GetImageClient(model).AsIImageGenerator());
    ai.UseAzureTableUsageStore(builder.Configuration["Storage"]);
});
```

`p` is the provider's configuration section, so keys, endpoints (Azure OpenAI, OpenRouter, a local Ollama) and managed identity stay the app's business. The app references the vendor packages; `Server.AI` references none of them. A provider whose required key is empty is "not configured" and every feature on it is off, logged once at startup with the feature and the missing key, so the failure is visible rather than a silent 404.

### 3.3 Resolution and "off"

For each feature Spine registers a keyed client (`[FromKeyedServices("RaceStory")] IChatClient`), built as:

```
UseDistributedCache (if Cache.Ttl)  →  Spine budget gate  →  Spine meter  →  UseOpenTelemetry  →  UseLogging  →  vendor adapter
```

A cache hit never reaches the gate or the meter, so cached answers are free and still served when the budget is spent. OpenTelemetry sits next to the provider so its spans are real provider calls.

App code on the server asks `IAIFeatures`, which carries the caller:

```csharp
var story = ai.Chat("RaceStory", caller);          // null when off: no key, disabled, or hard budget spent
if (story is null) return Results.NotFound();       // the answer Orientera's app already treats as "off"
```

A hard budget that runs out between the check and the call surfaces as `AIFeatureOffException(feature, AIOffReason.Budget)`, which `MapSpineAI` turns into the same 404 with `{ "off": "budget" }` in the body.

### 3.4 Exposed features are not an open proxy

Most features are called by the app's own endpoints, which build the prompt from facts ("facts in, prose out"). A feature with `Expose: true` is reachable directly by the client (`POST /ai/features/{name}`), and then the server owns the prompt: client system and tool messages are dropped, `SystemPrompt` is prepended, `MaxInputTokens` (estimated) and `MaxTurns` are enforced, and client `ChatOptions` are ignored except `ConversationId`. Without these rules an exposed feature is a free API key for anyone who reads the app's traffic.

---

## 4. Usage metering and cost (#510)

**What is counted.** The meter is a `DelegatingChatClient` (and a `DelegatingImageGenerator`). Non-streaming: `ChatResponse.Usage`. Streaming: the sum of `UsageContent` across updates, recorded when the enumeration ends, including when the caller stops early or disconnects. A stream that ends without usage (some providers send it only at the end) is recorded with the estimated input tokens and `Complete = false`, so the report shows it instead of losing it. Images: `ImageGenerationResponse.Usage` when the provider sends it, and always the image count and size.

**The record:**

```csharp
public sealed record AIUsageRecord(
    DateTimeOffset At, string Feature, string Provider, string Model, string Caller,
    long InputTokens, long CachedInputTokens, long OutputTokens, long ReasoningTokens,
    int Images, decimal? Cost, bool Complete, TimeSpan Duration, string? Error);
```

**Cost** = tokens × the `Ai:Prices` row for `provider/model`, in `Currency`. A missing price row records `Cost = null` and logs a warning once per model; the report lists the model as unpriced, and budgets count unpriced calls by count only. Prices are never in Spine's code: they change more often than Spine is released. Cache-write tokens (Anthropic charges them separately) arrive in `AdditionalCounts` if at all; whether the adapter fills them has not been checked, so `AdditionalCounts` keys can be priced by name in the price row.

**`IAIUsageStore`**, like `IPushInstallationStore`: `RecordAsync`, `GetTotalsAsync(scope, period)` and `QueryAsync(from, to, feature?)`. The Azure Table store keeps the raw records in one table (partition `yyyy-MM-dd`) and running totals in another (partition = period such as `d:2026-10-09` or `m:2026-10`, row = `feature|caller` and `feature|*`), incremented with ETag retries. A budget check is then two or four point reads. In-memory for tests and samples.

**Report endpoint.** `GET /ai/usage?from=&to=&feature=` returns totals per feature, model and day, the top callers and the unpriced models. It answers 401 unless `AuthorizeReport` is set; there is no anonymous default.

---

## 5. Budgets, rate limits, caching and observability (#510)

### 5.1 Budgets

Per feature: `DaySoft`, `DayHard`, `MonthSoft`, `MonthHard` in currency, and per caller `UserDayHard`, `UserMonthHard`, `UserDayCalls`. An app-wide `Ai:Budget` caps the sum. Before a call the gate reads the totals; at or over a hard limit the feature is off for that scope (for everyone at the feature level, for that caller at the user level). Passing a soft limit raises `OnBudgetThreshold` once per scope and period and adds `Spine-AI-Budget: soft` to the response so the app can say "nearly used up for today".

The cost of a call is known only afterwards, so the gate cannot be exact. It allows a call while spent < limit, which overshoots by at most one call; `MaxOutputTokens` bounds that call. A reservation scheme (reserve worst case, settle after) is what spine-voice.md §6.2 needs for realtime sessions, where usage may never be seen; for chat and images it costs a write per call and is not proposed for v1.

### 5.2 Who the caller is

`AICaller(string Id, AICallerKind Kind)`, resolved per request by `Identify`, defaulting to:
1. `HttpContext.User`'s `sub` when authenticated (`Plugin.Maui.Spine.Server.Authentication`, #520, or any JwtBearer setup);
2. the `Spine-Installation` header: the push installation id when the app has `Spine.PushNotifications`, otherwise a GUID the `Spine.AI` client keeps in `SecureStorage` the same way (`PushNotificationService.cs:96-104`);
3. otherwise `anonymous`.

Kinds 2 and 3 are claims, not identity: a client that rotates its id gets a fresh per-user allowance. Per-user limits for anonymous callers are therefore a courtesy; the feature and app budgets are what protect the bill, and a per-IP rate limit is added for anonymous callers. `RequireSignedIn()` per feature turns kinds 2 and 3 away.

### 5.3 Rate limits

`RequestsPerMinute` and `ConcurrentPerCaller` per feature, implemented with `System.Threading.RateLimiting` partitioned by caller id inside Spine's handler, not with `UseRateLimiter()`. The handler runs the same way under ASP.NET Core routing and under an Azure Functions `[Function]`, where ASP.NET Core's endpoint middleware does not run (the push endpoints are built for the same reason). A rejected call is 429 with `Retry-After`. The limiter is per instance; a distributed limiter (Redis) is out of scope, and the budgets in the store are the cross-instance backstop. ASP.NET Core apps can still add `.RequireRateLimiting(...)` on top.

### 5.4 Threshold alert to the owner

`Server.AI` does not reference `Plugin.Maui.Spine.Server`, so an app with AI and no push pulls in no Firebase. The alert is a callback, and push is one line in the app:

```csharp
ai.OnBudgetThreshold = (alert, sp, ct) => sp.GetRequiredService<IPushSender>().SendAsync(
    PushTarget.User(ownerId), new PushNotification { Title = "AI budget", Body = alert.ToString() }, ct);
```

### 5.5 Caching

Two different caches, both on standard parts:
- **Response cache per feature** (`Cache: { Ttl }`): M.E.AI's `UseDistributedCache`, keyed on the messages and options, over whatever `IDistributedCache` the host registers (Redis, or memory in development).
- **Result cache in app code**, Orientera's single-flight fingerprint cache: `HybridCache.GetOrCreateAsync(fingerprint, …)`. It runs the factory once per key across concurrent callers, does not store exceptions (failures are evicted, as today), and with an L2 survives scale-out. Spine adds no wrapper; the wiki shows the pattern.

### 5.6 Observability

`UseOpenTelemetry` emits the GenAI semantic-convention spans and token metrics; Spine adds the feature name and caller kind (never the caller id) as tags. Prompt and response content are not recorded unless the app turns `EnableSensitiveData` on. Aspire's dashboard shows them locally.

---

## 6. Generated-asset pipeline (#511)

Images are slow (tens of seconds) and expensive, so they are made once, stored, and served from storage. Orientera's arena images, generalized:

```
app: GET /ai/assets/{feature}/{key}
  ├─ blob v{Version}/{key}.png exists → 200 { url: "…/v3/{key}.png?v={lastModified ticks}" }
  └─ missing → order (deduplicated) → 202 { status: "ordered" }   (app shows a placeholder, asks again later)
worker: dequeue → IImageGenerator for {feature} (metered, budgeted) → optional IGeneratedAssetCheck
        → upload v{Version}/{key}.png → done.  A check that fails throws → the queue retries → poison after N.
```

- **Dedup.** The order inserts a row `feature|version|key` in a table and only enqueues when the insert succeeds, so a burst of requests orders once. A poisoned order is marked failed and retried only after a cool-down, so a prompt that always fails does not burn the budget.
- **Version.** `Image.Version` in the feature's configuration; bumping it makes every key missing, so assets regenerate lazily as they are asked for. Old versions stay in the blob container until deleted.
- **Cache-buster.** `?v=` from the blob's `LastModified`, so a regenerated asset under the same path is fetched anew by the app and by CDNs. `Plugin.Maui.Spine.Images` caches by URL, so this matters there too.
- **Prompt.** `IGeneratedAssetPrompt<TKey>` in the app builds the `ImageGenerationRequest` (with original images for edits) from the key. Spine never sees domain data.
- **Hosts.** ASP.NET Core: a `BackgroundService` that polls the queue. Functions: a `[QueueTrigger]` calls `GeneratedAssetWorker.HandleAsync(message, ct)`.
- **Abstractions.** `IGeneratedAssetStore` (look up, put, URL) and `IAssetOrderQueue`, with Azure Blob and Azure Queue implementations and in-memory ones for tests.

Almanacka's pictures could use the same pipeline if they ever come from a model instead of being drawn on the device.

---

## 7. Spine.AI client (#512)

`Plugin.Maui.Spine.AI` depends on `.Common`, `Microsoft.Extensions.AI.Abstractions` and `System.Net.ServerSentEvents` (a dependency of the Anthropic SDK too, so it is a well-trodden package). It does not reference `Microsoft.Maui.Essentials.AI`: that package is preview, and its newest build targets `net11.0` while Spine is on `net10.0`. The app references it and hands Spine the client.

```csharp
builder.UseSpineAI(o =>
{
    o.Backend = new Uri("https://api.example.com/ai");
    o.Feature("Assistant", f => f
        .OnDevice(sp => new AppleIntelligenceChatClient())   // the app's own reference; skipped where unavailable
        .ThenServer());                                       // POST {Backend}/features/Assistant, SSE
    o.Feature("RaceStory", f => f.Server());
});

public sealed partial class AssistantViewModel([FromKeyedServices("Assistant")] IChatClient chat) : ViewModelBase { … }
```

- **Server client.** `GetStreamingResponseAsync` posts the messages as M.E.AI JSON and reads `text/event-stream` of `ChatResponseUpdate` (.NET 10's `TypedResults.ServerSentEvents` on the server, `SseParser` on the client). The header comes from `AuthorizationHeader`, or from `ISignedInSession` in `.Common` when Spine.Authentication is referenced (spine-authentication.md §5.6), plus `Spine-Installation`. No provider key is ever in the app.
- **Answers in one shape.** 404 → `AIFeatureOffException` with the reason (`NotConfigured`, `Budget`); 429 → `AIRateLimitedException` with `RetryAfter`; network failure → the usual `HttpRequestException`. `TaskState` shows the first as its empty state.
- **Fallback chain.** Try each source in order; move on when it is unavailable or throws before the first update. After the first token there is no fallback: the user has already seen text. Availability on device: the chain asks the client through `GetService(typeof(ChatClientMetadata))` and an optional `Func<bool>`; how `AppleIntelligenceChatClient` reports "model not ready" or "Apple Intelligence off" has not been checked and decides how clean this is.
- **On-device usage.** The chain wraps on-device clients in the same meter, writing totals per feature and day to `Preferences`, readable through `IAIUsage`. They are not sent to the server in v1.

---

## 8. Spine.Controls.Chat (#513)

`Plugin.Maui.Spine.Controls.Chat` depends on `Plugin.Maui.Spine`, `.Controls.Shimmer`, `Microsoft.Extensions.AI.Abstractions` and `Markdig`. It works with any `IChatClient`, not only Spine.AI's.

```xml
<chat:ChatView Client="{Binding Chat}" Messages="{Binding Messages}"
               Suggestions="{Binding Suggestions}" Placeholder="Ask about the race" />
```

- **List.** A `CollectionView` with `ItemsUpdatingScrollMode=KeepLastItemInView` while the user is at the bottom; scrolling up stops the follow and shows a "jump to latest" button. Whether that mode behaves on iOS during rapid item height changes must be tried first; the fallback is `ScrollTo(last)` throttled to the render tick.
- **Streaming.** Updates are appended to the last message's text; the view re-renders at most every 50 ms and only the last block (below).
- **Markdown.** Markdig parses into blocks and inlines. Paragraphs and headings become a `Label` with a `FormattedString` (a `Span` per run: bold and italic as `FontAttributes`, inline code as a monospace `Span` with a background, links as a `Span` with a `TapGestureRecognizer`); lists a `Grid` of marker and label; code blocks a `Border` with a horizontal `ScrollView`; quotes a `Border` with a leading rule; tables a `Grid`. HTML blocks are shown as text. Streaming re-parses the whole message (chat messages are short, and Markdig is fast), keeps the views of blocks whose source text is unchanged and rebuilds only from the first changed block, which is nearly always the last.
- **Composer.** An `Editor` that grows to five lines, a send button that becomes stop (cancels the token) while streaming, and suggestion chips above it. It sits in the page footer, so Spine's keyboard avoidance (PR #458) lifts it; on a page that handles the keyboard itself, `KeyboardInset` is there.
- **Thinking.** Before the first token the assistant bubble is a `Skeleton` line under `Shimmer`. `ThinkingTemplate` lets the app use Highlight's `Edge` (#509) instead, without Chat referencing Highlight.
- **Tool calls.** `FunctionCallContent` and `FunctionResultContent` render through `ToolCallTemplate`, a compact card by default.
- **Accessibility.** The finished message is announced once (`SemanticScreenReader.Announce`), not per token; Reduce Motion stops the shimmer (Shimmer already does).

---

## 9. On-device spike (#514, outside Spine)

A proof of concept in its own repository, so the client's fallback chain knows what it can rely on and the result can go upstream to dotnet/maui-labs.

| | Android | Windows |
|---|---|---|
| API | ML Kit GenAI Prompt API. Microsoft's binding `Xamarin.Google.MLKit.GenAI.Prompt` 1.0.0.4-alpha1 (dotnet/android-libraries) tracks Maven `1.0.0-alpha1`; Google's Maven has `1.0.0-beta4` (verified in `maven-metadata.xml`). | `Microsoft.Windows.AI.Text.LanguageModel` (Phi Silica today), behind a Limited Access Feature token, on Copilot+ PCs with an NPU. |
| Limits | Foreground only (`BACKGROUND_USE_BLOCKED`), under about 4,000 input tokens, `BUSY` and `PER_APP_BATTERY_USE_QUOTA_EXCEEDED`, `checkStatus` and a model download. Devices: Pixel 9 and 10, Galaxy S26 (nano-v3). No tool calling. A Java Futures API exists alongside the Kotlin one. | Phi Silica is replaced by Aion Instruct: test package October 2026, Insiders November 2026, retail January 2027. The client is therefore named after the API, not the model. |
| Starting points | mattleibow/Maui.Essentials.AI | AI Dev Gallery's `PhiSilicaClient : IChatClient` |

praeclarum/CrossIntelligence (MIT, 0.6.59, verified on nuget.org: iOS, Mac Catalyst, macOS) covers Apple Intelligence plus OpenAI and OpenRouter, but is not an `IChatClient`, has no streaming in its public API and no Android or Windows, so it is a reference and not a dependency.

The spike reports: streaming (token by token or chunks), tool calling (none native; prompt-based at best), availability states and how to map them to "skip this source", model download size and flow, and first-token latency on one real device per platform.

---

## 10. Proposed API surface

### 10.1 Server (`Plugin.Maui.Spine.Server.AI`)

Type names follow M.E.AI's casing (`AIContent`, `AIFunction`) and spine-voice.md's `AddSpineAI`: `IAIUsageStore`, not `IAiUsageStore`. The configuration section stays `Ai`.

```csharp
public static class SpineAIServiceCollectionExtensions
{
    public static IServiceCollection AddSpineAI(this IServiceCollection services, IConfiguration section, Action<SpineAIOptions>? configure = null);
}

public sealed class SpineAIOptions
{
    public SpineAIOptions Chat(string provider, Func<IConfiguration, string, IChatClient> factory);
    public SpineAIOptions Image(string provider, Func<IConfiguration, string, IImageGenerator> factory);
    public SpineAIOptions UseInMemoryUsageStore();
    public SpineAIOptions UseAzureTableUsageStore(string connectionString, string prefix = "SpineAI");
    public SpineAIOptions UseUsageStore(Func<IServiceProvider, IAIUsageStore> factory);
    public Func<HttpRequest, ValueTask<AICaller>>? Identify { get; set; }
    public Func<HttpRequest, ValueTask<bool>>? AuthorizeReport { get; set; }
    public Func<AIBudgetAlert, IServiceProvider, CancellationToken, Task>? OnBudgetThreshold { get; set; }
}

public interface IAIFeatures
{
    /// <summary>The feature's client bound to <paramref name="caller"/>, or <see langword="null"/> when the feature is off.</summary>
    IChatClient? Chat(string feature, AICaller caller);
    IImageGenerator? Image(string feature, AICaller caller);
    AIFeatureState StateOf(string feature);   // On, NotConfigured, Disabled, BudgetSpent
}

public interface IAIUsageStore
{
    Task RecordAsync(AIUsageRecord record, CancellationToken cancellationToken = default);
    Task<AIUsageTotals> GetTotalsAsync(AIUsageScope scope, AIPeriod period, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AIUsageRecord> QueryAsync(DateTimeOffset from, DateTimeOffset to, string? feature = null, CancellationToken cancellationToken = default);
}

public static class SpineAIEndpoints   // both hosts, like SpinePushNotificationsEndpoints
{
    public static Task<IResult> FeatureAsync(HttpRequest request, string feature, CancellationToken cancellationToken = default);
    public static Task<IResult> AssetAsync(HttpRequest request, string feature, string key, CancellationToken cancellationToken = default);
    public static Task<IResult> UsageAsync(HttpRequest request, CancellationToken cancellationToken = default);
}

app.MapSpineAI("/ai");   // POST /ai/features/{name}, GET /ai/assets/{feature}/{key}, GET /ai/usage
```

Dependencies: `.Common`, `Microsoft.Extensions.AI` (brings `.Abstractions`), `Microsoft.Extensions.Caching.Hybrid`, `Azure.Data.Tables` (already in `.Server`), `Azure.Storage.Blobs` and `Azure.Storage.Queues` for §6, and the ASP.NET Core framework reference. No vendor SDK, no reference to `Plugin.Maui.Spine.Server`.

### 10.2 Client (`Plugin.Maui.Spine.AI`) and view (`Plugin.Maui.Spine.Controls.Chat`)

```csharp
public sealed class SpineAIOptions
{
    public Uri? Backend { get; set; }
    public Func<CancellationToken, Task<string?>>? AuthorizationHeader { get; set; }
    public SpineAIOptions Feature(string name, Action<AIFeatureChain> configure);
}

public sealed class AIFeatureChain
{
    public AIFeatureChain OnDevice(Func<IServiceProvider, IChatClient> factory, Func<bool>? isAvailable = null);
    public AIFeatureChain Server();
    public AIFeatureChain ThenServer();
}

public sealed class AIFeatureOffException(string feature, AIOffReason reason) : Exception;
public sealed class AIRateLimitedException(string feature, TimeSpan? retryAfter) : Exception;

public class ChatView : ContentView
{
    public static readonly BindableProperty ClientProperty;          // IChatClient
    public static readonly BindableProperty MessagesProperty;        // IList<ChatMessage>
    public static readonly BindableProperty SuggestionsProperty;     // IEnumerable<string>
    public static readonly BindableProperty ThinkingTemplateProperty;
    public static readonly BindableProperty ToolCallTemplateProperty;
    public static readonly BindableProperty ChatOptionsProperty;
    public Task SendAsync(string text, CancellationToken cancellationToken = default);
}

public class MarkdownView : ContentView { public static readonly BindableProperty TextProperty; }
```

Both client packages are `<SpineModule>`s and register with `UseSpine()`.

---

## 11. Implementation steps

1. **Server core (#510):** options binding, provider registry, keyed pipelines rebuilt on configuration change, `IAIFeatures`, the off states and their startup log. Tests with a fake `IChatClient` in `tests/Plugin.Maui.Spine.Server.Tests` or a new `Server.AI.Tests`.
2. **Meter and store:** streaming and non-streaming usage, prices, unpriced handling, in-memory and Azure Table stores (Azurite), the report endpoint.
3. **Gate and limits:** budgets with soft and hard levels, caller identification, the in-handler rate limiter, `OnBudgetThreshold`, the response header.
4. **Endpoints:** `MapSpineAI`, `SpineAIEndpoints` for Functions, SSE streaming, exposed-feature rules. Try streaming under the Functions isolated ASP.NET Core integration on Azure, not only locally.
5. **Orientera moves:** release, bump `SpineVersion`, replace the two direct SDK calls with features, its cache with `HybridCache`, and turn on per-user limits. This is the real test of the API.
6. **Asset pipeline (#511):** store, queue, worker for both hosts; move Orientera's arena images onto it.
7. **Client (#512):** server client, exceptions, fallback chain, on-device meter; a page in the Showcase sample against the push sample server with a fake provider so it runs without keys.
8. **Chat view (#513):** `MarkdownView` first (testable alone), then `ChatView`; Showcase page; wiki and `spine-controls` skill.
9. **Spike (#514):** in parallel, outside the repo; its report decides whether the client's chain needs an availability contract beyond `Func<bool>`.
10. Wiki pages `ai-server.md`, `ai-client.md`, `chat.md`; rows in `docs/wiki/packages.md`.

---

## 12. Open questions for the owner

1. **Default for exposed features:** sign-in required (as voice proposes), or installation id allowed with tighter limits? Orientera has no sign-in today.
2. **Azure Storage in `Server.AI`** or a separate `Plugin.Maui.Spine.Server.AI.Azure` for Tables, Blobs and Queues? The push package keeps Tables inside; Blobs and Queues are new weight for apps that only want chat.
3. **`MarkdownView` in `Controls.Chat`** or its own `Plugin.Maui.Spine.Controls.Markdown` package, usable for help and release notes without chat?
4. **Currency:** one per app (proposal), or per price row with conversion left to the report reader?
5. **On-device usage:** keep it on the device (proposal), or report it to the server so one report covers both?
6. **Essentials.AI on `net10.0`:** the maui-labs build that targets `net10.0` is from July; if new builds are `net11.0` only, should Spine's samples wait for .NET 11 or pin the July preview?
7. **Budget overshoot:** is "at most one call over" acceptable for chat and images, or should every call reserve its worst case as voice sessions must?

---

## 13. Sources

Read in the repo: `src/Plugin.Maui.Spine.Server/*` (extensions, endpoints, options, stores, `IPushSender`, `PushTarget`, csproj), `src/Plugin.Maui.Spine.Common/Push/PushInstallation.cs`, `src/Plugin.Maui.Spine.PushNotifications/SpinePushNotificationsOptions.cs`, `Services/PushNotificationService.cs`, `Services/PushRegistrationClient.cs`, `src/Plugin.Maui.Spine/Core/{SoftKeyboard,NavigableAttribute,SpineOptions,ViewModelBase,TaskState}.cs`, `Presentation/StateView.cs`, `Extensions/Haptics.cs`, `src/Plugin.Maui.Spine.Controls.Shimmer/*`, `docs/wiki/packages.md`, `docs/wiki/push-notifications-server.md`, `docs/proposals/spine-voice.md`, `spine-authentication.md`, `spine-highlight.md`.

- Microsoft.Extensions.AI overview: https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai
- dotnet/extensions, `IImageGenerator.cs` (experimental) and `DiagnosticIds.cs` (MEAI001): https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/Image, https://github.com/dotnet/extensions/blob/main/src/Shared/DiagnosticIds/DiagnosticIds.cs
- dotnet/extensions, `UsageDetails.cs`: https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/UsageDetails.cs
- NuGet: https://www.nuget.org/packages/Microsoft.Extensions.AI, https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI, https://www.nuget.org/packages/Anthropic, https://www.nuget.org/packages/OpenAI, https://www.nuget.org/packages/Google.GenAI, https://www.nuget.org/packages/Microsoft.Extensions.Caching.Hybrid, https://www.nuget.org/packages/Markdig, https://www.nuget.org/packages/Microsoft.Maui.Essentials.AI, https://www.nuget.org/packages/Xamarin.Google.MLKit.GenAI.Prompt, https://www.nuget.org/packages/CrossIntelligence
- Anthropic C# SDK: https://github.com/anthropics/anthropic-sdk-csharp
- HybridCache: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid
- ASP.NET Core rate limiting: https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit
- Markdig: https://github.com/xoofx/markdig
- Microsoft.Maui.Essentials.AI in maui-labs: https://github.com/dotnet/maui-labs/tree/main/src/AI/Microsoft.Maui.Essentials.AI
- .NET MAUI AI docs: https://learn.microsoft.com/en-us/dotnet/maui/ai/?view=net-maui-10.0
- Android and Windows on-device tracking issue: https://github.com/dotnet/maui/issues/32539
- praeclarum/CrossIntelligence: https://github.com/praeclarum/CrossIntelligence
- mattleibow/Maui.Essentials.AI: https://github.com/mattleibow/Maui.Essentials.AI
- AI Dev Gallery, `PhiSilicaClient`: https://github.com/microsoft/ai-dev-gallery/blob/main/AIDevGallery/Samples/SharedCode/IChatClient/PhiSilicaClient.cs
- ML Kit GenAI Prompt API: https://developers.google.com/ml-kit/genai/prompt/android/get-started
- Google Maven, `genai-prompt` versions: https://dl.google.com/android/maven2/com/google/mlkit/genai-prompt/maven-metadata.xml
- Windows AI, Phi Silica: https://learn.microsoft.com/en-us/windows/ai/apis/phi-silica
