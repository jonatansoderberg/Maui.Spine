using System.Collections.Specialized;
using System.ComponentModel;

namespace Plugin.Maui.Spine.Common;

/// <summary>How a <see cref="LiveActivityBinding"/> sends its updates.</summary>
public sealed record LiveActivityBindingOptions
{
    /// <summary>
    /// How long after a change the update goes out, carrying every change made meanwhile: one update per burst. A second
    /// by default, which keeps a busy model inside iOS's budget for updates.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The clock the interval is counted on; the system's by default.</summary>
    public TimeProvider? Time { get; init; }
}

/// <summary>
/// Keeps a Live Activity in step with a view model: each <see cref="INotifyPropertyChanged.PropertyChanged"/> marks it
/// changed, and once per <see cref="LiveActivityBindingOptions.Interval"/> the layout is rebuilt from the model as it is
/// then and sent, off the thread the change came on. An update that would send the same layout again is left out. Made
/// with <see cref="LiveActivityBindings.Bind{T}"/> or a layout file's generated <c>Bind</c>; dispose it to stop.
/// </summary>
public sealed class LiveActivityBinding : IDisposable
{
    readonly LiveActivity _activity;
    readonly Func<LiveActivityLayout> _build;
    readonly Func<DateTimeOffset?>? _staleAt;
    readonly Func<IEnumerable<object?>>? _sources;
    readonly ISpineStrings? _strings;
    readonly TimeSpan _interval;
    readonly TimeProvider _time;
    readonly object _gate = new();
    readonly SemaphoreSlim _sending = new(1, 1);
    readonly INotifyPropertyChanged _model;
    List<object> _watched = [];
    ITimer? _timer;
    string? _sent;
    bool _disposed;

    internal LiveActivityBinding(LiveActivity activity, INotifyPropertyChanged model, Func<LiveActivityLayout> build, Func<DateTimeOffset?>? staleAt,
        Func<IEnumerable<object?>>? sources, ISpineStrings? strings, LiveActivityBindingOptions? options, string? sent)
    {
        _activity = activity;
        _model = model;
        _build = build;
        _staleAt = staleAt;
        _sources = sources;
        _strings = strings;
        _interval = options?.Interval ?? TimeSpan.FromSeconds(1);
        _time = options?.Time ?? TimeProvider.System;
        _sent = sent;
        ArgumentOutOfRangeException.ThrowIfLessThan(_interval, TimeSpan.Zero);

        _model.PropertyChanged += OnPropertyChanged;
        if (_strings is not null)
            _strings.Changed += OnStringsChanged;
        Watch();
    }

    /// <summary>The activity the binding updates.</summary>
    public LiveActivity Activity => _activity;

    /// <summary>How many updates have been sent.</summary>
    public int Updates { get; private set; }

    /// <summary>Raised when an update could not be built or sent; the binding goes on with the next change.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>Marks the activity changed without a property changing: something the layout reads that the model does not announce.</summary>
    public void Refresh() => Changed();

    /// <summary>Sends a pending update now instead of at the end of the interval; nothing when none is pending.</summary>
    public Task FlushAsync()
    {
        lock (_gate)
        {
            if (_timer is null)
                return Task.CompletedTask;
            _timer.Dispose();
            _timer = null;
        }
        return SendAsync();
    }

    /// <summary>Stops following the model. A pending update is dropped; call <see cref="FlushAsync"/> first to send it.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
        _model.PropertyChanged -= OnPropertyChanged;
        if (_strings is not null)
            _strings.Changed -= OnStringsChanged;
        Unwatch();
    }

    void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => Changed();

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Changed();

    void OnStringsChanged(object? sender, EventArgs e) => Changed();

    void Changed()
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null)
                return;
            // The first change of a burst opens the window; the rest ride along with it.
            _timer = _time.CreateTimer(_ => _ = Due(), null, _interval, Timeout.InfiniteTimeSpan);
        }
    }

    Task Due()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (_disposed)
                return Task.CompletedTask;
        }
        return SendAsync();
    }

    async Task SendAsync()
    {
        await _sending.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            if (_activity.IsEnded)
            {
                Dispose();
                return;
            }
            var layout = _build();
            Watch();
            var json = layout.ToJson();
            if (json == _sent)
                return;
            await _activity.UpdateAsync(layout, _staleAt?.Invoke()).ConfigureAwait(false);
            _sent = json;
            Updates++;
        }
        catch (Exception e)
        {
            Failed?.Invoke(this, e);
        }
        finally
        {
            _sending.Release();
        }
    }

    /// <summary>Follows the model's own objects and lists that announce their changes, as they are now.</summary>
    void Watch()
    {
        if (_sources is null)
            return;
        var now = _sources().OfType<object>().Where(s => s is INotifyPropertyChanged or INotifyCollectionChanged).Distinct().ToList();
        lock (_gate)
        {
            if (_disposed)
                return;
            foreach (var old in _watched.Except(now))
                Detach(old);
            foreach (var added in now.Except(_watched))
                Attach(added);
            _watched = now;
        }
    }

    void Unwatch()
    {
        lock (_gate)
        {
            foreach (var source in _watched)
                Detach(source);
            _watched = [];
        }
    }

    void Attach(object source)
    {
        if (source is INotifyPropertyChanged properties) properties.PropertyChanged += OnPropertyChanged;
        if (source is INotifyCollectionChanged collection) collection.CollectionChanged += OnCollectionChanged;
    }

    void Detach(object source)
    {
        if (source is INotifyPropertyChanged properties) properties.PropertyChanged -= OnPropertyChanged;
        if (source is INotifyCollectionChanged collection) collection.CollectionChanged -= OnCollectionChanged;
    }
}

/// <summary>Live Activities that follow a view model.</summary>
public static class LiveActivityBindings
{
    /// <summary>
    /// Updates <paramref name="activity"/> from <paramref name="model"/> whenever it changes, one update per burst; see
    /// <see cref="LiveActivityBinding"/>. A layout file's generated <c>Bind</c> calls this with its <c>Build</c>.
    /// </summary>
    /// <param name="build">The layout for the model as it is.</param>
    /// <param name="staleAt">When each update turns old, from the model; none when <see langword="null"/>.</param>
    /// <param name="sources">The model's objects and lists that announce changes of their own, such as a live game inside the model.</param>
    /// <param name="strings">The strings the layout reads: a change of culture rebuilds it.</param>
    public static LiveActivityBinding Bind<T>(this LiveActivity activity, T model, Func<T, LiveActivityLayout> build, Func<T, DateTimeOffset?>? staleAt = null,
        Func<T, IEnumerable<object?>>? sources = null, ISpineStrings? strings = null, LiveActivityBindingOptions? options = null)
        where T : INotifyPropertyChanged =>
        Bind(activity, model, build, staleAt, sources, strings, options, sent: null);

    /// <summary>
    /// Starts a Live Activity of <paramref name="kind"/> drawn from <paramref name="model"/> and keeps it in step with it;
    /// <see langword="null"/> when the platform started none.
    /// </summary>
    public static async Task<LiveActivityBinding?> StartBoundAsync<T>(this ILiveActivityService activities, string kind, T model, Func<T, LiveActivityLayout> build,
        Func<T, DateTimeOffset?>? staleAt = null, Func<T, IEnumerable<object?>>? sources = null, ISpineStrings? strings = null,
        LiveActivityBindingOptions? options = null, string? channel = null)
        where T : INotifyPropertyChanged
    {
        var layout = build(model);
        var activity = await activities.StartAsync(kind, layout, staleAt?.Invoke(model), channel).ConfigureAwait(false);
        return activity is null ? null : Bind(activity, model, build, staleAt, sources, strings, options, layout.ToJson());
    }

    static LiveActivityBinding Bind<T>(LiveActivity activity, T model, Func<T, LiveActivityLayout> build, Func<T, DateTimeOffset?>? staleAt,
        Func<T, IEnumerable<object?>>? sources, ISpineStrings? strings, LiveActivityBindingOptions? options, string? sent)
        where T : INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(build);
        return new LiveActivityBinding(activity, model, () => build(model), staleAt is null ? null : () => staleAt(model),
            sources is null ? null : () => sources(model), strings, options, sent);
    }
}
