using System.Globalization;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// Internal <see cref="Grid"/> that hosts a page view and an optional title label inside a
/// <see cref="NavigationRegion"/>. The title label is bound to the page's ViewModel
/// and its visibility is driven by <see cref="Plugin.Maui.Spine.Core.ViewModelBase.IsHeaderBarVisible"/>.
/// </summary>
internal sealed class PagePresenter : Grid
{
    private const string HeaderBarTitleStyleKey = "HeaderBarTitle";

    private Label? _titleLabel;
    private ContentPresenter _contentPresenter;

    /// <summary>
    /// The page view currently hosted in this presenter.
    /// Assigning a new value re-binds the title label and margin bindings to the new page's ViewModel.
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

            // Title alignment comes from the page; action state comes from the region (this presenter)
            var marginBinding = new MultiBinding { Converter = new TitleAlignmentAndActionsToMarginConverter() };
            marginBinding.Bindings.Add(new Binding("BindingContext.TitleAlignment", source: Content));
            marginBinding.Bindings.Add(new Binding("BindingContext.PrimaryPageAction", source: this));
            marginBinding.Bindings.Add(new Binding("BindingContext.SecondaryPageAction", source: this));
            _titleLabel?.SetBinding(Label.MarginProperty, marginBinding);
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
            //Text = "Page title",
        };

        //var tmpborder = new Border
        //{
        //    BackgroundColor = Colors.Red,
        //    Stroke = Colors.LightGray,
        //    StrokeThickness = 0.5,
        //    HorizontalOptions = LayoutOptions.Fill,
        //    VerticalOptions = LayoutOptions.Fill,
        //    Content = _titleLabel
        //};

        //Children.Add(tmpborder);


        Children.Add(_titleLabel);

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

internal class TitleAlignmentAndActionsToMarginConverter : IMultiValueConverter
{
    private const double BaseHorizontalPadding = 8;
    private const double ActionPadding = 44;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3)
            return new Thickness(BaseHorizontalPadding, 0);

        if (values[0] is not Core.TitleAlignment alignment)
            return new Thickness(BaseHorizontalPadding, 0);

        var primary = values[1] as Core.PageAction;
        var secondary = values[2] as Core.PageAction;

        double left = BaseHorizontalPadding;

        if (alignment == Core.TitleAlignment.Left)
        {
            if (primary is { IsVisible: true })
                left += ActionPadding;

            //if (secondary is { IsVisible: true })
            //    right += ActionPadding;
        }

        return new Thickness(left, 0, 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}