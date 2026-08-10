using Orientera.Domain;

namespace Orientera.Services.Context;

/// <summary>
/// Decides where a competition sits on the journey and what the right primary action is.
/// Pure and clock-driven: the same competition replays its whole lifecycle as
/// <see cref="ContextInput.Now"/> moves, which is exactly what the time machine exercises.
/// </summary>
public static class ContextEngine
{
    public static ContextDecision Evaluate(ContextInput input)
    {
        var state = ResolveState(input);
        return new ContextDecision
        {
            State = state,
            PrimaryAction = ActionFor(state),
            PrimaryActionText = TextFor(state),
            StateText = StateTextFor(state),
        };
    }

    private static string StateTextFor(ContextState state) => state switch
    {
        ContextState.Discovered => "Upptäckt",
        ContextState.RegistrationOpen => "Anmälan öppen",
        ContextState.Registered => "Anmäld",
        ContextState.PmPublished => "PM publicerat",
        ContextState.StartListPublished => "Startlista",
        ContextState.RaceDay => "Tävlingsdag",
        ContextState.Live => "Live",
        ContextState.Finished => "Preliminärt",
        ContextState.ResultsPublished => "Resultat",
        ContextState.SplitsAvailable => "Sträcktider",
        ContextState.MapAndAnalysisAvailable => "Karta och analys",
        _ => string.Empty,
    };

    /// <summary>Returns the most advanced state whose conditions hold.</summary>
    private static ContextState ResolveState(ContextInput input)
    {
        var now = input.Now;
        var competition = input.Competition;
        var schedule = competition.Schedule;

        bool Published(DateTimeOffset? at) => at is { } t && t <= now;

        if (Published(schedule.MapPublishedAt))
            return ContextState.MapAndAnalysisAvailable;

        if (Published(schedule.SplitsPublishedAt))
            return ContextState.SplitsAvailable;

        if (Published(schedule.ResultsPublishedAt))
            return ContextState.ResultsPublished;

        if (now >= competition.LastFinish)
            return ContextState.Finished;

        if (now >= competition.FirstStart)
            return ContextState.Live;

        // Registration is what makes PM and start list personally relevant; without it the
        // user is still in discovery and "Anmäl dig" is the action that matters.
        bool registered = Published(input.MyEntryRegisteredAt) || Published(input.GroupEntryRegisteredAt);

        if (now.Date == competition.FirstStart.Date)
            return ContextState.RaceDay;

        if (registered && input.MyStartTime is not null && Published(schedule.StartListPublishedAt))
            return ContextState.StartListPublished;

        if (registered && Published(schedule.PmPublishedAt))
            return ContextState.PmPublished;

        if (registered)
            return ContextState.Registered;

        // When the opening time is known it decides. Eventor does not publish one, and there a
        // competition in the calendar with time left on its deadline is open for entry.
        bool opened = schedule.RegistrationOpensAt is { } opensAt
            ? opensAt <= now
            : schedule.EntryDeadline is not null;

        bool closed = schedule.EntryDeadline is { } deadline && now > deadline;

        if (opened && !closed)
            return ContextState.RegistrationOpen;

        return ContextState.Discovered;
    }

    private static ContextAction ActionFor(ContextState state) => state switch
    {
        ContextState.Discovered => ContextAction.ShowCompetition,
        ContextState.RegistrationOpen => ContextAction.Register,
        ContextState.Registered => ContextAction.Prepare,
        ContextState.PmPublished => ContextAction.ReadPm,
        ContextState.StartListPublished => ContextAction.ShowMyStart,
        ContextState.RaceDay => ContextAction.Navigate,
        ContextState.Live => ContextAction.FollowLive,
        ContextState.Finished => ContextAction.ShowPreliminary,
        ContextState.ResultsPublished => ContextAction.ShowMyResult,
        ContextState.SplitsAvailable => ContextAction.Analyse,
        ContextState.MapAndAnalysisAvailable => ContextAction.ShowRouteChoice,
        _ => ContextAction.ShowCompetition,
    };

    private static string TextFor(ContextState state) => state switch
    {
        ContextState.Discovered => "Visa tävling",
        ContextState.RegistrationOpen => "Anmäl dig",
        ContextState.Registered => "Förbered",
        ContextState.PmPublished => "Läs det viktigaste",
        ContextState.StartListPublished => "Visa min start",
        ContextState.RaceDay => "Navigera",
        ContextState.Live => "Följ live",
        ContextState.Finished => "Se preliminärt",
        ContextState.ResultsPublished => "Mitt resultat",
        ContextState.SplitsAvailable => "Analysera",
        ContextState.MapAndAnalysisAvailable => "Visa vägval",
        _ => "Visa tävling",
    };
}
