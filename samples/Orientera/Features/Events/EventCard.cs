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
    public required string MetaLabel { get; init; }
    public required string DistanceLabel { get; init; }

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
    public partial bool IsFavourite { get; set; }

    public string FavouriteGlyph => IsFavourite ? "★" : "☆";

    public string FavouriteDescription => IsFavourite
        ? $"Ta bort {Title} från favoriter"
        : $"Spara {Title} som favorit";

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

            if (IsFavourite)
                parts.Add("favoritmarkerad");

            return string.Join(", ", parts);
        }
    }

    partial void OnIsFavouriteChanged(bool value)
    {
        OnPropertyChanged(nameof(FavouriteGlyph));
        OnPropertyChanged(nameof(FavouriteDescription));
        OnPropertyChanged(nameof(Accessibility));
    }
}
