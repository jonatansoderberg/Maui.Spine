# Issue #833 — Calendar: days with their own colour, dot and description, a rounded-square shape and date limits

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/833
**Branch:** issue/833-calendar-days-with-their-own-colour-dot-and-descr (stacked on #831)
**Status:** In Progress

## Plan

`DaySource` beside `MarkSource`, through the same questions and cache; `DayShape` in the style options; `MinimumDate`
and `MaximumDate`. Part S4 of Glukos's reports plan (`docs/reports.md` §7.1 in Glukos): the day-by-day calendar.

## Open Questions

## Changes

- `CalendarDay`, `ICalendarDaySource`, `CalendarDaySource` (with `From`), `CalendarDays` (`CalendarDay.cs`).
- `Calendar.DaySource`; the mark source now answers through an adapter as `CalendarDay.Marked` days, so one set of
  questions, prefetching and caching serves both, and a mark source behaves as before.
- Drawing: a background in the day's shape that stays when selected (a ring in the number's colour) or today (the accent
  ring), faded on adjacent months; a dot in its own colour; the description after the date.
- `CalendarStyleOptions.DayShape` (`Circle`, `RoundedSquare`); the today ring is a rounded rectangle so it follows.
- `MinimumDate` / `MaximumDate`: faded, no marks, no taps, and arrows, swipes, pickers and `DisplayDate` stop at
  their months; the arrow that leads past them is faded.
- Tried in Glukos (simulator, packages `0.261118.8-dev.833`) as a temporary calendar on the person page: 31 days
  tinted by a made-up time in range as rounded squares, dots on some days, the selected day ringed in the text
  colour, today in the accent, the days after today faded, the next arrow faded and not moving past November.
- `docs/wiki/calendar.md` (Properties, "Days with their own look", Not included) and the `spine-controls` skill.

## Decisions

- No unit tests: the change is in the MAUI views; it was tried in Glukos instead (below).
- The number on a tinted day is black or white by the tint's lightness (`SpineAccent.TextOn`), not the theme's day
  colour: in the trial, light tints in dark mode left white numbers unreadable.
- A day of `CalendarDay.Marked` is a plain mark, so a day source can mix tinted days and ordinary marks.
