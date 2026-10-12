# Issue #512 — Spine.AI: client IChatClient through the Spine server, on-device fallback and usage

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/512
**Branch:** issue/512-spineai-client-ichatclient-through-the-spine-server
**Status:** In review (PR #536)

This is step 2.4 of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505), tracked in `docs/plans/ai-and-security.md`. The design is in `docs/proposals/spine-ai.md` §7 and §10.2, together with Q23 (on-device usage stays on the device) and Q24 (the samples pin the .NET 10 preview of Microsoft.Maui.Essentials.AI). It builds on #510 (PR #535), whose wire format it consumes.

## Plan

### 0. Package

- **`src/Plugin.Maui.Spine.AI`** targets `net10.0` and the MAUI TFMs, the same split as `Plugin.Maui.Spine.Images`.
  - **`net10.0` carries the platform-neutral core:** the server client, the fallback chain, the meter and the exceptions. That lets it be tested against a real `Server.AI` host without a device.
  - **The MAUI TFMs add** `UseSpineAI(this MauiAppBuilder, …)`, the installation id in `SecureStorage` and the usage store in `Preferences`.
- **Dependencies:** `.Common` and `Microsoft.Extensions.AI.Abstractions` 10.10.x. `System.Net.ServerSentEvents` (`SseParser`) is part of .NET 10, so it needs no package.
  - **No reference to `Microsoft.Maui.Essentials.AI`.** It is a preview, and the app hands the on-device client in itself.
  - **No reference to the core** either: the core is not needed, and see question 1 for `TaskState`.
- **Registration:** a `<SpineModule>` in `build/Plugin.Maui.Spine.AI.props`. `UseSpine()` then calls `UseSpineAI()`, and the app calls it again with its options; a second call only applies the options, as with Images.
- **Tests and repository files:** `tests/Plugin.Maui.Spine.AI.Tests`, plus the icon `assets/icons/ai.png`, `Spine.slnx`, `Spine.Packages.slnf` and `Directory.Packages.props`.

### 1. Options and features

```csharp
builder.UseSpineAI(o =>
{
    o.Backend = new Uri("https://api.example.com/ai");          // the prefix given to MapSpineAI
    o.AuthorizationHeader = ct => auth.GetHeaderAsync(ct);       // optional; read on every call
    o.Feature("Assistant", f => f
        .OnDevice(sp => new AppleIntelligenceChatClient(), isAvailable: () => OperatingSystem.IsIOSVersionAtLeast(26))
        .ThenServer());
    o.Feature("RaceStory", f => f.Server());
});

public sealed partial class AssistantViewModel([FromKeyedServices("Assistant")] IChatClient chat) : ViewModelBase { … }
```

- **`AIFeatureChain`** lists the sources in order: `OnDevice(factory, isAvailable?)`, `Server()` and `ThenServer()`, an alias that reads better after `OnDevice`. Any order is allowed, server first and then the device included.
- **Each feature is a keyed singleton `IChatClient`** (`SpineFeatureChatClient`). The server source is a `SpineServerChatClient` over one `HttpClient` that the package owns.
- **A feature with only a server source and no `Backend`** fails at the first call. The message names `SpineAIOptions.Backend`, so the failure is not silent.

### 2. The server client (`SpineServerChatClient`)

- **The request:** `POST {Backend}/features/{name}` with the body `{ "messages": [...], "conversationId": … }` in Microsoft.Extensions.AI's own JSON (`AIJsonUtilities.DefaultOptions`), the same format `SpineAIEndpoints` reads.
  - **Headers:** `Authorization` from `AuthorizationHeader` when it is set, and `Spine-Installation` with the installation id.
  - **The installation id** is the push installation id when the app has Spine.PushNotifications: the same `SecureStorage` key, `spine.push.installation-id`, moved into a shared constant in `.Common`. Otherwise it is a new GUID under that key, which push then adopts if it is added later.
- **Non-streaming calls** send `Accept: application/json` and read back a `ChatResponse`.
- **Streaming calls** send `Accept: text/event-stream` and read with `SseParser`, using `HttpCompletionOption.ResponseHeadersRead`.
  - Every default event is one `ChatResponseUpdate`.
  - `done` ends the stream.
  - `error` throws an `HttpRequestException` with status 502, carrying the server's message.
  - Cancellation stops reading and disposes the response, so the server sees the client go away and records the stream as cut short.
- **Answers in one shape:**

| Server answer | Client |
|---|---|
| 404 `{ "off": reason }` | `AIFeatureOffException(feature, reason)`: `NotConfigured`, `Disabled` or `Budget` |
| 403 `{ "off": "signInRequired" }` | `AIFeatureOffException(feature, SignInRequired)` |
| 429 with `Retry-After` | `AIRateLimitedException(feature, retryAfter)` |
| 413 `{ "tooLarge": "turns" or "tokens", "max": n }` | `AIRequestTooLargeException(feature, limit, max)`, so a chat can say "start a new conversation" |
| 404 **without** an `off` body (wrong `Backend`, route not mapped) | `HttpRequestException` with the status and the full URL. It is not "off", because a misconfigured URL must not look like a feature that is switched off |
| Other non-success, network failure | `HttpRequestException` with the status and URL |

### 3. The fallback chain (`SpineFeatureChatClient`)

- **For each source in order:**
  - It is skipped when `isAvailable` says no.
  - Otherwise it is called. A non-streaming call moves on when the source throws.
  - A stream moves on only when the source throws **before its first update**. After the first update there is no fallback, because the user has already seen text.
- **What counts as a failure:**
  - **Cancellation never falls back.**
  - **Every other exception does:** off, budget, rate-limited, too large, a network error, and an on-device model that is not ready.
  - **Each failure that falls back is logged** at Warning, with the source and the exception.
- **When every source fails,** the last exception is thrown. With the usual "device, then server" order, that is the server's answer in the one shape.
- **`GetService(typeof(AIAnsweredBy))`** and an `AnsweredBy` property in the response's `AdditionalProperties` say which source answered (`Device` or `Server`), so a UI can show it.
- **Availability on the device:** Essentials.AI has no public availability API (checked in 10.0.80-preview.1.26319.4). The chain therefore relies on `isAvailable` and on "throws before the first update". The plan is to test in the simulator what `AppleIntelligenceChatClient` actually does when Apple Intelligence is off or the model is not ready.

### 4. On-device usage (Q23: it stays on the device)

- **The meter:** on-device sources are wrapped in a meter that adds up `UsageDetails`, or estimates at four characters a token when the model reports none.
  - It writes calls and tokens, in and out, per feature and day through `IAIDeviceUsageStore`. That store is `Preferences` on the MAUI TFMs and in memory for tests.
  - It keeps 62 days and prunes older days when it writes.
- **`IAIUsage`** reads the totals: `ForDay(feature, day)` and `ForRange(feature, from, to)`. It has no cost, because an on-device call is free.
- **Server calls** are metered by the server (#510), not here.

### 5. Showcase and the push sample server

- **A page `AI client`** in `samples/MauiSpineSampleApp`:
  - a prompt, Send (streamed into a label), Stop, and which source answered
  - the error in the one shape
  - today's on-device usage
- **Two features on that page:**
  - `Assistant`: on the device first, then the server
  - `ServerOnly`: the server only
- **The backend** is the push sample server from #510 with its `demo` provider: `http://localhost:5100/ai` on iOS and Mac, and `http://10.0.2.2:5100/ai` on the Android emulator. Cleartext to these hosts only, debug builds only.
- **The sample references `Microsoft.Maui.Essentials.AI` 10.0.80-preview.1.26319.4 (Q24)** for `AppleIntelligenceChatClient` on iOS and Mac Catalyst. Android and Windows get the server only.

### 6. Documentation and the plan

- **Documentation:**
  - the package `README.md`
  - a new `docs/wiki/ai-client.md`
  - `docs/wiki/packages.md` and the root `README.md` (twenty-two packages)
  - the `spine-setup` skill
  - the proposal's status line
- **The plan:**
  - step 2.1 Done and the `Server.AI` row updated (#535)
  - step 2.4 In review, with Decisions, the Verification backlog and Last updated

### 7. Verification

- **Unit tests (`net10.0`):**
  - **The client against a real `Server.AI` host** (`TestServer`) with a fake provider: streaming and non-streaming, off with each reason, 403, 429 with `Retry-After`, 413, a 404 without a body, an error event mid-stream, cancellation, and the headers the server sees (`Spine-Installation`, `Authorization`).
  - **The chain with fakes:** unavailable, a throw before the first update, no fallback after the first update, cancellation, the last exception thrown, `AnsweredBy`, and the meter.
- **iPhone 17 simulator** (iOS 26):
  - the Showcase page against the sample server
  - `AppleIntelligenceChatClient` when the Mac's Apple Intelligence is on, and what happens when it is not available
  - a recording when text streams in
- **Android emulator `Pixel_10_Pro`:** the server path through `10.0.2.2`, and the fallback when the device source is missing.
- **Mac Catalyst:** compile.
- **Windows:** compile with the flags from the instructions.

## Open Questions

None. The owner answered both on 2026-10-12; see Decisions.

## Changes

- **`.Common`:**
  - `AI/AIFeatureException.cs`, with `AIOffReason`, `IEmptyResultException`, `AIFeatureException`, `AIFeatureOffException`, `AIRateLimitedException`, the new `AIRequestTooLargeException`, and `AIOffReasons` for the JSON words
  - `Push/SpineInstallation.cs`, holding the shared `SecureStorage` key and the header
  - the `InternalsVisibleTo` entries for the AI assemblies
- **The core:** `TaskState` gives `Empty` for an `IEmptyResultException` when there is no result yet. Two tests in `Core.Tests`.
- **`Server.AI`:**
  - It now references `.Common`, and the old `AIFeatureException.cs` is gone. `AIFeatureResults.ToResult()` is an extension method and also covers 413.
  - The endpoints answer 413 through it, and `AICaller.InstallationHeader` comes from `.Common`.
  - The push sample server gets `using Plugin.Maui.Spine.AI;`.
- **Push:** the installation id key comes from `SpineInstallation.IdKey`. The value is unchanged.
- **`src/Plugin.Maui.Spine.AI`:**
  - `SpineAIOptions` and `AIFeatureChain`, where `Server(name)` lets an app feature run on a server feature of another name
  - `SpineServerChatClient`, which writes the request body by hand around Microsoft.Extensions.AI's type info, reads SSE with `SseParser`, and turns a cancellation into `OperationCanceledException`
  - `SpineFeatureChatClient` (the fallback chain), with the `AnsweredBy()` extensions
  - `DeviceMeteredChatClient`, `IAIUsage`, the in-memory and `Preferences` stores, and `SpineAIRuntime`
  - `UseSpineAI` with keyed singletons, and its `<SpineModule>`
- **Tests:** `tests/Plugin.Maui.Spine.AI.Tests`, with 29 tests that run the client against a real `Server.AI` `TestServer`, plus the chain with fakes.
- **The Showcase:**
  - the page **AI client**, with Assistant (device, then server), a server-only switch, Stop, `AnsweredBy`, the problem line, on-device usage, and Story as a feature that is off in a `StateView`
  - `UseSpineAI` in `MauiProgram`
  - `Microsoft.Maui.Essentials.AI` 10.0.80-preview.1.26319.4 on iOS and Mac Catalyst, with `MAUIAI0001` suppressed
- **The icon:** `assets/logo-src/ai.png` and `assets/icons/ai.png`.
- **Documentation:**
  - the package README and `docs/wiki/ai-client.md`
  - `docs/wiki/ai-server.md` (the shared exceptions and the link to the client)
  - `docs/wiki/loading-states.md` (`IEmptyResultException`)
  - `docs/wiki/packages.md` (the row, the graph, the hook, the target frameworks, the `SpineModule` row and twenty-two packages) and the root `README.md`
  - the `spine-setup` skill and the proposal's status line
- **The plan:** step 2.1 Done, step 2.4 In review, steps 2.3 and 4.2 Ready, with Decisions, the Verification backlog and a test recipe.

### Verified
- **Unit tests:**
  - **The client against the real server host:** off with each reason, 403, 429 with `Retry-After`, 413, a wrong backend that names its URL, a missing backend, a 502 before and mid-stream, the headers the server sees, update-by-update streaming, a stream left early that the server records as incomplete, and a cancelled read that the handler turns into a `TimeoutException`. That last test was checked against the code without the fix: it fails with `TimeoutException`.
  - **The chain:** an unavailable source, a throw before the first update, no fallback after it, cancellation, the last failure, nothing available, and the meter with estimates.
  - **`Server.AI`** (59 tests plus the 5 Azurite facts) and **the core** (101) still pass.
- **iPhone 17 simulator (iOS 26.4):**
  - The page streams from the push sample server: a recording at 4 fps shows the text growing frame by frame.
  - **Device first:** `AppleIntelligenceChatClient` throws at once in the simulator (FoundationModels `GenerationError`, `ModelManagerError 1026`), and the server answered.
  - **Stop with real taps** showed "TimeoutException: The request timed out" before the fix and "Stopped." after it.
  - **Story** shows `Empty` with "The AI feature 'Story' is off: NotConfigured."
- **Android emulator `Pixel_10_Pro`:** `localhost` through `adb reverse` works for a cleartext request in Debug.
  - Streaming: the first word arrived after 400 ms.
  - Stop with real taps shows "Stopped.".
  - Story is `Empty`.
- **Builds:** Mac Catalyst built. Windows compiled for the package with the flags from the instructions.
- **Cleanup:** the installation id the client wrote was removed from `SecureStorage` on both, no usage was written, clean builds without the harness are installed, `adb reverse` is removed, and the emulator is shut down.

### Not verified (in the plan's Verification backlog)
- **Apple Intelligence answering on a device.**
- **Windows and Mac Catalyst at run time.**
- **A release build over HTTPS on a phone.**

## Decisions

- **2026-10-12, owner:** "Feature off" shows as `TaskState`'s empty state through a marker interface in `.Common` (`IEmptyResultException`), which the core's `TaskState` honours. The core knows nothing of AI.
- **2026-10-12, owner:** `AIOffReason` and the exceptions move to `.Common` and are shared by app and server. `Server.AI` references `.Common`, and `ToResult()` becomes an extension method.
- **2026-10-12, Claude:** The exceptions are in namespace `Plugin.Maui.Spine.AI` inside `.Common`, like `BlurHash` is in `Plugin.Maui.Spine.Images` there. **Why:** app code reads `using Plugin.Maui.Spine.AI;` for both the client and its exceptions. Server code adds that `using`, which the push sample server needed.
- **2026-10-12, Claude:** `SpineAIOptions.HttpHandler` is public, not internal as planned. **Why:** tests need it, and so does an app that pins certificates or logs traffic.
- **2026-10-12, Claude:** "Answered by" is an extension method on the response and the first update, `AnsweredBy()`, rather than `GetService(typeof(AIAnsweredBy))`. **Why:** the source is a property of one answer, not of the client.
- **2026-10-12, Claude:** `Server(name)` and `ThenServer(name)` take an optional server feature name. **Why:** the Showcase needs a device-first and a server-only client for the same server feature, and apps may name features differently from their server.
- **2026-10-12, Claude:** A call cancelled through its token always ends in `OperationCanceledException`. **Why:** iOS's `NSUrlSessionHandler` throws `TimeoutException` for a cancelled read, and Stop showed a timeout. This was found with real taps.
- **2026-10-12, Claude:** A 404 without an `off` body is an `HttpRequestException` with the URL, not "off". **Why:** a wrong `Backend` must not look like a switched-off feature.
- **2026-10-12, Claude:** The Showcase uses `http://localhost:5100/ai` on every platform, with `adb reverse` on Android, not `10.0.2.2`. **Why:** the push sample app's own notes. 10.0.2.2 is the gateway on the emulator's `eth0`, while app traffic goes over `wlan0`.
