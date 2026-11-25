# Issue #878 — Android: Motion.Depth parallax judders (sensor-rate updates, not frame-synced)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/878
**Branch:** issue/878-android-motion-frame-sync
**Status:** Completed

## Plan
- Split `TiltTracker` in two: `Update` (per sensor sample) moves the neutral attitude and sets a target;
  `Advance(seconds)` (per display frame) smooths `X`/`Y` toward it by the time since the last frame.
- Android: register the game rotation vector at 200 Hz, only update the tracker there; move the views from a
  `FrameTimer` tick (Choreographer) that runs exactly while the sensor listener runs.
- Tests for the split and for frame-rate independence.
- Check in the Showcase's Motion page on the emulator: the layers still move, and the listener and frame
  callback stop in the background.

## Changes
- `Core/TiltTracker.cs`: `TargetX`/`TargetY`, `Advance(seconds)` with the 0.06 s smoothing moved out of
  `Update`; `Advance` snaps to the target within 1e-4 and then reports no change; a frame after a stall
  counts for at most 0.25 s (`MaxStepSeconds`).
- `Core/FrameTimer.cs`: `FrameSeconds`, the vsync-aligned time of the latest tick.
- `Platforms/Android/Motion.Android.cs`: sensor at 5 000 µs (200 Hz, capped by Android at the sensor's own
  rate); a static `FrameTimer` with `Interval = 0` (every frame) started and stopped with the listener in
  `Start`/`Stop`, so `Evaluate` (nothing on screen, app stopped, animations removed) stops both; views are
  translated in the frame tick, skipped when the tracker did not move.
- `TiltTrackerTests`: helpers drive `Update` + `Advance`; new tests for the target/frame split, the same
  distance at 60/90/120/144 Hz frames and 50/60/200 Hz samples, two half frames equal one full frame, the
  stall cap, coming to rest, and `Reset`.
- Checked in the Showcase's Motion page on the Pixel 10 Pro emulator (60 Hz, host under load) with a temporary
  log: the sensor delivered ~100 samples/s (the emulator's cap), the views moved once per delivered frame, and
  tilting through `adb emu sensor set acceleration` moved the layers (X ±0.2, visible in screenshots). The
  listener and frame callback stopped on Home and on leaving the page, started again on return, and did not
  start with animations removed (`animator_duration_scale 0`). Not checked on a 90/120 Hz device.

- Rest (after the Galaxy Tab S10 FE test, where the depth felt smooth but an idle Almanacka still drew ~86 of
  90 fps): the frame callback stops once the views reach the target within 0.25 px at the deepest view
  (`TiltTracker.Advance(seconds, rest)`, `IsAtRest`, `IsAway(rest)`), and a sensor sample that moves the target
  further than that starts it again. The first frame after a start moves by one display period, so the start
  of a tilt is not a frame late.

## Decisions
- **200 Hz, not `SENSOR_DELAY_GAME`.** `SENSOR_DELAY_GAME` is 20 ms (50 Hz), slower than the old 60 Hz. With
  the smoothing on frames the sample rate no longer sets the step rate, but a faster sensor keeps the target
  fresh (at most 5 ms old) so a 120 Hz display does not follow a 50 Hz staircase. The listener only runs while a
  view with a depth is on screen and the app is in front.
- **Smoothing stays an exponential with a 0.06 s time constant**, now per frame: `1 - exp(-dt/τ)` is exact
  for any `dt`, so two 120 Hz frames move as far as one 60 Hz frame and the feel does not change with the
  display's rate.
- **The neutral attitude still follows per sample** (3 s time constant): it is too slow for its steps to be
  seen.
- **Nothing is set when the tracker is at rest**, so a still device does not invalidate the views every frame.
  The Choreographer callback keeps running while the listener runs, as the sensor keeps sampling then anyway.
- **0.25 px rest, not rounding the translation to whole pixels.** Whole-pixel steps would show as judder on a
  slow tilt; a quarter pixel is below what can be seen, and above the noise of a device lying on a table
  (about a hundredth of a degree, against about 0.1° for 0.25 px at a 16 pt depth). The threshold is per
  deepest view and screen density, kept in `_rest` and recomputed when views come and go or a depth changes,
  not on each of the 200 samples a second.
