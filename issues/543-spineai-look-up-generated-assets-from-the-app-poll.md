# Issue #543 — Spine.AI: look up generated assets from the app (poll on 202, placeholder, failed state)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/543
**Branch:** issue/543-spineai-look-up-generated-assets-from-the-app-poll
**Status:** In Progress

Step 2.3c of `docs/plans/ai-and-security.md`. The app side of #511 (`Plugin.Maui.Spine.Server.AI.Assets`, PR #544).

## Plan

### The server's answers (from #511)

`GET {Backend}/assets/{feature}/{key}`:

| Answer | Meaning |
|---|---|
| 200 `{ status: "ready", url }` | The image exists. |
| 202 `{ status: "pending", retryAfter }` + `Retry-After` | It is ordered. |
| 502 `{ status: "failed", reason, retryAfter }` + `Retry-After` | The last attempt failed. It is ordered again after `retryAfter`. |
| 404 `{ status: "notFound" }` | The key is unknown. |
| 404 `{ off: "…" }` | The feature is off. |
| 429 + `Retry-After` | Too many orders. |

### In `Plugin.Maui.Spine.AI`

**Core (`net10.0` too, so it can be tested without a device):**
- `IAIAssets`, registered by `UseSpineAI`, uses the same `Backend`, `HttpClient`, `Authorization` and installation-id header as the features.
  - `Task<AIAsset> LookUpAsync(string feature, string key, CancellationToken)` asks once.
  - `IAsyncEnumerable<AIAsset> WatchAsync(string feature, string key, AIAssetWatch? options, CancellationToken)` asks, then asks again while the answer is pending or rate limited, and yields each change.
- `AIAsset` (record) has `Status` (`Ready`, `Pending`, `Failed`, `NotFound`, `Off`, `RateLimited`), `Url`, `Failure` (the server's reason text), `RetryAfter` and `Off` (`AIOffReason`).
- Polling:
  - It waits for `Retry-After`, clamped to 2–60 seconds, and stops after `MaxWait` (default 10 minutes). The last answer is then still `Pending`.
  - A failed image is not asked again within the watch. The server orders it again only after its cool-down, which is an hour by default.
  - Network errors throw, as the features' calls do, so `TaskState` shows its error and retry.
- The key keeps its `/` between segments, and each segment is escaped.

**MAUI only:**
- Polling pauses while the app is in the background and asks again as soon as it returns. Spine has a hook for this, which I check first; otherwise `Window.Stopped`/`Resumed`.
- `GeneratedImage` attached properties on `Image` (Decisions).

### Showcase

A **Generated images** page that shows the four postcards from the messaging sample (`/ai/assets/Postcard/{city}`): pending, ready and failed, plus an unknown city. `Backend` for this page points at the messaging sample's API.

### Tests

`tests/Plugin.Maui.Spine.AI.Tests`, against a fake `HttpMessageHandler`, covers:
- every answer
- `Retry-After` and its bounds
- `MaxWait`
- cancellation
- key escaping
- the headers

One end-to-end run against the messaging sample with Azurite in Docker. Real image calls are reused: an image that is already made is not made again.

## Open Questions

None. The owner answered both on 2026-10-14; see Decisions.

## Changes

- `Core/AIAssets.cs` (also `net10.0`):
  - `IAIAssets` (`LookUpAsync`, `WatchAsync`), `AIAsset`, `AIAssetStatus` and `AIAssetWatch`.
  - The key's segments are escaped, and the `/` between them is kept.
  - A missing route throws `HttpRequestException` with the URL.
- `SpineAIRuntime`:
  - `AddHeadersAsync`, the `Authorization` and installation headers, now shared with `SpineServerChatClient`.
  - `WhenActiveAsync`, the foreground gate.
  - The wire helpers in `SpineServerChatClient` are internal so the asset client reuses them.
- `SpineAIOptions.AssetsBackend`.
- MAUI:
  - `AppForeground`, which follows `Window.Stopped` and `Resumed`.
  - `GeneratedImage` attached properties: `Feature`, `Key`, `Placeholder`, `FailedSource`, `PendingView`, `FailedView`, and read-only `Status` and `Asset`.
  - `UseSpineAI` registers `IAIAssets`.
- Tests: `AssetClientTests` covers the client against a real `Server.AI.Assets` host (TestServer, in-memory messaging, fake clock):
  - pending, then ready with the URL and the installation header
  - segment escaping
  - not found, off, failed with its reason, and rate limited
  - a missing route, and no backend
  - a watch that asks again after `Retry-After`, ends at its longest wait, ends at failed, and is cancelled
  - 39 tests pass in the project.
- Showcase: a Generated images page (visby, kiruna, stockholm, gothenburg, atlantis) with a spinner as `PendingView` and a label as `FailedView`; `AssetsBackend` at `http://localhost:5240/ai`; `Plugin.Maui.Spine.AI` in the global XAML namespace.
- Verified on the iPhone 17 Pro simulator against the messaging sample's API and Azurite in Docker:
  - Two real `gpt-image-1-mini` postcards: the app asked, got 202 four times and then 200, and showed the images.
  - Pending and failed were checked with an invalid key and a raised version, which costs nothing: the spinner showed, then the failed label.
  - The unknown city failed at once.
  - In the background no lookups were made for 45 seconds. Back in front, four were made within 6 seconds.
  - Android and Windows: compiled only.
- Docs:
  - `docs/wiki/ai-client.md`: a Generated images section, `AssetsBackend`, and the sample.
  - `docs/wiki/ai-assets.md`, the package README, README, `packages.md` and the `spine-setup` skill.
  - The plan: 2.3b Done, 2.3c In review, Decisions, the backlog, two test recipes.

## Decisions

- **2026-10-14, owner:** The UI pattern is attached properties on `Image`: `GeneratedImage.Feature`, `.Key`, `.Placeholder`, `.FailedSource` and a read-only `.Status` for triggers. Not only a service, and not a control of its own.
- **2026-10-14, owner:** A watch ends at `Failed`. It does not wait out the server's cool-down; the next visit asks again.
- **2026-10-14, Claude:** `GeneratedImage.PendingView` and `.FailedView` take other views by `{x:Reference}` and show or hide them. **Why:** MAUI bindings cannot follow an attached property; `Path=(GeneratedImage.Status)` and `Path=(ai:GeneratedImage.Status)` both stayed empty in the simulator (compiled bindings warn MAUIG2045, reflection bindings resolve nothing). The read-only `Status` stays for the image's own triggers and for code.
- **2026-10-14, Claude:** `SpineAIOptions.AssetsBackend`, defaulting to `Backend`. **Why:** the Showcase's features run on the push sample server (5100) and its images on the messaging sample (5240), and a real app can serve images from a Functions app of its own.
- **2026-10-14, Claude:** The Showcase uses an `ActivityIndicator` as the pending view, not a `Shimmer`. **Why:** a `Shimmer` around an empty `Border` in the card's grid, inside a `BindableLayout` template, drew nothing, even always loading and with an explicit height. That is a Shimmer question for another issue.
- **2026-10-14, Claude:** A watch's waits run on the runtime's `TimeProvider`, so the tests move a fake clock instead of sleeping.
