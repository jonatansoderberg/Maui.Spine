using System.Collections.ObjectModel;
using Orientera.Domain;

namespace Orientera.Features.Results;

/// <summary>
/// Explains the interval. A prediction the user cannot interrogate is a number they cannot
/// trust, so the drivers behind it are first-class content, not a tooltip.
/// </summary>
public partial class PredictionInfoSheetViewModel : ViewModelBase, IReceivesNavigationParameter<Prediction>
{
    [ObservableProperty] public partial string Range { get; set; } = string.Empty;
    [ObservableProperty] public partial string FieldText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ConfidenceText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ModelText { get; set; } = string.Empty;

    public ObservableCollection<string> Drivers { get; } = [];

    public Task OnNavigationParameterAsync(Prediction param)
    {
        Range = param.Range;
        FieldText = $"av {param.FieldSize} anmälda i {param.Class}";
        ConfidenceText = param.Confidence switch
        {
            >= 0.75 => "Hög tillförlitlighet",
            >= 0.55 => "Måttlig tillförlitlighet",
            _ => "Låg tillförlitlighet — tunt underlag",
        };
        ModelText = $"Modell {param.ModelVersion}";

        Drivers.Clear();

        foreach (var driver in param.Drivers)
            Drivers.Add(driver);

        return Task.CompletedTask;
    }
}
