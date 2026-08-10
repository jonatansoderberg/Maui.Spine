using Orientera.Domain;

namespace Orientera.Features.Results;

[NavigableSheet(
    Title = "Om prognosen",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public partial class PredictionInfoSheet : INavigableWithParameter<Prediction>
{
    public PredictionInfoSheet() => InitializeComponent();
}
