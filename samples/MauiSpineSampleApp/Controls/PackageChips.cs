using Microsoft.Maui.Layouts;

namespace MauiSpineSampleApp.Controls;

/// <summary>
/// The Spine packages a sample depends on, as small pills. <see cref="Packages"/> is a
/// comma-separated list of NuGet ids, e.g. <c>"Plugin.Maui.Spine, Plugin.Maui.Spine.Svg"</c>.
/// The sample references the projects directly; these are the packages an app would install.
/// </summary>
public sealed class PackageChips : ContentView
{
    public static readonly BindableProperty PackagesProperty = BindableProperty.Create(
        nameof(Packages), typeof(string), typeof(PackageChips), null,
        propertyChanged: (b, _, _) => ((PackageChips)b).Rebuild());

    public string? Packages
    {
        get => (string?)GetValue(PackagesProperty);
        set => SetValue(PackagesProperty, value);
    }

    private readonly FlexLayout _layout = new() { Wrap = FlexWrap.Wrap, JustifyContent = FlexJustify.Start, AlignItems = FlexAlignItems.Start };

    public PackageChips()
    {
        Content = _layout;
    }

    private void Rebuild()
    {
        _layout.Children.Clear();

        foreach (var id in (Packages ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var label = new Label
            {
                // Short form: the common prefix is implied, the pill says what is specific.
                Text = id.StartsWith("Plugin.Maui.Spine", StringComparison.Ordinal) ? "Spine" + id["Plugin.Maui.Spine".Length..] : id,
                FontSize = 10,
                LineBreakMode = LineBreakMode.NoWrap,
            };
            label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#512BD4"), Color.FromArgb("#CFC2FF"));

            var pill = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 9 },
                Padding = new Thickness(8, 2),
                Margin = new Thickness(0, 0, 6, 4),
                Content = label,
            };
            pill.SetAppThemeColor(Border.BackgroundColorProperty, Color.FromArgb("#22512BD4"), Color.FromArgb("#40B39DFF"));

            _layout.Children.Add(pill);
        }

        IsVisible = _layout.Children.Count > 0;
    }
}
