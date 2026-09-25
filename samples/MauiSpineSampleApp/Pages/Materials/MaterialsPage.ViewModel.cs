using System.ComponentModel;
using System.Text;
using MauiSpineSampleApp.Pages.HeaderBar;
using Plugin.Maui.Spine.Extensions;

namespace MauiSpineSampleApp.Pages.Materials;

public partial class MaterialsPageViewModel : ViewModelBase
{
    public IReadOnlyList<ChoiceGroup> Groups { get; }

    readonly ChoiceGroup _kind, _thickness, _tint, _interactive;

    [ObservableProperty]
    public partial MaterialKind Kind { get; set; } = MaterialKind.Blur;

    [ObservableProperty]
    public partial MaterialThickness Thickness { get; set; } = MaterialThickness.Regular;

    [ObservableProperty]
    public partial Color? Tint { get; set; }

    [ObservableProperty]
    public partial bool Interactive { get; set; }

    /// <summary>The gap between the two glass buttons in the container; they merge below the container's spacing.</summary>
    [ObservableProperty]
    public partial double Gap { get; set; } = 40;

    public MaterialsPageViewModel()
    {
        _kind = new("Kind",
        [
            new("Glass", "Liquid Glass on iOS 26: refracts what is behind it and can react to touch. For controls that float over content, never for panels full of it. Blur elsewhere.", () => Kind = MaterialKind.Glass),
            new("Blur", "What is behind, blurred: a system material on iOS, acrylic on Windows, a GPU blur on Android 12+ (tinted before). Panels over photos, maps and heroes.", () => Kind = MaterialKind.Blur),
            new("Tinted", "The theme's surface, see-through, no blur. Cheap, and the same on every platform.", () => Kind = MaterialKind.Tinted),
            new("Solid", "The theme's surface, opaque. What every material becomes when it has to be readable above all.", () => Kind = MaterialKind.Solid),
        ]);

        _thickness = new("Thickness",
        [
            new("Ultra thin", "Most of what is behind comes through.", () => Thickness = MaterialThickness.UltraThin),
            new("Thin", "Much of it.", () => Thickness = MaterialThickness.Thin),
            new("Regular", "The system's standard material. The default.", () => Thickness = MaterialThickness.Regular),
            new("Thick", "Little of it.", () => Thickness = MaterialThickness.Thick),
            new("Chrome", "Almost none, as behind a system bar.", () => Thickness = MaterialThickness.Chrome),
        ]);

        _tint = new("Tint",
        [
            new("None", "The material's own colour, following light and dark.", () => Tint = null),
            new("Accent", "A colour bled in: the tint of glass, a layer over a blur, the colour of a tinted or solid surface. Give a blur's tint some transparency.", () => Tint = Color.FromArgb("#660A84FF")),
        ]);

        _interactive = new("Interactive",
        [
            new("Off", "The glass is still.", () => Interactive = false),
            new("On", "iOS 26 glass lights up and stretches under the finger, as system buttons do. Glass only.", () => Interactive = true),
        ]);

        Groups = [_kind, _thickness, _tint, _interactive];
        Sync();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(Kind) or nameof(Thickness) or nameof(Tint) or nameof(Interactive))
            Sync();
    }

    void Sync()
    {
        if (Groups is null)
            return;

        _kind.Select((int)Kind - 1);
        _thickness.Select((int)Thickness);
        _tint.Select(Tint is null ? 0 : 1);
        _interactive.Select(Interactive ? 1 : 0);

        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(Code));
    }

    public string Caption => Kind == MaterialKind.Blur ? $"Blur · {Thickness}" : Kind.ToString();

    public string Code
    {
        get
        {
            var code = new StringBuilder($"<Border Material.Kind=\"{Kind}\"");
            if (Kind == MaterialKind.Blur && Thickness != MaterialThickness.Regular)
                code.Append($"\n        Material.Thickness=\"{Thickness}\"");
            if (Tint is not null)
                code.Append("\n        Material.Tint=\"#660A84FF\"");
            if (Interactive)
                code.Append("\n        Material.Interactive=\"True\"");
            code.Append("\n        StrokeThickness=\"0\"\n        StrokeShape=\"RoundRectangle 20\">\n  <Label Text=\"Over the photo\" />\n</Border>");
            code.Append("\n\n<!-- Glass that merges (iOS 26): -->\n<MaterialContainer Spacing=\"20\">\n  <HorizontalStackLayout Spacing=\"8\">\n    <Border Material.Kind=\"Glass\" ... />\n    <Border Material.Kind=\"Glass\" ... />");
            return code.ToString();
        }
    }
}
