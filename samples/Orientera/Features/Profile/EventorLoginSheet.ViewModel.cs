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
    LocalIdentityStore _identity) : ViewModelBase
{
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
    public async Task OnPageAsync(string? greeting, Func<Task<IReadOnlyList<SessionCookie>>> cookies)
    {
        if (Clean(greeting) is not { Length: > 0 })
            return;

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

    [RelayCommand]
    private async Task Cancel() => await _navigation.ReturnAsync(null!);
}
