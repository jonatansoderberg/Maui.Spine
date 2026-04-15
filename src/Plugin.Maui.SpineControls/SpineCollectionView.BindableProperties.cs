using Microsoft.Maui.Controls;

namespace Plugin.Maui.SpineControls;

public partial class SpineCollectionView
{
    // ────────────────────────────────────────────────────────────────────────
    // Header image / title
    // ────────────────────────────────────────────────────────────────────────

    public static readonly BindableProperty HeaderImageSourceProperty =
        BindableProperty.Create(nameof(HeaderImageSource), typeof(ImageSource), typeof(SpineCollectionView),
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnHeaderImageSourceChanged((ImageSource?)n));

    public ImageSource? HeaderImageSource
    {
        get => (ImageSource?)GetValue(HeaderImageSourceProperty);
        set => SetValue(HeaderImageSourceProperty, value);
    }

    public static readonly BindableProperty HeaderTitleProperty =
        BindableProperty.Create(nameof(HeaderTitle), typeof(string), typeof(SpineCollectionView), string.Empty,
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnHeaderTitleChanged((string?)n));

    public string? HeaderTitle
    {
        get => (string?)GetValue(HeaderTitleProperty);
        set => SetValue(HeaderTitleProperty, value);
    }

    public static readonly BindableProperty HeaderTitleColorProperty =
        BindableProperty.Create(nameof(HeaderTitleColor), typeof(Color), typeof(SpineCollectionView), Colors.White,
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnHeaderTitleColorChanged((Color?)n));

    public Color? HeaderTitleColor
    {
        get => (Color?)GetValue(HeaderTitleColorProperty);
        set => SetValue(HeaderTitleColorProperty, value);
    }

    public static readonly BindableProperty HeaderTitleFontFamilyProperty =
        BindableProperty.Create(nameof(HeaderTitleFontFamily), typeof(string), typeof(SpineCollectionView), null,
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnHeaderTitleFontFamilyChanged((string?)n));

    public string? HeaderTitleFontFamily
    {
        get => (string?)GetValue(HeaderTitleFontFamilyProperty);
        set => SetValue(HeaderTitleFontFamilyProperty, value);
    }

    public static readonly BindableProperty HeaderTitleFontSizeProperty =
        BindableProperty.Create(nameof(HeaderTitleFontSize), typeof(double), typeof(SpineCollectionView), 25.0,
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnHeaderTitleFontSizeChanged((double)n));

    public double HeaderTitleFontSize
    {
        get => (double)GetValue(HeaderTitleFontSizeProperty);
        set => SetValue(HeaderTitleFontSizeProperty, value);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Header dimensions
    // ────────────────────────────────────────────────────────────────────────

    public static readonly BindableProperty HeaderMaxHeightProperty =
        BindableProperty.Create(nameof(HeaderMaxHeight), typeof(double), typeof(SpineCollectionView), 230.0,
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnHeaderMaxHeightChanged((double)n));

    public double HeaderMaxHeight
    {
        get => (double)GetValue(HeaderMaxHeightProperty);
        set => SetValue(HeaderMaxHeightProperty, value);
    }

    public static readonly BindableProperty HeaderMinHeightProperty =
        BindableProperty.Create(nameof(HeaderMinHeight), typeof(double), typeof(SpineCollectionView), 42.0,
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnHeaderMinHeightChanged((double)n));

    public double HeaderMinHeight
    {
        get => (double)GetValue(HeaderMinHeightProperty);
        set => SetValue(HeaderMinHeightProperty, value);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Header overlay / content slots
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opacity driven 0→1 as header collapses (ideal for acrylic MaterialFrame).
    /// </summary>
    public static readonly BindableProperty HeaderOverlayContentProperty =
        BindableProperty.Create(nameof(HeaderOverlayContent), typeof(View), typeof(SpineCollectionView),
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnHeaderOverlayContentChanged((View?)n));

    public View? HeaderOverlayContent
    {
        get => (View?)GetValue(HeaderOverlayContentProperty);
        set => SetValue(HeaderOverlayContentProperty, value);
    }

    /// <summary>
    /// Direct access to the AbsoluteLayout overlay for extra views.
    /// </summary>
    public AbsoluteLayout HeaderChildrenLayout
    {
        get
        {
            EnsureHeaderBuilt();
            return _headerOverlayLayout!;
        }
    }

    /// <summary>
    /// Interactive content pinned to the top of the visible header area.
    /// Stays at the viewport top regardless of collapse state (TranslationY = 0).
    /// Set InputTransparent="True" CascadeInputTransparent="False" on the root
    /// view so scroll gestures on empty areas still reach the CollectionView.
    /// </summary>
    public static readonly BindableProperty HeaderTopContentProperty =
        BindableProperty.Create(nameof(HeaderTopContent), typeof(View), typeof(SpineCollectionView),
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnHeaderTopContentChanged((View?)n));

    public View? HeaderTopContent
    {
        get => (View?)GetValue(HeaderTopContentProperty);
        set => SetValue(HeaderTopContentProperty, value);
    }

    /// <summary>
    /// Interactive content anchored to the bottom edge of the collapsing header.
    /// Tracks the bottom edge via TranslationY (GPU-only, no layout passes).
    /// The built-in title label is hidden while this is set.
    /// Set InputTransparent="True" CascadeInputTransparent="False" on the root
    /// view so scroll gestures on empty areas still reach the CollectionView.
    /// </summary>
    public static readonly BindableProperty HeaderBottomContentProperty =
        BindableProperty.Create(nameof(HeaderBottomContent), typeof(View), typeof(SpineCollectionView),
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnHeaderBottomContentChanged((View?)n));

    public View? HeaderBottomContent
    {
        get => (View?)GetValue(HeaderBottomContentProperty);
        set => SetValue(HeaderBottomContentProperty, value);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scroll-bar inset / drag-region
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Right inset applied to the header so it does not overlap the native scrollbar.
    /// Defaults to 16 on Windows (where the scrollbar occupies space), 0 elsewhere.
    /// </summary>
    public static readonly BindableProperty HeaderScrollBarInsetProperty =
        BindableProperty.Create(nameof(HeaderScrollBarInset), typeof(double), typeof(SpineCollectionView), DefaultScrollBarInset,
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnHeaderScrollBarInsetChanged((double)n));

    public double HeaderScrollBarInset
    {
        get => (double)GetValue(HeaderScrollBarInsetProperty);
        set => SetValue(HeaderScrollBarInsetProperty, value);
    }

    /// <summary>
    /// When true and running on Windows, the header area is registered as a
    /// draggable title-bar region (excluding interactive child elements).
    /// </summary>
    public static readonly BindableProperty EnableHeaderAsDragRegionOnWindowsProperty =
        BindableProperty.Create(nameof(EnableHeaderAsDragRegionOnWindows), typeof(bool), typeof(SpineCollectionView), true,
            propertyChanged: (b, _, n) => ((SpineCollectionView)b).OnEnableHeaderAsDragRegionChanged((bool)n));

    public bool EnableHeaderAsDragRegionOnWindows
    {
        get => (bool)GetValue(EnableHeaderAsDragRegionOnWindowsProperty);
        set => SetValue(EnableHeaderAsDragRegionOnWindowsProperty, value);
    }

    /// <summary>
    /// Optional override to customize which child views are treated as interactive
    /// (excluded from the drag region). Return <see langword="true"/> to mark a view
    /// as interactive. When <see langword="null"/>, built-in heuristics are used.
    /// </summary>
    public Func<View, bool>? IsInteractiveElementOverride { get; set; }

    // ────────────────────────────────────────────────────────────────────────
    // Adaptive overlay
    // ────────────────────────────────────────────────────────────────────────

    public static readonly BindableProperty EnableAdaptiveOverlayProperty =
        BindableProperty.Create(nameof(EnableAdaptiveOverlay), typeof(bool), typeof(SpineCollectionView), false,
            propertyChanged: static (bindable, _, newValue) =>
            {
                if ((bool)newValue && bindable is SpineCollectionView self && self.Parent != null)
                    self.HookAdaptive();
            });

    public bool EnableAdaptiveOverlay
    {
        get => (bool)GetValue(EnableAdaptiveOverlayProperty);
        set => SetValue(EnableAdaptiveOverlayProperty, value);
    }

    /// <summary>
    /// When <see langword="true"/> the adaptive tinting logic also samples the top-right area of
    /// the header image (where the OS min/max/close buttons appear on Windows) and fires
    /// <see cref="CaptionButtonColorRequested"/> with the computed foreground colour.
    /// Has no visible effect on non-Windows platforms unless a custom
    /// <see cref="CaptionButtonColorRequested"/> handler is registered.
    /// </summary>
    public bool AdaptiveCaptionButtons { get; set; }

    /// <summary>
    /// Visual elements whose foreground colour is automatically adjusted based on
    /// the header image luminance behind them. Elements inside
    /// <see cref="HeaderTopContent"/> and <see cref="HeaderBottomContent"/> are
    /// auto-registered when <see cref="EnableAdaptiveOverlay"/> is true.
    /// </summary>
    public IList<VisualElement> AdaptiveTargets { get; } = new List<VisualElement>();

    public Color AdaptiveLightColor { get; set; } = Colors.White;
    public Color AdaptiveDarkColor { get; set; } = Colors.Black;

    /// <summary>
    /// Callback invoked on the UI thread whenever the adaptive colour for the OS caption buttons
    /// (min / max / close) changes. Subscribe to this from your Windows platform code to forward
    /// the computed colour to <c>AppWindowTitleBar.ButtonForegroundColor</c>.
    /// Typically wired up once by <c>SpineApplication</c> — you do not need to set this manually.
    /// </summary>
    public static Action<Color>? CaptionButtonColorRequested { get; set; }
}
