# Layout files: the gaps Almanacka found

**Branch:** fix/layout-files-almanacka-gaps
**Status:** Completed

Found while moving Almanacka's widgets to layout files (Spine 0.1.39), each of which forced a piece back into C#.

## Changes

- **A binding inside a fallback or prefix.** `{Note, Fallback='v {Week}'}` wrote "v {Week}" as it stood, and the unquoted `Fallback=v {Week}` was cut at the inner brace, both without a word. A quoted value, and an unquoted one up to the comma or brace outside its own braces, is now read as text that may hold bindings (`Markup.ReadItem`).
- **A background picture on a widget.** `<Widget BackgroundImage="{ThemeAsset}">`, a stored asset over `Background`; `Surface(m)` carries both (`WidgetSurface(color or gradient, image)`, or the picture alone).
- **A fixed surface goes on the timeline once.** The generated `Timeline` gave every entry the surface even when it never changed; now a surface that does not depend on the model is the timeline's (`Background`, `BackgroundImage`), and only one bound to it is each entry's, carrying the background too because an entry's surface replaces the timeline's.
- Tests: `TeamLayoutTests` (both fallback forms against the C# tree, a bound picture per entry, `Paper.layout.xml` with a fixed surface on the timeline); the XSD has `BackgroundImage`. Docs: `widget-layout-files.md`.

## Verification

- `Plugin.Maui.Spine.Layouts.Tests`: 69 passed.
