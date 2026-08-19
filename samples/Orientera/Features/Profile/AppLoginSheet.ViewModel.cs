using Orientera.Services.Eventor;

namespace Orientera.Features.Profile;

/// <summary>
/// Eventor-inloggning med appens egna fält, till skillnad från Eventors egen sida.
/// </summary>
/// <remarks>
/// Vägen in sedan #142, efter att ha vägts mot <see cref="EventorLoginSheet"/> på riktigt (#123).
/// Skillnaden är inte var lösenordet hamnar — båda vägarna sparar det i telefonens säkra lager och
/// skickar det till Eventors eget formulär — utan var det skrivs in. Två fält och en knapp i
/// stället för en helsida webb, och iOS erbjuder nyckelringen direkt i fälten.
///
/// Priset är betalt med öppna ögon: en löpare som lärt sig skriva sitt Eventor-lösenord i en app
/// som inte är Eventor har lärt sig den vana nätfiske lever på, och sidan som tar emot lösenordet
/// syns inte längre, så adressfältet kan inte kontrolleras.
///
/// Det som inte offras är utmaningen. Fälten loggar inte in — de skriver ned lösenordet och lämnar
/// över till Eventors egen sida, som fyller i och skickar. Den dagen förbundet lägger till
/// tvåfaktor står den sidan redan framme för att visa den.
/// </remarks>
public partial class AppLoginSheetViewModel(
    INavigationService _navigation,
    EventorCredentialStore _credentials) : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty] public partial bool IsWorking { get; set; }

    public bool CanSubmit =>
        !IsWorking && Username.Trim().Length > 0 && Password.Length > 0;

    /// <summary>
    /// Saves what was typed and hands over to Eventor's own form to actually log in.
    /// </summary>
    /// <remarks>
    /// The app never posts to <c>/Login</c> itself. What the runner typed here is filled into the
    /// federation's own page in a web view and submitted there, which is what survives the
    /// challenge in front of it — and what makes this sheet a different way of typing rather than
    /// a second implementation of logging in.
    /// </remarks>
    [RelayCommand]
    private async Task Submit()
    {
        if (!CanSubmit)
            return;

        IsWorking = true;

        await _credentials.SaveAsync(Username.Trim(), Password);

        // Not kept in the view model a moment longer than it takes to store it.
        Password = string.Empty;

        var session = await _navigation.NavigateToWithResultAsync<EventorLoginSheet, EventorLoginRequest, EventorWebSession>(
            new EventorLoginRequest(UseSavedPassword: true));

        IsWorking = false;

        await _navigation.ReturnAsync(session is { IsSuccess: true } ? session.Value : null!);
    }

    [RelayCommand]
    private async Task Cancel() => await _navigation.ReturnAsync(null!);
}
