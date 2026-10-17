# Issue #515 — Spike: Spine.Voice audio engine (full duplex, echo cancellation, levels and bands)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/515
**Branch:** issue/515-spike-spinevoice-audio-engine-full-duplex-echo-can
**Status:** In Review

Step 4.1a of `docs/plans/ai-and-security.md`. Proposal: `docs/proposals/spine-voice.md` §3. The owner split the step on 2026-10-15:
- #515: the shared core and the Apple engine (iOS, Mac Catalyst).
- #562: the Android engine (step 4.1b).
- #563: the Windows engine (step 4.1c).

## Plan

### The package: `Plugin.Maui.Spine.Voice`

It has no provider yet. #516 puts `IVoiceSession` and the OpenAI adapter on top of it.

It targets the MAUI frameworks plus `net10.0`. The `net10.0` build holds only the platform-neutral core, as `Plugin.Maui.Spine.AI` does, so the core can be tested without a device. It references `Plugin.Maui.Spine.Common` only, not the core.

**Core (`Core/`, also `net10.0`):**
- `VoiceAudioFormat(int SampleRate)`: mono PCM16, the format the providers use (24 kHz; 16 kHz input for Gemini later).
- `VoiceFrame`: a sealed class overwritten on each UI tick (proposal §5.1/§8).
  - `State`, `InputLevel` and `OutputLevel` (0..1).
  - `InputBands` and `OutputBands` (24 each, as `ReadOnlySpan<float>`).
  - `Viseme` (-1), `Time`, `TimeInState` and `Snapshot()`.
  - `VoiceState` holds the proposal's seven states. The engine itself uses `Idle`, `Connecting`, `Listening` and `Muted`; the rest come with #516.
- `VoiceMeter`:
  - RMS to 0..1 on a dB scale (-60..0 dB), with attack and release.
  - 24 log-spaced bands from about 80 Hz to 8 kHz, from a 512-point FFT. The FFT is our own (a radix-2, about 60 lines).
  - The audio thread writes into a double buffer, and the UI tick reads the latest.
- `PlaybackQueue`: PCM16 chunks queued per response item, with a prebuffer of about 100 ms.
  - `Flush()` drops everything queued at once and returns what was played of each item.
  - Played samples are counted per item, which gives `audio_end_ms` for OpenAI's truncate in #516.
  - The output meter follows what is played, not what arrived.
- `PcmResampler`: a windowed-sinc resampler for platforms without a converter of their own (Android in #562). Apple uses `AVAudioConverter`.
- `IVoiceAudio`, the engine contract the adapters use. It is public, because provider adapters may come as their own packages.
  - `StartAsync(input, output, options, ct)` and `StopAsync()`.
  - Captured PCM16 arrives through an `InputAvailable` callback on the audio thread (a `ReadOnlySpan<byte>`, without allocations). A channel can follow in #516 if the adapters need one.
  - Playback: `Enqueue(itemId, pcm16)`, `Flush()` and `PlayedMilliseconds(itemId)`.
  - `IsMuted`, `Duplex` (`Full`, `HalfWhileSpeaking`), `Frame`, and `IsVoiceProcessingOn` (what the platform actually gave).
  - Events: `Interrupted`, `Resumed` and `RouteChanged`, plus `Failed` with a readable reason.
  - `VoiceAudioOptions`:
    - `VoiceProcessing`, on by default. Off exists for the measurement.
    - `PreferSpeaker`, and `AllowBluetooth` (HFP).

**Apple (`Platforms/iOS` and `MacCatalyst`, shared `Apple` code):**
- **The session:** `AVAudioSession` in `.playAndRecord`, mode `.voiceChat`, options `DefaultToSpeaker` and `AllowBluetooth`. It is set once before activation, plus `setAllowHapticsAndSystemSoundsDuringRecording`.
- **One `AVAudioEngine` for both directions:**
  - `inputNode.SetVoiceProcessingEnabled(true)` before the start.
  - An `AVAudioPlayerNode` into the main mixer.
  - An input tap goes through `AVAudioConverter` to mono PCM16 at the input rate.
  - Playback PCM16 goes through `AVAudioConverter` to the player's format.
- **Convergence:** `Connecting` lasts until the engine has run for about one second. The proposal says the first second can leak echo.
- **Interruptions, routes and resets:**
  - An interruption (a phone call) means `Muted` and paused upload, then resume.
  - A route change restarts the engine on the new route.
  - A media-services reset rebuilds the engine.
- **Permission:**
  - The app supplies `NSMicrophoneUsageDescription`. Its absence is a readable failure that names the key, as the Scanner does.
  - The engine asks for permission and reports a denial.
- **Build items:**
  - Mac Catalyst gets `com.apple.security.device.audio-input` as a `<SpineEntitlement>`.
  - `<SpineBackgroundMode Include="audio" />` comes only when `SpineVoiceBackground=true`. It is off by default (decision 1).
  - `build/Plugin.Maui.Spine.Voice.props` declares a `<SpineModule>` for `UseSpineVoice()`.

### The Showcase: a Voice lab page

It shows the engine working and measures it.
- **Live view:** Start and Stop, voice processing on or off, speaker or receiver, mute, and the route shown. Input and output levels and the 24 bands are drawn with a `GraphicsView`; the voice visuals come with #516.
- **Echo test:**
  - It plays a known speech file through the engine's own player while recording. The file is about 10 s of Swedish speech made with macOS `say`, as 24 kHz PCM16. It has no rights issue and is bundled as a `MauiAsset`.
  - It does this twice, with voice processing off and on.
  - **ERLE:** the energy of the reference against the energy of the captured input while it plays, in dB.
  - **Residual:** the residual level during playback.
  - **Latency:** the output-to-input delay, from the cross-correlation of the reference with the take without voice processing.
  - **CPU:** the process's CPU time per second, with metering on.
- **Results:** they show on the page and are written to `voice-lab.json` in the app's data, so a run on the iPhone can be fetched with `devicectl` and pasted into the issue changelog.
- **Earpiece, speaker and Bluetooth:** each is one run.

### Tests (`tests/Plugin.Maui.Spine.Voice.Tests`, `net10.0`)

- **FFT:** a known sine lands in its bin.
- **Meter:**
  - Silence gives 0.
  - A full-scale tone is about 1, and -20 dBFS lands where the scale puts it.
  - A tone at 1 kHz lights its band and not the others.
  - Attack and release behave as set.
- **Playback queue:**
  - The prebuffer holds playback back.
  - The played count per item, and its milliseconds.
  - `Flush` empties the queue and reports the played milliseconds.
  - Items are kept apart.
- **Resampler:** 48→24 and 24→48 kHz keep the length and the tone frequency.
- **`VoiceFrame.Snapshot`:** it copies, and the original can be overwritten.

### Verification

- **The iPhone over the cable:** deploy with `devicectl`, run the lab through a harness, and fetch `voice-lab.json`.
  - Earpiece, speaker and, if a headset is at hand, Bluetooth.
  - Voice processing on and off.
  - Latency and CPU.
- **Mac Catalyst on this Mac:** the same, after the owner approves the microphone.
- **The simulator:** voice processing is only partly there, so it counts for compile and UI only.

### Docs

- `docs/wiki/voice.md`: the engine part only, and what was measured.
- The package README, the package tables and the counts.
- The plan:
  - 4.1 is split into 4.1a, 4.1b and 4.1c.
  - Decisions, the Verification backlog, the measured numbers, and Last updated.
- The proposal's status line.

## Open Questions

None. The owner answered on 2026-10-15: three issues, built in the repo with a Showcase lab, and the iPhone, an Android phone and this Mac are available.

## Changes

- `src/Plugin.Maui.Spine.Voice` (in `Spine.slnx` and `Spine.Packages.slnf`, icon `ai.png` for now):
  - `Core/` (also `net10.0`):
    - `IVoiceAudio`, `VoiceAudioOptions`, `VoiceAudioException`, `VoiceDuplex` and `VoiceFrame`
    - `VoiceMeter`, with its own radix-2 FFT
    - `PcmResampler`, a streaming windowed sinc with a tabled kernel
    - `PlaybackQueue`: per item, a prebuffer, `Complete`, `Flush` with the played position
    - `VoiceAudioEngine`, the shared engine the platforms subclass
  - `Apple/AppleVoiceAudio.cs`, with these parts:
    - one `AVAudioEngine`, voice processing on the input (and output) node, and an `AVAudioSourceNode` for the answer
    - interruptions, route changes, configuration changes and resets
    - guarded callbacks
    - the `NSMicrophoneUsageDescription` check and the permission
    - a fallback without voice processing that says so
  - `SpineVoiceExtensions.UseSpineVoice` registers a transient `IVoiceAudio`. Android and Windows get an engine that answers `Unavailable` until #562 and #563.
  - `build/`: a `SpineModule`, the Mac Catalyst audio-input entitlement, and the opt-in `audio` background mode (`SpineVoiceBackground`).
- `tests/Plugin.Maui.Spine.Voice.Tests`: 25 tests, covering:
  - the FFT, the meter's scale, bands, attack and release
  - the queue's prebuffer, order, completion and flush
  - the resampler's length, tone and anti-aliasing
  - the shared engine with a fake platform: PCM16 at the provider's rate, the states, mute and interruption, half duplex, playback at the device's rate, flush, and stop
- Showcase:
  - **The Voice lab page:** a `VoiceMeterView` with levels and bands, Start, Stop, Play the reference, and switches for echo cancellation, loudspeaker and mute.
  - **The echo test:** two takes saved as WAV, results in `voice-lab.json`.
  - **The reference:** 14 s of Swedish speech made with `say -v Alva`.
  - **Setup:** `NSMicrophoneUsageDescription` in both Info.plists, and the Mac entitlement.
- Docs:
  - `docs/wiki/voice.md` and the package README
  - README and `packages.md` (32 packages), the `spine-setup` skill
  - the proposal's status line
  - the plan: 4.1 split, Decisions, the backlog, a test recipe
- Measured on the owner's iPhone and on this Mac (see `docs/wiki/voice.md` § Measured).
- Follow-up issues: #562 (Android) and #563 (Windows).
- PR #566. `/code-review` found 9 things; fixed all:
  - a failed start closes what the platform opened (the session is not left active)
  - a start and a stop run one at a time (a double tap no longer opens the microphone twice)
  - `Listening` is announced by a timer when the convergence ends, not only from `UpdateFrame`; `StateChanged` may come on any thread
  - half duplex holds the microphone back for 300 ms after the answer's last sample, for what the device still plays
  - the session is activated and deactivated off the calling thread
  - the configuration-change observer is per engine
  - the played counts per item stay bounded
  - the echo test reports no latency when the correlation peaks at the edge of its window
  - a stopped engine's frame shows silence, and the lab's meter view stops redrawing it
  - 30 tests pass
  - Rerun on the iPhone after the fixes (18:30): no crash, loudspeaker echo −23.5 dBFS without and −80.8 dBFS (median) with echo cancellation, 57 dB removed.

## Decisions

- **2026-10-15, owner:** #515 is the shared core and the Apple engine. Android is #562 and Windows is #563, each with its own PR.
- **2026-10-15, owner:** The engine goes straight into `Plugin.Maui.Spine.Voice` in the repo, with a Showcase Voice lab page that measures it, rather than a scratch app outside the repo.
- **2026-10-15, Claude:** The answer is rendered by an `AVAudioSourceNode` that pulls from the queue, not an `AVAudioPlayerNode` with scheduled buffers. **Why:** played is then what was rendered, `Flush()` is immediate, and the echo canceller sees the output of the same engine.
- **2026-10-15, Claude:** The package's own windowed-sinc resampler on every platform instead of `AVAudioConverter`. **Why:** one tested path, shared with Android. Its kernel is tabled at 256 phases; computing a sine and a cosine per tap cost 60 % of a core in a Debug build.
- **2026-10-15, Claude:** Voice-chat mode only with voice processing. **Why:** the first iPhone run measured −51 dBFS of echo "without" echo cancellation; with the default mode it was −20 dBFS. Voice-chat mode suppresses echo by itself, which hid it.
- **2026-10-15, Claude:** No exception leaves a native callback, and notifications run on the main thread. **Why:** a `NullReferenceException` in a notification handler ended the app on the iPhone the moment voice processing came on.
- **2026-10-15, Claude:** A configuration change restarts the engine; a rebuild only after a media-services reset or a failed restart. **Why:** turning voice processing on posts a configuration change at once, and rebuilding then raced the start.
- **2026-10-15, Claude:** On macOS the mixer and output are connected before voice processing comes on, and a refused voice-processing unit falls back to running without it, visible as `IsVoiceProcessingOn = false` and a warning. **Why:** the Mac's second take failed with `kAudioUnitErr_FailedInitialization` (−10875) until the output side was connected first.
- **2026-10-15, Claude:** No `DefaultToSpeaker` in the session; `UseSpeaker` is an override. **Why:** with it the receiver could never be chosen (the first receiver run still went to the loudspeaker).
- **2026-10-15, Claude:** The echo test reports the median of 100 ms windows and the loudest window, not the mean. **Why:** the owner talked during one take, which pulled the mean up while the phone's own voice had been removed.
- **2026-10-15, owner:** No Release build for now (the disk ran out during one); CPU in Release is in the Verification backlog.
