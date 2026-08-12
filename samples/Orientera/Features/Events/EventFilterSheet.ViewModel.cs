using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Presentation;

namespace Orientera.Features.Events;

/// <summary>
/// What the sheet needs to open with: the filter as it stands, and the districts there are
/// actually competitions in.
/// </summary>
public sealed record FilterRequest(EventFilter Current, IReadOnlyList<string> Districts);

/// <summary>One district, and whether it is kept.</summary>
/// <remarks>
/// The toggle sits on the row rather than on the sheet, so the chip binds to itself. Reaching the
/// sheet's command through <c>RelativeSource AncestorType</c> from inside a bindable layout in a
/// FlexLayout resolved to nothing, and a chip that silently does not toggle is worse than one line
/// of command here.
/// </remarks>
public sealed partial class DistrictOption : ObservableObject
{
    public required string Name { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Toggle() => IsSelected = !IsSelected;
}

/// <summary>
/// Advanced filters. Secondary decisions belong in a bottom sheet — the user never leaves the
/// competition list to make one.
/// </summary>
public partial class EventFilterSheetViewModel(INavigationService _navigation)
    : ViewModelBase, IReceivesNavigationParameter<FilterRequest>
{
    public IReadOnlyList<string> PeriodOptions { get; } =
        ["Valfri tid", "Denna månad", "Nästa månad", "Inom tre månader", "Resten av året"];

    /// <summary>Every district there is something to see in, not every district in Sweden.</summary>
    public ObservableCollection<DistrictOption> Districts { get; } = [];

    [ObservableProperty]
    public partial bool HasDistricts { get; set; }

    [ObservableProperty]
    public partial int SelectedPeriod { get; set; }

    /// <summary>
    /// The sheet opens showing what is set. Without this it opened blank over an active filter,
    /// and applying it silently cleared everything the user had chosen.
    /// </summary>
    public Task OnNavigationParameterAsync(FilterRequest request)
    {
        var filter = request.Current;

        Districts.Clear();

        foreach (var district in request.Districts)
        {
            Districts.Add(new DistrictOption
            {
                Name = district,
                IsSelected = filter.Districts.Contains(district),
            });
        }

        HasDistricts = Districts.Count > 0;
        SelectedPeriod = (int)filter.Period;

        SelectedLevel = filter.MinimumLevel switch
        {
            CompetitionLevel.Championship => 1,
            CompetitionLevel.National => 2,
            CompetitionLevel.District => 3,
            _ => 0,
        };

        SelectedDiscipline = filter.Discipline switch
        {
            Domain.Discipline.Sprint => 1,
            Domain.Discipline.Middle => 2,
            Domain.Discipline.Long => 3,
            Domain.Discipline.Night => 4,
            _ => 0,
        };

        SelectedDistance = filter.MaxDistanceKm switch
        {
            25 => 1,
            50 => 2,
            100 => 3,
            _ => 0,
        };

        ShowTraining = filter.ShowTraining;
        OnlyMyClass = filter.OnlyMyClass;
        OnlyRegisterable = filter.OnlyRegisterable;

        return Task.CompletedTask;
    }


    public IReadOnlyList<string> LevelOptions { get; } =
        ["Alla nivåer", "Mästerskap", "Nationell och uppåt", "Distrikt och uppåt"];

    public IReadOnlyList<string> DisciplineOptions { get; } =
        ["Alla discipliner", "Sprint", "Medel", "Lång", "Natt"];

    public IReadOnlyList<string> DistanceOptions { get; } =
        ["Valfritt avstånd", "Inom 25 km", "Inom 50 km", "Inom 100 km"];

    [ObservableProperty]
    public partial int SelectedLevel { get; set; }

    [ObservableProperty]
    public partial int SelectedDiscipline { get; set; }

    [ObservableProperty]
    public partial int SelectedDistance { get; set; }

    [ObservableProperty]
    public partial bool ShowTraining { get; set; }

    [ObservableProperty]
    public partial bool OnlyMyClass { get; set; }

    [ObservableProperty]
    public partial bool OnlyRegisterable { get; set; }

    [RelayCommand]
    private async Task Apply() => await _navigation.ReturnAsync(Build());

    [RelayCommand]
    private async Task Clear()
    {
        SelectedLevel = 0;
        SelectedDiscipline = 0;
        SelectedDistance = 0;
        SelectedPeriod = 0;
        ShowTraining = false;

        foreach (var district in Districts)
            district.IsSelected = false;
        OnlyMyClass = false;
        OnlyRegisterable = false;

        await _navigation.ReturnAsync(EventFilter.Default);
    }

    private EventFilter Build() => new()
    {
        Districts = Districts.Where(d => d.IsSelected).Select(d => d.Name).ToHashSet(),
        Period = (EventPeriod)SelectedPeriod,
        MinimumLevel = SelectedLevel switch
        {
            1 => CompetitionLevel.Championship,
            2 => CompetitionLevel.National,
            3 => CompetitionLevel.District,
            _ => null,
        },
        Discipline = SelectedDiscipline switch
        {
            1 => Domain.Discipline.Sprint,
            2 => Domain.Discipline.Middle,
            3 => Domain.Discipline.Long,
            4 => Domain.Discipline.Night,
            _ => null,
        },
        MaxDistanceKm = SelectedDistance switch
        {
            1 => 25,
            2 => 50,
            3 => 100,
            _ => null,
        },
        ShowTraining = ShowTraining,
        OnlyMyClass = OnlyMyClass,
        OnlyRegisterable = OnlyRegisterable,
    };
}
