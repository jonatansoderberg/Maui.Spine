using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Sources;

namespace Orientera.Features.Profile;

public sealed partial class SearchResultRow : ObservableObject
{
    public required Person Person { get; init; }
    public required string Name { get; init; }
    public required string Meta { get; init; }

    [ObservableProperty]
    public partial bool IsFollowed { get; set; }

    public string ActionText => IsFollowed ? "Följer" : "Följ";

    partial void OnIsFollowedChanged(bool value) => OnPropertyChanged(nameof(ActionText));
}

/// <summary>
/// Min grupp is a private list, so this is a plain search over public people — no requests,
/// no mutual consent, nothing shared back.
/// </summary>
public partial class FollowRunnerSheetViewModel(IPeopleSource _people) : OrienteraViewModel
{
    private IReadOnlySet<PersonId> _followed = new HashSet<PersonId>();

    public ObservableCollection<SearchResultRow> Results { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    /// <summary>
    /// Where the search looks. Eventor has no public person lookup, so the app searches the
    /// result lists it has already fetched — which finds a real runner who has finished a race
    /// lately, and nobody else. Saying so is what keeps an empty answer from reading as "that
    /// person does not exist".
    /// </summary>
    [ObservableProperty]
    public partial string ScopeText { get; set; } =
        "Söker bland löpare i resultatlistorna för tävlingar runt idag.";

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        await ReloadFollowedAsync();
        await SearchAsync();
    }

    partial void OnQueryChanged(string value) => _ = SearchAsync();

    [RelayCommand]
    private async Task Toggle(SearchResultRow row)
    {
        if (row.IsFollowed)
            await _people.UnfollowAsync(row.Person.Id);
        else
            await _people.FollowAsync(row.Person, FollowReason.Favourite);

        await ReloadFollowedAsync();
        row.IsFollowed = _followed.Contains(row.Person.Id);
    }

    private async Task ReloadFollowedAsync()
    {
        var group = await _people.GetMyGroupAsync();
        _followed = group.Select(f => f.Person.Id).ToHashSet();
    }

    private Task SearchAsync() => LoadAsync(RunSearchAsync);

    private async Task RunSearchAsync()
    {
        // An empty query lists the district rather than showing a blank sheet.
        var matches = string.IsNullOrWhiteSpace(Query)
            ? await _people.SearchAsync("OK")
            : await _people.SearchAsync(Query);

        Results.Clear();

        foreach (var person in matches.Take(25))
        {
            Results.Add(new SearchResultRow
            {
                Person = person,
                Name = person.Name,
                Meta = $"{person.Club} · {person.DefaultClass}",
                IsFollowed = _followed.Contains(person.Id),
            });
        }

        IsEmpty = Results.Count == 0;
    }
}
