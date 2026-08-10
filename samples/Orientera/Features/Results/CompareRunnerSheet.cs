using Orientera.Domain;

namespace Orientera.Features.Results;

/// <summary>Which comparison the sheet should offer: the field to pick from, minus me.</summary>
public sealed record ComparisonRequest(CompetitionId Competition, string Class, PersonId Exclude);

[NavigableSheet(
    Title = "Jämför löpare",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public partial class CompareRunnerSheet :
    INavigableWithParameter<ComparisonRequest>,
    INavigableWithResult<PersonId>
{
    public CompareRunnerSheet() => InitializeComponent();
}
