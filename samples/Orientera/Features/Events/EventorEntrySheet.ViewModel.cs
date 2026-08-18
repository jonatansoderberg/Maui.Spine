using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Eventor;

namespace Orientera.Features.Events;

/// <summary>Which competition to enter, and in which class the app said it would.</summary>
public sealed record EventorEntry(CompetitionId Competition, string ClassName);

public partial class EventorEntrySheetViewModel(
    INavigationService _navigation,
    EventorReader _eventor) : OrienteraViewModel, IReceivesNavigationParameter<EventorEntry>
{
    [ObservableProperty] public partial string EntryUrl { get; set; } = EventorSite.Origin;

    /// <summary>The class picked in the app, for the script that tries to preselect it.</summary>
    public string ClassName { get; private set; } = string.Empty;

    /// <summary>
    /// What the header says. It claimed the runner was signed in whatever the truth was, so
    /// without a session the app promised one thing and Eventor answered "Du behöver vara inloggad
    /// för att anmäla dig" directly underneath.
    /// </summary>
    [ObservableProperty] public partial string SessionText { get; set; } = string.Empty;

    public Task OnNavigationParameterAsync(EventorEntry entry)
    {
        EntryUrl = EventorSite.EntryUrl(entry.Competition.Value);
        ClassName = entry.ClassName;

        return Task.CompletedTask;
    }

    /// <summary>
    /// The header is written from what Eventor answers, not from whether a session file exists.
    /// A stored session Eventor has forgotten looks exactly like a working one from here, and the
    /// old text said "Du är redan inloggad här" above Eventor's own "Du behöver vara inloggad för
    /// att anmäla dig".
    /// </summary>
    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        await base.OnAppearingAsync(navigationDirection);

        SessionText = await _eventor.AccessAsync() switch
        {
            EventorAccess.NoSession =>
                "Anmälan sker hos Eventor och kräver att du är inloggad. Logga in under Jag först.",
            EventorAccess.Expired =>
                "Eventor känner inte längre igen inloggningen. Logga in igen under Jag.",
            EventorAccess.Unreachable =>
                "Anmälan sker hos Eventor. Ingen kontakt med Eventor just nu.",
            _ => "Anmälan sker hos Eventor. Du är inloggad här.",
        };
    }

    [RelayCommand]
    private async Task Close() => await _navigation.BackAsync();
}
