using Orientera.Domain;

namespace Orientera.Features.Events;

[NavigableRegion(Title = "Tävling")]
public partial class EventDetailsPage : INavigableWithParameter<CompetitionId>
{
    public EventDetailsPage() => InitializeComponent();
}
