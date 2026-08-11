using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Orientera.Domain;

namespace Orientera.Backend.Ranking;

/// <summary>
/// Reads a club's Sverigelistan page.
/// </summary>
/// <remarks>
/// HTML, because there is nothing else: Eventor's API documents thirty-seven endpoints and none
/// of them is ranking (SP-02). That makes this the most fragile code in the backend — it depends
/// on a page layout nobody promised us, and a change to it breaks silently.
///
/// So the parser is deliberately forgiving about everything except what it needs. A row it cannot
/// read is skipped rather than thrown on, because one odd row must not cost a whole club, and a
/// page that yields nothing is reported as nothing rather than as a crash.
/// </remarks>
public static partial class RankingPageParser
{
    /// <summary>Columns as the page heads them: #, Namn, Klass, Riks, Poäng.</summary>
    private const int Columns = 5;

    public static IReadOnlyList<RankingRow> Parse(string clubId, string html)
    {
        var rows = new List<RankingRow>();
        RankingSection? section = null;

        foreach (Match match in SectionOrRowPattern().Matches(html))
        {
            // The club is two tables under two headings, each numbered from one. Read flat they
            // yield two runners ranked first, and a place that cannot be told apart.
            if (match.Groups["section"].Success)
            {
                section = Clean(match.Groups["section"].Value) switch
                {
                    "Damer" => RankingSection.Women,
                    "Herrar" => RankingSection.Men,
                    _ => section,
                };

                continue;
            }

            var row = match.Groups["row"];

            // The runner's own id, which the club page links every name to. Without it a row
            // could only be matched on a name, which is what SP-02 wrongly concluded.
            var runner = RunnerPattern().Match(row.Value);

            if (!runner.Success)
                continue;

            var cells = CellPattern().Matches(row.Value)
                .Select(c => Clean(c.Groups[1].Value))
                .ToList();

            if (cells.Count < Columns)
                continue;

            // The header row survives the cell match; it is skipped by failing to be a number.
            if (!int.TryParse(cells[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clubRank))
                continue;

            if (cells[1].Length == 0 || !TryPoints(cells[4], out double points))
                continue;

            rows.Add(new RankingRow
            {
                ClubId = clubId,
                RunnerId = runner.Groups[1].Value,
                Name = cells[1],
                Class = cells[2],
                ClubRank = clubRank,
                NationalRank = int.TryParse(cells[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int national)
                    ? national
                    : null,
                Points = points,
                Section = section,
            });
        }

        return rows;
    }

    /// <summary>Swedish decimals: "3,30". Parsed as such rather than by the server's locale.</summary>
    private static bool TryPoints(string text, out double points) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.GetCultureInfo("sv-SE"), out points);

    private static string Clean(string cell) =>
        WhitespacePattern().Replace(WebUtility.HtmlDecode(TagPattern().Replace(cell, string.Empty)), " ").Trim();

    /// <summary>Headings and rows in the order the page has them, so a row knows its section.</summary>
    [GeneratedRegex(
        @"<h3[^>]*>(?<section>.*?)</h3>|<tr[^>]*>(?<row>.*?)</tr>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SectionOrRowPattern();

    [GeneratedRegex(@"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CellPattern();

    [GeneratedRegex(@"/Ranking/[^/]+/Runner/Index/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RunnerPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
