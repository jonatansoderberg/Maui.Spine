using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Orientera.Domain;

namespace Orientera.Backend.Activities;

/// <summary>
/// Reads Eventor's club activity page.
/// </summary>
/// <remarks>
/// HTML again, and for the same reason as the ranking pages: <c>/api/activities</c> is documented
/// but answers 403 for our key, while <c>/api/organisation/115</c> answers 200 in the same run. So
/// the endpoint exists and the permission does not (issue #109).
///
/// The page groups activities under one heading per organisation — the club, its district, the
/// federation — and each row links to <c>/Activities/Show/{id}</c>.
///
/// Deadlines are shown to a human ("om 11 dagar") but the absolute time is always in the cell's
/// <c>title</c> attribute ("söndag 23 augusti 2026 klockan 20:00"). Reading the attribute rather
/// than the text keeps this free of "how long is a day" arithmetic and of the reading clock.
/// </remarks>
public static partial class ActivityPageParser
{
    private const string Heading = "Aktiviteter för ";

    /// <summary>Eventor's own wording, as the <c>title</c> attribute spells it out.</summary>
    private const string TitleFormat = "dddd d MMMM yyyy 'klockan' H:mm";

    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    public static IReadOnlyList<ClubActivity> Parse(string html, TimeZoneInfo zone)
    {
        var activities = new List<ClubActivity>();
        string? organisation = null;

        foreach (Match match in HeadingOrRowPattern().Matches(html))
        {
            if (match.Groups["heading"].Success)
            {
                var heading = Clean(match.Groups["heading"].Value);

                // Other headings exist on the page; only the activity ones name an organisation.
                organisation = heading.StartsWith(Heading, StringComparison.Ordinal)
                    ? heading[Heading.Length..]
                    : organisation;

                continue;
            }

            var row = match.Groups["row"].Value;
            var activity = ActivityPattern().Match(row);

            // The header row has no link, and so falls away here rather than on a cell count.
            if (!activity.Success || organisation is null)
                continue;

            var cells = CellPattern().Matches(row).Select(c => c.Groups[1].Value).ToList();

            if (cells.Count < 4)
                continue;

            activities.Add(new ClubActivity
            {
                Id = activity.Groups[1].Value,
                Name = Clean(activity.Groups[2].Value),
                Organisation = organisation,
                StartsAt = Moment(Clean(cells[1]), "yyyy-MM-dd HH:mm", zone),
                EntryDeadline = Moment(Title(cells[2]), TitleFormat, zone),
                EntryCount = int.TryParse(
                    Clean(cells[3]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int entries)
                    ? entries
                    : 0,
                IsOpen = row.Contains("/Activities/Register/", StringComparison.OrdinalIgnoreCase),
                Url = $"https://eventor.orientering.se/Activities/Show/{activity.Groups[1].Value}",
            });
        }

        return activities;
    }

    /// <summary>The absolute time behind a relative one, which is where Eventor keeps it.</summary>
    private static string? Title(string cell) =>
        TitlePattern().Match(cell) is { Success: true } title
            ? WebUtility.HtmlDecode(title.Groups[1].Value)
            : null;

    private static DateTimeOffset? Moment(string? text, string format, TimeZoneInfo zone)
    {
        if (text is null || !DateTime.TryParseExact(
            text, format, Swedish, DateTimeStyles.None, out var local))
        {
            return null;
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static string Clean(string value) =>
        SpacePattern().Replace(WebUtility.HtmlDecode(TagPattern().Replace(value, " ")), " ").Trim();

    /// <summary>Headings and rows in document order, so a row knows whose activity it is.</summary>
    [GeneratedRegex(
        @"<h3[^>]*>(?<heading>.*?)</h3>|<tr[^>]*>(?<row>.*?)</tr>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HeadingOrRowPattern();

    [GeneratedRegex(@"/Activities/Show/(\d+)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ActivityPattern();

    [GeneratedRegex(@"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CellPattern();

    [GeneratedRegex(@"title=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex TitlePattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
}
