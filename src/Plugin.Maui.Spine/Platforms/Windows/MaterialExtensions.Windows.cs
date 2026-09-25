using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;
using AcrylicBrush = Microsoft.UI.Xaml.Media.AcrylicBrush;
using GradientStop = Microsoft.UI.Xaml.Media.GradientStop;
using LinearGradientBrush = Microsoft.UI.Xaml.Media.LinearGradientBrush;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WBrush = Microsoft.UI.Xaml.Media.Brush;
using WPanel = Microsoft.UI.Xaml.Controls.Panel;
using WPath = Microsoft.UI.Xaml.Shapes.Path;
using WPoint = Windows.Foundation.Point;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    // MAUI paints the background for these; the material is painted again after it.
    static readonly string[] MaterialKeys = [Material.MapperKey, nameof(IView.Background), nameof(IBorderStroke.Shape), nameof(IView.Height)];

    static readonly BindableProperty MaterialAppliedProperty = BindableProperty.CreateAttached(
        "MaterialApplied", typeof(bool), typeof(SpineExtensions), false);

    static void ConfigureMaterials()
    {
        foreach (var key in MaterialKeys)
        {
            BorderHandler.Mapper.AppendToMapping(key, ApplyMaterial);
            ContentViewHandler.Mapper.AppendToMapping(key, ApplyMaterial);
            LayoutHandler.Mapper.AppendToMapping(key, ApplyMaterial);
        }
    }

    static void ApplyMaterial(IElementHandler handler, IElement element)
    {
        if (handler.PlatformView is not WPanel panel || element is not VisualElement visual)
            return;

        if (!Material.IsOn(visual))
        {
            // MAUI paints the view's own background again.
            if ((bool)visual.GetValue(MaterialAppliedProperty))
            {
                visual.SetValue(MaterialAppliedProperty, false);
                handler.UpdateValue(nameof(IView.Background));
            }
            return;
        }

        if (!(bool)visual.GetValue(MaterialAppliedProperty))
        {
            visual.SetValue(MaterialAppliedProperty, true);
            SpineTheme.Track(visual, () => visual.Handler?.UpdateValue(Material.MapperKey));
        }

        var brush = MaterialBrush(visual);

        // A Border paints its shape with a path; anything else paints the panel.
        if (panel.Children.OfType<WPath>().FirstOrDefault() is { } shape)
            shape.Fill = brush;
        else
            panel.Background = brush;
    }

    internal static WBrush MaterialBrush(VisualElement view)
    {
        var kind = Material.Resolve(Material.GetKind(view));
        var fade = Material.GetFade(view);
        var edge = Material.GetEdgeLine(view);
        var tint = Material.TintLayer(view);

        // Acrylic has no fade and no hairline; the header bar's band is the tinted surface instead. Its
        // blur radius is fixed, so a weaker one is acrylic let through.
        if (kind is MaterialKind.Blur or MaterialKind.Glass && fade <= 0 && edge is null)
        {
            var colour = Material.Over(tint, Material.Surface().WithAlpha(0.1f))!;
            return new AcrylicBrush
            {
                TintColor = colour.WithAlpha(1).ToWindowsColor(),
                TintOpacity = colour.Alpha,
                FallbackColor = colour.WithAlpha(1).ToWindowsColor(),
                Opacity = Material.BlurIntensity(view),
            };
        }

        var fill = tint ?? Colors.Transparent;
        if (fade <= 0 && edge is null)
            return new SolidColorBrush(fill.ToWindowsColor());

        // Stops in proportions of the element's height; ActualHeight is known once it has been laid out.
        var height = Math.Max(1, view.Height > 0 ? view.Height : view.HeightRequest);
        var gradient = new LinearGradientBrush { StartPoint = new WPoint(0, 0), EndPoint = new WPoint(0, 1) };
        var bottom = 1 - Math.Min(fade, height) / height;
        gradient.GradientStops.Add(new GradientStop { Color = fill.ToWindowsColor(), Offset = 0 });

        if (edge is not null)
        {
            var line = bottom - 1 / height;
            gradient.GradientStops.Add(new GradientStop { Color = fill.ToWindowsColor(), Offset = line });
            gradient.GradientStops.Add(new GradientStop { Color = edge.ToWindowsColor(), Offset = line });
            gradient.GradientStops.Add(new GradientStop { Color = edge.ToWindowsColor(), Offset = bottom });
        }
        else
        {
            gradient.GradientStops.Add(new GradientStop { Color = fill.ToWindowsColor(), Offset = bottom });
        }

        gradient.GradientStops.Add(new GradientStop { Color = fill.WithAlpha(0).ToWindowsColor(), Offset = 1 });
        return gradient;
    }
}
