namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Tells a <see cref="Calendar"/> which days have something on them. The calendar only draws the
/// marks; what a mark means, and the data behind it, stay with the source.
/// </summary>
/// <remarks>
/// Set it through <see cref="Calendar.MarkSource"/>. <see cref="CalendarMarks"/> is a ready-made
/// in-memory source, and <see cref="CalendarMarkSource.From"/> wraps a delegate.
/// </remarks>
public interface ICalendarMarkSource
{
    /// <summary>
    /// The dates in [<paramref name="first"/>, <paramref name="last"/>] (inclusive) that have something
    /// to mark. Called for the 42 days of a month page when it is shown, and ahead of time for the
    /// months either side of it, so the neighbour that slides in during a swipe has its marks already.
    /// </summary>
    /// <remarks>
    /// Called on the UI thread; may complete on any thread. <paramref name="cancellationToken"/> is
    /// cancelled when the calendar no longer needs the answer (it moved further away, the source
    /// changed, or the calendar left the screen). An exception is logged by the calendar, not thrown.
    /// </remarks>
    ValueTask<IReadOnlyCollection<DateOnly>> GetMarkedDatesAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken);

    /// <summary>
    /// Raised by the source when its data changed, on any thread; the calendar asks again for the
    /// pages it shows. A calendar listens only while it is loaded, so a long-lived source does not
    /// keep a closed page alive.
    /// </summary>
    event EventHandler? Changed;
}
