using Orientera.Services.Eventor;

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
    EventorSessionStore _sessions) : ViewModelBase
{
    public string LoginUrl => $"{EventorCookies.Origin}/Login";

    /// <summary>Reads the runner's own id off the start page. Absent means not logged in.</summary>
    public const string PersonIdScript =
        "(document.querySelector('.rankingStartPageBox a[href*=\"/Ranking/ol/Runner/Index/\"]')" +
        "?.getAttribute('href') || '')";

    [ObservableProperty]
    public partial string Explanation { get; set; } =
        "Logga in med ditt eget Eventor-konto. Kryssa i \"Kom ihåg mig\" så slipper du göra det igen.";

    /// <summary>
    /// Called after every navigation with whatever the page said about the logged-in runner. The
    /// sheet closes the moment Eventor shows a page that names one.
    /// </summary>
    public async Task OnPageAsync(string? personLink, Func<Task<IReadOnlyList<SessionCookie>>> cookies)
    {
        if (personLink is not { Length: > 0 }
            || System.Text.RegularExpressions.Regex.Match(personLink, @"(\d+)") is not { Success: true } id)
        {
            return;
        }

        var captured = await cookies();

        if (captured.Count == 0)
            return;

        var session = new EventorWebSession
        {
            Cookies = captured,
            PersonId = id.Groups[1].Value,
            CapturedAt = DateTimeOffset.Now,
        };

        _sessions.Save(session);

        await _navigation.ReturnAsync(session);
    }

    [RelayCommand]
    private async Task Cancel() => await _navigation.ReturnAsync(null!);
}
