using Orientera.Controls;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Local;

namespace Orientera.Features.Onboarding;

/// <summary>
/// "Vilka grenar håller du på med?", asked once, on the way in.
/// </summary>
/// <remarks>
/// Asked rather than assumed. Defaulting everyone to foot orienteering would empty the calendar
/// of somebody who rides, without a word about why; defaulting to all of them leaves MTBO in the
/// list of the nine in ten who do not. A question with foot already ticked costs one tap for
/// almost everybody and is the only answer that is right for the rest.
/// </remarks>
public partial class SportChoiceSheetViewModel(
    INavigationService _navigation,
    RacePreferenceStore _store) : ViewModelBase
{
    public ChipGroup SportGroup { get; } = new(single: false, "Alla grenar");

    public string Explanation =>
        "Kalendern visar bara de grenar du väljer. Du kan ändra det när som helst under Jag.";

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (SportGroup.Options.Count > 0)
            return Task.CompletedTask;

        var saved = _store.Load().Sports;

        foreach (var sport in Enum.GetValues<Sport>())
        {
            // Orienteringslöpning förkryssat: det nio av tio svarar, och det svaret ska kosta
            // ett tryck på "Fortsätt" och inget mer.
            bool chosen = saved.Count > 0 ? saved.Contains(sport) : sport == Sport.Foot;

            SportGroup.Add(Format.SportOrDefault(sport), sport, chosen);
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Continue()
    {
        var chosen = SportGroup.Selected.Select(o => (Sport)o.Value!).ToHashSet();

        // Nothing ticked is not an answer, and saving it would hide the whole calendar. It is
        // read as "all of them", which is what the store's empty set already means.
        _store.Save(_store.Load() with { Sports = chosen });

        await _navigation.BackAsync();
    }
}
