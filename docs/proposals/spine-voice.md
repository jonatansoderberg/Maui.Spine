# Spine.Voice: realtime voice sessions, audio visuals and server-made sessions (proposal)

**Status:** Proposal, 2026-10-09, with the owner's answers in [Decisions](#decisions-2026-10-09). Not started. Roadmap: [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505). Issues: [#515](https://github.com/jonatansoderberg/Maui.Spine/issues/515) (spike: audio engine, §3), [#516](https://github.com/jonatansoderberg/Maui.Spine/issues/516) (provider adapters and `VoiceView`, §4–§5), [#517](https://github.com/jonatansoderberg/Maui.Spine/issues/517) (session endpoints in `Plugin.Maui.Spine.Server.AI`, §6), [#518](https://github.com/jonatansoderberg/Maui.Spine/issues/518) (experiment: Live Activity and ongoing notification, §7). Live visual concepts: https://claude.ai/artifact/J5TCSDk8Rk1vZNti9MBM3J (private until the owner shares it). Nothing here has been built or run; claims about providers come from their documentation, read on 2026-10-09, and are marked where they are not verified.
**Question:** The owner (2026-10-09) wants voice that works out of the box against realtime AI, shows input and output audio, takes visual agents and avatars as plug-ins, is worth trying as a Live Activity, has a server part built on current best practice, and comes as modular packages. What should Spine build, and in which order?
**Answer:** A new package `Plugin.Maui.Spine.Voice` that owns the hard part nobody solves well in MAUI: a full-duplex audio engine with the platform's echo cancellation, a playback queue that can be flushed at barge-in, and metering into a `VoiceFrame` (state, two levels, 24 bands each). On top of it sits `IVoiceSession`, fed by thin provider adapters, because Microsoft.Extensions.AI's `IRealtimeClient` is experimental, has only an OpenAI implementation, and its standard messages carry no speech-started, truncate or cancel. Visuals are plug-ins that see one frame and draw on Skia, or host their own view. The app never holds a provider key: `MapSpineRealtimeSessions()` in `Plugin.Maui.Spine.Server.AI` mints short-lived, server-configured sessions, metered by the budgets in [spine-ai.md](spine-ai.md). WebSocket and PCM first; WebRTC later. The Live Activity is an experiment that rides on Spine.Widgets when both packages are referenced.


## Decisions (2026-10-09)

The owner answered the open questions in §10. Where an answer differs from the text below, the answer wins.

1. **Changed from the proposal:** the `audio` background mode is **off by default**. The app opts in with `SpineVoiceBackground=true`.
2. There is no mode with a provider key in the app.
3. **Changed from the proposal:** only **OpenAI and Azure OpenAI** adapters are in v1. Gemini and Voice Live come when an app needs them.
4. The small generic hook in Widgets for Live Activity actions is accepted.
5. Android uses a plain ongoing notification, not `CallStyle`.
6. WebRTC comes only if latency measurements call for it.
7. **Avatars:** AI avatars and other character visuals (3D, Rive, Lottie) become a separate control library that the owner specifies. Spine.Voice keeps the plug-in contract (`VoiceVisual`, `VoiceVisualView`, `VoiceFrame`) and the simple built-in visuals only.

---

## 1. The conclusion in short

| Question | Answer | Where |
|---|---|---|
| Where is the risk? | **The audio engine.** Echo cancellation decides whether barge-in works at all: without it the provider's VAD hears the model's own voice and interrupts itself. On iOS, `.playAndRecord` with `.voiceChat` does not cancel echo by itself; microphone and playback must go through one `AVAudioEngine` with voice processing on the input node. Spine has no audio code today. | §3, #515 |
| Build on `IRealtimeClient`? | **Yes, as one input, not as Spine's public surface.** It is `[Experimental("MEAI001")]`, its only implementation is OpenAI's, and barge-in needs provider events reached through `RawRepresentation`. Spine's own `IVoiceSession` stays stable while M.E.AI moves. | §4, #516 |
| Which providers? | OpenAI and Azure OpenAI (through `IRealtimeClient` plus raw events), Gemini Live (its own WebSocket protocol), Azure Voice Live (its own protocol). One package per provider SDK. | §4.3 |
| Transport | **WebSocket and PCM16 first.** OpenAI and Azure recommend WebRTC for clients, but there is no maintained libwebrtc binding for MAUI. Gemini Live is WebSocket only. | §4.2 |
| The visual contract | `VoiceVisual.Draw(SKCanvas, SKRect, VoiceFrame)`. The concept's `readonly record struct` with `ReadOnlySpan<float>` fields does not compile; `VoiceFrame` becomes a class reused per tick that exposes spans. | §5.1 |
| Avatars | Built-in Skia avatar driven by levels; 3D and Rive avatars are plug-ins hosting their own view, fed the same frame. Visemes are an optional stream: Azure Speech has them, the realtime APIs do not. | §5.3 |
| Keys | **Never in the app.** Server-negotiated WebRTC (OpenAI, Azure OpenAI) gives the client no credential at all; an OpenAI client secret is short-lived but its config is not a lock; Gemini tokens can lock model and config; Voice Live has no documented ephemeral token, so it needs a relay or the server control channel. | §6, #517 |
| Live Activity | **Worth an experiment.** Starting, updating and ending work today through `ILiveActivityService`. Mute and End buttons need one small hook in Widgets, because Live Activity taps are routed only to a `[Widget]` provider of the same kind. Android uses the microphone foreground service's own notification. | §7, #518 |

---

## 2. What Spine already has

| Part | Where | What it means here |
|---|---|---|
| Skia views with a capped frame loop | `MeshBackground.cs:75` (`IDispatcherTimer`), `:110-111` (Loaded/Unloaded), `:216-221` (window Stopped/Resumed), `:253-276` (Reduce Motion), `:279-313` (timer, clock-based) | `VoiceView` uses the same loop: paused off screen, in the background and under Reduce Motion, driven by the clock rather than a tick count. |
| `SKCanvasView` subclass | `AnimatedLabel.cs:17`, `UseSkiaSharp()` in `AnimatedLabelExtensions.cs:18` | Precedent for raster `SKCanvasView`, not `SKGLView`. SkiaSharp and `SkiaSharp.Views.Maui.Controls` 3.119.2 (`Directory.Packages.props:11-12`). |
| Module registration | `build/<Package>.props` with `<SpineModule>` (e.g. `Plugin.Maui.Spine.Controls.MeshBackground.props`), idempotent `UseXxx()` (`MeshBackgroundExtensions.cs:12-20`), `docs/wiki/packages.md` "Adding a module" | `UseSpineVoice()` registers itself when the package is referenced. |
| Live Activities from C# | `ILiveActivityService.StartAsync(kind, layout, staleAt, channel)` (`Common/Core/ILiveActivityService.cs:54`), `LiveActivity.UpdateAsync` (`:112`), `LiveActivityLayout` regions (`LiveActivityLayout.cs`) | Enough to show and update a conversation. Started only in the foreground (`docs/wiki/widgets.md:658`), which fits: a session is started by a tap. |
| Elapsed time without the app | `W.Relative(date, compact)` (`W.cs:47`), `W.Timer` (`W.cs:38`) | Conversation length on the lock screen with no updates. |
| Buttons that run without opening the app | `W.Button` (`W.cs:69`), `HandleActionAsync` (`SpineWidgetsExtensions.cs:84-110`), #218 | **Gap:** a Live Activity tap reaches only the `[Widget]` provider whose kind equals the activity's; any other kind is logged and dropped (`widgets.md:691`). Android's Live Update has no buttons (`widgets.md:691`, `:928-937`). |
| Cross-package cooperation without a reference | `PushNotificationsRegistered.cs:7` (marker), `IWidgetService`/`ILiveActivityService` resolved through `GetService` | Voice finds `ILiveActivityService` the same way; neither package references the other. |
| Plist and entitlement steps | `<SpineBackgroundMode>` and `<SpineEntitlement>` items written once by `Plugin.Maui.Spine.Common.targets:25-74, 76-93` | The `UIBackgroundModes` merge problem of `spine-background-tasks.md` §2 is fixed (#435): Voice contributes `<SpineBackgroundMode Include="audio" />` like `Plugin.Maui.Spine.BackgroundTasks.targets:26-29`. |
| Privacy strings and Android permissions | Scanner: the app supplies `NSCameraUsageDescription`, the handler checks it and reports a readable failure (`BarcodeScannerViewHandler.Apple.cs:291-293`); `[assembly: UsesPermission(Camera)]` (`BarcodeScannerViewHandler.Android.cs:23`) | Same for `NSMicrophoneUsageDescription` and `RECORD_AUDIO`. |
| Android services | `[Service]` attributes generate manifest entries (`SpineBackgroundJobService.cs:14`, `SpinePushNotificationsMessagingService.cs:14`) | No foreground service exists in Spine yet; Voice adds the first, of type `microphone`. |
| Haptics | `Haptics.IsSupported` / `Haptics.Play` (`Extensions/Haptics.cs:107, 123`) | Haptics live in the core, which Voice does not reference (§4.3), so the app plays them on `StateChanged`. iOS can suppress haptics while recording; the engine sets `setAllowHapticsAndSystemSoundsDuringRecording` so the app's haptics still play (general knowledge, not verified in Spine). |
| Loading and error states | `TaskState` (`Core/TaskState.cs`), `StateView` (`Presentation/StateView.cs`) | Connect failures and "feature off" answers surface as an error state with retry. |
| Server package pattern | `AddSpinePushNotifications` (`Server/ServiceCollectionExtensions.cs:19`), `MapSpinePushNotifications(prefix)` (`EndpointRouteBuilderExtensions.cs:17`), `Authenticate` callback (`SpinePushNotificationsOptions.cs:121-125`, "unset means every request is accepted") | The endpoints in §6 follow the shape, but **refuse anonymous callers by default**; a voice session costs money per minute. |
| Client auth header | `SpinePushNotificationsOptions.AuthorizationHeader` (`PushNotifications/SpinePushNotificationsOptions.cs:101`), read on every call | The same delegate on `SpineVoiceOptions`. |

There is no audio capture, playback or `AVAudioSession` code anywhere in `src/`.

---

## 3. The audio engine (#515)

The engine is one object per session that runs microphone and speaker together, hands the provider PCM16 at its rate, plays what comes back, can flush playback in one call, and meters both directions. It is the part of the package worth building even if every provider changes.

### 3.1 iOS and Mac Catalyst

- **One `AVAudioEngine` for both directions.** `inputNode.SetVoiceProcessingEnabled(true)` before the engine starts; an `AVAudioPlayerNode` into the main mixer for playback. Apple's echo canceller only knows what it is cancelling when the playback goes through the same engine; `.voiceChat` mode on a separate player does not cancel echo. This is the finding the spike must confirm on a device.
- **Session:** `.playAndRecord`, options `DefaultToSpeaker` and `AllowBluetooth` (HFP; A2DP has no microphone). Set once before activation; do not change mode while active. Voice processing has a convergence period at start, so the first second may leak echo: the session stays in `Connecting` until the engine has run briefly. `prefersEchoCancelledInput` exists on `AVAudioSession` in recent SDKs (which version is not verified).
- **Formats:** the input tap delivers the hardware format (often 48 kHz float); `AVAudioConverter` resamples to mono PCM16 at the provider's rate (24 kHz, or 16 kHz in for Gemini) and the reverse for playback.
- **Interruptions and routes:** `AVAudioSession.InterruptionNotification` (a phone call) moves the session to `Muted` and pauses upload; `RouteChangeNotification` restarts the engine on the new route; media-services reset rebuilds it.
- **Background:** `UIBackgroundModes: audio` keeps a session that started in the foreground running when the screen locks (the orange indicator shows). A session cannot be started from the background.
- **Mac Catalyst:** the same engine; voice processing exists on macOS but has not been tried from Catalyst. A sandboxed app needs `com.apple.security.device.audio-input`, contributed as a `<SpineEntitlement>`.

### 3.2 Android

- `AudioRecord` with `AudioSource.VoiceCommunication`, `AudioTrack` with `Usage.VoiceCommunication`, `AudioManager.Mode = InCommunication`, and `AcousticEchoCanceler.Create(sessionId)` and `NoiseSuppressor` when `IsAvailable`. Sharing one audio session id between record and track helps the canceller on some devices. All of this is general knowledge and not verified; quality varies by device, which is why the spike measures it.
- Routes through `AudioManager.SetCommunicationDevice` (API 31+) for speaker, earpiece and Bluetooth.
- **Foreground service, type `microphone`.** Android 14 requires the type and `FOREGROUND_SERVICE_MICROPHONE`; while-in-use permissions mean the service must be started while the app is visible. Its notification is the ongoing notification of §7. Google Play asks for a declaration of foreground service types (not verified for this type).
- Permissions as `[assembly: UsesPermission]`: `RECORD_AUDIO`, `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_MICROPHONE`, `MODIFY_AUDIO_SETTINGS`, `POST_NOTIFICATIONS`.

### 3.3 Windows

`AudioGraph` with `AudioRenderCategory.Communications` and `MediaCategory.Communications` for capture, so the system's communications processing (echo cancellation where the driver offers it) applies. Not verified; Windows is the last platform in the spike.

### 3.4 Shared core (C#, all platforms)

- **Playback queue.** Provider audio arrives in bursts faster than real time. Chunks are queued per response item with a short prebuffer (~100 ms); `Flush()` drops everything queued and stops the player at once. The engine counts **played** samples per item, which gives `audio_end_ms` for OpenAI's truncate (§4.1) and keeps the output meter in step with what the user hears rather than what arrived.
- **Metering.** Input is metered after echo cancellation; output at the moment it is handed to the player. RMS to 0..1 on a dB scale with attack and release smoothing; 24 log-spaced bands (about 80 Hz to 8 kHz) from a 512-point FFT. The FFT is ~60 lines of C#; no package. The audio thread writes into a double buffer, the UI tick reads the latest.
- **Half-duplex fallback.** Where the spike finds echo cancellation too weak (some Android devices, a laptop speaker), `VoiceDuplex.HalfWhileSpeaking` stops uploading microphone audio while the model speaks; the user then interrupts with a tap. This is a setting, not a silent fallback: the session reports which mode it runs in.
- **What the spike measures** on an iPhone and an Android phone, earpiece, speaker and Bluetooth: echo return loss with and without voice processing (play a known speech file, record, compare), mic-to-provider and provider-to-speaker latency, and CPU at 24 kHz with metering on.

---

## 4. Provider adapters and `IVoiceSession` (#516)

### 4.1 What the providers need from a client

| | OpenAI / Azure OpenAI realtime | Gemini Live | Azure Voice Live |
|---|---|---|---|
| Audio in / out | PCM16 24 kHz / 24 kHz (also `pcmu`) | PCM16 LE 16 kHz (`audio/pcm;rate=16000`) / 24 kHz | 16 or 24 kHz |
| Turn detection | `server_vad` (threshold, `prefix_padding_ms`, `silence_duration_ms`) or `semantic_vad` (eagerness); `create_response`, `interrupt_response` | `automaticActivityDetection`, or disabled with `activityStart`/`activityEnd` from the client | `azure_semantic_vad(_multilingual)`, `remove_filler_words`, `auto_truncate`; server noise suppression and echo cancellation |
| Barge-in over WebSocket | Client stops playback and sends `conversation.item.truncate` with `audio_end_ms` | `serverContent.interrupted`: flush playback | Server truncates with `auto_truncate`; client flushes |
| Push-to-talk cancel | `response.cancel` (WS), `output_audio_buffer.clear` (WebRTC) | `activityEnd` | as OpenAI |
| Limits | | Audio-only sessions 15 min; reconnect about every 10 min with `sessionResumption` | |
| Echo reference | | | Live-Reference AEC (API 2026-07-15+) takes stereo: mic on channel 0, playback on channel 1 |

Voice Live's server-side echo cancellation and its Live-Reference input are interesting for Android devices with a weak canceller: the engine already has the played samples, so sending them as channel 1 is cheap. That stays a later option.

### 4.2 Transport

OpenAI and Azure recommend WebRTC for clients: UDP, built-in jitter handling, and server-side truncation at barge-in, with events on the `oai-events` / `voice-live-events` data channel. In .NET for mobile there is no maintained libwebrtc binding, and SIPSorcery (pure C#) appears to lack mobile audio capture (not verified). WebRTC would also bring its own audio stack, which conflicts with the engine in §3. So v1 is **WebSocket with PCM16 from Spine's engine** for every provider, and WebRTC is a later spike once the engine is proven. The server endpoint for WebRTC (§6) can be built first, because it does not depend on the client's stack.

### 4.3 Where Microsoft.Extensions.AI fits

`Microsoft.Extensions.AI.Abstractions` (documentation shows 10.9.0) has `IRealtimeClient.CreateSessionAsync(RealtimeSessionOptions?)` and `IRealtimeClientSession` with `SendAsync(RealtimeClientMessage)` and `GetStreamingResponseAsync()`. Audio goes in as `InputAudioBufferAppendRealtimeClientMessage(DataContent)` plus a commit, and comes out as `OutputTextAudioRealtimeServerMessage` deltas and done. The builder adds `FunctionInvokingRealtimeClient`, logging and OpenTelemetry. All of it is `[Experimental("MEAI001")]`. The only implementation is `OpenAIRealtimeClient` in `Microsoft.Extensions.AI.OpenAI` (also `OPENAI002`), apparently over WebSocket. `GoogleGenAIRealtimeClient` is reported in googleapis/dotnet-genai (not verified). `Azure.AI.VoiceLive` 1.0.0 is a separate SDK with its own types.

The standard messages have no speech-started, truncate or cancel, which barge-in needs. So:

- **Generic adapter in the base package:** any `IRealtimeClient` works out of the box, with barge-in limited to what the standard messages allow (flush on a new response). This is the concept's `UseRealtime(sp => …)`.
- **Provider packages** add the missing events through `RawRepresentation` (OpenAI) or speak the provider's protocol directly over `ClientWebSocket` (Gemini, Voice Live), where an SDK would add weight without solving barge-in.
- Spine suppresses `MEAI001` inside its packages and pins the M.E.AI version; no M.E.AI realtime type appears in `IVoiceSession`, so an M.E.AI change is a Spine patch release, not a break for apps.

| Package | Depends on | Pulls in |
|---|---|---|
| `Plugin.Maui.Spine.Voice` | `.Common`, SkiaSharp.Views.Maui.Controls, Microsoft.Extensions.AI.Abstractions | Engine, `IVoiceSession`, generic `IRealtimeClient` adapter, `VoiceView`, built-in visuals |
| `Plugin.Maui.Spine.Voice.OpenAI` | `.Voice`, Microsoft.Extensions.AI.OpenAI | The OpenAI .NET SDK; covers Azure OpenAI's `/openai/v1/realtime` (OpenAI .NET ≥ 2.9.0) |
| `Plugin.Maui.Spine.Voice.Gemini` | `.Voice` | Nothing (raw WebSocket JSON) |
| `Plugin.Maui.Spine.Voice.AzureVoiceLive` | `.Voice` | Nothing in v1 (raw WebSocket through the relay, §6); `Azure.AI.VoiceLive` only if it earns its place |

The core `Plugin.Maui.Spine` gets nothing. `Voice` does not reference `Plugin.Maui.Spine` either: it works in an app without Spine navigation, like Images.

### 4.4 The session

`IVoiceSession` is the one object a page binds to. It owns the engine and an adapter, runs the state machine `Idle → Connecting → Listening ⇄ Thinking ⇄ Speaking`, with `Interrupted` on barge-in and `Muted` on mute or a system interruption, and exposes the latest `VoiceFrame` on the UI thread. Tools: the server decides which tool declarations a session has (§6); the app implements the ones the server marks as client tools, as `AIFunction`s registered by name. Tools that touch user data run on the server. Transcripts come from the providers' input and output transcription events and are exposed as M.E.AI `ChatMessage`s, so `Plugin.Maui.Spine.Controls.Chat` (#513) can show them **without either package referencing the other**. An interrupted reply is marked as interrupted; whether its text is cut to what was heard depends on what the provider reports (not verified per provider).

---

## 5. `VoiceView` and visual plug-ins (#516)

### 5.1 The frame

The concept page writes the frame as a `readonly record struct` with two `ReadOnlySpan<float>` members. That does not compile: `ReadOnlySpan<T>` is a `ref struct`, it can only be a field of another `ref struct`, and a record struct cannot be one. A `ref struct` frame would compile but could not be stored, captured or passed to a plug-in that hosts its own view and renders on its own thread. `ReadOnlyMemory<float>` in a record struct would compile, but it aliases the same reused buffers while looking like a value, which hides the trap.

So `VoiceFrame` is a **sealed class owned by the session and overwritten on every UI tick**. Its bands are `float[]` inside, exposed as `ReadOnlySpan<float>` properties (allowed on a class). That gives zero allocations at 60 frames per second, spans at the point of use, and one object that both a Skia visual and a hosted view receive. The rule is stated in the type's documentation: read it during the call; call `Snapshot()` to keep a copy. `Draw` takes it by reference, so the `in` of the concept is no longer needed.

### 5.2 The view

`VoiceView` is a `ContentView` holding an `SKCanvasView`, like `MeshBackground`, with the same frame loop and the same pauses. `SKCanvasView` rather than `SKGLView`: the views are small, the existing controls are raster, and `SKGLView` is OpenGL ES on iOS, which Apple has deprecated. If a full-screen orb is too slow on an older Android phone, the frame rate drops before the renderer changes. Default 30 frames per second, 60 settable. Under Reduce Motion the view draws state changes and a slow level, not the spectrum. It announces state changes to screen readers ("Listening", "Speaking") and is one accessible element with the state as its value.

### 5.3 Built-in visuals and plug-ins

| Visual | Name | What it draws |
|---|---|---|
| Orb | `orb` | A fluid blob; cool hue for input, warm for output |
| Duplex bars | `bars` | Output grows up, input grows down; both at once at barge-in |
| Ring | `ring` | A spectrum around a button or a profile picture |
| Avatar | `avatar` | A simple face: blinks, looks at the user while listening, mouth follows the output level |

A plug-in is either a `VoiceVisual` (draws on the canvas) or a `VoiceVisualView` (a `View` that hosts its own renderer, such as a 3D scene or a Rive file, and gets `OnFrame(VoiceFrame)` each tick). Both are registered by name with `AddVisual<T>(name)` and created per view, so each instance can keep its own smoothing and blink timers. A visual knows nothing about provider, transport or microphone.

**Visemes** are optional. Azure Speech's text-to-speech emits viseme ids with audio offsets; the realtime APIs do not. An `IVisemeSource` registered with the session fills `VoiceFrame.Viseme` aligned to played audio (§3.4 counts played samples, so the offsets line up). Without one the frame carries `-1`, and levels plus bands are the base every avatar gets.

**Screen edge** is not a `VoiceVisual`: it is drawn around the window, not inside a view's bounds. It is the Edge kind of `Plugin.Maui.Spine.Controls.Highlight` (#509, [spine-highlight.md](spine-highlight.md)) with its intensity driven by a level. Highlight owns the window-level glow (`IHighlights.AttachEdge(window)`, see spine-highlight.md §7) and takes its intensity as a plain `Func<float>`, so the app wires it in one line (`highlights.AttachEdge(window).Intensity = () => voice.Frame.OutputLevel;`) and neither package references the other. One effect, two packages, no dependency.

---

## 6. Session endpoints in `Plugin.Maui.Spine.Server.AI` (#517)

The server package is proposed in [spine-ai.md](spine-ai.md) (#510): named features in configuration, usage metering, budgets per feature, user and period, and a usage store. Realtime sessions are one more kind of feature there; this section covers only what is specific to voice.

### 6.1 Per provider

| Provider | Route | What the client gets | Can the client change the config? |
|---|---|---|---|
| OpenAI / Azure OpenAI, **WebRTC** (later client) | App posts its SDP offer; the server posts SDP plus session config (multipart) to `/v1/realtime/calls` (Azure: `/openai/v1/realtime/calls`) with the real key and returns the answer | No credential at all | Over the data channel, yes. A **sideband** connection from the server (`wss://api.openai.com/v1/realtime?call_id=…`) sees every event, runs server tools, receives usage, and can revert or end a session whose `session.updated` drifts. One unverified report of a 404 on the sideband URL. |
| OpenAI / Azure OpenAI, **WebSocket** (v1 client) | `POST /v1/realtime/client_secrets` (`expires_after` 10–7200 s, default 600) returns an `ek_` token | A short-lived token | **Yes.** The attached config is a default the client can override, and the TTL only limits starting a session. The server must not treat it as a lock. |
| Gemini Live | `authTokens.create` with `uses: 1`, `newSessionExpireTime` (1 min), `expireTime` (30 min), `liveConnectConstraints` locking model and config | `access_token` | **No**, when the constraints lock the fields. Reconnect with `sessionResumption` about every 10 minutes asks the endpoint again. |
| Azure Voice Live | No ephemeral token documented. Entra (scope `https://ai.azure.com/.default`) or key, on the server only. WebRTC through a server-initiated control channel (`/voice-live/realtime/calls`) relaying SDP; tool calls arrive only there | v1: a WebSocket **relay** on the server | Not through the relay: the server writes `session.update` and drops client attempts. |

The relay needs ASP.NET Core WebSockets; Azure Functions does not host inbound WebSockets (general knowledge, not verified for every plan), so `MapSpineRealtimeRelay` is ASP.NET Core only, unlike the push endpoints.

### 6.2 Best practice the endpoints enforce

1. **No key in the app, ever.** The provider key lives in the server's configuration or managed identity.
2. **Config built only on the server**: model, voice, instructions, VAD, tools and transcription come from `Ai:Features:<Name>:Realtime`; the request body carries the feature name and, for WebRTC, the SDP offer, nothing else.
3. **A signed-in user is required by default.** `Plugin.Maui.Spine.Server.Authentication` (#520) when present, otherwise the app's own authorization policy. Anonymous access needs an explicit `AllowAnonymous()`; this is the opposite of the push endpoints' default.
4. **Per-user rate and concurrency limits**: ASP.NET Core rate limiting partitioned by user id, and one live session per user by default.
5. **Short TTL**: 60 s to start a session (OpenAI `expires_after`, Gemini `newSessionExpireTime`).
6. **Metering by reservation.** On mint, the budget reserves `MaxMinutes × price per minute` from spine-ai.md's price table. With a sideband or relay, the reservation is reconciled from real usage (`response.done` usage on OpenAI). With an OpenAI client secret over WebSocket the server never sees usage, so the reservation is what is charged; Gemini's `expireTime` bounds the session the token can start. A budget that runs out returns the same "feature off" answer the app already handles (spine-ai.md).
7. **Traceability**: provider session or call id logged with the user id; OpenTelemetry spans from M.E.AI where the server uses it.
8. **Server tools on the server**: with a sideband or relay, tools that read user data run there and never reach the app.

---

## 7. Live Activity and ongoing notification (#518)

An experiment, decided afterwards. It runs only when both Voice and Widgets are referenced; Voice resolves `ILiveActivityService` through `GetService` and does nothing without it.

- **iOS.** At connect, Voice starts an activity of kind `spine.voice` with the state as a symbol and text, `W.Relative(startedAt, compact: true)` for the elapsed time (no updates needed), and **Mute** and **End** buttons. It updates on state changes only, plus a coarse level (five bars) at most about once a second. 30 updates per second is not what ActivityKit is for, and whether a level at 1 Hz looks alive or jittery is part of the experiment. Bars are boxes in the tree if the vocabulary can size them, otherwise a stored picture. The activity ends with the session.
- **The buttons need one hook in Widgets.** Taps are routed by kind to a `[Widget]` provider (`widgets.md:691`), and `spine.voice` is not a widget kind; making it one would take one of the nine slots. Proposal: `SpineWidgetsOptions.LiveActivityActions` maps a kind to a handler (`Func<IServiceProvider, string actionId, Task>`), which Voice adds for `spine.voice`. The handler runs in the app's process, which is alive because the audio session keeps it running, so Mute takes effect at once.
- **Android.** The microphone foreground service needs a notification anyway, so that is the surface: state, a chronometer, Mute and End as notification actions handled in-process. Widgets' Live Update is not used, because its template has no buttons. `Notification.CallStyle` (API 31) would give the system's call chip with a hang-up action; whether Google Play accepts it for an AI conversation is unknown, so it is a variant to try, not the default.
- **Not in scope:** starting a session from the lock screen (iOS cannot start one from the background), and push-updated activities (the app is running for the whole session).

---

## 8. Proposed API surface

```csharp
public enum VoiceState { Idle, Connecting, Listening, Thinking, Speaking, Interrupted, Muted }

public enum VoiceDuplex { Full, HalfWhileSpeaking }

/// <summary>One tick of the session, overwritten on the UI thread. Read it during the call; Snapshot() keeps a copy.</summary>
public sealed class VoiceFrame
{
    public const int BandCount = 24;
    public VoiceState State { get; }
    public float InputLevel { get; }      // 0..1, after echo cancellation
    public float OutputLevel { get; }     // 0..1, as played
    public ReadOnlySpan<float> InputBands => _inputBands;
    public ReadOnlySpan<float> OutputBands => _outputBands;
    public int Viseme { get; }            // -1 without an IVisemeSource
    public TimeSpan Time { get; }         // since connect
    public TimeSpan TimeInState { get; }
    public VoiceFrame Snapshot();
}

public abstract class VoiceVisual
{
    public abstract void Draw(SKCanvas canvas, SKRect bounds, VoiceFrame frame);
}

public abstract class VoiceVisualView : ContentView
{
    public abstract void OnFrame(VoiceFrame frame);
}

public interface IVoiceSession : IAsyncDisposable
{
    VoiceState State { get; }
    VoiceFrame Frame { get; }
    VoiceDuplex Duplex { get; }
    bool IsMuted { get; set; }
    IReadOnlyList<ChatMessage> Transcript { get; }
    event Action<VoiceState>? StateChanged;
    event Action? TranscriptChanged;

    Task ConnectAsync(string feature, CancellationToken cancellationToken = default);
    Task EndAsync();
    void Interrupt();                       // a tap cuts the answer off
    void BeginTurn(); void EndTurn();       // push-to-talk
    Task SendTextAsync(string text, CancellationToken cancellationToken = default);
}
```

In the app, with a server from §6:

```csharp
builder.UseSpineVoice(v =>
{
    v.SessionEndpoint = new("https://api.example.com/ai/realtime");
    v.AuthorizationHeader = ct => auth.GetHeaderAsync(ct);
    v.AddVisual<MyMascot>("mascot");
    v.ClientTools.Add(AIFunctionFactory.Create(OpenPage, "open_page"));
    v.LiveActivity = true;                  // only with Spine.Widgets referenced
});
builder.UseSpineVoiceOpenAI();              // or UseSpineVoiceGemini(), UseSpineVoiceAzureVoiceLive()
```

```xml
<spine:VoiceView Session="{Binding Voice}" Visual="orb" HeightRequest="240" />
```

The concept's `UseSpine(o => o.AddVoice(…))` becomes `UseSpineVoice(…)`, in line with every other package (`docs/wiki/packages.md`, Registration). The token endpoint answers with the provider, transport, URL, token, expiry, audio formats and the client tool names; the adapter registered for that provider connects. `IsSupported` is `true` where a microphone exists; permission has its own status.

On the server:

```csharp
builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"));   // spine-ai.md
app.MapSpineRealtimeSessions("/ai/realtime").RequireRateLimiting("per-user");
app.MapSpineRealtimeRelay("/ai/realtime/relay");                         // Voice Live; ASP.NET Core only
```

Build: `<SpineBackgroundMode Include="audio" />` when `SpineVoiceBackground` is `true` (default); `com.apple.security.device.audio-input` for Mac Catalyst; the app supplies `NSMicrophoneUsageDescription`, and the session reports a readable failure naming the key when it is missing, as the Scanner does.

---

## 9. Implementation steps

1. **Engine spike (#515)**, outside the repo first: iOS one-engine voice processing, Android `VOICE_COMMUNICATION` with `AcousticEchoCanceler` and the microphone foreground service. Measure echo, latency and CPU on an iPhone and an Android phone (§3.4). No provider needed. Decide whether `HalfWhileSpeaking` is needed by default anywhere.
2. **Session endpoints (#517)** after spine-ai.md's feature and budget core (#510): OpenAI client secrets, Gemini tokens, the relay for Voice Live; the WebRTC `/calls` route with sideband can follow without waiting for a client.
3. **`IVoiceSession`, OpenAI adapter, `VoiceView`, Orb and Bars (#516)**, WebSocket and PCM, barge-in with truncate. A page in the Showcase against a local server.
4. **Gemini and Voice Live adapters**, Ring and Avatar, `VoiceVisualView`, the transcript in Spine.Controls.Chat.
5. **Live Activity and notification experiment (#518)**, including the Widgets hook for `spine.voice` actions.
6. **WebRTC spike** once the engine is proven.
7. Wiki page, skill, README; the Showcase gallery entry.

---

## 10. Open questions for the owner

1. **Background audio by default?** A conversation that stops when the screen locks feels broken, but the `audio` mode is reviewed by Apple. Proposal: `SpineVoiceBackground=true`.
2. **A key-in-app mode for prototyping?** The roadmap says no provider keys in apps. Proposal: none; the sample ships a three-line server instead.
3. **Provider order.** Proposal: OpenAI and Azure OpenAI first, Gemini second, Voice Live third (it needs the relay).
4. **The Widgets hook** for Live Activity actions by a non-widget kind (§7): acceptable as a small generic addition to Widgets?
5. **Android `CallStyle`** for the ongoing notification, or a plain ongoing notification (§7)?
6. **WebRTC timing.** After v1 ships on WebSocket, or only if latency measurements ask for it?

---

## 11. Sources

Read in the repo: `MeshBackground.cs`, `MeshBackgroundExtensions.cs`, `AnimatedLabel.cs`, `AnimatedLabelExtensions.cs`, `Directory.Packages.props`, `ILiveActivityService.cs`, `LiveActivityLayout.cs`, `W.cs`, `SpineWidgetsExtensions.cs`, `PushNotificationsRegistered.cs`, `Plugin.Maui.Spine.Common.targets`, `Plugin.Maui.Spine.BackgroundTasks.targets`, the Scanner handlers, `SpineBackgroundJobService.cs`, `Haptics.cs`, the server package, `docs/wiki/packages.md`, `docs/wiki/widgets.md`, `docs/proposals/spine-background-tasks.md`, `docs/plans/ai-and-security.md`, and the concept page (https://claude.ai/artifact/J5TCSDk8Rk1vZNti9MBM3J).

- OpenAI, create a realtime client secret: https://developers.openai.com/api/docs/api-reference/realtime-sessions/create-realtime-client-secret
- OpenAI, realtime over WebRTC: https://developers.openai.com/api/docs/guides/realtime-webrtc
- OpenAI, server controls and sideband: https://developers.openai.com/api/docs/guides/realtime-server-controls
- OpenAI, voice activity detection: https://developers.openai.com/api/docs/guides/realtime-vad
- Microsoft, Voice Live how-to: https://learn.microsoft.com/en-us/azure/ai-services/speech-service/voice-live-how-to
- Microsoft, Voice Live over WebRTC: https://learn.microsoft.com/en-us/azure/ai-services/speech-service/voice-live-webrtc
- Google, Gemini Live ephemeral tokens: https://ai.google.dev/gemini-api/docs/live-api/ephemeral-tokens
- Google, Gemini Live guide: https://ai.google.dev/gemini-api/docs/live-guide
- Microsoft, `IRealtimeClientSession`: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.irealtimeclientsession
- `OpenAIRealtimeClient` source: https://source.dot.net/Microsoft.Extensions.AI.OpenAI/OpenAIRealtimeClient.cs.html
- Android 14, foreground service types required: https://developer.android.google.cn/about/versions/14/changes/fgs-types-required
