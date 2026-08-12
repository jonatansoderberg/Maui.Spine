using System.Text.Json;

namespace Orientera.Services.Eventor;

/// <summary>Where Eventor is, so the login page and the fetches cannot disagree about it.</summary>
public static class EventorSite
{
    public const string Origin = "https://eventor.orientering.se";
}

/// <summary>One cookie as the platform's web view holds it.</summary>
public sealed record SessionCookie(string Name, string Value, DateTimeOffset? ExpiresAt);

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

    /// <summary>
    /// When Eventor's own login cookie runs out, if it has an expiry at all.
    /// </summary>
    /// <remarks>
    /// Only Eventor's cookies count. Measured on #123: taking the longest-lived cookie of any kind
    /// made the app report "giltig till 16 sep 2027", which was an advertising cookie's date — the
    /// login itself was a session cookie with no expiry at all. A number read off the wrong cookie
    /// is worse than no number, because it is believed.
    /// </remarks>
    public DateTimeOffset? ExpiresAt
    {
        get
        {
            var dated = Cookies
                .Where(c => !IsTracking(c.Name))
                .Select(c => c.ExpiresAt)
                .OfType<DateTimeOffset>()
                .ToList();

            return dated.Count > 0 ? dated.Max() : null;
        }
    }

    /// <summary>
    /// The advertising and analytics cookies Eventor's pages set alongside the login, by name and
    /// by prefix. Excluding the known ones rather than listing the login by name is deliberate:
    /// what "kom ihåg mig" adds is not measured yet, and a cookie nobody has identified should
    /// count as possibly Eventor's rather than be quietly dropped.
    /// </summary>
    private static bool IsTracking(string name) =>
        Trackers.Contains(name, StringComparer.OrdinalIgnoreCase)
        || name.StartsWith("__utm", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("IABGPP", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] Trackers =
        ["lwuid", "adksid", "adkvid", "ple", "pld", "usprivacy", "euconsent-v2", "__mggpc__"];
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
