using Orientera.Domain;

namespace Orientera.Features.Results;

/// <summary>
/// Which comparison the sheet should offer. Spine's typed navigation cannot combine a
/// parameter with a result, so the caller leaves the request here before presenting the sheet.
/// </summary>
public sealed class ComparisonRequest
{
    public CompetitionId Competition { get; private set; }
    public string Class { get; private set; } = string.Empty;
    public PersonId Exclude { get; private set; }

    public void Set(CompetitionId competition, string className, PersonId exclude)
    {
        Competition = competition;
        Class = className;
        Exclude = exclude;
    }
}

[NavigableSheet(
    Title = "Jämför löpare",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public partial class CompareRunnerSheet : INavigableWithResult<PersonId>
{
    public CompareRunnerSheet() => InitializeComponent();
}
