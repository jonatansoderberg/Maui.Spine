# Issue #478 — Widgets: concurrent refreshes fail with IOException on a shared asset

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/478
**Branch:** issue/478-widgets-concurrent-refreshes-fail-with-ioexception
**Status:** In Review

## Plan
Two refreshes run at once when the app's launch refresh (`RefreshAllInBackground` from `OnCreate` / foreground) meets a background run (the `SpineBackgroundReceiver` alarm, or the `spine.widgets` JobScheduler job from #308, which starts right after it is booked). Seen on emulator-5556 (Pixel Tablet AVD) as `IOException … team-owls.png … being used by another process`.

Causes found in `Plugin.Maui.Spine.Widgets`:
- `StoreAssetAsync` (Android and Apple) writes in place with `File.Create`, which .NET opens exclusively (an advisory `flock` on Unix). A second writer of the same asset id throws; a reader through .NET throws too, and `BitmapFactory.DecodeFile` or the Swift extension can read a half-written PNG.
- `WriteTimeline` and Android's remote cache use one fixed `<kind>.json.tmp`, so two writers share the temporary file (`FileNotFoundException` on the move, or a sharing violation).
- Two `RefreshAsync` calls for the same kind interleave, so an older build can be written after a newer one.
- `WidgetIconAssets` marks an icon stored before writing it, so a parallel refresh writes its tree before the icon exists.

Approach — both halves, because neither covers the other:
1. Write every file the renderer reads through a temporary file with its own name and an atomic rename (`AtomicFile`). Covers different kinds, Live Activities and app code storing the same asset, and torn reads.
2. Serialize `RefreshAsync` per kind with a `SemaphoreSlim`, so the last write is the last build.
3. Serialize `WidgetIconAssets.EnsureAsync` and mark an icon stored only after it was written.

## Changes
- `Services/AtomicFile.cs`: `WriteAllText` and `WriteAsync` write `<path>.<guid>.tmp` and `File.Move(..., overwrite: true)` it over the target; the temp file is deleted on failure.
- Android `WidgetPlatform.WriteTimeline`/`StoreAssetAsync`, `SpineAppWidget.FetchRemoteAsync` (remote cache) and the picker-preview hash file use it.
- Apple `WidgetPlatform.WriteTimeline`/`StoreAssetAsync` use it.
- `WidgetService.RefreshAsync(kind)` waits on a per-kind `SemaphoreSlim` around build, icons, write, remote fetch and reload.
- `WidgetIconAssets.EnsureAsync` runs under one `SemaphoreSlim`; a name is added to the stored set after the write succeeds, so a failed write is retried.
- `IWidgetService` docs and `docs/wiki/widgets.md` ("Refreshing from the app") say that refreshes of one kind run one at a time and that `StoreAssetAsync` replaces a picture whole.

## Verification
A scratch harness (a `[ModuleInitializer]` injected with `CustomBeforeMicrosoftCommonTargets`, not committed) ran rounds of `RefreshAsync(kind)` twice per kind plus a `RefreshAllAsync`, all on the thread pool at once, with the sample's "logos stored" preference cleared so the score provider writes the team logos every round. After the rounds it checked that every stored PNG ends in `IEND` and that no `.tmp` file is left behind.

| Build | Device | Rounds | Failed refreshes |
|---|---|---|---|
| origin/master | emulator-5556 (Pixel Tablet AVD) | 15 | every round: `IOException` on `team-owls.png`, `sample_card.png`, …; `FileNotFoundException` on `sample.json.tmp` |
| fixed | emulator-5556 | 30 | 0 |
| origin/master | iPhone 17 Pro simulator (iOS 26.4) | 15 | 46 |
| fixed | iPhone 17 Pro simulator | 30 | 0 |

Cold-launch race with the fix (the launch refresh plus a `RefreshAllAsync` from the harness as the app starts, logos cleared beforehand): 6 launches on Android and 3 on iOS, no errors in the log or logcat, all PNGs whole, and no temporary files once both refreshes finished. The placed Spine sample widget on the Android home screen still draws its icon.

Builds: Widgets for android, ios, maccatalyst and the Windows TFM (compiled on the Mac).

Not verified: the JobScheduler path from #308 (that branch is not merged; it ends in the same `RefreshAllAsync`), a physical device.

## Decisions
- **A waiting second refresh instead of a dropped or coalesced one.** A refresh asked for after a push or a button tap may have newer data than one already running, so it builds again once the first is done. A background run that waits stays inside its budget, because the token it passes is honoured while waiting.
- **The gate is per kind, not global.** Different kinds do not share a timeline, and their shared assets are covered by the atomic write, so they still build in parallel.
- **A unique temporary name per write.** A fixed `.tmp` per file would race between writers again. A temp file left by a killed process is harmless and is not cleaned up.
- **The atomic write lives in the platform layer**, so `StoreAssetAsync` called directly by app code (the sample's `TeamLogos`, `CardWidget`, Live Activity starts) is covered too, not only the refresh path.
