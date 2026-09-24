namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A base for <see cref="ICalendarMarkSource"/> implementations: <see cref="NotifyChanged"/> raises
/// <see cref="Changed"/>. <see cref="From"/> makes one from a delegate.
/// </summary>
public abstract class CalendarMarkSource : ICalendarMarkSource
{
    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public abstract ValueTask<IReadOnlyCollection<DateOnly>> GetMarkedDatesAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken);

    /// <summary>Tells every calendar showing this source to ask again. Safe from any thread.</summary>
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// A source that answers with <paramref name="getMarkedDates"/>, e.g. a call to the app's own
    /// service. Call <see cref="NotifyChanged"/> on it when that service's data changes.
    /// </summary>
    public static CalendarMarkSource From(Func<DateOnly, DateOnly, CancellationToken, ValueTask<IReadOnlyCollection<DateOnly>>> getMarkedDates)
    {
        ArgumentNullException.ThrowIfNull(getMarkedDates);
        return new DelegateSource(getMarkedDates);
    }

    private sealed class DelegateSource(Func<DateOnly, DateOnly, CancellationToken, ValueTask<IReadOnlyCollection<DateOnly>>> getMarkedDates) : CalendarMarkSource
    {
        public override ValueTask<IReadOnlyCollection<DateOnly>> GetMarkedDatesAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken) =>
            getMarkedDates(first, last, cancellationToken);
    }
}

/// <summary>
/// An in-memory <see cref="ICalendarMarkSource"/>: a set of dates that a view model fills and a
/// <see cref="Calendar"/> binds to. Every change raises <see cref="CalendarMarkSource.Changed"/>.
/// </summary>
/// <remarks>Thread-safe; the calendar applies a change on the UI thread.</remarks>
public sealed class CalendarMarks : CalendarMarkSource
{
    private readonly Lock _gate = new();
    private readonly HashSet<DateOnly> _dates = [];

    public CalendarMarks() { }

    public CalendarMarks(IEnumerable<DateOnly> dates) => _dates.UnionWith(dates);

    public int Count
    {
        get { lock (_gate) return _dates.Count; }
    }

    public bool Contains(DateOnly date)
    {
        lock (_gate) return _dates.Contains(date);
    }

    /// <summary>Replaces every date with <paramref name="dates"/>.</summary>
    public void Set(IEnumerable<DateOnly> dates)
    {
        lock (_gate)
        {
            _dates.Clear();
            _dates.UnionWith(dates);
        }
        NotifyChanged();
    }

    /// <summary>Marks <paramref name="date"/>. <see langword="false"/> (and no change event) when it was marked already.</summary>
    public bool Add(DateOnly date)
    {
        bool added;
        lock (_gate) added = _dates.Add(date);
        if (added) NotifyChanged();
        return added;
    }

    /// <summary>Unmarks <paramref name="date"/>. <see langword="false"/> (and no change event) when it was not marked.</summary>
    public bool Remove(DateOnly date)
    {
        bool removed;
        lock (_gate) removed = _dates.Remove(date);
        if (removed) NotifyChanged();
        return removed;
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_dates.Count == 0)
                return;
            _dates.Clear();
        }
        NotifyChanged();
    }

    public override ValueTask<IReadOnlyCollection<DateOnly>> GetMarkedDatesAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        lock (_gate)
            return ValueTask.FromResult<IReadOnlyCollection<DateOnly>>(_dates.Where(d => d >= first && d <= last).ToArray());
    }
}
