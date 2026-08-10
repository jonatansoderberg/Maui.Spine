namespace Orientera.Features.Events;

[NavigableSheet(
    Title = "Filter",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen],
    InitialDetent = SheetDetent.Medium)]
public partial class EventFilterSheet : INavigableWithResult<EventFilter>
{
    public EventFilterSheet() => InitializeComponent();
}
