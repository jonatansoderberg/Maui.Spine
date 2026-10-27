using System.Text.Json.Nodes;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>
/// A Live Activity layout's shared nodes travel once and are put in place where a region uses them: by the widget
/// extension on iOS, and by <see cref="WidgetJson.ResolveShared"/> on Android.
/// </summary>
public class SharedNodeTests
{
    private static readonly SparklineNode Graph = W.Sparkline([5.1, 5.8, null, 6.4], height: 56);

    [Fact]
    public void A_use_takes_the_shared_node_with_its_own_height_over_the_node_s()
    {
        var layout = new LiveActivityLayout
        {
            LockScreen = W.VStack(W.Text("6,4"), W.Use("graph")),
            ExpandedBottom = W.Use("graph", height: 44),
            Shared = new Dictionary<string, WidgetNode> { ["graph"] = Graph },
        };

        var resolved = JsonNode.Parse(WidgetJson.ResolveShared(layout.ToJson()))!;

        Assert.Null(resolved["shared"]);
        Assert.True(JsonNode.DeepEquals(Json(Graph), resolved["lockScreen"]!["children"]![1]));
        Assert.True(JsonNode.DeepEquals(Json(Graph with { Height = 44 }), resolved["expandedBottom"]));
    }

    [Fact]
    public void A_layout_without_shared_nodes_is_left_as_it_is()
    {
        var json = new LiveActivityLayout { LockScreen = W.Text("Gävle OK") }.ToJson();

        Assert.Same(json, WidgetJson.ResolveShared(json));
    }

    [Fact]
    public void Uses_and_shared_nodes_read_back_from_their_json()
    {
        var layout = new LiveActivityLayout { Small = W.Use("graph", 36), Shared = new Dictionary<string, WidgetNode> { ["graph"] = Graph } };

        var back = WidgetJson.DeserializeLayout(layout.ToJson())!;

        var use = Assert.IsType<UseNode>(back.Small);
        Assert.Equal("graph", use.Name);
        Assert.Equal(36, use.Height);
        Assert.Equal(Graph.Values, Assert.IsType<SparklineNode>(back.Shared!["graph"]).Values);
        Assert.Contains("\"type\":\"use\",\"ref\":\"graph\",\"height\":36", layout.ToJson());
    }

    [Fact]
    public void A_use_of_a_node_the_layout_does_not_share_is_refused()
    {
        var layout = new LiveActivityLayout { LockScreen = W.HStack(W.Use("graf")), Shared = new Dictionary<string, WidgetNode> { ["graph"] = Graph } };

        var error = Assert.Throws<ArgumentException>(() => layout.ToJson());
        Assert.Contains("\"graf\"", error.Message);
    }

    [Fact]
    public void A_shared_node_that_uses_another_is_refused()
    {
        var layout = new LiveActivityLayout
        {
            LockScreen = W.Use("card"),
            Shared = new Dictionary<string, WidgetNode> { ["graph"] = Graph, ["card"] = W.VStack(W.Use("graph")) },
        };

        Assert.Throws<ArgumentException>(() => layout.ToJson());
    }

    private static JsonNode Json(WidgetNode node) =>
        JsonNode.Parse(new LiveActivityLayout { LockScreen = node }.ToJson())!["lockScreen"]!;
}
