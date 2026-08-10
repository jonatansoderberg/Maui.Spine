using Orientera.Domain;

namespace Orientera.Features.Results;

[NavigableRegion(Title = "Resultat")]
public partial class ResultsDetailPage : INavigableWithParameter<CompetitionId>
{
    public ResultsDetailPage() => InitializeComponent();
}
