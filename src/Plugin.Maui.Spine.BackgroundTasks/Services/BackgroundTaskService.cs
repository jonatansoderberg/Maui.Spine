using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.BackgroundTasks.Services;

/// <summary>
/// Runs the tasks: one DI scope per run, on the thread pool, at most one run per task at a time, and
/// the outcome stored so the next launch knows what is due. The platform parts decide when.
/// </summary>
internal sealed class BackgroundTaskService(
    BackgroundTaskRegistry registry,
    SpineBackgroundTasksOptions options,
    IServiceProvider services,
    ILogger<IBackgroundTasks> logger) : IBackgroundTasks
{
    private const string StoreKey = "spine.backgroundtasks.schedule";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Task<BackgroundTaskOutcome>> _running = new(StringComparer.Ordinal);
    private Dictionary<string, BackgroundTaskState>? _states;

    public bool RunsWhileClosed => (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst()) || OperatingSystem.IsAndroid();

    public IReadOnlyList<string> Names => registry.Names;

    public IReadOnlyList<BackgroundTaskDescriptor> Tasks => registry.Tasks;

    public event Action<string>? StatusChanged;

    public Task RequestAsync(string name, CancellationToken cancellationToken = default) =>
        RunAsync(Require(name), BackgroundTaskTrigger.Requested, cancellationToken);

    public Task RequestAsync(string name, BackgroundTaskTrigger trigger, CancellationToken cancellationToken = default) =>
        RunAsync(Require(name), trigger, cancellationToken);

    public Task RequestAsync<TTask>(CancellationToken cancellationToken = default) where TTask : IBackgroundTask =>
        RunAsync(registry.Find(typeof(TTask)) ?? throw new ArgumentException($"{typeof(TTask).FullName} has no [BackgroundTask] attribute, or is not in an assembly Spine scans (SpineOptions.AddAssembly).", nameof(TTask)),
            BackgroundTaskTrigger.Requested, cancellationToken);

    public BackgroundTaskStatus StatusOf(string name)
    {
        var task = Require(name);
        var interval = IntervalOf(task);
        BackgroundTaskState? state;
        bool running;
        lock (_gate)
        {
            state = States().GetValueOrDefault(name);
            running = _running.ContainsKey(name);
        }

        return new BackgroundTaskStatus(name, interval, running,
            state?.LastStarted, state?.LastEnded, state?.LastCompleted,
            state?.LastOutcome ?? BackgroundTaskOutcome.None, state?.LastTrigger, state?.LastError,
            BackgroundTaskPlanner.NextDue(interval, state, DateTimeOffset.UtcNow));
    }

    /// <summary>The interval in force: the options' override, else the attribute's (or the contributing package's).</summary>
    public TimeSpan IntervalOf(BackgroundTaskDescriptor task) => options.IntervalFor(task.Name) ?? task.DefaultInterval();

    /// <summary>When the platform should next run one of the tasks <paramref name="include"/> picks; <see langword="null"/> when none is scheduled.</summary>
    public DateTimeOffset? EarliestDue(Func<BackgroundTaskDescriptor, bool> include)
    {
        lock (_gate)
            return BackgroundTaskPlanner.Earliest(Plan(include), States(), DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Runs every task <paramref name="include"/> picks that is due, the most overdue first, one after
    /// the other until <paramref name="cancellationToken"/> is cancelled. A task that needs a network is
    /// left due while the device is offline. True when every run completed.
    /// </summary>
    public async Task<bool> RunDueAsync(Func<BackgroundTaskDescriptor, bool> include, BackgroundTaskTrigger trigger, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> due;
        lock (_gate)
            due = BackgroundTaskPlanner.Due(Plan(include), States(), DateTimeOffset.UtcNow);

        if (due.Count > 0)
            logger.LogInformation("Background tasks due ({Trigger}): {Tasks}.", trigger, string.Join(", ", due));

        var allCompleted = true;
        foreach (var name in due)
        {
            if (cancellationToken.IsCancellationRequested) return false;
            var task = Require(name);
            // A run since the list was made (the platform's own job, a request) may have made it not due.
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                if (BackgroundTaskPlanner.NextDue(IntervalOf(task), States().GetValueOrDefault(name), now) is not { } dueAt || dueAt > now)
                    continue;
            }

            if (task.RequiresNetwork && !IsOnline())
            {
                logger.LogInformation("Background task \"{Name}\" needs a network; left due until there is one.", name);
                continue;
            }

            allCompleted &= await RunAsync(task, trigger, cancellationToken) == BackgroundTaskOutcome.Completed;
        }

        return allCompleted;
    }

    /// <summary>
    /// Runs <paramref name="name"/> for the platform's own schedule (an Android job), due or not: the
    /// platform has already decided it is time.
    /// </summary>
    public Task<BackgroundTaskOutcome> RunScheduledAsync(string name, CancellationToken cancellationToken) =>
        registry.Find(name) is { } task
            ? RunAsync(task, BackgroundTaskTrigger.Scheduled, cancellationToken)
            : Task.FromResult(LogUnknown(name));

    private BackgroundTaskOutcome LogUnknown(string name)
    {
        logger.LogWarning("The platform ran background task \"{Name}\", which this version of the app does not declare.", name);
        return BackgroundTaskOutcome.None;
    }

    private Task<BackgroundTaskOutcome> RunAsync(BackgroundTaskDescriptor task, BackgroundTaskTrigger trigger, CancellationToken cancellationToken)
    {
        Task<BackgroundTaskOutcome> run;
        lock (_gate)
        {
            // A request while the platform's run is in progress (or the other way round) gets that run.
            if (_running.TryGetValue(task.Name, out var inFlight))
                return inFlight.WaitAsync(cancellationToken);

            run = Task.Run(() => RunCoreAsync(task, trigger, cancellationToken), CancellationToken.None);
            _running[task.Name] = run;
        }

        return run;
    }

    private async Task<BackgroundTaskOutcome> RunCoreAsync(BackgroundTaskDescriptor task, BackgroundTaskTrigger trigger, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        BackgroundTaskState state;
        lock (_gate)
        {
            state = States().GetValueOrDefault(task.Name) ?? new BackgroundTaskState();
            state = state with { LastStarted = started, LastTrigger = trigger };
            Save(task.Name, state);
        }
        Raise(task.Name);

        var outcome = BackgroundTaskOutcome.Completed;
        string? error = null;
        try
        {
            await using var scope = services.CreateAsyncScope();
            await task.RunAsync(scope.ServiceProvider, new BackgroundTaskRun(task.Name, trigger, state.LastCompleted), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = BackgroundTaskOutcome.Cancelled;
            error = "The run's time ran out.";
        }
        catch (Exception e)
        {
            outcome = BackgroundTaskOutcome.Failed;
            error = $"{e.GetType().Name}: {e.Message}";
            logger.LogError(e, "Background task \"{Name}\" failed ({Trigger}).", task.Name, trigger);
        }

        if (outcome == BackgroundTaskOutcome.Completed)
            await RefreshWidgetsAsync(task, cancellationToken);

        var ended = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            state = state with
            {
                LastEnded = ended,
                LastCompleted = outcome == BackgroundTaskOutcome.Completed ? ended : state.LastCompleted,
                LastOutcome = outcome,
                LastError = error,
            };
            Save(task.Name, state);
            _running.Remove(task.Name);
        }

        logger.LogInformation("Background task \"{Name}\" {Outcome} in {Elapsed} ms ({Trigger}).", task.Name, outcome, (int)(ended - started).TotalMilliseconds, trigger);
        Raise(task.Name);
        return outcome;
    }

    private async Task RefreshWidgetsAsync(BackgroundTaskDescriptor task, CancellationToken cancellationToken)
    {
        if (task.Widgets.Count == 0) return;
        if (services.GetService<IWidgetService>() is not { } widgets)
        {
            logger.LogWarning("Background task \"{Name}\" names widgets ({Widgets}) but Plugin.Maui.Spine.Widgets is not registered.", task.Name, string.Join(", ", task.Widgets));
            return;
        }

        foreach (var kind in task.Widgets)
        {
            try { await widgets.RefreshAsync(kind, cancellationToken); }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogError(e, "Refreshing widget \"{Kind}\" after background task \"{Name}\" failed.", kind, task.Name); }
        }
    }

    private IEnumerable<(string Name, TimeSpan Interval)> Plan(Func<BackgroundTaskDescriptor, bool> include) =>
        registry.Tasks.Where(include).Select(t => (t.Name, IntervalOf(t)));

    private BackgroundTaskDescriptor Require(string name) =>
        registry.Find(name) ?? throw new ArgumentException($"No background task is named \"{name}\". Known: {string.Join(", ", registry.Names)}.", nameof(name));

    private void Raise(string name)
    {
        try { StatusChanged?.Invoke(name); }
        catch (Exception e) { logger.LogError(e, "A StatusChanged handler failed for background task \"{Name}\".", name); }
    }

    private bool IsOnline()
    {
        try { return Connectivity.Current.NetworkAccess == NetworkAccess.Internet; }
        catch (Exception e)
        {
            // Without ACCESS_NETWORK_STATE on Android the check throws; the task then finds out itself.
            logger.LogDebug(e, "The network state could not be read.");
            return true;
        }
    }

    // Called under _gate.
    private Dictionary<string, BackgroundTaskState> States() =>
        _states ??= BackgroundTaskState.Deserialize(Preferences.Default.Get(StoreKey, ""));

    // Called under _gate.
    private void Save(string name, BackgroundTaskState state)
    {
        var states = States();
        states[name] = state;
        // Drops the states of tasks this version no longer declares.
        foreach (var gone in states.Keys.Where(k => registry.Find(k) is null).ToList())
            states.Remove(gone);
        Preferences.Default.Set(StoreKey, BackgroundTaskState.Serialize(states));
    }
}
