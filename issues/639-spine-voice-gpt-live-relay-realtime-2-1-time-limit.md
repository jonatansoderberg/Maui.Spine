# Issue #639 — Spine.Voice: GPT-Live through a relay, Realtime 2.1 models and a time limit per session

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/639
**Branch:** issue/639-spine-voice-gpt-live-relay-realtime-2-1-time-limit
**Base:** `issue/634-chat-and-voice-in-one-conversation-type-or-talk-on` (PR #635, not merged on 2026-10-20)
**Plan:** `docs/plans/ai-and-security.md`, step 4.6
**Status:** In Progress

## Plan

The owner asked (2026-10-20) for the voice chat to support GPT-Live and `gpt-realtime-2.1` and `-2.1-mini`, with a time limit per session to bound cost, after the spike report (`docs/proposals/spine-voice-gpt-live.md`, PR #637).

- **Server.AI:** `Realtime:Protocol: Live` with a `Backend` (Responses delegation). `POST /ai/realtime/{name}` answers a Live feature with a one-time ticket for `{prefix}/realtime/{name}/relay`. The relay opens GPT-Live with the key, writes `session.start` from configuration, filters what the app sends, runs server functions itself, ends the session at its limit, and settles the reservation with the seconds and backend tokens used.
- **Voice:** the `openai-live` protocol in `VoiceSession` next to `openai`: Live's audio and transcript events, no truncate/cancel, the app's functions answered with `response.item.create`.
- **Time limit:** `?maxMinutes=` on the token request (`IVoiceSession.MaxMinutes`), capped by the feature's `MaxMinutes`; the reservation follows it.
- **Models:** `Reasoning` for the 2.1 models; the sample's Voice on `gpt-realtime-2.1-mini`, plus VoiceFull and VoiceLive; docs on 2.1. Closes #636.

## Changes

- `AIRealtimeOptions.Protocol` (`Realtime`/`Live`), `Reasoning`, `Backend` (`AILiveBackendOptions`: `Model`, `Instructions`, `Reasoning`, `HostedTools`); `LimitFor` caps the app's `maxMinutes`.
- `IAILiveProvider.ConnectLiveAsync`, implemented by the OpenAI and Azure OpenAI provider (`wss://…/v1/live/sessions`, `api-key` on Azure, a hashed `OpenAI-Safety-Identifier`).
- `AIRealtimeTickets`: Data Protection–signed tickets with the expiry in the payload, read, checked against the caller, then spent once.
- `AIRealtimeRelay` and `SpineAIEndpoints.RealtimeRelayAsync`, mapped by `MapSpineRealtimeSessions` at `{prefix}/realtime/{name}/relay`; 400 naming `UseWebSockets()` for a plain request.
- `IAIFeatures.RealtimeAsync(…, maxMinutes)`, `AIRealtimeRequest.MaxMinutes`, `AIRealtimeSession.MaxMinutes`; the answer's `maxMinutes` is the session's.
- `AIUsageRecord.Adjustment`, `AIUsageMeter.SettleAsync`; both stores count an adjustment's cost but not as a call, and the report's sums too. Reserved minutes are fractional.
- Registry: a Live feature without `Backend:Model`, or on a provider without `IAILiveProvider`, is off with the reason.
- `IVoiceSession.MaxMinutes`; `VoiceTokenClient` sends `maxMinutes`, resolves a relative relay URL against `SessionEndpoint`, and opens the relay with the app's own headers (`IVoiceSocket.ConnectAsync` takes headers).
- `VoiceSession`: the `openai-live` protocol (start message with history and the app's functions, Live audio and transcript events, silence skipped, items ended after 400 ms without audio, yield detection, local-only barge-in, mute/unmute, silence padding, `spine.user_text`, functions through `response.event`, server functions shown from `spine.function.output`, `session.closed` with `time_limit`, goodbye ending after the voice's farewell or 8 s).
- A function's JSON string result goes to the model unquoted (both protocols, and in the relay).
- The relay drops GPT-Live's silent audio before it reaches the app.
- Sample server: Voice on `gpt-realtime-2.1-mini` with `Reasoning: low`, VoiceFull (`gpt-realtime-2.1`), VoiceLive (`gpt-live-1`, backend `gpt-6-luna`), prices, `app.UseWebSockets()`; README on Web sockets in App Service.
- Showcase Chat page: "Model for calls" (Realtime 2.1 mini, Realtime 2.1, GPT-Live) and "Time limit per call".
- Tests: `LiveRelayTests` (14), three more `RealtimeTests`, `LiveSessionTests` (16).
- Docs: `voice.md` (time limit, GPT-Live), `ai-server.md` (models, `maxMinutes`, GPT-Live through the relay), the Voice and Server.AI READMEs, the plan.

## Decisions

- The relay is for Live only; Realtime keeps the token. See the plan's decisions of 2026-10-20.
- The app speaks Live's own events with the relay, so a client token from OpenAI later would need only a different URL.
- A wrong caller does not spend a ticket: a test caught that the first version burned it.
- Silence detection by peak ≤ 32, measured against OpenAI: 151 of 223 pieces were exact zeros, 16 had a peak of 8 or less, and speech with its fades peaked above 32.

## Verified

Against OpenAI on 2026-10-20, the sample server on the Mac (`localhost:5199`), Swedish speech from macOS's Alva streamed at real time:

| Feature | What happened | Cost |
|---|---|---|
| VoiceLive (`gpt-live-1`, backend `gpt-6-luna`) | "Hej! Vad är klockan just nu?" → "Jag kollar det" → backend called `show_time` in the app → "Klockan är 12:34". "Tack, hej då!" → `end_conversation` → "Hej då!" → ended with `Goodbye`. States Listening/Thinking/Speaking followed. | 23 s: voice $0.0192, backend $0.0003 (4,072 tokens in, 50 out), settled from a 1-minute reservation |
| Voice (`gpt-realtime-2.1-mini`, `reasoning: low`) | `session.created` echoed `reasoning: {effort: low}`; the same questions, `show_time` in the app, a barge-in, and `Goodbye` | about $0.006 for 25 s (1,599 tokens in, 313 out) |

Both ran through the real `VoiceSession` with the fake engine (a scratch test, not committed), and through a raw WebSocket script.

GPT-Live sent its answer at real time: 22.7 s of audio over 23.7 s. Realtime 2.1-mini sent 3 s of answer in 0.2 s.

Not verified: see the plan's verification backlog for 4.6 (a device, Azure App Service, Azure OpenAI's Live endpoint).
