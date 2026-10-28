# Voice

```bash
dotnet add package Plugin.Maui.Spine.Voice
dotnet add package Plugin.Maui.Spine.Controls.Avatar  # AvatarView, to draw it
```

A spoken conversation with a realtime model has two parts:

- **The voice session (`IVoiceSession`)** gets a short-lived token from the app's server and connects to the provider (for GPT-Live, it connects to the server's relay instead), cuts the answer off when the user talks over it, keeps a transcript, and runs the functions the model calls: in the app, or on the server with the user's sign-in.
- **The audio engine (`IVoiceAudio`) under it** runs the microphone and the speaker at the same time, with the platform's echo cancellation. Without it the model hears its own answer and interrupts itself.

The provider's key never reaches the app. A conversation with any chat model, such as Claude, is a [pipeline](#a-pipeline-any-chat-model) instead: the user written down, the chat model's answer read aloud, each part on the device or the server. **`AvatarView`** (Spine.Controls.Avatar) draws the conversation, the sound in and out: see [Drawing the conversation](#drawing-the-conversation).

| Platform | Engine | State |
|---|---|---|
| iOS | One `AVAudioEngine` with voice processing | Measured on an iPhone 16 Pro (below) |
| Mac Catalyst | The same engine | Runs; the measurement was limited by the room |
| Android | `VOICE_COMMUNICATION` and `AcousticEchoCanceler` | Coming in #562 |
| Windows | Communications audio category | Coming in #563 |

On Android and Windows the engine says so when started: `VoiceAudioException` with `VoiceAudioProblem.Unavailable`.

## A voice session

The server side is [Realtime voice sessions](ai-server.md#realtime-voice-sessions): a feature with `"Kind": "Realtime"` and `MapSpineRealtimeSessions`. In the app:

```csharp
builder.UseSpineVoice(o =>
{
    o.SessionEndpoint = new Uri("https://api.example.com/ai");      // the prefix given to MapSpineRealtimeSessions
    o.AuthorizationHeader = ct => auth.GetHeaderAsync(ct);           // realtime features answer signed-in callers by default
    o.Functions.Add(SharedFunctions.ShowTime);                         // runs in the app; shared with UseSpineAI
});
```

The session offers only what the server's feature lists, the same list typed chat uses. An app function is offered when the feature lists it and the server declared it for the device (`ai.Functions.Add(fn, SpineAIFunctionSides.Device)` or `Both`); the app's `o.Functions` only supplies its implementation. The token also carries the feature's `MaxToolRounds`, the calls allowed per user turn: one more answers `refused`. It also carries `MaxToolResultCharacters`, the cap on a device result.

```csharp
public partial class AssistantViewModel(IVoiceSession voice) : ViewModelBase   // a new session per page
{
    async Task TalkAsync()
    {
        await voice.ConnectAsync("Voice");                 // POST {SessionEndpoint}/realtime/Voice, then the provider's socket
        Messages = voice.Transcript;                       // ChatView.Messages: the transcript as it is spoken
    }

    void Stop() => voice.Interrupt();                      // a tap cuts the answer off
    Task EndAsync() => voice.EndAsync();
}
```

- **Barge-in.** When the provider hears the user start to speak while the answer plays, the session does three things at once: it flushes the engine, it tells the model how much of the answer was heard (`conversation.item.truncate` with the played milliseconds), and it cancels the running response. `Interrupt()` does the same from a button. The state shows `Interrupted` for a moment, then `Listening`.
- **States:**
  - `Connecting` lasts while the token, the socket and the microphone open, and while the echo canceller settles.
  - `Listening` is when the user talks.
  - `Thinking` lasts from the response until its first audio.
  - `Speaking` lasts while the answer plays, which is longer than the provider sends it.
  - `Interrupted` and `Muted` are the other two.
  - `StateChanged` is raised on the main thread.
- **The transcript.** `Transcript` is an observable list of Microsoft.Extensions.AI `ChatMessage`s, changed on the main thread, so `Plugin.Maui.Spine.Controls.Chat`'s `ChatView` shows it as it is spoken. Neither package references the other.
  - The user's turn appears when it is committed, empty and marked pending (`AdditionalProperties["spine.pending"]`, `ChatView.PendingKey`), so `ChatView` shows a shimmering bubble; it gets its words when the transcription comes. A turn that could not be transcribed is removed.
  - The model's answer streams in.
  - An answer cut off by the user has `spine.interrupted` in its `AdditionalProperties`. Its text is what the provider transcribed, which can run ahead of what was heard.
  - Tool calls appear as `FunctionCallContent` and `FunctionResultContent`.
- **Functions.**
  - The token lists the server feature's functions, and the session declares them with the app's own (`SpineVoiceOptions.Functions`, the same set as `SpineAIOptions.Functions`) in one `session.update` when it connects.
  - A call to one of the app's functions runs in the app. A call to a server function is forwarded to `POST {SessionEndpoint}/functions/{feature}/{name}` with `AuthorizationHeader`, so it runs on the server with the signed-in user, and the result goes back to the model. No relay is needed: the app already holds the conversation.
  - A function both sides have runs where its `Prefer` says; `Prefer(name, side)` on the session changes that for one conversation, and declares the tools again while it runs.
  - An unknown, failing or refused function answers with an error result (`notFound`, `failed`, `signInRequired`), so the model can say so rather than wait.
- **The voice.** `Voice` asks the server for a voice at the next `ConnectAsync`; the server uses it when the feature offers it (`Realtime:Voices`), otherwise its own, and `SpokenVoice` says which. `VoiceAvatarDriver` sets it to the avatar's voice ([Avatars](avatars.md#with-a-voice-conversation)).
- **Hanging up on goodbye.** `EndsOnGoodbye = true` gives the model Spine's built-in `end_conversation` (`SpineAIFunctions.EndConversation`, the same in every session kind and in typed chat's `ChatView.EndsOnIntent`) to call when the user is done ("tack, hej då"). The model says a goodbye of a few words, and the session ends with `VoiceSessionEnd.Goodbye` once that has played and 0.7 s of quiet has passed, so the last word leaves the speaker. The farewell is not interrupted: from the moment the model hangs up the microphone is not sent, so the user saying goodbye back neither cuts it off nor starts a new answer. Only the Stop button (`Interrupt`) keeps the conversation going. With GPT-Live the tool is the backend's, and the voice reaches it by delegating (below). Off by default.
- **Push-to-talk.** `BeginTurn()` cuts off any answer and clears the input, and `EndTurn()` commits it and asks for the answer. It is meant for a feature with `"TurnDetection": "none"`.
- **Text.** `SendTextAsync(text)` says something in writing, and the model answers aloud.
- **The time limit.** `MaxMinutes` bounds what one conversation costs: set it before `ConnectAsync`, and the server takes it when it is shorter than the feature's `MaxMinutes` (never longer) and reserves only that from the budgets. The session ends itself then, and a GPT-Live session is also ended by the server a few seconds later if the app does not. A Realtime session cannot be ended by the server (see [the token is not a lock](ai-server.md#realtime-voice-sessions)). `TimeLeft` counts down. `Ended` says why the session ended: `User`, `TimeLimit`, `Provider` (the socket closed), `Goodbye` or `Failed`, with the exception.

  ```csharp
  voice.MaxMinutes = 2;                  // at most two minutes, whatever the feature allows
  await voice.ConnectAsync("Voice");
  ```
- **Refusals** are the AI client's exceptions:
  - `AIFeatureOffException` when the feature is off or needs a signed-in user (`signInRequired`)
  - `AIRateLimitedException` with `RetryAfter`
  - `HttpRequestException` naming the URL for a wrong `SessionEndpoint`
- **Providers.** The session speaks OpenAI's realtime events over a WebSocket, which covers Azure OpenAI too: the server's `protocol` is `openai` for both. A feature with `"Protocol": "Live"` answers `openai-live`, GPT-Live through the server's relay ([below](#gpt-live)); one with `"Relay": true` answers `openai-relay`, the same Realtime events through the server's relay ([server side](ai-server.md#the-realtime-api-through-the-relay)), which the session speaks exactly as `openai`. The server's feature decides; the app code is the same. Another protocol is refused before the microphone opens. Gemini Live and Azure Voice Live come when an app needs them.
- **The installation id.** The token request sends it, the same `SecureStorage` key as push and the AI client. Per-installation rates then apply when the feature lets installations in.

Seen on the owner's iPhone 16 Pro against `gpt-realtime` on 2026-10-15:
- The token, the socket, a spoken Swedish conversation, and the states all worked.
- `show_time` was called and answered ("Klockan är 19:30").
- The owner talked over the answer four times. Each time the answer stopped, the model was told how much was heard, and it answered what he said.

### GPT-Live

[GPT-Live](https://developers.openai.com/api/docs/guides/live) (`gpt-live-1`) is OpenAI's full-duplex voice model. It listens while it speaks, and it hands reasoning and functions to a backend model. It gives apps no credential, so the app connects to the server's relay, `{SessionEndpoint}/realtime/{feature}/relay`, with its own sign-in and a one-time ticket from the token request. The server opens GPT-Live with its key. Server side: [GPT-Live through the relay](ai-server.md#gpt-live-through-the-relay). In the app, nothing changes but what the session does underneath:

- **Delegation.** The voice calls no function itself: it starts backend work with `<|SpawnThinking|>`, and only the backend calls functions. The relay ends the voice's instructions with a delegation policy in the terms of OpenAI's prompting guide, written from the session's tools: the backend's tools with their descriptions, when to generate `<|SpawnThinking|>` (a fact or action a tool has, a correction, and a goodbye when the app offers `end_conversation`) and when not. The feature's `SystemPrompt` is the persona and should not say "hand it to the backend": asked that way, GPT-Live answered the time itself and never delegated. With the policy it delegated 21 times out of 21 (time, bookings, goodbye) through the relay on 2026-10-27.
- **Barge-in needs no message.** GPT-Live streams its answer at real time, silence included, and yields to the user by going quiet. The session plays what is not silence, so the queue holds little more than the network's jitter. If the model goes quiet while the user speaks and more than half a second is still queued, the session drops it and marks the answer interrupted. `Interrupt()` flushes the answer and drops what still comes of it until the model pauses; the model is not told.
- **The transcript** is grouped by speaker and pauses: GPT-Live sends both sides' words with times but marks no turns. A piece joins its speaker's latest message unless 1.5 s passed between them, and both sides can grow at once (a "mm" while the user talks is a message of its own). The user's turns are never pending.
- **States:** `Speaking` while answer audio plays, `Thinking` while the backend works on something the model handed it, `Listening` otherwise.
- **Functions** go to the backend model. The app's own functions run in the app as before. The server's run on the server inside the relay with the user's context, with no forwarding; the transcript shows the call and its result.
- **Text** typed during a call is given to the voice as the user's words.
- **Push-to-talk** mutes and unmutes the input; GPT-Live decides itself when to answer. `IsMuted` tells GPT-Live too, and while the engine sends nothing (muted, or `HalfWhileSpeaking`) the session sends silence, since GPT-Live listens to a continuous stream.
- **Hanging up on goodbye** works as with Realtime, but only the backend can call `end_conversation`, and the voice reaches it by delegating: the delegation policy tells it to delegate a goodbye. Turning `EndsOnGoodbye` on or off mid-call is told to the voice with `session.instructions.append`, since its instructions are otherwise fixed at the start. The voice talks while the backend works, so what it said before the hang-up may be the goodbye or only "one moment": the backend's answer asks it to say a goodbye only if it has not. If the voice says nothing more, the call ends 3 s after the hang-up when it had spoken since the user's last words, and 8 s after when it had not.

Seen against the push sample server on the Mac on 2026-10-20, with Swedish speech (macOS's Alva) streamed at real time through the real `VoiceSession`:
- "Hej! Vad är klockan just nu?": the voice said "Jag kollar det", the backend (`gpt-6-luna`) called `show_time` in the app, and the voice said "Klockan är 12:34".
- "Tack, hej då!": the backend called `end_conversation`, the voice said "Hej då!", and the session ended with `Goodbye`.
- 23 s of session cost $0.019 for the voice and $0.0003 for the backend. The relay recorded both and gave back the rest of the minute it had reserved.
- GPT-Live sent 22.7 s of audio over 23.7 s, nearly all of it silence (exact zeros, or peaks of 8 at most). The relay keeps silence off the phone's connection.

## Setup

`UseSpine()` registers the package once it is referenced (`UseSpineVoice()` without Spine). Each conversation gets its own engine:

```csharp
var audio = services.GetRequiredService<IVoiceAudio>();   // transient: one per conversation
```

The app supplies the microphone's usage text. Without it the engine fails at start with a message naming the key, rather than ending the app:

```xml
<!-- Platforms/iOS/Info.plist and Platforms/MacCatalyst/Info.plist -->
<key>NSMicrophoneUsageDescription</key>
<string>Talk to the assistant.</string>
```

- **Mac Catalyst:** the package contributes `com.apple.security.device.audio-input`. An app with its own `Entitlements.plist` adds the key there, and the build says so if it is missing.
- **Playback only:** `new VoiceAudioOptions { Input = false }` opens no microphone: no permission prompt, no usage text needed, no echo cancellation or convergence, and the playback audio category (spoken audio). [`ServerSpeech`](speech.md#a-voice-from-the-server) plays a server voice that way.
- **Background audio:** off by default. With `<SpineVoiceBackground>true</SpineVoiceBackground>` the package adds the iOS `audio` background mode, so a conversation that started in the foreground goes on with the screen locked. Apple reviews it.

## In the middle of a chat

`ConnectAsync(feature, conversation)` connects in the middle of a typed conversation:

- **The model is told what was said.** The text of the conversation's user and assistant turns (the last 40) goes to the provider as conversation items before the first spoken turn. Tool calls, results and pending turns are left out.
- **The spoken turns go into the same list.** `Transcript` is that list while the session is connected, so a `ChatView` bound to it shows typed and spoken turns in order. A turn still being transcribed when the call ends is removed.
- **Typing during the call** goes in with `SendTextAsync`, and the model answers aloud.
- `ConnectAsync(feature)` starts a session with an empty transcript of its own again.

`ChatComposer`'s microphone (`MicrophoneCommand`, `IsMicrophoneActive`, `MicrophoneTextCommand`) is made for this; see [Typed and spoken](chat.md#typed-and-spoken).

## A pipeline: any chat model

A realtime model hears and speaks itself. A pipeline builds the conversation from parts instead, so any chat model can talk, such as Claude, which has no voice of its own:

1. **Speech-to-text** writes the user's turn down: the device's recognizer ([Speech](speech.md#writing-speech-down)), or a `Kind: Transcription` feature on the server ([AI server](ai-server.md#transcription)).
2. **The chat model** answers: an `IChatClient` the app registers, such as a Spine.AI feature with its functions on both sides.
3. **Text-to-speech** reads the answer sentence by sentence as it streams in: the device's voices rendered to audio ([Speech](speech.md#rendering-to-audio)), or a `Kind: Speech` feature on the server, streamed as it is made ([Streamed speech](ai-server.md#streamed-speech)).

```csharp
builder.UseSpineVoice(o => o.Pipeline("Claude", p =>
{
    p.Language = "sv-SE";
    p.AddLocalSpeechToText().AddServerSpeechToText("Transcribe")    // the phone first, the server when it cannot
        .AddChat("Claude")                                          // AddSpineAI's o.Feature("Claude", f => f.Server("Claude"))
        .AddLocalTextToSpeech().AddServerTextToSpeech("ReadOpenAI");
}));

await voice.ConnectAsync("Claude", chat.Messages);   // a pipeline's name instead of a realtime feature
```

- **One session for both.** The registered `IVoiceSession` connects a pipeline when the name is one, and the server's realtime feature otherwise. States, `Transcript`, `SendTextAsync`, `Interrupt`, push-to-talk, `Prefer` and `VoiceAvatarDriver` work the same.
- **Fallbacks.** Several parts of a kind are tried in the order they were added: the first that is available is used, and the next takes over when it fails. A turn the device could not write down goes to the server with its audio; a voice that fails is skipped for the rest of the call. Another chat answers only while the first has said nothing.
- **Turns** are found by the microphone's level: speech for `StartOfTurn` (200 ms) begins one, quiet for `EndOfTurn` (800 ms) ends it, and 300 ms before the start goes with it. The threshold (`SpeechThreshold`, 0.02 of full scale) rises over a noisy room and doubles while the answer plays, so its echo is not taken for the user. Talking over the answer cuts it off and stops the chat.
- **The user's turn** shows as pending (`spine.pending`, a shimmer in `ChatView`) from the moment they start talking until its words are known, and is removed when nothing was said.
- **The answer** is read without markdown, cut at sentence ends (not at "kl. 18"). The server feature's `SystemPrompt` should ask for plain spoken sentences.
- **Permissions.** The device's recognizer asks for speech recognition when the call connects; the app declares `NSSpeechRecognitionUsageDescription`. Without it, or when the user says no, the server writes the user down, and a pipeline with no other part fails to connect, naming the reason.
- **Hanging up on goodbye.** With `EndsOnGoodbye`, each answer offers the chat Spine's built-in `end_conversation` (`SpineAIFunctions.EndConversation`) as the app's own function (`ChatOptions.Tools`), and the call ends with `VoiceSessionEnd.Goodbye` once the farewell has played and 0.7 s of quiet has passed, as with Realtime. The microphone is not listened to meanwhile. A Spine.AI server feature offers it to its model whenever the app does, with no declaration and no `Functions` entry. Turning `EndsOnGoodbye` off during the farewell cancels it, and the call goes on.
- **Time limit:** `MaxMinutes`, the app's own; no server reserves a pipeline's minutes. The call ends with `VoiceSessionEnd.TimeLimit`.
- **Timings** are logged at Debug under `Plugin.Maui.Spine.Voice`: the turn, the words, the chat's first words and functions, each sentence's first audio and a cut-off. A chat that answers no text is a warning (a reasoning model can spend its whole output limit thinking; turn reasoning off or raise `MaxOutputTokens`).
- **Not yet:** the device's parts on Android and Windows (#562, #563).

## A conversation

```csharp
await audio.StartAsync(new VoiceAudioOptions
{
    InputRate = 24_000,                  // what the provider wants from the microphone
    OutputRate = 24_000,                 // what it sends back
});

// The microphone: PCM16 little-endian mono, on the audio thread. Copy what you keep.
audio.InputAvailable += pcm16 => socket.Send(pcm16);

// The answer, as it arrives (faster than real time), per response item.
audio.Enqueue(itemId, answerPcm16);
audio.Complete(itemId);                  // its tail plays without waiting for a prebuffer

// The user talks over the answer: cut it off at once, and tell the provider how far it got.
if (audio.Flush() is { } position)
    await SendTruncateAsync(position.ItemId, position.Played);

await audio.StopAsync();
```

- **Prebuffer:** an answer starts once 100 ms is queued, so a slow network does not stutter its first syllables. `Prebuffer` sets the amount.
- **Played, not arrived:** the engine counts what it has played per item. `Flush()` and `Played(itemId)` therefore give what the user heard, which OpenAI's truncate takes as `audio_end_ms`.
- **Mute:** `IsMuted` stops the upload, and the input level falls to silence. A phone call does the same until it ends (`State` is `Muted`).
- **Half duplex:** `Duplex = VoiceDuplex.HalfWhileSpeaking` stops the upload while an answer plays. It is for devices whose echo cancellation is too weak; the user then interrupts with a tap. The default is `Full`.
- **Routes:** `UseSpeaker` chooses the loudspeaker or the receiver at the ear. A Bluetooth headset with a microphone (HFP) takes over while connected (`AllowBluetooth`). `Route` and `RouteChanged` say where the sound goes.
- **States:** `Connecting` lasts one second after the start (`Convergence`), while the echo canceller settles, then `Listening`. `Thinking` and `Interrupted` belong to the voice session. The engine's frame says `Speaking` while an answer plays, so a view drawing a bare engine knows who speaks; the engine's `State` stays `Listening`.

## Levels and bands

On the UI's tick, `UpdateFrame()` writes the latest into `audio.Frame`, a `VoiceFrame`:
- `InputLevel` and `OutputLevel`, 0 to 1 on a decibel scale from −60 to 0 dBFS. The input is measured after echo cancellation; the output as it plays.
- `InputBands` and `OutputBands`, 24 each, log-spaced from 80 Hz to 8 kHz, as `ReadOnlySpan<float>`.
- `State`, `Time` and `TimeInState`.
- `Viseme`: the mouth shape of what plays now, when the app gives an `IVisemeSource` (below); otherwise `-1`.

The frame is one object, overwritten on every tick without allocating. Read it during the call, and call `Snapshot()` to keep a copy.

## Drawing the conversation

An avatar draws the conversation: `AvatarView` with a `VoiceAvatarDriver` (Spine.Controls.Avatar). The driver reads the session's frame (or a bare engine's) on the view's tick: the state one to one, the microphone's and the answer's levels and 24 bands, and the mouth on the answer's playback clock.

```xml
<AvatarView Source="bars" Driver="{Binding Driver}" HeightRequest="200" />
```

```csharp
Driver = new VoiceAvatarDriver(voiceSession);   // or new VoiceAvatarDriver(audioEngine)
```

| `Source` | What it draws | Under Reduce Motion |
|---|---|---|
| `bars` (built in) | The answer's 24 bands up from a line and the microphone's down, so talking over the answer shows as both | The same row of bars in a still bell shape, rising with the level |
| `ring` (built in) | The answer's bands outward and the microphone's inward round an empty middle, for round a button or a picture | A ring of each colour whose size follows its level |
| a character (`Avatars/nova.spineavatar`, Kairo, a plush) | A face that listens, thinks, speaks with its mouth on the answer and flinches when talked over | The avatar's still pose |
| an ambient avatar (Voice Totem, Aurora Flow) | Light and shape that follow the levels | Slower motion |

- **Colours:** the built-in bars and ring draw the microphone in the `accent` slot, the app's accent unless `AccentColor` says otherwise, and the answer in the `output` slot, a warm orange unless `SetThemeColor("output", …)` says otherwise.
- **Frame rate:** `MaxFramesPerSecond` 15, 30 or 60. The view does not draw while it is hidden, scrolled out of sight or the app is in the background; under Reduce Motion it draws four times a second.
- **Screen readers** hear one element whose description is the avatar's name and state.
- **Your own look:** a `.spineavatar` of your own; see [Avatars](avatars.md#making-a-package). The format maps each band to a part (`inputBand0`–`23`, `outputBand0`–`23`), so a spectrum needs no code.

### Visemes

`IVisemeSource` gives the mouth shape for the item that plays and how much of it has played; the session asks it on every frame and puts the answer in `frame.Viseme`. The numbers are Azure Speech's 22 visemes (`Visemes.Silence` … `Visemes.P`), and `Visemes.Shape(viseme)` turns one into how open and how wide the mouth is.

```csharp
builder.UseSpineVoice(o => o.VisemeSource = new MyVisemes());   // e.g. timings from a TTS that gives them

sealed class MyVisemes : IVisemeSource
{
    public int VisemeAt(PlaybackPosition playing) => Lookup(playing.ItemId, playing.Played);
}
```

OpenAI's realtime API gives no visemes; without a source the avatar's mouth opens with the answer's level (AudioReactive).

### The screen's edge

Spine.Controls.Highlight glows the whole window's edge; give it the answer's level:

```csharp
edge = highlights.AttachEdge(window);
edge.Intensity = () => voice.UpdateFrame().OutputLevel;
// edge.Dispose() fades it out
```

## How it works on Apple

- **One engine for both directions.** The microphone and the answer go through one `AVAudioEngine`, with voice processing on its input node, and the answer is rendered by an `AVAudioSourceNode` in the same engine. Apple's echo canceller only removes what it knows is playing; a player of its own, even with the session in voice-chat mode, is not cancelled.
- **The session.** It is `.playAndRecord`, in voice-chat mode with voice processing and the default mode without it. Measuring showed that voice-chat mode alone already suppresses the echo, which hid it from a measurement "without echo cancellation".
- **Resampling.** The device's rates (often 48 kHz) are converted to the provider's by the package's own streaming windowed-sinc resampler, both ways.
- **Recovery.** When the hardware configuration changes (voice processing comes on, a headset is plugged in), the engine is started again. When media services are reset, it is rebuilt. Interruptions pause and resume it. Notifications are handled on the main thread, and no exception leaves an audio callback, which would end the app.
- **A Mac that refuses voice processing.** If the voice-processing unit fails to start, the engine starts without it and logs a warning. `IsVoiceProcessingOn` is then `false`.

## Live tests

`tests/Plugin.Maui.Spine.Voice.LiveTests` runs every session kind against the real models, as the app runs it. The user asks "Vad är klockan?", which only the app's `show_time` knows, then says goodbye, and the session must end on its own. The kinds covered are typed chat through the server (with ChatView's `end_conversation`), Realtime, GPT-Live through the relay, and a Claude pipeline.

- The server is the push sample's AI features, hosted in the test.
- The questions are spoken by OpenAI's voice and fed in real time through an audio engine without a device.
- The keys come from the environment, never from a file. Without them the tests are skipped. A run costs a few cents:

```bash
OPENAI_API_KEY=… ANTHROPIC_API_KEY=… dotnet test tests/Plugin.Maui.Spine.Voice.LiveTests
```

The session's log and states go to the test output (`--logger "console;verbosity=detailed"`). Run them after a change to a session, the relay or the functions.

## Measured

The Showcase's **Voice lab** page plays 14 seconds of Swedish speech through the engine's own player while the microphone records, once without echo cancellation and once with it. It reports the median level of 100 ms windows, so someone talking does not skew it, plus the loudest window, the room, the latency and the CPU. The results go to `voice-lab.json` in the app's data, and each take to a WAV file beside it.

iPhone 16 Pro, iOS 27.0.1, Debug build, phone on a table, 2026-10-15:

| Route | Echo without | Echo with | Removed (ERLE) | Room |
|---|---|---|---|---|
| Loudspeaker, quiet room | −20 dBFS (2 dB below the reference) | −90 to −120 dBFS in 120 of 134 windows | > 60 dB | −56 dBFS |
| Loudspeaker, noisier room | −23 dBFS | −49 dBFS, below the room | 25 dB (limited by the room) | −35 dBFS |
| Receiver | −32 dBFS | −63 dBFS | 31 dB | −44 to −58 dBFS |

- **Talking over the answer passes through.** In the quiet run the owner spoke for a second while the phone talked. Those 12 windows came through at −22 to −32 dBFS, while the phone's own voice stayed removed. That is the behaviour barge-in needs.
- **Latency.** The latency from the loudspeaker to the microphone, from the cross-correlation without echo cancellation, came out as 71 to 161 ms, and the correlation is unreliable in a noisy room.
- **CPU.** It was 50–60 % of one core in a Debug build, which runs the interpreter on iOS. A Release build has not been measured yet.
- **Mac Catalyst** (built-in speakers and microphone): voice processing came on, but the echo barely rose above the room (−39.6 against −38.5 dBFS), so the 7 dB removed says little.
