using Orientera.Services.Eventor;
using Orientera.Services.Local;

namespace Orientera.Features.Profile;

/// <summary>
/// The user logs in to Eventor on Eventor's own page.
/// </summary>
/// <remarks>
/// A web view and not a form of our own, on purpose. The app never has a field for the password:
/// what is typed goes into Eventor's page, and the app only reads the cookies afterwards. That is
/// also what makes the feature defensible — everyone reads the subscription they pay for
/// themselves, instead of borrowing one member's (#123).
/// </remarks>
public partial class EventorLoginSheetViewModel(
    INavigationService _navigation,
    EventorSessionStore _sessions,
    EventorReader _eventor,
    EventorCredentialStore _credentials,
    LocalIdentityStore _identity) : ViewModelBase, IReceivesNavigationParameter<EventorLoginRequest>
{
    /// <summary>
    /// Whether the sheet should fill Eventor's form itself from what it remembers.
    /// </summary>
    /// <remarks>
    /// The sheet is still Eventor's own page and the POST is still Eventor's own form — the only
    /// difference is who types. That is what keeps the challenge on the page working, and what
    /// will keep working the day the federation adds a second factor: when the silent attempt
    /// cannot finish, the page is already open in front of the runner to finish by hand.
    /// </remarks>
    public bool WantsSilentLogin { get; private set; }

    public Task OnNavigationParameterAsync(EventorLoginRequest param)
    {
        WantsSilentLogin = param.UseSavedPassword;

        if (param.UseSavedPassword)
            Explanation = "Loggar in dig igen på Eventor. Fyll i själv om sidan frågar.";

        return Task.CompletedTask;
    }

    public Task<(string Username, string Password)?> CredentialsAsync() => _credentials.ReadAsync();

    public string LoginUrl => $"{EventorSite.Origin}/Login";

    /// <summary>
    /// Whether the page in front of the user is a logged-in one.
    /// </summary>
    /// <remarks>
    /// The greeting, not the ranking box. Waiting for the box would have left a member of a club
    /// without Sverigelistan logging in forever: that page never grows one, so the sheet would
    /// never close — while the thing they came to do, be recognised, had already worked.
    /// </remarks>
    public const string LoggedInScript = "(document.querySelector('.loggedInName')?.textContent || '')";

    [ObservableProperty]
    public partial string Explanation { get; set; } =
        "Logga in med ditt eget Eventor-konto. Kryssa i \"Kom ihåg mig\" så slipper du göra det igen.";

    /// <summary>
    /// Called after every navigation with whatever the page said about the reader. The sheet closes
    /// the moment Eventor greets somebody by name.
    /// </summary>
    public async Task OnPageAsync(
        string? greeting,
        Func<Task<IReadOnlyList<SessionCookie>>> cookies,
        Func<string, Task<string?>> typed)
    {
        if (Clean(greeting) is not { Length: > 0 })
            return;

        // Remembered only once the login has actually worked. Storing what was typed before
        // Eventor accepted it would save a wrong password and replay it forever.
        string username = Decode(await typed(EventorLoginForm.ReadRememberedUsernameScript));
        string password = Decode(await typed(EventorLoginForm.ReadRememberedPasswordScript));

        if (username.Length > 0 && password.Length > 0)
            await _credentials.SaveAsync(username, password);

        var captured = await cookies();

        if (captured.Count == 0)
            return;

        var session = new EventorWebSession
        {
            Cookies = captured,
            CapturedAt = DateTimeOffset.Now,
        };

        // Saved before it is used: the reader fetches with whatever session is stored, and what it
        // brings back is what turns a number into a name.
        _sessions.Save(session);
        _eventor.Clear();

        var account = await _eventor.ReadAccountAsync();

        session = session with { Account = account, PersonId = (await _eventor.StartPageAsync())?.PersonId };
        _sessions.Save(session);

        if (account is not null)
            Adopt(account);

        await _navigation.ReturnAsync(session);
    }

    /// <summary>
    /// The login says who the user is. Until now that was typed in by hand (#75), because there was
    /// no source for it; now there is one, and two answers to the same question can only disagree.
    /// The class is Eventor's suggestion and stays the user's to change — people enter classes other
    /// than the one they are ranked in.
    /// </summary>
    private void Adopt(EventorAccount account) =>
        _identity.Save(new LocalIdentity
        {
            Name = account.Name,
            Club = account.Club,
            DefaultClass = account.DefaultClass ?? _identity.Current?.DefaultClass ?? string.Empty,
        });

    /// <summary>
    /// What the web view hands back is a JavaScript value, and the platforms disagree about how
    /// much of its JSON quoting comes along with it.
    /// </summary>
    private static string Clean(string? value) =>
        value?.Trim().Trim('"').Trim() ?? string.Empty;

    /// <summary>
    /// A percent-encoded value from the web view, back to what was typed.
    /// </summary>
    /// <remarks>
    /// Encoded on the way out so that no quote, backslash or newline in a password has to survive
    /// the platforms' differing ideas of how a JavaScript string is rendered — see
    /// <see cref="EventorLoginForm.ReadRememberedUsernameScript"/>.
    /// </remarks>
    private static string Decode(string? value)
    {
        var cleaned = Clean(value);

        try
        {
            return Uri.UnescapeDataString(cleaned);
        }
        catch (UriFormatException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Closes without a session. <see cref="INavigationService.ReturnAsync"/> is for handing one
    /// back and throws on null; closing is what leaves the waiting caller cancelled (#146).
    /// </summary>
    [RelayCommand]
    private async Task Cancel() => await _navigation.BackAsync();
}
