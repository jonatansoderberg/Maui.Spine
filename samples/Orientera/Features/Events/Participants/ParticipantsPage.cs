namespace Orientera.Features.Events.Participants;

[NavigableRegion(Title = "Deltagare")]
public partial class ParticipantsPage : INavigableWithParameter<ParticipantsTarget>
{
    public ParticipantsPage()
    {
        InitializeComponent();

        // How wide the split table may be is layout, not data: the view is the only one that
        // knows how much room the columns have before they have to scroll.
        Table.SizeChanged += (_, _) => (BindingContext as ParticipantsPageViewModel)?.Fit(Table.Width);
    }
}
