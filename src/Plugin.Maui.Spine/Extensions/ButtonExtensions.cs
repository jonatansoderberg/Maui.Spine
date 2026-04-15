namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Attached properties that extend <see cref="Button"/> with a compact layout mode
/// that removes default padding, insets, and minimum dimensions.
/// </summary>
public static class ButtonExtensions
{
    /// <summary>
    /// Attached property that, when <see langword="true"/>, configures the button
    /// with zero padding and minimum-size constraints removed.
    /// </summary>
    public static readonly BindableProperty CompactProperty =
        BindableProperty.CreateAttached(
            "Compact",
            typeof(bool),
            typeof(ButtonExtensions),
            false,
            propertyChanged: OnCompactChanged);

    /// <summary>Gets the value of the <see cref="CompactProperty"/> attached property.</summary>
    public static bool GetCompact(BindableObject view) => (bool)view.GetValue(CompactProperty);

    /// <summary>Sets the value of the <see cref="CompactProperty"/> attached property.</summary>
    public static void SetCompact(BindableObject view, bool value) => view.SetValue(CompactProperty, value);

    /// <summary>Returns <see langword="true"/> when compact mode is enabled on this button.</summary>
    public static bool IsCompact(this Button button) => GetCompact(button);

    static void OnCompactChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not Button button || newValue is not bool compact || !compact)
            return;

        button.Padding = new Thickness(0);
        button.MinimumHeightRequest = 0;
        button.MinimumWidthRequest = 0;

        if (button.Handler is not null)
            button.Handler.UpdateValue("SpineCompactButton");
        else
            button.HandlerChanged += OnHandlerChanged;

        static void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (sender is not Button btn) return;
            btn.HandlerChanged -= OnHandlerChanged;
            btn.Handler?.UpdateValue("SpineCompactButton");
        }
    }
}
