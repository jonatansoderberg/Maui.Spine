# Issue #714 — Server: a relay mode for OpenAI's Realtime protocol

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/714
**Branch:** issue/714-realtime-relay
**Status:** Completed

## Plan

Give a `Kind: Realtime` feature the Live relay's benefits: the app connects to the server, which opens the provider's
Realtime socket with its own key, converts the audio, runs server functions, counts the usage and ends the session at
its limit. The app speaks the Realtime API's own events, so Spine.Voice and a device that speaks `openai` need no new
protocol, only the relay's address.

## Changes

- `AIRealtimeOptions.Relay`; the token answers `protocol: "openai-relay"` with the relay's relative url; the relay route
  takes both protocols (`RealtimeRelayAsync`).
- `IAIRealtimeRelayProvider` (`ConnectRealtimeAsync`, `SessionOf`), implemented by the OpenAI provider from the same
  session body as the client secret's.
- `AIRealtimeApiRelay`: the session from the feature, the app's functions as far as the catalogue lists them, an
  allow-list of app events, audio at the app's rate and raw when asked (`spine.audio`), server functions run on the
  server (`spine.function.output`), `response.done` usage counted with audio tokens apart (`audio_input_tokens`,
  `audio_output_tokens` under the price's `Additional`), the reservation taken off at the end, the time limit.
- `AIRelayTools`: the catalogue logic shared by both relays (and the Live relay's helpers made internal).
- Spine.Voice: `openai-relay` connects like Live and speaks like `openai`.
- Tests: `RealtimeRelayTests` (ticket and session, the catalogue, audio both ways, server and app functions, settlement
  with audio prices, the limit); a Voice test for the relayed connection. Docs in `ai-server.md` and `voice.md`.

## Decisions

- No pause markers: the Realtime API delivers an answer faster than real time with its pauses inside the audio.
- `conversation.item.create` passes user and assistant messages (the app's own history) and function results only;
  `response.create` passes without its `response` object, so an app cannot change the instructions or the tools.
- Cached tokens stay one count for text and audio (the providers price them alike for the mini models).
