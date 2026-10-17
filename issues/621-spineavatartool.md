# Issue #621 — Spine.Avatar.Tool

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/621
**Branch:** issue/621-spineavatartool
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), step C1

## Plan
- A `net10.0` .NET tool, `spine-avatar`, on Core and the Skia renderers of Controls.Avatar, building on the scratch `slimtool` that made the Showcase's bundled copies in B1.4.
- Commands: `inspect`, `validate [--profile shipping]`, `pack [--strip] [--poster-light --poster-dark]`, `strip`, `render-sheet`, `report`.
- Posters drawn with the Skia renderers (vector, shader, glTF without masks); Kairo's captured from SceneKit on the simulator.
- Prove it on copies of the five reference packages outside the repo (#542 is open: the lab's Tools and committed Kairo stay untouched).

## Changes
- `src/Plugin.Maui.Spine.Avatar.Tool`: `Program.cs` (the commands, the options, the shipping profile), `AvatarFiles` (read with the report, write with a fresh SHA-256 table, PNGs stored), `AvatarPainter` (the first representation a Skia renderer takes, posed under Reduce Motion after 1.5 s in a state, drawn on a transparent square; sheets of states and expressions with labels), README. `PackAsTool`, command `spine-avatar`; SkiaSharp's Linux library added; native assets trimmed to six platforms without symbols.
- Core and Controls.Avatar give the tool their internals (`InternalsVisibleTo spine-avatar`).
- `Spine.slnx` and `Spine.Packages.slnf` list the tool; `Directory.Packages.props` has `SkiaSharp.NativeAssets.Linux.NoDependencies`.
- Tests: `AvatarToolTests` (pack renders real posters and passes the shipping profile for Nova, Puff, Voice Totem and Aurora Flow; the profile turns down the lab's Nova for its placeholders and `source/`; strip keeps Kairo loadable without `source/`; a SceneKit-only model keeps its posters unless given some).
- Showcase: the four bundled avatars repacked with the tool (512 px posters, no `source/`).
- Docs: `avatars.md` (Making a package), `packages.md` (the tool; and Voice ──► Speech, missed in #620), the plan (C1 row, §3, §8.1, backlog).

## Measurements
| Package | Before | After `pack --strip` | Posters | Shipping profile |
| --- | --- | --- | --- | --- |
| Kairo | 15.84 MB, 1×1 posters, `source/` | 16.52 MB | 512 px, captured from SceneKit | Pass 12, Warning 1 (size: download it) |
| Nova | 655.7 KB, 1×1 posters, `source/` | 748.8 KB | 512 px, Skia glTF | Pass 13 |
| Puff | 740.8 KB, 320 px, `source/` | 707.3 KB | 512 px, Skia glTF | Pass 13 |
| Voice Totem | 349.2 KB, 512 px, `source/` | 342.9 KB | 512 px, Skia spine2d | Pass 12 |
| Aurora Flow | 103.4 KB, 320 px, `source/` | 209.1 KB | 512 px, Skia SkSL | Pass 12, Warning 1 (gaze supported but not declared, as decided in B0) |

`strip` alone on Kairo: one file removed, the package loads (every file checked against the new table). The tool package: 34 MB (86 MB before trimming SkiaSharp's native assets); installed with `dotnet tool install --tool-path` and run.

## Decisions
- **Package id `Plugin.Maui.Spine.Avatar.Tool`**, like every Spine package, command `spine-avatar` as the authoring profile already names it.
- **Skia posters, SceneKit captures passed in.** A .NET tool is a plain console app and cannot host SceneKit; S2's glTF renderer draws Nova and Puff on any OS. Kairo's alpha-masked hair needs SceneKit, so its posters were captured from an `SCNView` snapshot (transparent background, Reduce Motion, light and dark) in a scratch app and passed with `--poster-light`/`--poster-dark`.
- **A poster is the idle, neutral pose under Reduce Motion**, so it is never caught mid-blink or mid-gesture; 512 px by default, transparent so it sits on either theme.
- **The shipping profile** fails what the authoring profile forbids in a shipped package (placeholder posters, `source/`) and warns where the advice is a choice (bundle under about 2 MB).
- **Trimmed native assets**: six platforms, no native symbols; a rarer platform gets SkiaSharp's own error.
- **The gallery keeps live avatars** on its cards for now; with real posters it could show them instead (C2 or later).

## Not verified (NotMeasured)
- The tool on Windows and Linux (built and run on the Mac only).
- The lab's packagers on the tool, and the lab's and sample server's Kairo replaced: waiting for #542.
