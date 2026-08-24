using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Presentation;

namespace Orientera.Features.Events;

/// <summary>
/// What the sheet needs to open with: the filter as it stands, the calendar the district list is
/// drawn from, the competitions the active quick filter leaves — which is what the count on the
/// button counts — and the reader three of the rules are about.
/// </summary>
public sealed record FilterRequest(
    EventFilter Current,
    IReadOnlyList<Competition> Catalogue,
    IReadOnlyList<Competition> Matching,
    Person? Me,
    DateTimeOffset Now);

/// <summary>One chip in the sheet: a choice, and whether it is taken.</summary>
/// <remarks>
/// The toggle sits on the option rather than on the sheet, so the chip binds to itself. Reaching
/// the sheet's command through <c>RelativeSource AncestorType</c> from inside a bindable layout
/// resolved to nothing once already, and a chip that silently does not toggle is worse than one
/// line of command here.
/// </remarks>
public sealed partial class FilterOption : ObservableObject
{
    public required string Label { get; init; }

    /// <summary>A district name, a level, a discipline, a period, a radius — or null for "any".</summary>
    public required object? Value { get; init; }

    public required FilterOptionGroup Group { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Toggle() => Group.Toggle(this);
}

/// <summary>
/// A row of chips. Multi-select rows toggle; single-select rows move the selection, because one
/// window and one radius is all a filter can mean, and their first chip is the "any" that means
/// the row is unset.
/// </summary>
public sealed partial class FilterOptionGroup(bool single, string emptyLabel) : ObservableObject
{
    public ObservableCollection<FilterOption> Options { get; } = [];

    /// <summary>Raised after every tap, so the button can say what the filter would show.</summary>
    public Action? Changed { get; set; }

    /// <summary>
    /// What is chosen, for the collapsed header: "Mitt distrikt", "Mästerskap +2", or the row's
    /// own word for nothing chosen. This is what makes a closed group safe — the choices are out
    /// of sight, the choice never is.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = emptyLabel;

    public IEnumerable<FilterOption> Selected => Options.Where(o => o.IsSelected);

    public void Toggle(FilterOption option)
    {
        if (single)
        {
            foreach (var other in Options)
                other.IsSelected = ReferenceEquals(other, option);
        }
        else
        {
            option.IsSelected = !option.IsSelected;
        }

        Describe();
        Changed?.Invoke();
    }

    public void Reset()
    {
        for (int i = 0; i < Options.Count; i++)
            Options[i].IsSelected = single && i == 0;

        Describe();
    }

    public FilterOption Add(string label, object? value, bool isSelected = false)
    {
        var option = new FilterOption { Label = label, Value = value, Group = this, IsSelected = isSelected };
        Options.Add(option);
        Describe();
        return option;
    }

    /// <summary>
    /// The "any" chip in a single-select row carries a null value, and it is not a choice — it is
    /// the absence of one, and reads as the empty label rather than as its own word.
    /// </summary>
    public void Describe()
    {
        var chosen = Options.Where(o => o is { IsSelected: true, Value: not null }).Select(o => o.Label).ToList();

        Summary = chosen.Count switch
        {
            0 => emptyLabel,
            1 => chosen[0],
            2 => $"{chosen[0]}, {chosen[1]}",
            _ => $"{chosen[0]} +{chosen.Count - 1}",
        };
    }
}

/// <summary>
/// Advanced filters. Secondary decisions belong in a bottom sheet — the user never leaves the
/// competition list to make one.
/// </summary>
public partial class EventFilterSheetViewModel(INavigationService _navigation)
    : ViewModelBase, IReceivesNavigationParameter<FilterRequest>
{
    private IReadOnlyList<Competition> _matching = [];
    private Person? _me;
    private DateTimeOffset _now;
    private string _query = string.Empty;

    /// <summary>Every district there is something to see in, not every district in Sweden.</summary>
    public FilterOptionGroup DistrictGroup { get; } = new(single: false, "Alla distrikt");

    public FilterOptionGroup PeriodGroup { get; } = new(single: true, "Valfri tid");

    public FilterOptionGroup LevelGroup { get; } = new(single: false, "Alla nivåer");

    public FilterOptionGroup DisciplineGroup { get; } = new(single: false, "Alla discipliner");

    /// <summary>
    /// The radius as a distance rather than as five buttons. A distance is a continuous quantity
    /// and a slider says so; the chips could only ever offer the four someone had thought of.
    /// The far end is no limit at all, which is why the scale ends in a word and not a number.
    /// </summary>
    [ObservableProperty]
    public partial double DistanceKm { get; set; } = MaxDistance;

    /// <summary>Past this the filter stops asking — the slider is at "Alla".</summary>
    public const double MaxDistance = 200;

    public const double MinDistance = 5;

    partial void OnDistanceKmChanged(double value)
    {
        DistanceSummary = SelectedDistance is { } km ? $"Inom {Format.Distance(km)}" : "Valfritt avstånd";
        Recount();
    }

    /// <summary>
    /// The radius the filter gets, snapped: five-kilometre steps where five kilometres is a
    /// decision, ten where it is not. Null at the top of the scale.
    /// </summary>
    private double? SelectedDistance => DistanceKm >= MaxDistance
        ? null
        : DistanceKm < 100
            ? Math.Round(DistanceKm / 5) * 5
            : Math.Round(DistanceKm / 10) * 10;

    [ObservableProperty]
    public partial string DistanceSummary { get; set; } = "Valfritt avstånd";

    [ObservableProperty]
    public partial bool HasDistricts { get; set; }

    /// <summary>"Mitt distrikt är Gästrikland. Inget valt betyder alla."</summary>
    [ObservableProperty]
    public partial string DistrictHint { get; set; } = "Inget valt betyder alla distrikt.";

    /// <summary>
    /// What the primary button says. Eventor keeps its calendar visible behind the filter drawer;
    /// a sheet cannot, so the count comes to the button instead — and it is the only thing that
    /// catches a combination that would show nothing before the user commits to it.
    /// </summary>
    [ObservableProperty]
    public partial string ApplyLabel { get; set; } = "Visa tävlingar";

    [ObservableProperty]
    public partial bool ShowTraining { get; set; }

    [ObservableProperty]
    public partial bool OnlyMyClass { get; set; }

    [ObservableProperty]
    public partial bool OnlyRegisterable { get; set; }

    partial void OnShowTrainingChanged(bool value) => Recount();

    partial void OnOnlyMyClassChanged(bool value) => Recount();

    partial void OnOnlyRegisterableChanged(bool value) => Recount();

    /// <summary>
    /// The sheet opens showing what is set. Without this it opened blank over an active filter,
    /// and applying it silently cleared everything the user had chosen.
    /// </summary>
    public Task OnNavigationParameterAsync(FilterRequest request)
    {
        var filter = request.Current;

        _matching = request.Matching;
        _me = request.Me;
        _now = request.Now;
        _query = filter.Query;

        BuildDistricts(request, filter);

        PeriodGroup.Options.Clear();

        foreach (var period in Enum.GetValues<EventPeriod>())
            PeriodGroup.Add(EventFilter.PeriodLabel(period), period, filter.Period == period);

        LevelGroup.Options.Clear();

        foreach (var level in Enum.GetValues<CompetitionLevel>())
            LevelGroup.Add(Format.Level(level), level, filter.Levels.Contains(level));

        DisciplineGroup.Options.Clear();

        foreach (var discipline in Enum.GetValues<Discipline>())
            DisciplineGroup.Add(Format.Discipline(discipline), discipline, filter.Disciplines.Contains(discipline));

        DistanceKm = filter.MaxDistanceKm is { } radius
            ? Math.Clamp(radius, MinDistance, MaxDistance)
            : MaxDistance;

        ShowTraining = filter.ShowTraining;
        OnlyMyClass = filter.OnlyMyClass;
        OnlyRegisterable = filter.OnlyRegisterable;

        foreach (var group in Groups)
            group.Changed = Recount;

        Recount();

        return Task.CompletedTask;
    }

    /// <summary>
    /// The user's own district is lifted out of A–Ö and put first. Nine times in ten it is the
    /// answer, and in an alphabetical row it is wherever the alphabet happens to have left it —
    /// Östergötland is behind twenty chips of horizontal scrolling.
    /// </summary>
    private void BuildDistricts(FilterRequest request, EventFilter filter)
    {
        var districts = request.Catalogue
            .Select(c => c.District)
            .Where(d => d.Length > 0)
            .Distinct()
            .OrderBy(d => d, StringComparer.CurrentCulture)
            .ToList();

        string? mine = request.Me?.District is { Length: > 0 } d && districts.Contains(d) ? d : null;

        DistrictGroup.Options.Clear();

        if (mine is not null)
            DistrictGroup.Add("Mitt distrikt", mine, filter.Districts.Contains(mine));

        foreach (var district in districts.Where(district => district != mine))
            DistrictGroup.Add(district, district, filter.Districts.Contains(district));

        HasDistricts = DistrictGroup.Options.Count > 0;

        // The chip says "Mitt distrikt" and not which, so the line under the heading does.
        DistrictHint = mine is null
            ? "Inget valt betyder alla distrikt."
            : $"Mitt distrikt är {mine}. Inget valt betyder alla.";
    }

    private IEnumerable<FilterOptionGroup> Groups =>
        [DistrictGroup, PeriodGroup, LevelGroup, DisciplineGroup];

    private void Recount()
    {
        // Nothing to count against: the identity or the calendar has not arrived yet. Saying
        // "Inget matchar filtret" over an empty catalogue is a lie, and it disables the button
        // that closes the sheet — which is how opening the filter during the first load left it
        // with no way out.
        if (_me is null || _matching.Count == 0)
        {
            ApplyLabel = "Visa tävlingar";
            return;
        }

        var filter = Build();
        int count = _matching.Count(c => filter.Includes(c, _me, _now));

        // Enabled even at zero. The empty list is a designed state that explains itself, and a
        // disabled primary button on the only way out of the sheet is the dead end ChipView
        // already documents: a control that looks like the working one beside it and is not.
        ApplyLabel = count switch
        {
            0 => "Inget matchar filtret",
            1 => "Visa 1 tävling",
            _ => $"Visa {count} tävlingar",
        };
    }

    [RelayCommand]
    private async Task Apply() => await _navigation.ReturnAsync(Build());

    [RelayCommand]
    private async Task Clear()
    {
        foreach (var group in Groups)
            group.Reset();

        DistanceKm = MaxDistance;
        ShowTraining = false;
        OnlyMyClass = false;
        OnlyRegisterable = false;

        await _navigation.ReturnAsync(EventFilter.Default);
    }

    private EventFilter Build() => new()
    {
        Districts = DistrictGroup.Selected.Select(o => (string)o.Value!).ToHashSet(),
        Query = _query,
        Period = PeriodGroup.Selected.Select(o => (EventPeriod)o.Value!).FirstOrDefault(),
        Levels = LevelGroup.Selected.Select(o => (CompetitionLevel)o.Value!).ToHashSet(),
        Disciplines = DisciplineGroup.Selected.Select(o => (Discipline)o.Value!).ToHashSet(),
        MaxDistanceKm = SelectedDistance,
        ShowTraining = ShowTraining,
        OnlyMyClass = OnlyMyClass,
        OnlyRegisterable = OnlyRegisterable,
    };
}
