using AsyncAwaitBestPractices;
using System.ComponentModel;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The per-view side of <see cref="Highlight"/>: follows the view's handler, window and visibility, decides
/// when a run plays, pauses and ends, and hands the drawing to the platform's <see cref="HighlightRenderer"/>.
/// </summary>
internal sealed partial class HighlightState : IDisposable
{
    readonly View _view;
    HighlightRenderer? _renderer;
    HighlightClock? _clock;
    IDispatcherTimer? _timer;
    IDispatcherTimer? _lookAgain;
    CancellationTokenSource? _spotlight;
    Window? _window;
    readonly List<VisualElement> _scrollers = [];
    bool _inWindow;
    TimeSpan? _outSince;
    bool _stopped;
    bool _ended;
    bool _announced;
    bool _hintSet;
    string? _ownHint;
    bool _tapWatched;
    bool _platformTap;
    bool _tapCommand;
    TapGestureRecognizer? _tapRecognizer;
    bool _disposed;

    public HighlightState(View view)
    {
        _view = view;
        view.HandlerChanging += OnHandlerChanging;
        view.HandlerChanged += OnHandlerChanged;
        view.Loaded += OnLoaded;
        view.Unloaded += OnUnloaded;
        view.PropertyChanged += OnViewPropertyChanged;
        view.SizeChanged += OnSizeChanged;
        ReducedMotion.Changed += OnReducedMotionChanged;
        HighlightKeys.WasReset += OnKeysReset;
        SpineTheme.Track(view, OnThemeChanged);

        if (view.Handler is not null)
            Connect();
        if (view.IsLoaded)
            WatchWindow(view.Window);

        Update();
    }

    static TimeSpan Now => TimeSpan.FromMilliseconds(Environment.TickCount64);

    HighlightKind Kind => Highlight.GetKind(_view);

    /// <summary>Starts over: a new kind, repeat, key or activation plays from the beginning.</summary>
    public void Restart()
    {
        if (_disposed)
            return;

        StopRun(animated: true);
        _ended = false;
        Update();
    }

    /// <summary>Draws again with the current look: colours, intensity, size, theme or Reduce Motion changed.</summary>
    public void Refresh()
    {
        if (_disposed)
            return;

        if (_clock is not null)
            UpdateHint();

        Update();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopRun(animated: true);
        _disposed = true;
        _view.HandlerChanging -= OnHandlerChanging;
        _view.HandlerChanged -= OnHandlerChanged;
        _view.Loaded -= OnLoaded;
        _view.Unloaded -= OnUnloaded;
        _view.PropertyChanged -= OnViewPropertyChanged;
        _view.SizeChanged -= OnSizeChanged;
        ReducedMotion.Changed -= OnReducedMotionChanged;
        HighlightKeys.WasReset -= OnKeysReset;
        WatchWindow(null);
        WatchScrolling(false);
        Disconnect();
    }

    bool Wanted =>
        !_disposed
        && Kind is not HighlightKind.None
        && Highlight.GetIsActive(_view)
        && !_ended
        && !(Highlight.GetKey(_view) is { } key && HighlightKeys.HasSeen(key));

    bool Showable =>
        _renderer is not null
        && _view.IsLoaded
        && _view.IsVisible
        && !_stopped
        && _view.Width > 0
        && _view.Height > 0
        && OnShownPage();

    /// <summary>
    /// Whether the view is on a page Spine shows: the top of the current region, tab or sheet. A page under
    /// another one stays loaded but cannot be seen. Without Spine's navigation every page counts.
    /// </summary>
    bool OnShownPage()
    {
        var shown = SpineScreen.ShownPages;
        if (shown.Count == 0)
            return true;

        for (Element? element = _view; element is not null; element = element.Parent)
        {
            foreach (var page in shown)
            {
                if (ReferenceEquals(page.Page, element))
                    return true;
            }
        }

        return false;
    }

    /// <summary>The one place that decides what shows: start, resume, pause or stop the run.</summary>
    void Update()
    {
        if (!Wanted)
        {
            if (_clock is not null)
                StopRun(animated: true);
            return;
        }

        if (Kind == HighlightKind.Spotlight)
        {
            Spotlight();
            return;
        }

        if (_clock is null)
        {
            _clock = new HighlightClock(HighlightCycles.Length(Kind, Highlight.GetRepeat(_view)));
            _announced = false;
            WatchTap(HighlightCycles.Resolve(Kind, Highlight.GetRepeat(_view)).EndsOnTap);
        }

        var showable = Showable;
        _inWindow = showable && _renderer!.IsInWindow();
        if (!_inWindow)
        {
            _clock.Pause(Now);
            _timer?.Stop();
            _renderer?.Hide(animated: false);

            // Out of sight while everything else is ready, such as a page still sliding in: no event says
            // when it arrives, so look again for a moment. After that, scrolling and navigation report it.
            _outSince ??= Now;
            if (showable && Now - _outSince < TimeSpan.FromSeconds(2))
                LookAgain();
            return;
        }

        _outSince = null;
        _lookAgain?.Stop();

        Announce();
        UpdateHint();
        _renderer!.Show(HighlightLook.For(_view, Kind));
        _clock.Resume(Now);
        ScheduleEnd();
    }

    /// <summary>
    /// A spotlight is a one-step tour of the view. It waits for a tap whatever the repeat says: it covers the
    /// window, so it must not go by itself or come back. A tour cut short by navigation plays again later.
    /// </summary>
    void Spotlight()
    {
        if (_spotlight is not null || !Showable || _renderer?.IsInWindow() != true)
            return;

        _spotlight = new CancellationTokenSource();
        var token = _spotlight.Token;
        RunSpotlightAsync(token).SafeFireAndForget();
    }

    async Task RunSpotlightAsync(CancellationToken token)
    {
        try
        {
            var result = await Tours.RunAsync(null, [new TourStep(_view, Highlight.GetTip(_view) ?? "")], token);
            if (!token.IsCancellationRequested && result.Outcome is TourOutcome.Completed or TourOutcome.Skipped)
            {
                _spotlight = null;
                _clock = new HighlightClock(null);
                End(byTap: true);
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }

        _spotlight = null;
    }

    void ScheduleEnd()
    {
        if (_clock?.Remaining(Now) is not { } remaining)
            return;

        if (remaining <= TimeSpan.Zero)
        {
            End(byTap: false);
            return;
        }

        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Interval = remaining;
        _timer.Start();
    }

    IDispatcherTimer CreateTimer()
    {
        var timer = _view.Dispatcher.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            // The clock, not the timer, says when the run is over: a pause can come between the two.
            if (_clock?.Remaining(Now) is { } left && left <= TimeSpan.FromMilliseconds(15))
                End(byTap: false);
            else
                ScheduleEnd();
        };
        return timer;
    }

    void End(bool byTap)
    {
        if (_clock is null)
            return;

        var kind = Kind;
        StopRun(animated: true);
        _ended = true;

        if (Highlight.GetKey(_view) is { } key)
            HighlightKeys.MarkSeen(key);

        Highlight.RaiseEnded(_view, kind, byTap);
    }

    void StopRun(bool animated)
    {
        _spotlight?.Cancel();
        _spotlight = null;
        _timer?.Stop();
        _lookAgain?.Stop();
        _outSince = null;
        _clock = null;
        _renderer?.Hide(animated);
        RestoreHint();
        WatchTap(false);
    }

    void Announce()
    {
        if (_announced)
            return;

        _announced = true;
        if (Highlight.GetTip(_view) is { Length: > 0 } tip)
            SemanticScreenReader.Default.Announce(tip);
    }

    /// <summary>A kind that shows a lasting state carries its tip as the view's hint while it runs.</summary>
    void UpdateHint()
    {
        var lasting = Kind is HighlightKind.Edge or HighlightKind.Trace or HighlightKind.Breathe;
        if (!lasting || Highlight.GetTip(_view) is not { Length: > 0 } tip)
        {
            RestoreHint();
            return;
        }

        if (!_hintSet)
        {
            _ownHint = SemanticProperties.GetHint(_view);
            _hintSet = true;
        }

        SemanticProperties.SetHint(_view, tip);
    }

    void RestoreHint()
    {
        if (!_hintSet)
            return;

        _hintSet = false;
        if (_ownHint is null)
            _view.ClearValue(SemanticProperties.HintProperty);
        else
            SemanticProperties.SetHint(_view, _ownHint);
    }

    /// <summary>
    /// Watches for a tap that ends an <see cref="HighlightRepeat.UntilTapped"/> run without taking it from the
    /// view: a button's click, Tap's own report for a view with <c>Tap.Command</c> (a second tap handler would
    /// take the tap from Tap on Android), or else a recogniser of the platform's or MAUI's.
    /// </summary>
    void WatchTap(bool watch)
    {
        if (watch == _tapWatched)
            return;

        _tapWatched = watch;
        if (watch)
        {
            if (_view is Button button)
                button.Clicked += OnTapped;
            else if (_view is ImageButton imageButton)
                imageButton.Clicked += OnTapped;
            else if (Tap.GetCommand(_view) is not null)
            {
                Tap.Executed += OnTapExecuted;
                _tapCommand = true;
            }
            else if (!(_platformTap = WatchPlatformTap(true)))
            {
                _tapRecognizer = new TapGestureRecognizer();
                _tapRecognizer.Tapped += OnTapped;
                _view.GestureRecognizers.Add(_tapRecognizer);
            }

            return;
        }

        switch (_view)
        {
            case Button button:
                button.Clicked -= OnTapped;
                break;
            case ImageButton imageButton:
                imageButton.Clicked -= OnTapped;
                break;
        }

        if (_tapCommand)
            Tap.Executed -= OnTapExecuted;
        if (_platformTap)
            WatchPlatformTap(false);
        if (_tapRecognizer is not null)
        {
            _tapRecognizer.Tapped -= OnTapped;
            _view.GestureRecognizers.Remove(_tapRecognizer);
        }

        _tapCommand = false;
        _platformTap = false;
        _tapRecognizer = null;
    }

    void OnTapExecuted(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _view))
            OnTapped(sender, e);
    }

    void OnTapped(object? sender, EventArgs e)
    {
        if (_clock is not null && _tapWatched)
            End(byTap: true);
    }

    void OnHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        if (e.OldHandler is not null)
            Disconnect();
    }

    void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_view.Handler is not null)
        {
            Connect();
            if (_view.IsLoaded)
            {
                WatchWindow(_view.Window);
                WatchScrolling(true);
            }
        }

        Update();
    }

    void Connect()
    {
        if (_renderer is not null || _view.Handler?.PlatformView is not { } platformView)
            return;

        _renderer = new HighlightRenderer(platformView);
        if (_platformTap)
            WatchPlatformTap(true);
    }

    // Losing the handler is the only teardown some pages give: MAUI can drop a page without Unloaded.
    void Disconnect()
    {
        WatchWindow(null);
        WatchScrolling(false);

        if (_renderer is null)
            return;

        if (_platformTap)
            WatchPlatformTap(false);
        _clock?.Pause(Now);
        _timer?.Stop();
        _lookAgain?.Stop();
        _renderer.Dispose();
        _renderer = null;
    }

    void OnLoaded(object? sender, EventArgs e)
    {
        WatchWindow(_view.Window);
        WatchScrolling(true);
        Update();
    }

    void OnUnloaded(object? sender, EventArgs e)
    {
        WatchWindow(null);
        WatchScrolling(false);
        Update();
    }

    /// <summary>
    /// Follows the scroll views and lists the view sits in, so a hint that is scrolled out of sight pauses
    /// instead of playing, and counting as seen, where nobody can see it.
    /// </summary>
    void WatchScrolling(bool watch)
    {
        foreach (var scroller in _scrollers)
        {
            if (scroller is ScrollView scrollView)
                scrollView.Scrolled -= OnScrolled;
            else if (scroller is ItemsView itemsView)
                itemsView.Scrolled -= OnItemsScrolled;
        }

        _scrollers.Clear();
        if (!watch)
            return;

        for (var parent = _view.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ScrollView scrollView)
            {
                scrollView.Scrolled += OnScrolled;
                _scrollers.Add(scrollView);
            }
            else if (parent is ItemsView itemsView)
            {
                itemsView.Scrolled += OnItemsScrolled;
                _scrollers.Add(itemsView);
            }
        }
    }

    void LookAgain()
    {
        if (_lookAgain is null)
        {
            _lookAgain = _view.Dispatcher.CreateTimer();
            _lookAgain.Interval = TimeSpan.FromMilliseconds(250);
            _lookAgain.IsRepeating = false;
            _lookAgain.Tick += (_, _) => Update();
        }

        if (!_lookAgain.IsRunning)
            _lookAgain.Start();
    }

    void OnScrolled(object? sender, ScrolledEventArgs e) => CheckInWindow();

    void OnItemsScrolled(object? sender, ItemsViewScrolledEventArgs e) => CheckInWindow();

    // Called on every scroll frame: only a change in or out of sight costs an update.
    void CheckInWindow()
    {
        if (_clock is not null && _renderer is not null && _renderer.IsInWindow() != _inWindow)
            Update();
    }

    void WatchWindow(Window? window)
    {
        if (ReferenceEquals(window, _window))
            return;

        // Static and window events hold the state, and through it the page, so they are only watched while
        // the view is in a window.
        if (_window is not null)
        {
            _window.Stopped -= OnWindowStopped;
            _window.Resumed -= OnWindowResumed;
            SpineScreen.ShownPagesChanged -= OnShownPagesChanged;
        }

        _window = window;
        _stopped = false;

        if (_window is not null)
        {
            _window.Stopped += OnWindowStopped;
            _window.Resumed += OnWindowResumed;
            SpineScreen.ShownPagesChanged += OnShownPagesChanged;
        }
    }

    void OnWindowStopped(object? sender, EventArgs e)
    {
        _stopped = true;
        Update();
    }

    // Animations do not survive the background on Apple; Update draws them again.
    void OnWindowResumed(object? sender, EventArgs e)
    {
        _stopped = false;
        Update();
    }

    void OnViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == VisualElement.IsVisibleProperty.PropertyName
            || e.PropertyName == Border.StrokeShapeProperty.PropertyName
            || e.PropertyName == Button.CornerRadiusProperty.PropertyName)
        {
            Update();
        }
    }

    void OnSizeChanged(object? sender, EventArgs e) => Update();

    void OnReducedMotionChanged(object? sender, EventArgs e) => Update();

    void OnShownPagesChanged(object? sender, EventArgs e) => Update();

    void OnThemeChanged()
    {
        if (!_disposed)
            Update();
    }

    void OnKeysReset(object? sender, EventArgs e)
    {
        if (_ended && Highlight.GetKey(_view) is { } key && !HighlightKeys.HasSeen(key))
            Restart();
        else
            Update();
    }

    /// <summary>A platform's own tap watcher; false to use a MAUI <see cref="TapGestureRecognizer"/>.</summary>
    partial void WatchPlatformTapCore(bool watch, ref bool handled);

    bool WatchPlatformTap(bool watch)
    {
        var handled = false;
        WatchPlatformTapCore(watch, ref handled);
        return handled;
    }
}
