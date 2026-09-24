using System.Globalization;
using Plugin.Maui.Spine.Core;
using Microsoft.Maui.Layouts;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// Internal <see cref="Grid"/> that hosts a page view and an optional title label inside a
/// <see cref="NavigationRegion"/>. The title label is bound to the page's ViewModel
/// and its visibility is driven by <see cref="Plugin.Maui.Spine.Core.ViewModelBase.IsHeaderBarVisible"/>.
/// </summary>
internal sealed class PagePresenter : Grid
{
    private const string HeaderBarTitleStyleKey = "HeaderBarTitle";

    /// <summary>Breathing room between the title and whatever sits next to it.</summary>
    internal const double TitleEdgePadding = 8;

    private Label? _titleLabel;
    private readonly TitleSlotLayout _titleBar;

    // Behind the title row of a collapsing header: transparent at the top, solid once content
    // scrolls under it. An opaque colour faded with Opacity, because a BoxView with an alpha
    // colour paints over black on iOS.
    private readonly BoxView _barBackground;
    private ContentPresenter _contentPresenter;
    private readonly ContentView _footerHost;
    private IPageFooterSource? _footerSource;
    private double _sheetOverhang;

    /// <summary>
    /// The page view currently hosted in this presenter.
    /// Assigning a new value re-binds the title label and slot bindings to the new page's ViewModel.
    /// </summary>
    public View? Content
    {
        get => _contentPresenter.Content;
        set
        {
            _contentPresenter.Content = value;

            // Update bindings sourced from the page view model
            _titleLabel?.SetBinding(Label.TextProperty, new Binding("BindingContext.Title", source: Content));
            _titleLabel?.SetBinding(Label.IsVisibleProperty, new Binding("BindingContext.IsHeaderBarVisible", source: Content));
            _titleLabel?.SetBinding(Label.HorizontalTextAlignmentProperty, new Binding("BindingContext.TitleAlignment",
                source: Content,
                converter: new TitleAlignmentToTextAlignmentConverter()));

            WatchPage(Content?.BindingContext as ViewModelBase);
            WatchFooter(Content as IPageFooterSource);

            // The header bar measures its own actions and publishes the result on the region
            // view model, which is this presenter's BindingContext.
            var slotsBinding = new MultiBinding { Converter = new ActionsToTitleSlotsConverter() };
            slotsBinding.Bindings.Add(new Binding("BindingContext.PrimaryActionSlot", source: this));
            slotsBinding.Bindings.Add(new Binding("BindingContext.SecondaryActionSlot", source: this));
            _titleBar.SetBinding(TitleSlotLayout.SlotsProperty, slotsBinding);
        }
    }

    /// <summary>Initializes the presenter, setting up the title label and content grid rows.</summary>
    public PagePresenter()
    {
        _contentPresenter = new ContentPresenter();

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _titleLabel = new Label
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            IsVisible = false
        };

        _titleBar = new TitleSlotLayout();
        _titleBar.Add(_titleLabel);

        _barBackground = new BoxView { InputTransparent = true, IsVisible = false, Opacity = 0 };

        Children.Add(_barBackground);
        Children.Add(_titleBar);

        Grid.SetRow(_contentPresenter, 1);
        Children.Add(_contentPresenter);

        _footerHost = new ContentView { IsVisible = false };
        Grid.SetRow(_footerHost, 2);
        Children.Add(_footerHost);

        _titleLabel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
                ApplyTitleRowHeight();
        };

        _titleLabel.HandlerChanged += (_, _) => ApplyResources();
        _titleLabel.SizeChanged += (_, _) => ApplyTitleCapOffset();

        // Keep the colour in sync when the user switches light/dark theme at runtime.
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, _) => ApplyTitleTextColor();

        SpineTheme.Track(this, ApplyBarBackgroundColor);
    }

    private ViewModelBase? _page;

    private void WatchFooter(IPageFooterSource? source)
    {
        if (!ReferenceEquals(source, _footerSource))
        {
            if (_footerSource is not null)
                _footerSource.FooterChanged -= OnFooterChanged;

            _footerSource = source;

            if (source is not null)
                source.FooterChanged += OnFooterChanged;
        }

        ApplyFooter();
    }

    private void OnFooterChanged(object? sender, EventArgs e) => ApplyFooter();

    // The footer is a property value of the page, not one of its children, so it is hosted here,
    // below the page, and handed the page's binding context by hand.
    private void ApplyFooter()
    {
        var footer = _footerSource?.Footer;

        _footerHost.Content = footer;
        _footerHost.IsVisible = footer is not null;

        if (footer is not null && _footerSource is BindableObject page)
            _footerHost.SetBinding(BindingContextProperty, new Binding(nameof(BindingContext), source: page));
        else
            _footerHost.RemoveBinding(BindingContextProperty);

        ApplySheetOverhang();
    }

    /// <summary>
    /// How far the bottom of this presenter reaches below the visible edge of the sheet it is in.
    /// Android's sheet keeps its full height and slides down to each detent instead of shrinking,
    /// so the content ends this much higher and the footer is lifted with it — on every frame of a
    /// drag, the way UIKit lays out a resizing sheet on iOS. Without it a page is laid out against
    /// the full-height sheet and its bottom is out of reach at a smaller detent.
    /// </summary>
    internal void SetSheetOverhang(double overhang)
    {
        if (Math.Abs(overhang - _sheetOverhang) < 0.5)
            return;

        _sheetOverhang = overhang;
        ApplySheetOverhang();
    }

    private void ApplySheetOverhang()
    {
        _footerHost.TranslationY = -_sheetOverhang;
        _contentPresenter.Margin = new Thickness(0, 0, 0, _sheetOverhang);
    }

    // The presenter follows the page for the two things that change its own layout: an overlay
    // header floats the title row over the content, and a fixed foreground colours the title.
    private void WatchPage(ViewModelBase? page)
    {
        if (ReferenceEquals(page, _page))
        {
            ApplyPageLayout();
            return;
        }

        if (_page is not null)
            _page.PropertyChanged -= OnPagePropertyChanged;

        _page = page;

        if (page is not null)
            page.PropertyChanged += OnPagePropertyChanged;

        ApplyPageLayout();
    }

    private void OnPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModelBase.HeaderBarMode) or nameof(ViewModelBase.SystemBarInsets)
            or nameof(ViewModelBase.HeaderBarForeground) or nameof(ViewModelBase.IsHeaderBarVisible))
            ApplyPageLayout();
        else if (e.PropertyName is nameof(ViewModelBase.HeaderBarCollapseProgress) or nameof(ViewModelBase.ScrollEdgeProgress))
            ApplyCollapse();
    }

    private void ApplyPageLayout()
    {
        var floats = _page?.HeaderBarMode.Floats() == true;

        // Overlay and collapsing: the content spans both rows and the title row sits over it,
        // pushed down by the status bar the content host no longer pads.
        Grid.SetRowSpan(_contentPresenter, floats ? 2 : 1);
        Grid.SetRow(_contentPresenter, floats ? 0 : 1);
        _titleBar.Margin = floats ? new Thickness(0, _page?.SystemBarInsets.Top ?? 0, 0, 0) : Thickness.Zero;
        _titleBar.InputTransparent = floats;
        // The title was added before the content and would draw under it once they share a row.
        _titleBar.ZIndex = floats ? 2 : 0;
        _barBackground.ZIndex = floats ? 1 : 0;

        ApplyTitleRowHeight();
        ApplyTitleTextColor();
        ApplyBarBackgroundColor();
        ApplyCollapse();
    }

    // A collapsing header shows its title and background as far as the page has scrolled; every
    // other mode shows the title outright and has no background of its own.
    private void ApplyCollapse()
    {
        var collapsing = _page?.HeaderBarMode == HeaderBarMode.CollapseOnScroll;

        var edge = collapsing ? _page!.ScrollEdgeProgress : 0;

        // Read the colour again each time the background starts to show: the presenter may not
        // have been in the page tree when the layout was applied, and a theme binding upstream
        // may have settled after the theme callback ran.
        if (edge > 0 && _barBackground.Opacity == 0)
            ApplyBarBackgroundColor();

        _titleLabel!.Opacity = collapsing ? _page!.HeaderBarCollapseProgress : 1;
        _barBackground.IsVisible = collapsing;
        _barBackground.Opacity = edge;
    }

    private void ApplyBarBackgroundColor()
    {
        if (_page?.HeaderBarMode == HeaderBarMode.CollapseOnScroll)
            _barBackground.Color = PageBackground();
    }

    /// <summary>
    /// The colour the page sits on: the first opaque background from the page view up through the
    /// host page (an app styles its <c>ContentPage</c>), otherwise what the platform paints behind
    /// pages in the current theme, so the collapsed bar reads as the page continuing behind the title.
    /// </summary>
    private Color PageBackground()
    {
        for (Element? element = Content ?? (Element)this; element is not null; element = element.Parent)
        {
            if (element is VisualElement { BackgroundColor: { Alpha: >= 1 } own })
                return own;
        }

        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark
            || (Application.Current?.RequestedTheme != AppTheme.Light
                && Application.Current?.PlatformAppTheme == AppTheme.Dark);

#if IOS || MACCATALYST
        var traits = UIKit.UITraitCollection.FromUserInterfaceStyle(isDark ? UIKit.UIUserInterfaceStyle.Dark : UIKit.UIUserInterfaceStyle.Light);
        return Microsoft.Maui.Platform.ColorExtensions.ToColor(UIKit.UIColor.SystemBackground.GetResolvedColor(traits));
#elif ANDROID
        // The window background is what shows behind every page: the theme's background in the
        // plain host, the bar's surface in the tab host.
        if (Platform.CurrentActivity?.Window?.DecorView.Background is Android.Graphics.Drawables.ColorDrawable drawable)
            return Microsoft.Maui.Platform.ColorExtensions.ToColor(drawable.Color);

        return isDark ? Colors.Black : Colors.White;
#else
        return isDark ? Color.FromArgb("#202020") : Colors.White;
#endif
    }

    // The title row is the header bar's height; under an overlay header it also holds the status
    // bar the title is pushed down by, so the title still centres on the bar's own 44/48 points.
    private void ApplyTitleRowHeight()
    {
        if (!_titleLabel!.IsVisible)
        {
            RowDefinitions[0].Height = new GridLength(0);
            return;
        }

        var overlayInset = _page?.HeaderBarMode.Floats() == true ? _page.SystemBarInsets.Top : 0;
        RowDefinitions[0].Height = new GridLength(HeaderBarConstants.Height + overlayInset);
    }

    private void ApplyResources()
    {
        if (_titleLabel is null)
            return;

        var style = TryFindStyle(Application.Current?.Resources, HeaderBarTitleStyleKey);
        if (style is not null)
        {
            _titleLabel.Style = style;
        }


        // AppThemeBinding inside a keyed style may evaluate to the Light value even in
        // dark mode when the label lives inside a BottomSheetDialog on Android.
        // A direct SetValue always wins over the style and uses the real platform theme.
        ApplyTitleTextColor();

        ApplyTitleCapOffset();
    }

    /// <summary>
    /// Centres the title's capitals on the row rather than its line box, so it lines up with the
    /// action beside it. Applied as a translation because it is a rendering correction, not a
    /// layout one — the label keeps the whole row and only its glyphs move.
    /// </summary>
    private void ApplyTitleCapOffset()
    {
        if (_titleLabel is null)
            return;

        var offset = TitleCapOffset.For(_titleLabel);
        if (!_titleLabel.TranslationY.Equals(offset))
            _titleLabel.TranslationY = offset;
    }


    private void ApplyTitleTextColor()
    {
        if (_titleLabel is null) return;

        if (_page?.HeaderBarForeground is { } foreground)
        {
            _titleLabel.TextColor = foreground;
            return;
        }
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark
            || (Application.Current?.RequestedTheme != AppTheme.Light
                && Application.Current?.PlatformAppTheme == AppTheme.Dark);
        _titleLabel.TextColor = isDark ? Colors.White : Colors.Black;
    }

    private static Style? TryFindStyle(ResourceDictionary? resources, string key)
    {
        if (resources is null)
            return null;

        if (resources.TryGetValue(key, out var v) && v is Style s)
            return s;

        foreach (var merged in resources.MergedDictionaries)
        {
            var found = TryFindStyle(merged, key);
            if (found is not null)
                return found;
        }

        return null;
    }
}

/// <summary>
/// Single-child layout that places the page title between the header bar's actions.
/// </summary>
/// <remarks>
/// The rule it exists for: a centred title stops being centred the moment it grows past the
/// nearest action, so while it fits it gets that mirrored width and sits in the middle of the bar.
/// A title too long to fit is going to be truncated whatever we do — then it may as well spend the
/// room on the side that carries no action, instead of being cut at a mirror of the side that does.
/// A left-aligned title always takes the whole space between the actions.
///
/// This is a layout rather than a margin on the label because the test needs the title's
/// unconstrained width, and the only place a child measures reliably is inside the layout pass.
/// </remarks>
internal sealed class TitleSlotLayout : Layout, ILayoutManager
{
    /// <summary>
    /// Space to keep free on each side: an action slot where the header bar has an action,
    /// plain padding where it has none. Set by <see cref="ActionsToTitleSlotsConverter"/>.
    /// </summary>
    public static readonly BindableProperty SlotsProperty = BindableProperty.Create(
        nameof(Slots),
        typeof(Thickness),
        typeof(TitleSlotLayout),
        new Thickness(PagePresenter.TitleEdgePadding, 0, PagePresenter.TitleEdgePadding, 0),
        propertyChanged: (bindable, _, _) => ((TitleSlotLayout)bindable).InvalidateMeasure());

    private Size _natural;

    public Thickness Slots
    {
        get => (Thickness)GetValue(SlotsProperty);
        set => SetValue(SlotsProperty, value);
    }

    protected override ILayoutManager CreateLayoutManager() => this;

    public Size Measure(double widthConstraint, double heightConstraint)
    {
        _natural = Size.Zero;

        if (Count == 0 || this[0] is not IView child || child.Visibility != Visibility.Visible)
            return Size.Zero;

        // Unconstrained on purpose: the label truncates, so measuring it against the width it is
        // about to get back would only ever confirm that it fits.
        _natural = child.Measure(double.PositiveInfinity, heightConstraint);

        var width = double.IsInfinity(widthConstraint)
            ? _natural.Width + Slots.HorizontalThickness
            : widthConstraint;

        return new Size(width, _natural.Height);
    }

    public Size ArrangeChildren(Rect bounds)
    {
        if (Count == 0 || this[0] is not IView child)
            return bounds.Size;

        var mirrored = Math.Max(Slots.Left, Slots.Right);
        var centredRoom = bounds.Width - (2 * mirrored);
        var isCentred = this[0] is Label { HorizontalTextAlignment: TextAlignment.Center };

        var (left, width) = isCentred && _natural.Width <= centredRoom
            ? (mirrored, centredRoom)
            : (Slots.Left, bounds.Width - Slots.HorizontalThickness);

        child.Arrange(new Rect(bounds.X + left, bounds.Y, Math.Max(0, width), bounds.Height));

        return bounds.Size;
    }
}

internal class TitleAlignmentToTextAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Core.TitleAlignment alignment)
        {
            return alignment == Core.TitleAlignment.Left ? TextAlignment.Start : TextAlignment.Center;
        }
        return TextAlignment.Center;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Turns the header bar's measured action slots into the space the title has to keep free on
/// each side. A side with no action gets plain padding.
/// <see cref="TitleSlotLayout"/> decides how much of it the title uses.
/// </summary>
internal class ActionsToTitleSlotsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => new Thickness(Reserve(values, 0), 0, Reserve(values, 1), 0);

    private static double Reserve(object[] values, int index) =>
        values.Length > index && values[index] is double slot && slot > 0
            ? slot + PagePresenter.TitleEdgePadding
            : PagePresenter.TitleEdgePadding;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
