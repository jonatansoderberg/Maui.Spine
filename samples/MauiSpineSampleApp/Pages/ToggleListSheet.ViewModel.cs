namespace MauiSpineSampleApp.Pages;

/// <summary>One row in the toggle list. Every input writes to <see cref="Log"/>.</summary>
public partial class ToggleRow(ToggleListSheetViewModel _owner, string _name) : ObservableObject
{
    public string Name => _name;

    [ObservableProperty] public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value) => _owner.Note($"{_name} → {value}");
}

/// <summary>
/// A row-per-setting toggle list in a sheet — the shape settings screens take.
/// </summary>
/// <remarks>
/// This sample exists because the pattern was once reported as broken (#36: a
/// <c>Switch</c> in a <c>DataTemplate</c> in a sheet ignoring taps). It is not broken. The
/// report came from driving the simulator with instantaneous synthetic taps, and
/// <c>UISwitch</c> toggles from a gesture recognizer that needs the touch to last a moment —
/// a <c>UIButton</c> tracks touches directly and answers a zero-length tap, which is what made
/// the two look different. Anyone testing a toggle on iOS without a finger needs a press with
/// dwell, or a drag across the knob.
///
/// All three cases are here so one run tells them apart: switches inside a template, a switch
/// bound straight to the view model, and a button in the same template.
/// </remarks>
public partial class ToggleListSheetViewModel : ViewModelBase
{
    public ToggleListSheetViewModel()
    {
        Rows = [new(this, "Rad 1"), new(this, "Rad 2")];
    }

    public IReadOnlyList<ToggleRow> Rows { get; }

    [ObservableProperty] public partial bool DirectSwitch { get; set; }

    [ObservableProperty] public partial string Log { get; set; } = "Inget tryck ännu.";

    partial void OnDirectSwitchChanged(bool value) => Note($"Direkt → {value}");

    [RelayCommand]
    private void Toggle(ToggleRow row) => row.IsOn = !row.IsOn;

    internal void Note(string line) => Log = line;
}
