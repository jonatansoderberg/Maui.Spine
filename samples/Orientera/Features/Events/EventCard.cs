using Microsoft.Maui.Controls.Shapes;
using Orientera.Domain;

namespace Orientera.Features.Events;

/// <summary>
/// One row in the competition list, already formatted. A recurring series arrives here as a
/// single card with <see cref="SpanLabel"/> set; the originals stay in the group.
/// </summary>
public sealed partial class EventCard : ObservableObject
{
    public required CompetitionId Competition { get; init; }
    public required string Title { get; init; }

    /// <summary>The date the row sorts and collapses by — the group's first day.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The day number: the top line of the date column.</summary>
    public required string DayLabel { get; init; }

    /// <summary>"mån" — under the day number.</summary>
    public required string WeekdayLabel { get; init; }

    /// <summary>"aug" — under the weekday, and only when the month changes.</summary>
    public required string MonthLabel { get; init; }

    /// <summary>
    /// The whole date column, spelled out for the screen reader: "idag", "lör 6 sep".
    /// Never drawn — the column shows the day, the weekday and sometimes the month, and a
    /// reader hearing three fragments in a row would hear three facts instead of one date.
    /// </summary>
    public required string SpokenDate { get; init; }

    /// <summary>
    /// False when the row above carries the same date. Set while the sections are built, so a
    /// calendar collapses hard and a ranked list — where the dates jump — almost never does.
    /// </summary>
    public bool ShowDate { get; set; } = true;

    /// <summary>
    /// The same rule one step up. A bare day number is enough under "September"; under "Mest
    /// relevant", which is ranked rather than dated, it is not.
    /// </summary>
    public bool ShowMonth { get; set; } = true;

    public required string PlaceLabel { get; init; }

    /// <summary>The organising club's badge, when the federation has one.</summary>
    public string? OrganiserLogo { get; init; }

    public bool HasOrganiserLogo => !string.IsNullOrEmpty(OrganiserLogo);
    public required string DisciplineLabel { get; init; }

    public required string LevelLabel { get; init; }

    /// <summary>The discipline and the level as one phrase, for the screen reader.</summary>
    /// <remarks>
    /// The row does not draw the level. "Nationell" on two rows in three says what the filter
    /// chips already say, and where the level does distinguish — a mästerskap — the cup stands
    /// beside the mark and the title already opens with "DM". A screen reader has neither the
    /// cup in view nor the chips, so it hears the level on every row.
    /// </remarks>
    public string MetaLabel => $"{DisciplineLabel} · {LevelLabel}";

    /// <summary>The row's second line: the discipline as a word, then who and where.</summary>
    public string MetaLine => $"{DisciplineLabel} · {PlaceLabel}";

    public required string DistanceLabel { get; init; }

    /// <summary>The discipline's mark, and the name the style picks its colour by.</summary>
    /// <remarks>
    /// The mark is a scanning aid, not the label: <see cref="MetaLine"/> still spells the
    /// discipline out under it, and the accessibility string reads the word rather than the shape.
    /// </remarks>
    public required Geometry? DisciplineShape { get; init; }

    public required string DisciplineKey { get; init; }

    /// <summary>The gold cup, for a championship. Null for every other level.</summary>
    public required Geometry? LevelShape { get; init; }

    public bool HasLevelShape => LevelShape is not null;

    /// <summary>
    /// What the date column cannot hold: "6 tillfällen" for a grouped series, "4–9 aug" for a
    /// single event that runs over several days. The column carries one day, because a spine
    /// that changes width row by row is not a spine; the exception is said in words.
    /// </summary>
    public string SpanLabel { get; init; } = string.Empty;

    public bool HasSpan => SpanLabel.Length > 0;

    /// <summary>The context state's own words — "Anmälan öppen", "Sträcktider".</summary>
    public string ContextLabel { get; init; } = string.Empty;

    /// <summary>
    /// False when an explicit badge already says the same thing. "Live" and "Anmäld" have
    /// their own badges, so repeating them as context would be noise.
    /// </summary>
    public bool ShowContextBadge { get; init; }

    public bool IsLive { get; init; }
    public bool IsRegistered { get; init; }
    public bool HasGroupEntry { get; init; }

    /// <summary>Whether the row has a third line at all.</summary>
    public bool HasBadges => IsLive || IsRegistered || HasGroupEntry || HasSpan || ShowContextBadge;

    [ObservableProperty]
    public partial bool IsInterested { get; set; }

    public string InterestGlyph => IsInterested ? "★" : "☆";

    /// <summary>
    /// "Intresserad", not "favorit". A favourite is a person you follow; about a competition the
    /// word for the star is whether you are interested in going, which is why this track is
    /// called interest and <see cref="Domain.FollowReason.Favourite"/> keeps the other word.
    /// </summary>
    public string InterestDescription => IsInterested
        ? $"Ta bort intressemarkeringen för {Title}"
        : $"Markera att du är intresserad av {Title}";

    /// <summary>
    /// The whole row as one spoken sentence. A row is one cell to a screen reader — six
    /// separate swipes through date, title, organiser and badges would make the list unusable.
    /// </summary>
    /// <remarks>
    /// The date is spoken on every row, including the ones that draw an empty column. Sight
    /// carries the date down from the row above; a reader moving one cell at a time has nothing
    /// above to carry it from.
    /// </remarks>
    public string Accessibility
    {
        get
        {
            var parts = new List<string> { SpokenDate, Title, PlaceLabel, MetaLabel, DistanceLabel };

            if (HasSpan)
                parts.Add(SpanLabel);

            if (IsLive)
                parts.Add("pågår nu");

            if (IsRegistered)
                parts.Add("du är anmäld");

            if (HasGroupEntry)
                parts.Add("någon i min grupp är anmäld");

            if (ShowContextBadge && ContextLabel.Length > 0)
                parts.Add(ContextLabel);

            if (IsInterested)
                parts.Add("intresserad");

            return string.Join(", ", parts);
        }
    }

    partial void OnIsInterestedChanged(bool value)
    {
        OnPropertyChanged(nameof(InterestGlyph));
        OnPropertyChanged(nameof(InterestDescription));
        OnPropertyChanged(nameof(Accessibility));
    }
}
