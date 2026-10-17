# Issue #592 — Spine.Speech: platform text-to-speech with word boundaries

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/592
**Branch:** issue/592-spinespeech-platform-text-to-speech-with-word-boun
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), steps B2.1–B2.3, D2.

## Plan

- **B2.1** `Plugin.Maui.Spine.Speech` (net10.0 core plus the MAUI TFMs): `ISpineSpeech` (`IsSupported`, `GetVoicesAsync`, `Speak`, `StopAll`), `SpeechUtterance` (`Started`, `Word`, `Viseme`, `Ended`, `Completed`, `Position`, `Stop`), a queue shared by every engine (`QueuedSpeech`), and the engines: `AVSpeechSynthesizer` (iOS, Mac Catalyst), `TextToSpeech` with `onRangeStart` (Android), `SpeechSynthesizer` with word-boundary cues through `MediaPlayer` (Windows); `UseSpineSpeech()` and its module props.
- **B2.2** `SpeechAvatarDriver` in the Avatar package (compiled on net10.0 too, so it is tested with a fake voice), an `OnInterrupted` hook so the view's `Interrupt()` stops the voice, and the Showcase Avatar page's **Speak** mode.
- **B2.3** README, `docs/wiki/speech.md`, `packages.md`, `avatars.md`, the `spine-controls` skill; the `speech.png` icon came with B1.1.

## Changes

- `src/Plugin.Maui.Spine.Speech/`: `Core/` (`ISpineSpeech`, `SpeakOptions`, `SpeechVoice`, `SpeechWord`, `SpeechViseme`, `SpeechEndReason`, `SpeechUtterance`, `QueuedSpeech`, `UnsupportedSpeech`, `SpeechVoices`), `Apple/`, `Android/`, `Windows/`, `SpineSpeechExtensions`, `build/*.props`, README. `tests/Plugin.Maui.Spine.Speech.Tests` (10 tests).
- Core: `EstimatedMouth` (the letter-guessed timeline the Text and Speech drivers share; `TextAvatarDriver` rewritten on it), `AvatarDriver.OnInterrupted`, `AvatarRuntime.Interrupt`; `TextAvatarDriver` clears on interrupt.
- `Plugin.Maui.Spine.Controls.Avatar`: `Drivers/SpeechAvatarDriver.cs`; references Speech (D2); `AvatarView.Interrupt` goes through the runtime; removing a driver leaves the avatar Idle.
- Showcase: the Avatar page's Speak mode (voice picker, Speak, Stop, the word count and timing); the Speech package in `GlobalXmlns.cs` and the csproj.
- Tests: `SpeechDriverTests` (Thinking → Speaking → Idle, words mouthed where the voice reached them, Interrupt stops the voice, timed visemes replace the guesses, a new text stops the old one). 164 avatar tests, 10 speech tests.

## Decisions

- **`SpeakOptions`, not `SpeechOptions`** (the plan's sketch): MAUI's implicit usings bring `Microsoft.Maui.Media.SpeechOptions`, so an app with `using Plugin.Maui.Spine.Speech;` would get an ambiguous name.
- **Speech depends on MAUI only, not on `Plugin.Maui.Spine`** (§3 listed the core): it needs nothing from it, and `UseSpine()` still registers it through the module props.
- **One utterance at a time is handed to the platform**; the queue is Spine's. Stopping a queued utterance then only removes it (AVSpeechSynthesizer cannot drop one queued utterance), and all three platforms behave the same.
- **A stop on Apple can arrive as didFinish** (seen on the iOS 26 simulator); the engine remembers it stopped and reports Stopped.
- **Apple's novelty voices are left out** of the list (Albert, Bad News, Bahh… led it alphabetically and became the default).
- **Position** is the player's on Windows and the time since the audio started elsewhere (neither Apple nor Android reports one); words carry the position they were reached at.
- **`SpeechAvatarDriver` lays each word's guessed visemes at the word's position**, the lab's Apple method; a `Viseme` event (a server voice, S3) switches the driver to TimedVisemes and drops the guesses.
- **Removing a driver leaves the avatar Idle**: `State` followed the driver, so it said Speaking after switching from Speak back to Manual mid-sentence.

## Verification

| Where | Result |
| --- | --- |
| Unit tests | Speech 10, avatar 164: pass |
| iPhone 17 Pro simulator, Showcase (Debug) | 68 voices; Speak: Speaking with EstimatedText, 16 word events over 6.1 s, then Idle; Stop: Stopped, back to Idle |
| Mac Catalyst, scratch app (Debug) | 41 English voices; "Hello, I am Nova…" spoken in 8.3 s with 16 word events in order (Hello 1.36 s … word 8.25 s), Nova Speaking with EstimatedText |
| Windows | Compiled on the Mac (`EnableWindowsTargeting`); not run |
