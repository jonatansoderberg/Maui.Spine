using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Controls;

public partial class Calendar
{
    public static readonly BindableProperty MarkSourceProperty = BindableProperty.Create(
        nameof(MarkSource), typeof(ICalendarMarkSource), typeof(Calendar), null,
        propertyChanged: (b, _, _) => ((Calendar)b).OnMarkSourceChanged());

    /// <summary>
    /// Where the marked days come from. <see langword="null"/> = no marks. The calendar asks it for the
    /// 42 days of each month page it shows, and again when it raises <see cref="ICalendarMarkSource.Changed"/>.
    /// </summary>
    public ICalendarMarkSource? MarkSource
    {
        get => (ICalendarMarkSource?)GetValue(MarkSourceProperty);
        set => SetValue(MarkSourceProperty, value);
    }

    // Answers kept for months the user may come back to; the displayed month and both neighbours
    // are always among the most recently used.
    private const int MarkCacheSize = 12;

    private readonly record struct DateRange(DateOnly First, DateOnly Last);

    /// <summary>One question to the source, for the days of one month page.</summary>
    private sealed class MarkQuery
    {
        public required DateRange Range;
        public readonly CancellationTokenSource Cancellation = new();

        // The answer; null while it runs, and after a failure or a cancellation.
        public HashSet<DateOnly>? Dates;
        public Task? Running;
        public long LastUsed;
    }

    private readonly Dictionary<DateRange, MarkQuery> _markQueries = [];
    private long _markClock;
    private bool _isAttached;
    private ICalendarMarkSource? _subscribedSource;

    // ── Lifetime ────────────────────────────────────────────────────────────────

    // Changed is listened to only while loaded: a source registered as a singleton would otherwise
    // hold every calendar, and the page around it, for the life of the app.
    private void OnCalendarLoaded(object? sender, EventArgs e)
    {
        _isAttached = true;
        SyncMarkSubscription();

        // The source may have changed while the calendar was away and could not hear it.
        RequeryMarks();
    }

    private void OnCalendarUnloaded(object? sender, EventArgs e)
    {
        _isAttached = false;
        SyncMarkSubscription();
        DropMarkQueries();
    }

    private void SyncMarkSubscription()
    {
        var wanted = _isAttached ? MarkSource : null;
        if (ReferenceEquals(wanted, _subscribedSource))
            return;

        if (_subscribedSource is not null)
            _subscribedSource.Changed -= OnMarksChanged;

        _subscribedSource = wanted;

        if (wanted is not null)
            wanted.Changed += OnMarksChanged;
    }

    private void OnMarkSourceChanged()
    {
        SyncMarkSubscription();
        DropMarkQueries();

        foreach (var page in new[] { _month, _monthPeek })
        {
            if (page is null)
                continue;

            page.Marks = null;
            page.MarkQuery = null;
        }

        UpdateShownMarks();
    }

    private void OnMarksChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _subscribedSource))
            return;

        if (Dispatcher is { IsDispatchRequired: true } dispatcher)
            dispatcher.Dispatch(RequeryMarks);
        else
            RequeryMarks();
    }

    /// <summary>Asks again for the pages on screen. The marks shown stay until the new answer arrives, so nothing blinks.</summary>
    private void RequeryMarks()
    {
        if (!_isAttached)
            return;

        // The pages keep their old query: a new one for the same range leaves the old marks up.
        DropMarkQueries();
        UpdateShownMarks();
    }

    private void DropMarkQueries()
    {
        foreach (var query in _markQueries.Values)
            query.Cancellation.Cancel();

        _markQueries.Clear();
    }

    /// <summary>Brings the marks of the month page, and of the neighbour while it is on screen, up to date.</summary>
    private void UpdateShownMarks()
    {
        foreach (var page in new[] { _month, _monthPeek })
        {
            if (page is null || page.Year < 0 || (page == _monthPeek && !page.Grid.IsVisible))
                continue;

            EnsureMarks(page);
            RefreshDayVisuals(page);
        }
    }

    // ── Queries ─────────────────────────────────────────────────────────────────

    private DateTime GridStart(int year, int month)
    {
        var firstOfMonth = new DateTime(year, month, 1);
        return firstOfMonth.AddDays(-(((int)firstOfMonth.DayOfWeek - (int)FirstDayOfWeek + 7) % 7));
    }

    private DateRange MonthRange(DateTime firstOfMonth)
    {
        var start = DateOnly.FromDateTime(GridStart(firstOfMonth.Year, firstOfMonth.Month));
        return new DateRange(start, start.AddDays(41));
    }

    /// <summary>
    /// Points <paramref name="page"/> at the answer for the days it shows, asking the source when no
    /// answer is cached. For the displayed month it also asks ahead for both neighbours, and drops
    /// the questions still running for months that are no longer near.
    /// </summary>
    /// <remarks>The caller refreshes the cells; an answer that arrives later refreshes them itself.</remarks>
    private void EnsureMarks(MonthPage page)
    {
        var source = MarkSource;
        if (source is null)
        {
            page.Marks = null;
            page.MarkQuery = null;
            return;
        }

        // Not on screen: nothing asks until Loaded, which asks for everything again.
        if (!_isAttached)
            return;

        var firstOfMonth = new DateTime(page.Year, page.Month, 1);
        var range = MonthRange(firstOfMonth);

        if (page == _month)
            PrefetchAround(source, firstOfMonth);

        var query = GetOrStartQuery(source, range);
        if (!ReferenceEquals(page.MarkQuery, query))
        {
            // Marks of another range would land on the wrong days; this range's older marks may
            // stay until the new answer replaces them.
            if (page.MarkQuery?.Range != range)
                page.Marks = null;

            page.MarkQuery = query;

            if (query.Dates is null && query.Running is { IsCompleted: false } running)
                _ = ApplyWhenAnsweredAsync(page, query, running);
        }

        if (query.Dates is not null)
            page.Marks = query.Dates;
    }

    private void PrefetchAround(ICalendarMarkSource source, DateTime firstOfMonth)
    {
        int index = firstOfMonth.Year * 12 + firstOfMonth.Month - 1;
        var window = new HashSet<DateRange>
        {
            MonthRange(firstOfMonth),
            MonthRange(WithMonthIndex(index - 1)),
            MonthRange(WithMonthIndex(index + 1)),
        };

        foreach (var query in _markQueries.Values.ToList())
        {
            // A question still running for a month that is no longer near will not be shown.
            if (!window.Contains(query.Range) && query.Dates is null)
            {
                query.Cancellation.Cancel();
                _markQueries.Remove(query.Range);
            }
        }

        foreach (var range in window)
            GetOrStartQuery(source, range);
    }

    private MarkQuery GetOrStartQuery(ICalendarMarkSource source, DateRange range)
    {
        if (_markQueries.TryGetValue(range, out var cached))
        {
            cached.LastUsed = ++_markClock;
            return cached;
        }

        var query = new MarkQuery { Range = range, LastUsed = ++_markClock };
        _markQueries[range] = query;

        while (_markQueries.Count > MarkCacheSize)
        {
            var oldest = _markQueries.Values.MinBy(q => q.LastUsed)!;
            oldest.Cancellation.Cancel();
            _markQueries.Remove(oldest.Range);
        }

        ValueTask<IReadOnlyCollection<DateOnly>> answer;
        try
        {
            answer = source.GetMarkedDatesAsync(range.First, range.Last, query.Cancellation.Token);
        }
        catch (Exception e)
        {
            ReportMarkFailure(source, range, e);
            return query;
        }

        // An in-memory source answers at once: the page is marked in the same render, with no second pass.
        if (answer.IsCompletedSuccessfully)
            query.Dates = ToSet(answer.Result);
        else
            query.Running = CompleteQueryAsync(source, query, answer);

        return query;
    }

    private async Task CompleteQueryAsync(ICalendarMarkSource source, MarkQuery query, ValueTask<IReadOnlyCollection<DateOnly>> answer)
    {
        try
        {
            var dates = await answer;
            if (!query.Cancellation.IsCancellationRequested)
                query.Dates = ToSet(dates);
        }
        catch (OperationCanceledException) when (query.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            ReportMarkFailure(source, query.Range, e);
        }
    }

    private async Task ApplyWhenAnsweredAsync(MonthPage page, MarkQuery query, Task running)
    {
        await running;

        if (Dispatcher is { IsDispatchRequired: true } dispatcher)
            dispatcher.Dispatch(Apply);
        else
            Apply();

        void Apply()
        {
            // The page may show another month by now, or the question may have been asked again.
            if (!ReferenceEquals(page.MarkQuery, query) || query.Dates is null)
                return;

            page.Marks = query.Dates;
            RefreshDayVisuals(page);
        }
    }

    private static HashSet<DateOnly> ToSet(IReadOnlyCollection<DateOnly>? dates) => dates is null ? [] : [.. dates];

    private void ReportMarkFailure(ICalendarMarkSource source, DateRange range, Exception exception)
    {
        var logger = Handler?.MauiContext?.Services.GetService<ILoggerFactory>()?.CreateLogger<Calendar>();
        if (logger is not null)
        {
            logger.LogError(exception, "Calendar mark source {Source} failed for {First:yyyy-MM-dd}..{Last:yyyy-MM-dd}; those days show no marks.",
                source.GetType().FullName, range.First, range.Last);
        }
        else
        {
            Trace.TraceError($"[Spine] Calendar mark source {source.GetType().FullName} failed for {range.First:yyyy-MM-dd}..{range.Last:yyyy-MM-dd}; those days show no marks. {exception}");
        }
    }
}
