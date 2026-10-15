# Voice

```bash
dotnet add package Plugin.Maui.Spine.Voice
```

Realtime voice AI needs the microphone and the speaker at the same time, with the platform's echo cancellation. Without it the provider hears its own answer and interrupts itself. `Plugin.Maui.Spine.Voice` owns that part. This page covers the audio engine. The voice session, the provider adapters and `VoiceView` come next (#516), on top of it.

| Platform | Engine | State |
|---|---|---|
| iOS | One `AVAudioEngine` with voice processing | Measured on an iPhone 16 Pro (below) |
| Mac Catalyst | The same engine | Runs; the measurement was limited by the room |
| Android | `VOICE_COMMUNICATION` and `AcousticEchoCanceler` | Coming in #562 |
| Windows | Communications audio category | Coming in #563 |

On Android and Windows the engine says so when started: `VoiceAudioException` with `VoiceAudioProblem.Unavailable`.

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
- **States:** `Connecting` lasts one second after the start (`Convergence`), while the echo canceller settles, then `Listening`. `Thinking`, `Speaking` and `Interrupted` belong to the voice session in #516.

## Levels and bands

On the UI's tick, `UpdateFrame()` writes the latest into `audio.Frame`, a `VoiceFrame`:
- `InputLevel` and `OutputLevel`, 0 to 1 on a decibel scale from −60 to 0 dBFS. The input is measured after echo cancellation; the output as it plays.
- `InputBands` and `OutputBands`, 24 each, log-spaced from 80 Hz to 8 kHz, as `ReadOnlySpan<float>`.
- `State`, `Time` and `TimeInState`.

The frame is one object, overwritten on every tick without allocating. Read it during the call, and call `Snapshot()` to keep a copy.

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
