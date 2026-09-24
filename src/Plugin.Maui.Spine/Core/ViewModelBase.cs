using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Base class for all Spine page ViewModels.
/// Provides lifecycle hooks, observable navigation metadata, and the
/// <see cref="PageActions"/> collection used to populate header-bar buttons.
/// Inherit from this class and inject services via the primary constructor.
/// </summary>
/// <example>
/// <code>
/// public partial class HomePageViewModel(INavigationService _navigation) : ViewModelBase
/// {
///     [ObservableProperty]
///     private string? _greeting;
///
///     [PageAction("Save")]
///     [RelayCommand]
///     private Task SaveAsync() { ... }
///
///     public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
///     {
///         Greeting = await _service.LoadGreetingAsync();
///     }
/// }
/// </code>
/// </example>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>Page title text bound to the header or title bar.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Controls whether the native window title bar is shown (desktop only).</summary>
    [ObservableProperty]
    public partial bool IsTitleBarVisible { get; set; }

    /// <summary>
    /// The edges on which Spine applies system-bar padding (safe area) for this page.
    /// Set automatically by Spine on each navigation based on the page's
    /// <see cref="NavigableRegionAttribute.SafeAreaEdges"/> or <see cref="NavigableSheetAttribute.SafeAreaEdges"/>.
    /// Edges excluded from this value cause content to render edge-to-edge behind that bar —
    /// use <see cref="SafeAreaInsets"/> to offset your content manually on those edges.
    /// </summary>
    [ObservableProperty]
    public partial SafeAreaEdges SafeAreaEdges { get; set; }

    /// <summary>
    /// The recommended <see cref="Thickness"/> a page should apply as <c>Margin</c> or <c>Padding</c>
    /// to keep its content clear of the system bars it extends behind.
    /// Set automatically by Spine before <see cref="OnAppearingAsync"/> fires.
    /// An edge is non-zero only when that edge is <em>not</em> included in <see cref="SafeAreaEdges"/>
    /// (i.e., Spine is not padding it and the page must inset itself).
    /// </summary>
    [ObservableProperty]
    public partial Thickness SafeAreaInsets { get; set; }

    /// <summary>
    /// The edges on which the page's first scrolling view takes <see cref="SafeAreaInsets"/> as a
    /// native content inset. Resolved by Spine from the page's attribute or the relevant defaults
    /// and applied to that view before the page appears.
    /// </summary>
    [ObservableProperty]
    public partial SafeAreaEdges ScrollInset { get; set; }

    /// <summary>
    /// The raw system bar dimensions in device-independent pixels (status bar, navigation bar,
    /// display cutouts). Available on all platforms — non-zero on Android, <see cref="Thickness.Zero"/>
    /// on platforms that handle safe areas natively.
    /// Use this to build custom edge-to-edge layouts that need the exact bar measurements.
    /// Set automatically by Spine from <see cref="ISystemInsetsProvider"/> before navigation.
    /// </summary>
    [ObservableProperty]
    public partial Thickness SystemBarInsets { get; set; }

    /// <summary>Controls whether Spine's in-page header bar is shown for this page.</summary>
    [ObservableProperty]
    public partial bool IsHeaderBarVisible { get; set; }

    /// <summary>Controls whether the back button is shown in the header bar.</summary>
    [ObservableProperty]
    public partial bool IsBackButtonVisible { get; set; }

    /// <summary>Whether the header bar takes its own row or floats over the content. Set from the page's attribute.</summary>
    [ObservableProperty]
    public partial HeaderBarMode HeaderBarMode { get; set; }

    /// <summary>
    /// Whether the page opens on its own large title that collapses into the header bar. Set from
    /// the page's attribute; set it later to change the header while the page is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool LargeTitle { get; set; }

    /// <summary>
    /// What is behind the header bar when content is under it. Set from the page's attribute; set it
    /// later to change the bar while the page is shown. The page is laid out again, under the bar or
    /// below it. <see cref="EffectiveHeaderBarBackground"/> is what Spine made of it.
    /// </summary>
    [ObservableProperty]
    public partial HeaderBarBackground HeaderBarBackground { get; set; }

    partial void OnHeaderBarBackgroundChanged(HeaderBarBackground value) => ReapplyHeaderBar?.Invoke();

    partial void OnHeaderBarModeChanged(HeaderBarMode value) => ReapplyHeaderBar?.Invoke();

    partial void OnLargeTitleChanged(bool value) => ReapplyHeaderBar?.Invoke();

    /// <summary>
    /// Resolves the header again after <see cref="HeaderBarBackground"/>, <see cref="HeaderBarMode"/>
    /// or <see cref="LargeTitle"/> changes on a page Spine has shown. Set by Spine when it applies the page's attribute.
    /// </summary>
    internal Action? ReapplyHeaderBar { get; set; }

    /// <summary>
    /// How far a <see cref="LargeTitle"/> page's header has collapsed: 0 while the large title is
    /// in view, 1 once the header bar's own title has faded in. Follows the scroll offset; bind to
    /// it to fade something of the page's own, such as a greeting in a hero.
    /// </summary>
    [ObservableProperty]
    public partial double HeaderBarCollapseProgress { get; internal set; }

    /// <summary>How far the header's background has faded in, 0 to 1. Read by the page presenter.</summary>
    [ObservableProperty]
    internal partial double ScrollEdgeProgress { get; set; }

    /// <summary>
    /// The background Spine resolved from <see cref="HeaderBarBackground"/> for this page, never
    /// <see cref="HeaderBarBackground.Auto"/>: what <c>Auto</c> means here, and <see cref="HeaderBarBackground.Solid"/>
    /// where a scroll edge value cannot be drawn (before iOS 26, or with Reduce Transparency on).
    /// </summary>
    [ObservableProperty]
    public partial HeaderBarBackground EffectiveHeaderBarBackground { get; internal set; } = HeaderBarBackground.Solid;

    /// <summary>
    /// Whether the page is laid out under the header bar: content starts there (Overlay) or scrolls
    /// under it. Only a <see cref="HeaderBarBackground.Solid"/> bar over a normal page, which hides
    /// whatever would be under it, keeps a row of its own.
    /// </summary>
    internal bool HeaderBarFloats =>
        HeaderBarMode == HeaderBarMode.Overlay || LargeTitle || EffectiveHeaderBarBackground != HeaderBarBackground.Solid;

    /// <summary>The view whose scroll offset the header follows, once Spine has found it.</summary>
    [ObservableProperty]
    internal partial View? HeaderBarScrollSource { get; set; }

    /// <summary>A fixed colour for the header bar's title and action icons, or <see langword="null"/> to follow the theme.</summary>
    [ObservableProperty]
    public partial Color? HeaderBarForeground { get; set; }

    /// <summary>The status bar style Spine applies while this page is shown.</summary>
    [ObservableProperty]
    public partial StatusBarStyle StatusBarStyle { get; set; }

    /// <summary>Where the page title is rendered — header bar or title bar.</summary>
    [ObservableProperty]
    public partial TitlePlacement TitlePlacement { get; set; }

    /// <summary>Horizontal alignment of the title text within the header bar.</summary>
    [ObservableProperty]
    public partial TitleAlignment TitleAlignment { get; set; }

    /// <summary>
    /// Actions displayed as buttons in the header bar. Declare them with
    /// <see cref="PageActionAttribute"/> on a command, or add instances here (the constructor is a
    /// good place). Adding, removing, or changing a property of an action while the page is
    /// showing updates the header bar.
    /// </summary>
    public ObservableCollection<PageAction> PageActions { get; } = new();

    /// <summary>
    /// The first visible action with <see cref="PageActionPlacement.Secondary"/> placement,
    /// or <see langword="null"/> if none exists.
    /// Bound to the secondary (right-hand) action slot in the header bar.
    /// </summary>
    public PageAction? DefaultPageAction => PageActions.FirstOrDefault(a => a is { IsVisible: true, Placement: PageActionPlacement.Secondary });

    /// <summary>
    /// Called by Spine when the page becomes visible.
    /// Override to load data, populate <see cref="PageActions"/>, or react to the
    /// <paramref name="navigationDirection"/>.
    /// </summary>
    /// <param name="navigationDirection">Whether the page was navigated <i>to</i> or returned <i>back</i> to.</param>
    public virtual Task OnAppearingAsync(NavigationDirection navigationDirection) => Task.CompletedTask;

    /// <summary>
    /// Called by Spine just before the page is hidden (navigating away or closing the sheet).
    /// </summary>
    /// <param name="navigationDirection">The direction of the navigation that is about to occur.</param>
    public virtual Task OnDisappearingAsync(NavigationDirection navigationDirection) => Task.CompletedTask;

    /// <summary>
    /// Called by Spine when the app returns to the foreground, or its window is activated again,
    /// while this page is shown: the current page of the region or of the selected tab, and of an
    /// open sheet together with the page under it. Override to refresh what may have changed while
    /// the app was away, such as today's date or data from a server.
    /// </summary>
    /// <remarks>
    /// Not called on the first activation at launch, which <see cref="OnAppearingAsync"/> already
    /// covers — only after a deactivation. Anything that takes the window out of the foreground or
    /// out of focus counts: going to the background, but also the notification shade, a system
    /// dialog, or another window on the desktop. Pages that are not shown (covered by another page
    /// on the stack, or on another tab) are not called; they get <see cref="OnAppearingAsync"/> when
    /// they are shown again.
    /// </remarks>
    public virtual Task OnResumedAsync() => Task.CompletedTask;

    /// <summary>
    /// Whether this page has already been told it is showing.
    /// </summary>
    /// <remarks>
    /// One appearance is one announcement. Two announcements mean two rounds of
    /// <see cref="OnAppearingAsync"/> running at once and writing the same properties in an order
    /// neither of them decides — measured on Mac Catalyst, where realizing the first tab announced
    /// it and the host page's own <c>OnAppearing</c> announced it again, and where every sheet was
    /// announced once before it was presented and once more when its region took it (#140).
    /// </remarks>
    private bool _appeared;

    /// <summary>
    /// Announces the appearing, unless this page has already been told.
    /// </summary>
    internal Task SendAppearingAsync(NavigationDirection navigationDirection)
    {
        if (_appeared)
            return Task.CompletedTask;

        _appeared = true;
        BeginLifetime();

        return OnAppearingAsync(navigationDirection);
    }

    /// <summary>Announces the disappearing, which begins the next appearance.</summary>
    internal Task SendDisappearingAsync(NavigationDirection navigationDirection)
    {
        _appeared = false;
        EndLifetime();

        return OnDisappearingAsync(navigationDirection);
    }

    /// <summary>
    /// Forgets that this page was told it is showing, so the next announcement lands.
    /// </summary>
    /// <remarks>
    /// A page replaced as a region's root is never told it disappeared — the stack is simply
    /// emptied under it — and would otherwise carry a stale "already showing" for the rest of the
    /// run and never appear again.
    /// </remarks>
    internal void ForgetAppearance()
    {
        _appeared = false;
        EndLifetime();
    }

    // --- Work that lives with the page -------------------------------------------------------

    private static readonly CancellationToken _cancelled = new(canceled: true);
    private CancellationTokenSource? _lifetime;
    private readonly List<PollRegistration> _polls = [];
    private readonly List<(Action Subscribe, Action Unsubscribe)> _whileVisible = [];
    private bool _suspended;

    /// <summary>
    /// A token that is cancelled when the page disappears (navigated away from, its sheet closed,
    /// its tab left) and renewed each time it appears. Pass it to loads started for this page so
    /// they stop with it; before the first appearance and while hidden it is already cancelled.
    /// It does not cancel when the app merely goes to the background — the page is still the one
    /// on screen; use <see cref="Poll"/> for work that should pause then.
    /// </summary>
    public CancellationToken PageLifetime => _lifetime?.Token ?? _cancelled;

    /// <summary>
    /// Runs <paramref name="work"/> on the UI thread when the page appears and then every
    /// <paramref name="interval"/>; pauses while the page is hidden or the window is deactivated
    /// (background, notification shade, another window), and runs again at once when the page
    /// reappears or the window is activated. Register once, from the constructor or
    /// <see cref="OnAppearingAsync"/>; the registration lives as long as the view model.
    /// An exception thrown by <paramref name="work"/> is logged and the loop goes on.
    /// </summary>
    protected void Poll(TimeSpan interval, Func<CancellationToken, Task> work)
    {
        var poll = new PollRegistration(interval, work);
        _polls.Add(poll);

        if (_appeared && !_suspended)
            poll.Start();
    }

    /// <summary>
    /// Subscribes <paramref name="handler"/> while the page is showing: <paramref name="subscribe"/>
    /// runs when the page appears, <paramref name="unsubscribe"/> when it disappears, and the
    /// handler is marshalled to the UI thread. For an event of type <see cref="Action"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// WhileVisible(h => _activities.ActivitiesChanged += h, h => _activities.ActivitiesChanged -= h, OnActivitiesChanged);
    /// </code>
    /// </example>
    protected void WhileVisible(Action<Action> subscribe, Action<Action> unsubscribe, Action handler)
    {
        void Marshalled() => OnMainThread(handler);
        Register(() => subscribe(Marshalled), () => unsubscribe(Marshalled));
    }

    /// <summary>Subscribes while the page is showing; for an event of type <see cref="Action{T}"/>.</summary>
    protected void WhileVisible<T>(Action<Action<T>> subscribe, Action<Action<T>> unsubscribe, Action<T> handler)
    {
        void Marshalled(T value) => OnMainThread(() => handler(value));
        Register(() => subscribe(Marshalled), () => unsubscribe(Marshalled));
    }

    /// <summary>Subscribes while the page is showing; for an event of type <see cref="EventHandler{TEventArgs}"/>.</summary>
    protected void WhileVisible<TEventArgs>(Action<EventHandler<TEventArgs>> subscribe, Action<EventHandler<TEventArgs>> unsubscribe, EventHandler<TEventArgs> handler)
    {
        void Marshalled(object? sender, TEventArgs args) => OnMainThread(() => handler(sender, args));
        Register(() => subscribe(Marshalled), () => unsubscribe(Marshalled));
    }

    /// <summary>
    /// Runs <paramref name="subscribe"/> when the page appears and <paramref name="unsubscribe"/>
    /// when it disappears, for anything the typed overloads do not fit. Nothing is marshalled.
    /// </summary>
    protected void WhileVisible(Action subscribe, Action unsubscribe) => Register(subscribe, unsubscribe);

    private void Register(Action subscribe, Action unsubscribe)
    {
        _whileVisible.Add((subscribe, unsubscribe));

        if (_appeared)
            subscribe();
    }

    private static void OnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
            action();
        else
            MainThread.BeginInvokeOnMainThread(action);
    }

    private void BeginLifetime()
    {
        _lifetime = new CancellationTokenSource();
        _suspended = false;

        foreach (var (subscribe, _) in _whileVisible)
            subscribe();

        foreach (var poll in _polls)
            poll.Start();
    }

    private void EndLifetime()
    {
        foreach (var poll in _polls)
            poll.Stop();

        foreach (var (_, unsubscribe) in _whileVisible)
            unsubscribe();

        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
    }

    /// <summary>The window went out of the foreground while this page is shown: polls pause.</summary>
    internal void SendSuspended()
    {
        if (!_appeared || _suspended)
            return;

        _suspended = true;

        foreach (var poll in _polls)
            poll.Stop();
    }

    /// <summary>The window is back while this page is shown: polls run again at once.</summary>
    internal void SendResumed()
    {
        if (!_appeared || !_suspended)
            return;

        _suspended = false;

        foreach (var poll in _polls)
            poll.Start();
    }

    /// <summary>One <see cref="Poll"/>: its interval, its work, and the loop currently running it.</summary>
    private sealed class PollRegistration(TimeSpan interval, Func<CancellationToken, Task> work)
    {
        private CancellationTokenSource? _running;

        public void Start()
        {
            Stop();
            var cts = _running = new CancellationTokenSource();
            _ = RunAsync(cts.Token);
        }

        public void Stop()
        {
            _running?.Cancel();
            _running?.Dispose();
            _running = null;
        }

        // Awaited on the UI thread's context, so work runs there and a bound property can be set directly.
        private async Task RunAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(interval);

            try
            {
                await StepAsync(ct);

                while (await timer.WaitForNextTickAsync(ct))
                    await StepAsync(ct);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task StepAsync(CancellationToken ct)
        {
            try
            {
                await work(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[Spine] Poll failed: {e}");
            }
        }
    }

    /// <summary>
    /// Called when the page is dismissed (via back navigation or sheet close) without an explicit
    /// result being returned via <see cref="INavigationService.ReturnAsync"/>.
    /// Override to perform cleanup or default-result logic.
    /// </summary>
    public virtual Task OnDismissedAsync() => Task.CompletedTask;

    /// <summary>
    /// Called before a back navigation is executed. Return <see langword="false"/> to cancel the back
    /// gesture (e.g., to prompt the user to save unsaved changes).
    /// </summary>
    public virtual Task<bool> OnBackRequestedAsync() => Task.FromResult(true);

    /// <summary>
    /// Called before a sheet close is executed. Return <see langword="false"/> to cancel the dismissal.
    /// </summary>
    public virtual Task<bool> OnCloseRequestedAsync() => Task.FromResult(true);

    /// <summary>
    /// Called on a tab root's ViewModel when its already-active tab is re-selected while the
    /// stack is at the root. Override for scroll-to-top handling. Only invoked for pages
    /// decorated with <see cref="NavigableTabAttribute"/>.
    /// </summary>
    public virtual Task OnTabReselectedAsync() => Task.CompletedTask;

    /// <summary>
    /// Holds the pending <see cref="TaskCompletionSource{T}"/> for an in-progress
    /// <c>NavigateToWithResultAsync</c> call targeting this page.  Managed by
    /// <c>NavigationService</c> and <c>NavigationRegionViewModel</c>.
    /// </summary>
    internal TaskCompletionSource<object?>? PendingResult { get; set; }

    /// <summary>Initializes the ViewModel and subscribes to <see cref="PageActions"/> collection changes.</summary>
    protected ViewModelBase()
    {
        PageActions.CollectionChanged += OnPageActionsCollectionChanged;
    }

    /// <summary>
    /// Raised when <see cref="PageActions"/> changes or any action in it changes a property,
    /// so the header bar can resolve its slots again.
    /// </summary>
    internal event Action? PageActionsChanged;

    /// <summary>Whether the actions declared with <see cref="PageActionAttribute"/> have been added.</summary>
    internal bool DeclaredActionsAdded { get; set; }

    private readonly HashSet<PageAction> _watchedActions = [];

    private void OnPageActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Reset carries no old items, so reconcile against the collection instead of the event.
        foreach (var action in _watchedActions.Where(a => !PageActions.Contains(a)).ToList())
        {
            action.PropertyChanged -= OnPageActionPropertyChanged;
            _watchedActions.Remove(action);
        }

        foreach (var action in PageActions)
        {
            if (_watchedActions.Add(action))
                action.PropertyChanged += OnPageActionPropertyChanged;
        }

        OnPropertyChanged(nameof(DefaultPageAction));
        PageActionsChanged?.Invoke();
    }

    private void OnPageActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PageAction.IsVisible))
            OnPropertyChanged(nameof(DefaultPageAction));

        PageActionsChanged?.Invoke();
    }

}