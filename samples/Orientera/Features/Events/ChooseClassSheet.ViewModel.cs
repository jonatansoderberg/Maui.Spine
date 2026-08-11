using System.Collections.ObjectModel;
using Orientera.Services.Sources;

namespace Orientera.Features.Events;

/// <summary>
/// What the picker needs: the classes to choose between, and what the choice means where it was
/// opened from — entering a competition and following a live list are different questions.
/// </summary>
public sealed record ClassChoice(IReadOnlyList<string> Classes, string Explanation);

public partial class ChooseClassSheetViewModel(
    INavigationService _navigation,
    IPeopleSource _people) : ViewModelBase, IReceivesNavigationParameter<ClassChoice>
{
    public ObservableCollection<string> Classes { get; } = [];

    [ObservableProperty]
    public partial string Explanation { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MyClass { get; set; } = string.Empty;

    public Task OnNavigationParameterAsync(ClassChoice choice)
    {
        Classes.Clear();

        foreach (var className in choice.Classes)
            Classes.Add(className);

        Explanation = choice.Explanation;

        return Task.CompletedTask;
    }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        var me = await _people.GetMeAsync();
        MyClass = me.DefaultClass;
    }

    [RelayCommand]
    private async Task Choose(string className) => await _navigation.ReturnAsync(className);
}
