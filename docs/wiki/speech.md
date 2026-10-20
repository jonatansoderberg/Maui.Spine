# Speech

`Plugin.Maui.Spine.Speech` reads text aloud with the platform's voices and says where it is: when the audio starts, each word as the voice reaches it, the position, and how it ended. That is what a caption that highlights the word being spoken needs, and what an avatar needs to move its mouth with the voice. It also writes speech down and renders the voices to audio, for a voice conversation that plays through its own engine.

```csharp
builder.UseSpine();   // or builder.UseSpineSpeech() in an app without Spine
```

```csharp
public sealed class ReaderViewModel(ISpineSpeech speech)
{
    public async Task ReadAsync(string text)
    {
        var utterance = speech.Speak(text, new SpeakOptions { Language = "sv-SE" });
        utterance.Started += (_, _) => IsReading = true;
        utterance.Word += (_, word) => Highlight(word.Start, word.Length);
        var reason = await utterance.Completed;   // Completed, Stopped or Failed
        IsReading = false;
    }
}
```

## Voices

`GetVoicesAsync(language)` lists the installed voices, best first: the exact language before others of the same language, then by quality (`Premium`, `Enhanced`, `Default`). Without a `VoiceId`, `Speak` takes the best voice for `SpeakOptions.Language`, or the platform's default when no language is given.

| Platform | Voices | Notes |
| --- | --- | --- |
| iOS, Mac Catalyst | `AVSpeechSynthesisVoice` | Enhanced and Premium voices are downloaded by the user in Settings, Accessibility, Spoken Content (Read & Speak on the Mac). Apple's novelty voices (Bells, Boing…) are left out |
| Android | `TextToSpeech.getVoices()`, those that need no network | Quality from the voice's rating: 500 and above Premium, 400 Enhanced |
| Windows | `SpeechSynthesizer.AllVoices` | Voices named "Natural" count as Premium |

## Utterances

| Member | What |
| --- | --- |
| `Started` | The audio started |
| `Word` | A word reached: `Start`, `Length` in the text, `At` the position then |
| `Viseme` | A mouth shape with its time (Azure Speech's ids 0–21), from a voice that reports them: `ServerSpeech` with an Azure feature |
| `Ended`, `Completed` | Once, with `Completed`, `Stopped` or `Failed` |
| `Position` | Zero while queued; the player's position on Windows, the time since the audio started elsewhere; frozen at the end |
| `Stop()` | Stops it, or drops it from the queue |

Utterances play one after another, and events arrive on the main thread. `StopAll()` stops the one speaking and drops the rest.

Words come from `willSpeakRangeOfSpeechString` on Apple, `UtteranceProgressListener.onRangeStart` on Android (Android 8 and later, and only on engines that implement it, such as Google's), and the synthesis stream's word-boundary cues on Windows, raised as the player reaches them.

## Writing speech down

`ISpineSpeechRecognizer` is the platform's speech-to-text, fed with audio the app already has rather than a microphone of its own: Spine.Voice's engine, with its echo cancellation, gives it the user and not the speaker. A [voice pipeline](voice.md#a-pipeline-any-chat-model) uses it for the user's turns.

```csharp
if (await recognizer.RequestPermissionAsync())
{
    using var recognition = recognizer.Start(new RecognitionOptions { Language = "sv-SE" });
    recognition.Partial += text => Caption = text;     // as the recognizer revises it
    recognition.Append(pcm16);                         // PCM16 mono at SampleRate (24 kHz by default), any thread
    var said = await recognition.FinishAsync();        // empty when nothing was said
}
```

- **iOS and Mac Catalyst** use `SFSpeechRecognizer` with an audio-buffer request, on the device when the language has a model there (`IsOnDevice`), else Apple's server. `OnDeviceOnly` keeps the audio on the device. `Phrases` are names and terms to prefer.
- **The usage text** `NSSpeechRecognitionUsageDescription` is required: without it `RequestPermissionAsync` throws, naming the key, rather than iOS ending the app.
- **Android and Windows** are not supported yet: `IsSupported` is false and `Start` throws.

## Rendering to audio

`ISpineSpeechRenderer` renders the same voices to PCM16 instead of playing them, so a voice conversation plays the answer through its own engine: the echo canceller knows it, talking over it cuts it off, and an avatar's mouth follows its level.

```csharp
await foreach (var piece in renderer.RenderAsync("Klockan är tre.", new SpeakOptions { Language = "sv-SE" }))
    audio.Enqueue(item, Resample(piece.Pcm16, piece.SampleRate));   // the voice's rate, 22.05 kHz for most of Apple's
```

iOS and Mac Catalyst render with `AVSpeechSynthesizer.write(_:toBufferCallback:)`, voice chosen as for `Speak`. Android and Windows are not supported yet.

## A voice from the server

`ServerSpeech` in `Plugin.Maui.Spine.Voice` is an `ISpineSpeech` whose voice is a speech feature on the app's server ([AI server](ai-server.md#speech), `Kind: Speech`). It sends the text to `POST {SessionEndpoint}/speech/{Feature}` with the app's sign-in and installation id, and plays the answer through Spine.Voice's engine without the microphone (`VoiceAudioOptions.Input = false`: no microphone permission, the playback audio category). The mouth shapes the server sends (Azure Speech) are raised as `Viseme` events, all at the start, with each one's time and length; an avatar's Speech driver then has TimedVisemes. A voice without them (OpenAI) gets its words spread over the audio by their length, raised as `Word` events when playback reaches them, so the driver guesses the mouth (EstimatedText).

```csharp
builder.UseSpineVoice(o =>
{
    o.SessionEndpoint = new Uri("https://api.example.com/ai");   // the prefix given to MapSpineAI
    o.SpeechFeature = "Read";
});

var speech = services.GetRequiredService<ServerSpeech>();
speech.Failed += e => status.Text = e.Message;   // a refusal (budget, rate), the network or the engine
avatar.Driver = new SpeechAvatarDriver(speech);
```

The voice is the server feature's, so `SpeakOptions` other than the text are not sent and `GetVoicesAsync` is empty. `Position` is what the engine has played. The engine starts with the first utterance and closes five seconds after the last. Only iOS and Mac Catalyst have Spine.Voice's engine so far; elsewhere `IsSupported` is false and an utterance fails.

## With an avatar

`SpeechAvatarDriver` in `Plugin.Maui.Spine.Controls.Avatar` speaks through `ISpineSpeech` and moves the avatar's mouth with the voice: Thinking until the voice starts, Speaking while it speaks, Idle after; each word's mouth shapes are guessed from its letters and laid down as the voice reaches it (lip-sync quality EstimatedText). The view's `Interrupt()` stops the voice. See [Avatars](avatars.md).

```csharp
var driver = new SpeechAvatarDriver(speech);
avatar.Driver = driver;
await driver.SpeakAsync("Welcome back");
```
