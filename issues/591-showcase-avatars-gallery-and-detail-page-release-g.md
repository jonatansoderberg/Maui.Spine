# Issue #591 — Showcase: Avatars gallery and detail page, release gate

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/591
**Branch:** issue/591-showcase-avatars-gallery-and-detail-page-release-g
**Status:** In Progress (B1.4 done in this branch; B1.5 waits for the owner's devices)
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), steps B1.4–B1.5, §7, D5.

## Plan

- **B1.4** Two Showcase pages in the three-file pattern, under `Pages/Avatars/`:
  - **Avatars** (gallery): the bundled set of D5 as cards, each a small live `AvatarView` at 15 fps with its name, a line, and chips for the profile and the renderer; Kairo as a download card (progress, cancel, cached in the app's data, "Forget it").
  - **Avatar** (detail, `INavigableWithParameter<AvatarEntry>`): one `AvatarView` and a segmented driver: Manual (state, expression, mute, a timed expression with intensity and duration, the avatar's gestures, Interrupt) and Text (mouth a text, or stream it in pieces like a chat answer); the renderer, the lip-sync quality and the load state under it; size 64/128/320, motion (System, Reduced, Full) and frame cap. Speak (B2) and Listen (B3) are added by their steps.
  - Bundled: Nova, Puff, Voice Totem, Aurora Flow from the lab, rewritten with the format defaults (`AvatarSlimming`) and stripped of `source/` (1.9 MB together). Kairo is served by the sample server from the lab's file (`GET /sample/avatars/kairo.spineavatar`).
- **B1.5** The release gate on the owner's iPhone, an Android phone and the Mac; Kairo measured on the iPhone. Waiting on the owner.

## Changes

- `samples/MauiSpineSampleApp/Pages/Avatars/`: `AvatarsPage`, `AvatarDetailPage` (each `.cs`, `.View.xaml`, `.ViewModel.cs`); entries in `MauiSpineSampleApp.csproj`, `GlobalXmlns.cs` (the view's and Core's namespaces, the pages'), and the index in `MainPage.ViewModel.cs` with the package name.
- `Resources/Raw/Avatars/`: `nova`, `plush-puff`, `voice-totem`, `aurora-flow` `.spineavatar` (slim, stripped).
- `MauiProgram.cs`: an unauthenticated `HttpClient` "SampleServer" for the download.
- Sample server: `GET /sample/avatars/{name}` serving only `kairo.spineavatar` from `avatars/` next to the server, linked from the lab in the csproj; README.
- `Plugin.Maui.Spine.Controls.Avatar`: `AvatarView` raises property changes for `Package`, `Representation`, `RendererName`, `Gestures` and `LipSyncQuality` (the detail page binds to them).
- Core: `AvatarRuntime` sets the quality back to None when the driver is removed (found on the detail page: Manual still said EstimatedText).

## Decisions

- **Cards show a live avatar, not the poster.** Nova's, Puff's and Kairo's posters are 1×1 placeholders until C1's `render-sheet`; a still poster can replace the live view then. At 15 fps four live cards cost 0.1–0.2 ms each on the simulator.
- **The bundled copies are made with `AvatarSlimming` and a strip of `source/`**, by a scratch tool outside the repo; C1's `pack` and `strip` will make them from now on. The lab's packages are not touched (#542).
- **Kairo is served from the lab's file**, linked in the server's csproj, rather than a second 16.6 MB copy in the repo. It still carries `source/` (229 KB); a stripped copy belongs on the server once C1's `strip` exists.
- **Gestures, timed expressions and Interrupt are the view's methods**, so the detail page hands its `AvatarView` to the view model (`Attach`) rather than inventing a manual driver for them.
- **Text mode has Stream it** besides Mouth it: `Begin`, then pieces as they arrive, then `Complete`, which is how B4.3 will wire `ChatView.Streamed`.

## Verification

### B1.4, iPhone 17 Pro simulator (iOS 26.4 runtime, Debug), the Showcase with a scratch command harness

| Check | Result |
| --- | --- |
| Gallery | Nova and Puff on SceneKit, Voice Totem and Aurora Flow on Skia (Metal), each at its 15 fps cap, 0.10–0.24 ms p50 |
| Kairo | Downloaded from the local sample server (16,607,659 bytes, progress shown), cached in `Library/avatars/`, then opened from the cache: SceneKit, 26.9 fps at 30, 0.34 ms p50 |
| Detail, Manual | State, expression, mute badge, gesture (nod), Reduced (23 → 4 fps), size 64 |
| Detail, Text | Stream it: Thinking, then Speaking with EstimatedText; back to Manual: quality None |
| Page margin | No `[Spine] … uncovered` line in the console (captured with `simctl launch --console-pty`; the harness's own lines arrived there) |

### B1.4, Android emulator Pixel 10 Pro (Debug, APK with embedded assemblies)

| Check | Result |
| --- | --- |
| Gallery | Voice Totem and Aurora Flow on Skia (CPU), 13–14 fps at the 15 cap (the emulator's compositor; see the lab's Measurements), Aurora Flow 3.0 ms p50 at 96 px; Nova, Puff and Kairo as their posters (no 3D route on Android before S2): Puff's poster is a real picture, Nova's and Kairo's are the 1×1 placeholders until C1 |
| Kairo | Downloaded through `adb reverse tcp:5100`, cached, shown as its poster |
| Detail | Nova (poster) Speaking with EstimatedText from Stream it; Voice Totem 26.5 fps at 30, Reduced drops the rate |

The page-margin check is Apple-only (`PagePresenter.Apple.cs`); nothing on Android prints it.

NotMeasured: the Azure sample server's Kairo (only the local server ran), Mac Catalyst, the owner's iPhone and an Android phone (B1.5).
