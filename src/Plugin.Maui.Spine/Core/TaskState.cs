using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.Spine.AI;

namespace Plugin.Maui.Spine.Core;

/// <summary>Where a <see cref="TaskState{T}"/> is in its load.</summary>
public enum TaskStateStatus
{
    /// <summary>Nothing has been loaded, and no load is running.</summary>
    NotStarted,

    /// <summary>The first load is running; there is no result to show yet.</summary>
    Loading,

    /// <summary>A load is running while the previous result stays on screen.</summary>
    Refreshing,

    /// <summary>The last load gave a result.</summary>
    Success,

    /// <summary>
    /// The last load gave a result the state's <c>isEmpty</c> calls empty, or failed with an exception that means
    /// there is nothing to show (<see cref="Plugin.Maui.Spine.AI.IEmptyResultException"/>, such as an AI feature that is off).
    /// </summary>
    Empty,

    /// <summary>The load failed and there is no earlier result to show; see <see cref="TaskState.Error"/>.</summary>
    Error,
}

/// <summary>
/// The state of one async load, for a <see cref="Presentation.StateView"/> or bindings of the page's
/// own. The non-generic face of <see cref="TaskState{T}"/>.
/// </summary>
public abstract partial class TaskState : ObservableObject
{
    private protected TaskState()
    {
        LoadCommand = new AsyncRelayCommand(() => LoadAsync(), AsyncRelayCommandOptions.AllowConcurrentExecutions);
    }

    /// <summary>Where the load is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsRefreshing))]
    public partial TaskStateStatus Status { get; private protected set; }

    /// <summary>
    /// Why the last load failed, or <see langword="null"/> after one that succeeded. Set on a failed
    /// refresh too, where <see cref="Status"/> stays <see cref="TaskStateStatus.Success"/> or
    /// <see cref="TaskStateStatus.Empty"/> and the earlier result stays on screen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorMessage), nameof(HasError))]
    public partial Exception? Error { get; private protected set; }

    /// <summary>The message of <see cref="Error"/>.</summary>
    public string? ErrorMessage => Error?.Message;

    /// <summary>Whether the last load failed, including a refresh that kept the earlier result.</summary>
    public bool HasError => Error is not null;

    /// <summary>
    /// Whether there is nothing to show yet and a load is running or about to: <see cref="TaskStateStatus.Loading"/>
    /// or <see cref="TaskStateStatus.NotStarted"/>. Not while <see cref="TaskStateStatus.Refreshing"/>,
    /// which keeps the result on screen. Bind a skeleton to it: <c>Skeleton.IsActive="{Binding People.IsLoading}"</c>.
    /// </summary>
    public bool IsLoading => Status is TaskStateStatus.Loading or TaskStateStatus.NotStarted;

    /// <summary>
    /// Whether a load is running while the earlier result stays on screen. Bind a <c>RefreshView</c>'s
    /// <c>IsRefreshing</c> to it, with <see cref="LoadCommand"/> as its command. Setting it does
    /// nothing; the value follows the load.
    /// </summary>
    public bool IsRefreshing
    {
        get => Status == TaskStateStatus.Refreshing;
        set { }
    }

    /// <summary>Whether a load has given a result, which later loads keep on screen while they run.</summary>
    [ObservableProperty]
    public partial bool HasResult { get; private protected set; }

    /// <summary>Whether the state has a placeholder to show while the first load runs.</summary>
    public abstract bool HasPlaceholder { get; }

    /// <summary>
    /// Loads again: the retry of an error, a pull to refresh, or the first load. Runs alongside a
    /// load already running, which it replaces.
    /// </summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>
    /// Loads, replacing a load already running (which is cancelled and changes nothing). With a
    /// result already on screen the status is <see cref="TaskStateStatus.Refreshing"/>, otherwise
    /// <see cref="TaskStateStatus.Loading"/>. Never throws: a failure is <see cref="Error"/>, and a
    /// cancellation puts the status back where it was.
    /// </summary>
    /// <param name="cancellationToken">
    /// Stops the load. Without one, a state made with <c>ViewModelBase.Load</c> uses the page's
    /// lifetime, so it stops when the page disappears.
    /// </param>
    public abstract Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets the result and the error and cancels a load that is running, back to
    /// <see cref="TaskStateStatus.NotStarted"/>. For when what is loaded changes, such as a filter, and
    /// the old result should not stay on screen while the new one loads: call it, then
    /// <see cref="LoadAsync"/>.
    /// </summary>
    public abstract void Reset();

    /// <summary>The page lifetime of a state registered with <c>ViewModelBase.Load</c>.</summary>
    internal Func<CancellationToken>? Lifetime { get; set; }

    /// <summary>Whether the page should load when it appears: nothing loaded yet, or nothing to show after a failure.</summary>
    internal bool NeedsLoad => Status is TaskStateStatus.NotStarted or TaskStateStatus.Error;
}

/// <summary>
/// One async load and where it is: <see cref="TaskState.Status"/>, the <see cref="Result"/>, the
/// <see cref="TaskState.Error"/>, and a <see cref="TaskState.LoadCommand"/> to retry or refresh. On a page,
/// make it with <c>ViewModelBase.Load</c>, which loads when the page appears and cancels when it
/// disappears; show it with a <see cref="Presentation.StateView"/>.
/// </summary>
/// <example>
/// <code>
/// public TaskState&lt;IReadOnlyList&lt;Competition&gt;&gt; Competitions { get; }
///
/// public CompetitionsPageViewModel(ICompetitionsApi api)
/// {
///     Competitions = Load(ct => api.GetCompetitionsAsync(ct), isEmpty: list => list.Count == 0);
/// }
/// </code>
/// </example>
/// <param name="load">The load. Called on the caller's thread and awaited there, so the result is set on the UI thread when the load is started from it.</param>
/// <param name="isEmpty">Whether a result counts as empty, which shows the empty state instead of the content.</param>
/// <param name="placeholder">
/// A value to show until the first result, such as a few empty rows, so a skeleton drawn over the
/// content has the size the loaded content will have.
/// </param>
public sealed class TaskState<T>(
    Func<CancellationToken, Task<T>> load,
    Func<T, bool>? isEmpty = null,
    Func<T>? placeholder = null) : TaskState
{
    private CancellationTokenSource? _running;
    private TaskStateStatus _settled;
    private T? _placeholder;
    private bool _placeholderMade;

    /// <summary>The result of the last load that succeeded; kept while a later load runs or after it fails.</summary>
    public T? Result
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>
    /// What to show: the <see cref="Result"/> once there is one, the placeholder until then. Bind the
    /// content to it.
    /// </summary>
    public T? Value => HasResult ? Result : Placeholder;

    /// <inheritdoc/>
    public override bool HasPlaceholder => placeholder is not null;

    private T? Placeholder
    {
        get
        {
            if (placeholder is null || _placeholderMade)
                return _placeholder;

            _placeholderMade = true;
            return _placeholder = placeholder();
        }
    }

    /// <inheritdoc/>
    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.CanBeCanceled && Lifetime is not null)
            cancellationToken = Lifetime();

        if (cancellationToken.IsCancellationRequested)
            return;

        _running?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = cts;

        Status = HasResult ? TaskStateStatus.Refreshing : TaskStateStatus.Loading;

        try
        {
            var result = await load(cts.Token);

            if (_running != cts)
                return;

            Result = result;
            HasResult = true;
            OnPropertyChanged(nameof(Value));
            Error = null;
            Settle(isEmpty?.Invoke(result) == true ? TaskStateStatus.Empty : TaskStateStatus.Success);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (_running == cts)
                Status = _settled;
        }
        catch (Exception e)
        {
            if (_running != cts)
                return;

            System.Diagnostics.Debug.WriteLine($"[Spine] TaskState<{typeof(T).Name}> load failed: {e}");
            Error = e;

            // A failure that means "nothing to show", such as an AI feature that is off, is the empty state,
            // not an error with a retry. A result already on screen stays, as with any failure.
            Settle(HasResult ? _settled : e is IEmptyResultException ? TaskStateStatus.Empty : TaskStateStatus.Error);
        }
        finally
        {
            if (_running == cts)
                _running = null;

            cts.Dispose();
        }
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        _running?.Cancel();
        _running = null;

        Result = default;
        HasResult = false;
        OnPropertyChanged(nameof(Value));
        Error = null;
        Settle(TaskStateStatus.NotStarted);
    }

    private void Settle(TaskStateStatus status)
    {
        _settled = status;
        Status = status;
    }
}
