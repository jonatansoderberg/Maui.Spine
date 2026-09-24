namespace MauiSpineSampleApp.Controls;

/// <summary>
/// A view that colours itself in code, the way a Skia-drawn or code-built control does. It is
/// blind to AppThemeBinding and DynamicResource, so it asks the theme service to be told when to
/// paint again.
/// </summary>
public sealed class TokenSwatch : ContentView
{
    private readonly Border _border = new() { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, Padding = new Thickness(12, 10) };
    private readonly Label _label = new() { FontSize = 14 };

    public TokenSwatch()
    {
        _border.Content = _label;
        Content = _border;

        Paint();
        SpineTheme.Track(this, Paint);
    }

    private void Paint()
    {
        var resources = Application.Current?.Resources;
        _border.BackgroundColor = resources?["Accent"] as Color;
        _label.TextColor = resources?["OnAccent"] as Color;
        _label.Text = $"Painted in code at theme version {SpineTheme.Version}";
    }
}
