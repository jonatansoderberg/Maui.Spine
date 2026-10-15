# Issue #567 — Spine.Voice: VoiceView and the built-in visuals (Orb, Bars, Ring, Avatar)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/567
**Branch:** issue/567-spinevoice-voiceview-and-the-built-in-visuals-orb
**Status:** In Review

Step 4.3b of `docs/plans/ai-and-security.md`. Proposal: `docs/proposals/spine-voice.md` §5. It builds on `IVoiceSession` (#516) and the engine's `VoiceFrame` (#515).

## Plan

### A new package: `Plugin.Maui.Spine.Controls.Voice` (the owner's choice, 2026-10-15)

It references `Plugin.Maui.Spine.Voice`, the core (`Plugin.Maui.Spine`, for `ReducedMotion` and the visibility rules the other controls follow) and SkiaSharp, like `Controls.MeshBackground`. `Plugin.Maui.Spine.Voice` stays free of SkiaSharp, so an app that uses only the session or the engine does not get it. `UseSpineVoiceView()` comes through a `SpineModule`, and calls `UseSkiaSharp()`.

### The plug-in contract

- **`VoiceVisual`**: `abstract void Draw(SKCanvas canvas, SKRect bounds, VoiceFrame frame, VoiceVisualContext context)`.
  - The context holds the input and output colours, `ReducedMotion` and the time step since the last draw.
  - A visual is created per view, so it can keep its own smoothing and blink timers.
- **`VoiceVisualView`**: an abstract `ContentView` with `OnFrame(VoiceFrame frame, VoiceVisualContext context)`, for a renderer of its own. This is where the owner's separate avatar library (3D, Rive) plugs in.
- **Registration:** `UseSpineVoiceView(o => o.AddVisual<MyMascot>("mascot"))`, by name.
  - The built-ins are `orb`, `bars`, `ring` and `avatar`.
  - An unknown name logs once and falls back to `orb`.

### `VoiceView`

A `ContentView` holding an `SKCanvasView` (raster, as in MeshBackground), or the registered `VoiceVisualView`.

- **Bindables:**
  - `Session` (`IVoiceSession`), or `Audio` (`IVoiceAudio`) for a page without a session.
  - `Visual`, `orb` by default.
  - `FrameRate`: 30 by default, 60 settable.
  - `InputColor` (the app's accent by default) and `OutputColor` (a warm colour by default).
- **The frame loop:** it follows MeshBackground. It pauses while detached, hidden, scrolled out of sight or in the background (`Window.Stopped`), and it is driven by the clock.
- **Reduce Motion:** the view draws at 4 frames a second, and the visuals draw the state and a slow level instead of the spectrum (`context.ReducedMotion`).
- **Screen readers:** the view is one element whose description is the state ("Listening", "Speaking"…), and it announces a state change.

### The built-in visuals (SkiaSharp)

| Name | What it draws |
|---|---|
| `orb` | A soft blob with a radial gradient. It breathes with the level and wobbles with the bands, cool while listening and warm while speaking; thinking is a slow pulse. |
| `bars` | Duplex bars: the answer grows up from the middle and the microphone down, 24 bands each. At barge-in both show at once. |
| `ring` | A spectrum around a circle, for around a button or a profile picture: output outside, input inside. |
| `avatar` | A simple face. It blinks, looks at the user while listening, and its mouth follows the viseme when a source gives one, otherwise the output level. |

### Visemes (`IVisemeSource`, the owner's choice: now)

These go in `Plugin.Maui.Spine.Voice`, because the frame is the session's.
- **`IVisemeSource`:** `int VisemeAt(PlaybackPosition playing)` returns the mouth shape for the item that plays and how far it has played, or -1 when it has none.
- **Registration:** `SpineVoiceOptions.VisemeSource`.
- **The engine:** it gets `PlaybackPosition? Playing` (the item and its played time), which the queue already counts.
- **The session:** on `UpdateFrame` it asks the source and sets `VoiceFrame.Viseme`. Aligned to played audio, as the proposal says.
- **Ids:** a small `Visemes` table of mouth-shape ids, the Azure Speech 0–21 set. The avatar maps them to mouth shapes.
- **No built-in source:** the realtime APIs give none. The avatar's level fallback is the base, and a source can come from Azure Speech or a local estimate later.

### The screen edge

The screen edge needs no new code: `highlights.AttachEdge(window).Intensity = () => voice.UpdateFrame().OutputLevel` with Controls.Highlight's `IEdgeGlow` (#531). Neither package references the other. It is documented, and the Showcase has it as a switch.

### Showcase

- **The Voice chat page:** the plain meter becomes a `VoiceView`, with a picker for `orb`, `bars`, `ring` and `avatar` and a switch for the screen edge.
- **The Voice lab:** keeps its meter.
- **A small Voice visuals page:** all four visuals at once, driven by the reference recording through an engine, so they can be seen without a model or a cost.

### Tests

- **`tests/Plugin.Maui.Spine.Controls.Voice.Tests`:** the visuals' drawing lives in a `net10.0`-buildable core (SkiaSharp only). Each visual is drawn into an `SKBitmap`, and the tests check that:
  - silence and a loud frame differ
  - the bars grow the right way
  - Reduce Motion draws without the spectrum
  - the avatar's mouth opens with the level and follows a viseme
  - an unknown visual name falls back
- **Voice tests:** the viseme source is asked with the playing position, and the frame carries its answer.

### Verification

- The simulator: each visual, the picker, Reduce Motion (the simulator's accessibility setting), and the screen edge.
- The iPhone: one short conversation with `orb` and the edge on. Real calls only for that.

### Docs

- `docs/wiki/voice.md` gets a view section, and the new package gets a README.
- README and `packages.md` (33 packages), the `spine-controls` and `spine-setup` skills.
- The package icon is reused for now.
- The proposal's status line, and the plan (4.3b, Decisions, the backlog).

## Open Questions

None. The owner decided on 2026-10-15: a new package `Controls.Voice`, and `IVisemeSource` now.

## Changes

- New package `Plugin.Maui.Spine.Controls.Voice` (net10.0 + MAUI TFMs, SpineModule `UseSpineVoiceView`):
  - `VoiceVisual` and `VoiceVisualContext` (colours, pixel scale, delta, Reduce Motion), and the built-in `OrbVisual`, `BarsVisual`, `RingVisual` and `AvatarVisual`, each with a quieter Reduce Motion form.
  - `VoiceVisualRegistry`: the four by name, `AddVisual<T>` / `AddVisualView<T>`, a new instance per view, and the orb for an unknown name.
  - `VoiceView`: `Session` or `Audio`, `Kind`, `FrameRate`, `InputColor`, `OutputColor`; its own timer that stops while detached, hidden, scrolled away or in the background, 4 fps under Reduce Motion; the state as its accessible description, with Muted, Interrupted and the end announced; `VoiceVisualView` for views of an app's own.
- Spine.Voice: `IVisemeSource` and `Visemes` (Azure's 22, `Shape`), `SpineVoiceOptions.VisemeSource`, `IVoiceAudio.Playing` (`PlaybackQueue.Current`); the session puts the source's answer in `frame.Viseme`.
- Spine.Voice: the engine's frame says `Speaking` while an answer plays (its `State` stays `Listening`).
- Tests: `tests/Plugin.Maui.Spine.Controls.Voice.Tests` (14: each visual drawn into a bitmap, Reduce Motion, the avatar's mouth and blinking, the registry); a viseme test in the session tests and a frame-state test in the engine tests.
- Showcase: Voice chat draws the session with `VoiceView` (a picker for the four) and can glow the screen's edge with the answer through Highlight; a new Voice visuals page shows all four on one engine playing the reference.
- Docs: `docs/wiki/voice.md` (Drawing the conversation: the visuals, own visuals, visemes, the screen's edge), the package README, README and packages.md (33 packages), highlight.md's `UpdateFrame()` line, the spine-controls and spine-setup skills, the proposal's status, the plan.
- Verified in the iPhone 17 simulator: the four visuals against the reference (warm while it plays, the avatar's mouth with the level, the loop), and Voice chat with the avatar and the edge in a real gpt-realtime session.

## Decisions

- **2026-10-15, owner:** `VoiceView` and the visuals are their own package, `Plugin.Maui.Spine.Controls.Voice` (Voice, the core, SkiaSharp), not part of `Plugin.Maui.Spine.Voice`. **Why:** it reuses the core's `ReducedMotion` and visibility rules like the other controls, and keeps SkiaSharp out of apps that only use the session or the engine.
- **2026-10-15, owner:** `IVisemeSource` comes now, though no realtime provider gives visemes yet.
- The property is `Kind`, not `Visual`: `Visual` hid `VisualElement.Visual` (CS0108).
- The engine's frame says `Speaking` while it plays. The Voice visuals page drew the answer in the user's colour with a closed mouth, because a bare engine only knew `Listening`; the engine's `State` keeps its documented four values, and the session overwrites the frame's state anyway.
- The Voice visuals page repeats the reference from a one-second timer, not from `StateChanged`: the engine's state does not change when playback ends.
- The edge in Voice chat calls `Session.UpdateFrame()` itself. `IVoiceSession` has no `Frame`; the meters' `Read` gives the latest values without consuming them, so two callers per tick are harmless.
- Review (/code-review on PR #569):
  - The orb disposes its blur filter and gradient shader every frame. A paint does not dispose them, so each frame leaked native memory.
  - The unknown-`Kind` warning waits for the handler, because XAML sets `Kind` before the logger can be reached.
  - Only Muted, Interrupted and the end are announced, so VoiceOver does not talk over the answer at every turn.
  - The input colour is `SpineTheme.GetAccent`, cached and cleared through `SpineTheme.Track`. It used to be a `Primary` lookup on every tick.
  - `VoiceVisualRegistry` is internal: no app could reach the instance the view uses.
  - The engine's `TimeInState` counts from the frame's own state change, so the synthetic `Speaking` starts at zero.
  - Kept as they are: the static options, which are the pattern of Spine.Voice and Spine.Images, and the edge's own `UpdateFrame()` call in the sample.
