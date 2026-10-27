using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using Plugin.Maui.Spine.Server;
using Xunit;
using Xunit.Abstractions;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>
/// A Live Activity layout is a JSON string inside the APNs payload, so how it is escaped decides how
/// much of the 4 KB limit it takes (#279).
/// </summary>
public class ApnsPayloadSizeTests(ITestOutputHelper output)
{
    private static readonly WidgetColor Ink = WidgetColor.FromHex("#FFFFFF");
    private static readonly WidgetColor Muted = WidgetColor.FromHex("#A6FFFFFF");

    /// <summary>
    /// A game's score card as the Puckkoll app builds it: two logos with Swedish names on the lock
    /// screen, the score, the period and the clock, shots and a power play, and the Dynamic Island.
    /// </summary>
    private static LiveActivityLayout ScoreCard()
    {
        WidgetNode Side(string logo, string name) => W.VStack(4, W.Image(logo, 48).FullColor(), W.Text(name).Caption().Bold().Color(Ink));

        return new LiveActivityLayout
        {
            LockScreen = W.VStack(10,
                W.HStack(8,
                    Side("logo-dif", "Djurgården"),
                    W.Spacer(),
                    W.VStack(2,
                        W.Text("Period 2 · 17:19").Caption().Color(Muted),
                        W.Text("2–2").Title().Bold().Color(Ink)),
                    W.Spacer(),
                    Side("logo-ifb", "Björklöven")),
                W.HStack(8,
                    W.Text("Skott 17–14").Caption().Color(Muted),
                    W.Spacer(),
                    W.HStack(W.Text("Powerplay Björklöven").Caption().Bold().Color(WidgetColor.FromHex("#111111")))
                        .Padding(5).Background(WidgetColor.FromHex("#F5C400")).CornerRadius(8))).Padding(14),
            Background = WidgetColor.FromHex("#0F131A"),
            ExpandedLeading = W.VStack(4, W.Image("logo-dif", 40).FullColor(), W.Text("Djurgården").Caption().Bold()),
            ExpandedTrailing = W.VStack(4, W.Image("logo-ifb", 40).FullColor(), W.Text("Björklöven").Caption().Bold()),
            ExpandedCenter = W.VStack(2, W.Text("2–2").Title().Bold(), W.Text("P2 · 17:19").Caption().Secondary()),
            ExpandedBottom = W.Text("Mål: Anderson-Dolan 16:13 P2").Caption(),
            CompactLeading = W.HStack(4, W.Image("logo-dif", 20).FullColor(), W.Text("2").Headline().Bold()),
            CompactTrailing = W.HStack(4, W.Text("2").Headline().Bold(), W.Image("logo-ifb", 20).FullColor()),
            Minimal = W.Text("2–2").Caption().Bold(),
        };
    }

    /// <summary>
    /// A glucose card as the Glukos app builds it: name and age, the value large and rounded with its unit and trend, and
    /// the hour as a graph three times over, on the lock screen, at the bottom of the expanded island and in CarPlay.
    /// </summary>
    private static LiveActivityLayout GlucoseCard(bool shared = false)
    {
        var at = new DateTimeOffset(2026, 10, 26, 11, 42, 17, TimeSpan.FromHours(1));
        var color = WidgetColor.FromHex("#2BB894");
        var muted = WidgetColor.FromHex("#A6FFFFFF");
        var points = Enumerable.Range(0, 60).Select(i => (at.AddMinutes(i - 59).AddSeconds(i % 7), 6.2 + 0.9 * Math.Sin(i / 9.0))).ToList();
        SparklineNode Sparkline(double height) => W.Sparkline(points, at.AddHours(-1), at, TimeSpan.FromMinutes(5), color, height)
            .Band(3.9, 10, WidgetColor.FromHex("#F5B83D"), WidgetColor.FromHex("#F2614A")).Area().Dot();
        WidgetNode Graph(double height) => shared ? W.Use("graph", height) : Sparkline(height);
        WidgetNode Title() => W.Text("Jonatan").Caption().Bold().Color(WidgetColor.FromHex("#FFFFFF"));
        WidgetNode Age() => shared ? W.Use("age") : W.Relative(at, ago: true).Caption().Color(muted).Trailing();
        WidgetNode Reading(double size, bool drop = true)
        {
            var number = W.Text("6,8").FontSize(size).Bold().FontDesign(TextDesign.Rounded).Color(color);
            var arrow = W.Text("→").FontSize(size * 0.7).Bold().Color(color);
            return drop ? W.HStack(6, W.Icon("drop", color, size * 0.55), number, arrow) : W.HStack(6, number, arrow);
        }
        var unit = W.Text("mmol/L · Stabilt").Caption().Bold().Color(muted);

        return new LiveActivityLayout
        {
            LockScreen = W.VStack(10, W.HStack(8, Title(), W.Spacer(), Age()), W.HStack(12, W.VStack(2, Reading(40), unit), Graph(56))).Padding(16),
            Background = WidgetColor.FromHex("#000000"),
            Link = new Uri("se.cosmomedia.glukos://widget/glucose?person=p2qoh7wot5"),
            ExpandedLeading = W.HStack(0, Title()).Padding(6),
            ExpandedTrailing = W.HStack(0, W.Spacer(), Age()).Padding(6),
            ExpandedBottom = W.HStack(12, W.VStack(2, Reading(30), unit), Graph(44)),
            CompactLeading = W.Icon("drop", color),
            CompactTrailing = W.Text("6,8 →").Headline().Bold().FontDesign(TextDesign.Rounded).Color(color),
            Minimal = W.Text("6,8").Headline().Bold().FontDesign(TextDesign.Rounded).Color(color),
            Small = W.HStack(12, W.VStack(2, Title(), Reading(26, drop: false), Age()), Graph(40)),
            Shared = shared
                ? new Dictionary<string, WidgetNode> { ["graph"] = Sparkline(56), ["age"] = W.Relative(at, ago: true).Caption().Color(muted).Trailing() }
                : null,
        };
    }

    [Fact]
    public void A_graph_and_an_age_sent_once_save_half_a_kilobyte_of_the_glucose_card()
    {
        var options = new LiveActivityOptions { StaleAt = DateTimeOffset.UnixEpoch.AddYears(57) };
        string Update(LiveActivityLayout layout) => PushPayloads.ApnsLiveActivity("glucose:p2qoh7wot5", layout, LiveActivityEvent.Update,
            alert: null, options, "se.cosmomedia.glukos", DateTimeOffset.UnixEpoch.AddYears(56)).Json;
        var repeated = Bytes(Update(GlucoseCard()));
        var shared = Bytes(Update(GlucoseCard(shared: true)));

        output.WriteLine($"update {repeated} bytes with every region's own graph and age, {shared} bytes with them shared");

        // 3454 against 2913 bytes when written: the graph's two repeats and the age's.
        Assert.True(repeated - shared > 500, $"{repeated - shared} of {repeated} bytes saved");
    }

    [Fact]
    public void A_shared_card_resolves_to_the_card_it_was_built_from()
    {
        var resolved = JsonNode.Parse(WidgetJson.ResolveShared(GlucoseCard(shared: true).ToJson()));

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(GlucoseCard().ToJson()), resolved), resolved!.ToJsonString());
    }

    [Fact]
    public void A_glucose_card_with_three_graphs_fits_with_room_to_spare()
    {
        var layout = GlucoseCard();
        var update = PushPayloads.ApnsLiveActivity("glucose:p2qoh7wot5", layout, LiveActivityEvent.Update, alert: null,
            new LiveActivityOptions { StaleAt = DateTimeOffset.UnixEpoch.AddYears(57) }, "se.cosmomedia.glukos", DateTimeOffset.UnixEpoch.AddYears(56)).Json;
        var start = Payload(layout);

        output.WriteLine($"layout {Bytes(layout.ToJson())} bytes; update {Bytes(update)} bytes, start with alert and channel {Bytes(start)} bytes");

        Assert.True(Bytes(update) < 3500, $"{Bytes(update)} bytes of APNs' 4096");
        Assert.True(Bytes(start) < 4096, $"{Bytes(start)} bytes of APNs' 4096");
    }

    private static string Payload(LiveActivityLayout layout) => PushPayloads.ApnsLiveActivity(
        "match:p2qoh7wot5", layout, LiveActivityEvent.Start,
        new PushAlert("Nedsläpp!", "Djurgården–Björklöven har börjat."),
        new LiveActivityOptions { Channel = "dHN0LTEyMzQ1Njc4OTAxMjM0NTY=", StaleAt = DateTimeOffset.UnixEpoch.AddYears(56) },
        "se.cosmomedia.puckkoll", DateTimeOffset.UnixEpoch.AddYears(56)).Json;

    private static int Bytes(string json) => Encoding.UTF8.GetByteCount(json);

    [Fact]
    public void The_layout_travels_as_an_object_without_escaping()
    {
        var layout = ScoreCard();
        var payload = Payload(layout);

        output.WriteLine($"layout {Bytes(layout.ToJson())} bytes, payload {Bytes(payload)} bytes");

        Assert.Equal(JsonValueKind.Object, JsonNode.Parse(payload)!["aps"]!["content-state"]!["layout"]!.GetValueKind());
        Assert.DoesNotContain("\\\"", payload);
        Assert.DoesNotContain("\\u00", payload);
        Assert.Contains("Björklöven", payload);
        // The alert, the channel and the activity's own fields around it are all a start adds.
        Assert.True(Bytes(payload) - Bytes(layout.ToJson()) < 400, $"{Bytes(payload) - Bytes(layout.ToJson())} bytes around the layout");
    }

    [Fact]
    public void A_small_family_tree_fits_beside_the_rest()
    {
        var layout = ScoreCard() with
        {
            Small = W.HStack(6,
                W.Image("logo-dif", 24).FullColor(), W.Text("2–2").Headline().Bold(), W.Image("logo-ifb", 24).FullColor(),
                W.Spacer(), W.Text("P2 · 17:19").Caption().Secondary()),
        };
        var payload = Payload(layout);

        output.WriteLine($"payload with a small tree {Bytes(payload)} bytes, without {Bytes(Payload(ScoreCard()))}");

        // A row of five nodes costs about 400 bytes; half a kilobyte is still spare.
        Assert.True(Bytes(payload) < 3600, $"{Bytes(payload)} bytes leaves too little of APNs' 4096");
    }

    [Fact]
    public void The_layout_reads_back_unchanged_from_the_payload()
    {
        var layout = ScoreCard();
        var payload = JsonNode.Parse(Payload(layout))!;

        var embedded = payload["aps"]!["content-state"]!["layout"]!.ToJsonString();

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(layout.ToJson()), JsonNode.Parse(embedded)));
        Assert.Equal(layout.ToJson(), WidgetJson.DeserializeLayout(embedded)!.ToJson());
    }
}
