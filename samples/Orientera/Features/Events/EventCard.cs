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

    partial void OnIsFavouriteChanged(bool value) => OnPropertyChanged(nameof(FavouriteGlyph));
}
