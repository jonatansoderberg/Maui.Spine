namespace Orientera.Features.Live;

[NavigableSheet(
    Title = "Välj tävling",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium])]
public partial class ChooseCompetitionSheet : INavigableWithParameter<CompetitionChoice>, INavigableWithResult<string>
{
    public ChooseCompetitionSheet() => InitializeComponent();
}
