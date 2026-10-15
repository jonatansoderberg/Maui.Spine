# Issue #517 — Spine.Server.AI: realtime voice session endpoints (no keys in the app)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/517
**Branch:** issue/517-spineserverai-realtime-voice-session-endpoints-no
**Status:** In Progress

## Plan

Step 4.2 of `docs/plans/ai-and-security.md`. The proposal is `docs/proposals/spine-voice.md` §6. The owner's answers narrow the issue's scope: only OpenAI and Azure OpenAI in v1 (Q28), and WebSocket with PCM rather than WebRTC unless measurements call for it (Q31). Gemini Live, Azure Voice Live, WebRTC SDP exchange and the sideband connection are therefore out of this PR. They stay in the proposal for when an app needs them.

**A realtime feature in configuration.** `AIFeatureKind.Realtime` joins Chat and Image. `Ai:Features:<Name>` with `Kind: Realtime` gets a `Realtime` section (`AIRealtimeOptions`) that holds everything the session is built from: voice, instructions, turn detection, input transcription, `MaxMinutes` and the token's TTL (60 s by default). The client sends only the feature name.

**Providers.** `ai.Realtime("openai", AIRealtimeProviders.OpenAI)` and `ai.Realtime("azure", AIRealtimeProviders.AzureOpenAI)`. These are built-in minters over `HttpClient`, configured from `Ai:Providers:<name>` (`ApiKey`, and for Azure `Endpoint`). An app can also register its own minter. M.E.AI's `IRealtimeClient` connects; it does not mint, so the minter is Spine's own small interface.

**The endpoint.** `POST {prefix}/realtime/{name}` through `MapSpineRealtimeSessions(prefix = "/ai")`, plus `SpineAIEndpoints.RealtimeSessionAsync` for Azure Functions. It answers `{ provider, model, url, token, expiresAt, maxMinutes }`, and the app connects to `url` with the token as bearer. The provider's key never leaves the server.

**Best practice from voice §6.2:**
- A realtime feature answers only signed-in callers unless its configuration says `Callers: Any`, which is the opposite of chat's default.
- `RequestsPerMinute`, the per-caller `DayCalls` and the budgets go through `AIBudgetGate` as for chat.
- Metering by reservation: each mint records `MaxMinutes × Ai:Prices:<provider>:<model>:PerMinute` at once, because the server never sees the session's real usage.
- The token is short-lived. The provider's session id is logged with the caller.

**Tests.** Endpoint tests with a fake minter: off/unknown feature 404, anonymous refused by default, rate limit 429, budget off, reservation recorded, config from the server only. A unit test checks the OpenAI request body the minter builds against a stub `HttpMessageHandler`.

**Docs.** README and the wiki page for Server.AI, the plan (4.2 status, Decisions, Last updated).

## Open Questions

## Changes

- `AIConfiguration.cs`: `AIFeatureKind.Realtime`, `AIFeatureOptions.Realtime` (`AIRealtimeOptions`: voice, turn detection, eagerness, noise reduction, transcription model and language, `MaxMinutes`, `TokenSeconds`), `AIPrice.PerMinute`. `Callers` is nullable; unset means signed-in only for realtime.
- `AIRealtime.cs` (new): `IAIRealtimeProvider`, `AIRealtimeRequest`, `AIRealtimeSession`, and `AIRealtimeProviders.OpenAI` / `.AzureOpenAI`, which call `client_secrets` with the server's key and the feature's session (PCM16 at 24 kHz).
- `SpineAIOptions.Realtime(name, factory)`; the registry builds realtime features, and `AddSpineAI` registers no keyed `IChatClient` for them.
- `IAIFeatures.RealtimeAsync`: callers, rates and budgets through `AIBudgetGate`, then the token, then `MaxMinutes × PerMinute` recorded through `AIUsageMeter` (`minutes` parameter, `AIPricing.Cost`).
- `SpineAIEndpoints.RealtimeSessionAsync` and `MapSpineRealtimeSessions(prefix)`: `POST /ai/realtime/{name}`, `Cache-Control: no-store`, the provider's session id in the log, 502 without the provider's message.
- Tests: `RealtimeTests` (the endpoint with a fake provider) and `OpenAIRealtimeProviderTests` (the request body, headers and URLs against a stub handler).
- Push sample server: `Voice` feature (`Callers: Any`, 3 minutes, 0.2 USD a minute as an example price), `ai.Realtime("openai", …)`, `MapSpineRealtimeSessions`, and a `send.http` request.
- Docs: wiki `ai-server.md` (Realtime voice sessions, the Functions handler, the sample), package README, and the plan (4.2 in review, Decisions, Verification backlog, a live recipe, Plan history).

## Decisions

- The owner chose tokens only, with no relay, on 2026-10-15. The owner also chose OpenAI as the only live verification; Azure OpenAI is built from Microsoft's docs and tested against a stub.
- A realtime feature must be `Expose: true`, like an exposed chat feature, and a chat route never answers it.
- `ConcurrentPerCaller` covers only the token request: the server cannot see when a session ends.
- Live against OpenAI (2026-10-15, push sample server on localhost:5100): 200 with `no-store`. The socket opened with the `ek_` token as bearer, and `session.created` carried `marin`, `semantic_vad`, `gpt-4o-mini-transcribe` and the server's instructions. A client `session.update` changed the instructions. **A second connection with the same token opened the same session**, so the token is not single-use; the docs say so, and `TokenSeconds` defaults to 60. The usage report showed the reservation (5 min × 0.2 = 1.0, incomplete), and the next token was refused at the sample's DayHard of 1. The sample's budget was then set to 1.5 and the session length to 3 minutes.
- Server.AI is not released yet, so `Callers` could become nullable and `IAIFeatures` could gain a member without breaking anyone.
- Verified locally (CI is paused): `dotnet test` for Server.AI (76 passed, 5 Azurite skipped), Server.AI.Assets (29 passed, 3 skipped) and Spine.AI (40 passed); the push sample server builds.
