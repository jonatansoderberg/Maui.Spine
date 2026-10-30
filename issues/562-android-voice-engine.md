# Issue #562 — Spine.Voice audio engine on Android

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/562
**Branch:** issue/562-android-voice-engine
**Status:** Completed (the engine; the microphone foreground service is a follow-up)

## Plan

The Android half of the engine that #515 built for Apple, behind the same `VoiceAudioEngine` base: the base keeps
resampling, metering, the playback queue, duplex, mute and the state; the platform opens its devices, hands the
microphone's samples to `OnCaptured` and pulls the answer through `RenderOutput`.

- `AudioRecord` with the `VOICE_COMMUNICATION` source (or `VOICE_RECOGNITION` when voice processing is off, for
  measuring), mono PCM16, at the provider's rate when the device opens it and otherwise 16, 48 or 44.1 kHz with the
  base resampling; `AcousticEchoCanceler` and `NoiseSuppressor` on its session where the device offers them.
- `AudioTrack` for voice communication, streaming, on the microphone's audio session so the platform's echo canceller
  knows what plays; `AudioManager.Mode = InCommunication` while the microphone is open, the mode put back after.
- Two threads at `URGENT_AUDIO` priority, 20 ms a turn: the recorder converts to floats and calls `OnCaptured`; the
  player calls `RenderOutput`, converts to PCM16 and writes blocking. A flush (the user talks over the answer) empties
  the track too, through a new `OnFlushed` hook on the base, so the cut-off is heard at once rather than after the
  track's buffer.
- Routes: the communication device on Android 12 and later (`SetCommunicationDevice` for the loudspeaker or the
  earpiece; a headset or Bluetooth device, when present, keeps the sound as on Apple), `SpeakerphoneOn` before that;
  the route name from the current communication device or the first non-built-in output, kept current with an
  `AudioDeviceCallback`. Audio focus: gain for voice communication; a transient loss (a call) interrupts the
  conversation and the gain resumes it, as Apple's interruption does.
- Permissions: the engine asks for the microphone through MAUI's `Permissions` and fails with `NotConfigured` when
  `RECORD_AUDIO` is not declared and `PermissionDenied` when refused. The app declares the permission itself, as it
  declares `NSMicrophoneUsageDescription` on Apple.
- Out of this PR: the microphone foreground service (Android 14+ while the app is in the background, with #518's
  ongoing notification) and its permissions. The engine runs while the app is visible, which is the realtime
  conversation's case; the service is a follow-up.
- Measured in the Google TV emulator (the owner's Android phone was out of reach), which has a host microphone and a
  player but no echo canceller; the Voice lab's numbers from a phone come later.

## Changes

- `Core/VoiceAudioEngine.cs`: `OnFlushed()`, a hook `Flush()` calls after dropping the queue, for a platform whose
  player buffers ahead of `RenderOutput`.
- `Android/AndroidVoiceAudio.cs`: the engine (microphone, player, effects, focus, routes, two audio threads).
- `SpineVoiceExtensions.CreateAudio`: Android gets it; `UnsupportedVoiceAudio` is Windows' only.
- The Showcase's Android manifest declares `RECORD_AUDIO`, so its Voice lab and voice pages run there.
- `docs/wiki/voice.md` (the platform table, what the app declares), the package README, and the plan's step 4.1b.

## Decisions

- **No foreground service in this PR.** The engine runs while the app is visible, which is what a realtime
  conversation is; Android 14's microphone foreground service (with #518's notification) and its permissions are a
  follow-up, so the engine lands without waiting for them.
- **`MODIFY_AUDIO_SETTINGS` is the app's to declare, and checked.** `AudioManager.setMode` is refused silently without
  it ("Audio Settings Permission Denial: setMode()" in the system log, found with the Showcase in the emulator). The
  engine looks in the manifest and logs a warning instead of leaving the user guessing why the echo canceller is off.
- **The emulator's microphone is a tone.** With no host microphone (`Failed to create voice virtio-snd-mic0` in the
  emulator's log), the Google TV emulator feeds a `VOICE_COMMUNICATION` capture a 220 Hz sine at -3 dBFS and a
  `VOICE_RECOGNITION` capture silence. The echo test's "with echo cancellation" take therefore reads -3 dBFS whatever
  plays; its numbers mean nothing there. The recordings (`voice-lab-with.wav`) show the engine's capture path is right:
  24 kHz, full scale, the sine clean.
- **What the emulator did verify** (Showcase Voice lab, 2026-10-30 15:38): start at 24 kHz in and out with the
  communication source, route "Speaker", the reference played through the engine with all 24 output bands moving,
  stop and start again, both takes of the echo test running to the end and saving their WAVs; CPU 10–11 % of a core
  during playback with capture.
