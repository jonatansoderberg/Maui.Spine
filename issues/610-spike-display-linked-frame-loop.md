# Issue #610 — Spike: display-linked frame loop

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/610
**Branch:** issue/610-spike-display-linked-frame-loop
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), step S4

## Plan
- Reproduce the lab's 52–56 fps (iPhone, `IDispatcherTimer`, 60 cap) on the simulator.
- Try a `CADisplayLink` outside the packages first: the scratch app hosts `AvatarView` (`IsHosted`) and ticks it from a link (`TickFromHost`).
- If it holds 60, put one display-linked timer behind `AvatarView` and `VoiceView`, shaped like `IDispatcherTimer` so the views change by two lines each.

## Changes
- `Plugin.Maui.Spine/Core/FrameTimer.cs` (internal): `Interval`, `IsRunning`, `Start`, `Stop`, `Tick`. iOS and Mac Catalyst: `CADisplayLink` on the main run loop (common modes) with `PreferredFrameRateRange` at the rate. Android: `Choreographer` frame callbacks, posted again each frame. Windows: `CompositionTarget.Rendering`. A due time advancing by the interval decides which frames tick; a stall ticks once.
- `Plugin.Maui.Spine.csproj`: `InternalsVisibleTo` for Controls.Avatar and Controls.Voice.
- `AvatarView` and `VoiceView`: `FrameTimer` instead of `Dispatcher.CreateTimer()`.
- Plan: §4.7, the S4 row, §8.1, the backlog, Last updated.

## Measurements
iPhone 17 Pro simulator (iOS 26.4), Debug, the scratch app:

| Loop | Avatar | Cap | fps | Render p50 / p95 | Per frame |
| --- | --- | --- | --- | --- | --- |
| `IDispatcherTimer` (before) | Dotling | 60 | 56.1 | 0.16 / 0.27 ms | 0 B |
| `IDispatcherTimer` (before) | Voice Totem | 60 | 56.4 | 0.08 / 0.15 ms | 0 B |
| `CADisplayLink` from the scratch app | Dotling, Voice Totem | 60 | 60.0, 60.0 | same | 0 B |
| `FrameTimer` in `AvatarView` | Dotling | 60 / 30 / 15 | 60.0 / 30.0 / 15.1 | 0.15–0.16 / 0.26–0.28 ms | 0 B |
| `FrameTimer` in `AvatarView` | Voice Totem; Nova (SceneKit) | 60 | 60.0; 60.0 | 0.08; 0.27 / 0.42 ms | 0 B |
| `FrameTimer`, Reduce Motion | Dotling | 4 | 4.0 | 0.15 ms | 0 B |
| `FrameTimer` in `VoiceView` (character, Showcase Assistant page) | Nova | 30 (VoiceView's default) | 30 (150 frames in 5 s) | 0.31–0.38 ms | — |

Android emulator: the `Choreographer` path ticks and draws (Voice Totem, Dotling), at 3.6–3.8 fps; the host was at load average 20–23, and the old timer gave 0.8–3 fps there in #607's runs, so this says nothing about the loop.

## Decisions
- **Internal, in the core, shared by `InternalsVisibleTo`.** Both views depend on `Plugin.Maui.Spine`; a public timer would add API beyond the plan's §5, and the packages are released together from one tag.
- **The link asks for the rate on Apple** (`PreferredFrameRateRange` with minimum, maximum and preferred at the rate), so a ProMotion display may run slower for it instead of waking for frames that are skipped; the due-time check stays as a guard.
- **Frames skipped against a due time** on Android and Windows (and as the guard on Apple), with 2 ms tolerance because the timestamps are vsync-aligned: 30 at 60 Hz is every other frame, 60 at 120 Hz every other frame, 60 at 90 Hz two frames in three.
- **The views keep their own `Stopwatch` deltas**, as before; the timer only says when.

## Not verified (NotMeasured)
- 60 fps on the iPhone at a 60 cap, the step's done-criterion (the simulator reproduced the old 56 and now gives 60.0).
- `Choreographer` on an Android phone; `CompositionTarget.Rendering` on Windows (compiled on the Mac, never run).
