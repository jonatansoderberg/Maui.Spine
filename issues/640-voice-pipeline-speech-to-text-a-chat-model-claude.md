# Issue #640 — Voice pipeline: speech-to-text, a chat model (Claude) and text-to-speech as parts of one voice session

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/640
**Branch:** issue/640-voice-pipeline-speech-to-text-a-chat-model-claude
**Base:** `issue/634-chat-and-voice-in-one-conversation-type-or-talk-on` (PR #635, which builds on #633; neither merged on 2026-10-20)
**Plan:** `docs/plans/ai-and-security.md`, step 4.7
**Status:** Completed

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
- **Server.AI:** `Kind: Transcription` (`ai.Transcription`, `AITranscriptionProviders.OpenAI`, `POST /ai/transcribe/{name}` with a WAV body, 413 over `MaxSeconds`, metered by minutes); speech streamed as it is made with `Accept: audio/pcm` (`IAIStreamingSpeechProvider`, OpenAI's `stream_format: audio`, `Spine-Sample-Rate`), a non-streaming provider sending its audio as one piece; `PerMinute` takes fractions of a minute. Tests in `PipelineServerTests`.
- **Speech:** `ISpineSpeechRecognizer` (`SFSpeechRecognizer` with an audio-buffer request, on the device when it can, the usage key checked before iOS would end the app) and `ISpineSpeechRenderer` (`AVSpeechSynthesizer.write(_:toBufferCallback:)` through a block, since .NET for iOS does not bind it); unsupported elsewhere. `UseSpineSpeech` registers both.
- **Voice:** `SpineVoiceOptions.Pipeline(name, …)` and `VoicePipeline` (`AddLocalSpeechToText`, `AddServerSpeechToText`, `AddChat`, `AddLocalTextToSpeech`, `AddServerTextToSpeech`, `Language`, `SpeechThreshold`, `StartOfTurn`, `EndOfTurn`, `MaxTurn`); `PipelineVoiceSession` (level-based turns with a pre-roll, pending turns, fallbacks, the answer cut into sentences and streamed through the engine, barge-in, tool cards); `RoutingVoiceSession` is the registered `IVoiceSession` and picks the pipeline or the realtime session by name, sharing one engine; `VoiceAvatarDriver` reads the engine through `IVoiceAudioOwner`. Tests in `PipelineTests`.
- **Sample server:** features `Claude` (Anthropic, the functions, a prompt for spoken answers), `Transcribe` (`gpt-4o-mini-transcribe`) and `ReadOpenAI` (`gpt-4o-mini-tts`, voice marin) with prices; `ai.Transcription("openai", …)`.
- **Showcase:** pipelines `Claude` (the phone's voice first) and `ClaudeOpenAIVoice` (the server's voice); the Chat page's options choose Realtime, Claude or Claude with OpenAI's voice; `NSSpeechRecognitionUsageDescription`.
- **Merged master** (GPT-Live, #642): pipelines take `MaxMinutes`; the Chat page lists the pipelines with the realtime models under **Model for calls**; the step is 4.7.
- **Seen on the iPhone (2026-10-20):** a Claude call against Azure with on-device recognition (the turn written down 50 ms after it ended), `show_time` on the phone, the phone's voice. Claude Haiku 5.5 answered "Hej då" with no text: it spent its 300 output tokens reasoning; the feature now has `"Reasoning": "None"` and 400 tokens.
- **Timings in the log** (Debug, `Plugin.Maui.Spine.Voice`), and the Showcase writes them to stdout for `devicectl --console`; a chat that answers no text is a warning.
- **Hanging up on goodbye:** the pipeline offers `end_conversation` per answer (`ChatOptions.Tools`); Spine.AI's server client offers a call's own functions as device functions and runs them; the sample server declares it and the Claude feature lists it. The goodbye tool's description and answers are shared with the realtime session, and its farewell is two or three words.
- **Shorter waits:** one reused `AVSpeechSynthesizer` for every render (a new one per sentence took 0.7 s to its first audio), and a 600 ms end of turn in the Showcase.
- **Showcase:** the avatar above the chat (it had no width and never showed; None hides it), the call's length and time left in the status, why a call ended (time limit, provider), and the model for calls remembered.
- **An uninterruptible farewell (owner, 2026-10-20):** once the model hangs up, the microphone is not sent (realtime, GPT-Live) or listened to (pipeline), the provider's input buffer is cleared, and the call ends after the farewell. On the iPhone the user's "Hej då" back had cut the farewell and the model answered it again. The Stop button still keeps the call going.
- **GPT-Live hangs up:** the relay tells the voice to hand goodbyes to its backend when the app's tools include `end_conversation`; it had only said "Hej då!" itself.
- **ChatComposer's AutoFocus runs once per composer:** the keyboard came up on its own when a call ended (the composer was loaded again).
- **2026-10-27, after master was merged:**
  - The voice token request keeps trying a server that is still starting (up to 90 s), and the Chat page says "Waking the server…": the sample server on Azure's free plan took 54 s to start.
  - A page with a footer keeps the bottom of its safe area. Since #657 the chat's composer sat on the home indicator and the screen's rounded corners cut its buttons.
  - GPT-Live's voice never delegated in a call on the iPhone (the backend saw 0 tokens): it answered the time itself and never reached `end_conversation`. The cause was the prompt: it said "lämna över till backend", while OpenAI's GPT-Live prompting guide steers delegation with a "Delegation policy" (backend tools, when to generate `<|SpawnThinking|>` and when not). The relay now writes that policy from the session's tools after the feature's prompt, and the sample's prompt is only the persona. A harness on the Mac (synthesized Swedish speech through the local relay to the real GPT-Live) saw 21 delegations in 21 turns over 7 conversations: `show_time`, `get_my_bookings` and `end_conversation` each time. A first attempt that had the relay pass heard goodbyes to the backend itself was dropped as a workaround (owner).
  - GPT-Live said goodbye twice: the app answered `end_conversation` with "say a goodbye now" although the voice already had. It now counts the voice's audio after the user's last words as the farewell, as with Realtime.
  - `VoiceAvatarDriver` opens the mouth by the answer's level against its recent peak: GPT-Live's quiet voice barely opened it.
- **Tests:** the GPT-Live and pipeline tests read the transcript under the same lock as the session's dispatch ("Collection was modified" now and then).
- **Docs:** voice.md (A pipeline), speech.md (Writing speech down, Rendering to audio), ai-server.md (Streamed speech, Transcription, the price rows), the package READMEs, the spine-setup and spine-controls skills, plan step 4.7.

## Decisions
- **Owner, 2026-10-20:** a named pipeline declared in the app; speech-to-text on the device and on the server in v1; real streaming of the server's speech; Spine.Speech both ways, with the pipeline in Spine.Voice; the device's voice rendered into the voice engine.
- **One registered `IVoiceSession`.** A router picks the session by the name given to `ConnectAsync`, so the Chat page, `ChatComposer` and `VoiceAvatarDriver` need nothing new; settings go to both sessions so they carry over.
- **Turns by level on the client.** There is no provider VAD in a pipeline. The threshold follows the room's noise floor (not learned while the answer plays) and doubles while it plays, against residual echo.
- **The turn's audio is kept** while the device writes it down live, so a failure falls back to the server with the whole turn rather than losing it.
- **Sentences end only where the next word starts one**, so "kl. 18" and "t.ex. en" are not cut, at the cost of waiting for the next token.
- **A chat failure ends the call** with the error, as a realtime provider's does: a silent failure would leave the user talking to nothing. Another chat answers only while nothing has been said.
- **Hanging up goes through the server's device functions.** A pipeline cannot add a tool to a Spine.AI server feature's model directly, so it offers `end_conversation` as the call's own device function; the server offers it only when it declared it and the feature lists it, as for any device function. **Why:** the server owns what its model may call.
- **Earlier function calls are not sent back to the model** (the server drops client tool messages), so Claude took back answers it had checked. The sample's prompt says those calls were made; Spine itself is unchanged.
