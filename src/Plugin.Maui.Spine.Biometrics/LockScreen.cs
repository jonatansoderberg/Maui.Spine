using Microsoft.Maui.Controls.Shapes;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary>The unlock screen of the app lock and of a relocked page: the app's name, the reason and an Unlock button.</summary>
internal sealed class LockScreen : Grid
{
    public LockScreen(string reason, Action unlock, Action? back)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page?.BackgroundColor is { Alpha: >= 1 } background)
            BackgroundColor = background;
        else
            this.SetAppThemeColor(BackgroundColorProperty, Colors.White, Colors.Black);

        var glyph = new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString("M7 11V7a5 5 0 0 1 10 0v4M5 11h14v10H5z")!,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            Aspect = Stretch.Uniform,
            WidthRequest = 40,
            HeightRequest = 40,
            HorizontalOptions = LayoutOptions.Center,
        };
        glyph.SetAppTheme<Brush>(Shape.StrokeProperty, Brush.Black, Brush.White);
        AutomationProperties.SetIsInAccessibleTree(glyph, false);

        var title = new Label
        {
            Text = AppInfo.Current.Name,
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
        };
        SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level1);

        var message = new Label
        {
            Text = reason,
            FontSize = 16,
            Opacity = 0.7,
            HorizontalTextAlignment = TextAlignment.Center,
        };

        var unlockButton = new Button
        {
            Text = BiometricsStrings.Get("Spine.Biometrics.Unlock"),
            HorizontalOptions = LayoutOptions.Center,
            MinimumWidthRequest = 160,
        };
        unlockButton.Clicked += (_, _) => unlock();

        var content = new VerticalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(32),
            MaximumWidthRequest = 420,
            VerticalOptions = LayoutOptions.Center,
            Children = { glyph, title, message, unlockButton },
        };

        if (back is not null)
        {
            var backButton = new Button
            {
                Text = BiometricsStrings.Get("Spine.Biometrics.Back"),
                HorizontalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.Transparent,
                BorderWidth = 0,
            };
            backButton.SetAppThemeColor(Button.TextColorProperty, Colors.Black, Colors.White);
            backButton.Clicked += (_, _) => back();
            content.Children.Add(backButton);
        }

        Children.Add(content);
    }
}
