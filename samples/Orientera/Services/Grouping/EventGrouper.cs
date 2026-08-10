using System.Globalization;
using System.Text;
using Orientera.Domain;

namespace Orientera.Services.Grouping;

/// <summary>
/// Collapses recurring events into one calendar row. Six Eventor rows for "Veckans bana"
/// on 4–9 August become a single card; the originals stay inside the group.
/// </summary>
/// <remarks>
/// The heuristic is the one the spec starts from: normalised title + organiser + place +
/// classification, then split into runs of adjacent dates. It is deliberately conservative —
/// a wrongly merged championship is far worse than a training series left ungrouped.
/// Tuning against several months of real data is spike SP-09.
/// </remarks>
public static class EventGrouper
{
    /// <summary>Largest gap in days between two occurrences that still counts as adjacent.</summary>
    public const int DefaultAdjacentDayGap = 1;

    /// <summary>
    /// Tokens that only enumerate an occurrence and must not separate it from its siblings:
    /// "Veckans bana etapp 3" and "Veckans bana #4" share a title.
    /// </summary>
    private static readonly HashSet<string> OrdinalTokens = new(StringComparer.Ordinal)
    {
        "etapp", "deltavling", "del", "omgang", "dag", "tillfalle", "nr", "no", "vecka",
    };

    public static IReadOnlyList<EventGroup> Group(
        IEnumerable<Competition> competitions,
        int adjacentDayGap = DefaultAdjacentDayGap)
    {
        var groups = new List<EventGroup>();

        var byKey = competitions
            .GroupBy(GroupKey)
            .OrderBy(g => g.Min(c => c.FirstStart));

        foreach (var candidates in byKey)
        {
            var ordered = candidates.OrderBy(c => c.FirstStart).ToList();

            var run = new List<Competition> { ordered[0] };

            for (int i = 1; i < ordered.Count; i++)
            {
                int gap = ordered[i].Date.DayNumber - ordered[i - 1].Date.DayNumber;

                if (gap <= adjacentDayGap)
                {
                    run.Add(ordered[i]);
                    continue;
                }

                groups.Add(Build(run));
                run = [ordered[i]];
            }

            groups.Add(Build(run));
        }

        return groups.OrderBy(g => g.FirstDate).ThenBy(g => g.Title).ToList();
    }

    private static EventGroup Build(List<Competition> run)
    {
        var first = run[0];

        return new EventGroup
        {
            Id = new EventGroupId(run.Count == 1
                ? first.Id.Value
                : $"grp-{NormalizeTitle(first.Name)}-{first.Date:yyyyMMdd}".Replace(' ', '-')),
            Title = run.Count == 1 ? first.Name : CommonTitle(run),
            Organiser = first.Organiser,
            Place = first.Place,
            Occurrences = run.ToList(),
        };
    }

    /// <summary>
    /// The shared part of the occurrences' names, so "Veckans bana etapp 3" and
    /// "Veckans bana etapp 4" present as "Veckans bana".
    /// </summary>
    private static string CommonTitle(List<Competition> run)
    {
        var wordLists = run
            .Select(c => c.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        var shared = new List<string>();
        int shortest = wordLists.Min(w => w.Length);

        for (int i = 0; i < shortest; i++)
        {
            string word = wordLists[0][i];
            if (wordLists.All(w => string.Equals(w[i], word, StringComparison.OrdinalIgnoreCase)))
                shared.Add(word);
            else
                break;
        }

        // Drop a trailing "etapp"/"deltävling" left behind once the differing number is gone.
        while (shared.Count > 1 && OrdinalTokens.Contains(Fold(shared[^1]).Trim()))
            shared.RemoveAt(shared.Count - 1);

        string title = string.Join(' ', shared).TrimEnd(',', '-', '–', ' ');
        return title.Length > 0 ? title : run[0].Name;
    }

    private static (string Title, string Organiser, string Place, Discipline Discipline, CompetitionLevel Level)
        GroupKey(Competition competition) =>
        (NormalizeTitle(competition.Name),
         competition.Organiser,
         competition.Place,
         competition.Discipline,
         competition.Level);

    /// <summary>
    /// Lower-cases, folds Swedish diacritics, drops punctuation, then removes the tokens that
    /// only number an occurrence — both the ordinal words and the numbers next to them.
    /// </summary>
    internal static string NormalizeTitle(string title)
    {
        var words = Fold(title).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(words.Length);

        for (int i = 0; i < words.Length; i++)
        {
            if (OrdinalTokens.Contains(words[i]))
            {
                // Skip the ordinal word and the number that follows it.
                if (i + 1 < words.Length && words[i + 1].All(char.IsDigit))
                    i++;
                continue;
            }

            if (words[i].All(char.IsDigit))
                continue;

            kept.Add(words[i]);
        }

        return string.Join(' ', kept);
    }

    /// <summary>Lower-cases, strips Swedish diacritics and turns punctuation into spaces.</summary>
    private static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);

        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            folded.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return folded.ToString();
    }
}
