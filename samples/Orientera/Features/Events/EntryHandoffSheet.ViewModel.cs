namespace Orientera.Features.Events;

/// <summary>What the landing needs to say: which competition, and in which class.</summary>
public sealed record EntryHandoff(string Competition, string ClassName);

public partial class EntryHandoffSheetViewModel(INavigationService _navigation)
    : ViewModelBase, IReceivesNavigationParameter<EntryHandoff>
{
    [ObservableProperty] public partial string Competition { get; set; } = string.Empty;

    [ObservableProperty] public partial string ClassLine { get; set; } = string.Empty;

    public Task OnNavigationParameterAsync(EntryHandoff handoff)
    {
        Competition = handoff.Competition;

        // The class is the one thing the runner picked in the app that has to survive the trip.
        // Saying it here is also how they find out it did not, if it did not.
        ClassLine = string.IsNullOrWhiteSpace(handoff.ClassName)
            ? "Ingen klass vald — du väljer den i formuläret"
            : $"Klass {handoff.ClassName}";

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Continue() => await _navigation.ReturnAsync(true);

    [RelayCommand]
    private async Task Cancel() => await _navigation.ReturnAsync(false);
}
