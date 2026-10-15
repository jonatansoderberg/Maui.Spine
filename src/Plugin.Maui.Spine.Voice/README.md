# Plugin.Maui.Spine.Voice

Voice for Spine apps. The audio engine runs the microphone and the speaker at once, with the platform's echo cancellation, so a realtime voice model does not hear itself. It takes and gives PCM16 at the provider's rate, keeps a playback queue that is cut off at once when the user talks over the answer, and meters both directions into levels and 24 bands for visuals.

iOS and Mac Catalyst now; Android and Windows come next.

```bash
dotnet add package Plugin.Maui.Spine.Voice
```

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
