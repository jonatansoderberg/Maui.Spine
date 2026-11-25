using System.Globalization;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Microsoft.Maui.Layouts;
using Plugin.Maui.Spine.Svg;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// Internal <see cref="Grid"/> that hosts a page view and an optional title label inside a
/// <see cref="NavigationRegion"/>. The title label is bound to the page's ViewModel
/// and its visibility is driven by <see cref="Plugin.Maui.Spine.Core.ViewModelBase.IsHeaderBarVisible"/>.
/// </summary>
internal sealed partial class PagePresenter : Grid
{
    private const string HeaderBarTitleStyleKey = "HeaderBarTitle";

    /// <summary>Breathing room between the title and whatever sits next to it.</summary>
    internal const double TitleEdgePadding = 8;

    /// <summary>
    /// What the title keeps free on a side of the bar with no action: the page margin with glass buttons and on Windows,
    /// so a left-aligned title lines up with the page's content, otherwise <see cref="TitleEdgePadding"/>.
    /// </summary>
    internal static double EmptySideInset =>
        PageActionView.UseGlassHeaderActions || OperatingSystem.IsWindows() ? HeaderBarConstants.PageMargin : TitleEdgePadding;

    private Label? _titleLabel;
    private readonly Image _titleIcon;
    private readonly TitleSlotLayout _titleBar;

    /// <summary>The row the page's title sits in, part of the page; a lightbox fades it with its other chrome.</summary>
    internal View TitleBar => _titleBar;

    // Behind the title row when content is under the bar and the system draws nothing there: a
    // material that fades in with Opacity as content scrolls under the bar.
    private readonly ContentView _barBackground;
    private ContentPresenter _contentPresenter;
    private readonly ContentView _footerHost;
    private IPageFooterSource? _footerSource;

    // The page's own row under the header bar (SpinePage.HeaderRow), part of the title row.
    private readonly ContentView _headerRowHost;

    // The page's search field in the row below the header bar, at the bottom of the title row.
    private SearchField? _searchField;
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
            _titleLabel?.SetBinding(Label.IsVisibleProperty, new Binding("BindingContext.IsHeaderBarVisible", source: Content));
            _titleLabel?.SetBinding(Label.HorizontalTextAlignmentProperty, new Binding("BindingContext.TitleAlignment",
                source: Content,
                converter: new TitleAlignmentToTextAlignmentConverter()));

            WatchPage(Content?.BindingContext as ViewModelBase);
            ApplyTitleText();
            WatchFooter(Content as IPageFooterSource);

            // The header bar measures its own actions and publishes the result on the region
            // view model, which is this presenter's BindingContext.
            var slotsBinding = new MultiBinding { Converter = new ActionsToTitleSlotsConverter(() => Content?.BindingContext) };
            slotsBinding.Bindings.Add(new Binding("BindingContext.PrimaryActionSlot", source: this));
            slotsBinding.Bindings.Add(new Binding("BindingContext.SecondaryActionSlot", source: this));
            slotsBinding.Bindings.Add(new Binding("BindingContext.HeaderRegionViewModel", source: this));
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
#if IOS || MACCATALYST
            // UIKit sizes the scroll edge effect to the elements over the scroll view; a label as
            // tall as the bar makes it cover the whole bar rather than stop below the text.
            VerticalOptions = LayoutOptions.Fill,
            VerticalTextAlignment = TextAlignment.Center,
#else
            VerticalOptions = LayoutOptions.Center,
#endif
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            IsVisible = false
        };

        // The page's TitleIcon, laid out after the title's text by the title row.
        _titleIcon = new Image { IsVisible = false, InputTransparent = true };
        AutomationProperties.SetIsInAccessibleTree(_titleIcon, false);

        _titleBar = new TitleSlotLayout();
        _titleBar.Add(_titleLabel);
        _titleBar.Add(_titleIcon);

        _barBackground = new ContentView { InputTransparent = true, IsVisible = false, Opacity = 0 };

        Children.Add(_barBackground);
        Children.Add(_titleBar);
        AddStatusBarEdge();

        Grid.SetRow(_contentPresenter, 1);
        Children.Add(_contentPresenter);

        _footerHost = new ContentView { IsVisible = false };
        Grid.SetRow(_footerHost, 2);
        Children.Add(_footerHost);

        // Over the content and the bar's background when they share the row, as the search field is. It spans the
        // title row and the content's, so it is laid out at its own height whatever the title row's height is, and
        // that height then sets the title row's (OnHeaderRowSized).
        _headerRowHost = new ContentView { IsVisible = false, VerticalOptions = LayoutOptions.Start, ZIndex = 3 };
        Grid.SetRowSpan(_headerRowHost, 2);
        Children.Add(_headerRowHost);
        _headerRowHost.SizeChanged += (_, _) => OnHeaderRowSized();

        _titleLabel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
            {
                ApplyTitleRowHeight();
                ApplyTitleIcon();
            }
            else if (e.PropertyName == nameof(Opacity))
                _titleIcon.Opacity = _titleLabel.Opacity;
            else if (e.PropertyName is nameof(Label.FontSize) or nameof(Label.TextColor))
                ApplyTitleIcon();
        };

        _titleLabel.HandlerChanged += (_, _) => ApplyResources();
        _titleLabel.SizeChanged += (_, _) => ApplyTitleCapOffset();

        // Keep the colour in sync when the user switches light/dark theme at runtime.
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, _) => ApplyTitleTextColor();

        SpineTheme.Track(this, () =>
        {
            ApplyBarBackgroundColor();
            RefreshSoftEdge();
        });
    }

    private ViewModelBase? _page;

    private void WatchFooter(IPageFooterSource? source)
    {
        if (!ReferenceEquals(source, _footerSource))
        {
            if (_footerSource is not null)
            {
                _footerSource.FooterChanged -= OnFooterChanged;
                _footerSource.HeaderRowChanged -= OnHeaderRowChanged;
            }

            _footerSource = source;

            if (source is not null)
            {
                source.FooterChanged += OnFooterChanged;
                source.HeaderRowChanged += OnHeaderRowChanged;
            }
        }

        ApplyFooter();
        ApplyHeaderRow();
    }

    private void OnHeaderRowChanged(object? sender, EventArgs e) => ApplyHeaderRow();

    // The header row is a property value of the page, as the footer is: hosted here, in the title row,
    // and handed the page's binding context by hand.
    private void ApplyHeaderRow()
    {
        var row = _footerSource?.HeaderRow;

        _headerRowHost.Content = row;
        _headerRowHost.IsVisible = row is not null;

        if (row is not null && _footerSource is BindableObject page)
            _headerRowHost.SetBinding(BindingContextProperty, new Binding(nameof(BindingContext), source: page));
        else
            _headerRowHost.RemoveBinding(BindingContextProperty);

        // A new page's row as tall as the last one raises no SizeChanged: read its height now as well.
        OnHeaderRowSized();
        ApplyHeaderRowFrame();
    }

    // The row's height, once it is laid out, moves the title row's bottom, the page's top inset and so the
    // scroll inset.
    private void OnHeaderRowSized()
    {
        if (_page is not { } page)
            return;

        var height = _headerRowHost is { IsVisible: true, Content: not null } && page.IsHeaderBarVisible
            ? Math.Ceiling(_headerRowHost.Height)
            : 0;
        if (height < 0 || Math.Abs(height - page.HeaderRowRestHeight) < 0.5)
            return;

        page.HeaderRowRestHeight = height;
        page.SafeAreaInsets = NavigationRegion.SafeAreaInsetsFor(page, page.SystemBarInsets);
        ApplyTitleRowHeight();
    }

    // Under the header bar and a search row; while a search takes the bar's place the row goes up with the
    // bar and fades, and the content takes its place.
    private void ApplyHeaderRowFrame()
    {
        if (!_headerRowHost.IsVisible || _page is not { } page)
            return;

        var progress = page.SearchProgress;
        var sides = page.SafeAreaInsets;
        var overlayInset = page.HeaderBarFloats ? page.SystemBarInsets.Top : 0;
        var top = overlayInset + HeaderBarConstants.BarHeight * (1 - progress) + page.SearchRowHeight;

        _headerRowHost.Opacity = 1 - progress;
        _headerRowHost.InputTransparent = progress > 0.5;
        _headerRowHost.Margin = new Thickness(sides.Left, top, sides.Right, 0);
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
        if (e.PropertyName is nameof(ViewModelBase.HeaderBarMode) or nameof(ViewModelBase.SystemBarInsets) or nameof(ViewModelBase.SafeAreaInsets)
            or nameof(ViewModelBase.HeaderBarForeground) or nameof(ViewModelBase.IsHeaderBarVisible)
            or nameof(ViewModelBase.LargeTitle) or nameof(ViewModelBase.EffectiveHeaderBarBackground)
            or nameof(ViewModelBase.SearchLayout))
            ApplyPageLayout();
        else if (e.PropertyName is nameof(ViewModelBase.HeaderBarCollapseProgress) or nameof(ViewModelBase.ScrollEdgeProgress))
            ApplyCollapse();
        else if (e.PropertyName is nameof(ViewModelBase.HeaderBarScrollSource))
            UpdateSystemScrollEdge();
        else if (e.PropertyName is nameof(ViewModelBase.FormattedTitle))
            ApplyTitleText();
        else if (e.PropertyName is nameof(ViewModelBase.TitleIcon) or nameof(ViewModelBase.TitleIconColor) or nameof(ViewModelBase.TitleIconLineWidthScale))
            ApplyTitleIcon();
        else if (e.PropertyName is nameof(ViewModelBase.SearchProgress))
        {
            ApplySearchRowFrame();
            ApplyHeaderRowFrame();
            ApplyTitleBarMargin();
            ApplyTitleRowHeight();
            ApplyCollapse();
            RefreshSoftEdge();
        }
    }

    // The page's title: its formatted title when it has one, otherwise the plain one, which follows the view model.
    // A label shows one or the other, and setting its text would take a formatted title away again, so the binding
    // goes while a formatted title is shown.
    // The page's TitleIcon after the title, in the page's colour or the title's, shown while the title is. Spine's
    // icons keep room around their glyph (a plus fills about 40 % of its box), so the box is half again the title's
    // font size: the glyph then stands about as tall as the title's capitals.
    private void ApplyTitleIcon()
    {
        if (_titleLabel is null)
            return;

        var svg = _page?.TitleIcon;
        _titleIcon.IsVisible = _titleLabel.IsVisible && !string.IsNullOrWhiteSpace(svg);
        if (!_titleIcon.IsVisible)
            return;

        var size = Math.Round(1.5 * (_titleLabel.FontSize > 0 ? _titleLabel.FontSize : 17));
        _titleIcon.WidthRequest = _titleIcon.HeightRequest = size;
        var colour = _page?.TitleIconColor ?? _titleLabel.TextColor ?? Colors.Black;
        SvgImageSource.SetLightTintColor(_titleIcon, colour);
        SvgImageSource.SetDarkTintColor(_titleIcon, colour);
        SvgImageSource.SetLineWidthScale(_titleIcon, _page?.TitleIconLineWidthScale ?? 1);
        if (SvgImageSource.GetSvg(_titleIcon) != svg)
            SvgImageSource.SetSvg(_titleIcon, svg);
        _titleIcon.Opacity = _titleLabel.Opacity;
    }

    private void ApplyTitleText()
    {
        ApplyTitleIcon();
        if (_titleLabel is null)
            return;

        if (_page?.FormattedTitle is { } formatted)
        {
            _titleLabel.RemoveBinding(Label.TextProperty);
            _titleLabel.FormattedText = formatted;
            _titleLabel.SetBinding(SemanticProperties.DescriptionProperty, new Binding("BindingContext.Title", source: Content));
            return;
        }

        var wasFormatted = _titleLabel.FormattedText is not null;
        if (wasFormatted)
            _titleLabel.FormattedText = null;
        _titleLabel.RemoveBinding(SemanticProperties.DescriptionProperty);
        _titleLabel.SetBinding(Label.TextProperty, new Binding("BindingContext.Title", source: Content));

        // The presenters are reused from page to page, and a platform label that has shown formatted text keeps
        // that text's attributes for the plain text that follows: the next page's title came out in the last span's
        // font. The label's own font, colour and spacing have not changed, so MAUI would not set them again.
        if (wasFormatted && _titleLabel.Handler is { } handler)
        {
            handler.UpdateValue(nameof(ITextStyle.Font));
            handler.UpdateValue(nameof(ITextStyle.TextColor));
            handler.UpdateValue(nameof(ITextStyle.CharacterSpacing));
            handler.UpdateValue(nameof(ILabel.Text));
        }
    }

    private void ApplyPageLayout()
    {
        var floats = _page?.HeaderBarFloats == true;

        // Overlay and large title: the content spans both rows and the title row sits over it,
        // pushed down by the status bar the content host no longer pads.
        Grid.SetRowSpan(_contentPresenter, floats ? 2 : 1);
        Grid.SetRow(_contentPresenter, floats ? 0 : 1);
        ApplyTitleBarMargin();
        _titleBar.InputTransparent = floats;
        // The title was added before the content and would draw under it once they share a row.
        _titleBar.ZIndex = floats ? 2 : 0;
        _barBackground.ZIndex = floats ? 1 : 0;

        ApplySearchRow();
        ApplyHeaderRowFrame();
        ApplyTitleRowHeight();
        ApplyTitleTextColor();
        ApplyBarBackgroundColor();
        ApplyCollapse();
        ApplyStatusBarEdge();
        UpdateSystemScrollEdge();
    }

    // A page that draws under a side of the safe area still keeps its title inside it. While a search
    // takes the header bar's place the title goes up with the bar, keeping its height, and fades.
    private void ApplyTitleBarMargin()
    {
        var floats = _page?.HeaderBarFloats == true;
        var sides = _page?.SafeAreaInsets ?? Thickness.Zero;
        var top = floats ? _page?.SystemBarInsets.Top ?? 0 : 0;
        var givenWay = HeaderBarConstants.BarHeight * (_page?.SearchProgress ?? 0);
        _titleBar.Margin = new Thickness(sides.Left, top - givenWay, sides.Right, 0);
    }

    /// <summary>Installs or removes UIKit's scroll edge effect for the page; iOS and Mac Catalyst 26 only.</summary>
    partial void UpdateSystemScrollEdge();

    /// <summary>Brings UIKit's stretched soft edge (iOS 27) in line with the theme; Apple platforms only.</summary>
    partial void RefreshSoftEdge();

    /// <summary>Adds the element a <see cref="HeaderBarBackground.SoftStatusBar"/> effect is sized to; Apple platforms only.</summary>
    partial void AddStatusBarEdge();

    /// <summary>Shows that element, as tall as the status bar, while the page asks for it.</summary>
    partial void ApplyStatusBarEdge();

    /// <summary>
    /// The background the presenter paints behind the title row. A bar with a row of its own has
    /// nothing under it and needs none: the page's own background shows.
    /// </summary>
    private HeaderBarBackground BarBackground =>
        _page is { HeaderBarFloats: true } page ? page.EffectiveHeaderBarBackground : HeaderBarBackground.Transparent;

    /// <summary>Whether UIKit draws this page's background: the system scroll edge effect.</summary>
    private bool UsesSystemScrollEdge =>
        BarBackground.IsScrollEdge() && Services.NavigableMeta.HasSystemScrollEdge;

    // A large title's header shows its title as far as the page has scrolled, and a solid
    // background behind a floating header fades in the same way; otherwise the title is shown
    // outright and the bar has no background of its own.
    private void ApplyCollapse()
    {
        var solid = HasSolidBackground();
        var edge = solid ? _page!.ScrollEdgeProgress : 0;

        // Read the colour again each time the background starts to show: the presenter may not
        // have been in the page tree when the layout was applied, and a theme binding upstream
        // may have settled after the theme callback ran.
        if (edge > 0 && _barBackground.Opacity == 0)
            ApplyBarBackgroundColor();

        _titleLabel!.Opacity = (_page?.LargeTitle == true ? _page.HeaderBarCollapseProgress : 1) * Math.Max(1 - (_page?.SearchProgress ?? 0), SearchTitleOpacity);
        _barBackground.IsVisible = solid;
        _barBackground.Opacity = edge;
        ApplySystemScrollEdgeRest();
    }

    /// <summary>
    /// What is left of the title while a search has the header bar's place: next to nothing, but UIKit
    /// sizes its scroll edge effect to the labels in the title row and passes over a label that is
    /// fully transparent, which left the rows sharp under the field and the status bar.
    /// </summary>
    private const double SearchTitleOpacity = 0.02;

    /// <summary>Hides UIKit's scroll edge effect while the page is at rest; Apple platforms only.</summary>
    partial void ApplySystemScrollEdgeRest();

    // Solid, or the band that stands in for the scroll edge effect where the system has none.
    private bool HasSolidBackground() =>
        BarBackground is HeaderBarBackground.Solid || (BarBackground.IsScrollEdge() && !UsesSystemScrollEdge);

    /// <summary>How far the soft stand-in band fades out below the bar.</summary>
    private const double ScrollEdgeBandFade = 24;

    /// <summary>How much longer a blurred soft band fades than a tinted one: it starts inside the bar and ends further below it.</summary>
    private const double SoftBlurFadeFactor = 3.5;

    /// <summary>How opaque the soft stand-in band is behind the bar: rows stay faintly visible through it.</summary>
    private const float ScrollEdgeBandAlpha = 0.9f;

    /// <summary>How far the status-bar stand-in band fades out below the status bar.</summary>
    private const double StatusBarBandFade = 16;

    /// <summary>
    /// Black over the soft blur, as much as iOS 26's soft edge darkened what it blurred: about a fifth
    /// in light mode and two fifths in dark mode (the same as <c>SoftEdgeStretch</c> puts back on iOS 27).
    /// </summary>
    private const float SoftBlurDimLight = 0.22f, SoftBlurDimDark = 0.4f;

    /// <summary>The page's colour over the hard blur: frosted, with the rows' colours still coming through, as UIKit's hard band.</summary>
    private const float ScrollEdgeHardBlurTint = 0.4f;

    /// <summary>How opaque the hard stand-in band is: rows only just show through, as through UIKit's frosted band.</summary>
    private const float ScrollEdgeHardBandAlpha = 0.96f;

    private void ApplyBarBackgroundColor()
    {
        if (!HasSolidBackground())
            return;

        var colour = PageBackground();
        Material.SetTintOpacity(_barBackground, 1);

        if (BarBackground is HeaderBarBackground.Solid)
        {
            Material.SetKind(_barBackground, MaterialKind.None);
            Material.SetSystemBlur(_barBackground, SystemBlur.None);
            Material.SetTint(_barBackground, colour);
            Material.SetFade(_barBackground, 0);
            Material.SetEdgeLine(_barBackground, null);
            _barBackground.HeightRequest = -1;
            _barBackground.VerticalOptions = LayoutOptions.Fill;
            Grid.SetRowSpan(_barBackground, 1);
            return;
        }

        // The scroll edge where the system does not draw it: a blur of the rows behind the bar where
        // the platform has one, the page's colour where it does not, over the same band.
        var blurs = Material.Resolve(MaterialKind.Blur) == MaterialKind.Blur;
        var bar = RowDefinitions[0].Height.Value;
        double height;
        Material.SetKind(_barBackground, MaterialKind.Blur);

        if (BarBackground is HeaderBarBackground.HardEdge)
        {
            // The hard style: nearly opaque, ending at the bar's bottom edge in a hairline.
            var hairline = HairlineThickness();
            height = bar + hairline;
            Material.SetSystemBlur(_barBackground, SystemBlur.Regular);
            Material.SetTint(_barBackground, colour.WithAlpha(blurs ? ScrollEdgeHardBlurTint : ScrollEdgeHardBandAlpha));
            Material.SetFade(_barBackground, 0);
            Material.SetEdgeLine(_barBackground, HairlineColour());
        }
        else
        {
            // The soft style: see-through behind the bar (or only the status bar), fading out below
            // it so rows dissolve into the bar instead of meeting an edge.
            var statusBarOnly = BarBackground is HeaderBarBackground.SoftStatusBar;
            var fade = statusBarOnly ? StatusBarBandFade : ScrollEdgeBandFade;
            height = (statusBarOnly ? _page?.SystemBarInsets.Top ?? 0 : bar) + fade;

            // A blur fades as UIKit's progressive one does: it starts to let go well inside the bar,
            // not at its edge, and reaches further below it.
            if (blurs)
            {
                height += fade;
                fade *= SoftBlurFadeFactor;
            }
            Material.SetSystemBlur(_barBackground, SystemBlur.Thin);
            Material.SetTint(_barBackground, blurs
                ? Colors.Black.WithAlpha(IsDarkTheme() ? SoftBlurDimDark : SoftBlurDimLight)
                : colour.WithAlpha(ScrollEdgeBandAlpha));
            Material.SetFade(_barBackground, fade);
            Material.SetEdgeLine(_barBackground, null);
        }

        _barBackground.HeightRequest = height;
        _barBackground.VerticalOptions = LayoutOptions.Start;
        Grid.SetRowSpan(_barBackground, 2);
    }

    /// <summary>One device pixel, the width of a system separator.</summary>
    private static double HairlineThickness()
    {
        var density = DeviceDisplay.Current.MainDisplayInfo.Density;
        return density > 0 ? 1 / density : 1;
    }

    /// <summary>A separator's colour in the current theme, as UIKit's hairline under a bar.</summary>
    private static Color HairlineColour() =>
        IsDarkTheme() ? Color.FromRgba(84, 84, 88, 166) : Color.FromRgba(60, 60, 67, 74);

    private static bool IsDarkTheme() =>
        Application.Current?.RequestedTheme == AppTheme.Dark
        || (Application.Current?.RequestedTheme != AppTheme.Light
            && Application.Current?.PlatformAppTheme == AppTheme.Dark);

    /// <summary>
    /// The first opaque background from the page view up through the host page, or <see langword="null"/> when
    /// the page and everything it sits on are see-through, as in a translucent window.
    /// </summary>
    internal Color? OwnBackground()
    {
        for (Element? element = Content ?? (Element)this; element is not null; element = element.Parent)
        {
            if (element is VisualElement { BackgroundColor: { Alpha: >= 1 } own })
                return own;
        }

        return null;
    }

    /// <summary>
    /// The colour the page sits on: the first opaque background from the page view up through the
    /// host page (an app styles its <c>ContentPage</c>), otherwise what the platform paints behind
    /// pages in the current theme, so the collapsed bar reads as the page continuing behind the title.
    /// </summary>
    internal Color PageBackground()
    {
        if (OwnBackground() is { } own)
            return own;

        var isDark = IsDarkTheme();

#if IOS || MACCATALYST
        var traits = UIKit.UITraitCollection.FromUserInterfaceStyle(isDark ? UIKit.UIUserInterfaceStyle.Dark : UIKit.UIUserInterfaceStyle.Light);
        return Microsoft.Maui.Platform.ColorExtensions.ToColor(UIKit.UIColor.SystemBackground.GetResolvedColor(traits))
            ?? (isDark ? Colors.Black : Colors.White);
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

    /// <summary>
    /// Shows the page's search field at the bottom of the title row when it goes in the row below
    /// the header bar, inside the same side margin as the page's content.
    /// </summary>
    private void ApplySearchRow()
    {
        var shown = _page is { SearchLayout: SearchLayout.Row };

        if (!shown)
        {
            if (_searchField is not null)
            {
                _searchField.IsVisible = false;
                _searchField.Search = null;
            }
            return;
        }

        if (_searchField is null)
        {
            _searchField = new SearchField { VerticalOptions = LayoutOptions.Start, OutlastsFocus = SearchField.HidesHeaderBar };
            Children.Add(_searchField);
        }

        _searchField.Search = _page!.Search;
        _searchField.IsVisible = true;
        // Over the content and the bar's background when they share the row.
        _searchField.ZIndex = 3;
        ApplySearchRowFrame();
    }

    // The field's frame and its own state as far as a search has taken the header bar's place. At
    // rest it is in the row below the bar; while searching it is centred on the bar's item row, its
    // button where the bar's trailing action is. A field shown only for the search stays in the
    // bar's row and comes in from a little below it as the bar goes. On Android the field runs to
    // the sides while searching, as Material's search view header.
    private void ApplySearchRowFrame()
    {
        if (_searchField is not { IsVisible: true } || _page is not { } page)
            return;

        var progress = page.SearchProgress;
        var sides = page.SafeAreaInsets;
        var inset = SearchRowInset * (SearchField.FullWidthWhileSearching ? 1 - progress : 1);
        var overlayInset = page.HeaderBarFloats ? page.SystemBarInsets.Top : 0;

        var activeHeight = SearchField.ActiveRowHeight;
        var activeTop = (HeaderBarConstants.Height - activeHeight) / 2;
        double top, height;

        if (page.SearchInPlace)
        {
            top = activeTop + SearchField.InPlaceRise * (1 - progress);
            height = activeHeight;
        }
        else
        {
            top = HeaderBarConstants.BarHeight + (activeTop - HeaderBarConstants.BarHeight) * progress;
            height = HeaderBarConstants.SearchRowHeight + (activeHeight - HeaderBarConstants.SearchRowHeight) * progress;
        }

        _searchField.SideMarginWhileSearching = (BindingContext as NavigationRegionViewModel)?.Presentation is NavigationPresentation.Sheet
            ? HeaderBarConstants.SheetSideMargin
            : HeaderBarConstants.RegionSideMargin;
        _searchField.SearchProgress = progress;
        _searchField.Opacity = page.SearchInPlace ? progress : 1;
        _searchField.HeightRequest = height;
        _searchField.Margin = new Thickness(sides.Left + inset, overlayInset + top, sides.Right + inset, 0);
    }

    /// <summary>
    /// How far in from the page's sides the search row starts: the page margin, less the room
    /// UIKit's search bar keeps around its field.
    /// </summary>
    private static double SearchRowInset =>
#if IOS || MACCATALYST
        HeaderBarConstants.PageMargin - 8;
#else
        HeaderBarConstants.PageMargin;
#endif

    // The title row is the header bar's height; under an overlay header it also holds the status
    // bar the title is pushed down by. The title centres on the item row (Height) at the top of the
    // bar, not on the whole bar, which is taller on iOS 26.
    private void ApplyTitleRowHeight()
    {
        if (!_titleLabel!.IsVisible)
        {
            RowDefinitions[0].Height = new GridLength(0);
            return;
        }

        var overlayInset = _page?.HeaderBarFloats == true ? _page.SystemBarInsets.Top : 0;
        var search = _page?.SearchRowHeight ?? 0;
        var headerRow = _page?.HeaderRowHeight ?? 0;
        RowDefinitions[0].Height = new GridLength(HeaderBarConstants.BarHeight + overlayInset + search + headerRow);

        // Padding keeps the label as tall as the bar and a search row below it, so UIKit's edge
        // effect covers all of it. UIKit's own hard band under an inline title in a pushed or root
        // navigation stack stops at the items, though (a large title's, a sheet's and one over a
        // search field reach the bar's bottom), so there the label ends with the item row. The
        // search row's height at rest, also while a search takes it away: a padding is not
        // animated, and the title would drop as the row started to shrink.
        var row = _page is { SearchLayout: SearchLayout.Row, SearchInPlace: false };
        var ownRow = _page?.HeaderRowRestHeight ?? 0;
        var below = new Thickness(0, 0, 0, HeaderBarConstants.BarHeight - HeaderBarConstants.Height + (row ? HeaderBarConstants.SearchRowHeight : 0) + ownRow);
        var itemsOnly = BarBackground is HeaderBarBackground.HardEdge
            && _page?.LargeTitle != true
            && !row
            && ownRow == 0
            && (BindingContext as NavigationRegionViewModel)?.Presentation is not NavigationPresentation.Sheet;
        _titleLabel.Padding = itemsOnly ? Thickness.Zero : below;
        _titleLabel.Margin = itemsOnly ? below : Thickness.Zero;

        ApplyBarBackgroundColor();
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
        new Thickness(PagePresenter.EmptySideInset, 0, PagePresenter.EmptySideInset, 0),
        propertyChanged: (bindable, _, _) => ((TitleSlotLayout)bindable).InvalidateMeasure());

    private Size _natural;

    public Thickness Slots
    {
        get => (Thickness)GetValue(SlotsProperty);
        set => SetValue(SlotsProperty, value);
    }

    protected override ILayoutManager CreateLayoutManager() => this;

    /// <summary>The room between the title's text and its icon's box; the room around the icon's glyph adds to it.</summary>
    internal const double IconSpacing = 0;

    private Size _icon;

    Size ILayoutManager.Measure(double widthConstraint, double heightConstraint)
    {
        _natural = Size.Zero;
        _icon = Size.Zero;

        if (Count == 0 || this[0] is not IView child || child.Visibility != Visibility.Visible)
            return Size.Zero;

        // Unconstrained on purpose: the label truncates, so measuring it against the width it is
        // about to get back would only ever confirm that it fits.
        _natural = child.Measure(double.PositiveInfinity, heightConstraint);
        if (Icon is { } icon)
            _icon = icon.Measure(double.PositiveInfinity, double.PositiveInfinity);

        var width = double.IsInfinity(widthConstraint)
            ? _natural.Width + IconRoom + Slots.HorizontalThickness
            : widthConstraint;

        return new Size(width, _natural.Height);
    }

    // The page's title icon, when it has one to show (PagePresenter's second child).
    private IView? Icon => Count > 1 && this[1] is IView { Visibility: Visibility.Visible } icon ? icon : null;

    private double IconRoom => Icon is null ? 0 : IconSpacing + _icon.Width;

    Size ILayoutManager.ArrangeChildren(Rect bounds)
    {
        if (Count == 0 || this[0] is not IView child)
            return bounds.Size;

        var mirrored = Math.Max(Slots.Left, Slots.Right);
        var centredRoom = bounds.Width - (2 * mirrored);
        var isCentred = this[0] is Label { HorizontalTextAlignment: TextAlignment.Center };
        var iconRoom = IconRoom;

        // A centred title stays put, as a navigation bar's does: it keeps its own width in the
        // middle of the bar, so an action that grows or shrinks beside it (even inside an
        // animation) does not change its frame, and it is truncated rather than pushed aside
        // when it no longer fits. With an icon, the title and the icon are centred together,
        // and the title gives way first.
        // Before the title is measured its natural width is 0: a frame of that width would be a
        // point that the title then grows out of.
        var (left, width) = isCentred
            ? _natural.Width > 0
                ? CentredFrame(bounds.Width, Math.Min(_natural.Width + iconRoom, centredRoom))
                : (mirrored, centredRoom)
            : (Slots.Left, bounds.Width - Slots.HorizontalThickness);
        var textWidth = iconRoom > 0 && _natural.Width > 0 ? Math.Min(_natural.Width, width - iconRoom) : width - iconRoom;

        child.Arrange(new Rect(bounds.X + left, bounds.Y, Math.Max(0, textWidth), bounds.Height));

        // Centred on the title's text: the label's own row, above the padding and margin that keep it as tall as the bar.
        if (Icon is { } icon && this[0] is Label label)
        {
            var textTop = label.Margin.Top + label.Padding.Top;
            var textHeight = bounds.Height - label.Margin.VerticalThickness - label.Padding.VerticalThickness;
            icon.Arrange(new Rect(
                bounds.X + left + Math.Max(0, textWidth) + IconSpacing,
                bounds.Y + textTop + ((textHeight - _icon.Height) / 2),
                _icon.Width,
                _icon.Height));
        }

        return bounds.Size;
    }

    static (double Left, double Width) CentredFrame(double barWidth, double width) =>
        ((barWidth - width) / 2, width);
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
internal class ActionsToTitleSlotsConverter(Func<object?>? page = null) : IMultiValueConverter
{
    // The slots are those of the page the header bar shows (the third value). During a transition that is the page
    // arriving, and the page leaving keeps the slots it had: its title would otherwise step aside for the other
    // page's actions just as it starts to slide away.
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length > 2 && values[2] is ViewModelBase header && page?.Invoke() is ViewModelBase own && !ReferenceEquals(header, own)
            ? Binding.DoNothing
            : new Thickness(Reserve(values, 0), 0, Reserve(values, 1), 0);

    // With glass buttons, and on Windows, a side without an action keeps the page margin, so a left-aligned title
    // starts where the page's content and the buttons' edges do, as a UINavigationBar's does and as a WinUI title bar's
    // title, 16 points in, does.
    private static double Reserve(object[] values, int index) =>
        values.Length > index && values[index] is double slot && slot > 0
            ? slot + HeaderBarConstants.TitleActionGap
            : PagePresenter.EmptySideInset;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
