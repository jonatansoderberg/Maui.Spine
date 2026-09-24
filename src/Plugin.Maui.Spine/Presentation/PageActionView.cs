using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Svg;

namespace Plugin.Maui.Spine.Presentation;

internal sealed class PageActionView : ContentView
{
    public static readonly BindableProperty ActionProperty = BindableProperty.Create(
        nameof(Action),
        typeof(PageAction),
        typeof(PageActionView),
        default(PageAction),
        propertyChanged: OnActionChanged);

    /// <summary>A fixed colour for the text and the icon, or <see langword="null"/> to follow the theme.</summary>
    public static readonly BindableProperty ForegroundProperty = BindableProperty.Create(
        nameof(Foreground), typeof(Color), typeof(PageActionView), null,
        propertyChanged: static (b, _, _) => ((PageActionView)b).ApplyForeground());

    public Color? Foreground
    {
        get => (Color?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly BindableProperty HideDisabledProperty = BindableProperty.Create(
        nameof(HideDisabled),
        typeof(bool),
        typeof(PageActionView),
        false,
        propertyChanged: OnHideDisabledChanged);

    readonly Button _textButton;
    readonly ImageButton _imageButton;
    Action? _applyTextButtonColor;
    readonly Border _badge;
    readonly Label _badgeLabel;
    readonly bool _glass;
    string? _currentSvg;

    public PageAction? Action
    {
        get => (PageAction?)GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public bool HideDisabled
    {
        get => (bool)GetValue(HideDisabledProperty);
        set => SetValue(HideDisabledProperty, value);
    }

    public PageActionView()
    {
        _textButton = new Button
        {
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Margin = new Thickness(12, 0, 12, 0)
        };

        ButtonExtensions.SetCompact(_textButton, true);

        void ApplyTextButtonColor()
        {
            if (Foreground is { } foreground)
            {
                _textButton.TextColor = foreground;
                return;
            }

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark
                || (Application.Current?.RequestedTheme != AppTheme.Light
                    && Application.Current?.PlatformAppTheme == AppTheme.Dark);
            _textButton.TextColor = isDark
                ? GetResourceColor("PrimaryDark", Color.FromArgb("#ac99ea"))
                : GetResourceColor("Primary", Color.FromArgb("#512BD4"));
        }

        _applyTextButtonColor = ApplyTextButtonColor;

        ApplyTextButtonColor();

        // Re-apply in HandlerChanged because the implicit Button style is applied when the
        // view enters the visual tree and can race with the initial assignment.
        // Direct SetValue (not a binding) definitively wins over any style setter.
        _textButton.HandlerChanged += (_, _) =>
        {
            if (_textButton.Handler is null) return;
            ApplyTextButtonColor();
        };

        // Keep the colour in sync when the user switches light/dark theme at runtime.
        if (Application.Current is { } currentApp)
            currentApp.RequestedThemeChanged += (_, _) => ApplyTextButtonColor();

        _imageButton = new ImageButton
        {
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            BorderColor = Colors.Transparent,
            CornerRadius = DeviceInfo.Platform == DevicePlatform.Android ? 24 : 0,
        };
        _imageButton.ApplyCommonVisualStates(HideDisabled);

        _imageButton.SetBinding(VisualElement.WidthRequestProperty, new Binding(nameof(WidthRequest), source: this));
        _imageButton.SetBinding(VisualElement.HeightRequestProperty, new Binding(nameof(HeightRequest), source: this));
        _imageButton.SetBinding(ImageButton.PaddingProperty, new Binding(nameof(Padding), source: this));

        _glass = UseGlassHeaderActions;
        if (_glass)
        {
            // Compact zeroed the padding; the capsule needs some room around the text, and it
            // sits centred in the 44-point row rather than filling it.
            _textButton.Padding = new Thickness(14, 8);
            _textButton.VerticalOptions = LayoutOptions.Center;
            Glass.SetStyle(_textButton, GlassStyle.Regular);

            // The glass makes the slot visible, so the icon becomes a circle centred in it
            // rather than a pill hugging the screen edge.
            _imageButton.HorizontalOptions = LayoutOptions.Center;
            _imageButton.SetBinding(VisualElement.WidthRequestProperty, new Binding(nameof(HeightRequest), source: this));
            Glass.SetStyle(_imageButton, GlassStyle.Regular);
        }

        _badgeLabel = new Label
        {
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        // The same red the native tab badges use, so a count reads the same everywhere.
        _badge = new Border
        {
            Content = _badgeLabel,
            BackgroundColor = Color.FromArgb("#FF3B30"),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(4, 0),
            MinimumWidthRequest = 16,
            HeightRequest = 16,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            // On the glass capsule the pill sits inside the slot's corner; on a plain text button
            // it sits past the text, which ends 12 points short of the edge.
            Margin = _glass ? new Thickness(0, 2, 6, 0) : new Thickness(0, 0, 0, 0),
            InputTransparent = true,
            IsVisible = false,
        };

        Content = new Grid
        {
            Children = { _textButton, _imageButton, _badge }
        };

        ApplyAction();
    }

    // The option is read here rather than passed down: HeaderBarView and PageActionView are built by
    // pages, not by DI, and the attached property is a no-op off Apple anyway.
    static bool UseGlassHeaderActions =>
        OperatingSystem.IsIOS()
        && IPlatformApplication.Current?.Services.GetService<SpineOptions>()?.Apple.GlassHeaderActions == true;

    /// <summary>Raised when the assigned action's <see cref="PageAction.IsVisible"/> changes in place.</summary>
    public event Action? VisibilityChanged;

    static void OnActionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (PageActionView)bindable;
        var oldSvg = (oldValue as PageAction)?.Svg;
        var newSvg = (newValue as PageAction)?.Svg;

        if (oldValue is PageAction oldAction)
            oldAction.PropertyChanged -= view.OnActionPropertyChanged;
        if (newValue is PageAction newAction)
            newAction.PropertyChanged += view.OnActionPropertyChanged;

        var sameSvg = !string.IsNullOrWhiteSpace(oldSvg)
                   && !string.IsNullOrWhiteSpace(newSvg)
                   && oldSvg == newSvg;

        if (sameSvg)
            view.ApplyAction();
        else
            _ = view.ApplyActionAnimatedAsync();
    }

 

    void ApplyForeground()
    {
        _applyTextButtonColor?.Invoke();

        if (_imageButton.Behaviors.OfType<SvgImageSourceBehavior>().FirstOrDefault() is { } svg)
        {
            svg.TintColor = Foreground;
            svg.UpdateImage();
        }
    }

    void OnActionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PageAction.Svg):
                _ = ApplyActionAnimatedAsync();
                break;
            case nameof(PageAction.IsVisible):
                ApplyAction();
                VisibilityChanged?.Invoke();
                break;
            default:
                ApplyAction();
                break;
        }
    }

    static void OnHideDisabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (PageActionView)bindable;
        view._imageButton.ApplyCommonVisualStates(view.HideDisabled);
    }

    static Color GetResourceColor(string key, Color fallback) =>
        Application.Current?.Resources?.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;

    async Task ApplyActionAnimatedAsync()
    {
        var wasVisible = _textButton.IsVisible || _imageButton.IsVisible;

        if (wasVisible)
            await this.FadeToAsync(0, 60);

        ApplyAction();

        // On Android the new ImageSource is decoded asynchronously after being set;
        // a short delay lets the platform render the new bitmap before revealing it.
        await Task.Delay(50);

        if (_textButton.IsVisible || _imageButton.IsVisible)
            await this.FadeToAsync(1, 60);
        else
            Opacity = 1;
    }

    void ApplyAction()
    {
        var action = Action;
        if (action is null || !action.IsVisible)
        {
            _textButton.IsVisible = false;
            _imageButton.IsVisible = false;
            _badge.IsVisible = false;
            return;
        }

        var hasSvg = !string.IsNullOrWhiteSpace(action.Svg);

        _imageButton.IsVisible = hasSvg;
        _textButton.IsVisible = !hasSvg;
        _imageButton.IsEnabled = action.IsEnabled;
        _textButton.IsEnabled = action.IsEnabled;
        // The app's Disabled visual state may not reach a button whose colour is set directly.
        _imageButton.Opacity = action.IsEnabled ? 1 : 0.4;
        _textButton.Opacity = action.IsEnabled ? 1 : 0.4;

        _badgeLabel.Text = action.Badge ?? string.Empty;
        SemanticProperties.SetDescription(_imageButton, action.Description);
        SemanticProperties.SetDescription(_textButton, action.Description);

        MenuButton.SetItems(_imageButton, action.Menu);
        MenuButton.SetItems(_textButton, action.Menu);
        MenuButton.SetShowsSelection(_textButton, action.MenuShowsSelection);
        _badge.IsVisible = !string.IsNullOrEmpty(action.Badge);

        if (hasSvg)
        {
            if (action.Svg != _currentSvg)
            {
                _imageButton.Behaviors.Clear();
                var behavior = new SvgImageSourceBehavior
                {
                    Svg = action.Svg!,
                    LightTintColor = Colors.Black,
                    DarkTintColor = Colors.White,
                    TintColor = Foreground,
                };
                // A 24-point glyph in the 44-point glass circle, the size a UIBarButtonItem uses.
                if (_glass)
                    behavior.Padding = new Thickness(10);
                _imageButton.Behaviors.Add(behavior);
                _currentSvg = action.Svg;
            }

            _imageButton.Command = action.Command;
            _imageButton.CommandParameter = action.CommandParameter;
        }
        else
        {
            if (_currentSvg is not null)
            {
                _imageButton.Behaviors.Clear();
                _currentSvg = null;
            }

            _textButton.Text = action.Text ?? string.Empty;
            _textButton.Command = action.Command;
            _textButton.CommandParameter = action.CommandParameter;
        }
    }
}
