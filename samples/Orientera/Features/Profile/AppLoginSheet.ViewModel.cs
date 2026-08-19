using Orientera.Services.Eventor;

namespace Orientera.Features.Profile;

/// <summary>
/// Eventor-inloggning med appens egna fält, till skillnad från Eventors egen sida.
/// </summary>
/// <remarks>
/// Byggd för att utvärderas mot <see cref="EventorLoginSheet"/>, inte för att ersätta den utan
/// vidare. Skillnaden är inte var lösenordet hamnar — båda vägarna sparar det i telefonens
/// säkra lager och skickar det till Eventors eget formulär — utan var det skrivs in.
///
/// Det som talar för: två fält och en knapp i stället för en helsida webb, och iOS erbjuder
/// nyckelringen direkt i fälten.
///
/// Det som talar emot, och som är värt att väga: en runa som lärt sig skriva sitt Eventor-lösenord
/// i en app som inte är Eventor har lärt sig fel vana, och det är vanan nätfiske lever på. Sidan
/// som tar emot lösenordet syns inte längre, så adressfältet kan inte kontrolleras. Och den dag
/// förbundet lägger till tvåfaktor eller en utmaning som kräver interaktion har de här fälten
/// ingenstans att visa den — Eventors egen sida har det.
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

        // A sheet that has nothing to hand back closes rather than returns. ReturnAsync is
        // contracted to deliver a result and throws on null, and the throw lands in a command
        // where nothing catches it — the sheet animated away and then the app was gone (#146).
        if (session is { IsSuccess: true, Value: { } captured })
            await _navigation.ReturnAsync(captured);
        else
            await _navigation.BackAsync();
    }

    [RelayCommand]
    private async Task Cancel() => await _navigation.BackAsync();
}
