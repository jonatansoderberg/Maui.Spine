# Issue #747 — Widgets: icons from SVG only, prepared when the app starts

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/747
**Branch:** issue/747-widgets-icons-from-svg-only-prepared-when-the-app-starts
**Status:** Completed

Step W4 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), decision D3.

## Plan

- `W.Icon(SpineIcons.X)` finds its SVG: strip the constants' `.svg` before the lookup.
- No SF Symbol for a name that is not an SF Symbol's: iOS draws nothing without the SVG, as Android does.
- `SpineWidgetsOptions.Icons`: icons drawn when the app starts, so a pushed layout finds them.
- `AppIcons`: a constant per SVG the app embeds, written by the build.

## Changes

- `WidgetIconAssets`: `Open` strips `.svg`; a name with no SVG is logged once (`ILogger<WidgetIconAssets>`) and not tried again.
- `SpineWidgetRenderer.swift`: the SF Symbol fallback only for dotted names.
- `SpineWidgetsOptions.Icons`; `RefreshAllInBackground` (at launch and when the app goes to the background) draws them first.
- `Plugin.Maui.Spine.Svg/build/Plugin.Maui.Spine.Svg.targets`, packed as `build` and `buildTransitive`: `SpineAppIcons.g.cs` with `internal static class AppIcons` in the app's root namespace, from `@(EmbeddedResource)` and `@(MauiImage)` `.svg` items, names deduplicated; `SpineAppIcons=false` turns it off.
- Docs: `widgets.md` Icons; the plan's W4 row and a note on D3.

## Decisions

- **Dotted SF names keep their iOS fallback.** Glukos shows `exclamationmark.circle` beside a value that stopped coming; there is no SVG of that name, so dropping the fallback for every name would silently take the mark away on iOS. New names (PascalCase constants, app file names) never fall back, so the two platforms agree for them.
- **`Icons` on the options, not an attribute.** One list, read at launch, is enough until W12's generator can collect the icons a layout file names.
- **`AppIcons` by MSBuild, not a source generator.** The items are the build's own (`EmbeddedResource`, `MauiImage`); a target writing one file is simpler and runs in every IDE. It is imported from the NuGet package only, so Spine's own projects (ProjectReference) do not get it.

## Verification

- A scratch project importing the targets with three SVGs (`drop`, `figure_run`, `my-logo`, one also a `MauiImage`): `AppIcons.Drop`, `Figure_run`, `My_logo` compiled and used from code.
- The Android library builds; `swiftc -typecheck`. Drawn on devices with W1–W5 after W5.
