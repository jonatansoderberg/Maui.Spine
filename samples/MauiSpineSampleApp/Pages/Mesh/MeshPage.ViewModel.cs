using System.ComponentModel;
using System.Text;
using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Mesh;

public partial class MeshPageViewModel : SampleViewModel
{
    // The day themes of a calendar app, one colour per point of a 3 × 3 mesh, row by row.
    static readonly (string Name, string Colors, string Description)[] Days =
    [
        ("Dawn", "#2B2F6B, #6B4C9A, #F2A07B, #9A5C8E, #F7C59F, #FFD9A8, #FDE3C2, #FFE9C9, #FFF1DC", "Night lifting at the top, peach and gold at the horizon."),
        ("Noon", "#7EC8F2, #A8DBF7, #CDEBFB, #9AD3F5, #E8F6FD, #BFE4FA, #DDF1FC, #F4FBFE, #CFEAFA", "A pale sky, brightest in the middle."),
        ("Dusk", "#1E1442, #4A2470, #2A1650, #B4466E, #E2735A, #7A3270, #F6A15A, #F4C27A, #D9705A", "Violet overhead, embers along the bottom."),
        ("Night", "#05070F, #0B1230, #070A18, #101A44, #1C2C6B, #0E1636, #070A16, #0A1028, #04050C", "Deep blue, one faint glow."),
    ];

    readonly ChoiceGroup _preset, _drift, _day;
    readonly SliderOption _columns, _rows, _frameRate;
    readonly INavigationService _navigation;

    [ObservableProperty]
    public partial MeshPreset Preset { get; set; } = MeshPreset.Aurora;

    [ObservableProperty]
    public partial MeshDrift Drift { get; set; } = MeshDrift.Slow;

    [ObservableProperty]
    public partial int Columns { get; set; } = 3;

    [ObservableProperty]
    public partial int Rows { get; set; } = 3;

    [ObservableProperty]
    public partial int FrameRate { get; set; } = 30;

    [ObservableProperty]
    public partial int DayIndex { get; set; }

    public MeshPageViewModel(INavigationService navigation)
    {
        _navigation = navigation;

        _preset = new("Preset",
        [
            new("Accent", "Built from the app's accent, light or dark with the theme. Pick another accent from the palette button in the header and the mesh follows. The default.", () => Preset = MeshPreset.Accent),
            new("Aurora", "Green, teal and violet: northern lights over a night sky in dark mode, pastels in light mode.", () => Preset = MeshPreset.Aurora),
            new("Sunset", "Amber, coral and rose: dusk in dark mode, a warm dawn in light mode.", () => Preset = MeshPreset.Sunset),
        ]);

        _drift = new("Drift",
        [
            new("None", "A still mesh, drawn once and again only when something changes. The default.", () => Drift = MeshDrift.None),
            new("Slow", "About half a minute a round: barely noticed, for a background that is always there.", () => Drift = MeshDrift.Slow),
            new("Medium", "About fifteen seconds a round.", () => Drift = MeshDrift.Medium),
            new("Fast", "About seven seconds a round, for onboarding or a success screen.", () => Drift = MeshDrift.Fast),
        ]);

        _columns = new("Columns", 2, 8, () => Columns, value => Columns = (int)Math.Round(value), "0");
        _rows = new("Rows", 2, 8, () => Rows, value => Rows = (int)Math.Round(value), "0");
        _frameRate = new("Frame rate", 5, 60, () => FrameRate, value => FrameRate = (int)Math.Round(value), "0");

        _day = new("Day", [.. Days.Select((day, i) => new Choice(day.Name, day.Description, () => DayIndex = i))]);

        Sync();
    }

    public IReadOnlyList<Color> DayColors => [.. Days[DayIndex].Colors.Split(", ").Select(Color.FromArgb)];

    public string DayName => Days[DayIndex].Name;

    [RelayCommand]
    private Task ShowMeshOptions() => ShowOptionsAsync("Mesh", _preset, _drift, _columns, _rows, _frameRate);

    [RelayCommand]
    private Task ShowDayOptions() => ShowOptionsAsync("Your own colours", _day);

    [RelayCommand]
    private Task OpenFullScreen() =>
        _navigation.NavigateToAsync<MeshFullScreenPage, MeshSettings>(new(Preset, Drift, Columns, Rows, FrameRate));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(Preset) or nameof(Drift) or nameof(Columns) or nameof(Rows) or nameof(FrameRate) or nameof(DayIndex))
            Sync();
    }

    void Sync()
    {
        _preset.Select((int)Preset);
        _drift.Select((int)Drift);
        _columns.Value = Columns;
        _rows.Value = Rows;
        _frameRate.Value = FrameRate;
        _day.Select(DayIndex);

        OnPropertyChanged(nameof(DayColors));
        OnPropertyChanged(nameof(DayName));
        OnPropertyChanged(nameof(Code));
        OnPropertyChanged(nameof(DayCode));
    }

    /// <summary>The markup for the mesh on screen.</summary>
    public string Code
    {
        get
        {
            var code = new StringBuilder("<Grid>\n  <MeshBackground");
            var attributes = new List<string>();
            if (Preset != MeshPreset.Accent)
                attributes.Add($"Preset=\"{Preset}\"");
            if (Drift != MeshDrift.None)
                attributes.Add($"Drift=\"{Drift}\"");
            if (Columns != 3)
                attributes.Add($"Columns=\"{Columns}\"");
            if (Rows != 3)
                attributes.Add($"Rows=\"{Rows}\"");
            if (FrameRate != 30)
                attributes.Add($"FrameRate=\"{FrameRate}\"");

            code.Append(attributes.Count > 0 ? " " + string.Join("\n                  ", attributes) : "");
            code.Append(" />\n\n  <Border Material.Kind=\"Glass\"\n          StrokeThickness=\"0\"\n          StrokeShape=\"RoundRectangle 22\">\n    <Label Text=\"Good evening\" />\n  </Border>\n</Grid>");
            return code.ToString();
        }
    }

    public string DayCode
    {
        get
        {
            var colors = Days[DayIndex].Colors.Split(", ");
            var rows = string.Join(",\n          ", colors.Chunk(3).Select(row => string.Join(", ", row)));
            return $"<MeshBackground Drift=\"Slow\"\n  Colors=\"{rows}\" />";
        }
    }
}
