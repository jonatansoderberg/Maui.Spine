# Issue #595 — Spine.Controls.Avatar.Voice: the voice driver and VoiceView's character

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/595
**Branch:** issue/595-spinecontrolsavatarvoice-the-voice-driver-and-voic
**Status:** Completed (a real realtime session is NotMeasured)
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), steps B3.1–B3.3, D4.

## Plan

- **B3.1** `Plugin.Maui.Spine.Controls.Avatar.Voice`: `VoiceAvatarDriver` (net10.0, tested against a fake engine) over `IVoiceSession` or `IVoiceAudio`; VoiceView's `character` (`CharacterVoiceVisual`, a `VoiceVisualView` hosting an `AvatarView` with `IsHosted`, fed the VoiceView's frame and ticked with its delta); the attached property for its avatar (D4); `UseSpineAvatarVoice()` and its module props.
- **B3.2** The Showcase's **Assistant** page (realtime session, avatar free or as the character, transcript in `ChatView`, typing, mute, push-to-talk, end, time left, avatar picker, edge glow), and a **Listen** mode on the Avatar page (a bare engine with no provider).
- **B3.3** `avatars.md`, `voice.md`, `packages.md`, README, the `spine-controls` skill.

## Changes

- `src/Plugin.Maui.Spine.Controls.Avatar.Voice/`: `VoiceAvatarDriver`, `CharacterVoiceVisual`, `VoiceAvatar` (attached `Source`), `SpineAvatarVoiceExtensions`, `build/*.props`, README.
- `Plugin.Maui.Spine.Voice`: `VoiceSession.Audio` (internal) and `InternalsVisibleTo` for the avatar's voice package and the avatar tests, so the driver reads a session's playback position.
- Showcase: `Pages/Assistant/` (three files); the Avatar page's Listen mode; csproj, `GlobalXmlns.cs`, the index.
- Tests: `VoiceDriverTests` (the state 1:1 for six states, the microphone's level, visemes as timed cues, a new answer resets the cues, Interrupt flushes the engine, the muted badge). 175 avatar tests.
- Docs as planned.

## Decisions

- **D4's attached property is `VoiceAvatar.Source`** (the owner's choice, 2026-10-17): a class `Avatar` in `Plugin.Maui.Spine.Controls` hides Core's namespace `Plugin.Maui.Spine.Controls.Avatar` and broke every `using` of it (12 errors in the package itself).
- **The character's driver is fed the VoiceView's frame** (`Feed`) instead of calling `UpdateFrame()` a second time per tick; the VoiceView's Reduce Motion sets the avatar's motion mode.
- **The session's visemes become short cues each tick** (the tick's length plus 30 ms, at the played position): the session reports the shape under the playhead now, not its length.
- **A new playback item, or a position going backwards (a flush), starts a new speech generation**, so a cut-off answer's cues are dropped.
- **Without an engine to ask** (a session that is not Spine's `VoiceSession`), the driver runs its own clock while the answer plays.
- **Entering Interrupted calls `Input.Interrupt()`** (the reflex and the closed mouth), not just the state.
- **The character's `AvatarView` is out of the accessibility tree**: the VoiceView is the one element and makes the announcements.

## Verification (iPhone 17 Pro simulator, Debug, the Showcase with the scratch harness)

| Check | Result |
| --- | --- |
| Avatar page, Listen | A recorded answer played through the engine: Speaking with AudioReactive; after it, Listening with the simulator's microphone |
| Assistant, AvatarView | Connecting during a connect attempt; the local server has no OpenAI key, so the page says the Voice feature is off |
| Assistant, VoiceView character | Nova (SceneKit) and Aurora Flow (Skia) inside the VoiceView, on its tick, 28.6 fps; switching the avatar reloads it |
| Page margin | No warning in the console |

NotMeasured: a real realtime session (Connecting → Listening → Thinking → Speaking → Interrupted with a provider): the local server has no OpenAI key and the Azure one needs the owner's sign-in; Azure OpenAI's socket; the owner's iPhone.
