using System.Net;
using System.Text.RegularExpressions;

namespace Orientera.Domain.Eventor;

/// <summary>
/// Reads <c>/MyPages/Events</c> — "Mina tävlingar" — for the competitions the reader is entered in
/// but has not run yet.
/// </summary>
/// <remarks>
/// The page is one table covering the whole season, run and unrun in the same rows, so the two
/// have to be told apart — and the date is the only column that does it honestly.
///
/// The first attempt keyed on the "Ändra anmälan" link, on the reasoning that Eventor itself marks
/// an unrun row by offering to change it. It survived two days. Once the entry deadline passed the
/// link vanished while the runner stayed entered, and the competition dropped off the app's Hem the
/// week it mattered most. The column it sat in turns out to carry a placement, or <em>ej godkänd</em>,
/// or <em>Ändra anmälan och/eller tjänster</em>, or <em>Starttid: 11:18</em> once the draw is out —
/// a set with no end to enumerate.
///
/// So: dated today or later. A race being run this morning counts as an entry until its results
/// appear, which is what the app should say about it anyway.
///
/// Entered, not registered-at. The page does not carry the moment the entry was made, and no
/// amount of parsing will invent it; see <see cref="EventorEntry"/>.
/// </remarks>
public static partial class MyEventsPageParser
{
    public static IReadOnlyList<EventorEntry> Parse(string html, DateOnly today)
    {
        if (TablePattern().Match(html) is not { Success: true } table)
            return [];

        var entries = new List<EventorEntry>();

        foreach (Match row in RowPattern().Matches(table.Value))
        {
            var cells = CellPattern().Matches(row.Value);

            if (cells.Count <= ClassColumn)
                continue;

            if (!DateOnly.TryParse(Cell(cells, DateColumn), out var date) || date < today)
                continue;

            if (EventIdPattern().Match(row.Value) is not { Success: true } link)
                continue;

            entries.Add(new EventorEntry
            {
                EventId = link.Groups["id"].Value,
                Class = ClassName(Cell(cells, ClassColumn)),
            });
        }

        return entries;
    }

    /// <summary>
    /// The races already behind the runner, with what they did in them.
    /// </summary>
    /// <remarks>
    /// The same table, read the other way. Eventor's "Mina tävlingar" is the only page that lists a
    /// person's results across competitions — the calendar knows the race and the result list knows
    /// the field, but neither knows which of them are yours.
    /// </remarks>
    public static IReadOnlyList<EventorResult> ParseResults(string html, DateOnly today)
    {
        if (TablePattern().Match(html) is not { Success: true } table)
            return [];

        var results = new List<EventorResult>();

        foreach (Match row in RowPattern().Matches(table.Value))
        {
            var cells = CellPattern().Matches(row.Value);

            // Rows are not the same width. A placement fills nine cells; "ej godkänd" fills seven
            // and simply stops, so every column past the placement has to be asked for rather
            // than indexed.
            if (cells.Count <= PlaceColumn)
                continue;

            if (!DateOnly.TryParse(Cell(cells, DateColumn), out var date) || date >= today)
                continue;

            if (EventIdPattern().Match(row.Value) is not { Success: true } link)
                continue;

            string place = Cell(cells, PlaceColumn);

            results.Add(new EventorResult
            {
                EventId = link.Groups["id"].Value,
                Date = date,
                Name = Cell(cells, NameColumn),
                Class = ClassName(Cell(cells, ClassColumn)),

                // A placement is a number. Everything else in that cell — "ej godkänd", a status,
                // a blank — means the runner started and was not classified, which is a fact the
                // list should keep rather than round to a zero.
                Place = int.TryParse(place, out int p) ? p : null,
                PlaceText = place,
                Discipline = DisciplineNames.In(Cell(cells, NameColumn)),
                Time = Duration(Cell(cells, TimeColumn)),
                Behind = Duration(Cell(cells, BehindColumn).TrimStart('+')),
            });
        }

        return results;
    }


    /// <summary>A column's text, or empty where the row stopped short of it.</summary>
    private static string Cell(MatchCollection cells, int index) =>
        index < cells.Count ? Clean(cells[index].Groups[1].Value) : string.Empty;

    /// <summary>"1:05:51" or "43:57" — Eventor writes hours only when there are any.</summary>
    private static TimeSpan? Duration(string value)
    {
        var parts = value.Split(':');

        if (parts.Length is < 2 or > 3 || parts.Any(p => !int.TryParse(p, out _)))
            return null;

        int[] n = [.. parts.Select(int.Parse)];

        return parts.Length == 3
            ? new TimeSpan(n[0], n[1], n[2])
            : new TimeSpan(0, n[0], n[1]);
    }

    /// <summary>Datum, Tävlingens namn, Arrangörsorganisationer, Klass — Eventor's own order.</summary>
    private const int DateColumn = 0;

    private const int NameColumn = 1;

    private const int ClassColumn = 3;

    private const int PlaceColumn = 5;

    private const int TimeColumn = 6;

    private const int BehindColumn = 7;

    /// <summary>
    /// "H21 (36)" is the class and how many entered it. The count belongs to the competition, not
    /// to the entry, and changes under the reader's feet until the deadline.
    /// </summary>
    private static string ClassName(string cell) =>
        EntrantCountPattern().Replace(cell, string.Empty).Trim();

    private static string Clean(string value) =>
        SpacePattern().Replace(WebUtility.HtmlDecode(TagPattern().Replace(value, " ")), " ").Trim();

    [GeneratedRegex(@"<table[^>]*>.*?</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TablePattern();

    [GeneratedRegex(@"<tr[^>]*>.*?</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RowPattern();

    [GeneratedRegex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CellPattern();

    /// <summary>
    /// The competition's id, from whichever of the row's links happens to carry it — the entry
    /// link before the deadline, the start list after it, the competition page throughout. It is
    /// the same number the calendar knows the competition by.
    /// </summary>
    [GeneratedRegex(@"(?:eventId=|/Events/Show/)(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"\s*\(\d+\)\s*$")]
    private static partial Regex EntrantCountPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
}
