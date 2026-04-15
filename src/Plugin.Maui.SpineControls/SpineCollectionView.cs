using Microsoft.Maui.Controls;
using System;

namespace Plugin.Maui.SpineControls;

/// <summary>
/// A CollectionView subclass with a collapsing sticky header and optional
/// adaptive overlay that tints child elements based on the header image.
///
/// The header lives in a Grid that this control injects itself into on
/// OnParentSet, so it is always pinned above the scroll area and can
/// never be scrolled away.
///
/// Responsibilities are split across partial classes for maintainability:
///   SpineCollectionView.cs                  – state, ctor, parent injection
///   SpineCollectionView.BindableProperties.cs – all bindable property declarations
///   SpineCollectionView.Header.cs           – header construction &amp; property-changed handlers
///   SpineCollectionView.Scrolling.cs        – scroll handling
///   SpineCollectionView.AdaptiveOverlay.cs  – adaptive colour sampling (SkiaSharp)
///   SpineCollectionView.Windows.cs          – Windows drag-region (platform partial)
/// </summary>
public partial class SpineCollectionView : CollectionView
{
    // ────────────────────────────────────────────────────────────────────────
    // Internal state
    // ────────────────────────────────────────────────────────────────────────
    private const double DefaultScrollBarInset = 0.0;

    private int _clipHash;
    private bool _wrapperInjected;
    private bool _headerBuilt;

    // Current visible header height — kept in sync each frame; used only to
    // seed a new anchor when scroll direction changes.
    private double _currentHeight = -1;

    // Anchor: the scroll-offset and header-height captured at the moment the
    // current scroll direction began.  All height calculations are derived
    // from here deterministically — never accumulated — so rounding errors
    // can never drift over a long scroll session.
    private double _anchorOffset    = -1;
    private double _anchorHeight    = -1;

    // +1 = scrolling down (collapsing), -1 = scrolling up (expanding), 0 = unknown
    private int _scrollDirection = 0;

    // Last dispatched overlay opacity (rounded); skip dispatch when unchanged
    private double _lastOverlayOpacity = -1;

    // Cached bindable-property values for the scroll hot-path — updated via
    // propertyChanged so GetValue() is never called per frame.
    private double _maxHeight    = 230.0;
    private double _minHeight    = 42.0;
    private double _collapseZone = 188.0;

    // ────────────────────────────────────────────────────────────────────────
    // Named parts
    // ────────────────────────────────────────────────────────────────────────
    private Border? _headerBorder;
    private Image? _headerImage;
    protected Image? HeaderImage => _headerImage;
    private AbsoluteLayout? _headerOverlayLayout;
    private AbsoluteLayout? _headerTopActionsLayout;
    private AbsoluteLayout? _headerBottomActionsLayout;
    private Label? _titleLabel;
    private View? _overlayView;
    private View? _headerTopContent;
    private View? _headerBottomContent;

    // ────────────────────────────────────────────────────────────────────────
    // Construction
    // ────────────────────────────────────────────────────────────────────────

    public SpineCollectionView()
    {
        Scrolled += OnScrolled;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Drag-region partial hooks (implemented on Windows, no-op elsewhere)
    // ────────────────────────────────────────────────────────────────────────

    partial void SetupDragRegion();
    partial void ScheduleDragRegionUpdate();
    partial void TeardownDragRegion();
    partial void OnHandlerChangedPartial();

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        OnHandlerChangedPartial();
    }

    private void OnEnableHeaderAsDragRegionChanged(bool enabled)
    {
        System.Diagnostics.Debug.WriteLine($"[DragRegion] OnEnableHeaderAsDragRegionChanged: enabled={enabled}, _headerBorder is null={_headerBorder == null}, _wrapperInjected={_wrapperInjected}");
        if (enabled)
            SetupDragRegion();
        else
            TeardownDragRegion();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Parent injection
    // ────────────────────────────────────────────────────────────────────────

    private void EnsureHeaderBuilt()
    {
        if (_headerBuilt) return;
        _headerBuilt = true;
        _headerBorder = BuildHeaderBorder();

        // Debug: catch any TranslationY write regardless of its source.
        // If TRANSLATIONCHANGED appears in the log without a preceding VISUAL
        // line, something outside OnScrolled is moving the header.
        _headerBorder.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(View.TranslationY))
                System.Diagnostics.Debug.WriteLine(
                    $"[Scroll] TRANSLATIONCHANGED  val={_headerBorder!.TranslationY:F2}");
        };
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();

        if (Parent == null || _wrapperInjected) return;

        EnsureHeaderBuilt();

        if (Parent is not Layout parentLayout) return;

        _wrapperInjected = true;

        int myIndex      = parentLayout.Children.IndexOf(this);
        int gridRow      = Grid.GetRow(this);
        int gridColumn   = Grid.GetColumn(this);
        int gridRowSpan  = Grid.GetRowSpan(this);
        int gridColSpan  = Grid.GetColumnSpan(this);

        parentLayout.Children.RemoveAt(myIndex);

        // Single-row, single-column wrapper — both children overlap in the
        // same cell. The CollectionView fills the full height; the header
        // floats on top via ZIndex and moves with TranslationY only.
        var wrapper = new Grid();

        // Debug: a SizeChanged on the wrapper Grid means a layout pass ran
        // and potentially repositioned every child, including _headerBorder.
        wrapper.SizeChanged += (_, _) =>
            System.Diagnostics.Debug.WriteLine(
                $"[Scroll] WRAPPER-SIZECHANGED  w={wrapper.Width:F2}  h={wrapper.Height:F2}");

        Grid.SetRow(wrapper, gridRow);
        Grid.SetColumn(wrapper, gridColumn);
        Grid.SetRowSpan(wrapper, gridRowSpan);
        Grid.SetColumnSpan(wrapper, gridColSpan);

        // CollectionView goes in first (bottom layer).
        // A fixed-height Header spacer pushes the first item below the fully-
        // expanded header. It scrolls with the list so items naturally appear
        // from under the collapsing header as the user scrolls down.
        Header = new BoxView
        {
            HeightRequest   = _maxHeight,
            Color           = Colors.Transparent,
            VerticalOptions = LayoutOptions.Start
        };
        wrapper.Add(this);

        // Header floats on top, anchored to the top of the cell.
        _headerBorder!.VerticalOptions = LayoutOptions.Start;
        wrapper.Add(_headerBorder);

        // Top-pinned interactive overlay — stays at TranslationY = 0 so
        // children positioned relative to the viewport top never move off-screen.
        _headerTopActionsLayout = new AbsoluteLayout
        {
            HeightRequest           = _maxHeight,
            Margin                  = new Thickness(0, 0, HeaderScrollBarInset, 0),
            VerticalOptions         = LayoutOptions.Start,
            ZIndex                  = 1002,
            InputTransparent        = true,
            CascadeInputTransparent = false
        };
        wrapper.Add(_headerTopActionsLayout);

        // Bottom-tracking interactive overlay — uses the same TranslationY as
        // _headerBorder so children at the proportional bottom ride the
        // collapsing edge. Pure GPU transform, zero layout passes.
        _headerBottomActionsLayout = new AbsoluteLayout
        {
            HeightRequest           = _maxHeight,
            Margin                  = new Thickness(0, 0, HeaderScrollBarInset, 0),
            VerticalOptions         = LayoutOptions.Start,
            ZIndex                  = 1001,
            InputTransparent        = true,
            CascadeInputTransparent = false
        };
        wrapper.Add(_headerBottomActionsLayout);

        // Apply any content that was set before the parent was attached.
        var pendingTop = (View?)GetValue(HeaderTopContentProperty);
        if (pendingTop != null)
            OnHeaderTopContentChanged(pendingTop);

        var pendingBottom = (View?)GetValue(HeaderBottomContentProperty);
        if (pendingBottom != null)
            OnHeaderBottomContentChanged(pendingBottom);

        parentLayout.Children.Insert(myIndex, wrapper);

        System.Diagnostics.Debug.WriteLine($"[DragRegion] OnParentSet complete: EnableHeaderAsDragRegionOnWindows={EnableHeaderAsDragRegionOnWindows}, _headerBorder is null={_headerBorder == null}");
        if (EnableHeaderAsDragRegionOnWindows)
            SetupDragRegion();

        // Hook adaptive overlay if enabled before the parent was set.
        if (EnableAdaptiveOverlay)
            HookAdaptive();
    }
}
