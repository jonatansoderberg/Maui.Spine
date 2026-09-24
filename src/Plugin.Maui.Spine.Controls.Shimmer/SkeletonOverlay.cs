using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The overlay behind both <see cref="Shimmer"/> and <see cref="Skeleton"/>: a <see cref="GraphicsView"/>
/// on top of its host that collects placeholder blocks from the host's visual tree, fills the ones
/// without a colour of their own and sweeps the wave across all of them.
/// </summary>
/// <remarks>
/// It takes no room in its parent and covers the parent's whole bounds, so it can be added to any
/// layout without moving anything: a stack's spacing before it is handed back through a negative
/// desired size.
/// </remarks>
internal sealed class SkeletonOverlay : GraphicsView
{
    private const string WaveAnimationName = "SpineSkeletonWave";
    private const double LineHeightFactor = 1.2;
    private const double BarHeightFactor = 0.7;
    private const double FallbackBarFraction = 0.6;
    private const double FallbackBlockSize = 48;
    private const int MaxSettleAttempts = 10;

    private readonly VisualElement _host;
    private readonly Func<VisualElement?>? _skeletonRoot;
    private readonly SkeletonDrawable _drawable = new();
    private readonly List<VisualElement> _tracked = [];
    private readonly Dictionary<VisualElement, object> _hidden = [];
    private readonly Dictionary<VisualElement, (bool Width, bool Height)> _reserved = [];

    private ShimmerStyleOptions? _styleOptions;
    private double? _waveWidth;
    private double? _waveOpacity;
    private ShimmerStyleOptions _style;
    private bool _isLoading;
    private bool _isAttached;
    private bool _waveRunning;
    private bool _refreshQueued;
    private bool _released;
    private bool _unsettled;
    private int _settleAttempts;

    /// <param name="host">The view the overlay covers: the <see cref="Shimmer"/>, or the layout under <see cref="Skeleton"/>.</param>
    /// <param name="skeletonRoot">
    /// A <see cref="Shimmer"/>'s placeholder layout, walked by the placeholder rules. <see langword="null"/>
    /// for a skeleton layout, whose own views become the blocks.
    /// </param>
    public SkeletonOverlay(VisualElement host, Func<VisualElement?>? skeletonRoot)
    {
        _host = host;
        _skeletonRoot = skeletonRoot;
        _style = ShimmerStyleOptions.Resolve(null);
        _isLoading = skeletonRoot is null;

        Drawable = _drawable;
        BackgroundColor = Colors.Transparent;
        _drawable.WaveEverywhereWhenEmpty = !IsSkeletonLayout;

        // Over a real layout the hidden views must not take taps; over a Shimmer the overlay is decoration.
        InputTransparent = !IsSkeletonLayout;
        ZIndex = 10_000;

        PushStyle();
        UpdateSemantics();

        SpineTheme.Track(this, Repaint);
        ReduceMotion.Listen(this);

        Loaded += OnLoaded;

        // The overlay can be arranged after its host reported its size (Android); the wave needs a width.
        SizeChanged += (_, _) => UpdateAnimationState();
        // A BindableLayout reset removes the overlay and Skeleton puts it straight back; the Unloaded
        // of the removal can arrive after that, while the overlay is on screen again.
        Unloaded += (_, _) =>
        {
            if (!IsLoaded)
                Detach();
        };

        // Losing the handler is the only teardown an abandoned Android window gives: MAUI can drop
        // the page without raising Unloaded, and a committed animation keeps ticking from MAUI's
        // static animation registry until something aborts it.
        HandlerChanged += (_, _) =>
        {
            if (Handler is null)
                Detach();
        };

        _host.SizeChanged += OnHostChanged;
        _host.PropertyChanged += OnHostPropertyChanged;
        _host.DescendantAdded += OnHostDescendantsChanged;
        _host.DescendantRemoved += OnHostDescendantsChanged;
    }

    private bool IsSkeletonLayout => _skeletonRoot is null;

    public ShimmerStyleOptions? StyleOptions
    {
        get => _styleOptions;
        set
        {
            _styleOptions = value;
            PushStyle();

            // The Animation's length is fixed at Commit, so a new duration needs a restart.
            if (_waveRunning)
            {
                StopWave();
                UpdateAnimationState();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value)
                return;
            _isLoading = value;
            UpdateSemantics();
            UpdateAnimationState();
        }
    }

    /// <summary>Overrides <see cref="ShimmerStyleOptions.WaveWidth"/> when set.</summary>
    public double? WaveWidth
    {
        get => _waveWidth;
        set
        {
            _waveWidth = value;
            PushStyle();
        }
    }

    /// <summary>Overrides <see cref="ShimmerStyleOptions.WaveOpacity"/> when set.</summary>
    public double? WaveOpacity
    {
        get => _waveOpacity;
        set
        {
            _waveOpacity = value;
            PushStyle();
        }
    }

    /// <summary>Picks the overlay up again after it was put back into a layout that is on screen.</summary>
    public void EnsureAttached()
    {
        if (IsLoaded && !_isAttached && !_released)
            OnLoaded(this, EventArgs.Empty);
    }

    public void OnReduceMotionChanged()
    {
        // Restarted rather than left alone: with animations off, MAUI's Android ticker stops ticking
        // the committed animation, and it does not come back by itself when they are on again.
        StopWave();
        UpdateAnimationState();
    }

    /// <summary>
    /// Scans the placeholders again after the current layout pass. For changes made from code deep
    /// inside the tree that raise no size change.
    /// </summary>
    public void QueueRefresh()
    {
        if (_refreshQueued || _released)
            return;

        _refreshQueued = true;

        // MAUI raises SizeChanged from the Frame setter, before the element's children are arranged;
        // scanning straight from the event reads their previous frames and keeps a stale clip, and a
        // Fill-width block then stays clipped to its old, narrower bounds.
        Dispatcher.Dispatch(() =>
        {
            _refreshQueued = false;
            if (_released)
                return;

            try
            {
                Refresh();
            }
            catch (Exception ex)
            {
                // A scan over a tree that a theme change or a page swap is rearranging must not take
                // the process with it; the next size change scans again.
                System.Diagnostics.Debug.WriteLine($"[Spine] Skeleton scan of {_host.GetType().Name} failed: {ex}");
            }
        });
    }

    /// <summary>Scans the placeholders now.</summary>
    public void Refresh()
    {
        Untrack();
        _drawable.Placeholders.Clear();
        _unsettled = false;

        if (_skeletonRoot is null)
            CollectLeaves(_host, 0, 0);
        else if (_skeletonRoot() is { } skeleton)
            CollectPlaceholders(skeleton, skeleton.Frame.X, skeleton.Frame.Y);

        _drawable.InvalidatePlaceholders();
        Invalidate();

        // A view given a minimum size is not always arranged at it by the time the scan runs, and
        // when a BindableLayout rebuilds its rows no size change reports the final frames (seen on
        // iOS: rows scanned at height 0 kept their bars stacked on top of each other). Look again
        // shortly, a bounded number of times.
        if (_unsettled && _settleAttempts < MaxSettleAttempts)
        {
            _settleAttempts++;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), QueueRefresh);
        }
        else if (!_unsettled)
        {
            _settleAttempts = 0;
        }
    }

    /// <summary>
    /// Ends a skeleton layout: stops the wave, drops every subscription and gives the views back
    /// their opacity, their place in the accessibility tree and the sizes they had.
    /// </summary>
    public void Release()
    {
        _released = true;
        Detach();

        _host.SizeChanged -= OnHostChanged;
        _host.PropertyChanged -= OnHostPropertyChanged;
        _host.DescendantAdded -= OnHostDescendantsChanged;
        _host.DescendantRemoved -= OnHostDescendantsChanged;

        foreach (var (view, token) in _hidden)
            LeafHiding.Restore(view, token);
        _hidden.Clear();

        foreach (var (view, (width, height)) in _reserved)
        {
            if (width)
                view.ClearValue(MinimumWidthRequestProperty);
            if (height)
                view.ClearValue(MinimumHeightRequestProperty);
        }
        _reserved.Clear();
    }

    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        // A stack adds its spacing before every visible child, this one included; giving it back
        // keeps the stack exactly as tall (or wide) as without the overlay.
        if (Parent is StackBase { Spacing: > 0 } stack && stack.Children.Any(c => c != this && c.Visibility != Visibility.Collapsed))
        {
            var horizontal = stack is HorizontalStackLayout or StackLayout { Orientation: StackOrientation.Horizontal };
            return horizontal ? new Size(-stack.Spacing, 0) : new Size(0, -stack.Spacing);
        }

        return Size.Zero;
    }

    protected override Size ArrangeOverride(Rect bounds) =>
        Parent is VisualElement parent
            ? base.ArrangeOverride(new Rect(0, 0, parent.Width, parent.Height))
            : base.ArrangeOverride(bounds);

    private void OnLoaded(object? sender, EventArgs e)
    {
        _isAttached = true;
        Refresh();
        QueueRefresh();
        UpdateAnimationState();
    }

    private void Detach()
    {
        _isAttached = false;
        StopWave();

        // Everything tracked is inside the host today, but an element that outlives the overlay would
        // root it and its whole window through the SizeChanged handler; untracking costs nothing
        // because Loaded scans from scratch.
        Untrack();
    }

    private void OnHostChanged(object? sender, EventArgs e) => OnLayoutChanged();

    private void OnHostDescendantsChanged(object? sender, ElementEventArgs e)
    {
        if (e.Element != this)
            OnLayoutChanged();
    }

    private void OnHostPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == IsVisibleProperty.PropertyName)
            UpdateAnimationState();
    }

    private void OnLayoutChanged()
    {
        if (_isAttached)
            QueueRefresh();
        UpdateAnimationState();
    }

    private void Repaint()
    {
        PushStyle();
        UpdateSemantics();
        if (_isAttached)
            Refresh();
    }

    private void PushStyle()
    {
        _style = ShimmerStyleOptions.Resolve(_styleOptions);
        _drawable.PlaceholderColor = _style.PlaceholderColor ?? Colors.LightGray;
        _drawable.WaveColor = _style.WaveColor ?? Colors.White;
        _drawable.WaveOpacity = (float)Math.Clamp(_waveOpacity ?? _style.WaveOpacity, 0, 1);
        _drawable.WaveWidth = (float)Math.Clamp(_waveWidth ?? _style.WaveWidth, 0.01, 1);
        _drawable.WaveAngle = (float)_style.WaveAngle;
        _drawable.InvalidatePaint();
        _drawable.InvalidatePlaceholders();
        Invalidate();
    }

    private void UpdateSemantics()
    {
        if (_isLoading)
        {
            SemanticProperties.SetDescription(this, SpineStrings.Current["Shimmer.Loading"]);
            AutomationProperties.SetIsInAccessibleTree(this, true);
        }
        else
        {
            ClearValue(SemanticProperties.DescriptionProperty);
            AutomationProperties.SetIsInAccessibleTree(this, false);
        }
    }

    private void UpdateAnimationState()
    {
        if (_isLoading && _isAttached && _host.IsVisible && Width > 0 && !ReduceMotion.IsEnabled)
        {
            if (!_waveRunning)
                StartWave();
        }
        else
        {
            StopWave();
        }
    }

    private void StartWave()
    {
        this.AbortAnimation(WaveAnimationName);

        var length = (uint)Math.Max(1, _style.WaveDuration.TotalMilliseconds);
        _drawable.ShowWave = true;

        // Every overlay reads the same clock, so a list of skeleton rows sweeps as one.
        var sweep = new Animation(_ =>
        {
            _drawable.Progress = (float)(Environment.TickCount64 % length) / length;
            Invalidate();
        });

        // ~40 fps: smooth for a slow, soft band, and fewer invalidations while pages build content under it.
        sweep.Commit(this, WaveAnimationName, rate: 24, length: length, easing: Easing.Linear, repeat: () => true);
        _waveRunning = true;
    }

    private void StopWave()
    {
        if (!_waveRunning && !_drawable.ShowWave)
            return;

        this.AbortAnimation(WaveAnimationName);
        _waveRunning = false;
        _drawable.ShowWave = false;
        Invalidate();
    }

    /// <summary>
    /// Every visual child except a <see cref="Shape"/> and a skeleton overlay (this one, or one still fading
    /// out). A shape in a skeleton is a
    /// <c>Border</c>'s <c>StrokeShape</c>, and one set by a <c>Style</c> is a single instance shared
    /// by every border using that style for the life of the process: a <c>SizeChanged</c> handler on
    /// it roots the overlay and its whole window. Shapes have no frame of their own, so nothing is lost.
    /// Do not "simplify" this away.
    /// </summary>
    private IEnumerable<VisualElement> WalkableChildren(VisualElement element) =>
        ((IVisualTreeElement)element).GetVisualChildren().OfType<VisualElement>().Where(c => c is not Shape and not SkeletonOverlay);

    // Shimmer: BoxViews, empty Borders and childless elements with a background are the blocks.
    private void CollectPlaceholders(VisualElement element, double x, double y)
    {
        foreach (var child in WalkableChildren(element))
        {
            // Every element on the way down is watched: a block sized by its parent is re-arranged
            // after the root's own SizeChanged, and only its own event says the bounds went stale.
            Track(child);

            if (child.Frame.Width <= 0 || child.Frame.Height <= 0)
                continue;

            var (cx, cy) = Offset(element, child, x, y);

            if (IsPlaceholder(child))
            {
                var rect = new RectF((float)cx, (float)cy, (float)child.Frame.Width, (float)child.Frame.Height);
                _drawable.Placeholders.Add(new Placeholder(rect, OwnCornerRadius(child, rect), !HasOwnPaint(child)));
            }
            else
            {
                CollectPlaceholders(child, cx, cy);
            }
        }
    }

    private bool IsPlaceholder(VisualElement element) => element switch
    {
        BoxView => true,
        Border { Content: null } => true,
        _ => element.BackgroundColor is { Alpha: > 0 } && !WalkableChildren(element).Any(),
    };

    private static bool HasOwnPaint(VisualElement element) => element switch
    {
        BoxView box => box.Color is { Alpha: > 0 } || element.BackgroundColor is { Alpha: > 0 } || !Brush.IsNullOrEmpty(element.Background),
        _ => element.BackgroundColor is { Alpha: > 0 } || !Brush.IsNullOrEmpty(element.Background),
    };

    private static float OwnCornerRadius(VisualElement element, RectF rect) => element switch
    {
        Border { StrokeShape: RoundRectangle round } => (float)round.CornerRadius.TopLeft,
        Border { StrokeShape: Ellipse } => Math.Min(rect.Width, rect.Height) / 2,
        BoxView box => (float)box.CornerRadius.TopLeft,
        _ => 0f,
    };

    // Skeleton layout: containers keep drawing, every other view is hidden and becomes a block.
    private void CollectLeaves(VisualElement element, double x, double y)
    {
        foreach (var child in WalkableChildren(element))
        {
            Track(child);

            if (!child.IsVisible)
                continue;

            // A nested skeleton layout draws itself.
            if (child is Microsoft.Maui.Controls.Layout && Skeleton.GetIsActive(child))
                continue;

            var (cx, cy) = Offset(element, child, x, y);

            if (IsContainer(child))
            {
                CollectLeaves(child, cx, cy);
                continue;
            }

            Hide(child);

            // A size set now shows after the layout pass; scan again then rather than count on a
            // size change arriving.
            if (Reserve(child))
                QueueRefresh();

            AddLeaf(child, cx, cy);
        }
    }

    private static bool IsContainer(VisualElement element) => element switch
    {
        Microsoft.Maui.Controls.Layout => true,
        Border border => border.Content is not null,
        ContentView content => content.Content is not null,
        ScrollView scroll => scroll.Content is not null,
        _ => false,
    };

    private void Hide(VisualElement view)
    {
        // Hidden again on every scan: a view that got a new platform view since (a recycled cell,
        // a page shown again) would otherwise show through. The first token is the one to restore.
        if (LeafHiding.Hide(view) is { } token)
            _hidden.TryAdd(view, token);
    }

    /// <summary>
    /// Gives a view that has no size without its data a minimum one: a label as many lines as it
    /// will show, an image its requested size or a square. Only values the app did not set are
    /// touched, and <see cref="Release"/> clears them again.
    /// </summary>
    /// <returns>Whether a size was set now.</returns>
    private bool Reserve(VisualElement view)
    {
        var width = false;
        var height = false;
        var overrideHeight = Skeleton.GetHeight(view);

        if (overrideHeight > 0)
        {
            height = SetMinimum(view, MinimumHeightRequestProperty, overrideHeight);
        }
        else if (view is Label label && string.IsNullOrEmpty(label.Text) && label.FormattedText is null)
        {
            height = SetMinimum(view, MinimumHeightRequestProperty, Lines(label, 0) * LineHeight(label));
        }
        else if (view is Image image && (image.Frame.Width <= 0 || image.Frame.Height <= 0))
        {
            var side = image.WidthRequest > 0 ? image.WidthRequest
                : image.HeightRequest > 0 ? image.HeightRequest
                : image.Frame.Width > 0 ? Math.Min(image.Frame.Width, FallbackBlockSize * 4)
                : FallbackBlockSize;

            if (image.Frame.Width <= 0)
                width = SetMinimum(view, MinimumWidthRequestProperty, side);
            if (image.Frame.Height <= 0)
                height = SetMinimum(view, MinimumHeightRequestProperty, side);
        }

        if (!width && !height)
            return false;

        _reserved[view] = _reserved.TryGetValue(view, out var had) ? (had.Width || width, had.Height || height) : (width, height);
        return true;
    }

    private static bool SetMinimum(VisualElement view, BindableProperty property, double value)
    {
        if (view.IsSet(property))
            return false;

        view.SetValue(property, value);
        return true;
    }

    private void AddLeaf(VisualElement view, double x, double y)
    {
        var frame = view.Frame;
        if (_reserved.ContainsKey(view) && (frame.Height + 0.5 < view.MinimumHeightRequest || frame.Width + 0.5 < view.MinimumWidthRequest))
        {
            _unsettled = true;
            InvalidateUpTo(view);
        }
        var available = AvailableWidth(view);
        var overrideWidth = Skeleton.GetWidth(view);
        var radius = (float)Math.Max(0, _style.CornerRadius);

        if (view is Label label)
        {
            var lineHeight = LineHeight(label);
            var lines = Lines(label, frame.Height);
            var barHeight = (float)Math.Max(2, lineHeight * BarHeightFactor);
            var hasText = !string.IsNullOrEmpty(label.Text) || label.FormattedText is not null;

            for (var i = 0; i < lines; i++)
            {
                var last = i == lines - 1;
                var width = overrideWidth > 0 && (last || lines == 1) ? Resolve(overrideWidth, available)
                    : hasText && lines == 1 ? Math.Min(frame.Width, Math.Max(0, label.DesiredSize.Width - label.Margin.HorizontalThickness))
                    : last ? available * FallbackBarFraction
                    : available;

                if (width <= 0)
                    continue;

                var top = y + i * lineHeight + (lineHeight - barHeight) / 2;
                var rect = new RectF((float)x, (float)top, (float)width, barHeight);
                _drawable.Placeholders.Add(new Placeholder(rect, Math.Min(radius, barHeight / 2), true));
            }
            return;
        }

        if (frame.Width <= 0 || frame.Height <= 0)
            return;

        var blockWidth = overrideWidth > 0 ? Resolve(overrideWidth, available) : frame.Width;
        var block = new RectF((float)x, (float)y, (float)blockWidth, (float)frame.Height);

        // A view that is a Border's only content (an avatar in a circle) takes the border's shape.
        var shaped = view.Parent is Border border && border.Content == view ? OwnCornerRadius(border, block) : 0f;
        _drawable.Placeholders.Add(new Placeholder(block, shaped > 0 ? shaped : Math.Min(radius, Math.Min(block.Width, block.Height) / 2), true));
    }

    /// <summary>
    /// Asks for a new measure of <paramref name="view"/> and every container up to the host. A minimum
    /// size set while a <c>BindableLayout</c> is still adding the row does not always reach the row's
    /// cached measure on iOS, and the row stays at the height it had without text.
    /// </summary>
    private void InvalidateUpTo(VisualElement view)
    {
        for (Element? element = view; element is not null && element != _host; element = element.Parent)
        {
            if (element is IView measured)
                measured.InvalidateMeasure();
        }

        ((IView)_host).InvalidateMeasure();
    }

    private static double Resolve(double value, double available) => value <= 1 ? value * available : value;

    private static double LineHeight(Label label) =>
        label.FontSize * (label.LineHeight > 0 ? label.LineHeight : LineHeightFactor);

    private static int Lines(Label label, double height)
    {
        var lines = Skeleton.GetLines(label);
        if (lines > 0)
            return lines;

        return height > 0 ? Math.Max(1, (int)Math.Round(height / LineHeight(label))) : 1;
    }

    /// <summary>
    /// The width a view could take: its own when it fills its slot, otherwise what is left of its
    /// parent's content width from where it starts.
    /// </summary>
    private static double AvailableWidth(VisualElement view)
    {
        var frame = view.Frame;
        if (view.Parent is not VisualElement parent)
            return frame.Width;

        var padding = parent switch
        {
            Microsoft.Maui.Controls.Layout layout => layout.Padding,
            Border border => border.Padding,
            ContentView content => content.Padding,
            _ => default,
        };

        var remaining = parent.Width - padding.Right - frame.X - ((view as View)?.Margin.Right ?? 0);
        return Math.Max(frame.Width, remaining);
    }

    private static (double X, double Y) Offset(VisualElement parent, VisualElement child, double x, double y)
    {
        var cx = x + child.Frame.X;
        var cy = y + child.Frame.Y;

        if (parent is ScrollView scroll)
        {
            cx -= scroll.ScrollX;
            cy -= scroll.ScrollY;
        }

        return (cx, cy);
    }

    private void Track(VisualElement element)
    {
        element.SizeChanged += OnTrackedSizeChanged;
        _tracked.Add(element);
    }

    private void Untrack()
    {
        foreach (var element in _tracked)
            element.SizeChanged -= OnTrackedSizeChanged;
        _tracked.Clear();
    }

    private void OnTrackedSizeChanged(object? sender, EventArgs e)
    {
        if (_isAttached)
            QueueRefresh();
    }
}
