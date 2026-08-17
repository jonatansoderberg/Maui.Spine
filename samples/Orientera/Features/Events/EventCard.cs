using Microsoft.Maui.Controls.Shapes;
using Orientera.Domain;

namespace Orientera.Features.Events;

/// <summary>
/// One row in the competition list, already formatted. A recurring series arrives here as a
/// single card with <see cref="OccurrenceLabel"/> set; the originals stay in the group.
/// </summary>
public sealed partial class EventCard : ObservableObject
{
    public required CompetitionId Competition { get; init; }
    public required string Title { get; init; }
    public required string DateLabel { get; init; }
    public required string PlaceLabel { get; init; }

    /// <summary>The organising club's badge, when the federation has one.</summary>
    public string? OrganiserLogo { get; init; }

    public bool HasOrganiserLogo => !string.IsNullOrEmpty(OrganiserLogo);
    public required string DisciplineLabel { get; init; }

    public required string LevelLabel { get; init; }

    /// <summary>The distance and the level as one phrase, for the screen reader.</summary>
    /// <remarks>
    /// The row draws them as separate labels so a mark can stand beside each. A screen reader
    /// reads the row as one description, and two fragments with a glyph between them read as two
    /// unrelated words there.
    /// </remarks>
    public string MetaLabel => $"{DisciplineLabel} · {LevelLabel}";

    public required string DistanceLabel { get; init; }

    /// <summary>The discipline's mark, and the name the style picks its colour by.</summary>
    /// <remarks>
    /// The mark is a scanning aid, not the label: <see cref="MetaLabel"/> still spells the
    /// distance out beside it, and the accessibility string reads the word rather than the shape.
    /// </remarks>
    public required Geometry? DisciplineShape { get; init; }

    public required string DisciplineKey { get; init; }

    /// <summary>The gold cup, for a championship. Null for every other level.</summary>
    public required Geometry? LevelShape { get; init; }

    public bool HasLevelShape => LevelShape is not null;

    /// <summary>"6 tillfällen" for a grouped series, empty otherwise.</summary>
    public string OccurrenceLabel { get; init; } = string.Empty;

    public bool IsRecurring => OccurrenceLabel.Length > 0;

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
    /// The whole card as one spoken sentence. A card is one cell to a screen reader — six
    /// separate swipes through date, title, organiser and badges would make the list unusable.
    /// </summary>
    public string Accessibility
    {
        get
        {
            var parts = new List<string> { DateLabel, Title, PlaceLabel, MetaLabel, DistanceLabel };

            if (IsRecurring)
                parts.Add(OccurrenceLabel);

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
