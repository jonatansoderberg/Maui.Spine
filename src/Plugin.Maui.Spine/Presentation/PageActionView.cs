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

    public static readonly BindableProperty HideDisabledProperty = BindableProperty.Create(
        nameof(HideDisabled),
        typeof(bool),
        typeof(PageActionView),
        false,
        propertyChanged: OnHideDisabledChanged);

    readonly Button _textButton;
    readonly ImageButton _imageButton;
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
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark
                || (Application.Current?.RequestedTheme != AppTheme.Light
                    && Application.Current?.PlatformAppTheme == AppTheme.Dark);
            _textButton.TextColor = isDark
                ? GetResourceColor("PrimaryDark", Color.FromArgb("#ac99ea"))
                : GetResourceColor("Primary", Color.FromArgb("#512BD4"));
        }

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

        Content = new Grid
        {
            Children = { _textButton, _imageButton }
        };

        ApplyAction();
    }

    // The option is read here rather than passed down: HeaderBar and PageActionView are built by
    // pages, not by DI, and the attached property is a no-op off Apple anyway.
    static bool UseGlassHeaderActions =>
        OperatingSystem.IsIOS()
        && IPlatformApplication.Current?.Services.GetService<SpineOptions>()?.Apple.GlassHeaderActions == true;

    static void OnActionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (PageActionView)bindable;
        var oldSvg = (oldValue as PageAction)?.Svg;
        var newSvg = (newValue as PageAction)?.Svg;

        var sameSvg = !string.IsNullOrWhiteSpace(oldSvg)
                   && !string.IsNullOrWhiteSpace(newSvg)
                   && oldSvg == newSvg;

        if (sameSvg)
            view.ApplyAction();
        else
            _ = view.ApplyActionAnimatedAsync();
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
            return;
        }

        var hasSvg = !string.IsNullOrWhiteSpace(action.Svg);

        _imageButton.IsVisible = hasSvg;
        _textButton.IsVisible = !hasSvg;

        if (hasSvg)
        {
            if (action.Svg != _currentSvg)
            {
                _imageButton.Behaviors.Clear();
                var behavior = new SvgImageSourceBehavior
                {
                    Svg = action.Svg!,
                    LightTintColor = Colors.Black,
                    DarkTintColor = Colors.White
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
