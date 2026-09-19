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

    private static string Payload(LiveActivityLayout layout) => PushPayloads.ApnsLiveActivity(
        "match:p2qoh7wot5", layout, LiveActivityEvent.Start,
        new PushAlert("Nedsläpp!", "Djurgården–Björklöven har börjat."),
        new LiveActivityOptions { Channel = "dHN0LTEyMzQ1Njc4OTAxMjM0NTY=", StaleAt = DateTimeOffset.UnixEpoch.AddYears(56) },
        "se.cosmomedia.puckkoll", DateTimeOffset.UnixEpoch.AddYears(56)).Json;

    /// <summary>The same payload as the default encoder wrote it before #279, the layout inside it included.</summary>
    private static string WrittenTheOldWay(string payload)
    {
        var node = JsonNode.Parse(payload)!;
        var state = node["aps"]!["content-state"]!;
        state["json"] = JsonSerializer.Serialize(JsonNode.Parse(state["json"]!.GetValue<string>()));
        return JsonSerializer.Serialize(node);
    }

    private static int Bytes(string json) => Encoding.UTF8.GetByteCount(json);

    [Fact]
    public void The_embedded_layout_costs_about_two_bytes_per_quote()
    {
        var payload = Payload(ScoreCard());
        var before = WrittenTheOldWay(payload);

        output.WriteLine($"layout {ScoreCard().ToJson().Length} characters; payload before {Bytes(before)} bytes, now {Bytes(payload)} bytes");

        Assert.DoesNotContain("\\u0022", payload);
        Assert.Contains("Björklöven", payload);
        Assert.True(Bytes(payload) < Bytes(before) * 0.65, $"{Bytes(payload)} bytes now against {Bytes(before)} before");
        Assert.True(Bytes(payload) < 3200, $"{Bytes(payload)} bytes leaves too little of APNs' 4096");
    }

    [Fact]
    public void The_layout_reads_back_unchanged_from_the_payload()
    {
        var layout = ScoreCard();
        var payload = JsonNode.Parse(Payload(layout))!;

        var embedded = payload["aps"]!["content-state"]!["json"]!.GetValue<string>();

        Assert.Equal(layout.ToJson(), embedded);
        Assert.Equal(layout.ToJson(), WidgetJson.DeserializeLayout(embedded)!.ToJson());
    }
}
