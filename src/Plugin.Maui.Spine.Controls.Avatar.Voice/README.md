# Plugin.Maui.Spine.Controls.Avatar.Voice

An avatar for a realtime voice conversation with [Spine.Voice](https://www.nuget.org/packages/Plugin.Maui.Spine.Voice).

- **`VoiceAvatarDriver`** follows an `IVoiceSession` (or a bare `IVoiceAudio`): the avatar's state is the session's, one to one; the microphone's and the answer's levels and 24 bands reach it every tick; the mouth follows the answer's level, or the session's visemes as timed cues on the playback clock when a viseme source is registered. The view's `Interrupt()` interrupts the session.
- **VoiceView's `character`**: an avatar inside a `VoiceView`, ticked by the VoiceView, from `VoiceAvatar.Source` or `SpineAvatarOptions.DefaultSource`.

```xml
<AvatarView Source="{Binding Avatar}" Driver="{Binding Driver}" HeightRequest="240" />
<VoiceView Session="{Binding Voice}" Kind="character" VoiceAvatar.Source="{Binding Avatar}" HeightRequest="240" />
```

```csharp
Driver = new VoiceAvatarDriver(voiceSession);
```

`UseSpine()` registers the `character` kind for an app that references this package (`UseSpineAvatarVoice()` without Spine). See [Avatars](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/avatars.md#with-a-voice-conversation).
