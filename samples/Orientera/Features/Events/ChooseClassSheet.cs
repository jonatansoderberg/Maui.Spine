namespace Orientera.Features.Events;

[NavigableSheet(
    Title = "Välj klass",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium])]
public partial class ChooseClassSheet : INavigableWithResult<string>
{
    public ChooseClassSheet() => InitializeComponent();
}
