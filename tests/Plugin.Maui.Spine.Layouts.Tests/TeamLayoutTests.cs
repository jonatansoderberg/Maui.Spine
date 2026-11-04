using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Layouts.Tests;

/// <summary>A widget layout with every structure and markup extension, against the same tree written with <see cref="W"/>.</summary>
public class TeamLayoutTests
{
    static SpineStrings Strings(string culture)
    {
        var strings = new SpineStrings().AddProvider(new XmlStringProvider()
            .Add("", () => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Layouts", "Strings", "strings.xml")))
            .Add("sv", () => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Layouts", "Strings", "strings.sv.xml"))));
        strings.Culture = new System.Globalization.CultureInfo(culture);
        return strings;
    }

    static readonly WidgetColor Ink = WidgetColor.Pair("#1C1C1E", "#FFFFFF");

    static WidgetNode Expected(TeamModel m, ISpineStrings s)
    {
        WidgetNode Card(string title, WidgetNode?[] content) =>
            W.VStack([W.Label(title).Headline().Bold().Italic().TextColor(Ink), .. content]).Spacing(6).Padding(12, 8);

        IEnumerable<WidgetNode?> goals = m.Goals.Count == 0
            ? [W.Label(s["NoGoals"]).Caption().TextColor(WidgetColor.Secondary)]
            : m.Goals.Take(2).Select(g => W.Label($"{g.Scorer} {g.Minute:00}'").Caption());

        var row = W.HStack(
                W.Icon(m.Mark, WidgetColor.Pair("#1C1C1E", "#FFFFFF")).Decorative(),
                W.Label(m.Next ?? s["NoMoreGames"]).MaxLines(2).Margin(0, 2, 0, 0).On(WidgetPlatform.Android, n => n.FontSize(12)),
                W.Label(m.Venue is null ? "" : "@ " + m.Venue).TextColor(WidgetColor.Primary).On(WidgetPlatform.iOS, n => n.TextColor(WidgetColor.Secondary)))
            .Evenly();

        var tree = W.VStack(
                Card(m.TeamName, [m.Live is null ? null : W.Label($"{m.Live.HomeGoals}–{m.Live.AwayGoals}").Title().TextColor(m.IsWinning ? WidgetColor.Green : WidgetColor.Red), .. goals]),
                W.Label(s.Get("Goals", m.GoalCount)),
                m.IsLive ? row.Background(WidgetColor.Red) : row,
                W.Label(m.Venue ?? $"Home of {m.TeamName}").Caption(),
                W.Label(m.Next ?? $"v {m.GoalCount}").Caption(),
                m.IsFinal ? W.Button("next-game", W.Label(s["NextGame"]).Caption()) : null)
            .Spacing(4);

        return W.Adaptive(tree, new Dictionary<WidgetFamily, WidgetNode>
        {
            [WidgetFamily.AccessoryCircular] = W.Ring(m.Share, WidgetColor.Accent, 40).Description(s.Get("Shots", m.HomeShots, m.AwayShots)),
        });
    }

    static TeamModel Live() => new("Owls", new LiveGame(3, 2), [new("Ek", 4), new("Berg", 17), new("Lind", 33)], 3, true, false, true,
        "ArrowUpRight", "Bears, Friday", "Arena", 0.62, 31, 24) { Badge = "badge-owls" };

    static TeamModel Between() => new("Owls", null, [], 0, false, true, false, "ArrowDownRight", null, null, null, 0, 0);

    public static TheoryData<string, string> Cases => new() { { "live", "" }, { "live", "sv" }, { "between", "" }, { "between", "sv" } };

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_file_writes_the_tree_written_in_CSharp(string game, string culture)
    {
        var model = game == "live" ? Live() : Between();
        var strings = Strings(culture);
        Assert.Equal(WidgetJsonOf(Expected(model, strings)), WidgetJsonOf(TeamLayout.Build(model, strings)));
    }

    [Fact]
    public void The_background_is_the_gradient_with_the_picture_over_it()
    {
        var surface = TeamLayout.Surface(Live(), Strings(""));
        Assert.Equal(["#1C222D", "#0B0E13"], surface.Gradient!.Colors.Select(c => c.Value));
        Assert.Equal("badge-owls", surface.Image);
        Assert.Null(TeamLayout.Surface(Between(), Strings("")).Image);
    }

    [Fact]
    public void A_surface_that_follows_the_model_is_each_entrys_own()
    {
        var now = DateTimeOffset.UtcNow;
        var timeline = TeamLayout.Timeline([(now, Live()), (now.AddHours(2), Between())], Strings("sv"));
        Assert.Equal(2, timeline.Entries.Count);
        Assert.Equal("badge-owls", timeline.Entries[0].Surface!.Image);
        Assert.NotNull(timeline.Entries[1].Surface!.Gradient);
        Assert.Null(timeline.BackgroundGradient);
    }

    [Fact]
    public void A_surface_that_does_not_is_the_timelines_once()
    {
        var now = DateTimeOffset.UtcNow;
        var timeline = PaperLayout.Timeline([(now, Live()), (now.AddDays(1), Between())]);
        Assert.Equal("#102030", timeline.BackgroundColor?.Value);
        Assert.Equal("paper", timeline.BackgroundAsset);
        Assert.All(timeline.Entries, e => Assert.Null(e.Surface));
    }

    [Fact]
    public void Actions_and_icons_are_named()
    {
        Assert.Equal("next-game", TeamLayout.Actions.NextGame);
        Assert.Equal(["ArrowDownRight", "ArrowUpRight"], TeamLayout.Icons.Order());
    }

    static string WidgetJsonOf(WidgetNode node) => new WidgetTimeline().Add(DateTimeOffset.UnixEpoch, node).ToJson();
}
