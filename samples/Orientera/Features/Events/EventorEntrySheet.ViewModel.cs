using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Eventor;

namespace Orientera.Features.Events;

public partial class EventorEntrySheetViewModel(INavigationService _navigation)
    : OrienteraViewModel, IReceivesNavigationParameter<CompetitionId>
{
    [ObservableProperty] public partial string EntryUrl { get; set; } = EventorSite.Origin;

    public Task OnNavigationParameterAsync(CompetitionId competition)
    {
        EntryUrl = EventorSite.EntryUrl(competition.Value);

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Close() => await _navigation.BackAsync();
}
