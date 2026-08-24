namespace Orientera.Features.Results;

/// <summary>
/// One runner's race: the result, the legs and what the analysis makes of them.
/// </summary>
/// <remarks>
/// About a person, not a field. The field is the participant list's job, and splitting the two
/// is what lets this page be opened from any row rather than only from the reader's own.
/// </remarks>
[NavigableRegion(Title = "Resultat")]
public partial class RunnerResultPage : INavigableWithParameter<RunnerResultTarget>
{
    public RunnerResultPage() => InitializeComponent();
}
