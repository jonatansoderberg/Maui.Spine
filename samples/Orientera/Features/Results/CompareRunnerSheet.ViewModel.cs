using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Sources;

namespace Orientera.Features.Results;

public sealed record CompareCandidate
{
    public required PersonId Person { get; init; }
    public required string Name { get; init; }
    public required string Club { get; init; }
    public required string PlaceText { get; init; }
    public required string TimeText { get; init; }
    public required bool IsWinner { get; init; }
    public required bool IsInMyGroup { get; init; }
    public required string Accessibility { get; init; }
}

public partial class CompareRunnerSheetViewModel(
    INavigationService _navigation,
    IPeopleSource _people,
    IParticipationSource _participation) : ViewModelBase,
    IReceivesNavigationParameter<ComparisonRequest>
{
    private ComparisonRequest? _request;

    public ObservableCollection<CompareCandidate> Candidates { get; } = [];

    public Task OnNavigationParameterAsync(ComparisonRequest param)
    {
        _request = param;
        return Task.CompletedTask;
    }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (_request is null)
            return;

        var results = await _participation.GetResultsAsync(_request.Competition);
        var group = await _people.GetMyGroupAsync();
        var groupIds = group.Select(f => f.Person.Id).ToHashSet();

        var candidates = results
            .Where(r => r.Class == _request.Class && r.Person != _request.Exclude && r.Splits.Count > 0)
            .OrderBy(r => r.Place ?? int.MaxValue)
            .ToList();

        Candidates.Clear();

        foreach (var result in candidates)
        {
            Candidates.Add(new CompareCandidate
            {
                Person = result.Person,
                Name = result.Name,
                Club = result.Club,
                PlaceText = Format.Place(result.Place),
                TimeText = Format.Time(result.Time),
                IsWinner = result.Place == 1,
                IsInMyGroup = groupIds.Contains(result.Person),
                Accessibility = string.Join(", ",
                    new[]
                    {
                        Format.SpokenPlace(result.Place),
                        result.Name,
                        result.Club,
                        Format.SpokenTime(result.Time),
                        groupIds.Contains(result.Person) ? "i min grupp" : string.Empty,
                    }.Where(part => part.Length > 0)),
            });
        }
    }

    [RelayCommand]
    private async Task Choose(CompareCandidate candidate) =>
        await _navigation.ReturnAsync(candidate.Person);
}
