# Issue #755 — Widgets: layout files (*.layout.xml) bound to a model, in tree mode

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/755
**Branch:** issue/755-widgets-layout-files
**Status:** Completed

Step W12 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), §5, decisions D9 and D11.

## Plan

A Roslyn incremental generator, shipped inside `Plugin.Maui.Spine.Common`'s package, turns each `*.layout.xml` into a static class of `W.*` calls. Done when Glukos's two files write exactly the JSON `GlucoseActivity.Layout` writes, in every case its tests draw.

## Changes

- `src/Plugin.Maui.Spine.Layouts.Generator` (netstandard2.0, Roslyn 4.0): `Markup` (bindings and extensions, nested and quoted), `Vocabulary` (elements, attributes, enums; the XSD is written from it), `LayoutCompiler` (XML to C#, scopes, narrowing, templates as local functions, `#line` per node), `LayoutGenerator` (layouts, `*.resources.xml` and `strings*.xml` from the additional files), `Diagnostics` (`SPL001`–`SPL015`).
- `Plugin.Maui.Spine.Common`: `LayoutValues` (what generated code calls: a color from text, a setter only when a value is there, `Share`, `Each`); the generator in `analyzers/dotnet/cs`; the build file adds the additional files (`SpineLayouts=false` turns it off); `build/spine-layout.xsd`.
- `tests/Plugin.Maui.Spine.Layouts.Tests`: Glukos's files against a copy of its C# (seven cases), a widget with every structure and extension against the same tree in C# (two languages), every diagnostic with its line, a compiler error mapped into the file, the XSD against the vocabulary. In CI.
- Glukos, local branch `spine/layout-files` (not pushed): `GlucoseActivityModel`, `Layouts/GlucoseActivity.layout.xml`, `Layouts/GlucoseNoValue.layout.xml`, `GlucoseActivityLayoutFileTests` (ten cases, with the graph and without).
- Docs: `docs/wiki/widget-layout-files.md`; links from `widgets.md` and `packages.md`; the plan's §5 notes and step table; the examples' Glukos note.

## Decisions

- **Same JSON first.** Where two C# forms differ in JSON the file names the one it means; the examples were corrected where they would have changed Glukos's payload (see the plan's §5 notes).
- **Roslyn 4.0** for the generator, so every SDK since .NET 6 runs it; the tests compile with Roslyn 5.
- **Extension methods need a `using`**: the generated file has `using Plugin.Maui.Spine.Common;` and qualifies everything else with `global::`.
- **`{Loc}` takes `ISpineStrings`** rather than a culture, so the app and a server pass what they already have.
- **SPL012 checks icons only when the project can see `SpineIcons` or `AppIcons`**; a project with neither (Glukos.Shared) is not checked, and dotted SF names are left alone.

## Verification

- `Plugin.Maui.Spine.Layouts.Tests`: 41 passed. `Plugin.Maui.Spine.Server.Tests`: 308 passed.
- Glukos with Spine packed as 0.0.0-local: 225 passed, the 20 new among them; `Glukos.Shared` builds with no warnings (SPL013 is suppressed there, as its graph has no description today).
- The XSD validates Glukos's file with `xmllint`.
