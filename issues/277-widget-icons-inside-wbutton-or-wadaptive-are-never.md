# Issue #277 — Widget icons inside W.Button or W.Adaptive are never rasterized

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/277
**Branch:** issue/277-widget-icons-inside-wbutton-or-wadaptive-are-never
**Status:** In Progress

## Plan

Root cause: `WidgetIconAssets.Icons` (src/Plugin.Maui.Spine.Widgets/Services/WidgetIconAssets.cs) recurses into `StackNode.Children` only. `ButtonNode.Child`, `AdaptiveNode.Fallback` and `AdaptiveNode.Trees` are the other nodes with children (src/Plugin.Maui.Spine.Common/Core/WidgetNode.cs); an icon only inside them is never stored as `icons/<name>.png`. Live Activity layouts use the same walk.

1. Make the walk cover every node with children: stacks, `ButtonNode.Child`, `AdaptiveNode.Fallback` and every `AdaptiveNode.Trees` value.
2. Unit test: a tree with icons only inside a button, an adaptive fallback, an adaptive per-family tree and a nested stack yields all of them, once each.
3. Remove the Troubleshooting row about this bug from docs/wiki/widgets.md (added in #276).

## Open Questions

None. Jonatan chose: the walk moves to Common as an internal helper; the docs row goes in a last commit once #276 is merged.

## Changes

- `WidgetTree.Icons` (src/Plugin.Maui.Spine.Common/Core/WidgetTree.cs, internal): walks stacks, `ButtonNode.Child`, `AdaptiveNode.Fallback` and every `AdaptiveNode.Trees` value.
- `WidgetIconAssets` uses it instead of its own stack-only walk.
- Common gets `InternalsVisibleTo` for Plugin.Maui.Spine.Widgets and Plugin.Maui.Spine.Server.Tests.
- `WidgetTreeTests` in Plugin.Maui.Spine.Server.Tests: icons inside a button, an adaptive fallback, an adaptive per-family tree and nested stacks are all found.
- Pending: remove the Troubleshooting row from docs/wiki/widgets.md after #276 is merged.

## Decisions

- The walk lives in Common, not Widgets: Widgets targets only MAUI platforms, so no net10.0 test project can reference it. Internal rather than a public `Descendants()`, to add no API surface.
- The renderers need no change: iOS (`SpineWidgetRenderer.swift`) and Android (`WidgetIcons`) look up `icons/<name>.png` by name wherever the icon sits; only the stored asset was missing.
