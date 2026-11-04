using Plugin.Maui.Spine.Common;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>One alignment model (#745): a stack's alignment, one child's place in a VStack, and text alignment.</summary>
public class WidgetAlignmentTests
{
    static string Json(WidgetNode tree) => new LiveActivityLayout { LockScreen = tree }.ToJson();

    [Theory]
    [InlineData(WidgetAlignment.Start, "start")]
    [InlineData(WidgetAlignment.Center, "center")]
    [InlineData(WidgetAlignment.End, "end")]
    public void Every_stack_takes_start_center_and_end(WidgetAlignment alignment, string written)
    {
        Assert.Contains($"\"align\":\"{written}\"", Json(W.VStack(W.Text("a")).Alignment(alignment)));
        Assert.Contains($"\"align\":\"{written}\"", Json(W.HStack(W.Text("a")).Alignment(alignment)));
        Assert.Contains($"\"align\":\"{written}\"", Json(W.ZStack(W.Text("a")).Alignment(alignment)));
    }

    [Fact]
    public void Baseline_is_for_an_hstack_and_corners_for_a_zstack()
    {
        Assert.Contains("\"align\":\"baseline\"", Json(W.HStack(W.Text("a")).Alignment(WidgetAlignment.Baseline)));
        Assert.Contains("\"align\":\"topStart\"", Json(W.ZStack(W.Text("a")).Alignment(WidgetAlignment.TopStart)));
        Assert.Contains("\"align\":\"bottomEnd\"", Json(W.ZStack(W.Text("a")).Alignment(WidgetAlignment.BottomEnd)));
        Assert.Throws<ArgumentException>(() => W.VStack().Alignment(WidgetAlignment.Baseline));
        Assert.Throws<ArgumentException>(() => W.HStack().Alignment(WidgetAlignment.TopStart));
    }

    [Fact]
    public void The_old_names_write_what_they_did()
    {
        Assert.Contains("\"align\":\"center\"", Json(W.VStack(W.Text("a")).CenterItems()));
        Assert.Contains("\"align\":\"baseline\"", Json(W.HStack(W.Text("a")).AlignBaselines()));
        Assert.Contains("\"fill\":true", Json(W.HStack(W.VStack(W.Text("a")).Expand())));
    }

    [Fact]
    public void One_child_is_placed_on_its_own_across_a_vstack()
    {
        var json = Json(W.VStack(W.Text("left"), W.Timer(DateTimeOffset.UnixEpoch).HorizontalOptions(WidgetLayoutOptions.Center), W.Divider().HorizontalOptions(WidgetLayoutOptions.Fill)));
        Assert.Contains("\"hAlign\":\"center\"", json);
        Assert.Contains("\"hAlign\":\"fill\"", json);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"hAlign\":\"center\""));
    }

    [Fact]
    public void Text_alignment_writes_the_existing_keys()
    {
        Assert.Contains("\"centered\":true", Json(W.Text("a").HorizontalTextAlignment(WidgetTextAlignment.Center)));
        Assert.Contains("\"trailing\":true", Json(W.Text("a").HorizontalTextAlignment(WidgetTextAlignment.End)));
        var start = Json(W.Text("a").Centered().HorizontalTextAlignment(WidgetTextAlignment.Start));
        Assert.DoesNotContain("centered", start);
        Assert.DoesNotContain("trailing", start);
    }
}
