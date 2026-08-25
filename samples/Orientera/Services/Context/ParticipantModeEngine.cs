using Orientera.Domain;

namespace Orientera.Services.Context;

/// <summary>
/// Decides which of a participant list's four modes can be opened, and which one the page opens
/// on. Pure, like <see cref="ContextEngine"/>, and driven by the same clock through
/// <see cref="ParticipantInput.State"/> — so the whole switcher replays with the time machine.
/// </summary>
/// <remarks>
/// The division of labour is the point (D10). The calendar knows what <em>should</em> exist by
/// now; only an answer knows what <em>does</em>, and where the two disagree the answer wins — in
/// both directions. A source that said "nothing here" closes a mode the calendar expected; a
/// source that has not been asked leaves a mode open, because not knowing is not the same as
/// knowing there is none (#89). The page then opens on the furthest mode that survived, so the
/// calendar reaches the default only through what it made available.
/// </remarks>
public static class ParticipantModeEngine
{
    private static readonly ParticipantMode[] Ladder =
        [ParticipantMode.Entries, ParticipantMode.StartList, ParticipantMode.Live, ParticipantMode.Results];

    public static ParticipantDecision Decide(ParticipantInput input)
    {
        var modes = Ladder
            .Select(mode => Offer(mode, input))
            .ToList();

        return new ParticipantDecision
        {
            Default = Resolve(input, modes),
            Modes = modes,
        };
    }

    private static ParticipantModeOffer Offer(ParticipantMode mode, ParticipantInput input)
    {
        var sighting = input.Sightings.For(mode);

        bool available = sighting switch
        {
            Sighting.Present => true,
            Sighting.Absent => false,
            _ => input.State >= Expected(mode),
        };

        return new ParticipantModeOffer
        {
            Mode = mode,
            Text = TextFor(mode),
            IsAvailable = available,
            ConditionText = available ? string.Empty
                : sighting == Sighting.Absent ? AbsentTextFor(mode)
                : NotYetTextFor(mode),
        };
    }

    /// <summary>
    /// The state from which the calendar says a mode ought to have something behind it.
    /// </summary>
    /// <remarks>
    /// Results begins at <see cref="ContextState.Live"/>, not at <see cref="ContextState.Finished"/>:
    /// the preliminary list fills up as runners come in, and it is the same list (D11). Waiting
    /// for the arena to close would hide finished runners from the people watching them finish.
    /// </remarks>
    private static ContextState Expected(ParticipantMode mode) => mode switch
    {
        ParticipantMode.Entries => ContextState.RegistrationOpen,
        ParticipantMode.StartList => ContextState.StartListPublished,
        ParticipantMode.Live => ContextState.Live,
        _ => ContextState.Live,
    };

    /// <summary>Where the calendar alone would open the page, for a competition with nothing behind any mode.</summary>
    private static ParticipantMode Preferred(ContextState state) => state switch
    {
        < ContextState.StartListPublished => ParticipantMode.Entries,
        ContextState.StartListPublished or ContextState.RaceDay => ParticipantMode.StartList,
        ContextState.Live => ParticipantMode.Live,
        _ => ParticipantMode.Results,
    };

    /// <summary>
    /// The most advanced mode that has something behind it.
    /// </summary>
    /// <remarks>
    /// Availability already carries the calendar's opinion, so this needs no second helping of it:
    /// the furthest the reader can actually get is where the page opens. The one inversion is the
    /// race itself — while anyone is out, the split table is the race, and the handful of rows
    /// that have come in is not yet the answer to "how did it go".
    /// <para>
    /// <see cref="ParticipantInput.IsRunningNow"/> is only meaningful once the live source has
    /// answered, so a page that decides before its first fetch will decide again after it.
    /// </para>
    /// </remarks>
    private static ParticipantMode Resolve(ParticipantInput input, IReadOnlyList<ParticipantModeOffer> modes)
    {
        var available = modes.Where(offer => offer.IsAvailable).Select(offer => offer.Mode).ToList();

        // Nothing at all: the calendar's preference stands so the page has something to draw its
        // empty state under, with a switcher that says chip by chip what is missing and why.
        if (available.Count == 0)
            return Preferred(input.State);

        if (input.IsRunningNow && available.Contains(ParticipantMode.Live))
            return ParticipantMode.Live;

        return available[^1];
    }

    private static string TextFor(ParticipantMode mode) => mode switch
    {
        ParticipantMode.Entries => "Anmälda",
        ParticipantMode.StartList => "Startlista",
        ParticipantMode.Live => "Live",
        _ => "Resultat",
    };

    /// <summary>The calendar has not reached this mode yet — it is coming.</summary>
    private static string NotYetTextFor(ParticipantMode mode) => mode switch
    {
        ParticipantMode.Entries => "finns när anmälan öppnat",
        ParticipantMode.StartList => "finns när startlistan lottats",
        ParticipantMode.Live => "finns när tävlingen startat",
        _ => "finns efter målgång",
    };

    /// <summary>A source answered, and there was nothing. A different sentence, because it is a different no.</summary>
    private static string AbsentTextFor(ParticipantMode mode) => mode switch
    {
        ParticipantMode.Entries => "ingen är anmäld i klassen",
        ParticipantMode.StartList => "klassen är inte lottad",
        ParticipantMode.Live => "klassen finns inte i livelistan",
        _ => "inget resultat för klassen",
    };
}
