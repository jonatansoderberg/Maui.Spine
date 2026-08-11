using System.Collections.ObjectModel;

namespace Orientera.Features.Live;

/// <summary>One competition to choose between, with enough beside the name to tell two apart.</summary>
public sealed record CompetitionOption(string Id, string Title, string Subtitle)
{
    public string Accessibility => $"{Title}, {Subtitle}";
}

public sealed record CompetitionChoice(IReadOnlyList<CompetitionOption> Options, string Explanation);

/// <summary>
/// Which of the competitions running right now the live tab should show. The same mechanic as
/// the class picker beside it — tapping the name asks, rather than cycling through.
/// </summary>
public partial class ChooseCompetitionSheetViewModel(INavigationService _navigation)
    : ViewModelBase, IReceivesNavigationParameter<CompetitionChoice>
{
    public ObservableCollection<CompetitionOption> Options { get; } = [];

    [ObservableProperty] public partial string Explanation { get; set; } = string.Empty;

    public Task OnNavigationParameterAsync(CompetitionChoice choice)
    {
        Options.Clear();

        foreach (var option in choice.Options)
            Options.Add(option);

        Explanation = choice.Explanation;

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Choose(CompetitionOption option) => await _navigation.ReturnAsync(option.Id);
}
