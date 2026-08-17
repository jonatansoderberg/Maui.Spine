using System.Text.Json;

namespace Orientera.Services.Eventor;

/// <summary>Where Eventor is, so the login page and the fetches cannot disagree about it.</summary>
public static class EventorSite
{
    public const string Origin = "https://eventor.orientering.se";

    /// <summary>
    /// Eventor's own entry form for a competition.
    /// </summary>
    /// <remarks>
    /// Entering a race is Eventor's business, not the app's: it takes payment details, club
    /// membership and rules the app has no copy of. What the app can do is stop lying about it —
    /// a button that says "Anmäl dig" and opens a class picker leaves the runner believing they
    /// have entered. Measured on the live site: the event page links to <c>/Entry?eventId=…</c>.
    /// </remarks>
    public static string EntryUrl(string eventId) =>
        $"{Origin}/Entry?eventId={Uri.EscapeDataString(eventId)}";
}

/// <summary>One cookie as the platform's web view holds it.</summary>
public sealed record SessionCookie(string Name, string Value, DateTimeOffset? ExpiresAt)
{
    /// <summary>
    /// The domain the web view files it under, where the platform says. Null on Android, whose
    /// <c>CookieManager</c> answers with a header and no metadata, and on sessions captured before
    /// #123 measured this.
    /// </summary>
    /// <remarks>
    /// Kept because "which domain" is the whole open question about "kom ihåg mig": a login cookie
    /// on <c>.orientering.se</c> and one on <c>eventor.orientering.se</c> are told apart by nothing
    /// else. Only the name, the domain and the expiry are ever written down or logged — a cookie's
    /// value is the login itself.
    /// </remarks>
    public string? Domain { get; init; }
}

/// <summary>
/// Who Eventor says the session belongs to.
/// </summary>
/// <remarks>
/// Read once, at login, and kept because it is what the app then calls the user: a name and a club
/// instead of a number. The class is Eventor's "Förvald klass 1" and is a suggestion — the runner
/// may enter another one, and the app lets them say so.
/// </remarks>
public sealed record EventorAccount
{
    public required string Name { get; init; }
    public required string Club { get; init; }
    public string? ClubId { get; init; }
    public string? DefaultClass { get; init; }
}

/// <summary>
/// The Eventor session the user logged in with, on this phone.
/// </summary>
/// <remarks>
/// The whole point of reading the ranking with the user's own login is that everyone sees what they
/// themselves pay for. That only works if the session belongs to the phone and never leaves it —
/// so this is stored locally and is never sent to the backend.
/// </remarks>
public sealed record EventorWebSession
{
    public required IReadOnlyList<SessionCookie> Cookies { get; init; }

    /// <summary>
    /// The runner's own Eventor id, read off the start page after login. Absent for a member of a
    /// club without Sverigelistan: only the ranking box carries the id, and they have no box.
    /// </summary>
    public string? PersonId { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>Name, club and class as Eventor states them. Absent on sessions captured before #123.</summary>
    public EventorAccount? Account { get; init; }

    /// <summary>The header the on-device fetches send.</summary>
    public string Header => string.Join("; ", Cookies.Select(c => $"{c.Name}={c.Value}"));

    /// <summary>Eventor's own login cookie. Nothing else says anything about the session.</summary>
    public const string LoginCookie = "ASP.NET_SessionId";

    /// <summary>
    /// When the login runs out, which so far is never: Eventor issues a session cookie with no
    /// expiry, and it dies when the server forgets it rather than on a date.
    /// </summary>
    /// <remarks>
    /// Read off the login cookie by name. The first attempt took the longest-lived cookie that was
    /// not a known tracker, which made the app promise "giltig till 16 sep 2027" — an advertising
    /// cookie's date. Excluding trackers by name was the wrong shape: measured twice on #123, the
    /// jar carried fourteen and then twenty cookies, and the second run brought Google's
    /// <c>_ga</c>, <c>__gads</c>, <c>__gpi</c> and <c>__eoi</c> on <c>.orientering.se</c>, which the
    /// list did not know. The promise came back, off a different cookie. A list of everyone else's
    /// cookies is never finished; the login has one name.
    ///
    /// Naming it is only safe because "kom ihåg mig" was finally measured, ticked, against the
    /// widened <c>orientering.se</c> filter: it adds no persistent cookie at all. If Eventor ever
    /// starts issuing one, this is the line that has to learn its name.
    /// </remarks>
    public DateTimeOffset? ExpiresAt =>
        Cookies.FirstOrDefault(c => c.Name.Equals(LoginCookie, StringComparison.OrdinalIgnoreCase))?.ExpiresAt;
}

/// <summary>The captured session, on this phone only.</summary>
public sealed class EventorSessionStore(string _path)
{
    public EventorWebSession? Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<EventorWebSession>(File.ReadAllText(_path))
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    public void Save(EventorWebSession session)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(session));
        }
        catch (IOException)
        {
            // A session that cannot be written is one the user logs in for again. Not fatal.
        }
    }

    public void Forget()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }
}
