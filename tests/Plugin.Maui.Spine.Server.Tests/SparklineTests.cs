using System.Text;
using System.Text.Json.Nodes;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using Plugin.Maui.Spine.Server;
using Xunit;
using Xunit.Abstractions;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>
/// <see cref="W.Sparkline(IEnumerable{ValueTuple{DateTimeOffset, double}}, DateTimeOffset, DateTimeOffset, TimeSpan?, WidgetColor?, double)"/>
/// is the graph a server can push inside a Live Activity, so what it writes has to be small enough for APNs' 4 KB.
/// </summary>
public class SparklineTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 14, 0, 0, TimeSpan.FromHours(2));
    private static readonly WidgetColor Teal = WidgetColor.FromHex("#14C8B4");

    /// <summary>An hour of glucose at one reading a minute, in mmol/L, drifting out of range and back.</summary>
    private static (DateTimeOffset, double)[] Hour() =>
        [.. Enumerable.Range(0, 60).Select(i => (Now.AddMinutes(i - 59), 6.2 + 4.1 * Math.Sin(i / 9.0) + i * 0.013))];

    [Fact]
    public void Points_land_on_an_even_axis_with_gaps_where_a_step_has_none()
    {
        var spark = W.Sparkline(
            [(Now.AddMinutes(-10), 5), (Now.AddMinutes(-9.5), 5.5), (Now.AddMinutes(-9.9), 9), (Now.AddMinutes(-7), 6), (Now.AddMinutes(-20), 1)],
            Now.AddMinutes(-10), Now, TimeSpan.FromMinutes(1));

        Assert.Equal(11, spark.Values.Count);
        Assert.Equal(5.5, spark.Values[0]);
        Assert.Null(spark.Values[1]);
        Assert.Equal(6, spark.Values[3]);
        Assert.Equal(60, spark.Step);
        Assert.Equal(Now.AddMinutes(-10), spark.From);
    }

    [Fact]
    public void Values_are_rounded_to_a_thousandth_of_their_range()
    {
        Assert.Equal([5.43, 12.68], W.Sparkline([5.4321, 12.6789]).Values);
        Assert.Equal([98, 213], W.Sparkline([98.4, 212.6]).Values);
    }

    [Fact]
    public void Band_area_and_dot_survive_the_trip()
    {
        var layout = new LiveActivityLayout
        {
            LockScreen = W.Sparkline(Hour(), Now.AddHours(-1), Now, color: Teal)
                .Band(3.9, 10, above: WidgetColor.Yellow, below: WidgetColor.Red).Area().Dot(),
        };

        var json = layout.ToJson();
        var back = Assert.IsType<SparklineNode>(WidgetJson.DeserializeLayout(json)!.LockScreen);

        Assert.Equal(json, new LiveActivityLayout { LockScreen = back }.ToJson());
        Assert.Equal((3.9, 10.0), (back.Low, back.High));
        Assert.Equal(WidgetColor.Yellow, back.AboveColor);
        Assert.True(back.Area);
        Assert.True(back.Dot);
        Assert.Contains("\"type\":\"sparkline\"", json);
        Assert.DoesNotContain("aspect", json);
    }

    [Fact]
    public void An_hour_at_one_point_a_minute_fits_a_live_activity_push()
    {
        var graph = W.Sparkline(Hour(), Now.AddHours(-1), Now, TimeSpan.FromMinutes(1), Teal, height: 56)
            .Band(3.9, 10, above: WidgetColor.FromHex("#F5C400"), below: WidgetColor.FromHex("#FF5A3C")).Area().Dot();
        var value = W.HStack(4, W.Text("7,4").Title().Bold().Color(WidgetColor.FromHex("#FFFFFF")), W.Icon("arrow.up.right", Teal));
        var layout = new LiveActivityLayout
        {
            LockScreen = W.VStack(8,
                W.HStack(8, value, W.Spacer(), W.Relative(Now, compact: true, prefix: "för ").Caption().Secondary()),
                graph).Padding(4),
            Background = WidgetColor.FromHex("#0F131A"),
            ExpandedLeading = value,
            ExpandedTrailing = W.Relative(Now, compact: true).Caption(),
            ExpandedBottom = graph,
            CompactLeading = W.Icon("drop.fill", Teal),
            CompactTrailing = W.Text("7,4 ↗").Caption().Bold(),
            Minimal = W.Text("7,4").Caption().Bold(),
        };

        var payload = PushPayloads.ApnsLiveActivity("glucose", layout, LiveActivityEvent.Update, alert: null,
            new LiveActivityOptions { StaleAt = Now.AddMinutes(20) }, "se.cosmomedia.glukos", Now).Json;
        var bytes = Encoding.UTF8.GetByteCount(payload);
        var graphBytes = Encoding.UTF8.GetByteCount(new LiveActivityLayout { LockScreen = graph }.ToJson());

        output.WriteLine($"payload {bytes} bytes, one sparkline of 60 values {graphBytes} bytes");

        // The graph twice, lock screen and expanded island, and still well inside 4096.
        Assert.True(graphBytes < 650, $"a sparkline of 60 values takes {graphBytes} bytes");
        Assert.True(bytes < 3000, $"{bytes} bytes leaves too little of APNs' 4096");
        Assert.Equal(layout.ToJson(), JsonNode.Parse(payload)!["aps"]!["content-state"]!["json"]!.GetValue<string>());
    }
}
