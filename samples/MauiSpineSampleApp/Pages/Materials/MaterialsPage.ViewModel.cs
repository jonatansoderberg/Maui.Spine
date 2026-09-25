using System.ComponentModel;
using System.Text;
using MauiSpineSampleApp.Pages.HeaderBar;
using Plugin.Maui.Spine.Extensions;

namespace MauiSpineSampleApp.Pages.Materials;

public partial class MaterialsPageViewModel : ViewModelBase
{
    /// <summary>The groups whose choice the page describes.</summary>
    public IReadOnlyList<ChoiceGroup> Groups { get; }

    /// <summary>The footer's rows around its sliders: preset and kind over the intensity, tint over its opacity, interactive last.</summary>
    public IReadOnlyList<ChoiceGroup> KindGroups { get; }

    public IReadOnlyList<ChoiceGroup> TintGroups { get; }

    public IReadOnlyList<ChoiceGroup> LastGroups { get; }

    readonly ChoiceGroup _preset, _kind, _tint, _interactive;

    static readonly MaterialPreset[] Presets =
        [MaterialPreset.GlassClear, MaterialPreset.GlassRegular, MaterialPreset.BlurUltraThin, MaterialPreset.BlurThin, MaterialPreset.BlurRegular, MaterialPreset.BlurThick];

    // The tint colours the footer offers, with their markup.
    static readonly (string Label, string Hex, string Description)[] Tints =
    [
        ("Auto", "", "The theme's surface: white in light mode, near-black in dark, the colour of cards and sheets. The default."),
        ("White", "#FFFFFF", "White, in light and dark alike."),
        ("Black", "#000000", "Black: a darker panel over the same blur."),
        ("Accent", "#0A84FF", "A colour bled in: over a blur, or into glass."),
    ];

    [ObservableProperty]
    public partial MaterialKind Kind { get; set; } = MaterialKind.Blur;

    [ObservableProperty]
    public partial double Intensity { get; set; } = 1;

    [ObservableProperty]
    public partial int TintIndex { get; set; }

    [ObservableProperty]
    public partial double TintOpacity { get; set; }

    [ObservableProperty]
    public partial bool Interactive { get; set; }

    /// <summary>The gap between the two glass buttons in the container; they merge below the container's spacing.</summary>
    [ObservableProperty]
    public partial double Gap { get; set; } = 40;

    public MaterialsPageViewModel()
    {
        _preset = new("Preset", [.. Presets.Select(preset => new Choice(preset.ToString(), PresetDescription(preset), () => Apply(preset)))]);

        _kind = new("Kind",
        [
            new("None", "Nothing is done to what is behind: the tint alone. A tinted panel, cheap and the same on every platform; solid at tint opacity 1.", () => Kind = MaterialKind.None),
            new("Blur", "What is behind, blurred: the thinnest system material on iOS, acrylic on Windows, a GPU blur on Android 12+ (the tint alone before). Panels over photos, maps and heroes.", () => Kind = MaterialKind.Blur),
            new("Glass", "Liquid Glass on iOS 26: refracts what is behind it and can react to touch. For controls that float over content, never for panels full of it. Blur elsewhere.", () => Kind = MaterialKind.Glass),
        ]);

        _tint = new("Tint", [.. Tints.Select((tint, i) => new Choice(tint.Label, tint.Description, () => TintIndex = i))]);

        _interactive = new("Interactive",
        [
            new("Off", "The glass is still.", () => Interactive = false),
            new("On", "iOS 26 glass lights up and swells under the finger, as system buttons do. Press and hold a panel.", () => Interactive = true),
        ]);

        Groups = [_kind, _tint, _interactive];
        KindGroups = [_preset, _kind];
        TintGroups = [_tint];
        LastGroups = [_interactive];
        Sync();
    }

    static string PresetDescription(MaterialPreset preset)
    {
        var (kind, intensity, tintOpacity) = Material.Values(preset);
        return $"Kind {kind}, intensity {intensity:0.##}, tint opacity {tintOpacity:0.##}.";
    }

    void Apply(MaterialPreset preset) => (Kind, Intensity, TintOpacity) = Material.Values(preset);

    /// <summary>The preset whose values are the ones on screen, if any.</summary>
    MaterialPreset? Preset => Presets.Cast<MaterialPreset?>().FirstOrDefault(p =>
    {
        var (kind, intensity, tintOpacity) = Material.Values(p!.Value);
        return kind == Kind && Math.Abs(intensity - Intensity) < 0.005 && Math.Abs(tintOpacity - TintOpacity) < 0.005;
    });

    public Color? Tint => TintIndex == 0 ? null : Color.FromArgb(Tints[TintIndex].Hex);

    public bool UsesIntensity => Kind != MaterialKind.None;

    public string PresetSummary => Preset is { } preset
        ? $"{preset}: {PresetDescription(preset)} A preset only gives these values; set any of them yourself and yours wins."
        : "None: these values are no preset's.";

    public string IntensityDescription => Kind switch
    {
        MaterialKind.Glass => "How frosted the glass is: clear at 0, the system's regular glass at 1, and a steady morph between.",
        MaterialKind.Blur => "How strongly what is behind is blurred: not at all at 0, the full system material at 1.",
        _ => "Nothing is done to what is behind, so there is nothing to frost.",
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(Kind) or nameof(Intensity) or nameof(TintIndex) or nameof(TintOpacity) or nameof(Interactive))
            Sync();
    }

    void Sync()
    {
        if (Groups is null)
            return;

        _preset.Select(Preset is { } preset ? Array.IndexOf(Presets, preset) : -1);
        _kind.Select((int)Kind);
        _tint.Select(TintIndex);
        _interactive.Select(Interactive ? 1 : 0);
        _interactive.IsVisible = Kind == MaterialKind.Glass;

        OnPropertyChanged(nameof(Tint));
        OnPropertyChanged(nameof(UsesIntensity));
        OnPropertyChanged(nameof(PresetSummary));
        OnPropertyChanged(nameof(IntensityDescription));
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(Code));
    }

    public string Caption => Preset?.ToString()
        ?? (UsesIntensity ? $"{Kind} · {Intensity:0.00} · tint {TintOpacity:0.00}" : $"Tint {TintOpacity:0.00}");

    public string Code
    {
        get
        {
            var code = new StringBuilder();
            if (Preset is { } preset)
            {
                code.Append($"<Border Material.Preset=\"{preset}\"");
            }
            else
            {
                code.Append("<Border");
                if (Kind != MaterialKind.None)
                    code.Append($" Material.Kind=\"{Kind}\"");
                if (Kind != MaterialKind.None && Intensity < 1)
                    code.Append($"\n        Material.Intensity=\"{Intensity:0.00}\"");
                if (TintOpacity > 0)
                    code.Append($"\n        Material.TintOpacity=\"{TintOpacity:0.00}\"");
            }
            if (Tint is not null)
                code.Append($"\n        Material.Tint=\"{Tints[TintIndex].Hex}\"");
            if (Interactive && _interactive.IsVisible)
                code.Append("\n        Material.Interactive=\"True\"");
            code.Append("\n        StrokeThickness=\"0\"\n        StrokeShape=\"RoundRectangle 20\">\n  <Label Text=\"Over the photo\" />\n</Border>");
            code.Append("\n\n<!-- Glass that merges (iOS 26): -->\n<MaterialContainer Spacing=\"20\">\n  <HorizontalStackLayout Spacing=\"8\">\n    <Border Material.Kind=\"Glass\" ... />\n    <Border Material.Kind=\"Glass\" ... />");
            return code.ToString();
        }
    }
}
