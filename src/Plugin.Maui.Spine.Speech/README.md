# Plugin.Maui.Spine.Speech

Text-to-speech for Spine apps, with what a caption or an avatar needs to follow it.

- **Voices**: `GetVoicesAsync(language)` lists the installed voices best first, with their quality, so an app can use Apple's downloaded Enhanced and Premium voices (MAUI's `TextToSpeech` picks the compact one).
- **Utterances**: `Speak(text, options)` returns at once; the utterance raises `Started`, `Word` (each word as the voice reaches it, with the position) and `Ended`, has a `Position`, and `Completed` says how it ended. Utterances play one after another; `Stop()` and `StopAll()` end them.
- **Sound on Apple**: the voice plays as spoken audio, so it is heard with the Ring/Silent switch on silent and a podcast pauses while it talks; a session that records (Spine.Voice listening) is left as it is.
- **Platforms**: `AVSpeechSynthesizer` on iOS and Mac Catalyst, `TextToSpeech` on Android (words from Android 8, on engines that report them), `SpeechSynthesizer` through `MediaPlayer` on Windows.

```csharp
builder.UseSpine();   // registers ISpineSpeech for an app that references this package (UseSpineSpeech() without Spine)
```

```csharp
var utterance = speech.Speak("Welcome back", new SpeakOptions { Language = "en-GB", Rate = 1.1f });
utterance.Word += (_, word) => caption.Highlight(word.Start, word.Length);
var reason = await utterance.Completed;   // Completed, Stopped or Failed
```

With `Plugin.Maui.Spine.Controls.Avatar`, `SpeechAvatarDriver` makes an avatar speak the text. See [Speech](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/speech.md).
