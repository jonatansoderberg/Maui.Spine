using Microsoft.Maui.Controls.Shapes;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The bubble: a long-pressed header's full caption above (or below) it, and the "Copied"
/// confirmation near the bottom of the grid.
/// </summary>
/// <remarks>
/// <para>
/// A header narrower than its caption truncates, and a column such as a checkbox has no values to
/// infer the caption from. Long-pressing any header shows the whole text. It is deliberately not
/// conditioned on the text being truncated: MAUI has no reliable "did this label ellipsize" signal,
/// and a bubble that answers on some headers and not others is worse than one that always does.
/// </para>
/// <para>
/// Short press stays sorting. The long press is timed off a <see cref="PointerGestureRecognizer"/>, and
/// the tap that arrives on release is swallowed so asking what a header says never re-sorts the grid.
/// </para>
/// </remarks>
public partial class DataGrid
{
    private Border? _bubble;
    private Label? _bubbleLabel;
    private IDispatcherTimer? _bubbleHideTimer;
    private IDispatcherTimer? _headerPressTimer;

    /// <summary>The header the visible bubble belongs to; null for a bubble placed at the bottom.</summary>
    private View? _bubbleAnchor;

    /// <summary>
    /// Where the finger went down, in the root's coordinates. The bubble centres on this rather than
    /// on the header cell, which is as wide as its column.
    /// </summary>
    private Point? _bubblePressPoint;

    /// <summary>
    /// Set when a header press lasted long enough; read and cleared by the header's tap handler,
    /// which fires on release and would otherwise sort.
    /// </summary>
    private bool _headerLongPressFired;

    private const double BubbleGap = 8;

    private void AttachHeaderTooltip(View container, DataGridColumn column, DataGridStyleOptions options)
    {
        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += (_, e) => BeginHeaderLongPress(container, column, options, e.GetPosition(_root));
        // On Android these do not arrive once the tap detector has claimed the touch; the header's
        // tap handler is what ends a short press there.
        pointer.PointerReleased += (_, _) => CancelHeaderLongPress();
        pointer.PointerExited += (_, _) => CancelHeaderLongPress();
        container.GestureRecognizers.Add(pointer);
    }

    private void BeginHeaderLongPress(View container, DataGridColumn column, DataGridStyleOptions options, Point? pressPoint)
    {
        CancelHeaderLongPress();
        _headerLongPressFired = false;

        var timer = Dispatcher.CreateTimer();
        timer.Interval = options.LongPressDuration;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            _headerLongPressFired = true;
            _bubblePressPoint = pressPoint;
            ShowBubble(column.Header, container, options);
        };
        _headerPressTimer = timer;
        timer.Start();
    }

    private void CancelHeaderLongPress()
    {
        _headerPressTimer?.Stop();
        _headerPressTimer = null;
    }

    /// <summary>Shows <paramref name="text"/> in the bubble at <paramref name="anchor"/>, or near the bottom of the grid when it is null.</summary>
    private void ShowBubble(string? text, View? anchor, DataGridStyleOptions options)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // A header replaced by a sort, reload or layout switch still reports its old position.
        if (anchor is not null && OffsetWithin(anchor, _root) is null)
            return;

        EnsureBubble();

        _bubbleLabel!.Text = text;
        _bubbleLabel.TextColor = options.TooltipTextColor;
        _bubbleLabel.FontSize = options.TooltipFontSize;
        _bubbleLabel.FontFamily = options.FontFamily;
        _bubble!.BackgroundColor = options.TooltipBackgroundColor;
        _bubble.Padding = options.TooltipPadding;
        _bubble.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(options.TooltipCornerRadius) };

        _bubbleAnchor = anchor;
        if (anchor is null)
            _bubblePressPoint = null;

        // Transparent until placed, so it never shows for a frame at the previous position.
        _bubble.Opacity = 0;
        _bubble.IsVisible = true;
        PlaceBubble();

        _bubbleHideTimer ??= CreateBubbleHideTimer();
        _bubbleHideTimer.Stop();
        _bubbleHideTimer.Interval = anchor is null
            ? TimeSpan.FromSeconds(Math.Min(1.5, options.TooltipVisibleDuration.TotalSeconds))
            : options.TooltipVisibleDuration;
        _bubbleHideTimer.Start();
    }

    /// <summary>
    /// Places the bubble above its header, or below when there is no room above inside the grid (a
    /// clipping parent would cut it away), clamped to the grid's width. A bubble without a header
    /// sits centred near the bottom.
    /// </summary>
    /// <remarks>
    /// Placed with Translation from the bubble's laid-out size, never from a Measure() call: measuring
    /// a Border that was just made visible returns numbers that are not real yet. Translation costs no
    /// layout pass, so driving this from SizeChanged cannot loop.
    /// </remarks>
    private void PlaceBubble()
    {
        if (_bubble is null || !_bubble.IsVisible)
            return;

        if (_bubble.Height <= 0 || _bubble.Width <= 0)
            return; // not laid out yet; SizeChanged brings us back

        var available = _root.Width;

        if (_bubbleAnchor is null)
        {
            _bubble.TranslationX = Math.Max(0, (available - _bubble.Width) / 2);
            _bubble.TranslationY = Math.Max(0, _root.Height - _statusRow.Height - _bubble.Height - 16);
            _bubble.Opacity = 1;
            return;
        }

        if (OffsetWithin(_bubbleAnchor, _root) is not { } offset)
        {
            HideTooltip();
            return;
        }

        var (x, y) = offset;
        var centreOn = _bubblePressPoint?.X ?? x + _bubbleAnchor.Width / 2;
        _bubble.TranslationX = available > _bubble.Width
            ? Math.Clamp(centreOn - _bubble.Width / 2, 0, available - _bubble.Width)
            : 0;

        var above = y - _bubble.Height - BubbleGap;
        _bubble.TranslationY = above >= 0 ? above : y + _bubbleAnchor.Height + BubbleGap;

        _bubble.Opacity = 1;
    }

    private void HideTooltip()
    {
        // Cancel first: a press still timing would re-open the bubble against a header on its way out.
        CancelHeaderLongPress();

        _bubbleHideTimer?.Stop();
        _bubbleAnchor = null;
        _bubblePressPoint = null;

        if (_bubble is not null)
            _bubble.IsVisible = false;
    }

    private IDispatcherTimer CreateBubbleHideTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => HideTooltip();
        return timer;
    }

    /// <summary>Built on first use and reused: a grid nobody long-presses pays nothing.</summary>
    private void EnsureBubble()
    {
        if (_bubble is not null)
            return;

        _bubbleLabel = new Label { LineBreakMode = LineBreakMode.WordWrap, MaxLines = 3 };

        _bubble = new Border
        {
            Content = _bubbleLabel,
            StrokeThickness = 0,
            IsVisible = false,
            // Never a touch target: an overlay would swallow row taps while it is up.
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            MaximumWidthRequest = 320,
            // Above the status row and, on iOS, above a list that paints past its bounds.
            ZIndex = 100,
        };
        _bubble.SizeChanged += (_, _) => PlaceBubble();

        _root.Add(_bubble, 0, 0);
        Grid.SetRowSpan(_bubble, _root.RowDefinitions.Count);
    }

    /// <summary>
    /// Position of <paramref name="child"/> in <paramref name="ancestor"/>'s coordinates, or null when
    /// the parent chain never reaches it, which is how a replaced header cell is recognised.
    /// </summary>
    private static (double X, double Y)? OffsetWithin(VisualElement child, VisualElement ancestor)
    {
        double x = 0, y = 0;

        for (VisualElement? current = child; current is not null; current = current.Parent as VisualElement)
        {
            if (current == ancestor)
                return (x, y);

            x += current.X;
            y += current.Y;
        }

        return null;
    }
}
