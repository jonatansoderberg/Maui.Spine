# Spine.Voice and GPT-Live: a second protocol next to Realtime? (spike report)

**Status:** Spike, 2026-10-20. Research only: nothing was built or run against `gpt-live-1`. Claims about OpenAI come from developers.openai.com, read on 2026-10-20 (the markdown versions of the pages), and from the developer forum, which is marked as such. Spine's side was read on `origin/master` and on the open branches of #632 (PR #633) and #634 (PR #635). Roadmap: [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505); voice design: [spine-voice.md](spine-voice.md) §4 and §6.
**Question:** The owner asked (2026-10-20) how GPT-Live differs from the Realtime API, and whether `Plugin.Maui.Spine.Voice` and `Plugin.Maui.Spine.Server.AI` should support GPT-Live (`gpt-live-1`, `v1/live/sessions`) next to Realtime (`v1/realtime`, the `gpt-realtime` family).
**Answer:** **Wait.** GPT-Live is a different protocol, not a new Realtime model, and it has **no client credential**: no client secret, no ephemeral token. A session starts either from the server over a WebSocket with the project key, or over WebRTC/SIP with an SDP exchange the server makes. Spine's design since #517 (the app opens the provider's WebSocket with a short-lived token, no relay) cannot carry it. Supporting it means one of two things the owner has decided against so far: a WebSocket **relay** on the server (ASP.NET Core only, not Azure Functions), or a **WebRTC** client in MAUI. Spine's audio engine itself fits: PCM16 at 24 kHz over a WebSocket is the default format, and OpenAI expects the client to cancel echo. **Do now instead:** move off `gpt-realtime` and `gpt-realtime-mini`, which OpenAI removes on **2027-01-20**, to `gpt-realtime-2.1` and `gpt-realtime-2.1-mini` (same prices). If the owner wants GPT-Live soon, the relay is the smaller path, and it would also give Realtime a locked configuration and real usage (§6).

---

## 1. The difference in short

| | Realtime API (`gpt-realtime-2.1`, `-2.1-mini`) | GPT-Live (`gpt-live-1`) |
|---|---|---|
| What the model is | One speech-to-speech model that talks **and** calls tools | A voice front end that talks; reasoning and tools are **delegated** to a backend (a Responses model, or the app's own) |
| Turns | Half duplex: VAD (`server_vad`, `semantic_vad`) or manual commits decide when a response starts | Full duplex: listens while it speaks, backchannels ("mm-hmm"), decides itself when to speak. No commits, no `response.create` for speech |
| Endpoint | `wss://…/v1/realtime?model=…`, `POST /v1/realtime/calls` (WebRTC), `POST /v1/realtime/client_secrets` | `wss://…/v1/live/sessions` (WebSocket, server only), `POST /v1/live/sessions` (WebRTC or SIP), `…/{id}/attach` (sideband), `…/{id}/fork` |
| Client credential | `ek_` client secret, TTL 10–7200 s | **None.** The key stays on the server; WebRTC clients get an SDP answer only |
| Price | Per token: 2.1 audio $32 in / $64 out per 1M; 2.1-mini $10 / $20 | **$0.05 per minute**, billed per second from start to close (silence included), **plus** the backend's tokens and tools |
| Rate limit | RPM/TPM per tier | Concurrent sessions: Build 50, Launch 300, Grow 500; Free tier not supported |
| Transcripts | Input transcription is a separate model you configure; items with ids, `…completed` events | Native: `session.input_transcript.delta` and `session.output_transcript.delta` with `start_ms`/`end_ms`, no item ids, no "turn done" |
| Published results | Turn-taking 1.41 s (OpenAI's announcement, `gpt-realtime-2.1`) | Turn-taking 0.80 s; Tau3 83.6 % first attempt with a GPT-6 Astra backend vs 45.7 % for `gpt-realtime-2.1` |

## 2. The protocol (question 1)

### 2.1 Creating a session

- **WebSocket:** connect to `wss://api.openai.com/v1/live/sessions` with `Authorization: Bearer $OPENAI_API_KEY`, send `session.start` with `model`, `instructions`, `audio.format`, `audio.output.voice`, `delegation`, optional `input` (prior messages) and `store`, then wait for `session.started`. Both the guide and the reference say: "Authenticate from your backend with your OpenAI API key … Keep the key on your server." Azure's Foundry has `gpt-live-1` too, at `wss://<resource>.openai.azure.com/openai/v1/live/sessions`, with a key or an Entra token; also server side.
- **WebRTC:** the client makes an SDP offer, the server posts `{ session, transport: { type: "webrtc", sdp } }` to `POST /v1/live/sessions` and returns the answer. The client never holds a credential. Creating the session bills 15 s up front, credited against the running session.
- **No client secret.** The create reference lists only `webrtc` and `sip` as transports; there is no `client_secrets` route for Live and no WebSocket token. This is the decisive difference for Spine: `VoiceSession` today opens the provider's WebSocket with the server's `ek_` token (`AIRealtime.cs`, `VoiceTokenClient.cs`), and that path does not exist for Live.
- **A real lock.** Unlike a Realtime client secret, a WebRTC Live session can restrict its untrusted front end: `session.client.data_channel.allowed_client_events` and `allowed_server_events` (startup only). Model, instructions, voice, audio and delegation mode are immutable after start anyway; `session.update` only changes `delegation.responses` settings.

### 2.2 Events that differ

| Spine uses today (Realtime) | GPT-Live |
|---|---|
| `input_audio_buffer.append` | `session.input_audio.append` (same base64 PCM16) |
| `input_audio_buffer.speech_started` → barge-in | **None.** The model yields by itself |
| `conversation.item.truncate` with the played ms | **None** |
| `response.cancel` | **None** for speech. Live's `response.create` only continues backend work |
| `input_audio_buffer.clear` / `.commit` + `response.create` (push-to-talk) | **None.** `session.input_audio.mute` / `.unmute` (muting does not stop speech or backend work) |
| `response.output_audio.delta` with `item_id`, `…audio.done` | `session.output_audio.delta`: no item id, no timing, **no done event** |
| `…input_audio_transcription.completed`, `…output_audio_transcript.done` | Transcript deltas with timestamps only; the app groups them |
| `conversation.item.create` (text, tool output) | `session.thinking.append` / `.commentary.append` / `.instructions.append` (≤500 tokens each); `response.item.create` for tool results |
| `session.update` with tools | Tools live in `delegation.responses.tools`; calls arrive nested in `response.event` |
| `response.done` usage | `session.usage.updated` (`usage.seconds`, cumulative) and `session.closed` (final usage, `reason`) |

The forum report that `session.update`, `response.cancel` and `input_audio_buffer.clear` are missing over the data channel (fred5, 2026-09-16, in the announcement thread) matches the reference: the client events are `session.start`, `session.update`, `session.input_audio.append`/`mute`/`unmute`, `session.output_audio.fixture.play`, `session.instructions`/`thinking`/`commentary.append`, `decision.*`, `response.item.create`, `response.create` and `session.close`. There is no cancel or truncate at all, and `session.update` is rejected for anything outside `delegation.responses`.

### 2.3 Barge-in, full duplex and Spine's engine

- **Echo:** OpenAI's support answer (forum, 2026-10-08) is to plan for local echo cancellation; the session schema has no `noise_reduction` either. Spine's engine (one `AVAudioEngine` with voice processing on Apple, #515) is exactly that, so the engine is an asset here, not an obstacle. Full duplex makes echo matter more than in Realtime: the model hears the uplink continuously, not just at turn boundaries.
- **Format:** `{"type":"audio/pcm","rate":24000}` is the default, mono PCM16 LE, one format for both directions. Spine's engine runs at 24 kHz both ways. No change.
- **Barge-in is the open risk.** Over a WebSocket the client receives audio deltas with no timing and no speech-started signal, so when the model yields, audio already queued in Spine's `PlaybackQueue` keeps playing. The client has to detect the user's speech itself (the engine's input level after echo cancellation while the answer plays, or input transcript deltas arriving during playback) and flush. Whether Live paces WebSocket audio at real time, which would keep the queue short, is not documented (not verified). Over WebRTC the server owns the jitter buffer, so this problem does not arise.
- **Transcripts** suit `ChatView` less well: both speakers' captions grow at once and nothing marks a finished turn, so `VoiceSession`'s one-message-per-item model becomes grouping by speaker and gap (the forum uses ~1.5 s).
- **Push-to-talk** (`BeginTurn`/`EndTurn`) has no equivalent; it becomes mute/unmute.
- **Goodbye:** Spine's `end_conversation` client tool depends on the model calling a tool. In Live only the backend calls tools, and several forum threads (2026-09-17 to 10-15) report the voice model saying goodbye without delegating, intermittently, even with OpenAI's advice applied. `EndsOnGoodbye` would be unreliable.

**Can Spine's own audio engine drive it?** Technically yes: PCM16 24 kHz over a WebSocket with Spine's echo cancellation is what the WebSocket guide describes. The blocker is that only a holder of the key may open that WebSocket.

## 3. Delegation and Spine's functions (question 2)

- **Responses delegation:** OpenAI runs a Responses model (`delegation.responses`: model, instructions, tools, `tool_choice`, `parallel_tool_calls`, reasoning, `service_tier`) and feeds results back to the voice. Hosted tools (web search, file search, Code Interpreter, MCP) run at OpenAI. **Custom function calls still come back to whoever holds the connection**: nested `response.output_item.done` in a `response.event`, answered with `response.item.create` and then `response.create`. So delegation alone does not move Spine's server functions onto the server: the app would still forward them, as since #632, unless the server holds a connection (a relay, or a sideband, which exists for WebRTC and SIP only).
- **MCP is the exception.** A Responses backend calls a remote MCP server itself, with `authorization` and `headers` the session supplies, and Live does not echo those values to the client. If `Server.AI` exposed a feature's `SpineAIFunction`s as an MCP endpoint and minted a per-session credential bound to the caller, OpenAI would call the server directly: the server would see every call with its caller, the app would forward nothing. Usage still would not reach the server. This needs an MCP server package and per-session credentials (not verified end to end).
- **Client delegation:** GPT-Live emits `session.delegation.created` (an id, no task text); the app builds the request from the transcript, runs any backend and returns the result with `session.commentary.append` (or `thinking.append`) and the delegation id. For Spine this has an attractive shape: the backend can be the server's existing exposed **chat feature** with its functions, budgets, rates and meter (#510, #632). The server then sees the caller and every token of the backend exactly. The cost is a second round trip and that the app owns the conversation context.
- **What the server sees:** without a connection of its own, the caller (from the session request or the chat/function calls) and nothing of the voice minutes. With a relay or a sideband: `session.usage.updated`/`session.closed` seconds, nested `response.completed` usage for a Responses backend, and every transcript and tool call.

## 4. Cost and the reservation (question 3)

- **Voice:** $0.05/min, per second, from start through close, silence and backend waiting included. A 10-minute call is $0.50 before the backend. For comparison, the sample server prices `gpt-realtime-mini` at `PerMinute: 0.05` and `gpt-realtime` at `0.2`: GPT-Live's voice layer costs what Spine already estimates for the mini model, but it is a floor, not an estimate.
- **Backend:** tokens at the chosen model's rates (for example `gpt-6-luna` $0.10 in / $0.50 out per 1M), billed separately and unbounded per minute: a talkative session that delegates often costs more.
- **Mapping onto `MaxMinutes × PerMinute`:** the voice layer maps exactly (`PerMinute: 0.05`, and `MaxMinutes` is the true worst case if the session is closed on time). The backend does not: either `PerMinute` gets a margin for it (a guess), or the backend is metered on its own. With client delegation through the chat feature it is metered exactly by the existing meter.
- **Real usage becomes visible only with a connection.** A relay sees `usage.seconds` and can reconcile the reservation to the second, which today's token design cannot do for Realtime either (spine-voice.md §6.2.6). Ending on time also needs the connection: `session.close`, then wait for `session.closed` (OpenAI's examples wait up to 15 s) to get final usage; a dropped socket leaves usage unconfirmed.
- Live recommends closing idle sessions and resuming with `input` or a fork; Spine's `MaxMinutes` timer and a "Resume conversation" flow would fit that.

## 5. Limits and gotchas (question 4)

- **Concurrency:** 50 / 300 / 500 sessions at Build / Launch / Grow; the Free tier cannot use the model. One live session per user (spine-voice §6.2.4) keeps this far away for Spine's apps.
- **No client credential** (§2.1). OpenAI's quickstarts and the one mobile SDK they list (Switchboard's, React Native, iOS) all use WebRTC with a server.
- **Immutable after start:** model, instructions (append only), voice, audio format, delegation mode, `store`.
- **Missing commands:** no cancel, truncate, clear or commit (§2.2); no output-audio-done; no input noise reduction setting.
- **Unreliable steering (forum):** missed delegations for goodbye, hang-up and cancel; `session.instructions.append` interrupts speech, `commentary.append` is sometimes not spoken; semi-rigid flows are harder than with Realtime.
- **Voices:** the default `marin` (and `cedar`, per a forum tester) plus 21 Live voices in Portuguese, German, French, Hindi, Japanese and Korean, **none Swedish**. A French tester reports accents drifting during a call. Spine's owner and the Showcase's reference speech are Swedish: test that before anything else.
- **Context:** 128k tokens, summarised in the background; above 90 % a replacement engine continues with 8,192 tokens of history.
- **History at start:** `session.input`, up to 128 messages and 8,192 tokens, at startup only. That fits #634 (a call continuing a typed chat) better than Realtime's `conversation.item.create`, but over a relay the history has to reach the server before `session.start`.
- **Moderation** can cut the current speech with an `error`, or end the session (`reason: content`). Live audio carries a SynthID watermark (OpenAI, 2026-07-31).
- **WebRTC data channel** messages are limited (one tester measured 256 KB `maxMessageSize`).

## 6. What adopting it would take

Spine's types already leave room: `AIRealtimeSession.Protocol` exists, and `VoiceSession` refuses any protocol but `openai` with a clear message. A Live protocol would be `openai-live`.

### 6.1 Path A: a relay (recommended if GPT-Live is wanted)

**Server (`Plugin.Maui.Spine.Server.AI`):**
1. `"Realtime": { "Protocol": "live", … }` on a `Kind: Realtime` feature, with the backend under it (`Delegation`: `responses` with model, instructions and the feature's `Functions`, or `client`).
2. `POST /ai/realtime/{feature}` keeps its shape: it checks the caller, rates and budget as today, reserves `MaxMinutes × 0.05` (plus a backend margin), and answers `{ protocol: "openai-live", url: "wss://<server>/ai/realtime/{feature}/socket", token: <one-time ticket, 60 s> }`. The app's flow does not change.
3. `MapSpineRealtimeRelay` (ASP.NET Core only): accepts the app's WebSocket with the ticket, opens `wss://api.openai.com/v1/live/sessions` (Azure: `…/openai/v1/live/sessions`) with the key, sends `session.start` built only from configuration (plus the app's chat history as `input` user/assistant text), forwards from the app **only** `session.input_audio.append`, `mute`, `unmute` and `session.close`, runs server functions itself with the caller's `SpineAIFunctionContext`, forwards device functions to the app, ends the session at `MaxMinutes`, and reconciles the reservation from `session.closed` and the backend's `response.completed`.
4. Bandwidth: 24 kHz PCM16 is 48 KB/s, about 64 KB/s base64, each way through the server (~38 MB each way for 10 minutes). App Service's Free tier also caps concurrent WebSockets (general knowledge, not verified; the Showcase server is on F1).

**Client (`Plugin.Maui.Spine.Voice`):** split `VoiceSession`'s event handling into a protocol adapter (`openai` stays as it is) and add `openai-live`: audio append and deltas (one synthetic item per stretch of speech), barge-in by the app's own detection and a flush only, transcripts grouped by speaker and gap, mute/unmute for push-to-talk, device tools from nested `response.event`, and a graceful `session.close`. Roughly the size of #516's adapter, plus tests against recorded event streams.

A relay also answers the open problems of the token design for Realtime: a configuration the app cannot change, real usage, server tools without forwarding. The plan already names those as the triggers for a relay (#517 decision, 2026-10-15).

### 6.2 Path B: WebRTC

Server: `POST /ai/realtime/{feature}` takes the app's SDP offer, posts it to `/v1/live/sessions` with `client.data_channel.allowed_client_events` limited, returns the answer, and attaches a sideband (`…/{id}/attach`) for server functions and usage; a long-lived outbound socket, so not on Azure Functions either. Client: a maintained libwebrtc binding for iOS, Android and Windows, which MAUI does not have (spine-voice §4.2), and WebRTC's own audio processing beside or instead of Spine's engine. This is the larger path; it is what decision 6 (WebRTC only if latency calls for it) put off.

### 6.3 Not an option

Opening `v1/live/sessions` from the app with a key, or a key-in-app mode: decision 2 of 2026-10-09.

## 7. Realtime models now (question 5)

- Current: `gpt-realtime-2.1` and `gpt-realtime-2.1-mini` ("distilled reasoning model … improves alphanumeric recognition over GPT-Realtime-2"), plus `gpt-realtime-2` and `gpt-realtime-1.5`.
- **Deprecated:** `gpt-realtime` → `gpt-realtime-2.1` and `gpt-realtime-mini` → `gpt-realtime-2.1-mini`, removed **2027-01-20** (notice of 2026-07-20).
- `gpt-realtime-2.1-mini` costs the same as `gpt-realtime-mini` (audio $10 / $0.30 cached / $20; text $0.60 / $2.40) with a 128k context instead of 32k and 32k output tokens instead of 4,096. `gpt-realtime-2.1` costs as `gpt-realtime` for audio, text output $24 instead of $16.
- **Recommendation ([#636](https://github.com/jonatansoderberg/Maui.Spine/issues/636)):** move the sample server's `Voice` feature from `gpt-realtime-mini` to `gpt-realtime-2.1-mini`, add its `PerMinute` price, and change the wiki's and tests' `gpt-realtime` examples to `gpt-realtime-2.1`. Both are reasoning models with configurable effort; Spine's session body sets no reasoning, so the default applies. Check on the phone that latency and barge-in stay as measured on 2026-10-15 before calling it done.

## 8. Open questions for the owner

1. **Relay or wait?** GPT-Live needs a server connection Spine does not have. Build a relay now (ASP.NET Core only; it would also lock Realtime sessions and give real usage), or wait until OpenAI offers a client credential for Live or an app needs it?
2. **Model bump:** answered 2026-10-20: its own issue, [#636](https://github.com/jonatansoderberg/Maui.Spine/issues/636).
3. **If GPT-Live comes, which backend?** Client delegation through the existing chat feature (exact metering, functions as today), or Responses delegation (less code, usage seen only through the connection), possibly with Spine's server functions as an MCP endpoint?
4. **Swedish:** no Live voice is listed for Swedish. Worth a 10-minute test of `marin` in Swedish in OpenAI's Live Console before any work?
5. **Issues:** [#636](https://github.com/jonatansoderberg/Maui.Spine/issues/636) for the model bump. Only if question 1 is yes: "Spike: GPT-Live through a relay in Server.AI".

## 9. Sources

Read in the repo: `src/Plugin.Maui.Spine.Voice/Core/VoiceSession.cs`, `VoiceTokenClient.cs`, `src/Plugin.Maui.Spine.Server.AI/AIRealtime.cs` (master and the #632/#634 branches), `docs/wiki/voice.md`, `docs/wiki/ai-server.md` (Realtime voice sessions, Functions in a voice session), `docs/plans/ai-and-security.md` (#517 decisions), [spine-voice.md](spine-voice.md) §4 and §6, the sample server's `appsettings.*.json` on the #634 branch.

- OpenAI, GPT-Live 1 model page: https://developers.openai.com/api/docs/models/gpt-live-1
- OpenAI, Getting started with GPT-Live: https://developers.openai.com/api/docs/guides/live
- OpenAI, Prompting GPT-Live: https://developers.openai.com/api/docs/guides/live-prompting
- OpenAI, Delegation and tools: https://developers.openai.com/api/docs/guides/live-delegation
- OpenAI, Managing GPT-Live sessions: https://developers.openai.com/api/docs/guides/live-conversations
- OpenAI, Migrate to GPT-Live: https://developers.openai.com/api/docs/guides/live-migration
- OpenAI, WebSockets (GPT-Live tab): https://developers.openai.com/api/docs/guides/voice-websockets?api=live
- OpenAI, WebRTC (GPT-Live tab): https://developers.openai.com/api/docs/guides/voice-webrtc?api=live
- OpenAI, Server-side controls (sideband): https://developers.openai.com/api/docs/guides/voice-server-controls?api=live
- OpenAI, Cost optimization (GPT-Live): https://developers.openai.com/api/docs/guides/voice-latency-cost?api=live
- OpenAI, Live session create reference: https://developers.openai.com/api/reference/resources/live/methods/create
- OpenAI, Live primary WebSocket reference: https://developers.openai.com/api/reference/resources/live/primary-websocket
- OpenAI, Pricing: https://developers.openai.com/api/docs/pricing
- OpenAI, gpt-realtime-2.1-mini: https://developers.openai.com/api/docs/models/gpt-realtime-2.1-mini
- OpenAI, Deprecations: https://developers.openai.com/api/docs/deprecations
- OpenAI Developer Community, "Introducing GPT-Live-1 in the API" (2026-09-10, replies to 10-06): https://community.openai.com/t/introducing-gpt-live-1-in-the-api/1396471
- Forum, echo handling: https://community.openai.com/t/1387031 and https://community.openai.com/t/1403507
- Forum, missed delegations: https://community.openai.com/t/1398585
- Microsoft, Use GPT-Live in Foundry: https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/gpt-live
