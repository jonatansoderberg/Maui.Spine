namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// How a <see cref="Calendar"/> draws one day that its <see cref="Calendar.DaySource"/> describes: a colour behind
/// the number, a dot under it, and what a screen reader says after the date. A day with neither colour is drawn as a
/// marked day (<see cref="CalendarStyleOptions.MarkStyle"/>).
/// </summary>
/// <param name="Background">The fill behind the number, in the day's shape (<see cref="CalendarStyleOptions.DayShape"/>), such as a tint for how the day went. None when null.</param>
/// <param name="Dot">A small dot under the number in this colour, such as an event that day; with or without a background. None when null.</param>
/// <param name="Description">Said after the date, such as "92 percent in range, one low"; "Marked" when null.</param>
public sealed record CalendarDay(Color? Background = null, Color? Dot = null, string? Description = null)
{
    /// <summary>A day drawn as marked, as a <see cref="ICalendarMarkSource"/> marks it.</summary>
    public static CalendarDay Marked { get; } = new();
}

/// <summary>
/// Tells a <see cref="Calendar"/> how to draw the days of a range: a colour, a dot and a description per day. The
/// richer sibling of <see cref="ICalendarMarkSource"/>; when both are set, this one is used.
/// </summary>
/// <remarks>
/// <see cref="CalendarDays"/> is a ready-made in-memory source, and <see cref="CalendarDaySource.From"/> wraps a
/// delegate. Asked, cached and refreshed exactly as a mark source is.
/// </remarks>
public interface ICalendarDaySource
{
    /// <summary>
    /// The days in [<paramref name="first"/>, <paramref name="last"/>] (inclusive) to draw, each with its look; a day
    /// not in the answer is drawn plainly. Called for the 42 days of a month page and ahead for its neighbours.
    /// </summary>
    /// <remarks>Called on the UI thread; may complete on any thread. An exception is logged by the calendar, not thrown.</remarks>
    ValueTask<IReadOnlyDictionary<DateOnly, CalendarDay>> GetDaysAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken);

    /// <summary>Raised by the source when its data changed, on any thread; the calendar asks again for the pages it shows.</summary>
    event EventHandler? Changed;
}

/// <summary>A base for day sources: <see cref="NotifyChanged"/> raises <see cref="Changed"/>.</summary>
public abstract class CalendarDaySource : ICalendarDaySource
{
    public event EventHandler? Changed;

    public abstract ValueTask<IReadOnlyDictionary<DateOnly, CalendarDay>> GetDaysAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken);

    /// <summary>Tells the calendars showing this source to ask again.</summary>
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>A source that asks <paramref name="getDays"/>, such as an API call for the days of a month.</summary>
    public static CalendarDaySource From(Func<DateOnly, DateOnly, CancellationToken, ValueTask<IReadOnlyDictionary<DateOnly, CalendarDay>>> getDays) =>
        new DelegateSource(getDays);

    private sealed class DelegateSource(Func<DateOnly, DateOnly, CancellationToken, ValueTask<IReadOnlyDictionary<DateOnly, CalendarDay>>> getDays) : CalendarDaySource
    {
        public override ValueTask<IReadOnlyDictionary<DateOnly, CalendarDay>> GetDaysAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken) =>
            getDays(first, last, cancellationToken);
    }
}

/// <summary>
/// An in-memory <see cref="ICalendarDaySource"/> for a view model that already has the days: <see cref="Set"/> them
/// and the calendars showing it redraw.
/// </summary>
public sealed class CalendarDays : CalendarDaySource
{
    private Dictionary<DateOnly, CalendarDay> _days = [];

    public CalendarDays() { }

    public CalendarDays(IEnumerable<KeyValuePair<DateOnly, CalendarDay>> days) => _days = new(days);

    public int Count => _days.Count;

    public CalendarDay? this[DateOnly date] => _days.GetValueOrDefault(date);

    /// <summary>Replaces every day, and redraws once.</summary>
    public void Set(IEnumerable<KeyValuePair<DateOnly, CalendarDay>> days)
    {
        _days = new(days);
        NotifyChanged();
    }

    public void Clear()
    {
        if (_days.Count == 0)
            return;
        _days = [];
        NotifyChanged();
    }

    public override ValueTask<IReadOnlyDictionary<DateOnly, CalendarDay>> GetDaysAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        // A copy, so a Set while the calendar holds the answer does not change it under it.
        var days = _days;
        IReadOnlyDictionary<DateOnly, CalendarDay> range = days.Where(d => d.Key >= first && d.Key <= last).ToDictionary();
        return ValueTask.FromResult(range);
    }
}
