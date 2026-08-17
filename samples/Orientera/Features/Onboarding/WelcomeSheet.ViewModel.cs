namespace Orientera.Features.Onboarding;

/// <summary>What the reader answered the welcome with.</summary>
public sealed record WelcomeChoice(bool WantsLogin);

/// <summary>
/// "Logga in på Eventor, så vet appen vem du är" — or look around first.
/// </summary>
/// <remarks>
/// Skipping is a real option and is worded as one. The app works without an account: the calendar,
/// the results and the live lists are all there. What the login adds is knowing which of those rows
/// are yours, and Sverigelistan, which is behind a fee that is nobody's business but the runner's.
/// </remarks>
public partial class WelcomeSheetViewModel(INavigationService _navigation) : ViewModelBase
{
    public string Explanation =>
        "Loggar du in på Eventor vet appen vem du är: dina anmälningar, din klubbs aktiviteter och "
        + "din plats på Sverigelistan. Du loggar in på Eventors egen sida, och uppgifterna sparas i\n"
        + "telefonens säkra lager så att du slipper göra om det — aldrig på någon server.";

    public string SkipExplanation =>
        "Du kan titta runt först. Tävlingskalendern och resultaten finns utan inloggning.";

    [RelayCommand]
    private async Task Login() => await _navigation.ReturnAsync(new WelcomeChoice(WantsLogin: true));

    [RelayCommand]
    private async Task Skip() => await _navigation.ReturnAsync(new WelcomeChoice(WantsLogin: false));
}
