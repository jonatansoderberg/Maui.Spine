# Issue #511 — Spine.Server.AI: generated-asset pipeline (order, queue, generate, archive, look up)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/511
**Branch:** issue/511-spineserverai-generated-asset-pipeline-order-queue
**Status:** In Review

Step 2.3b of `docs/plans/ai-and-security.md`. Builds on `Server.AI` (#510) and `Server.Messaging` (#540).

## Plan

Generated images are slow (tens of seconds) and cost money, so each one is made once, archived in blob storage and served from there. The lookup never waits for a model.

### Flow

```
app: GET /ai/assets/{feature}/{key}
  ├─ key rejected by the app's prompt (unknown event, bad format)   → 404
  ├─ blob v{Version}/{key}.png exists                               → 200 { status: "ready", url: ".../v3/{key}.png?v={LastModified}" }
  ├─ failure marker v{Version}/{key}.failed.json, within cool-down  → failed answer (Q2) { status: "failed", reason, retryAfter }
  └─ missing → publish GenerateAsset { Feature, Version, Key }
               with DeduplicationId "feature|version|key"           → 202 { status: "pending" }  (Retry-After header)
handler (Server.Messaging subscriber "spine-ai-assets"):
  prompt.BuildAsync(key) → IImageGenerator of the feature (AICaller.System: metered, budgeted)
  → optional IGeneratedAssetCheck → upload v{Version}/{key}.{ext}, delete any failure marker
  last attempt failed / unrecoverable → write the failure marker
```

### Types (names are proposals)

- `IGeneratedAssetPrompt<TKey>` (the app): `ValueTask<bool> AcceptsAsync(TKey key, ct)` (guards the lookup: only keys the app knows can be ordered) and `ValueTask<ImageGenerationRequest> BuildAsync(TKey key, ct)` (the request, with original images for an edit). `TKey : IParsable<TKey>`; the route's string is parsed (400 if it does not parse) and `key.ToString()` is the canonical name in blob paths and the dedup id, checked to round-trip and to be a safe path (`[A-Za-z0-9._-]`, `/` between segments, at most 256 characters).
- `IGeneratedAssetCheck<TKey>` (optional, the app): looks at the bytes and returns pass or a reason. A failed check throws inside the handler, so Messaging retries it (a new image each attempt).
- Registration: `services.AddSpineAIAssets(section, a => a.Asset<ArenaKey, ArenaPrompt>("ArenaImage").Check<ArenaCheck>())` plus the store (`UseAzureBlobAssetStore(...)` / `UseInMemoryAssetStore()`), and the subscriber declared on the app's `AddSpineMessaging` builder through `m.AddSpineAIAssets()` (message type, subscriber, retry policy).
- `IGeneratedAssetStore`: `GetAsync(path)` (url + last modified), `PutAsync(path, bytes, contentType)`, failure marker read/write/delete. Azure Blob and in-memory implementations.
- `IGeneratedAssets` for server code: `LookUpAsync(feature, key, caller)` → `GeneratedAsset { Status, Url, Reason, RetryAfter }`, the same thing the endpoint answers.
- Endpoints in the style of `SpineAIEndpoints`: `SpineAIAssetEndpoints.GetAsync(request, feature, key, ct)` for Functions, and `MapSpineAIAssets()` for ASP.NET Core.

### Configuration

Per feature, next to the feature's existing settings:

```json
"Ai": { "Features": { "ArenaImage": {
  "Kind": "Image", "Provider": "openai", "Model": "gpt-image-1",
  "Image": { "Size": "1536x1024" },
  "Asset": { "Version": 3, "Expose": true, "FailureCoolDown": "01:00:00", "OrdersPerMinute": { "Installation": 5, "SignedIn": 20 } }
} } }
```

- `Version` bump: every key is missing again, so images are remade lazily, one at a time as they are asked for. Old versions stay in the container as an archive.
- Only features with `Asset.Expose` answer the endpoint; others get the same 404 as an unknown feature.

### Abuse and cost

The lookup is anonymous-capable, so an unchecked key would let anyone order images on the owner's budget:
- `AcceptsAsync` refuses keys the app does not know.
- Orders (not lookups, not duplicates) count against a per-caller rate, `Asset:OrdersPerMinute`, by the same caller identity as `Server.AI`.
- Generation runs as `AICaller.System`, so the feature's and the app's budgets apply. A spent budget is not retried: it writes a failure marker with reason `budget` and a retry-after at the period's end.

### Lookup cost

A `ready` answer is cached in memory for a short time (default 5 minutes) and carries `Cache-Control`. `pending` and `failed` are never cached in memory, so a finished image shows on the next ask. (Orientera caches "not there yet" for 5 minutes too.)

### Failures the app can see

The handler knows its own retry policy, so on the last attempt (or an `UnrecoverableMessageException`, a refused check on the last attempt, a feature that is off) it writes `v{Version}/{key}.failed.json` with the reason (`model`, `check`, `budget`, `off`, `prompt`), the time and the attempts; never the provider's message, which goes to the log only (as in #510). After the cool-down, the next lookup orders again with the dedup id `feature|version|key|{failedAt ticks}`, because the original id is still reserved for its window.

### Tests

- Unit tests on Messaging's in-memory bus with `FakeTimeProvider` and a fake `IImageGenerator`: lookup → 202 → handled → 200 with `?v=`; a burst of lookups orders once; version bump remakes; a check that fails then passes; the last failed attempt shows `failed`, and the cool-down orders again; budget spent; rejected and unparseable keys; order rate limit; path safety.
- Integration against Azurite (`[AzuriteFact]`): the blob store, the public URL, `LastModified` cache-buster.
- One real image call through the sample, the smallest size and quality.

### Sample and docs

- An assets endpoint in `samples/SpineMessagingSample.Api` (it already runs Azurite and the pump under Aspire), with an OpenAI image feature; the key from `~/.config/openai.env` through an environment variable.
- `docs/wiki/ai-assets.md` (or a section in the AI wiki page), the proposal's §6 status, the package tables, the plan.

## Open Questions

None. The owner answered the four questions on 2026-10-14; see Decisions.

## Changes

- New package `src/Plugin.Maui.Spine.Server.AI.Assets` (in `Spine.slnx` and `Spine.Packages.slnf`, icon `server-ai.png`):
  - `IGeneratedAssetPrompt<TKey>` (`AcceptsAsync`, `BuildAsync`) and `IGeneratedAssetCheck<TKey>` for the app; `TKey : IParsable<TKey>`, canonical text checked to round-trip and to be a safe path.
  - `IGeneratedAssets.LookUpAsync` (string and typed), returning `GeneratedAsset` (`Ready`, `Pending`, `Failed`, `NotFound`, `Off`, `RateLimited`).
  - `GenerateAssetHandler` on Spine messaging: version check, skip when already made, prompt → `IAIFeatures.Image(feature, AICaller.System)` → check → archive; failure record on the last attempt or at once for budget/off/unrecoverable.
  - `IGeneratedAssetStore` with `AzureBlobGeneratedAssetStore` (public container + private `-failures` container, `PublicBaseUrl`, startup errors for forbidden or private access) and `InMemoryGeneratedAssetStore`.
  - `AddSpineAIAssets`, `MessagingBuilder.AddGeneratedAssets`, `MapSpineAIAssets`, `SpineAIAssetEndpoints.GetAsync`, `GeneratedAsset.ToResult`; a startup check for missing `AddSpineAI`, missing messaging declaration, non-image features and unknown media types.
- Tests `tests/Plugin.Maui.Spine.Server.AI.Assets.Tests`: 29 (lookup, dedup across two servers, version bump, stale orders, keys, rate limits, off, media type, check retry, failure + cool-down, prompt and budget failures, no provider text in the record, endpoints via TestServer, three Azurite facts with an anonymous fetch).
- Messaging sample: the API serves `Postcard` assets (`Postcards.cs`, `appsettings.json`, `send.http`), runs the `spine-ai-assets` pump; the shared declaration calls `AddGeneratedAssets()`; the AppHost passes `Ai__Providers__openai__ApiKey` on when set.
- Verified live: one `gpt-image-1-mini` postcard through the API against Azurite in Docker (202 → handled on attempt 1 → 200 with `?v=`; the URL fetched anonymously as `image/png`).
- Docs: `docs/wiki/ai-assets.md`, the package README, README and `docs/wiki/packages.md` (28 packages), `spine-setup` skill, `ai-server.md` link, the proposal's status line, the plan (2.3a Done, 2.3b In review, 2.3c #543 added, Decisions, Verification backlog, a test recipe).
- Follow-up issue #543 for the app side.
- PR #544. `/code-review` found 9 things; fixed 7:
  - the container access-level probe only logs a refusal (401/403), so a managed identity without ACL rights no longer stops the host;
  - rate limiters are created under a lock, and one replaced after a reload is disposed with the service, not while a lookup may hold it;
  - a key that parses but whose `ToString()` does not read back is logged once per feature, instead of every lookup being a silent "not found";
  - `OrderWindow` defaults to 15 minutes (was 1 hour), and the host warns at startup when it is shorter than the retries can take, so a lost order answers "pending" for 15 minutes at most and a short window cannot make an image twice unnoticed;
  - `InMemoryGeneratedAssetStore` keeps a base URL's path without a trailing slash;
  - the sample AppHost passes the OpenAI key as a secret parameter (`openaikey`), masked in the dashboard.
  - Skipped: reusing Messaging.AzureStorage's internal connection lookup (it would add Storage Queues as a dependency for a dozen lines), and skipping the failure-record read on pending polls (it is how a failure on another server becomes visible).
  - 32 tests pass.

## Decisions

- **2026-10-14, owner:** A new package, `Plugin.Maui.Spine.Server.AI.Assets` (references `Server.AI`, `Server.Messaging` and `Azure.Storage.Blobs`), not `Server.AI` itself. **Why:** an app with only chat should not pull in Messaging.
- **2026-10-14, owner:** A failed image answers 502 with `{ status: "failed", reason, retryAfter }` and a `Retry-After` header; ready is 200 and pending 202.
- **2026-10-14, owner:** Images are served from a public-read blob container, with an optional `PublicBaseUrl` for a CDN. **Why:** SAS URLs change on every lookup and break the app's URL cache.
- **2026-10-14, owner:** #511 is the server only. Client support (a lookup that polls on 202 in `Spine.AI`, and a placeholder pattern) is a follow-up issue.
- **2026-10-14, Claude:** Blob paths are `{feature}/v{version}/{key}.{ext}`, not the proposal's `v{Version}/{key}.png`. **Why:** several features share one container.
- **2026-10-14, Claude:** Failure records go in a private `{container}-failures` container. **Why:** the images' container is public, and a record is not for the app to read.
- **2026-10-14, Claude:** The lookup asks the store before anything else, then the failure record, then whether the feature is on, then its own memory of orders, and only then `AcceptsAsync`, the rate limit and the publish. **Why:** polling a pending image costs two blob reads and no app code, and an archived image is served even when the feature is off.
- **2026-10-14, Claude:** The order after a cool-down uses the deduplication id `feature|version|key|{failedAt ticks}`. **Why:** Messaging holds the first id for `OrderWindow`, and a re-order with it would be dropped silently as a duplicate.
- **2026-10-14, Claude:** A spent budget or a feature that is off fails the order at once (reason `budget` or `off`), without retries. **Why:** retrying cannot help before the period ends, and every retry would ask the gate again.
- **2026-10-14, Claude:** The `?v=` URL is written by hand, not with `UriBuilder`. **Why:** `UriBuilder.Uri` keeps `:443` in `OriginalString`, which is what JSON writes (a test caught it).
- **2026-10-14, Claude:** The lookup's caches (ready answers, its own orders) are small dictionaries on the injected `TimeProvider`, not `MemoryCache`. **Why:** tests move the clock.
- **2026-10-14, Claude (review):** `OrderWindow` is 15 minutes by default, with a startup warning when it is shorter than the retries can take (3 minutes per attempt plus the waits with jitter; about 11 minutes for the default 3 attempts). **Why:** an order lost without reaching the handler's last attempt leaves no failure record, so the window is how long the app sees "pending" before the next lookup orders again.
