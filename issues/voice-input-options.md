# Voice: a conversation without the device's microphone, or without voice processing

**GitHub:** no issue; found in TV:Dash on the Chromecast with Google TV on 2026-11-01.
**Branch:** feat/voice-input-options
**Status:** In progress

## What happened

The Chromecast has no microphone of its own; its remote's microphone reaches an app only through the system's
remote service, which bridges the remote's stream into the recording that is active and not silenced when the
Assistant button is pressed, and looks for a plain recognition source. Spine.Voice's engine always opened the device's
microphone as a voice-communication source with the platform's echo cancellation, and the app had no say.

## Changes

- `SpineVoiceOptions.Microphone` (default true): off, a conversation opens no microphone; the app hands the session the
  user's words with `SendTextAsync`.
- `SpineVoiceOptions.VoiceProcessing` (default true): off, the microphone is a plain recognition source without the
  platform's echo cancellation.
- `VoiceSession` and `PipelineVoiceSession` pass both into the engine's `VoiceAudioOptions`.

## Decisions

- App-level options rather than per-conversation ones: the device decides, not the call.
