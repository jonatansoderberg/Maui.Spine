# Issue #898 — CodeField: a code typed into cells

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/898
**Branch:** issue/898-code-field
**Status:** Completed

## Plan
Move Hockeymatch's gift-code cells (its redeem sheet, 2026-11-27) into the core as a general control, and let the
sign-in sheet's emailed code use it.

## Changes
- `Presentation/CodeField.cs`: `Length`, `GroupSize`, `Alphabet`, `Substitutions`, `Code`, `State`
  (`CodeFieldState`), `IsActive`, `CompletedCommand`, colours and `FontFamily`; strings `Spine.CodeField.*` (en, sv).
- Sign-in sheet: the emailed code in a `CodeField` (8, 4-4, the server's alphabet), sent when filled while verifying.
- Showcase page "Code field"; `docs/wiki/code-field.md`, README rows, `authentication.md`.

## Decisions
- One hidden `Entry` under the cells, not an entry per cell: backspace on an empty iOS entry raises nothing, and paste,
  AutoFill and accessibility work on one field.
- The control keeps its own caret position and sets the native caret again after iOS moves it (a text set from code
  moves it, and iOS reports the same text once more later).
- `Alphabet` and `Substitutions` as strings rather than a mapping function, so XAML can set them.
- The row asks for 48 points a cell and takes less where there is less: star columns alone ask for nothing, and the
  Showcase's example host gives a view only what it asks for.
- Every `BoxView` clears its `BackgroundColor`: the MAUI template's implicit style fills it.
