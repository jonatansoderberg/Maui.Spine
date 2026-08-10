using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Sources;

namespace Orientera.Features.Results;

public sealed record MyResultRow
{
    public required CompetitionId Competition { get; init; }
    public required string Name { get; init; }
    public required string Meta { get; init; }
    public required string PlaceText { get; init; }
    public required string TimeText { get; init; }
    public required string BehindText { get; init; }
    public required bool HasSplits { get; init; }
    public required bool IsPreliminary { get; init; }
    public required string Accessibility { get; init; }
}

public partial class ResultsPageViewModel(
    INavigationService _navigation,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation) : OrienteraViewModel
{
    public ObservableCollection<MyResultRow> Results { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial bool HasResults { get; set; }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        await LoadAsync(BuildAsync);

        if (IsOffline)
        {
            Results.Clear();
            HasResults = false;
            IsEmpty = true;
        }
    }

    private async Task BuildAsync()
    {
        var me = await _people.GetMeAsync();
        var competitions = await _events.GetCompetitionsAsync();
        var results = await _participation.GetResultsForPersonAsync(me.Id);

        Results.Clear();

        foreach (var result in results)
        {
            var competition = competitions.FirstOrDefault(c => c.Id == result.Competition);

            if (competition is null)
                continue;

            Results.Add(new MyResultRow
            {
                Competition = result.Competition,
                Name = competition.Name,
                Meta = $"{competition.Date:d MMM} · {result.Class} · {Format.Discipline(competition.Discipline)}",
                PlaceText = Format.Place(result.Place),
                TimeText = Format.Time(result.Time),
                BehindText = result.BehindWinner is { } behind ? Format.Delta(behind) : string.Empty,
                HasSplits = result.Splits.Count > 0,
                IsPreliminary = result.Status == ResultStatus.Preliminary,
                Accessibility = string.Join(", ",
                    new[]
                    {
                        competition.Name,
                        $"{competition.Date:d MMMM}",
                        $"klass {result.Class}",
                        Format.SpokenPlace(result.Place),
                        Format.SpokenTime(result.Time),
                        Format.SpokenDelta(result.BehindWinner),
                        result.Splits.Count > 0 ? "sträcktider finns" : string.Empty,
                    }.Where(part => part.Length > 0)),
            });
        }

        HasResults = Results.Count > 0;
        IsEmpty = !HasResults;
    }

    [RelayCommand]
    private async Task OpenResult(MyResultRow row) =>
        await _navigation.NavigateToAsync<ResultsDetailPage, CompetitionId>(row.Competition);
}
