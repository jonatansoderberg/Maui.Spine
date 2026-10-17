# Avatars

Animated characters and ambient visuals for a voice assistant, text-to-speech, a streamed chat answer, a guide or a status indicator. An avatar is content, a `.spineavatar` file the app bundles or downloads; Spine draws it, drives it and checks it.

> **Status:** in progress, following [the plan](../plans/spine-avatar.md). Core, the view, the drivers, the presenter, scripts and chat wiring exist; the tool follows.

## Packages

| Package | What |
| --- | --- |
| `Plugin.Maui.Spine.Controls.Avatar` | `AvatarView`: Skia (vector scenes, SkSL shaders) everywhere, glTF with SceneKit on iOS and Mac Catalyst and with Skia on Android and Windows, the poster where none applies |
| `Plugin.Maui.Spine.Controls.Avatar.Core` | The format, the scheduler, the drivers' contract and the text driver, visemes, glTF; no MAUI |
| `Plugin.Maui.Spine.Speech` | Text-to-speech with word boundaries, which `SpeechAvatarDriver` speaks through ([Speech](speech.md)) |
| `Plugin.Maui.Spine.Controls.Avatar.Voice` | `VoiceAvatarDriver` for a realtime [voice](voice.md) conversation, and VoiceView's `character` |
| `Plugin.Maui.Spine.Avatar.Tool` | The `spine-avatar` command: checks, packs and draws `.spineavatar` files ([Making a package](#making-a-package)) |

## The view

```xml
<AvatarView Source="{Binding Avatar}" State="Listening" Expression="Curious" HeightRequest="240" />
```

```csharp
avatar.Source = AvatarSource.FromMauiAsset("Avatars/nova.spineavatar");   // or FromFile for a downloaded one
avatar.SetExpression(new(AvatarExpression.Happy, 0.8f, TimeSpan.FromSeconds(3)));
await avatar.PlayGestureAsync("nod");
avatar.LookAt(point);       // a point in the view's coordinates, or null to look back at the viewer
avatar.Interrupt();
```

`UseSpine()` registers the package for an app that references it. The view picks the first representation of the avatar a renderer here can draw: Skia for `spine2d` and `sksl` (on the GPU through Metal on iOS and Mac Catalyst), SceneKit for `glb` on iOS and Mac Catalyst, and Skia on the CPU for `glb` on Android and Windows. The Skia glTF renderer poses, skins and lights the model per vertex and does not sample textures, so a textured material shows its base colour; a model with alpha-masked materials (hair cards, lashes) is left to its poster. Where no renderer applies the view shows the avatar's poster with its state underneath and logs once.

| Property | What |
| --- | --- |
| `Source` | The avatar. A new one cancels the load before it; the avatar on screen stays until the next is ready |
| `Driver` | What drives it; null for the view's own `State`, `IsMuted` and `Expression` |
| `State`, `IsMuted`, `Expression`, `ExpressionIntensity` | The manual state, the microphone-off badge, the base expression. With a driver, `State` follows the driver |
| `MotionMode` | `System` follows Reduce Motion: a still pose, four frames a second, a slow mouth. `Reduced` and `Full` override it |
| `MaxFramesPerSecond` | 15, 30 (default) or 60 |
| `AccentColor` | The colour of the avatar's `accent` slot; unset, the app's accent |
| `AccessibilityText` | What a screen reader says; unset, the avatar's name and state |
| `LoadState`, `LoadError`, `Package`, `RendererName`, `LipSyncQuality`, `Gestures` | What is shown and how |

The view runs no timer while it is detached, hidden (itself or an ancestor), scrolled out of sight or the app is in the background. A screen reader hears one element; muting and an interruption are announced, other state changes are not.

## Presenters and scripts

`AvatarPresenter` is an avatar that presents: setting `Text` makes it say the text (aloud with `Speak="True"` and Spine.Speech, mouthed otherwise) while a caption bubble fills in with it. `Placement` puts the avatar `Left`, `Right` or `Top` of the caption; `ShowsCaption="False"` leaves only the avatar (in a tour card, which shows the tip itself). A screen reader hears one element, the caption.

```xml
<AvatarPresenter Avatar="{Binding Avatar}" Text="{Binding Caption}" Placement="Left" />
```

An `AvatarScript` is a short performance, played by `AvatarView.PlayAsync` or `AvatarPresenter.PlayAsync`, one step after another; the view's own driver comes back after it:

```csharp
var script = AvatarScript.Create()
    .State(AvatarState.Connecting)
    .Say("Hi, I am Nova.")                                  // aloud when the app has Spine.Speech
    .Express(AvatarExpression.Happy).Gesture("nod")
    .LookAt(addButton).Say("Tap here to add one.", speak: false)
    .WaitForTap();                                          // a tap on the avatar goes on
await presenter.PlayAsync(script, cancellationToken);
```

In a Highlight tour, a presenter given as `TourStep.Presenter` (or `Highlight.Presenter` on a Spotlight) says each step's tip, looks at the spotlighted element and plays the step's `Gesture`; see [Highlight](highlight.md#a-guide-in-the-card).

## With a voice conversation

`Plugin.Maui.Spine.Controls.Avatar.Voice` follows a [Spine.Voice](voice.md) session. The avatar's seven states are the session's, one to one; the microphone's and the answer's levels and 24 bands reach it every tick; a new answer or a cut-off one starts a new speech generation. The realtime providers send no visemes, so the mouth follows the answer's level (AudioReactive); with a viseme source registered (`SpineVoiceOptions.VisemeSource`) the session's visemes become timed cues on the answer's playback clock (TimedVisemes). The view's `Interrupt()` interrupts the session.

On a page centred on the avatar, give an `AvatarView` the driver:

```xml
<AvatarView Source="{Binding Avatar}" Driver="{Binding Driver}" HeightRequest="240" />
```

```csharp
Driver = new VoiceAvatarDriver(voiceSession);   // or new VoiceAvatarDriver(audioEngine) for a bare IVoiceAudio
```

Where the conversation is drawn by a `VoiceView`, use its `character` kind; the avatar runs on the VoiceView's tick rather than a timer of its own, and the VoiceView is the one screen-reader element:

```xml
<VoiceView Session="{Binding Voice}" Kind="character" VoiceAvatar.Source="{Binding Avatar}" HeightRequest="240" />
```

Without `VoiceAvatar.Source` the character shows `SpineAvatarOptions.DefaultSource` (`UseSpineAvatar(o => o.DefaultSource = …)`), and logs a warning when neither is set.

## The format

A `.spineavatar` is a ZIP with a manifest, models, bindings, posters, a licence and a provenance note. Format 1.0 has defaults for what avatars share, so a manifest names only what differs: Nova's is 695 bytes. The [authoring profile](../proposals/spine-avatar.md) describes it in full; the schemas are embedded in Core (`AvatarSchemas`).

Loading never trusts the file: names, sizes and the compression ratio are checked before anything is read, every file must match its SHA-256, nothing is executed or extracted to disk, and every check is in the report.

```csharp
var report = new AvatarValidationReport();
var package = AvatarArchive.TryRead(stream, report);
if (package is null)
    foreach (var failure in report.Failures)
        Console.WriteLine($"{failure.Area}/{failure.Name}: {failure.Detail}");
```

## Making a package

The `spine-avatar` .NET tool turns a generator's output into a package to ship, and checks one before it goes out:

```bash
dotnet tool install -g Plugin.Maui.Spine.Avatar.Tool
```

```bash
spine-avatar pack nova.spineavatar -o shipped/nova.spineavatar --strip
```

| Command | What |
| --- | --- |
| `inspect <file>` | Representations with their capabilities, posters and their size, every file, the validation summary |
| `validate <file> [--profile shipping]` | The format's checks; `shipping` also fails a poster under 128 px (a placeholder) and anything under `source/`, and warns over 2 MB (download it rather than bundle it). Exit code 1 on a failure |
| `pack <file> -o <out> [--strip]` | The manifest and bindings rewritten with the defaults, capabilities left to be computed, posters rendered at 512 px (`--size`), `source/` dropped with `--strip`, the hash table rewritten, the result validated |
| `strip <file> -o <out>` | Only `source/` removed and the hash table rewritten |
| `render-sheet <file> -o <dir>` | The posters and a sheet of the states and one of the expressions, as PNG, for review |
| `report <file>...` | One Markdown row per package: size, representations, posters, `source/`, validation, shipping |

Posters are drawn with the package's own Skia renderers on any OS: vector scenes, shaders and glTF models without alpha-masked materials. A model only SceneKit draws (alpha-masked hair or lashes, such as Kairo) keeps its posters unless `--poster-light` and `--poster-dark` give it pictures captured on Apple, with a transparent background.

## Driving an avatar

One driver at a time pushes state, levels and visemes into the avatar on each tick (`AvatarDriver`, `AvatarInput`). Every driver states what its mouth timing is worth (`AvatarLipSyncQuality`): `EstimatedText` for text, `AudioReactive` for a level, `TimedVisemes` for a voice with viseme timings, such as a server voice from Azure Speech through `ServerSpeech` ([Speech](speech.md#a-voice-from-the-server)).

`TextAvatarDriver` mouths text with no audio, for a streamed chat answer or a caption:

```csharp
var text = new TextAvatarDriver();
text.Begin();               // a reply is coming: Thinking
text.Append("Hello, ");     // Speaking while it mouths
text.Append("I am Nova.");
text.Complete();            // Idle once it is mouthed
```

With a [ChatView](chat.md), the chat's `Sent` and `Streamed` events drive the Text driver, so the avatar thinks until the answer's first words and mouths it as it streams in:

```csharp
chat.Sent += (_, _) => text.Begin();
chat.Streamed += (_, e) => { text.Append(e.Delta); if (e.IsComplete) text.Complete(); };
```

`SpeechAvatarDriver` reads text aloud through [Speech](speech.md) and moves the mouth with the voice: Thinking until the voice starts, Speaking while it speaks, each word's mouth shapes laid down as the voice reaches it (EstimatedText). The view's `Interrupt()` stops the voice.

```csharp
var speech = new SpeechAvatarDriver(spineSpeech);
avatar.Driver = speech;
await speech.SpeakAsync("Welcome back", new SpeakOptions { Language = "en-GB" });
```

Removing a driver (`Driver = null`) leaves the avatar Idle and back on the view's own properties.

Visemes are the 15 of `oculus15-v1`; `AvatarVisemes.FromAzure` maps Azure Speech's 22 (Spine.Voice's `Visemes`).
