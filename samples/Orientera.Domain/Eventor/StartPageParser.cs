using System.Net;
using System.Text.RegularExpressions;

namespace Orientera.Domain.Eventor;

/// <summary>
/// Reads Eventor's start page — the app's answer to "am I still logged in, and as whom?".
/// </summary>
/// <remarks>
/// The session cookie has no expiry the app can trust, so liveness is asked rather than computed:
/// a page with the greeting is a live session, a page without it is not. That also means a dead
/// session and a signed-out one are the same thing here, which is the honest reading — the page
/// does not distinguish them either.
/// </remarks>
public static partial class StartPageParser
{
    public static EventorStartPage Parse(string html) => new()
    {
        Name = NamePattern().Match(html) is { Success: true } name ? Clean(name.Groups[1].Value) : null,
        PersonId = Group(RunnerPattern().Match(html)),
        Club = ClubNamePattern().Match(html) is { Success: true } club ? Clean(club.Groups[1].Value) : null,
        ClubId = Group(ClubPattern().Match(html)),
    };

    private static string? Group(Match match) => match.Success ? match.Groups[1].Value : null;

    private static string Clean(string value) =>
        SpacePattern().Replace(WebUtility.HtmlDecode(TagPattern().Replace(value, " ")), " ").Trim();

    [GeneratedRegex(@"class=""loggedInName""[^>]*>(.*?)</span>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex NamePattern();

    /// <summary>
    /// The club stands in its own paragraph right after the greeting, inside the user menu. It is
    /// read there rather than off "Klubblista för …" in the ranking box, because a club without
    /// Sverigelistan has no such box and still has a name.
    /// </summary>
    [GeneratedRegex(
        @"class=""loggedInName"".*?</p>\s*<p>(.*?)</p>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ClubNamePattern();

    [GeneratedRegex(@"/Ranking/[^/]+/Runner/Index/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RunnerPattern();

    [GeneratedRegex(@"/Ranking/[^/]+/Club/Index/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ClubPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
}
