# Issue #462 — Scanner: Android close-up zoom, teardown on detach, watchdog and lifecycle fixes from the PWOS port

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/462
**Branch:** issue/462-scanner-android-close-up-zoom-teardown-on-detach-w
**Status:** In Progress

## Plan

All work is in `src/Plugin.Maui.Spine.Scanner`. One commit per numbered item, so each can be reviewed and reverted alone.

### 1. Android close-up zoom
- `ScannerCamera.CloseUpZoom(ICameraInfo, viewW, viewH, frameW, frameH)`, the same model as Apple's `CloseUpZoom`: from `Camera2CameraInfo.From(info)` read `LENS_INFO_MINIMUM_FOCUS_DISTANCE` (diopters → mm), `LENS_INFO_AVAILABLE_FOCAL_LENGTHS` and `SENSOR_INFO_PHYSICAL_SIZE`; take the upright frame and the `PreviewView`'s fill-centre crop → visible width at the closest focus → `clamp(visible * 0.5 / 37 mm, 1, min(3, maxZoomRatio))`, 1 below 1.05.
- `ApplyCloseUpZoom()` on the main thread: after each bind, on `PreviewView.LayoutChange` (tablet turned), and once the first frame gives the upright frame size. Applied once per (view size, frame size). 1× while a light grid is read, as on Apple (`ApplyLens`).
- `FrameReader` gets a `Zoom` value and `TakeDiagnostics` adds `zoom 1.8×` when above 1, on both platforms.

### 2. Release everything on leaving the window
- **Android:** `SetOnScreen(false)` → `Release()`: unbind, remove the camera-state and lifecycle observers, close the ML Kit client (see 3). `Shutdown()` becomes `Release()` plus the `_shutdown` flag. The next attach binds afresh.
- **Apple:** `MovedToWindow` with no window → `Release()`: dispose the observers, and on the session queue stop the session, remove inputs/outputs, clear the output's sample-buffer delegate, drop `_frames` and `_device`, `_configured = false`. Configuration moves onto the session queue too, with a `_generation` counter bumped by each release, so a release queued before a configuration cannot take apart what it sets up afterwards (and a configuration overtaken by a release backs out).
- Check that a closed Spine sheet with `BarcodeScannerPage` now leaves no capture session / executor thread behind (iPhone + emulator).

### 3. Android `RejectedExecutionException`
- One app-wide `static readonly Lazy<IExecutorService>` analysis executor, never shut down.
- `Release()` closes the ML Kit client by submitting to that executor, so it runs after any frame still being read; the analyzer checks a released flag there so a late frame cannot create a new client.

### 4. Watchdog only restarts a stuck camera
- **Android:** observe `CameraInfo.CameraState` with `ObserveForever` (removed in `Unbind`); `CheckFrames` restarts only when the state is OPEN and no frame came for 2 s. `Unbind()` copies `PreviewView.Bitmap` into the still only when pausing on screen (`IsScanning = false` after a hit), not on release or a watchdog restart.
- **Apple:** `_interrupted` set in `WasInterrupted`, cleared in `InterruptionEnded` (with the frame clock reset); the watchdog skips while set, so `Interrupted` stays visible.

### 5. Android reports `Interrupted`
- From the same `CameraState` observer: `ERROR_CAMERA_IN_USE`, `ERROR_MAX_CAMERAS_IN_USE`, `ERROR_DO_NOT_DISTURB_MODE_ENABLED` → `Interrupted` (cleared when the camera opens again); `ERROR_CAMERA_DISABLED`, `ERROR_CAMERA_FATAL_ERROR`, `ERROR_STREAM_CONFIG`, `ERROR_CAMERA_REMOVED` → `Failed`.

### 6. Android picks up a permission granted in Settings
- After `PermissionDenied`, add an `ILifecycleEventObserver` to the activity's lifecycle; on `ON_RESUME` only `CheckStatusAsync` (never request) and call `Update()` once granted. Removed on release and when granted.

### 7. `IsTorchOn` written back when the camera stops
- `BarcodeScannerView.SetTorchOff()`; the handlers call a `TorchStopped` hook when scanning stops or the view leaves its window, not on an internal watchdog restart. On Apple this replaces the unused `TorchChanged`.
- `BarcodeScannerPage` hides its torch action while a problem is shown.

### 8. Aim selection (lower priority)
- Platforms deliver every code of a frame: Android drops the `break`, Apple returns all Vision observations. `FrameHit` becomes a frame's list of hits plus a frame number.
- `BarcodeScannerView`: `ScanArea` tests each code's centre (corner average) and picks the one nearest the area's centre; doc comment updated. A code without corners counts only when no `ScanArea` is set.

### 9. Confirmation reads (lower priority)
- New `ConfirmationReads` (default 2): the same value must be read in frames at most 2 apart, with no other value in between, before it is reported. Light-grid hits always count as confirmed (slow at 10–15 fps, with Data Matrix error correction).
- Each platform numbers its analysed frames (counted in `FrameReader`), restarting from 1 on each bind; a frame number that does not move forward starts over.

### Docs and verification
- `BarcodeScannerView` remarks, `src/Plugin.Maui.Spine.Scanner/README.md`, `docs/wiki/barcodes.md` and `.claude/skills/spine-controls/SKILL.md`: Android close-up zoom, centre-based `ScanArea`, `ConfirmationReads`.
- Build Android and iOS. Test on the emulator (camera in use by another app, permission granted in Settings, background/foreground without false NoFrames) and the iPhone (interruption, sheet closed repeatedly, torch written back). The tablet zoom can only be confirmed on a device with a wide lens; the diagnostics line shows the zoom.

## Open Questions

- None. Scope (all items incl. 8 and 9) and the `ConfirmationReads` default (2 for standard codes, light grid always 1) decided by Jonatan 2026-10-07.

## Changes

- **Release on leaving the window (2, 3).** Android: `ScannerCamera.Release()` unbinds and closes the ML Kit client on the analysis thread; leaving the window releases, pausing on screen only unbinds and keeps the still. One app-wide analysis executor, never shut down; `_analysing` keeps a late frame from creating a new client. Apple: `Release()` bumps `_generation`, disposes the observers and empties the session on its queue (clearing the output's delegate); `Configure` runs on the session queue, and a configuration overtaken by a release starts clean.
- **Watchdog and camera state (4, 5).** Android observes `CameraInfo.CameraState` with `ObserveForever` (removed in `Unbind`); the watchdog restarts only an OPEN, uninterrupted camera that sent no frame for 2 s, and the frame clock starts when the camera opens. Camera-in-use / max-cameras / do-not-disturb errors report `Interrupted` (cleared when it opens again); disabled / fatal / stream-config / removed report `Failed`. Apple sets `_interrupted` in `WasInterrupted`, clears it and resets the frame clock in `InterruptionEnded`, and the watchdog skips while it is set.

## Decisions

- Work in a git worktree (`../Maui.Spine-462`) off `origin/master`, since the main checkout is shared and behind.
- `ConfirmationReads` defaults to 2: a partly seen linear code can decode as another valid number; the second read costs one frame. Light-grid reads skip it.
