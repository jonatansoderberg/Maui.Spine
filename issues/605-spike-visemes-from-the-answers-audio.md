# Issue #605 — Spike: visemes from the answer's audio

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/605
**Branch:** issue/605-spike-visemes-from-the-answers-audio
**Status:** Completed (verdict: parked)
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), spike S1.

## Plan

A heuristic classifier from 20 ms frames of the answer's PCM to Azure's viseme ids, with hysteresis, measured for cost and allocations, against the lab fixture, and on real speech; internal so that parking it leaves no API behind.

## Changes

- `Plugin.Maui.Spine.Voice/Core/AudioVisemes.cs`: `AudioVisemeClassifier` (internal).
- `tests/…Avatar.Core.Tests/AudioVisemeSpikeTests.cs`: the measurements (the fixture's agreement, the reference speech's distribution, cost and allocations); the Showcase's `voice-reference.wav` linked into the test fixtures.
- `docs/proposals/spine-voice.md`: an appendix with the numbers and the verdict.

## Decisions

- **Parked**, not shipped: the shapes cannot be validated without phoneme-timed speech, and labelling them TimedVisemes would overclaim. Revisit in S3 against Azure Speech's own visemes on its own audio.
- **No phoneme synthesizer was installed** to get a ground truth: it would change the owner's system.
- **Thresholds were calibrated on the reference speech's own features** (voiced frames −38 to −12 dBFS; the mid band 3.5–13 % of the low band's energy); the first, guessed thresholds gave 59 % "ow".
- The classifier stays in the repository, internal, with its tests, for the S3 comparison.

## Measurements (Mac, Debug)

| Measure | Result |
| --- | --- |
| Cost | 5.6 µs per 20 ms frame, 0 B |
| Fixture (tones) | 26.0 % exact, 40.5 % same group |
| Reference speech, 13.6 s | sil 23 %, aa 36 %, eh 10 %, ow 11 %, uw 6 %, s 6 %, p 5 %, iy 2 %, ae 1 %; 10.4 changes/s |
