using System.Collections.ObjectModel;
using Orientera.Controls;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Local;

namespace Orientera.Features.Profile;

/// <summary>One kind of race on the reader's own list, with where it sits on it.</summary>
public sealed partial class FavouriteRow : ObservableObject
{
    public required RacePreference Preference { get; init; }

    public required string Label { get; init; }

    /// <summary>Its place, from one. The number is the point of the list.</summary>
    [ObservableProperty]
    public partial int Position { get; set; }

    [ObservableProperty]
    public partial bool CanMoveUp { get; set; }

    [ObservableProperty]
    public partial bool CanMoveDown { get; set; }

    public string Accessibility => $"{Label}, plats {Position}";
}

/// <summary>
/// Which sports the runner does, and which races they would rather be at.
/// </summary>
/// <remarks>
/// Two settings that look alike and are not. The sports are a fact — somebody who does not own a
/// bike will not own one next week, and MTBO has no business in their calendar at all. The
/// favourites are a taste, and a taste never hides anything: the races still appear, further down.
/// </remarks>
public partial class RacePreferenceSheetViewModel(
    INavigationService _navigation,
    RacePreferenceStore _store) : ViewModelBase
{
    /// <summary>The sports, all six, with the ones the runner does turned on.</summary>
    public ChipGroup SportGroup { get; } = new(single: false, "Alla grenar");

    /// <summary>
    /// Every kind of race the chosen sports can produce, as chips to add and remove.
    /// </summary>
    /// <remarks>
    /// Built from the sports that are on, not from all six: one sport gives six chips and a grid
    /// anyone can read, where thirty-six would be a wall. The list shrinks when a sport is turned
    /// off, and anything on the favourites list from that sport goes with it — keeping a
    /// favourite MTBO sprint for somebody who has just said they do not ride is keeping a
    /// preference that can never apply.
    /// </remarks>
    public ChipGroup PairGroup { get; } = new(single: false, string.Empty);

    /// <summary>The chosen kinds, best first.</summary>
    public ObservableCollection<FavouriteRow> Favourites { get; } = [];

    [ObservableProperty]
    public partial bool HasFavourites { get; set; }

    /// <summary>
    /// The other half, as its own property. The app has no value converters — view models hand
    /// XAML finished answers — and a negation is a finished answer.
    /// </summary>
    [ObservableProperty]
    public partial bool HasNoFavourites { get; set; } = true;

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        var saved = _store.Load();

        SportGroup.Options.Clear();

        foreach (var sport in Enum.GetValues<Sport>())
            SportGroup.Add(Format.SportOrDefault(sport), sport, saved.Sports.Contains(sport));

        SportGroup.Changed = OnSportsChanged;
        PairGroup.Changed = OnPairsChanged;

        Favourites.Clear();

        foreach (var favourite in saved.Favourites)
            Favourites.Add(Row(favourite));

        BuildPairs();
        Renumber();

        return Task.CompletedTask;
    }

    private IReadOnlySet<Sport> ChosenSports =>
        SportGroup.Selected.Select(o => (Sport)o.Value!).ToHashSet();

    /// <summary>
    /// The sports a pair list can be built from. No sport chosen means every sport is allowed,
    /// and a grid of thirty-six is not a list of choices — so the pairs offer foot orienteering,
    /// which is what "I have not said" means in practice.
    /// </summary>
    private IReadOnlyList<Sport> PairSports =>
        ChosenSports is { Count: > 0 } chosen ? [.. chosen.OrderBy(s => s)] : [Sport.Foot];

    private void OnSportsChanged()
    {
        // A favourite whose sport has just been switched off cannot ever apply again.
        var allowed = ChosenSports;

        if (allowed.Count > 0)
        {
            foreach (var row in Favourites.Where(r => !allowed.Contains(r.Preference.Sport)).ToList())
                Favourites.Remove(row);
        }

        BuildPairs();
        Renumber();
        Persist();
    }

    private void OnPairsChanged()
    {
        var chosen = PairGroup.Selected.Select(o => (RacePreference)o.Value!).ToHashSet();

        foreach (var row in Favourites.Where(r => !chosen.Contains(r.Preference)).ToList())
            Favourites.Remove(row);

        // Appended, not inserted: a new choice is the least favourite until it is moved, and
        // silently placing it first would reorder a list the reader had arranged.
        foreach (var preference in chosen.Where(p => Favourites.All(r => r.Preference != p)))
            Favourites.Add(Row(preference));

        Renumber();
        Persist();
    }

    private void BuildPairs()
    {
        PairGroup.Options.Clear();

        var sports = PairSports;

        foreach (var sport in sports)
        {
            foreach (var discipline in Enum.GetValues<Discipline>())
            {
                var preference = new RacePreference(sport, discipline);

                PairGroup.Add(
                    Label(preference, sports.Count > 1),
                    preference,
                    Favourites.Any(r => r.Preference == preference));
            }
        }
    }

    /// <summary>
    /// "Medel" where there is only one sport to confuse it with, "Indoor sprint" where there is
    /// more than one. The word for a race is its distance; the sport is what has to be said only
    /// when it is in question.
    /// </summary>
    private static string Label(RacePreference preference, bool withSport)
    {
        string distance = Format.Discipline(preference.Discipline);

        if (!withSport || preference.Sport == Sport.Foot)
            return distance;

        return $"{Format.Sport(preference.Sport)} {distance.ToLower(Format.Culture)}";
    }

    private FavouriteRow Row(RacePreference preference) => new()
    {
        Preference = preference,
        Label = Label(preference, withSport: true),
    };

    private void Renumber()
    {
        for (int i = 0; i < Favourites.Count; i++)
        {
            Favourites[i].Position = i + 1;
            Favourites[i].CanMoveUp = i > 0;
            Favourites[i].CanMoveDown = i < Favourites.Count - 1;
        }

        HasFavourites = Favourites.Count > 0;
        HasNoFavourites = !HasFavourites;
    }

    private void Persist() =>
        _store.Save(new RacePreferences(ChosenSports, [.. Favourites.Select(r => r.Preference)]));

    [RelayCommand]
    private void MoveUp(FavouriteRow row) => Move(row, -1);

    [RelayCommand]
    private void MoveDown(FavouriteRow row) => Move(row, +1);

    private void Move(FavouriteRow row, int step)
    {
        int from = Favourites.IndexOf(row);
        int to = from + step;

        if (from < 0 || to < 0 || to >= Favourites.Count)
            return;

        Favourites.Move(from, to);
        Renumber();
        Persist();
    }

    [RelayCommand]
    private async Task Done() => await _navigation.BackAsync();
}
