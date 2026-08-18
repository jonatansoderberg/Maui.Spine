using System.Collections.ObjectModel;
using Orientera.Presentation;
using Orientera.Services.Sources;

namespace Orientera.Features.Events;

/// <summary>
/// What the picker needs: the classes to choose between, which one is already chosen, and what the
/// choice means where it was opened from — entering a competition and following a live list are
/// different questions.
/// </summary>
public sealed record ClassChoice(
    IReadOnlyList<string> Classes,
    string Explanation,
    string? Selected = null);

/// <summary>One row in the picker.</summary>
public sealed record ClassRow(string Name, bool IsSelected)
{
    public string Check => IsSelected ? "✓" : string.Empty;

    public string Accessibility => IsSelected ? $"{Name}, vald klass" : Name;
}

/// <summary>
/// The class picker, in the order a runner reads it.
/// </summary>
/// <remarks>
/// The list arrives in the organiser's order, which starts at D16 and counts down — so the chosen
/// class was neither marked nor near the top, and the runner's own class could be below the fold
/// (testkörningen, skärm 18 och 19). Two things fix that and neither is a sort by name: the list
/// splits into age classes and courses, because that is how a local competition is actually
/// divided, and inside each group the chosen class and the runner's own come first.
/// </remarks>
public partial class ChooseClassSheetViewModel(
    INavigationService _navigation,
    IPeopleSource _people) : ViewModelBase, IReceivesNavigationParameter<ClassChoice>
{
    private IReadOnlyList<string> _classes = [];
    private string? _selected;

    public ObservableCollection<ClassRow> AgeClasses { get; } = [];
    public ObservableCollection<ClassRow> Courses { get; } = [];

    [ObservableProperty] public partial string Explanation { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasAgeClasses { get; set; }

    [ObservableProperty] public partial bool HasCourses { get; set; }

    public Task OnNavigationParameterAsync(ClassChoice choice)
    {
        _classes = choice.Classes;
        _selected = choice.Selected;
        Explanation = choice.Explanation;

        return Task.CompletedTask;
    }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        var me = await _people.GetMeAsync();
        Build(me.DefaultClass);
    }

    private void Build(string myClass)
    {
        AgeClasses.Clear();
        Courses.Clear();

        foreach (var name in Order(_classes.Where(c => Format.IsAgeClass(c)), myClass))
            AgeClasses.Add(new ClassRow(name, name == _selected));

        foreach (var name in Order(_classes.Where(c => !Format.IsAgeClass(c)), myClass))
            Courses.Add(new ClassRow(name, name == _selected));

        HasAgeClasses = AgeClasses.Count > 0;
        HasCourses = Courses.Count > 0;
    }

    /// <summary>
    /// The chosen class first, then the runner's own, then the organiser's order untouched — it
    /// carries meaning that an alphabetical sort would throw away.
    /// </summary>
    private IEnumerable<string> Order(IEnumerable<string> names, string myClass) =>
        names.OrderBy(n => n == _selected ? 0 : n == myClass ? 1 : 2);

    [RelayCommand]
    private async Task Choose(string className) => await _navigation.ReturnAsync(className);
}
