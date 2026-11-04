using System.Text.Json.Nodes;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>Overrides by platform, surface and size (#749), and what Explain says of a tree.</summary>
public class WidgetOverrideTests
{
    static readonly DateTimeOffset At = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

    static JsonObject Node(WidgetNode node) => (JsonObject)JsonNode.Parse(new LiveActivityLayout { LockScreen = node }.ToJson())!["lockScreen"]!;

    [Fact]
    public void An_override_is_written_as_when_and_a_patch()
    {
        var json = Node(W.Label("9,3").FontSize(40).On(WidgetPlatform.Android, n => n.FontSize(36)));
        Assert.Equal(40, json["size"]!.GetValue<double>());
        var entry = json["on"]![0]!;
        Assert.Equal("android", entry["when"]!["platform"]!.GetValue<string>());
        Assert.Equal(36, entry["set"]!["size"]!.GetValue<double>());
        Assert.Single(entry["set"]!.AsObject());
    }

    [Fact]
    public void Surfaces_combine_and_overrides_keep_their_order()
    {
        var node = W.Elapsed(At).On(WidgetPlace.Island | WidgetPlace.CarPlay, n => n.Caption()).On(WidgetPlace.Notification, n => n.Hidden());
        var json = Node(node);
        Assert.Equal("island,carPlay", json["on"]![0]!["when"]!["surface"]!.GetValue<string>());
        Assert.True(json["on"]![1]!["set"]!["hidden"]!.GetValue<bool>());
    }

    [Fact]
    public void A_property_taken_away_is_written_as_null_and_children_cannot_change()
    {
        var json = Node(W.Label("a").Bold().On(WidgetPlatform.iOS, n => n with { Bold = false }));
        Assert.True(json["on"]![0]!["set"]!.AsObject().ContainsKey("bold"));
        Assert.Null(json["on"]![0]!["set"]!["bold"]);
        Assert.Throws<ArgumentException>(() => W.VStack(W.Label("a")).On(WidgetPlatform.Android, n => W.VStack(W.Label("b"))));
    }

    [Fact]
    public void Applied_where_it_matches_and_hidden_nodes_go()
    {
        var tree = Node(W.VStack(W.Label("Alex"), W.Elapsed(At).On(WidgetPlace.Notification, n => n.Hidden()), W.Label("9,3").FontSize(40).On(WidgetPlatform.Android, n => n.FontSize(36))));
        var android = WidgetOverrides.Apply(tree, new DrawPlace("android", ["notification"], null, 17))!;
        Assert.Equal(2, android["children"]!.AsArray().Count);
        Assert.Equal(36, android["children"]![1]!["size"]!.GetValue<double>());
        Assert.Null(android["children"]![1]!["on"]);

        var ios = WidgetOverrides.Apply(tree, new DrawPlace("ios", ["lockScreen"], null, 26))!;
        Assert.Equal(3, ios["children"]!.AsArray().Count);
        Assert.Equal(40, ios["children"]![2]!["size"]!.GetValue<double>());
    }

    [Fact]
    public void Size_and_version_conditions()
    {
        var tree = Node(W.Label("names").On(WidgetFamily.Small, n => n.MaxLines(1)).On(null, null, null, 18, n => n.Italic()));
        Assert.Equal(1, WidgetOverrides.Apply(tree, new DrawPlace("ios", ["homeScreen"], "small", 17))!["maxLines"]!.GetValue<int>());
        Assert.Null(WidgetOverrides.Apply(tree, new DrawPlace("ios", ["homeScreen"], "medium", 17))!["maxLines"]);
        Assert.Null(WidgetOverrides.Apply(tree, new DrawPlace("ios", ["homeScreen"], "small", 17))!["italic"]);
        Assert.True(WidgetOverrides.Apply(tree, new DrawPlace("ios", ["homeScreen"], "small", 18))!["italic"]!.GetValue<bool>());
    }

    [Fact]
    public void Explain_names_overrides_spines_rules_and_what_android_cannot_draw()
    {
        var layout = new LiveActivityLayout
        {
            LockScreen = W.VStack(
                W.HStack(W.Label("Alex"), W.Spacer(), W.Elapsed(At, ElapsedFormat.Ago)).AlignBaselines(),
                W.Label("9,3").FontSize(40).FontDesign(TextDesign.Rounded).On(WidgetPlatform.Android, n => n.FontSize(36))).Padding(16),
        };
        var android = layout.Explain(WidgetPlatform.Android);
        Assert.Contains(android, l => l.Contains("size 36"));
        Assert.Contains(android, l => l.Contains("padding is dropped"));
        Assert.Contains(android, l => l.Contains("relative") && l.Contains("header counts it"));
        Assert.Contains(android, l => l.Contains("tops, not the baselines"));
        Assert.Contains(android, l => l.Contains("default font"));
        Assert.Contains(android, l => l.Contains("drawn as a clock"));
        Assert.DoesNotContain(layout.Explain(WidgetPlatform.iOS), l => l.Contains("header counts it"));
    }

    [Fact]
    public void Keeping_the_age_in_the_notification_is_honoured()
    {
        var layout = new LiveActivityLayout { LockScreen = W.VStack(W.Label("Alex"), W.Elapsed(At).On(WidgetPlace.Notification, n => n.Hidden(false))) };
        Assert.DoesNotContain(layout.Explain(WidgetPlatform.Android), l => l.Contains("header counts it"));
    }
}
