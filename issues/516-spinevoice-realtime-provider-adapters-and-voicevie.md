# Issue #516 — Spine.Voice: realtime provider adapters and VoiceView with visual plug-ins

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/516
**Branch:** issue/516-spinevoice-realtime-provider-adapters-and-voicevie
**Status:** In Review

Step 4.3a of `docs/plans/ai-and-security.md`. Proposal: `docs/proposals/spine-voice.md` §4.

It builds on:
- the audio engine (#515, `IVoiceAudio`)
- the session endpoint (#517, `POST /ai/realtime/{name}`, which returns `{ protocol, url, token, expiresAt, maxMinutes }`)

`VoiceView` and the visuals were split out by the owner into #567 (step 4.3b).

## Plan

### `IVoiceSession` in `Plugin.Maui.Spine.Voice`

It is the one object a page binds to. It owns an engine (`IVoiceAudio`) and a protocol adapter.

- **Members:**
  - `ConnectAsync(feature)`, `EndAsync()`
  - `IsMuted`, `Duplex`, `Interrupt()` (a tap cuts the answer off)
  - `BeginTurn()` and `EndTurn()` for push-to-talk
  - `SendTextAsync(text)`
  - `UpdateFrame()`, which delegates to the engine and puts the session's state in the frame
- **The state machine:** `Idle → Connecting → Listening ⇄ Thinking ⇄ Speaking`, `Interrupted` on barge-in, `Muted` on mute or a phone call. It is raised through `StateChanged`.
- **The transcript:** an observable `IList<ChatMessage>` (M.E.AI), updated on the main thread. `Plugin.Maui.Spine.Controls.Chat`'s `ChatView.Messages` can bind to it without either package referencing the other.
  - The user's turns come from the input transcription, and the assistant's from the output transcript, streamed.
  - An answer cut off by barge-in is marked `interrupted` in its `AdditionalProperties`.
- **Ending:** the session ends itself after the server's `maxMinutes`, because the server cannot (#517). `Ended` says why: the user, the time, the provider, or a failure.
- **Errors:** they are readable exceptions and a `Failed` event.
  - A refused token reuses Spine.AI's answers: 403 `signInRequired`, 404 `off`, 429 with `Retry-After`. They come as `AIFeatureOffException` and `AIRateLimitedException` from `.Common`, the same shape the AI client throws.
  - A socket that closes ends the session with the reason.

### Getting the token

`UseSpineVoice(o => …)` gets `SpineVoiceOptions`:
- `SessionEndpoint`: the prefix given to `MapSpineRealtimeSessions`, such as `https://api.example.com/ai`.
- `AuthorizationHeader`, read on every call, as in Spine.AI.
- `HttpHandler` (for tests).
- `ClientTools`: `AIFunction`s.

The token request carries the installation id header. It is the same `SecureStorage` key the AI client and push use, `SpineInstallation.IdKey` in `.Common`.

### The OpenAI adapter (protocol `openai`, which covers Azure OpenAI too)

A small adapter over `ClientWebSocket` with the provider's JSON events, decided by the owner on 2026-10-15. It has no SDK dependency and works around M.E.AI's experimental `IRealtimeClient`.
- **Connecting:**
  - It opens `url` with `Authorization: Bearer {token}`.
  - The session's configuration is the server's.
  - The client sends `session.update` only to add its client tools.
- **The microphone:** the engine's PCM16 at 24 kHz goes to `input_audio_buffer.append`, base64, in frames of about 100 ms. A sender loop keeps the audio thread free of the socket.
- **The answer:** `response.output_audio.delta` (or the beta `response.audio.delta`) is queued per `item_id`, and `…audio.done` marks the item complete.
- **Barge-in:** on `input_audio_buffer.speech_started` while an answer plays:
  - the engine flushes;
  - `conversation.item.truncate` goes out with the item and the played `audio_end_ms`;
  - `response.cancel` goes out while a response runs.
  - `Interrupt()` does the same from a tap.
- **States:**
  - `response.created` means `Thinking`.
  - The first audio means `Speaking`.
  - `Speaking` lasts while the engine still plays, not until `response.done`.
- **Transcripts:**
  - `conversation.item.input_audio_transcription.completed` gives the user's turn.
  - `response.output_audio_transcript.delta/done` (or the beta names) gives the assistant's.
- **Client tools:**
  - `response.function_call_arguments.done` runs the `AIFunction` by name.
  - The result goes back as `conversation.item.create` (`function_call_output`), followed by `response.create`.
  - An unknown tool or a failing one answers with an error output, so the model can say so.
- **Push-to-talk:**
  - `BeginTurn` interrupts any answer and clears the input buffer.
  - `EndTurn` commits it and creates a response. This is meaningful with the feature's `TurnDetection: none`.
- **Text:** `SendTextAsync` sends a user text item, then `response.create`.
- **Provider errors:** `error` events are logged and raised, without the payload's secrets.
- **The core's other protocols:** the adapter sits behind a small `IVoiceProtocol` keyed by the server's `protocol`, so Gemini or Voice Live can come later without changing the session.

### Where the code lives

- The session and the adapter go in `Core/`, which also builds for `net10.0`, behind a tiny socket abstraction. They are then tested without a device or OpenAI.
- `Microsoft.Extensions.AI.Abstractions` is a new dependency, for `ChatMessage` and `AIFunction`. It is already in the repo.

### Tests (`tests/Plugin.Maui.Spine.Voice.Tests`)

They run against a fake socket that scripts OpenAI events, and a fake engine. They cover:
- the token request: the URL, the headers, and 403/404/429 mapped to the AI exceptions
- the audio going out in frames
- answer audio queued per item and completed
- barge-in sending truncate with the played milliseconds and cancel
- the state machine
- transcripts, including an interrupted answer
- a client tool round trip, and an unknown tool
- push-to-talk, text, and `maxMinutes`
- a closed socket

### Showcase

A **Voice chat** page against the push sample server's `Voice` feature (`gpt-realtime`, Swedish, `Callers: Any`):
- Connect and End, Mute, a Stop button that interrupts, the state, the plain level meter from the lab, and the transcript in a `ChatView`.
- One client tool, `show_time`, that answers the time, to show the round trip.
- The iPhone reaches the Mac's server over the LAN: `SessionEndpoint` comes from a setting, and the sample gets ATS `NSAllowsLocalNetworking` if needed.

### Verification

- **Real calls, used sparingly:** a short conversation in the simulator, then barge-in on the iPhone through the loudspeaker, since that is what #515 measured.
- **Cost:** each token reserves `MaxMinutes × PerMinute` (3 × 0.2 USD) in the sample's budget, and real use is billed per minute of audio.

### Docs

- `docs/wiki/voice.md` gets a session section, and the package README is updated.
- The `spine-setup` skill.
- The plan: 4.3 split into 4.3a and 4.3b, Decisions, the backlog, and Last updated.
- The proposal's status line.

## Open Questions

None. The owner decided on 2026-10-15: our own WebSocket adapter, SkiaSharp for the visuals in #567, and two issues.

## Changes

- `Core/` (also `net10.0`):
  - `SpineVoiceOptions`: `SessionEndpoint`, `AuthorizationHeader`, `HttpHandler`, `ClientTools`.
  - `VoiceTokenClient` and `VoiceSessionToken`: 403/404 `off` becomes `AIFeatureOffException`, and 429 becomes `AIRateLimitedException`.
  - `IVoiceSocket` and `ClientVoiceSocket`.
  - `IVoiceSession`, `VoiceSessionEnd` and `VoiceSession`, which speaks OpenAI's realtime events:
    - the microphone in 100 ms frames
    - the answer per item
    - barge-in with truncate and cancel
    - the states and the transcript
    - client tools
    - push-to-talk and text
    - the time limit
- `UseSpineVoice(o => …)` registers `IVoiceSession` (transient, dispatching to the main thread, with the installation id from `SecureStorage`). Common gives Voice its internals for `AIOffReasons`. Voice references Microsoft.Extensions.AI.Abstractions.
- Tests: 18 session tests against a scripted socket and a fake engine. The project has 48, and they pass three times in a row.
- Showcase:
  - **The Voice chat page:** the server address, the meter, Connect, End, Stop the answer, Mute, and the transcript in a `ChatView`.
  - **The sample's setup:** `UseSpineVoice` with the `show_time` tool, `NSLocalNetworkUsageDescription`, and the lab's `VoiceMeterView` taking a session.
- Verified on the owner's iPhone 16 Pro against the push sample server's `Voice` feature (`gpt-realtime`, Swedish) over the LAN:
  - **The conversation:** the token, the socket and a spoken conversation all worked, through Connecting, Listening, Thinking and Speaking.
  - **The tool:** `show_time` was called and answered ("Klockan är 19:30").
  - **Barge-in:** the owner talked over the answer four times; each time it stopped, and the model answered what he said.
  - **The owner's verdict:** "Funkar bra".
  - **The first connect** failed with "The Internet connection appears to be offline" while iOS's local-network prompt was still open; the next connect worked.
- Docs:
  - `docs/wiki/voice.md` gets a voice session section, and the package README.
  - README, `packages.md` and the `spine-setup` skill.
  - The proposal's status line.
  - The plan: 4.1a Done, 4.3 split, Decisions, the backlog.

## Decisions

- **2026-10-15, owner:** The OpenAI realtime protocol is spoken directly over `ClientWebSocket` inside `Plugin.Maui.Spine.Voice`, not through M.E.AI's `IRealtimeClient` and the OpenAI SDK. **Why:** barge-in needs `speech_started`, truncate and cancel, which the experimental standard messages lack, and it adds no dependency.
- **2026-10-15, owner:** `VoiceView` and the visuals draw with SkiaSharp, in the Voice package (#567).
- **2026-10-15, owner:** #516 is the session, the adapter, tools and transcripts with a Showcase page. `VoiceView` and the visuals are #567.

- **2026-10-15, Claude:** No `IVoiceProtocol` abstraction yet: the session checks for `openai` and refuses anything else before the microphone opens. **Why:** one protocol; CLAUDE.md asks for no abstractions for hypothetical futures.
- **2026-10-15, Claude:** The session ends itself after `maxMinutes`, and `Speaking` follows the engine's playback rather than `response.done`.
- **2026-10-15, Claude:** A user turn is added as "…" at `input_audio_buffer.committed` and filled by its transcription, so the transcript keeps the order of speech even though the transcription arrives after the answer starts.
- **2026-10-15, Claude:** Microphone audio is cut into exact 100 ms frames. **Why:** a test sent one second at once, and it went out as a single message.
