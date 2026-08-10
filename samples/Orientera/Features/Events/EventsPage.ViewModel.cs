namespace Orientera.Features.Events;

public partial class EventsPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [RelayCommand] private async Task OpenDetails() => await _navigation.NavigateToAsync<EventDetailsPage>();
}
