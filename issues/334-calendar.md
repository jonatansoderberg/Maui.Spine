# Issue #334 — Plugin.Maui.Spine.Controls.Calendar: month calendar with year/decade pickers and week numbers

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/334
**Branch:** issue/334-calendar
**Status:** In Progress
**Stage:** 3 of the app-review plan (#333)

## Plan

Port a production-tested month calendar (built from plain MAUI views) as its own package, without event days.

- **Package** `src/Plugin.Maui.Spine.Controls.Calendar`, namespace `Plugin.Maui.Spine.Controls`, same csproj shape as AnimatedLabel. It references the core (`SpineTheme`, `SpineStyleOptions`, `SpineStrings` through Common).
- **Control** `Calendar : ContentView`, split into partial files (properties and tree, gestures and sliding, rendering). Kept: `SelectedDate`, `DisplayDate`, `Today`, `ShowSelectedDate`, `ShowTrailingDays`, `ShowWeekNumbers`, `FirstDayOfWeek`, `Culture`, `StyleOptions`. Removed: event dates and their fills; the source's injected day/month name lists (names come from the culture, as the issue asks).
- **Behaviour kept as is**: 42 cells built once per page, two pages per view swapped by a committed slide (`Animation.Commit`), one tap and one pan recognizer on the content host, axis lock after 10 units, fling commit/cancel, the iOS `ShouldBegin` that refuses drags that are not more horizontal than vertical.
- **Style options** `CalendarStyleOptions : SpineStyleOptions<CalendarStyleOptions>`; every colour `Color?`, theme-aware defaults in `ApplyThemeDefaults`. `CurrentHighlightColor` replaces the event fill as the marker of the current month/year in the pickers.
- **Accent**: the app's `Primary` resource (`PrimaryDark` in dark mode when present, as in the MAUI template), else the iOS system blue.
- **Nav arrows**: a MAUI `Path` chevron stroked with the accent; no icon font, no SVG lookup.
- **Repaint**: `SpineTheme.Track(this, Repaint)` repaints colours and re-renders text, so both a theme switch and a culture switch (culture `null` = `SpineStrings.Current.Culture`) show at once.
- **Strings** under `Calendar.*` (week column header, previous/next labels for screen readers, today/selected, week number), `strings.xml` + `strings.sv.xml`, registered by `UseSpineCalendar()`.
- **Accessibility**: each day label gets the full date (long date pattern) plus today/selected; week numbers and picker months get descriptions; nav arrows get labels per view.
- **Sample**: `Pages/Calendar/CalendarPage` in a `ScrollView` with toggles (week numbers, trailing days, first day of week, culture), the selected date, and code examples.
- **Docs**: `docs/wiki/calendar.md`, packages table in README and `docs/wiki/packages.md`, `/spine-controls` skill, package icon.

## Open Questions

None.

## Changes

- New package `src/Plugin.Maui.Spine.Controls.Calendar`: `Calendar` (`Calendar.cs` properties and tree, `Calendar.Gestures.cs` tap/pan/slide, `Calendar.Rendering.cs` navigation and rendering), `CalendarStyleOptions` on the shared `SpineStyleOptions<T>`, `UseSpineCalendar()`, `Resources/Strings/strings.xml` + `strings.sv.xml`, README, package icon `assets/icons/calendar.png` (source in `assets/logo-src/`).
- Event days and everything behind them dropped; the pickers' "current" marker is `CurrentHighlightColor`.
- Theme-aware defaults; accent from the app's `Primary` / `PrimaryDark` resource, else system blue; selected text turns black on a light accent.
- Nav arrows are MAUI `Path` chevrons stroked with the accent (no icon font).
- `Repaint` through `SpineTheme.Track` re-applies colours and rewrites text, so theme and culture switches show live; `Culture` null follows `SpineStrings.Current.Culture`.
- Accessibility: day labels carry the full date plus Today/Selected, week numbers "Week n", weekday headers the full day name, picker months "Month Year", arrows a label per view, the title a hint.
- `Today` and `DisplayDate` defaults come from `defaultValueCreator`, so they are evaluated per calendar rather than once per process.
- Every `BoxView` sets `BackgroundColor = Transparent`: the MAUI template's implicit `BoxView` style paints a grey square behind each round fill otherwise (seen on the first iOS run).
- Weekday headers use `AbbreviatedDayNames` (trailing dot trimmed, first letter capitalised); `ShortestDayNames` gave single letters in English.
- Sample: `Pages/Dates/DatesPage` ("Calendar" in the index, `calendarday.svg`), inside a `ScrollView`, with live theme and language switches, toggles for week numbers, trailing days and selection, first day of week, a culture pin, and code examples.
- Docs: `docs/wiki/calendar.md`, README and `docs/wiki/packages.md` rows (ten packages), `agent-skills.md`, `/spine-controls` skill; `Spine.slnx` and `Spine.Packages.slnf` list the project.

## Decisions

- **Name `Calendar`, not `SpineCalendar`/`MonthCalendar`.** It matches the package and XAML users never see a clash. `System.Globalization.Calendar` only collides in a C# file that imports both namespaces; the docs show the alias. The sample needed none. The sample page lives in `Pages/Dates` because a `Pages.Calendar` namespace would shadow the type inside the sample.
- **Registration is `UseSpineCalendar()`** (the brief's name) rather than `UseCalendar()`, which is generic enough to clash with other libraries.
- **No resource dictionary with tokens.** The source shipped one with brand colours; here the code defaults are theme-aware and an app overrides with a `DefaultCalendarStyleOptions` resource.
- **Style colours are `Color?`** and `InheritColorsFrom` only fills unset ones, per the shared chain; fonts are plain values and only change by replacing `StyleOptions`, which rebuilds.
- **Header title uses the text colour, not the accent** (the source drew it in the brand colour); only arrows, ring and fills carry the accent.
- **Calendar text keys follow `SpineStrings.Culture`** even when `Culture` pins the calendar to another culture: the pinned culture drives names and date formats, the strings store drives words.
- **Mac Catalyst gets the same `ShouldBegin` restriction as iOS** (`#if IOS || MACCATALYST`): a click-drag is the pan; wheel and trackpad scrolling never start it.
- **Windows is documented, not changed.** MAUI's pan recognizer sets the element's manipulation mode on WinUI; a mouse wheel scrolls as usual, a touch drag starting on the calendar may not scroll a parent `ScrollView`. Unverified without a Windows touch device, so no untested workaround was added.
