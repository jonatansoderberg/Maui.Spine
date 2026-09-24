# Issue #364 — Calendar: mark days from an external source

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/364
**Branch:** issue/364-calendar-marks
**Status:** Completed

## Plan

The calendar does not own events; a source tells it which dates of a range are marked.

1. `ICalendarMarkSource` (`GetMarkedDatesAsync(first, last, ct)` + `Changed`) in the Calendar package.
2. `Calendar.MarkSource` bindable property. One query per month page range (the 42 days on the page),
   kept in a small cache keyed by range. Rendering the displayed month also prefetches the previous and
   next months, so the neighbour that slides in during a swipe or an arrow tap already has its marks.
   Queries for ranges that left the window are cancelled. `Changed` is subscribed only while the
   calendar is loaded; Unloaded unsubscribes and drops the cache, Loaded resubscribes and asks again.
   Results are applied on the UI thread; a failing source is logged (ILogger when the handler has
   services, otherwise `Trace`), never thrown.
3. `CalendarMarks` (in-memory, `Set`/`Add`/`Remove`/`Clear`, raises `Changed`) and
   `CalendarMarkSource.From(delegate)`.
4. `CalendarStyleOptions`: `MarkFillColor`, `TrailingMarkFillColor`, `MarkedTextColor`, `MarkStyle`
   (`Fill` | `Dot`). Opaque accent blends as defaults.
5. `Calendar.Marked` string (en/sv) appended to a marked day's description.
6. Sample: fake async service on the Calendar page, switch + "change data" button, code example.
   Docs: `docs/wiki/calendar.md` "Marking days", `/spine-controls` skill.

## Open Questions

None blocking.

## Changes

- `ICalendarMarkSource`, `CalendarMarkSource` (base with `NotifyChanged()` and `From(delegate)`),
  `CalendarMarks` (thread-safe in-memory set) and `CalendarMarkStyle` in the Calendar package.
- `Calendar.MarkSource` (`Calendar.Marks.cs`): per-page-range queries with a 12-entry cache,
  prefetch of both neighbour months, cancellation of running queries outside that window,
  `Changed` subscribed between Loaded and Unloaded only, requery on re-attach, results applied on
  the UI thread, source failures logged (`ILogger<Calendar>` or `Trace.TraceError`).
- Day cells: mark fill under everything but the selected fill (inside the today ring when today is
  marked); optional dot view created lazily per cell for `MarkStyle = Dot`.
- `CalendarStyleOptions`: `MarkStyle`, `MarkFillColor`, `TrailingMarkFillColor`, `MarkedTextColor`.
- `Calendar.Marked` string (en "Has events", sv "Har händelser") appended to a marked day's description.
- Sample Calendar page: stand-in async service (300 ms, pseudo-random days plus today), MarkSource
  switch, Fill/Dot choice, "Change the service's data" button, a query counter, code example.
- Docs: `docs/wiki/calendar.md` "Marking days", styling rows, strings; `/spine-controls` skill;
  README / packages / package description rows.

## Decisions

- **Neighbour prefetch instead of querying only at peek time.** The brief asked for the peek page to
  be queried before a slide; with an async source a query started at drag start still lands during
  the slide. Asking for both neighbours whenever the displayed month renders means a swipe or arrow
  tap finds the answer cached. Cost: three queries per new month instead of one.
- **Cache of 12 page ranges, least recently used out.** Running queries outside the
  current/previous/next window are cancelled; finished ones are kept so going back is instant.
  `Changed`, a new source and Unloaded drop the whole cache.
- **No queries before Loaded.** Until the calendar is attached it cannot hear `Changed`, so Loaded
  asks for everything (and after a re-attach asks again, since data may have changed meanwhile).
  On requery the marks on screen stay until the new answer arrives, so nothing blinks.
- **`CalendarMarkSource` abstract base** in addition to the interface: it gives `NotifyChanged()`,
  which the delegate-based `From` needs anyway, and `CalendarMarks` derives from it.
- **Marked or not only (v1).** A per-day kind/colour or count would need a second result type and
  more cell visuals; documented as an extension path (a second interface beside this one).
- **Dot style implemented** since the dot view is created lazily per cell (no extra views for Fill).
  Dot colour: accent; on the selected fill the selected text colour; on trailing days the trailing
  text colour.
- **Today and marked:** the mark fill sits inside the today ring, and the number takes
  `MarkedTextColor`: in dark mode the accent today-number on the accent-tinted fill was hard to read
  (seen on the Android emulator); the ring still marks today.
  **Selected and marked:** the selected fill wins (with Dot, the dot remains, in the text colour).
- **Dark defaults blend into `#2C2C2E`, not black.** Blended into black (as `CurrentHighlightColor`
  is) the fill came out darker than the card it sits on and read as a hole. Light: 18 % / 8 % accent
  over white; dark: 42 % / 15 % over `#2C2C2E`. All opaque (BoxView alpha paints over black on iOS).
- **Logging:** `ILogger<Calendar>` from the handler's services when available, otherwise
  `Trace.TraceError` (not `Debug.WriteLine`, which is compiled out of the Release package).
