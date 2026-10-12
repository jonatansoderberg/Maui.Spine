# Issue #510 — Spine.Server.AI: named AI features, provider plug-and-play, usage metering, cost and budgets

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/510
**Branch:** issue/510-spineserverai-named-ai-features-provider-plug-and
**Status:** In Progress

This is step 2.1 of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505), tracked in `docs/plans/ai-and-security.md`. The design is in `docs/proposals/spine-ai.md` §3–§5 and §10.1, together with the owner's answers Q19, Q20, Q22 and Q25. The owner chose to keep the step in one issue and one PR rather than split it (2026-10-12).

## Plan

### 0. Package and dependencies

- **`src/Plugin.Maui.Spine.Server.AI`:** `net10.0`, with a `FrameworkReference` to `Microsoft.AspNetCore.App` (the same as `.Server`, so it serves both ASP.NET Core and the Functions isolated worker) and a `ProjectReference` to `.Common`.
  - It does not reference `Plugin.Maui.Spine.Server`, so push is not pulled in.
  - It has no `<SpineModule>`, because it is a server package.
  - `InternalsVisibleTo` the tests.
- **New `PackageVersion`s:**
  - `Microsoft.Extensions.AI` 10.10.0, which brings `.Abstractions` 10.10.x
  - `Microsoft.Extensions.Caching.Hybrid` 10.10.0, only if the package needs it. §5.5 says Spine adds no wrapper, so the wiki may be enough.
  - `Azure.Data.Tables`, which is already pinned.
  - Blobs and Queues come with #511, not here.
- **`MEAI001`** (`IImageGenerator` is experimental) is suppressed inside the package with `<NoWarn>`.
- **Tests:** a new `tests/Plugin.Maui.Spine.Server.AI.Tests` (xunit, `FakeTimeProvider`, and Azurite facts like `AzureTablePushInstallationStoreTests`).
- **Repository files:** `Spine.slnx` and `Spine.Packages.slnf`, plus an icon in `assets/icons/server-ai.png` from `assets/logo-src/server-ai.png`. The icon is the Server original with a sparkle in the corner, like Highlight, and its row goes in the logo-src README.

### 1. Configuration and features (§3.1, §3.3)

- **`SpineAIConfiguration`** is bound from the section passed to `AddSpineAI` and re-read through `IOptionsMonitor`.
  - `Providers`: a name → its raw `IConfigurationSection`.
  - `Features`: a name → `AIFeatureOptions`, which holds:
    - `Kind`: Chat or Image
    - `Provider` and `Model`
    - `Enabled`, `MaxOutputTokens`, `Temperature` and `Reasoning` (`ReasoningOptions` from M.E.AI, which Orientera's `Effort.Low` maps onto)
    - `Image` (size, count)
    - `Expose`, `SystemPrompt`, `MaxInputTokens` and `MaxTurns`
    - `Callers`, `Budget`, `RequestsPerMinute`, `ConcurrentPerCaller` and `Cache.Ttl`
  - `Prices`: `provider/model` → the row.
  - `Currency`, the app-wide `Budget` and an optional `TimeZone`.
- **`SpineAIOptions`** (the code half):
  - `Chat(provider, factory)` and `Image(provider, factory)`
  - the store choice: `UseInMemoryUsageStore`, `UseAzureTableUsageStore` and `UseUsageStore`
  - the callbacks `Identify`, `AuthorizeReport` and `OnBudgetThreshold`
- **`AIFeatureRegistry`** (internal) builds and caches the shared inner pipeline per feature. A chat feature gets `UseOpenTelemetry` → `UseLogging` → the vendor adapter, and an image feature the image equivalents.
  - The pipeline is rebuilt when the feature's section, or its provider's section, changes. The old client is disposed once nothing is using it.
  - The feature's defaults (`MaxOutputTokens`, `Reasoning`, `ModelId` and so on) are applied with `ConfigureOptions`. A call's own values win for app-owned features. Exposed features ignore the client's values (§3.4).
- **The keyed services:** `[FromKeyedServices("RaceStory")] IChatClient` resolves to the feature's client with the caller `system`, meaning server-side work with no user. It is null when the feature is off.
- **`IAIFeatures`:**
  - `Chat(feature, caller)` and `Image(feature, caller)` return a client bound to the caller, or `null` when the feature is off.
  - `StateOf(feature)` returns `On`, `NotConfigured`, `Disabled` or `BudgetSpent`.
  - The bound client is built per call: `UseDistributedCache` (when `Cache.Ttl` is set and an `IDistributedCache` is registered) → the budget gate and rate limiter (§3) → the meter (§2) → the shared inner pipeline.
  - The caller lives in the per-call objects, not in `ChatOptions.AdditionalProperties`. Otherwise it would enter the cache key, and two callers asking the same thing would not share an answer.
- **The startup log:** once per feature that is off, with the reason. For example "RaceStory is off: provider 'anthropic' has no ApiKey", or "…no factory registered for provider 'x'". A feature that names an unknown provider in configuration is logged, not thrown, so that a configuration change cannot take the server down.

### 2. Meter, prices and stores (§4)

- **`MeteringChatClient` and `MeteringImageGenerator`** (internal, `DelegatingChatClient` and `DelegatingImageGenerator`):
  - **Non-streaming calls:** `ChatResponse.Usage`.
  - **Streaming calls:** the sum of the `UsageContent` across the updates. The total is recorded in a `finally` when the enumeration ends, also when the caller stops early or the request is cancelled. The write then uses `CancellationToken.None`, because the cost has been paid.
  - **No usage seen:** the call is recorded with the estimated input tokens (characters / 4) and `Complete = false`.
  - **Errors:** recorded with `Error` set and whatever usage was seen.
- **`AIPrices`:** cost = tokens × the row's price. A row has `InputPerMillion`, `CachedInputPerMillion`, `OutputPerMillion`, `ReasoningPerMillion` (reasoning tokens are billed as output when the row has no price of its own), `PerImage` and `Additional` (an `AdditionalCounts` key → its price per million).
  - A missing row gives `Cost = null` and one warning per model.
  - Cached input is subtracted from input before pricing, if the provider counts it inside `InputTokenCount`. That is how M.E.AI documents it; the tests pin it.
- **`AIUsageRecord`** follows §4 as written.
- **`IAIUsageStore`:**
  - `RecordAsync`
  - `GetTotalsAsync(scope, period)`, where the scope is a feature, a feature and caller, or the app
  - `QueryAsync(from, to, feature?)`
  - `TryMarkAlertAsync(scope, period, level)`, so that `OnBudgetThreshold` fires once per scope and period across instances
- **The two stores:**
  - `InMemoryAIUsageStore`.
  - `AzureTableAIUsageStore`, with two tables. `{prefix}Usage` holds the raw records (partition `yyyy-MM-dd`, row = reversed ticks + a GUID, so that a query reads in order). `{prefix}Totals` holds the running totals: partitions `d:2026-10-12` and `m:2026-10`, rows `feature|caller`, `feature|*` and `*|*`. Totals are incremented with ETag retries.
  - **The period's day** is in `TimeZone`, UTC by default.
- **`GET {prefix}/usage?from=&to=&feature=`** returns totals per feature, model and day, the top callers (ten by cost) and the unpriced models.
  - It answers 401 unless `AuthorizeReport` says yes.
  - It reads the raw records, which is fine for the volumes of these apps. The totals table answers the budget checks.

### 3. Gate, callers and rate limits (§5)

- **`AICaller(Id, Kind)`** has the kinds `SignedIn`, `Installation`, `Anonymous` and `System`. `Identify` defaults to:
  1. `HttpContext.User`'s `sub`, or `NameIdentifier`, when the user is authenticated
  2. the `Spine-Installation` header
  3. `Anonymous`, keyed by the remote IP address
- **Budgets:**
  - Per feature: `DaySoft`, `DayHard`, `MonthSoft` and `MonthHard`.
  - Per caller: `DayHard`, `MonthHard` and `DayCalls`, with one set for signed-in users and a tighter one for installations and anonymous callers (Q19). The shape is open question 1.
  - App-wide: `Ai:Budget`, with the same four levels.
  - Unpriced calls count toward `DayCalls` only.
- **The gate:**
  - Before a call it reads the totals: the feature's day and month, the caller's day and month, and the app's day and month. These are point reads in the Table store.
  - At or over a hard limit, the call throws `AIFeatureOffException(feature, AIOffReason.Budget)`. When the feature's own limit is the one spent, `StateOf` says `BudgetSpent`.
  - Past a soft limit, `OnBudgetThreshold` fires once per scope and period through `TryMarkAlertAsync`. `Spine-AI-Budget: soft` is set on the current response when there is an `HttpContext`; the package registers `AddHttpContextAccessor`.
  - A cache hit never reaches the gate (§3.3).
- **Rate limits:**
  - `RequestsPerMinute` is a `SlidingWindowRateLimiter` and `ConcurrentPerCaller` a `ConcurrencyLimiter`. Both are `PartitionedRateLimiter`s keyed by caller id, and they live in the gate, so they cover both the exposed endpoint and the app's own endpoints.
  - A streaming call holds its concurrency lease until the enumeration ends.
  - A rejected call throws `AIRateLimitedException` with `RetryAfter`.
  - Anonymous callers are also limited per IP address, through the same partition: their id is the IP.
- **`AIFeatureException`** is the base class of both exceptions. Its `ToResult()` gives the same answer `MapSpineAI` gives:
  - 404 with `{ "off": "budget" | "disabled" | "notConfigured" }`, the "off" answer Orientera's app already handles
  - 429 with `Retry-After`
  - 403 `{ "off": "signInRequired" }` for `Callers: SignedIn`

  An app endpoint can then write `catch (AIFeatureException e) { return e.ToResult(); }`.

### 4. Endpoints (§3.4, §10.1)

- **`app.MapSpineAI("/ai")`** maps two routes:
  - `POST /ai/features/{name}`, only for features with `Expose: true`. Any other name gets the same 404 as "off", so the route does not reveal which features exist.
  - `GET /ai/usage`
- **`SpineAIEndpoints.FeatureAsync` and `UsageAsync`** are callable from a Functions `[Function]`, like `SpinePushNotificationsEndpoints`. `AssetAsync` comes with #511.
- **The wire format** is M.E.AI's own JSON (`AIJsonUtilities.DefaultOptions`), so that the `Spine.AI` client (#512) can deserialise `ChatMessage`, `ChatResponse` and `ChatResponseUpdate` without types of its own.
  - **Request:** `{ "messages": [...], "conversationId": "…" }`.
  - **Response:** with `Accept: text/event-stream` it is SSE, one `data:` line per `ChatResponseUpdate` and a final `event: done`, flushed per update. Otherwise it is a `ChatResponse`.
  - An error before the first byte gets a status code. One after the first byte becomes `event: error`.
- **Rules for exposed features:**
  - The client's `system` and `tool` messages are dropped, and `SystemPrompt` is put first.
  - `MaxTurns` counts the user messages. `MaxInputTokens` is estimated with characters / 4, and over it the answer is 413.
  - The client's `ChatOptions` are ignored except `ConversationId`.
  - `Callers: SignedIn` turns installations and anonymous callers away.
  - Only chat is exposed. Exposing images is for #511.

### 5. Sample server, documentation and the plan

- **Sample server** (`samples/MauiSpinePushNotificationsSampleApp.Server`):
  - `AddSpineAI` with a `demo` provider, an `IChatClient` in the sample that streams a canned answer with made-up usage, so the whole thing runs without keys.
  - An exposed feature `Assistant`, a server-owned feature `Summary` behind `/sample/summary`, and `send.http` entries for both.
  - The usage report is open on localhost only.
  - The sample references `Anthropic` and `Microsoft.Extensions.AI.OpenAI`, and registers both providers with one line each. Features on them are off until `Ai:Providers:<name>:ApiKey` is in user secrets.
- **Documentation:**
  - the package `README.md`
  - a new `docs/wiki/ai-server.md`
  - `docs/wiki/packages.md` (the count, the table and the diagram) and the root `README.md` (the count and the table)
  - the `spine-setup` skill's server section, plus a short "AI on the server" section in the `spine-notifications` skill only if it fits. A new skill is not planned; ask if one is wanted.
  - the proposal's status line
- **The plan (`docs/plans/ai-and-security.md`):**
  - Step 3.3 Done (PR #534), phase 3 marked done, and the Highlight row in the Package map set to Done.
  - Step 2.1 In review, with its PR.
  - Decisions from this issue, the Verification backlog and Last updated.

### 6. Verification (all on the Mac)

- **Unit tests** with a fake `IChatClient` and `IImageGenerator`, `FakeTimeProvider` and the in-memory store:
  - resolution and the off states, and a rebuild after a configuration change
  - metering of streaming, non-streaming and abandoned streams, and pricing including unpriced and cached tokens
  - budget levels and the one-call overshoot, the soft alert once, and the rate limiter and the concurrency lease of a stream
  - the rules for exposed features, and `ToResult`
- **Azurite facts** for `AzureTableAIUsageStore`, among them concurrent increments of the same total. Azurite is in `~/.npm-global/bin`.
- **An ASP.NET Core test host** (`WebApplicationFactory`, or `TestServer` from `Microsoft.AspNetCore.TestHost`) for `MapSpineAI`: SSE framing, 404, 429 and 401.
- **Azure Functions through Aspire:** a scratch Aspire AppHost and Functions app outside the repo (the shape of Orientera's AppHost: `AddAzureFunctionsProject`, with Azurite as the emulator). The Functions app references the worktree's `Server.AI`, calls `SpineAIEndpoints.FeatureAsync` and `UsageAsync` from `[Function]`s, and uses the Azure Table usage store. I check that SSE streams per update through the Functions host's ASP.NET Core integration.
- **Real providers, used sparingly:** a few short calls with `MaxOutputTokens` low, against Anthropic (Orientera's `Story` key) and OpenAI (`~/.config/openai.env`, as Orientera's `ImageModel.FindApiKey` reads it). The calls are streaming and non-streaming chat, plus at most one image. The keys go in as environment variables to the scratch AppHost; they are never printed and never land in the repo. The point is to see what the adapters really fill in `UsageDetails`: cached input, reasoning, cache-write in `AdditionalCounts`, and usage at the end of a stream.
- **The sample server** with `curl` against `demo`.

## Open Questions

None. The owner answered all three on 2026-10-12; see Decisions.

## Changes

- `src/Plugin.Maui.Spine.Server.AI`: the configuration model, `SpineAIOptions`, `AIFeatureRegistry` (pipelines rebuilt on a configuration change), `IAIFeatures`, the caller-bound clients (`CallerChatClient` and `CallerImageGenerator`), the TTL cache, the meter and pricing, the in-memory and Azure Table usage stores, `AIBudgetGate` (budgets, per-caller limits, rate and concurrency limits, alerts), `SpineAIEndpoints` with `MapSpineAI` (JSON and SSE, the rules for exposed features), and the usage report.
- `tests/Plugin.Maui.Spine.Server.AI.Tests`: 64 tests, all passing, including the Azurite facts (Azurite in memory on 10002).
- The sample server: `DemoChatClient`, the Anthropic and OpenAI providers, an `Ai` section in `appsettings.Development.json`, `/sample/summary` and `/sample/picture`, and `send.http` entries.
- `Spine.slnx`, `Spine.Packages.slnf` and `Directory.Packages.props` (`Microsoft.Extensions.AI` 10.10.0; `Anthropic`, `Microsoft.Extensions.AI.OpenAI` and `Microsoft.AspNetCore.TestHost` for the sample and the tests).

### Verified
- **The demo provider on the sample server:** JSON and SSE. SSE arrives one update at a time, about 60 ms apart. Hidden and unknown features both answer 404 `notConfigured`, and an empty ApiKey shows in the startup log.
- **OpenAI, live (about 0.035 USD in all):**
  - `gpt-5.4-nano`, non-streaming and streaming. In a stream the usage comes last, in an update of its own, and `cachedInputTokenCount` and `reasoningTokenCount` are filled.
  - `gpt-image-1-mini`, one 1024×1024 picture. The adapter reports tokens (21 in, 4160 out).
  - A stream that curl cut short was recorded as incomplete and failed.

### Left to do
- **Functions through Aspire:** a scratch AppHost and Functions app in the shape of Orientera's (Aspire 13.4.6, Functions Worker 2.52.0, `Http.AspNetCore` 2.1.1, `global.json` 10.0.201).
- **Anthropic:** the `Story__ApiKey` in Orientera's `local.settings.json` answers 401 "invalid x-api-key". A valid key is needed to test the adapter.
- **The package icon:** `assets/logo-src/server-ai.png` and `assets/icons/server-ai.png` are missing, and a pack fails without them.
- **Documentation:** the package README, `docs/wiki/ai-server.md`, `docs/wiki/packages.md`, the root `README.md`, the `spine-setup` skill and the proposal's status line.
- **The plan:** step 3.3 Done, phase 3 done and the Highlight row Done, plus step 2.1, Decisions, the Verification backlog and Last updated.
- **The PR.**

## Decisions

- **2026-10-12, owner:** #510 stays one issue and one PR (§3–§5 whole), not split in two.
- **2026-10-12, owner:** Per-caller limits are two blocks, `SignedIn` and `Installation`, under `Budget.PerCaller` and `RequestsPerMinute`. `Installation` also covers anonymous callers. A missing `Installation` block falls back to the `SignedIn` values, and the wiki says to set it tighter.
- **2026-10-12, owner:** The sample server gets the Anthropic and OpenAI adapters, and the adapters are tried with a few real calls.
- **2026-10-12, owner:** The Functions path is verified by running it under Aspire. Orientera's backend has keys for both OpenAI and Anthropic, to be used sparingly.
- **2026-10-12, Claude:** Prices are nested as `Ai:Prices:<provider>:<model>` instead of the proposal's `"provider/model"` key. **Why:** the binder and environment variables read `:` levels naturally.
- **2026-10-12, Claude:** The budget is checked against the store's totals before every call, and a spent feature-level or app-level budget is also remembered in memory until the period ends. That way `Chat()` can return `null` and `StateOf` can report `BudgetSpent` without async I/O.
- **2026-10-12, Claude:** A keyed `IChatClient` is a singleton that looks up the feature at every call, as `AICaller.System`, and throws `AIFeatureOffException` while the feature is off. **Why:** it has to work inside singletons, and it must see configuration changes.
- **2026-10-12, Claude:** A provider failure answers 502 "The model call failed." The provider's own message goes to the log only, because it can contain keys or internal details.

