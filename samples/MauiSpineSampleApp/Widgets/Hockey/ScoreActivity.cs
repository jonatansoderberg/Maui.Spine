using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Widgets.Hockey;

/// <summary>
/// The game's Live Activity: a countdown before face-off, then the scoreboard. Every update replaces
/// the whole layout, so it is always built from the game in one place.
/// </summary>
public static class ScoreActivity
{
    /// <summary>A dark surface, fixed in both appearances, with the text on it fixed too.</summary>
    public static readonly WidgetColor Surface = WidgetColor.FromHex("#0F131A");
    public static readonly WidgetColor Ink = WidgetColor.FromHex("#FFFFFF");
    public static readonly WidgetColor Muted = WidgetColor.FromHex("#A6FFFFFF");
    public static readonly WidgetColor Lamp = WidgetColor.FromHex("#F5C400");

    /// <summary>
    /// How long the activity may go without an update before iOS dims it: longer than an
    /// intermission, so only an app that stopped updating looks stale.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(40);

    public static LiveActivityLayout Layout(Game game, DateTimeOffset now, Uri link)
    {
        if (game.Phase == GamePhase.Scheduled)
            return BeforeFaceOff(game, link);

        var home = game.Home;
        var away = game.Away;

        return new LiveActivityLayout
        {
            LockScreen = W.VStack(8, [Board(home, away, Centre(game, now)), Footer(game), .. NextGame(game)]).Padding(14),
            Background = Surface,
            Link = link,

            // Android's Live Update says it in words: a tree of three columns has no line that reads as a
            // title, and the badge is a picture rather than a status-bar glyph.
            Android = new LiveUpdateText(
                Title: $"{home.ShortName} {game.Score} {away.ShortName}",
                Body: string.Join(" · ", IslandLines(game, now)),
                Chip: game.Score,
                Icon: Teams.Followed.BadgeAsset),

            // Each side's badge and goals beside the camera; between them one centred text. A centre
            // made of stacks and spacers would take the width of the regions beside it.
            ExpandedLeading = W.HStack(8, W.Image(home.LogoAsset, 44).FullColor(), W.Text($"{game.HomeGoals}").Title().Bold()),
            ExpandedTrailing = W.HStack(8, W.Text($"{game.AwayGoals}").Title().Bold(), W.Image(away.LogoAsset, 44).FullColor()),
            ExpandedCenter = W.Text(string.Join('\n', IslandLines(game, now))).Caption().Centered(),

            CompactLeading = W.HStack(W.Image(home.LogoAsset, 22), W.Text($"{game.HomeGoals}").Headline().Bold()),
            CompactTrailing = W.HStack(W.Text($"{game.AwayGoals}").Headline().Bold(), W.Image(away.LogoAsset, 22)),
            Minimal = W.Text(game.Score).Caption().Bold(),

            // CarPlay's Dashboard and the Apple Watch Smart Stack (SpineWidgetsLiveActivitySmall in the
            // project): the score between the badges, and where the game is.
            Small = W.HStack(8,
                W.Image(home.LogoAsset, 28).FullColor(), W.Text(game.Score).Title().Bold().Color(Ink), W.Image(away.LogoAsset, 28).FullColor(),
                W.Spacer(), W.Text(game.PhaseText(now)).Caption().Color(Muted)),
        };
    }

    /// <summary>
    /// Before face-off: the start time where the score will be, and a countdown the system runs by
    /// itself. A timer claims all the width it is offered, so it gets a centred row of its own.
    /// </summary>
    private static LiveActivityLayout BeforeFaceOff(Game game, Uri link)
    {
        var home = game.Home;
        var away = game.Away;
        var time = game.FaceOffAt.ToLocalTime().ToString("HH:mm");
        var countdown = W.Timer(game.FaceOffAt, prefix: "Face-off in ").Caption().Color(Ink).Centered();

        return new LiveActivityLayout
        {
            LockScreen = W.VStack(8, Board(home, away, [W.Text("Face-off").Caption().Color(Muted), W.Text(time).Title().Bold().Color(Ink)]), countdown).Padding(14),
            Background = Surface,
            Link = link,

            Android = new LiveUpdateText(
                Title: $"{home.ShortName}–{away.ShortName}",
                Body: $"Face-off {time}",
                Chip: time,
                Icon: Teams.Followed.BadgeAsset),

            ExpandedLeading = W.Image(home.LogoAsset, 44).FullColor(),
            ExpandedTrailing = W.Image(away.LogoAsset, 44).FullColor(),
            ExpandedCenter = W.Text(time).Title().Bold(),
            ExpandedBottom = countdown,

            CompactLeading = W.HStack(W.Image(home.LogoAsset, 22), W.Image(away.LogoAsset, 22)),
            CompactTrailing = W.Text(time).Caption().Bold(),
            Minimal = W.Image(Teams.Followed.LogoAsset, 22),

            Small = W.HStack(8,
                W.Image(home.LogoAsset, 28).FullColor(), W.Image(away.LogoAsset, 28).FullColor(),
                W.Spacer(), W.Text(time).Title().Bold().Color(Ink)),
        };
    }

    /// <summary>
    /// Badge over name on each side and the score between, in three columns of equal width. A stack
    /// lays out from its leading edge, so every line is centred between spacers of its own.
    /// </summary>
    private static WidgetNode Board(Team home, Team away, WidgetNode[] centre) => W.HStack(0,
        Column(W.Image(home.LogoAsset, 44).FullColor(), W.Text(home.ShortName).Caption().Bold().Color(Ink)),
        Column(centre),
        Column(W.Image(away.LogoAsset, 44).FullColor(), W.Text(away.ShortName).Caption().Bold().Color(Ink)));

    private static WidgetNode[] Centre(Game game, DateTimeOffset now)
    {
        var phase = W.Text(game.IsRunning ? $"Period {game.Period}" : game.PhaseText(now)).Caption().Color(Muted);
        var score = W.Text(game.Score).Title().Bold().Color(Ink);
        return game.IsRunning
            ? [phase, score, W.Text(game.Clock(now)).Headline().Bold().Color(Ink)]
            : [phase, score];
    }

    /// <summary>One of three equal columns: filled so Android divides the row as iOS does.</summary>
    internal static WidgetNode Column(params WidgetNode[] nodes) => W.VStack(4, [.. nodes.Select(Centred)]).Fill();

    internal static WidgetNode Centred(WidgetNode node) => W.HStack(W.Spacer(), node, W.Spacer());

    private static WidgetNode Footer(Game game) => W.VStack(2,
        [Centred(W.Text(game.Shots).Caption().Color(Muted)),
         .. game.LatestGoals(2).Select(line => Centred(W.Text(line).Caption().Color(Ink)))]);

    /// <summary>
    /// At the final score, a button on the lock screen. The activity's kind is the widget's, so the tap
    /// reaches <see cref="ScoreWidget"/>'s action handler, in the app's process, without opening it.
    /// Android's Live Update has no buttons and leaves it out.
    /// </summary>
    private static IEnumerable<WidgetNode> NextGame(Game game) => game.Phase == GamePhase.Final
        ? [Centred(W.Button(ScoreWidget.NextGameAction, W.Text("Next game ›").Caption().Bold().Color(Lamp)))]
        : [];

    private static IEnumerable<string> IslandLines(Game game, DateTimeOffset now)
    {
        yield return game.PhaseText(now);
        yield return game.Shots;
        foreach (var goal in game.LatestGoals(1))
            yield return goal;
    }
}
