# Plugin.Maui.Spine.Voice

Voice for Spine apps. `IVoiceSession` holds a spoken conversation with a realtime model on the app's server (OpenAI's Realtime models or GPT-Live, OpenAI or Azure OpenAI): a short-lived token or the server's relay instead of a key in the app, a time limit per session, the answer cut off when the user talks over it, a transcript for `ChatView`, and tools the app answers itself. Under it, the audio engine runs the microphone and the speaker at once, with the platform's echo cancellation, so a realtime voice model does not hear itself. It takes and gives PCM16 at the provider's rate, keeps a playback queue that is cut off at once when the user talks over the answer, and meters both directions into levels and 24 bands for visuals.

iOS and Mac Catalyst now; Android and Windows come next.

```bash
dotnet add package Plugin.Maui.Spine.Voice
```

```csharp
builder.UseSpineVoice(o => o.SessionEndpoint = new Uri("https://api.example.com/ai"));

await voice.ConnectAsync("Voice");       // IVoiceSession, one per conversation
chat.Messages = voice.Transcript;
voice.Interrupt();                       // a Stop button; talking over the answer does the same
await voice.EndAsync();
```

Text read aloud by a speech feature on the server (`Kind: Speech`), played without the microphone; Azure Speech's mouth shapes reach an avatar's Speech driver as TimedVisemes:

```csharp
var speech = services.GetRequiredService<ServerSpeech>();   // an ISpineSpeech
speech.Feature = "Read";
await speech.Speak("Välkommen tillbaka.").Completed;
```

The engine on its own, for a provider of your own:

```csharp
var audio = services.GetRequiredService<IVoiceAudio>();   // one per conversation
await audio.StartAsync(new VoiceAudioOptions { InputRate = 24_000, OutputRate = 24_000 });

audio.InputAvailable += pcm16 => socket.Send(pcm16);    // the microphone, after echo cancellation
audio.Enqueue(itemId, answerPcm16);                     // the model's answer
var position = audio.Flush();                           // barge-in: cut off, and how far it got

var frame = audio.UpdateFrame();                        // on the UI's tick: levels and bands
```

The app writes `NSMicrophoneUsageDescription` in Info.plist.

Documentation: [Voice](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/voice.md).
