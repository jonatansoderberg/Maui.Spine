# Spine Avatar: implementation plan (packages, drivers, Speech, Showcase)

**Status:** Phase A (the lab) done in [#524](https://github.com/jonatansoderberg/Maui.Spine/issues/524); Kairo's hair continues in [#542](https://github.com/jonatansoderberg/Maui.Spine/issues/542). Phases B–C planned 2026-10-17 from the 2026-10-16 review of the lab against Spine.Voice and the 2026-10-17 review of the format (D9). B0 (format 1.0, schemas, authoring profile in [spine-avatar.md](../proposals/spine-avatar.md), driver contract) done in [#586](https://github.com/jonatansoderberg/Maui.Spine/issues/586); no package exists yet.
**Last updated:** 2026-10-17 (B2 done: `Plugin.Maui.Spine.Speech`, `SpeechAvatarDriver`, the Avatar page's Speak mode; B3 next)
**Inputs:** *Maui Spine Avatar Control Specification* v1.0 (PDF, Swedish, 2026-10-09; `spec/Spine-Avatar-Spec.md` in the model bundle), the lab (`samples/SpineAvatarLab`, [changelog](../../issues/524-avatar-lab-load-render-and-drive-the-reference-ava.md) with its Findings and Measurements), the `spine-avatar` skill (`.claude/skills/spine-avatar/SKILL.md`).
**Related:** [spine-voice.md](../proposals/spine-voice.md) decision 7 (avatars are a separate control library; Voice keeps the plug-in contract), [ai-and-security.md](ai-and-security.md) phases 2 and 4, `Plugin.Maui.Spine.Voice`, `Plugin.Maui.Spine.Controls.Voice`, `Plugin.Maui.Spine.Controls.Chat`, `Plugin.Maui.Spine.Controls.Highlight`.
**Goal:** Avatars as a Spine control family an app can use for a realtime voice assistant, for text-to-speech, for plain text (a chat answer streamed, a caption), for guides and onboarding, and as an ambient status indicator; one scheduler, pluggable drivers and renderers, avatars as content (`.spineavatar`) that can be bundled or downloaded, published through the normal release flow.

## How to continue from a new session

1. Read this file, then the lab changelog's Findings and Measurements and the `spine-avatar` skill's ground rules.
2. Pick the first step whose status is **Ready** and whose dependencies are **Done**. Create its GitHub issue if the Issue column is empty (`gh issue create`, title as in the table), then `/spine-issue <id>`.
3. Work on the issue branch in a git worktree, never a checkout in the shared main folder. Do not touch `samples/SpineAvatarLab/Tools` or the Kairo package while #542 is open.
4. In the same PR:
   - set the step's status (**In progress** → **In review** → **Done**) and its PR number
   - record every non-obvious choice under **Decisions** here and in the issue changelog
   - add anything not run on a device to the **Verification backlog**
   - update **Last updated** at the top
5. Status values: **Proposal**, **Waiting** (on another step or an answer), **Ready**, **In progress**, **In review** (PR open), **Done**, **Parked**, **Dropped**.

## 1. What exists

Phase A built `samples/SpineAvatarLab`, a plain MAUI app (not a Spine app, on purpose) whose folders already match the planned packages:

| Folder | Content | Verdict (review 2026-10-16) |
| --- | --- | --- |
| `Avatar.Core/` | Manifest, safe ZIP reader with SHA-256 and the spec's limits, validator, the scheduler (activity and expression crossfades, idle variants, gestures, blink, gaze, springs, generation-keyed viseme cues, PP closure, underrun detection), `AvatarRenderFrame`, GLB reader and `GltfAvatarRig`, level meter, text-to-viseme estimate. No MAUI, no Skia; `tests/Plugin.Maui.Spine.Controls.Avatar.Core.Tests` compiles it alone. | **Keep.** Deterministic, allocation-free per frame, tested. The best part of the lab. |
| `Avatar.Core/AvatarContracts.cs` | The spec's proposed contracts: `IAvatarVisualSource`, `AvatarVisualEvent`, `IAvatarAudioPlayer`, `IAvatarSpeechSynthesizer`, `AvatarBands` (copies per frame). | **Trim.** Duplicates what Spine.Voice already is (`IVoiceSession`, `IVoiceAudio`, `VoiceFrame`, `IVisemeSource`). Keep the enums, `AvatarExpressionRequest`, `IAvatarPlaybackClock` and `AvatarPlaybackSnapshot`. |
| `Avatar.Skia/` | spine2d 1.1 renderer on `SKCanvas`: compiled scene, reused paths, gradients, blur, strokes, theme slots. | **Keep.** 0 B per frame, under 1 ms on the iPhone; blur-heavy avatars are over budget (Pip 4.7 ms, Aurora Motion 17.8 ms). |
| `Avatar.View/` | `AvatarView` (own `IDispatcherTimer`, load generations, mirrors), `AvatarSource`, `IAvatarSurface`, own `ReduceMotion`. | **Slim.** Lab-only API out (`MirrorOf`, `ViewYaw`, `HeadTurn`, `ThreeDLook`, `ThreeDRenderer`, `SkiaOnGpu`, `Stats`, `RendererDetail`, `LoadMilliseconds`, `LastFrame`, `FrameRendered`); the core's `ReducedMotion` and `SpineTheme` in; a driver contract instead of the `FrameRendered` push from feeds. |
| `Avatar.ThreeD/SceneKitAvatarSurface.cs` | GLB on SceneKit (Metal) for iOS and Mac Catalyst: geometry, PBR, morphers, skinners, studio or portrait light, bloom, contact shadow. | **Keep** for Apple. Render on demand rather than `RendersContinuously`; set only changed node transforms (Robot Expressive: 74 per frame, 2 KB). |
| `Avatar.ThreeD/ThreeDAvatarSurface.cs` + `Resources/Raw/wwwroot/three` | GLB on three.js in a `HybridWebView`, 24 KB JSON per frame. | **Lab only.** Measured on the Mac against SceneKit: 31 % vs 12 % CPU, 786 vs 307 MB, 373 vs 82 ms first frame, five WebKit processes. Not a product route. |
| `Avatar.Feeds/` | Microphone, platform TTS, fixture player; they push into the scheduler from `AvatarView.FrameRendered`. | **Lab only.** The product's audio is Spine.Voice; the TTS feed's word-timed estimate becomes the Speech package's method. |
| `Resources/Raw/Avatars/*.spineavatar` | 16 packages: Nova (1.9 MB), Pip, Dotling, Voice Totem, Aurora ×3, Pebble Bot, Robot Expressive, five plush, mpfb (13 MB), Kairo (16.6 MB; GLB 24.7 MB, 322 k triangles). | Nova-class are product size. Kairo is a downloaded asset, not a bundled one; `source/*.py` must not ship in a product package. Kairo is **NotMeasured** on any phone. |
| `Tools/` | Generators (Nova, plush, Aurora, Kairo's photo pipeline with Pixal3D and Blender), packagers, lab scripts. | Content production, stays in the lab and the skill. |

The format review of 2026-10-17 (D9) found the container, the SkSL contract and the validator right-sized, and the manifests and bindings mostly boilerplate: the five kept packages repeat the same states, expressions, viseme mapping and reduced-motion block, Aurora Flow's bindings hold 31 empty poses, Kairo's bindings carry four keys no schema knows, and Kairo's and Nova's posters are 1×1 placeholders. B0 fixes that with defaults, `extensions` and tool-made posters, without invalidating a single existing package.

What the review found missing for a product: the drivers (voice, text, speech, script) as one contract; a viseme id map (Spine.Voice speaks Azure's 22, the packages Oculus-15 canonical 0–14 and `EnqueueViseme` throws above 14); a single frame loop when an avatar sits inside `VoiceView`; a 3D route for Android and Windows; a viseme source for realtime audio (the realtime APIs send none, so today the mouth follows the level); device measurements for Kairo and for anything on an Android phone.

## 2. Principles

- **One scheduler, many drivers, many renderers.** Every way of driving an avatar (a voice session, a text stream, text-to-speech, a script, the app's own state) goes through the same `AvatarInput`; every renderer draws the same `AvatarRenderFrame`. New drivers and renderers are additions, never changes to the scheduler.
- **Spine.Voice owns audio.** The avatar never opens a microphone or a player; it borrows a clock, levels, bands and visemes. Text-to-speech playback also goes through one engine (Speech), not through the avatar.
- **Honest lip-sync.** Every driver states its `AvatarLipSyncQuality` (None, EstimatedText, AudioReactive, TimedPhonemes, TimedVisemes), the Showcase shows it, and no avatar pretends to timings it does not have.
- **Smorgasbord.** Core has no MAUI; the control package uses only renderers the OS provides (Skia, SceneKit); a renderer with a heavy native dependency (Filament) is its own package; Speech and the Voice adapter are their own packages so an app without voice carries nothing of it.
- **Avatars are content.** `.spineavatar` is the unit: versioned format, validated on load, bundled as a `MauiAsset` or downloaded by the app. Spine ships no avatar in a package; the Showcase bundles a set.
- **Works without a network and without audio.** Manual and text driving need nothing; Reduce Motion gives a still pose and a slow mouth; a missing renderer shows the poster and the state as text.
- **Accessible by default.** One screen-reader element per avatar (or per hosting `VoiceView`), the state in its description, announcements only for mute, interruption and end.
- **Measure before promising.** Release gates are run on the owner's iPhone and an Android phone in Release; anything else is **NotMeasured** and written down.

## 3. Package map

| Package | Kind | Depends on | Content | Issue | Status |
| --- | --- | --- | --- | --- | --- |
| `Plugin.Maui.Spine.Controls.Avatar.Core` | New, `net10.0` | System.Text.Json | Format (manifest, archive, validator, schemas as embedded resources), scheduler, `AvatarRenderFrame`, GLB model and rig, `AvatarVisemes` (canonical 15, maps from Azure 22 and the platform TTS alphabets), text-to-viseme estimate, level meter | B0, B1 | Done (B1.1) |
| `Plugin.Maui.Spine.Controls.Avatar` | New, MAUI | Core, `Plugin.Maui.Spine`, SkiaSharp.Views.Maui, `Plugin.Maui.Spine.Speech` (D2) | `AvatarView`, `AvatarSource`, `AvatarDriver` and `AvatarInput`, the Manual, Text, Speech and Script drivers, `AvatarPresenter` (avatar + caption), Skia surface, SceneKit surface (iOS, Mac Catalyst), poster fallback, `UseSpineAvatar()` | B1, B2, B4 | Done (B1.2); drivers Speech, Script and the presenter follow |
| `Plugin.Maui.Spine.Speech` | New, MAUI | MAUI only (§8.1) | `ISpineSpeech`: platform text-to-speech with voices, word boundaries and a playback clock (AVSpeechSynthesizer; Android `TextToSpeech` with `onRangeStart`; Windows `Windows.Media.SpeechSynthesis` with word-boundary metadata). Later: server voices with visemes (S3) | B2, S3 | Done (B2); server voices with S3 |
| `Plugin.Maui.Spine.Controls.Avatar.Voice` | New, MAUI | Avatar, `Plugin.Maui.Spine.Voice`, `Plugin.Maui.Spine.Controls.Voice` | `VoiceAvatarDriver` over `IVoiceSession` or `IVoiceAudio` (state 1:1, levels, bands, clock, visemes), the `VoiceView` kind `character` | B3 | Proposal |
| `Plugin.Maui.Spine.Server.AI` (existing) | Feature kind `Speech` | — | `POST /ai/speech/{feature}`: text in, PCM and a viseme timeline out; providers Azure Speech (visemes with offsets), OpenAI TTS (audio only); metered like the other kinds | S3 | Proposal |
| `Plugin.Maui.Spine.Controls.Avatar.Filament` | Later, Android | Avatar, Filament AAR binding | Native 3D on Android, only if S2 picks it | S2 | Proposal |
| `Spine.Avatar.Tool` | .NET tool | Core, Skia renderer (SceneKit on the Mac) | `inspect`, `validate --profile`, `pack` (defaults, computed capabilities, posters, hashes), `strip` (drops `source/`), `render-sheet` (posters and sheets), `report` | C1 | Proposal |
| `samples/SpineAvatarLab` | Stays | project references to Core and Avatar | The heavy tool: inspector, measurements, feeds, the web 3D surface, the generators | B1 | — |
| `samples/MauiSpineSampleApp` (Showcase) | Pages | Avatar, Speech, Avatar.Voice | Avatars gallery, Avatar detail, Assistant, Onboarding demo (§7) | B1–B4 | — |

Package icons: `assets/icons/avatar.png` for the three avatar packages (variants if cheap), `speech.png` for Speech.

## 4. Architecture

### 4.1 Layers

```
 app state / ChatView / ISpineSpeech / IVoiceSession / AvatarScript
            │            │              │                 │
      ManualDriver  TextDriver   SpeechDriver   VoiceAvatarDriver   ScriptDriver      ← AvatarDriver (one at a time)
            └────────────┴──────────────┴─────────────────┴─────────────┘
                                   AvatarInput                                        ← state, levels, bands, clock, cues, expression, gesture
                                 AvatarScheduler                                       ← Core: one AvatarRenderFrame per tick
                                   IAvatarSurface                                      ← Skia (spine2d) | SceneKit (GLB, Apple) | Filament/Skia-3D (S2) | poster
                                     AvatarView                                        ← the tick, visibility, Reduce Motion, theme, semantics
                                  AvatarPresenter                                      ← avatar + caption (+ actions), for guides and chat
```

### 4.2 The driver contract (Core, since B0)

```csharp
public abstract class AvatarDriver
{
    /// <summary>What the driver may do to the avatar; set while attached.</summary>
    protected AvatarInput Input { get; }

    /// <summary>What the driver's mouth timing is worth; shown by the Showcase, read by screen readers' hints.</summary>
    public AvatarLipSyncQuality LipSyncQuality { get; protected set; }

    protected internal virtual void OnAttached() { }   // no view: the contract is in Core (§8.1)
    protected internal virtual void OnDetached() { }

    /// <summary>One tick of the view, before the scheduler updates. Pull what you need and push it into Input.</summary>
    protected internal abstract void OnTick(TimeSpan delta);
}
```

`AvatarView.Driver` is bindable; `null` means the Manual driver (the view's own `State`, `IsMuted`, `Expression` bindables drive). Pull, not push: nothing writes into the scheduler between ticks, so there is one lock-free path per frame and the lab's `FrameRendered` event goes.

### 4.3 `AvatarInput`

A sealed facade over the scheduler so its lab-ish surface stays internal: `SetState`, `SetMuted`, `SetInputLevel(level, bands)`, `SetOutputLevel(level, bands)`, `PlaybackClock`, `EnqueueViseme(generation, start, duration, canonicalId, strength)`, `ResetSpeech(generation)`, `SetExpression(request)`, `SetBaseExpression`, `PlayGesture(name)`, `Interrupt()`, `LookAt`, `LipSyncQuality`.

### 4.4 Drivers

| Driver | Package | Input | Mouth | Quality | States it sets |
| --- | --- | --- | --- | --- | --- |
| Manual | Avatar | The view's bindables and methods | None or a manual viseme | None | Whatever the app sets |
| Text | Avatar | `Append(text)`, `Complete()`, `Clear()`, `CharactersPerSecond`; a one-shot `Mouth(text)` | Estimated from letters (Core's `AvatarTextVisemes`), laid down a second ahead of an internal clock; level and bands guessed from the viseme | EstimatedText | Thinking while busy with nothing to mouth, Speaking while mouthing, Idle after |
| Speech | Avatar (uses Speech) | `SpeakAsync(text, options)` → `ISpineSpeech` | Estimated per word as the voice reaches it (word boundaries); timed visemes when a server voice gives them (S3) | EstimatedText, later TimedVisemes | Thinking until the voice starts, Speaking, Idle; `Interrupt()` stops the voice |
| Voice | Avatar.Voice | `IVoiceSession` or a bare `IVoiceAudio`; `UpdateFrame()` each tick | Visemes from `VoiceFrame.Viseme` mapped 22→15 and turned into cues (start at the played position, closed when the id changes); otherwise the output level | TimedVisemes when a source is registered, else AudioReactive | `VoiceState` → `AvatarState` 1:1 (same seven values, same order) |
| Script | Avatar | `AvatarScript`: `Say`, `State`, `Express`, `Gesture`, `LookAt(view or point)`, `Wait`, `WaitForTap`; `PlayAsync(script, ct)`. A Highlight tour builds one step's script from its `TourStep` (B4.2) | Through the Text or Speech driver for `Say` | That driver's | As scripted |

**Chat wiring (no package):** `ChatView` gets a `Streamed` event (the answer's text delta and whether it is complete, B4). An app wires `chat.Streamed += (_, e) => text.Append(e.Delta)`; `IsBusy` true with no delta yet is Thinking. The Showcase's Assistant page does this, and optionally reads the finished answer aloud through the Speech driver.

### 4.5 Renderers and selection

`AvatarView` asks the package for its first representation whose renderer this platform has: Apple `skia`, `native3d`; Android and Windows `skia` until S2 adds a 3D route. A package with no drawable representation shows its poster (`previews/poster-light|dark.png`) and the state as text, and logs once. The web (three.js) surface is not in the package.

### 4.6 Visemes

Core owns the vocabulary: the Oculus-15 canonical ids (`sil, PP, FF, TH, DD, kk, CH, SS, nn, RR, aa, E, I, O, U`) the packages map to their poses, plus `AvatarVisemes.FromAzure(int)` (Spine.Voice's 22-id set), `FromText` (the estimate) and room for ARKit and platform alphabets. One table, tested both ways.

### 4.7 Loops and threads

One tick per visible avatar: the view's `IDispatcherTimer` at `MaxFramesPerSecond` (15, 30, 60; 4 under Reduce Motion), paused when detached, hidden, scrolled away or in the background. Hosted inside `VoiceView` (kind `character`), the avatar runs no timer; the host's tick calls the driver and `Tick()`. SceneKit renders on demand per tick, not continuously. S4 tries a display-linked loop (`CADisplayLink`, `Choreographer`) because the dispatcher timer lands at 52–56 fps.

### 4.8 Theme, Reduce Motion, semantics

The core's `ReducedMotion` (with `Changed`) and `SpineTheme` (`Track`, `GetAccent`) replace the lab's own; `AccentColor` unset means the app's accent. One semantic element: the avatar's description is its name and state (`AccessibilityText` overrides); hosted in `VoiceView` the host owns it. `Semantic.Merge` on `AvatarPresenter`.

### 4.9 Sources and size

`AvatarSource.FromMauiAsset`, `FromFile`, `FromStream(name, open)`; nothing in the package touches the network. Guidance in the docs: bundle avatars under about 2 MB; download the rest (Kairo 16.6 MB) and cache under app data; the Showcase shows the download with a progress and a cached second open. Shipped packages are `strip`ped of `source/`.

## 5. Public API (sketch)

```csharp
// Registration: UseSpine() does it for an app that references the package; call it to set defaults.
builder.UseSpineAvatar(o =>
{
    o.DefaultSource = AvatarSource.FromMauiAsset("Avatars/nova.spineavatar");   // what VoiceView's "character" shows
    o.Renderers.Add(new MyRenderer());                                           // S2's door for a renderer package
});

// The view.
<avatar:AvatarView Source="{Binding Avatar}" Driver="{Binding Driver}" State="Listening" Expression="Curious"
                   MaxFramesPerSecond="30" MotionMode="System" AccentColor="{StaticResource Primary}" />
avatar.SetExpression(new(AvatarExpression.Happy, 0.8f, TimeSpan.FromSeconds(3)));
await avatar.PlayGestureAsync("nod");
avatar.Interrupt();
avatar.LookAt(point);

// Drivers.
var text = new TextAvatarDriver();            text.Append("Hello"); text.Complete();
var speech = new SpeechAvatarDriver(speechService);   await speech.SpeakAsync("Welcome back", new SpeechOptions { Language = "sv-SE" });
var voice = new VoiceAvatarDriver(session);   // or new VoiceAvatarDriver(audioEngine)
var script = AvatarScript.Create().State(Connecting).Say("Hi, I am Nova.").Express(Happy).Gesture("nod").WaitForTap();
await avatar.PlayAsync(script, ct);

// Inside VoiceView.
<VoiceView Session="{Binding Voice}" Kind="character" avatar:Avatar.Source="{Binding Avatar}" />

// A presenter for guides and chat.
<avatar:AvatarPresenter Avatar="{Binding Avatar}" Text="{Binding Caption}" Placement="Left" />

// Speech on its own.
var utterance = speech.Speak("Read this aloud", options);   // events: Started, Word(start, length, at), Viseme(id, at, duration), Ended(reason)
await utterance.Completed;
```

Spine's rules: typed bindables, `[Experimental]` on anything that waits for S2 or S3, XML docs on every public member, `README.md` per package, a wiki page (`docs/wiki/avatars.md`, `speech.md`) and the `spine-controls` skill updated.

## 6. Steps

### Phase A: the lab (done 2026-10-14; #542 continues)

See the [changelog](../../issues/524-avatar-lab-load-render-and-drive-the-reference-ava.md) for A1–A9, the findings and the measurements. Exit criteria met except: no Android phone measurement, and Kairo unmeasured on any device.

### Phase B0: format and contracts

| Step | What | Issue | Depends on | Status | PR | Done when |
| --- | --- | --- | --- | --- | --- | --- |
| B0.1 | Freeze `avatar.schema.json` 1.0 **with defaults and `extensions`** (D9), and write `bindings.schema.json` for `native3d`, `spine2d` and `sksl`. **Defaults**, so an author writes only deviations: a state's pose is the state's name, `listening` is input-reactive, `speaking` output-reactive, `thinking` has gaze `processing`, transitions 220 ms; an expression's pose is `expr_<name>` with the profile's channels, and a **missing expression is a warning** with a fallback to neutral, not a failure; `canonicalProfile: oculus15-v1` implies the standard `viseme_*` mapping; `idle_a`/`idle_b` and the conventional gesture names are picked up when the model has them; `reducedMotion` defaults to the `reduced_motion` pose. **Removed or computed:** `platforms` goes (the renderer decides); `capabilities` is **computed by the validator** from the bindings and written to the report, a declared list being accepted and checked against it. **Bindings:** poses and animations whose names follow the conventions (`viseme_PP`, `expr_happy`, `nod`) need no entry and empty poses are not written; a **`sksl` representation needs no bindings file**; the keys the rig reads (`stateAnimations`, `gaze`, `blink`, `muteBadge`, `headNode`, `eyesNode`, `mouthNode`, `speechMouthMesh`, `expressionMouthTargets`, `defaultTransforms`, framing, `clipBlend`, theme bindings for gradient stops and strokes) are in the schema, and renderer- or model-specific keys (Kairo's `lighting`, `springs`, `neckChain`, `blinkFrames`, the `bloom` tuning) move to an **`extensions`** object with a prefix so the schema stays strict. Clip value semantics (1.0 absolute, 1.1 offset/factor) and level parameters stated explicitly. **Backward compatible:** a fully explicit manifest stays valid; defaults fill only what is absent | [#586](https://github.com/jonatansoderberg/Maui.Spine/issues/586) | A | Done | [#587](https://github.com/jonatansoderberg/Maui.Spine/pull/587) | The validator applies the defaults, checks bindings against the schema and computes capabilities; the five reference packages (Kairo, Nova, Aurora Flow, Puff, Voice Totem) validate unchanged, and again after being rewritten with the defaults (Nova's manifest under 1 KB, Aurora Flow's without a bindings file); the schemas are embedded in Core |
| B0.2 | The authoring profile in English as `docs/proposals/spine-avatar.md`: the conventions and defaults first and the explicit fields as overrides; the three representation kinds (`glb` on `native3d`, `spine2d` and `sksl` on `skia`) with Nova, Voice Totem and Aurora Flow as the worked examples; the canonical viseme set and the Azure map; the container, its limits and the SHA-256 table; `extensions`; `source/` allowed in development and stripped for shipping; **posters mandatory and produced by the tool** (C1 `render-sheet`), the state, expression and viseme sheets optional. spine2d 1.1 documented as **frozen**: the character features (path poses, viseme path poses, blur, blend modes) stay supported but have no reference model | [#586](https://github.com/jonatansoderberg/Maui.Spine/issues/586) | B0.1 | Done | [#587](https://github.com/jonatansoderberg/Maui.Spine/pull/587) | A generator author can build a valid package from the document alone; the Nova and Aurora Flow manifests shown in it are the real ones after the rewrite |
| B0.3 | Trim `AvatarContracts.cs`; define `AvatarDriver`, `AvatarInput` and `AvatarVisemes` in Core and the view layer, with the decisions of §8 recorded | [#586](https://github.com/jonatansoderberg/Maui.Spine/issues/586) | B0.1 | Done | [#587](https://github.com/jonatansoderberg/Maui.Spine/pull/587) | Core tests cover the 22→15 map, the Text driver's timeline and a fake driver's tick order |

### Phase B1: Core and Avatar packages, the gallery

| Step | What | Issue | Depends on | Status | PR | Done when |
| --- | --- | --- | --- | --- | --- | --- |
| B1.1 | `Plugin.Maui.Spine.Controls.Avatar.Core`: move `Avatar.Core/**` (and the rig) into the package; `tests/…Avatar.Core.Tests` on a project reference | [#588](https://github.com/jonatansoderberg/Maui.Spine/issues/588) | B0.3 | Done | [#589](https://github.com/jonatansoderberg/Maui.Spine/pull/589) | `dotnet build Spine.Packages.slnf` green; tests green; the lab compiles against the package |
| B1.2 | `Plugin.Maui.Spine.Controls.Avatar`: `AvatarView` slimmed (§1), drivers Manual and Text, Skia surface, SceneKit surface, poster fallback, the core's Reduce Motion, theme and semantics, `UseSpineAvatar()`, `build/*.props`, README, icon | [#588](https://github.com/jonatansoderberg/Maui.Spine/issues/588) | B1.1 | Done | [#590](https://github.com/jonatansoderberg/Maui.Spine/pull/590) | Nova on SceneKit and Pip, Dotling, Voice Totem on Skia in a scratch Spine app on the iPhone 17 simulator; 100 source switches with flat memory; Reduce Motion gives the still pose |
| B1.3 | Lab on project references: `Avatar.Core`, `Avatar.Skia`, `Avatar.View` and SceneKit removed from the lab, the inspector reaching diagnostics through an internal accessor (`InternalsVisibleTo`); feeds and the web surface stay. Lands after #542 merges | | B1.2, #542 | Waiting | | The lab builds and its measurements still read |
| B1.4 | Showcase **Avatars** gallery and **Avatar** detail page (§7), bundled set per D5, Kairo downloaded from the sample server and cached, Manual and Text driving | [#591](https://github.com/jonatansoderberg/Maui.Spine/issues/591) | B1.2 | Done | [#593](https://github.com/jonatansoderberg/Maui.Spine/pull/593) | The pages run on the simulator and the emulator; the console shows no page-margin warning |
| B1.5 | Release gate: iOS Release on the iPhone (AOT; watch for struct-generic skips like #579), Android Release on a phone, Mac Catalyst; Kairo measured on the iPhone | [#591](https://github.com/jonatansoderberg/Maui.Spine/issues/591) | B1.4 | Waiting (owner: devices) | | Numbers in the Verification backlog; Kairo's verdict (bundle, download or 2D twin) recorded |

### Phase B2: Speech

| Step | What | Issue | Depends on | Status | PR | Done when |
| --- | --- | --- | --- | --- | --- | --- |
| B2.1 | `Plugin.Maui.Spine.Speech`: `ISpineSpeech` with voices, `Speak` returning an utterance with `Started`, `Word`, `Ended` and a playback clock; Apple `AVSpeechSynthesizer` (`willSpeakRange`), Android `TextToSpeech` + `UtteranceProgressListener.onRangeStart`, Windows `SpeechSynthesizer` stream with word-boundary metadata played through `MediaPlayer`; Mac Catalyst as iOS; `UseSpineSpeech()` | [#592](https://github.com/jonatansoderberg/Maui.Spine/issues/592) | B0.3 | Done | PR_B2 | A scratch app reads a paragraph aloud on iOS, Android and Mac with word events arriving in time; Windows compiled, run on the Windows machine (#501 list) |
| B2.2 | Speech driver in Controls.Avatar: words laid down as the voice reaches them (the lab's Apple method), `Interrupt()` stops the voice, Thinking until the first word | [#592](https://github.com/jonatansoderberg/Maui.Spine/issues/592) | B2.1, B1.2 | Done | PR_B2 | The Showcase Avatar page's **Speak** mode: pick a voice, type, the avatar speaks it with EstimatedText quality shown |
| B2.3 | Wiki `speech.md`, README, icon, the `spine-controls` skill | [#592](https://github.com/jonatansoderberg/Maui.Spine/issues/592) | B2.2 | Done | PR_B2 | — |

### Phase B3: the voice assistant

| Step | What | Issue | Depends on | Status | PR | Done when |
| --- | --- | --- | --- | --- | --- | --- |
| B3.1 | `Plugin.Maui.Spine.Controls.Avatar.Voice`: `VoiceAvatarDriver` (state 1:1, levels and bands each tick, clock over `IVoiceAudio.Playing` where a new item or a flush is a new generation and the position is presentation time, visemes 22→15 as cues), `VoiceView` kind `character` hosting an `AvatarView` with no timer of its own (D4) | | B1.2, B0.3 | Waiting | | `<VoiceView Kind="character">` and `<AvatarView Driver="{VoiceAvatarDriver}">` both show Nova following a real realtime session on the simulator: Connecting, Listening with the input level, Thinking, Speaking with the mouth on the output level, Muted badge, Interrupted |
| B3.2 | Showcase **Assistant** page (§7): realtime session, transcript in `ChatView`, avatar, text entry through `SendTextAsync`, mute, push-to-talk, end, avatar picker, optional edge glow | | B3.1, B1.4 | Waiting | | Runs against the Azure sample server on the simulator and the owner's iPhone |
| B3.3 | Wiki `avatars.md` (drivers, hosting, visemes, sources, sizes), `voice.md` cross-links, README, the `spine-controls` skill | | B3.2 | Waiting | | — |

### Phase B4: presenter, script, guides and chat

| Step | What | Issue | Depends on | Status | PR | Done when |
| --- | --- | --- | --- | --- | --- | --- |
| B4.1 | `AvatarPresenter` (avatar + caption bubble with streamed text, placement, optional actions, `Semantic.Merge`) and `ScriptAvatarDriver` with `AvatarScript` | | B1.2, B2.2 | Waiting | | A scratch page plays a five-step script with text and speech; the caption streams; a tap advances `WaitForTap` |
| B4.2 | Highlight in combination: `TourStep.Presenter` (a `View`, typically an `AvatarPresenter`, shown in the tour card beside the tip), the tour telling the presenter each step's tip and the target's frame (`SpineTargets.TryGetFrame`) so the avatar mouths or speaks the tip and `LookAt`s the spotlighted element, `Highlight.Kind="Spotlight"` for a one-step hint with the same presenter, and a `TourStep.Gesture` (nod, lean) played when the step opens | | B4.1 | Waiting | | The Showcase **Onboarding demo**: Nova presents a three-step tour over the start page, looks at each highlighted control, speaks through Speech where available (else text only); the tour's Escape, Reduce Motion and screen-reader behaviour unchanged |
| B4.3 | `ChatView.Streamed` event (delta, complete) in Controls.Chat; the Assistant page mouths the text while it streams and can read the finished answer aloud | | B3.2 | Waiting | | Assistant page: text mode without a microphone shows the avatar mouthing the streamed answer |

### Phase B5: spikes (each its own issue; decide afterwards what ships)

| Step | What | Issue | Depends on | Status | PR | Done when |
| --- | --- | --- | --- | --- | --- | --- |
| S1 | Visemes from the answer's audio in Spine.Voice: an `IVisemeSource` fed from the playback path in 20 ms frames (RMS, band energies, spectral centroid, F1/F2 by LPC if useful) into 6–8 mouth shapes with hysteresis; a tiny ONNX model evaluated if the heuristic fails; measured against the lab fixture (`synthetic-test.wav` + `timed-cues.json`) and live on the Showcase | | #516 done | Ready | | Agreement numbers in a proposal section; under 0.3 ms per frame, 0 B steady; `o.VisemeSource = VoiceVisemes.FromAudio()` behind an option; ship or park |
| S2 | 3D on Android and Windows (D1). (a) A Skia renderer for untextured, unskinned GLB avatars: CPU transform of the rig's nodes and morphs, `SKCanvas.DrawVertices` with per-vertex shading and painter's sort, Metal/Vulkan-backed canvas where available; covers Nova, plush, Pebble Bot, Robot Expressive. (b) Filament binding (`filament-android`, `gltfio-android`) as its own package. Measure both on the Android emulator and a phone against SceneKit's numbers. Runs in the lab against `GltfAvatarRig`; the winner is ported into the package | | A | Ready | | The route for Android and Windows chosen and written here; Nova drawn on the emulator at 30 fps under 4 ms, or the reason it cannot |
| S3 | Server voices with visemes: `AIFeatureKind.Speech` in Server.AI with Azure Speech (visemes with audio offsets) and OpenAI TTS; the client plays PCM through Spine.Voice's engine in a playback-only mode (`VoiceAudioOptions.Input = false`), the Speech driver gets TimedVisemes | | B2.2, #510 done | Waiting | | The Showcase Avatar page's **Speak** mode offers a server voice and shows TimedVisemes; metered in `/ai/usage` |
| S4 | A display-linked frame loop (`CADisplayLink`, `Choreographer`, `CompositionTarget.Rendering`) behind `AvatarView` and `VoiceView`'s timer; tried in the lab first | | A | Ready | | 60 fps reached on the iPhone where 52–56 was; or the dispatcher timer kept with the reason |
| S5 | Skia blur budget: cache blurred static layers or blur at a lower resolution so Pip and Aurora Motion fit 4 ms p95 on the iPhone; in the lab's renderer, moved with it | | A | Ready | | Numbers before and after on the iPhone |

### Phase C: tool, docs and release

| Step | What | Issue | Depends on | Status | PR | Done when |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | `Spine.Avatar.Tool`: `inspect`, `validate --profile`, `pack` (applies the defaults, computes capabilities, **renders the posters** and updates the hash table), `strip`, `render-sheet` (posters and sheets; Skia on every OS, SceneKit on the Mac for GLB), `report`; the lab's Python packagers call it or are replaced by it | | B1.1, B0.1 | Waiting | | The five reference packages pass `validate` and come out of `pack` with real posters (Kairo's and Nova's are 1×1 placeholders today); `strip` removes `source/` from Kairo and keeps the hashes right |
| C2 | Docs: wiki pages, READMEs, the `spine-avatar` skill's "Finish" section pointing at the tool, `packages.md`, the Showcase's gallery texts | | B3.3, B4.3 | Waiting | | — |
| C3 | Release gates and the first tag with the new package ids: iOS Release/AOT and Android Release on phones, Mac Catalyst, flat memory over 100 navigations, posters when a renderer is missing; new ids checked on nuget.org after the run | | all of B | Waiting | | Core, Avatar, Speech and Avatar.Voice on nuget.org; Filament only if S2 chose it and its gates pass |

## 7. Showcase

| Page | Shows | Package line in the gallery |
| --- | --- | --- |
| **Avatars** (gallery) | Cards with posters for the bundled set (D5), chips by profile (Character, Ambient) and renderer; Kairo as a download card with progress and size; tap opens the detail page | `Plugin.Maui.Spine.Controls.Avatar` |
| **Avatar** (detail) | One `AvatarView` large, with a segmented driver: **Manual** (seven states, mute, expressions with intensity and duration, gestures), **Text** (an entry; "Mouth it" streams it through the Text driver), **Speak** (voice picker, entry, Speech driver; later a server voice), **Listen** (a bare `IVoiceAudio` engine: Listening with the microphone's level, no provider). Below: renderer name, `LipSyncQuality`, size 64/128/320, light/dark, Reduce Motion override, frame cap | `Plugin.Maui.Spine.Controls.Avatar`, `.Speech` |
| **Assistant** | The realtime assistant: avatar on top (`character` or a free `AvatarView`), the transcript in `ChatView`, a text entry (`SendTextAsync`), mute, push-to-talk, end, time left, avatar picker; text mode without a microphone mouths the streamed answer and can read it aloud | `Plugin.Maui.Spine.Controls.Avatar.Voice` |
| **Onboarding demo** | Nova presents a three-step tour over the start page through Highlight: the presenter in the tour card, the avatar looking at each spotlighted control and speaking the tip, a nod when the user moves on; the same presenter on a single Spotlight hint; Reduce Motion and screen-reader behaviour shown | `Plugin.Maui.Spine.Controls.Avatar`, `.Highlight` |

Conventions: the three-file page pattern, `HeaderBarConstants.PagePadding` inside the scroll source, entries in `MainPage.ViewModel.cs` with the package name, `GlobalXmlns.cs`. The existing **Voice chat** page keeps `orb`, `bars`, `ring` and the simple `avatar` and gains `character`.

## 8. Decisions before work starts

Each needs the owner's answer; the recommendation is marked.

- **D1, 3D on Android and Windows.** (a) **Recommended:** S2 first tries a Skia renderer for untextured, unskinned GLB avatars inside Controls.Avatar, one code path on every platform, with SceneKit kept for textured and skinned models on Apple. (b) Filament as its own Android package if (a) cannot hold 30 fps at 320 DIP. (c) Apple-only 3D in v1 with a `skia` representation required of cross-platform packages (Nova could get a 2D twin; Kairo cannot). The web route is dropped from the product in every case.
- **D2, Speech as a package of its own that Controls.Avatar depends on.** **Recommended yes:** reading aloud is useful without an avatar (a chat answer, an accessibility feature), Speech has no native dependency beyond the OS, and the alternative (a fourth avatar package) is one more id for nothing.
- **D3, chat without a package.** **Recommended:** a `Streamed` event on `ChatView` and the Text driver; no `Controls.Avatar.Chat`.
- **D4, how `character` gets its avatar.** **Recommended:** the attached property `Avatar.Source` on `VoiceView`, falling back to `SpineAvatarOptions.DefaultSource`, and a warning when neither is set. `<AvatarView Driver="{VoiceAvatarDriver}">` stays the primary form for avatar-centred pages.
- **D5, what the Showcase bundles.** The owner named the models worth keeping as they are on 2026-10-17: **Kairo, Nova, Aurora Flow (its shader to be improved), Puff and Voice Totem**; the others (Dotling, Pip, Pebble Bot, Robot Expressive, Aurora, Aurora Motion, the other plush, mpfb, the `face-*` series) stay lab material until regenerated. **Recommended:** bundle Nova, Puff, Voice Totem and Aurora Flow (about 3 MB together, stripped); Kairo downloaded from the Azure sample server (`SpineSampleServer`) and cached.
- **D6, the lab.** **Recommended:** stays as the heavy tool on project references (B1.3), after #542 merges; the generators and the Kairo pipeline stay in `Tools/` and the skill.
- **D7, format freeze.** Applied in B0 (#586). **Recommended:** manifest 1.0 and bindings 1.0 frozen in B0.1 as D9 shapes them; a new renderer id (S2) is an addition, not a version bump; `source/` optional and stripped from shipped packages.
- **D9, format slimming (decided by the owner 2026-10-17).** Defaults over declarations (states, expressions, viseme mapping, idle and gesture names, reduced motion); `platforms` removed and `capabilities` computed; conventional pose and animation names need no bindings entry and a `sksl` representation no bindings file; private keys under `extensions`; a missing expression is a warning with a fallback, not a failure; posters are mandatory and made by the tool; spine2d 1.1 frozen, its character features kept but without a reference model. The container (ZIP, SHA-256 table, limits, licence, provenance), the SkSL contract and the validator stay as they are. Reasoning: the five reference packages repeat the same 4–5 KB of manifest, Aurora Flow's bindings were 31 empty poses, Kairo's bindings had four keys outside any schema, and no kept model uses spine2d's character features.
- **D8, names.** `AvatarView`, `AvatarPresenter`, `AvatarDriver`, `AvatarInput`, `AvatarScript`, drivers `TextAvatarDriver`, `SpeechAvatarDriver`, `VoiceAvatarDriver`, `ScriptAvatarDriver`; `VoiceView` kind `character`; namespaces `Plugin.Maui.Spine.Controls` (views) and `Plugin.Maui.Spine.Controls.Avatar` (Core types), matching the other control packages.

### 8.1 Decided during the work

- **B2 (#592)**: `SpeakOptions` instead of the sketch's `SpeechOptions` (MAUI's implicit usings have `Microsoft.Maui.Media.SpeechOptions`); Speech depends on MAUI only, not on the core; Spine keeps the queue and hands the platform one utterance at a time; Apple's novelty voices are left out; the position is the player's on Windows and the time since the audio started elsewhere; `SpeechAvatarDriver` lays each word's guessed visemes at the word's position and switches to TimedVisemes on a `Viseme` event; drivers get `OnInterrupted` (the view's `Interrupt()` stops the voice); removing a driver leaves the avatar Idle.
- **B1.4 (#591)**: gallery cards show a small live avatar at 15 fps instead of the poster, until C1 renders real posters (Nova's, Puff's and Kairo's are 1×1); the bundled copies are made with `AvatarSlimming` and stripped of `source/` (1.9 MB together); Kairo is served by the sample server from the lab's file (still with `source/`, 229 KB, until C1's `strip`); the detail page hands its `AvatarView` to the view model for gestures, timed expressions and Interrupt; Text mode streams a reply in pieces as B4.3 will.
- **B1.2 (#588)**: `AvatarView` runs on Core's `AvatarRuntime`; the lab-only API and `Seed`, `SecondaryMotion`, `IsAnimationEnabled` stay out; Skia draws on Metal on Apple always; `MaxFramesPerSecond` defaults to 30; unset `AccentColor` is the app's accent; announcements for mute and Interrupted only; the renderer door (`AvatarRenderer`, `SpineAvatarOptions.Renderers`) is `[Experimental("SPINEAVATAR001")]` until S2; hosting for D4 is internal (`IsHosted`, `TickFromHost`). **D4 needs a new name**: a class `Avatar` in `Plugin.Maui.Spine.Controls` clashes with the namespace `Plugin.Maui.Spine.Controls.Avatar`.
- **B1.1 (#588)**: renderer internals (the spine2d scene model, the glTF model and rig, the shader contract, the level meter, the validator) are `internal` with `InternalsVisibleTo` for the Avatar packages, the tests and the lab; `AvatarRenderFrame` stays public for S2's renderer door. Namespace `Plugin.Maui.Spine.Controls.Avatar` (D8); the schema resource names keep the assembly's `….Avatar.Core.Schemas.*`.
- **B0 (#586)**: the defaults include `interrupted` 90 ms and `surprised` 100 ms (all five kept packages use them); posters, licence and provenance keep explicit paths (D9 lists no defaults for them); `speech` absent is mode `none`, `canonicalProfile` alone is `canonicalVisemes` with the standard mapping; a declared capability list is used as written and checked both ways against the computed one (Aurora Flow keeps `gaze` off that way); the schema check is a subset validator in Core, unknown keys warn and everything else fails with the JSON path; the morph-name pose convention is GLB only; `viseme_sil` may be empty; "Nova's manifest under 1 KB" is the authored part without the hash table (695 B compact). `AvatarDriver`, `AvatarInput`, `AvatarRuntime` (driver tick, then scheduler) and `TextAvatarDriver` live in **Core**, so `OnAttached()` takes no view; `AvatarInput` carries state, mute, base expression, clock, quality and look across a source change; `TextAvatarDriver.Begin()` is added for "a reply is coming". `AvatarSlimming` (the rewrite with defaults) is in Core for C1's `pack`.

## 9. Verification gates and backlog

Release gates (C3), run in Release on the owner's iPhone and an Android phone unless stated: first frame after a warm load under 300 ms; 60 fps on the iPhone and at least 30 on Android at 320 DIP; 2D render p95 under 4 ms; 0 B per frame in steady state; memory flat over 100 source switches and 100 navigations; speech cut within 90 ms of `Interrupt()`; no stale cues after a generation reset; the poster shown when the renderer is missing; VoiceOver and TalkBack read one element per avatar.

| Step | Not yet verified | Needs | How |
| --- | --- | --- | --- |
| A | Any Skia or SceneKit avatar on an Android **phone** (the emulator's compositor caps at 15–19 fps) | An Android phone | The lab in Release, `measured-result.json` |
| A | Kairo on any device (17 MB package, 322 k triangles with morph targets) | The owner's iPhone | The lab, then B1.5 |
| A | The 100-load stress run on the iPhone ended with the device connection lost; a crash is not excluded | The iPhone, a cable | Repeat with `devicectl --console` |
| A | Mac Catalyst microphone feed under a normal launch (denied under the harness) | The owner at the Mac | The lab's Listen |
| B1.4 | Kairo from the Azure sample server (the endpoint ran only locally); the Showcase pages on the iPhone and Mac Catalyst | The owner's deploy of the server; the iPhone | B1.5 |
| B1.2 | `AvatarView` on Android (emulator and phone), Windows and a Mac Catalyst run | Emulator, an Android phone, the Windows machine | B1.4's Showcase pages; B1.5 |
| B2 | Windows speech at run time (word-boundary metadata, `MediaPlayer` clock) | The Windows machine (#501) | Scratch app |
| B2 | Android speech on a phone with Google's engine (`onRangeStart` words); Speech on the owner's iPhone with an Enhanced or Premium voice | An Android phone, the iPhone | The Showcase's Speak mode |
| B0 | The lab's three.js page with a package that relies on the conventions or `extensions` (it reads the bindings file as written) | — | Lab only; no committed package relies on them until C1 rewrites them |
| B3 | Azure OpenAI's realtime socket with an avatar (only OpenAI called so far, per #516) | An Azure OpenAI resource | Assistant page |

## 10. Issues to create

One per step, titles as the tables word them, each referencing this plan: B0.1–B0.3 (one issue, "Spine.Avatar: format 1.0, bindings schema and the driver contract"), B1.1–B1.2 ("Spine.Controls.Avatar: Core and AvatarView packages"), B1.3 ("Avatar Lab on the packages"), B1.4–B1.5 ("Showcase: Avatars gallery and detail page, release gate"), B2.1–B2.3 ("Spine.Speech: platform text-to-speech with word boundaries"), B3.1–B3.3 ("Spine.Controls.Avatar.Voice: the voice driver and VoiceView's character"), B4.1–B4.2 ("Avatar presenter, scripts and Highlight tours"), B4.3 ("ChatView.Streamed and the Assistant page's text mode"), S1 ("Spike: visemes from the answer's audio"), S2 ("Spike: 3D avatars on Android and Windows"), S3 ("Spine.Server.AI: Speech feature kind with visemes"), S4 ("Spike: display-linked frame loop"), S5 ("Skia renderer: blur within budget"), C1 ("Spine.Avatar.Tool"), C3 ("Avatar packages: release gates and first tag").

## Appendix A: assets

Round 1 (Dotling, Voice Totem, Pebble Bot) are procedural with flat fills; round 2 added spine2d 1.1 (gradients, blur, strokes, blend modes), the generation prompt ([spine-avatar-model-prompt.md](spine-avatar-model-prompt.md)), Pip, Aurora ×3, Nova, five plush characters (`Tools/plush_avatars.py`), Robot Expressive (three.js, CC0), mpfb (TalkingHead sample, CC0; a lip-sync test subject) and Kairo (the owner's head: Pixal3D reconstructions, `Tools/kairo_scan.py`, hair in Blender; see #542 and the skill). Licences and provenance are inside each package. Not usable: three.js `facecap.glb` (no licence), Ready Player Me (shut down), Avaturn and MetaPerson (paid, realistic), VRoid (per-model terms). Image-to-3D services give textured GLB without facial morph targets.
