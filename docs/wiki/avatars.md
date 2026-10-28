# Avatars

Animated characters and ambient visuals for a voice assistant, text-to-speech, a streamed chat answer, a guide or a status indicator. An avatar is content, a `.spineavatar` file the app bundles or downloads; Spine draws it, drives it and checks it.

> **Status:** in progress, following [the plan](../plans/spine-avatar.md). Core, the view, the drivers, the presenter, scripts and chat wiring exist; the tool follows.

## Packages

| Package | What |
| --- | --- |
| `Plugin.Maui.Spine.Controls.Avatar` | `AvatarView`, its drivers (text, speech, a realtime [voice](voice.md) conversation) and the built-in `bars` and `ring`: Skia (vector scenes, SkSL shaders) everywhere, glTF with SceneKit on iOS and Mac Catalyst and with Skia on Android and Windows, the poster where none applies |
| `Plugin.Maui.Spine.Controls.Avatar.Core` | The format, the scheduler, the drivers' contract and the text driver, visemes, glTF; no MAUI |
| `Plugin.Maui.Spine.Speech` | Text-to-speech with word boundaries, which `SpeechAvatarDriver` speaks through ([Speech](speech.md)) |
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
| `BodyColor` | The avatar's main colour, its `body` slot by convention (the Minis); unset, the preset's or the avatar's own |
| `SetThemeColor(slot, color)` | A colour for any other theme slot, in light and dark (spine2d and sksl); null gives the slot back. The built-in bars and ring draw the answer in `output` and the microphone in `accent` |
| `Preset` | One of the avatar's named figures (format 1.1): its option values and colours. Changing it clears `SetOption` values |
| `SetOption(name, value)`, `GetOption(name)` | An option, such as a shape, over the preset's value and the default; null gives it back. Changes what is drawn without loading again |
| `AccessibilityText` | What a screen reader says; unset, the avatar's name and state |
| `LoadState`, `LoadError`, `Package`, `RendererName`, `LipSyncQuality`, `Gestures` | What is shown and how |

### Figures, options and colours

A package can carry several figures (format 1.1). Its `options` are choices such as a shape, and its `presets` name figures: a set of option values and colours. One package then serves as many figures as it has presets, each in its own view:

```xml
<AvatarView Source="mini.spineavatar" Preset="red" />
<AvatarView Source="mini.spineavatar" Preset="brown" BodyColor="{StaticResource Brand}" />
```

```csharp
avatar.SetOption("shape", "drop");   // over the preset; throws, listing the values, when the avatar has no such option or value
avatar.GetOption("shape");           // "drop"
avatar.SetOption("shape", null);     // back to the preset's value or the default
```

The app's colours (`BodyColor`, `AccentColor`, `SetThemeColor`) win over the preset's, which win over the avatar's own. A preset names a light and a dark colour; a colour the app gives serves both. A preset or option value set before the avatar has loaded is checked when it loads: one the avatar does not have is logged and left out. The format is in the [authoring profile](../proposals/spine-avatar.md#2-the-manifest-avatarjson).

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

`VoiceAvatarDriver` follows a [Spine.Voice](voice.md) session. The avatar's seven states are the session's, one to one; the microphone's and the answer's levels and 24 bands reach it every tick; a new answer or a cut-off one starts a new speech generation. The realtime providers send no visemes, so the mouth follows the answer's level (AudioReactive); with a viseme source registered (`SpineVoiceOptions.VisemeSource`) the session's visemes become timed cues on the answer's playback clock (TimedVisemes). The view's `Interrupt()` interrupts the session.

On a page centred on the avatar, give an `AvatarView` the driver:

```xml
<AvatarView Source="{Binding Avatar}" Driver="{Binding Driver}" HeightRequest="240" />
```

```csharp
Driver = new VoiceAvatarDriver(voiceSession);   // or new VoiceAvatarDriver(audioEngine) for a bare IVoiceAudio
```

**The avatar's voice.** A package names the voice it speaks with, per provider, in its manifest: `"voices": { "openai": "marin", "azure": "sv-SE-SofieNeural" }` (`AvatarManifest.Voices`, `AvatarInput.Voices`). The driver sets the session's `Voice` to the avatar's `openai` one, and the session asks the server for it at its next connection; the server uses it when the feature offers it (`Realtime:Voices`, [AI on the server](ai-server.md)), and `SpokenVoice` says what it got. An avatar that names none, such as bars and ring, speaks with the feature's own voice. `UsesAvatarVoice = false` leaves the choice to the app. The Showcase's avatars: Kairo `marin`, Nova `cedar`, Puff `coral`, Voice Totem `sage`, Aurora Flow `shimmer`; `spine-avatar pack … --voice openai=marin` writes it.

The sound in and out rather than a face: Spine has two avatars built in, given by name.

```xml
<AvatarView Source="bars" Driver="{Binding Driver}" HeightRequest="200" />   <!-- or Source="ring" -->
```

`bars` puts the answer's 24 bands up from a line and the microphone's down; `ring` puts them outward and inward round an empty middle, for round a button or a picture. In code: `AvatarSource.BuiltIn("bars")`; `AvatarSource.BuiltInNames` lists them. Each band is its own part on its own parameter (`outputBand0`–`23`, `inputBand0`–`23`), so an avatar of your own can draw a spectrum the same way.

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
| `pack <file> -o <out> [--strip] [--asset-version 1.2.0]` | The manifest and bindings rewritten with the defaults, capabilities left to be computed, posters rendered at 512 px (`--size`), `source/` dropped with `--strip`, `assetVersion` set for a new release, the hash table rewritten, the result validated |
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

## Versions and downloaded avatars

A package's `assetVersion` (major.minor.patch) is its release: `spine-avatar pack … --asset-version 3.0.0` sets it, and a changed
avatar ships with a higher one. An app that downloads an avatar rather than bundling it keeps the file in its data folder and
compares versions to update it. The Showcase does this for Kairo (`Pages/Avatars/AvatarDownloads.cs`): the sample server lists
what it offers at `GET /sample/avatars` (id, name, profile, version, file, size, SHA-256, read from each package at startup, with the posters beside it), the app reads the
kept copy's version from its manifest, offers the update when the server's is later, and keeps a download only when it matches
the server's hash. A source never touches the network; the app opens the kept file with `AvatarSource.FromFile`.
