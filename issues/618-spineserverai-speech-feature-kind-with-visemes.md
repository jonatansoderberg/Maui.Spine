# Issue #618 — Spine.Server.AI: Speech feature kind with visemes

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/618
**Branch:** issue/618-spineserverai-speech-feature-kind-with-visemes
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), step S3

## Plan
Two PRs.
1. **Server**: `AIFeatureKind.Speech` in Server.AI, following Realtime: a provider interface, `ai.Speech(name, factory)`, the feature's voice options, a price per million characters, `IAIFeatures.SpeechAsync` with the gate and the meter, and `POST /ai/speech/{name}`. Providers: Azure Speech with visemes, OpenAI with audio only.
2. **Client**: the app plays the PCM through Spine.Voice's engine without the microphone (`VoiceAudioOptions.Input = false`), and the Speech driver gets the visemes as TimedVisemes. The Showcase's Avatar detail page offers a server voice in Speak mode, and the sample server gets a demo provider that needs no key.

## Changes
- `AISpeech.cs`: `AISpeechRequest`, `AISpeech` (PCM16 mono, rate, visemes), `AISpeechViseme` (Azure's 22-id alphabet, offset), `IAISpeechProvider`, `AISpeechProviders.OpenAI` (`POST audio/speech`, `response_format: pcm`, 24 kHz) and `AISpeechProviders.AzureSpeech` (the service's WebSocket).
- `AIConfiguration`: `AIFeatureKind.Speech`, `AIFeatureOptions.Speech` (`AISpeechOptions`: `Voice`, `Language`, `Instructions`, `Speed`, `MaxCharacters`), `AIPrice.PerMillionCharacters`.
- `SpineAIOptions.Speech(...)`, the registry's speech branch, `IAIFeatures.SpeechAsync` (413 over `MaxCharacters`, gate, meter by characters), `AIPricing` and `AIUsageMeter` count characters; no keyed `IChatClient` for a speech feature.
- `SpineAIEndpoints.SpeechAsync` and `MapSpineAI` maps `POST {prefix}/speech/{name}`.
- Tests: `SpeechTests` (the endpoint, the feature's voice, pricing, 413, 400, 404, 502 without the provider's message) and `SpeechProviderTests` (OpenAI's request; Azure's protocol against a local WebSocket that answers like the service: three messages sent, audio frames joined, visemes from `audio.metadata` sorted and converted from 100 ns ticks).
- Docs: `docs/wiki/ai-server.md` (Speech section, the Functions handler), the package README, the `spine-setup` skill; the plan.

Client (second PR):
- `VoiceAudioOptions.Input` (default true). Off, the Apple engine asks for no microphone, never touches the input node, uses the playback category in spoken-audio mode, has no voice processing and no convergence, and leaves the speaker override alone.
- `ServerSpeech` (Plugin.Maui.Spine.Voice, core): an `ISpineSpeech` on the shared queue. It sends `POST {SessionEndpoint}/speech/{Feature}` with sign-in and installation id, plays the PCM through the engine at the answer's rate, and takes the utterance's position from the engine's played time. It raises the visemes with their lengths at the start, and spreads words over the audio when there are none. It ends when the engine has played it all, flushes on Stop, raises `Failed` with the reason, and closes the engine five seconds after the last utterance. Registered by `UseSpineVoice` as a transient; `SpineVoiceOptions.SpeechFeature`.
- Speech gives Voice its internals (`InternalsVisibleTo`); Voice references Speech.
- Sample server: `DemoSpeechProvider` (a hum, a viseme per letter, 75 ms each), features `Read` (demo) and `ReadAzure` (Azure, off until a key), prices, a `send.http` request.
- Showcase Avatar detail page: two server voices at the end of the Speak mode's picker (the Speech driver over `ServerSpeech`), status with the viseme count, Spine.Voice in the page's packages, a code line.
- Tests: `ServerSpeechTests` (request, playback without input at the answer's rate, visemes with lengths, position from the engine, completion, stop, a refusal with the reason, a missing feature, words without visemes).
- Docs: `speech.md` (A voice from the server), `voice.md` (playback only), `avatars.md`, the Voice README, the `spine-controls` skill; the plan.

## Decisions
- **Azure Speech over its WebSocket.** Its REST endpoint returns audio only; visemes come only through the SDKs. The owner pointed at the source: Microsoft's JavaScript Speech SDK is open source (MIT) and shows the protocol. Connect to `wss://{region}.tts.speech.microsoft.com/tts/cognitiveservices/websocket/v1` with `Ocp-Apim-Subscription-Key` and `X-ConnectionId`. Send `speech.config`, `synthesis.context` (`metadataOptions.visemeEnabled`, `outputFormat: raw-24khz-16bit-mono-pcm`) and `ssml`, each a text message of headers (`Path`, `X-RequestId`, `X-Timestamp`, `Content-Type`), a blank line and the body. The service answers `turn.start`, binary `audio` (a big-endian 16-bit header length, the headers, the audio), `audio.metadata` (`{ Metadata: [{ Type: "Viseme", Data: { Offset, VisemeId } }] }`, offsets in 100 ns ticks) and `turn.end`. So the native SDK is not needed, and no package of its own either.
- **JSON with base64 audio**, one round trip with the visemes beside the audio; a third more bytes than binary, simple to cache and to read in any client.
- **Mapped by `MapSpineAI`**, unlike realtime's opt-in route: a speech call is priced per character like a chat call, and the feature still needs `Expose: true`.
- **Callers `Any` by default**, as for chat.
- **Not `[Experimental]`**: the shape follows Realtime's, which has shipped; what is unverified is a real provider.

## Verified
- Server: Server.AI tests 87 passed (11 new); Azure's protocol against a local WebSocket stand-in.
- Client: Voice tests 59/59 (5 new); Speech 10/10, Controls.Voice 14/14 unchanged.
- Sample server: `POST /ai/speech/Read` gives 24 kHz PCM and visemes, recorded in `/ai/usage` at 16 per million characters; `ReadAzure` is off with "empty ApiKey" in the startup log.
- iPhone 17 Pro simulator, Showcase Avatar detail page, Nova, Speak with "Server: demo voice": state Speaking, lip sync TimedVisemes, "Completed after 52 visemes (TimedVisemes) in 4.3 s", the call in `/ai/usage`.

## Not verified (NotMeasured)
- The server voice heard on the owner's iPhone (the simulator's audio was not listened to), and with Azure's real visemes in step with Azure's real audio.
- Azure Speech against the real service, and OpenAI's `audio/speech` with a real key: the push sample server has no AI keys.
