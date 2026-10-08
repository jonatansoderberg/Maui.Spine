# Issue #310 — Feature idea: MeshBackground — animated mesh and aurora gradients

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/310
**Branch:** issue/310-feature-idea-meshbackground-animated-mesh-and-auro
**Status:** Completed

## Plan

A new control package, `Plugin.Maui.Spine.Controls.MeshBackground`, in the shape of the other SkiaSharp control (`AnimatedLabel`): a `UseMeshBackground()` registration that `UseSpine()` calls through a `SpineModule` item, a README, an icon, and a project in `Spine.slnx` and `Spine.Packages.slnf`.

### The control
`MeshBackground` is a leaf view that sits behind other content in a `Grid`, as the issue's sketch has it:

```xml
<Grid>
    <MeshBackground Preset="Aurora" Drift="Slow" />
    <Border Material.Kind="Glass" …>…</Border>
</Grid>
```

- `Columns` × `Rows` control points (default 3 × 3, each 2–8), as SwiftUI's `MeshGradient(width:height:)` counts them.
- `Colors`: one colour per control point, row by row, repeated when the list is shorter. A type converter takes `"#FF5E3A, #FFCC00, …"` in XAML. Unset, the colours come from `Preset`.
- `Preset`: `Accent` (default; built from `SpineTheme.GetAccent`, so it follows `IThemeService.Accent` and light/dark), `Aurora`, `Sunset`. Each preset is a designed 3 × 3 palette per theme, sampled for other grid sizes. Repaints through `SpineTheme.Track`.
- `Drift`: `None` (default), `Slow`, `Medium`, `Fast`. Interior points move on slow Lissajous paths and edge points slide along their edge, so the mesh always covers the view.
- `FrameRate`: the cap while drifting, default 30.

### Drawing
- The control points span a bicubic (Catmull-Rom) surface in position and in colour; the surface is sampled into a fine vertex grid and drawn with one `SKCanvas.DrawVertices` call. Colour is interpolated in Oklab, so two hues blend without a grey middle.
- The canvas renders at a fraction of the screen's pixels and is scaled up by the platform compositor (a `Scale` transform on the inner canvas): a soft gradient loses nothing, and the CPU fills a small bitmap instead of a full-screen one.
- Only control points animate; nothing is allocated per frame except what SkiaSharp's `DrawVertices` needs.

### Battery
- A dispatcher timer at `FrameRate`, running only while drifting, loaded, visible, the window not stopped (app in the background), and Reduce Motion off. Reduce Motion gives the still mesh; the setting is read again when the window is resumed or activated.
- Measure the app's CPU on the simulator while the mesh drifts (`ps -o %cpu`) and record it here.

### Showcase
A page "Mesh backgrounds" (`Pages/Mesh/`) with the mesh behind glass and blur cards (`Material.Kind`), options for preset, drift, grid size and frame rate, a custom-colours example, and the code.

### Docs
`docs/wiki/mesh-background.md` with screenshots, README and `docs/wiki/packages.md` package lists, getting-started link, `spine-controls` skill section.

### Tests
The surface and palette maths have no MAUI types; they are compiled into `tests/Plugin.Maui.Spine.Controls.Tests` like `RollingNumber`.

## Open Questions

None blocking; decisions made overnight are listed below for review.

## Changes

- New package `Plugin.Maui.Spine.Controls.MeshBackground` (`src/Plugin.Maui.Spine.Controls.MeshBackground/`): `MeshBackground`, `MeshPreset`, `MeshDrift`, `MeshColorsTypeConverter`, `UseMeshBackground()` with a `SpineModule` declaration so `UseSpine()` registers it, README, icon (`assets/icons/mesh-background.png`, source in `assets/logo-src/`). Added to `Spine.slnx` and `Spine.Packages.slnf`.
- `MeshSurface`: control points → bicubic Catmull-Rom surface in position and Oklab colour, sampled 12 times per patch, drawn as one `DrawVertices` call; vertex colours computed once per palette, positions once per frame, no per-frame allocations of our own.
- `MeshPalettes`: 3 × 3 designs for `Aurora` and `Sunset` (light and dark), and an `Accent` design built from `SpineTheme.GetAccent` in Oklch; sampled for other grid sizes. `Oklab` conversions.
- The canvas is laid out at `size / (2 × density)` by a private `ScaledCanvasHost` layout and scaled up with a `Scale` transform (GPU), clipped to the view. On Android the canvas gets a hardware layer with a filtering paint, because Android otherwise scales the view's drawing nearest-neighbour (visible 4-pixel blocks).
- Animation: a dispatcher timer at `FrameRate` while drifting; stopped when unloaded, the handler is lost, the mesh is hidden, the window is stopped (background), or Reduce Motion is on (read again on window Activated/Resumed). Each tick skips the redraw when an ancestor is hidden or the mesh is scrolled out of the window (iOS `ConvertRectToView`, Android `GetGlobalVisibleRect`).
- Tests: `tests/Plugin.Maui.Spine.Controls.Tests/MeshSurfaceTests.cs` (edges stay on the edges at every phase, no fold-over for 3 × 3, 5 × 4 and 8 × 8 over 160 phases, control colours reproduced, triangle indices, repetition, preset sampling, accent preset light/dark, Oklab round trip and non-grey blend).
- Showcase: page "Mesh backgrounds" (`Pages/Mesh/`) with three examples (behind glass with options for preset, drift, grid and frame rate; day themes from `Colors`; a two-colour list in XAML) and a full-screen page (`MeshFullScreenPage`) with the mesh behind the overlay header bar and glass/blur/tint panels. `SampleIndex`, `GlobalXmlns.cs`, csproj.
- Docs: `docs/wiki/mesh-background.md` with two screenshots; README (packages and docs tables, sixteen packages), `docs/wiki/packages.md`, `getting-started.md`, `agent-skills.md`, a pointer from `materials.md`, a section in `.claude/skills/spine-controls/SKILL.md`.

## Decisions

- **Own package** (`Plugin.Maui.Spine.Controls.MeshBackground`), like every other control, rather than inside the core or AnimatedLabel: the core does not depend on SkiaSharp, and an app that only wants navigation should not get it. It references the core for `SpineTheme`.
- **`DrawVertices` over a bicubic surface instead of `DrawPatch`.** The issue names Coons patches. Skia's `DrawPatch` interpolates each patch's four corner colours bilinearly, which leaves visible creases along the patch edges and star shapes at the points (the reason SwiftUI added `smoothsColors`). The control points still define the surface; it is sampled into a triangle grid instead of handing Skia one patch per cell. `SKPicture` caching (issue note) does not apply: a still mesh is drawn only on invalidation, and a drifting one changes every frame.
- **Colour blended in Oklab**, so hue pairs do not pass through grey.
- **`Drift` defaults to `None`.** The issue says "optionally drifting"; a still mesh costs nothing after its first draw, and drift is a deliberate opt-in. `Slow` ≈ 30 s a round, `Medium` 15 s, `Fast` 7.5 s.
- **A still mesh is the drift's first frame**, not the regular grid: points sit up to a quarter of a cell from their grid places, which looks less mechanical and makes switching drift on continuous. Drift reach is 0.25 of a cell; 0.32 folded the 8 × 8 mesh in the test.
- **Columns × Rows count points**, as SwiftUI's `MeshGradient(width:height:)` does, 2–8 each. `Colors` cycles when shorter, so `Colors="#A, #B"` works for any grid.
- **Presets are 3 × 3 designs sampled for other sizes** rather than separate designs per size. `Accent` (default) is built from the accent in Oklch; Aurora and Sunset are fixed. A grey accent gives a neutral mesh.
- **Rendered at half the view's size in points, scaled by the compositor.** On the iPhone 17 Pro simulator a drifting full-screen mesh cost ~9 % CPU (whole app; 3 % still) against ~25 % drawn at full resolution. A soft gradient shows no difference; checked in full-resolution crops on iOS and Android.
- **Off-screen**: the timer stops for detached/hidden/background; for a hidden ancestor or scrolled out of sight the timer keeps ticking (cheap) and skips the draw, because MAUI raises nothing for those.
- **Reduce Motion is read when the window is activated or resumed**, not observed: the setting is changed in the system's settings, which takes the app out of the foreground. Simpler than Shimmer's observers.
- **The drift phase is accumulated per tick** (speed × time), so changing `Drift` carries on from where the points are; it resets to 0 (the still pose) when drift is turned off or Reduce Motion comes on.
- **Android hardware layer with a filtering paint** on the canvas: without it Android's view transform scaled the small bitmap nearest-neighbour and the dither showed as a faint 4-pixel checker.
- **Showcase icon**: `sunset.svg` from the bundled set (no mesh glyph exists; drawing one with `/spine-symbol` needs a pick by Jonatan).

## Verification

- **iOS** (iPhone 17 Pro simulator, iOS 26): all three examples and the full-screen page in light and dark, presets Accent (with a purple accent), Aurora and Sunset; glass, blur and tint panels over the mesh. ~29 redraws a second at `FrameRate` 30. Timer stops on a covered page (the page under the full-screen page has no window), in the background (Window.Stopped; 0 % CPU), with Reduce Motion on (`ReduceMotionEnabled`, relaunch) and with `Drift="None"`; a mesh scrolled out of the window is not redrawn. `Colors` from a XAML string works (type converter under the sample's XAML compiler). The full-screen page's mesh was collected after going back.
- **CPU** (`top`, whole app, simulator, Debug): full-screen Aurora behind three panels — still ~3 %, Slow at 30 fps ~9–14 %, at 60 fps ~14 %; at full resolution (experiment) ~25 %.
- **Android** (emulator-5556, which was a Pixel Tablet AVD tonight, Android 16): same pages, light and dark, blur panels over the mesh, smooth upscaling after the layer fix (checked in a contrast-boosted crop), timer stops on a covered page and in the background (0–3 % CPU). The emulator reached only ~14 redraws a second for a full-screen drifting mesh: per frame the control's own work is ~5 ms (0.5 ms geometry, 4.5 ms raster of 402 × 642), the emulated GPU issue ~23 ms, and with three blur panels Spine's blur re-recording adds ~23 ms to the traversal (CPU 8–22 % without panels, 30–70 % with). Real-device numbers are not measured.
- **Builds**: package for iOS, Mac Catalyst, Android, Windows (compiled on the Mac); Showcase for iOS, Android and Mac Catalyst; `dotnet pack` contains the icon, README and the `SpineModule` props; Controls tests 51/51.

