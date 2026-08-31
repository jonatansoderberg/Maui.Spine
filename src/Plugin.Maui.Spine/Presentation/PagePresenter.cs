using System.Globalization;
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
    private ContentPresenter _contentPresenter;

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

        Children.Add(_titleBar);

        Grid.SetRow(_contentPresenter, 1);
        Children.Add(_contentPresenter);

        _titleLabel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
                RowDefinitions[0].Height = _titleLabel.IsVisible
                    ? new GridLength(HeaderBarConstants.Height)
                    : new GridLength(0);

        };

        _titleLabel.HandlerChanged += (_, _) => ApplyResources();
        _titleLabel.SizeChanged += (_, _) => ApplyTitleCapOffset();

        // Keep the colour in sync when the user switches light/dark theme at runtime.
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, _) => ApplyTitleTextColor();
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
