using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Orientera.Domain;

namespace Orientera.Backend.Ranking;

/// <summary>
/// Reads a runner's own Sverigelistan page — the one behind the fee.
/// </summary>
/// <remarks>
/// Two tables carry everything. The first is every result: date, competition, discipline, class
/// and points, with the counting ones numbered one to six. The second is the lists — Sverigelistan
/// and one per discipline — each with a national place and an average.
///
/// This is the same fragility as the club page and worse, because more depends on it. A layout
/// change breaks it silently, which is why the tests read a saved page rather than the parser.
/// </remarks>
public static partial class RunnerRankingParser
{
    public static RankingSnapshot? Parse(string personId, string html, DateOnly readOn)
    {
        var tables = TablePattern().Matches(html);

        if (tables.Count < 2)
            return null;

        var results = Results(tables[0].Value, readOn);
        var lists = Lists(tables[1].Value);

        if (!lists.TryGetValue("Sverigelistan", out var overall))
            return null;

        return new RankingSnapshot
        {
            Person = new PersonId(personId),
            Date = readOn,
            Points = overall.Points,
            NationalPlace = overall.Place,
            // The page shows a current standing, not a change. A trend needs two readings, and
            // this reads one — so it is zero rather than a number invented to fill the field.
            Trend = 0,
            DisciplinePoints = lists
                .Where(l => DisciplineOf(l.Key) is not null)
                .ToDictionary(l => DisciplineOf(l.Key)!.Value, l => l.Value.Points),
            Results = results,
        };
    }

    /// <summary>Eventor's own list names. "Sverigelistan" is the overall one and has no discipline.</summary>
    private static Discipline? DisciplineOf(string list) => list switch
    {
        "Långlistan" => Discipline.Long,
        "Medellistan" => Discipline.Middle,
        "Nattlistan" => Discipline.Night,
        "Sprintlistan" => Discipline.Sprint,
        _ => null,
    };

    private static Dictionary<string, (int Place, double Points)> Lists(string table)
    {
        var lists = new Dictionary<string, (int, double)>(StringComparer.Ordinal);

        foreach (Match row in RowPattern().Matches(table))
        {
            var cells = Cells(row.Groups[1].Value);

            // Each list is followed by a row for the runner's own class with the same shape; only
            // the named lists are taken, so the class rows fall away on the name lookup.
            if (cells.Count < 4 || DisciplineOf(cells[0]) is null && cells[0] != "Sverigelistan")
                continue;

            if (int.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int place)
                && TryNumber(cells[2], out double points))
            {
                lists[cells[0]] = (place, points);
            }
        }

        return lists;
    }

    private static List<RankingResult> Results(string table, DateOnly readOn)
    {
        var results = new List<RankingResult>();

        foreach (Match row in RowPattern().Matches(table))
        {
            var html = row.Groups[1].Value;
            var cells = Cells(html);

            if (cells.Count < 5 || !TryNumber(cells[4], out double points))
                continue;

            // The date cell also carries the counting rank when the result is one of the six.
            var date = DatePattern().Match(cells[0]);

            if (!date.Success || !DateOnly.TryParse(date.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                continue;

            var competition = EventPattern().Match(html);

            results.Add(new RankingResult
            {
                Competition = new CompetitionId(competition.Success ? competition.Groups[1].Value : cells[1]),
                CompetitionName = cells[1],
                Date = day,
                Points = points,
                IsCounting = html.Contains("positionContainer", StringComparison.Ordinal),
                // Sverigelistan counts exactly one year back, which is what makes a result expire.
                ExpiresOn = day.AddYears(1),
            });
        }

        return results;
    }

    private static List<string> Cells(string row) =>
        [.. CellPattern().Matches(row).Select(c => Clean(c.Groups[1].Value))];

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.GetCultureInfo("sv-SE"), out value);

    private static string Clean(string cell) =>
        SpacePattern().Replace(WebUtility.HtmlDecode(TagPattern().Replace(cell, " ")), " ").Trim();

    [GeneratedRegex(@"<table.*?</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TablePattern();

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RowPattern();

    [GeneratedRegex(@"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CellPattern();

    [GeneratedRegex(@"/Ranking/[^/]+/Event/Index/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex EventPattern();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
}
