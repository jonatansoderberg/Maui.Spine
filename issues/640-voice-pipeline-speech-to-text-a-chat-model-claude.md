# Issue #640 — Voice pipeline: speech-to-text, a chat model (Claude) and text-to-speech as parts of one voice session

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/640
**Branch:** issue/640-voice-pipeline-speech-to-text-a-chat-model-claude
**Base:** `issue/634-chat-and-voice-in-one-conversation-type-or-talk-on` (PR #635, which builds on #633; neither merged on 2026-10-20)
**Plan:** `docs/plans/ai-and-security.md`, step 4.6
**Status:** In Progress

## Plan

### Server (Spine.Server.AI)
- `Kind: Transcription`: `ai.Transcription("openai", AITranscriptionProviders.OpenAI)` (`/v1/audio/transcriptions`, e.g. `gpt-4o-transcribe`), `POST /ai/transcribe/{name}` with a WAV body, answering `{ text }`. Metered by audio minutes (`PerMinute`), callers, rates and budgets as speech.
- Streamed speech: `POST /ai/speech/{name}` with `Accept: audio/pcm` answers raw PCM16 as it is made (`Spine-Sample-Rate` header). A provider that streams implements `IAIStreamingSpeechProvider` (OpenAI's `/v1/audio/speech` with `stream_format: audio`); the others send their whole audio as one chunk.

### Spine.Speech (both ways)
- `ISpineSpeechRecognizer`: `Start(options)` gives an `ISpeechRecognition` that takes PCM16 (`Append`), raises `Partial`, and answers the text from `FinishAsync`. Apple: `SFSpeechRecognizer` with an audio-buffer request, on the device when it can. Other platforms: not supported in v1.
- `ISpineSpeechRenderer`: `RenderAsync(text, options)` gives the synthesizer's audio as PCM16 chunks instead of playing it, so a voice session can play it through its own engine (echo cancellation, barge-in, the avatar's levels). Apple: `AVSpeechSynthesizer.WriteUtterance`.

### Spine.Voice
- `SpineVoiceOptions.Pipeline(name, p => p.AddLocalSpeechToText().AddServerSpeechToText("Transcribe").AddChat("Claude").AddLocalTextToSpeech().AddServerTextToSpeech("Read"))`. Parts given more than once form a fallback chain in order.
- `PipelineVoiceSession`: voice detection on the engine's microphone (level with hysteresis) → recognition (with a short pre-roll) → the chat client (a keyed `IChatClient`, so a Spine.AI feature with its functions) streamed → sentences → speech rendered into the engine. Talking over the answer flushes it and cancels the chat. States, transcript (the shared conversation, pending turns), `Prefer`, `SendTextAsync` as the realtime session.
- The registered `IVoiceSession` picks the realtime or the pipeline session per `ConnectAsync(name)`.

### Showcase and sample server
- Server: `Claude` (Anthropic, functions), `Transcribe` (OpenAI), `ReadOpenAI` (OpenAI speech, streamed).
- Showcase: the Chat page's options choose the voice: Realtime (OpenAI) or Claude (pipeline).

## Open Questions

(none: answered by the owner on 2026-10-20; see the issue's Decisions)

## Changes

## Decisions
