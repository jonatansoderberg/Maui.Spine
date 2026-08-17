using System.Net;
using System.Text.RegularExpressions;

namespace Orientera.Domain.Eventor;

/// <summary>
/// Reads Eventor's public list of entrants — <c>/Events/Entries?eventId=…&amp;groupBy=EventClass</c>.
/// </summary>
/// <remarks>
/// This is the field before the draw, which is the only time it is the only thing there is. Once
/// the organiser draws start times the start list says the same and more, and the app shows that
/// instead.
///
/// Public, measured: the page answers identically with no cookies at all. That is why it is read
/// here, by the backend, and not on the phone with the reader's login — one fetch serves every
/// user, and a list of who is going to a competition is nobody's private business (#123).
///
/// The page is one heading and one table per class, thirty of them for a national event. Runners
/// carry no person id, only a name and a club, so the reader is found in the list the same way the
/// live lists find them: by <see cref="RunnerIdentity"/>.
/// </remarks>
public static partial class EntryListPageParser
{
    public static IReadOnlyList<EventorEntrant> Parse(string html)
    {
        var entrants = new List<EventorEntrant>();

        foreach (Match section in ClassSectionPattern().Matches(html))
        {
            string className = ClassName(Clean(section.Groups["class"].Value));

            if (className.Length == 0)
                continue;

            foreach (Match row in RowPattern().Matches(section.Groups["table"].Value))
            {
                var cells = CellPattern().Matches(row.Value);

                if (cells.Count < 2)
                    continue;

                string name = Clean(cells[0].Groups[1].Value);

                if (name.Length == 0)
                    continue;

                entrants.Add(new EventorEntrant
                {
                    Name = name,
                    Club = Clean(cells[1].Groups[1].Value),
                    Class = className,
                });
            }
        }

        return entrants;
    }

    /// <summary>"H21 (36)" — the class and how many have entered it so far.</summary>
    private static string ClassName(string heading) =>
        EntrantCountPattern().Replace(heading, string.Empty).Trim();

    private static string Clean(string value) =>
        SpacePattern().Replace(WebUtility.HtmlDecode(TagPattern().Replace(value, " ")), " ").Trim();

    /// <summary>
    /// A class heading and the table under it. Anchored on the heading rather than on the table so
    /// the runners stay attached to the class they entered — the page has thirty of each, and a
    /// table taken on its own says nothing about which class it belongs to.
    /// </summary>
    /// <remarks>
    /// The trailing count is required, and it is what tells a class heading from the page's own
    /// furniture. Without it the site's sidebar heading — "Produkter och tjänster" — pairs with the
    /// first class table and eighteen runners end up in a class named after an advertisement.
    /// </remarks>
    [GeneratedRegex(
        @"<h[23][^>]*>(?<class>[^<]*?\(\d+\))\s*</h[23]>\s*(?<table><table[^>]*>.*?</table>)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ClassSectionPattern();

    [GeneratedRegex(@"<tr[^>]*>.*?</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RowPattern();

    [GeneratedRegex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CellPattern();

    [GeneratedRegex(@"\s*\(\d+\)\s*$")]
    private static partial Regex EntrantCountPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
}
