using Orientera.Domain;
using Orientera.Presentation;

namespace Orientera.Features.Events;

/// <summary>
/// Advanced filters. Secondary decisions belong in a bottom sheet — the user never leaves the
/// competition list to make one.
/// </summary>
public partial class EventFilterSheetViewModel(INavigationService _navigation) : ViewModelBase
{
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
        ShowTraining = false;
        OnlyMyClass = false;
        OnlyRegisterable = false;

        await _navigation.ReturnAsync(EventFilter.Default);
    }

    private EventFilter Build() => new()
    {
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
