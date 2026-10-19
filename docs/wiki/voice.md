# Voice

```bash
dotnet add package Plugin.Maui.Spine.Voice
dotnet add package Plugin.Maui.Spine.Controls.Avatar  # AvatarView, to draw it
```

A spoken conversation with a realtime model has two parts:

- **The voice session (`IVoiceSession`)** gets a short-lived token from the app's server, connects to the provider, cuts the answer off when the user talks over it, keeps a transcript, and runs the tools the app answers itself.
- **The audio engine (`IVoiceAudio`) under it** runs the microphone and the speaker at the same time, with the platform's echo cancellation. Without it the model hears its own answer and interrupts itself.

The provider's key never reaches the app. **`AvatarView`** (Spine.Controls.Avatar) draws the conversation, the sound in and out: see [Drawing the conversation](#drawing-the-conversation).

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
    o.ClientTools.Add(AIFunctionFactory.Create(
        () => DateTime.Now.ToString("HH:mm"), "show_time", "Says what time it is on the user's phone."));
});
```

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
  - The user's turn appears as "…" when it is committed, and gets its words when the transcription comes. A turn that could not be transcribed is removed.
  - The model's answer streams in.
  - An answer cut off by the user has `spine.interrupted` in its `AdditionalProperties`. Its text is what the provider transcribed, which can run ahead of what was heard.
  - Tool calls appear as `FunctionCallContent` and `FunctionResultContent`.
- **Client tools.**
  - `ClientTools` are added to the session when it connects. When the model calls one, the session runs it and sends the result back, and the model goes on.
  - An unknown or failing tool answers with an error, so the model can say so rather than wait.
  - Tools that touch user data belong on the server; #632 plans functions that run on the server with the user's context.
- **Hanging up on goodbye.** `EndsOnGoodbye = true` gives the model a tool, `end_conversation`, to call when the user is done ("tack, hej då"). The model says a short goodbye, and the session ends with `VoiceSessionEnd.Goodbye` once that has played and 0.7 s of quiet has passed, so the last word leaves the speaker. Talking over the goodbye keeps the conversation going. Off by default.
- **Push-to-talk.** `BeginTurn()` cuts off any answer and clears the input, and `EndTurn()` commits it and asks for the answer. It is meant for a feature with `"TurnDetection": "none"`.
- **Text.** `SendTextAsync(text)` says something in writing, and the model answers aloud.
- **The time limit.** The server cannot end a session (see [the token is not a lock](ai-server.md#realtime-voice-sessions)), so the session ends itself after the server's `maxMinutes`. `TimeLeft` counts down. `Ended` says why the session ended: `User`, `TimeLimit`, `Provider` (the socket closed), `Goodbye` or `Failed`, with the exception.
- **Refusals** are the AI client's exceptions:
  - `AIFeatureOffException` when the feature is off or needs a signed-in user (`signInRequired`)
  - `AIRateLimitedException` with `RetryAfter`
  - `HttpRequestException` naming the URL for a wrong `SessionEndpoint`
- **Providers.** The session speaks OpenAI's realtime events over a WebSocket, which covers Azure OpenAI too: the server's `protocol` is `openai` for both. Another protocol is refused before the microphone opens. Gemini Live and Azure Voice Live come when an app needs them.
- **The installation id.** The token request sends it, the same `SecureStorage` key as push and the AI client. Per-installation rates then apply when the feature lets installations in.

Seen on the owner's iPhone 16 Pro against `gpt-realtime` on 2026-10-15:
- The token, the socket, a spoken Swedish conversation, and the states all worked.
- `show_time` was called and answered ("Klockan är 19:30").
- The owner talked over the answer four times. Each time the answer stopped, the model was told how much was heard, and it answered what he said.

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
