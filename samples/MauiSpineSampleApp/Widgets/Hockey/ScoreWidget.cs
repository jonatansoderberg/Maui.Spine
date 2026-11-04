using MauiSpineSampleApp.Pages.LiveActivities;
using MauiSpineSampleApp.Pages.Widgets;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Widgets;
using static MauiSpineSampleApp.Widgets.Hockey.ScoreActivity;

namespace MauiSpineSampleApp.Widgets.Hockey;

/// <summary>
/// The followed team's widget: the game while it is on, otherwise the next game and the latest
/// results, on the home screen and the Lock Screen. Its buttons run in the app without opening it:
/// one rebuilds the widget for a fresh game clock, one turns a finished game into the next fixture.
/// The Live Activity has the same kind, so its button reaches this handler too.
/// </summary>
[Widget(Kind)]
public sealed class ScoreWidget(LiveScore _score, IWidgetService _widgets, INavigationService _navigation)
    : IWidgetProvider, IWidgetActionHandler, IWidgetLinkHandler
{
    public const string Kind = "score";
    public const string RefreshAction = "refresh";
    public const string NextGameAction = "next";

    private static readonly WidgetColor Win = WidgetColor.FromHex("#5BD27A");
    private static readonly WidgetColor Loss = WidgetColor.FromHex("#FF6B6B");

    private static readonly WidgetGradient Ground = new(
        [WidgetColor.FromHex("#1C222D"), WidgetColor.FromHex("#0B0E13")], WidgetGradientDirection.Diagonal);

    public async Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        await TeamLogos.EnsureAsync(_widgets, cancellationToken);

        var game = _score.Game;
        var now = DateTimeOffset.Now;
        var timeline = new WidgetTimeline().Add(now, Trees(game, now));

        // The platform turns the widget to the scoreboard at face-off by itself, app running or not.
        if (game.Phase == GamePhase.Scheduled && game.FaceOffAt > now && game.FaceOffAt - now < TimeSpan.FromDays(2))
        {
            var started = game with { Phase = GamePhase.Live, Period = 1, PeriodStartedAt = game.FaceOffAt };
            timeline = timeline.Add(game.FaceOffAt, Trees(started, game.FaceOffAt));
        }

        return timeline
            .Background(Ground)
            .Refresh(game.IsRunning ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(1))
            .OpenUrl(_widgets.LinkFor(context.Kind));
    }

    public async Task OnActionAsync(WidgetAction action)
    {
        // Refresh needs nothing done: Spine rebuilds the widget when this returns, and the clock with it.
        if (action.ActionId == NextGameAction && _score.Game.Phase == GamePhase.Final)
            await _score.NextGameAsync();
    }

    /// <summary>The Live Activity's link carries <c>from=activity</c>; the widget's carries nothing.</summary>
    public Task OnWidgetOpenedAsync(WidgetLink link) => link.Url.Query.Contains("from=activity", StringComparison.Ordinal)
        ? _navigation.ShowAsync<LiveActivitiesPage>()
        : _navigation.ShowAsync<WidgetsPage>();

    internal static Dictionary<WidgetFamily, WidgetNode> Trees(Game game, DateTimeOffset now)
    {
        var next = game.Phase == GamePhase.Scheduled;
        return new()
        {
            [WidgetFamily.Small] = next ? SmallNext(game, now) : SmallLive(game, now),
            [WidgetFamily.Medium] = next ? MediumNext(game, now) : MediumLive(game, now),
            [WidgetFamily.AccessoryRectangular] = next ? RectangularNext(game, now) : RectangularLive(game, now),
            [WidgetFamily.AccessoryInline] = W.Text(next
                ? $"· {Teams.Followed.Abbreviation} vs {game.Opponent.Abbreviation} {game.When(now)}"
                : $"· {game.Home.Abbreviation} {game.Score} {game.Away.Abbreviation} · {Brief(game)}"),
            [WidgetFamily.AccessoryCircular] = next
                ? W.VStack(0, W.Text(game.Opponent.Abbreviation).Caption().Bold(), W.Text(game.FaceOffAt.ToLocalTime().ToString("HH:mm")).Caption())
                : W.VStack(0, W.Text(game.Score).Headline().Bold(), W.Text(Brief(game)).Caption()),
        };
    }

    private static string Brief(Game game) => game.Phase switch
    {
        GamePhase.Live => $"P{game.Period}",
        GamePhase.Intermission => "Int.",
        _ => "Final",
    };

    /// <summary>
    /// "P2 · 07:19" as of the last build, with a button that builds it again. WidgetKit allows a few
    /// reloads an hour, so a clock that ticks belongs in the Live Activity; the widget shows a moment.
    /// </summary>
    private static WidgetNode Phase(Game game, DateTimeOffset now)
    {
        var text = W.Text(game.PhaseText(now)).Caption().Color(Muted).Pending();
        return game.IsRunning
            ? Centred(W.HStack(4, text, W.Button(RefreshAction, W.Icon("refresh", Muted))))
            : Centred(text);
    }

    /// <summary>At the final score the game is history; the button turns the widget to the next one.</summary>
    private static IEnumerable<WidgetNode> NextGame(Game game) => game.Phase == GamePhase.Final
        ? [Centred(W.Button(NextGameAction, W.Text("Next game ›").Caption().Bold().Color(Lamp)))]
        : [];

    private static IEnumerable<WidgetNode> Goals(Game game, int count) =>
        game.LatestGoals(count).Select(line => Centred(W.Text(line).Caption().Color(Ink)));

    private static WidgetNode SmallLive(Game game, DateTimeOffset now) => W.VStack(6,
        [W.Spacer(),
         W.HStack(0,
            Column(W.Image(game.Home.LogoAsset, 38).FullColor()),
            Column(W.Text(game.Score).Title().Bold().Color(Ink)),
            Column(W.Image(game.Away.LogoAsset, 38).FullColor())),
         Phase(game, now),
         .. Goals(game, 1),
         .. NextGame(game),
         W.Spacer()]);

    /// <summary>The board with the names, then the shots and the last three goals as centred lines.</summary>
    private static WidgetNode MediumLive(Game game, DateTimeOffset now) => W.VStack(4,
        [W.HStack(0,
            Column(W.Image(game.Home.LogoAsset, 40).FullColor(), W.Text(game.Home.ShortName).Caption().Bold().Color(Ink)),
            Column(W.Text(game.Score).Title().Bold().Color(Ink)),
            Column(W.Image(game.Away.LogoAsset, 40).FullColor(), W.Text(game.Away.ShortName).Caption().Bold().Color(Ink))),
         Phase(game, now),
         Centred(W.Text(game.Shots).Caption().Color(Muted)),
         .. Goals(game, 3),
         .. NextGame(game)]);

    private static WidgetNode Header() => W.HStack(6,
        W.Image(Teams.Followed.LogoAsset, 28).FullColor(),
        W.Text(Teams.Followed.ShortName).Headline().Bold().Color(Ink),
        W.Spacer());

    private static string HomeOrAway(Game game) => game.IsHome ? "home" : "away";

    private static WidgetNode SmallNext(Game game, DateTimeOffset now) => W.VStack(4,
        Header(),
        W.Spacer(),
        W.Text($"Next · {HomeOrAway(game)}").Caption().Color(Muted),
        W.HStack(6,
            W.Image(game.Opponent.LogoAsset, 22).FullColor(),
            W.Text(game.Opponent.ShortName).Body().Bold().Color(Ink),
            W.Spacer()),
        FaceOff(game, now));

    /// <summary>A countdown the system runs once face-off is near; the day and time before that.</summary>
    private static WidgetNode FaceOff(Game game, DateTimeOffset now) => game.FaceOffAt - now < TimeSpan.FromHours(1)
        ? W.Timer(game.FaceOffAt, prefix: "Face-off in ").Caption().Bold().Color(Lamp)
        : W.Text(game.When(now)).Caption().Bold().Color(Lamp);

    /// <summary>
    /// Rows rather than columns: the team, the next game with its time at the right edge, and the
    /// latest results along the bottom.
    /// </summary>
    private static WidgetNode MediumNext(Game game, DateTimeOffset now)
    {
        var results = game.Recent.Take(3)
            .Select(result => (WidgetNode)W.Text(Game.ResultShort(result)).Caption().Bold().Color(Game.Won(result) ? Win : Loss))
            .ToArray();

        return W.VStack(4,
            Header(),
            W.Spacer(),
            W.Text($"Next · {HomeOrAway(game)}").Caption().Color(Muted),
            W.HStack(8,
                W.Image(game.Opponent.LogoAsset, 22).FullColor(),
                W.Text(game.Opponent.Name).Body().Bold().Color(Ink),
                W.Spacer(),
                FaceOff(game, now)),
            W.Spacer(),
            W.HStack(10, [W.Text("Latest").Caption().Color(Muted), W.Spacer(), .. results]));
    }

    // The Lock Screen draws in one tint over the wallpaper: text only, no colours.
    private static WidgetNode RectangularLive(Game game, DateTimeOffset now) => W.VStack(2,
        W.Text($"{game.Home.Abbreviation} {game.Score} {game.Away.Abbreviation}").Headline().Bold(),
        W.Text(game.PhaseText(now)).Caption(),
        W.Text(game.LatestGoals(1).FirstOrDefault() ?? game.Shots).Caption());

    private static WidgetNode RectangularNext(Game game, DateTimeOffset now) => W.VStack(2,
        [W.Text($"{Teams.Followed.ShortName} vs {game.Opponent.ShortName}").Headline().Bold(),
         W.Text($"{game.When(now)} · {HomeOrAway(game)}").Caption(),
         .. game.Recent.Take(1).Select(result => W.Text($"Last: {Game.ResultShort(result)}").Caption())]);
}
