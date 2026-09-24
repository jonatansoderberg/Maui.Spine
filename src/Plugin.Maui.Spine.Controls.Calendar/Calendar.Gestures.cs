using System.Diagnostics;
#if IOS || MACCATALYST
using UIKit;
#endif

namespace Plugin.Maui.Spine.Controls;

public partial class Calendar
{
    private enum PanAxis { Undecided, Horizontal, Vertical }

    private const string SlideAnimationName = "SpineCalendarSlide";
    private const uint SlideDurationMs = 250;

    // A drag commits to the neighbouring page past this share of the width, or on a fling faster
    // than FlingVelocity (units per millisecond, ~500/s) in the direction of the drag.
    private const double CommitFraction = 1.0 / 3;
    private const double FlingVelocity = 0.5;

    // Movement before a pan is claimed as horizontal or left to the scroll view around it.
    private const double AxisLockDistance = 10;

    // A velocity sample older than this means the finger stopped before it lifted: no fling.
    private const long FlingSampleMaxAgeMs = 100;

    private PanAxis _panAxis;
    private int _dragDelta;
    private double _lastPanX;
    private long _lastPanTimestamp;
    private double _panVelocity;

    // The slide animating now: its direction (0 = none), whether it ends on the neighbour, and a
    // generation that retires the finished callback of a slide that was completed by hand.
    private int _slideDelta;
    private bool _slideCommits;
    private int _slideGeneration;

    /// <summary>One tap and one pan recognizer on the content host, and none on the cells.</summary>
    /// <remarks>
    /// Recognizers on the cells break both platforms: on Android a child with a tap recognizer
    /// consumes the touch, so a swipe that starts on a day never reaches the host, and on iOS a
    /// cell's and the host's recognizers both fire, so one swipe moves two months. With nothing
    /// below the host there is one gesture per touch; a tap is resolved to the cell under it by
    /// position. A tap is not reported once the finger has moved past the touch slop, and a pan
    /// does not start before it, so the two stay apart.
    /// </remarks>
    private void AttachGestures(View host)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnContentTapped;
        host.GestureRecognizers.Add(tap);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        host.GestureRecognizers.Add(pan);

#if IOS || MACCATALYST
        host.HandlerChanged += (_, _) => RestrictNativePanToHorizontal(host);
        host.Loaded += (_, _) => RestrictNativePanToHorizontal(host);
#endif
    }

#if IOS || MACCATALYST
    /// <summary>Lets the native pan begin only on a drag that is more horizontal than vertical.</summary>
    /// <remarks>
    /// MAUI's <c>UIPanGestureRecognizer</c> does not recognize simultaneously with others, so once it
    /// begins the <c>UIScrollView</c> around the calendar never gets the touch, and a vertical drag that
    /// starts on the calendar scrolls nothing. The managed axis lock in <see cref="OnPanRunning"/>
    /// comes too late for that. Refusing to begin leaves the drag to the scroll view. On Mac Catalyst
    /// a click-drag is the pan and wheel or trackpad scrolling never reaches it.
    /// </remarks>
    private static void RestrictNativePanToHorizontal(View host)
    {
        if (host.Handler?.PlatformView is not UIView view || view.GestureRecognizers is not { } recognizers)
            return;

        foreach (var recognizer in recognizers)
        {
            if (recognizer is UIPanGestureRecognizer pan)
            {
                // Velocity, not translation: when ShouldBegin runs the translation still reads {0, 0}.
                pan.ShouldBegin = g =>
                {
                    var velocity = ((UIPanGestureRecognizer)g).VelocityInView(g.View);
                    return Math.Abs(velocity.X) > Math.Abs(velocity.Y);
                };
            }
        }
    }
#endif

    private void OnContentTapped(object? sender, TappedEventArgs e)
    {
        // Mid-slide the cells are translated away from the bounds they are hit-tested by.
        if (_slideDelta != 0 || _panAxis == PanAxis.Horizontal)
            return;

        switch (_viewMode)
        {
            case CalendarViewMode.Month when _month is not null:
                if (e.GetPosition(_month.Grid) is { } dayPoint
                    && _month.DayCells.FirstOrDefault(c => c.Container.IsVisible && c.Container.Bounds.Contains(dayPoint)) is { } day)
                {
                    OnDayTapped(day);
                }
                break;

            case CalendarViewMode.Year when _year is not null:
                if (e.GetPosition(_year.Grid) is { } monthPoint
                    && _year.Cells.FindIndex(c => c.Cell.Bounds.Contains(monthPoint)) is var month and >= 0)
                {
                    OnMonthTapped(month + 1);
                }
                break;

            case CalendarViewMode.Decade when _decade is not null:
                if (e.GetPosition(_decade.Grid) is { } yearPoint
                    && _decade.Cells.FindIndex(c => c.Cell.Bounds.Contains(yearPoint)) is var year and >= 0)
                {
                    OnDecadeYearTapped(year);
                }
                break;
        }
    }

    /// <summary>
    /// Drags the page with the finger, the neighbour beside it, and on release snaps to the
    /// neighbour or springs back.
    /// </summary>
    /// <remarks>
    /// The axis is decided once, after <see cref="AxisLockDistance"/>. A vertical drag is left alone
    /// for the rest of the gesture, so the calendar never takes the scroll from a <c>ScrollView</c>
    /// around it. On Android the scroll view takes the touch over and the pan ends as
    /// <see cref="GestureStatus.Canceled"/>, which springs back.
    /// </remarks>
    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                CompleteSlide();
                _panAxis = PanAxis.Undecided;
                _dragDelta = 0;
                _panVelocity = 0;
                _lastPanX = 0;
                _lastPanTimestamp = Stopwatch.GetTimestamp();
                break;

            case GestureStatus.Running:
                OnPanRunning(e.TotalX, e.TotalY);
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                OnPanEnded(e.StatusType == GestureStatus.Completed);
                break;
        }
    }

    private void OnPanRunning(double totalX, double totalY)
    {
        if (_panAxis == PanAxis.Undecided)
        {
            if (Math.Abs(totalX) < AxisLockDistance && Math.Abs(totalY) < AxisLockDistance)
                return;

            _panAxis = Math.Abs(totalX) > Math.Abs(totalY) ? PanAxis.Horizontal : PanAxis.Vertical;
        }

        if (_panAxis != PanAxis.Horizontal)
            return;

        var now = Stopwatch.GetTimestamp();
        var elapsedMs = Stopwatch.GetElapsedTime(_lastPanTimestamp, now).TotalMilliseconds;
        if (elapsedMs > 0)
            _panVelocity = (totalX - _lastPanX) / elapsedMs;
        _lastPanX = totalX;
        _lastPanTimestamp = now;

        var width = _contentHost?.Width ?? 0;
        if (width <= 0 || totalX == 0)
            return;

        // Dragging left brings the next page in from the right.
        int delta = totalX < 0 ? 1 : -1;
        if (delta != _dragDelta)
        {
            // Back across the start: the other neighbour takes the place beside the page.
            if (!PreparePeek(delta))
            {
                // Nothing that way (the first or last supported year): the page stays put.
                _dragDelta = 0;
                SetSlideOffset(0, delta);
                return;
            }
            _dragDelta = delta;
        }

        SetSlideOffset(Math.Clamp(totalX, -width, width), delta);
    }

    private void OnPanEnded(bool completed)
    {
        var axis = _panAxis;
        _panAxis = PanAxis.Undecided;

        if (axis != PanAxis.Horizontal || _dragDelta == 0)
            return;

        int delta = _dragDelta;
        _dragDelta = 0;

        var width = _contentHost?.Width ?? 0;
        var offset = CurrentPage?.TranslationX ?? 0;

        bool recentSample = Stopwatch.GetElapsedTime(_lastPanTimestamp).TotalMilliseconds <= FlingSampleMaxAgeMs;
        bool flingOn = recentSample && _panVelocity * -delta > FlingVelocity;

        // A fling back towards the start cancels, even when the page is already past the threshold.
        bool flingBack = recentSample && _panVelocity * delta > FlingVelocity;

        bool commit = completed && width > 0 && !flingBack && (Math.Abs(offset) > width * CommitFraction || flingOn);
        AnimateSlide(offset, commit ? -delta * width : 0, delta, commit);
    }

    // ── Sliding ─────────────────────────────────────────────────────────────────

    private Grid? CurrentPage => _viewMode switch
    {
        CalendarViewMode.Month => _month?.Grid,
        CalendarViewMode.Year => _year?.Grid,
        _ => _decade?.Grid,
    };

    private Grid? PeekPage => _viewMode switch
    {
        CalendarViewMode.Month => _monthPeek?.Grid,
        CalendarViewMode.Year => _yearPeek?.Grid,
        _ => _decadePeek?.Grid,
    };

    /// <summary>
    /// Renders the page <paramref name="delta"/> away into the active view's neighbour and shows it.
    /// False when there is no such page.
    /// </summary>
    private bool PreparePeek(int delta)
    {
        var target = TargetDate(delta);
        if (IsDisplayed(target))
            return false;

        switch (_viewMode)
        {
            case CalendarViewMode.Month when _monthPeek is not null:
                RenderMonth(_monthPeek, target.Year, target.Month);
                break;
            case CalendarViewMode.Year when _yearPeek is not null:
                RenderYear(_yearPeek, target);
                break;
            case CalendarViewMode.Decade when _decadePeek is not null:
                RenderDecade(_decadePeek, target);
                break;
        }

        if (PeekPage is { } peek)
            peek.IsVisible = true;

        return true;
    }

    private void SetSlideOffset(double offset, int delta)
    {
        var width = _contentHost?.Width ?? 0;

        if (CurrentPage is { } current)
            current.TranslationX = offset;
        if (PeekPage is { } peek)
            peek.TranslationX = offset + delta * width;
    }

    /// <summary>The arrows: slide one page over, the way a committed drag ends.</summary>
    private void Navigate(int delta)
    {
        CompleteSlide();

        var width = _contentHost?.Width ?? 0;
        if (width <= 0)
        {
            SetDisplayMonth(TargetDate(delta));
            return;
        }

        if (!PreparePeek(delta))
            return;

        SetSlideOffset(0, delta);
        AnimateSlide(0, -delta * width, delta, commit: true);
    }

    /// <remarks>
    /// <see cref="Animation.Commit"/>, never a loop of awaited <c>TranslateTo</c>: that returns at once
    /// on a view that cannot animate yet, and the loop then holds the UI thread.
    /// </remarks>
    private void AnimateSlide(double from, double to, int delta, bool commit)
    {
        var width = _contentHost?.Width ?? 0;

        _slideDelta = delta;
        _slideCommits = commit;
        int generation = ++_slideGeneration;

        // The remaining distance sets the time, so a page released near its end does not crawl.
        var share = width > 0 ? Math.Abs(to - from) / width : 1;
        var length = (uint)Math.Clamp(share * SlideDurationMs, 1, SlideDurationMs);

        new Animation(offset => SetSlideOffset(offset, delta), from, to).Commit(
            this,
            SlideAnimationName,
            rate: 16,
            length: length,
            easing: Easing.CubicOut,
            finished: (_, _) =>
            {
                if (generation == _slideGeneration)
                    EndSlide();
            });
    }

    /// <summary>Jumps a slide in flight to where it was going. A no-op when nothing slides.</summary>
    private void CompleteSlide()
    {
        if (_slideDelta == 0)
            return;

        _slideGeneration++;
        this.AbortAnimation(SlideAnimationName);
        EndSlide();
    }

    /// <summary>Drops a slide in flight, and any drag, without navigating.</summary>
    private void CancelSlide()
    {
        _slideCommits = false;
        CompleteSlide();
        _dragDelta = 0;
        _panAxis = PanAxis.Undecided;
    }

    private void EndSlide()
    {
        int delta = _slideDelta;
        bool commits = _slideCommits;
        _slideDelta = 0;
        _slideCommits = false;

        if (commits)
        {
            var target = TargetDate(delta);

            // The neighbour already shows the target: it becomes the page, and the old page waits as
            // the next neighbour.
            switch (_viewMode)
            {
                case CalendarViewMode.Month: (_month, _monthPeek) = (_monthPeek, _month); break;
                case CalendarViewMode.Year: (_year, _yearPeek) = (_yearPeek, _year); break;
                case CalendarViewMode.Decade: (_decade, _decadePeek) = (_decadePeek, _decade); break;
            }

            ResetPages();
            SetDisplayMonth(target);
            UpdateHeader();
        }
        else
        {
            ResetPages();
        }
    }

    /// <summary>The active view's page on screen and in place, every other page hidden.</summary>
    private void ResetPages()
    {
        foreach (var page in AllPages())
        {
            page.TranslationX = 0;
            page.IsVisible = false;
        }

        if (CurrentPage is { } current)
            current.IsVisible = true;
    }
}
