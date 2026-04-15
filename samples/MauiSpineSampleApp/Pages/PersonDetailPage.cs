namespace MauiSpineSampleApp.Pages;

[NavigableRegion(Title = "Person Detail")]
public partial class PersonDetailPage : INavigableWithParameter<PersonData>
{
    public PersonDetailPage() => InitializeComponent();
}
