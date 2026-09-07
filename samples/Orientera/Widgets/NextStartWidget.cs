using System.Web;
using Orientera.Domain;
using Orientera.Features.Events.Participants;
using Orientera.Features.Home;
using Orientera.Presentation;
using Orientera.Services.Context;
using Orientera.Services.Sources;
using Orientera.Services.Time;
using Plugin.Maui.Spine.Widgets;

namespace Orientera.Widgets;

/// <summary>
/// "Nästa start" på hemskärmen: nästa tävling jag är anmäld till, med min starttid som nedräkning.
/// Ett tryck öppnar startlistan för samma tävling.
/// </summary>
[Widget("next-start")]
public sealed class NextStartWidget(
    IClock _clock,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    CompetitionContextService _context,
    IWidgetService _widgets,
    INavigationService _navigation) : IWidgetProvider, IWidgetLinkHandler
{
    public async Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var now = _clock.Now;
        var next = await NextForMeAsync(now, cancellationToken);

        if (next is null)
            return WidgetTimeline
                .Single(Nothing())
                .Refresh(TimeSpan.FromHours(6))
                .OpenUrl(_widgets.LinkFor(context.Kind));

        // Ett anrop, två svar: EvaluateAsync hade hämtat samma indata en gång till.
        var input = await _context.BuildInputAsync(next, cancellationToken);
        var decision = ContextEngine.Evaluate(input);
        var start = input.MyStartTime;

        var timeline = new WidgetTimeline();

        timeline.Add(now, Trees(next, decision, start, started: start <= now));

        // Två poster i stället för en omladdning: WidgetKit byter träd vid starttiden av sig självt,
        // och omladdningarna är budgeterade (förstudien §4.3b).
        if (start is { } startTime && startTime > now)
            timeline.Add(startTime, Trees(next, decision, start, started: true));

        return timeline
            .Refresh(TimeSpan.FromMinutes(30))
            .OpenUrl(new Uri($"{_widgets.LinkFor(context.Kind)}?competition={Uri.EscapeDataString(next.Id.Value)}"));
    }

    public Task OnWidgetOpenedAsync(WidgetLink link) =>
        HttpUtility.ParseQueryString(link.Url.Query)["competition"] is { Length: > 0 } competition
            ? _navigation.NavigateToAsync<ParticipantsPage, ParticipantsTarget>(
                new ParticipantsTarget(CompetitionId.From(competition), Mode: ParticipantMode.StartList))
            : _navigation.SwitchToTabAsync<HomePage>();

    /// <summary>The next competition I have entered that has not finished — the same rule as "Nästa för dig" on Hem.</summary>
    private async Task<Competition?> NextForMeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var me = await _people.GetMeAsync(cancellationToken);
        var entries = await _participation.GetEntriesAsync(cancellationToken);
        var mine = entries.Where(e => e.Person == me.Id).Select(e => e.Competition).ToHashSet();

        return (await _events.GetCompetitionsAsync(cancellationToken))
            .Where(c => mine.Contains(c.Id) && c.LastFinish > now)
            .OrderBy(c => c.FirstStart)
            .FirstOrDefault();
    }

    private static Dictionary<WidgetFamily, WidgetNode> Trees(
        Competition competition, ContextDecision decision, DateTimeOffset? start, bool started)
    {
        // Nedräkningen måste vara en Timer-nod: systemet ritar den varje sekund utan att
        // extensionet körs, medan en text appen räknar ut står stilla till nästa omladdning.
        TextLikeNode headline = (start, started) switch
        {
            ({ } time, false) => W.Timer(time).Title().Bold().Color(Brand),
            ({ } time, true) => W.Text($"Startade {Format.Clock(time)}").Headline().Bold().Color(Brand),
            _ => W.Text(decision.StateText).Headline().Bold().Color(Brand),
        };

        var when = start is { } mine
            ? $"Din start {Format.Clock(mine)}"
            : $"Första start {Format.Clock(competition.FirstStart)}";

        return new Dictionary<WidgetFamily, WidgetNode>
        {
            [WidgetFamily.Small] = W.VStack(6,
                Header(),
                W.Text(competition.Name).Caption().Secondary(),
                headline),

            [WidgetFamily.Medium] = W.VStack(6,
                W.HStack(W.Icon("figure.run", Brand), W.Text(competition.Name).Headline().Bold(), W.Spacer()),
                W.Text($"{Format.Discipline(competition.Discipline)} · {competition.Place}").Caption().Secondary(),
                // Utan starttid är rubriken tävlingens läge, och "Startar om PM publicerat" är ingen mening.
                start is null
                    ? headline
                    : W.HStack(4, W.Text(started ? "Ute på banan" : "Startar om").Body(), headline),
                W.Text(when).Caption().Secondary()),
        };
    }

    private static WidgetNode Nothing() => W.VStack(6,
        Header(),
        W.Text("Ingen anmälan på gång").Headline().Bold(),
        W.Text("Öppna kalendern för att hitta nästa tävling.").Caption().Secondary());

    private static WidgetNode Header() =>
        W.HStack(W.Icon("figure.run", Brand), W.Text("Nästa start").Caption().Secondary(), W.Spacer());

    /// <summary>BrandTint ur appens ljusa tema — den gröna som klarar 3:1 mot både ljus och mörk bakgrund.</summary>
    private static WidgetColor Brand { get; } = WidgetColor.From((Color)new Resources.Styles.LightTheme()["BrandTint"]);
}
