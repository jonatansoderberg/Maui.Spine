namespace Orientera.Domain;

/// <summary>
/// Which question a competition's participant list is answering. Declared in lifecycle order,
/// like <see cref="ContextState"/>, so "the most advanced one available" is a comparison rather
/// than a table.
/// </summary>
public enum ParticipantMode
{
    /// <summary>Who has entered, in the weeks before the draw.</summary>
    Entries,

    /// <summary>Who starts when, once the times are drawn.</summary>
    StartList,

    /// <summary>Where the field is right now, radio control by radio control.</summary>
    Live,

    /// <summary>How it went — preliminary while the arena is open, official once published.</summary>
    Results,
}

/// <summary>
/// What a mode's source has been seen to say. Ordered, and only ever recorded upwards: a list
/// that has existed does not stop having existed because the next request timed out.
/// </summary>
public enum Sighting
{
    /// <summary>Not asked, or asked and not answered. Not the same as knowing there is nothing.</summary>
    Unknown,

    /// <summary>The source answered, and there was nothing. A fact about the competition.</summary>
    Absent,

    /// <summary>Rows came back.</summary>
    Present,
}

/// <summary>
/// What the four sources have said about one class of one competition.
/// </summary>
/// <remarks>
/// Accumulated over the life of a page and reset when the class changes — the answers belong to
/// the class they were asked about, and carrying H21's start list over to D14 would be a page
/// that lies about a list it never read.
/// </remarks>
public sealed record ParticipantSightings
{
    public Sighting Entries { get; init; }
    public Sighting StartList { get; init; }
    public Sighting Live { get; init; }
    public Sighting Results { get; init; }

    public Sighting For(ParticipantMode mode) => mode switch
    {
        ParticipantMode.Entries => Entries,
        ParticipantMode.StartList => StartList,
        ParticipantMode.Live => Live,
        _ => Results,
    };

    /// <summary>
    /// Records what a source answered, keeping the strongest answer seen so far.
    /// </summary>
    /// <remarks>
    /// Monotonic on purpose. Offline is <see cref="Sighting.Unknown"/>, and letting it overwrite
    /// a <see cref="Sighting.Present"/> would grey out the start list the runner is standing at
    /// the arena reading.
    /// </remarks>
    public ParticipantSightings Saw(ParticipantMode mode, Sighting sighting) => mode switch
    {
        ParticipantMode.Entries => this with { Entries = Max(Entries, sighting) },
        ParticipantMode.StartList => this with { StartList = Max(StartList, sighting) },
        ParticipantMode.Live => this with { Live = Max(Live, sighting) },
        _ => this with { Results = Max(Results, sighting) },
    };

    private static Sighting Max(Sighting known, Sighting seen) => seen > known ? seen : known;
}

/// <summary>
/// Everything the mode engine needs: what the calendar says, and what the sources have answered.
/// </summary>
public sealed record ParticipantInput
{
    /// <summary>Where the competition sits on the journey. The calendar's answer, and a guess.</summary>
    public required ContextState State { get; init; }

    /// <summary>What the sources have actually said. Beats the calendar wherever the two disagree.</summary>
    public ParticipantSightings Sightings { get; init; } = new();

    /// <summary>
    /// Whether anyone in the field is still out on the course.
    /// </summary>
    /// <remarks>
    /// <see cref="ContextState.Live"/> lasts until the arena closes, which can be hours after the
    /// last runner in one class came in. Without this the page would open on a split table nobody
    /// is moving through, when what the reader wants by then is the result list.
    /// </remarks>
    public bool IsRunningNow { get; init; }
}

/// <summary>One mode as the switcher offers it.</summary>
public sealed record ParticipantModeOffer
{
    public required ParticipantMode Mode { get; init; }

    /// <summary>The Swedish label on the chip.</summary>
    public required string Text { get; init; }

    public required bool IsAvailable { get; init; }

    /// <summary>
    /// Why the mode cannot be opened, in the app's own words. Empty when it can.
    /// </summary>
    /// <remarks>
    /// A greyed chip with no reason reads as a broken chip — the finding from the test run that
    /// gave the competition page its <c>LiveConditionText</c>. The sentence also says which kind
    /// of "no" it is: one the calendar has not reached yet, or one a source answered outright.
    /// </remarks>
    public required string ConditionText { get; init; }
}

/// <summary>The switcher's four chips, and which of them the page opens on.</summary>
public sealed record ParticipantDecision
{
    public required ParticipantMode Default { get; init; }

    /// <summary>All four, in lifecycle order — an unavailable mode is shown, not hidden.</summary>
    public required IReadOnlyList<ParticipantModeOffer> Modes { get; init; }

    public ParticipantModeOffer this[ParticipantMode mode] => Modes.First(offer => offer.Mode == mode);

    public bool IsAvailable(ParticipantMode mode) => this[mode].IsAvailable;
}
