# Issue #618 — Spine.Server.AI: Speech feature kind with visemes

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/618
**Branch:** issue/618-spineserverai-speech-feature-kind-with-visemes
**Status:** In Progress
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

## Decisions
- **Azure Speech over its WebSocket.** Its REST endpoint returns audio only; visemes come only through the SDKs. The owner pointed at the source: Microsoft's JavaScript Speech SDK is open source (MIT) and shows the protocol. Connect to `wss://{region}.tts.speech.microsoft.com/tts/cognitiveservices/websocket/v1` with `Ocp-Apim-Subscription-Key` and `X-ConnectionId`. Send `speech.config`, `synthesis.context` (`metadataOptions.visemeEnabled`, `outputFormat: raw-24khz-16bit-mono-pcm`) and `ssml`, each a text message of headers (`Path`, `X-RequestId`, `X-Timestamp`, `Content-Type`), a blank line and the body. The service answers `turn.start`, binary `audio` (a big-endian 16-bit header length, the headers, the audio), `audio.metadata` (`{ Metadata: [{ Type: "Viseme", Data: { Offset, VisemeId } }] }`, offsets in 100 ns ticks) and `turn.end`. So the native SDK is not needed, and no package of its own either.
- **JSON with base64 audio**, one round trip with the visemes beside the audio; a third more bytes than binary, simple to cache and to read in any client.
- **Mapped by `MapSpineAI`**, unlike realtime's opt-in route: a speech call is priced per character like a chat call, and the feature still needs `Expose: true`.
- **Callers `Any` by default**, as for chat.
- **Not `[Experimental]`**: the shape follows Realtime's, which has shipped; what is unverified is a real provider.

## Not verified (NotMeasured)
- Azure Speech against the real service, and OpenAI's `audio/speech` with a real key: the push sample server has no AI keys.
