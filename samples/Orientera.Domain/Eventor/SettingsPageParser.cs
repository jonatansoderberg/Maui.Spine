using System.Net;
using System.Text.RegularExpressions;

namespace Orientera.Domain.Eventor;

/// <summary>
/// Reads <c>/MyPages/Settings</c> for the two things the start page cannot say: which club the
/// reader competes for when the ranking box is missing, and which class they normally enter.
/// </summary>
/// <remarks>
/// Read once, at login. The page is a quarter of a megabyte and says nothing that changes between
/// two openings of the app.
/// </remarks>
public static partial class SettingsPageParser
{
    public static EventorSettings Parse(string html)
    {
        var club = ClubPattern().Match(html);

        return new EventorSettings
        {
            ClubId = club.Success ? club.Groups["id"].Value : null,
            Club = club.Success ? Clean(club.Groups["name"].Value) : null,
            DefaultClass = DefaultClass(html),
        };
    }

    /// <summary>
    /// "Förvald klass 1", which is the runner's own statement of what they enter. Eventor also
    /// ranks them in a class — H45 against an entered H21 on the account this was measured on —
    /// but that is Sverigelistan's arithmetic, not a choice, and the app wants the choice.
    /// </summary>
    private static string? DefaultClass(string html)
    {
        if (ClassSelectPattern().Match(html) is not { Success: true } select)
            return null;

        var selected = SelectedOptionPattern().Match(select.Groups[1].Value);

        return selected.Success && Clean(selected.Groups[1].Value) is { Length: > 0 } name ? name : null;
    }

    private static string Clean(string value) =>
        SpacePattern().Replace(WebUtility.HtmlDecode(TagPattern().Replace(value, " ")), " ").Trim();

    /// <summary>
    /// The club list is one radio per membership and the checked one is the default. Attribute
    /// order is Eventor's, so the id is taken from the label that names it rather than from the
    /// order of <c>checked</c> and <c>value</c> on the input.
    /// </summary>
    [GeneratedRegex(
        @"<input[^>]*checked[^>]*DefaultOrganisationId_(?<id>\d+)[^>]*>\s*"
        + @"<label[^>]*>(?<name>.*?)</label>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ClubPattern();

    [GeneratedRegex(
        @"<select[^>]*PreferredBaseClassId0[^>]*>(.*?)</select>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ClassSelectPattern();

    [GeneratedRegex(@"<option[^>]*\bselected\b[^>]*>(.*?)</option>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SelectedOptionPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
}
