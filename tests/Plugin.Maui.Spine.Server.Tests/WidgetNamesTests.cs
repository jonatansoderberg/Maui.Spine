using Plugin.Maui.Spine.Common;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>MAUI-style names (#746): each writes what its older name writes, so the JSON does not change.</summary>
public class WidgetNamesTests
{
    static readonly DateTimeOffset At = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

    static string Json(WidgetNode tree) => new LiveActivityLayout { LockScreen = tree }.ToJson();

    [Fact]
    public void Label_countdown_and_elapsed_are_the_old_nodes()
    {
        Assert.Equal(Json(W.Text("Alex").Bold()), Json(W.Label("Alex").Bold()));
        Assert.Equal(Json(W.Timer(At, "Nedsläpp om ")), Json(W.Countdown(At, "Nedsläpp om ")));
        Assert.Equal(Json(W.Relative(At)), Json(W.Elapsed(At)));
        Assert.Equal(Json(W.Relative(At, compact: true)), Json(W.Elapsed(At, ElapsedFormat.Clock)));
        Assert.Equal(Json(W.Relative(At, rounded: true)), Json(W.Elapsed(At, ElapsedFormat.Minutes)));
        Assert.Equal(Json(W.Relative(At, ago: true, prefix: "Uppdaterad ")), Json(W.Elapsed(At, ElapsedFormat.Ago, "Uppdaterad ")));
    }

    [Fact]
    public void Spacing_and_colours_by_their_maui_names()
    {
        Assert.Equal(Json(W.VStack(8, W.Text("a"))), Json(W.VStack(W.Text("a")).Spacing(8)));
        Assert.Equal(Json(W.Text("a").Color(WidgetColor.Secondary)), Json(W.Text("a").TextColor(WidgetColor.Secondary)));
        Assert.Equal(Json(W.Icon("drop", WidgetColor.Green)), Json(W.Icon("drop").Tint(WidgetColor.Green)));
        Assert.Contains("\"color\":\"blue\"", Json(W.Progress(0.5).Tint(WidgetColor.Blue)));
    }

    [Fact]
    public void Font_size_takes_a_role_and_font_attributes_bold_and_italic()
    {
        Assert.Equal(Json(W.Text("a").Caption()), Json(W.Text("a").FontSize(TextRole.Caption)));
        Assert.DoesNotContain("\"size\"", Json(W.Text("a").FontSize(20).FontSize(TextRole.Caption)));

        var both = Json(W.Text("a").FontAttributes(WidgetFontAttributes.Bold | WidgetFontAttributes.Italic));
        Assert.Contains("\"bold\":true", both);
        Assert.Contains("\"italic\":true", both);
        Assert.Equal(Json(W.Text("a").Bold()), Json(W.Text("a").FontAttributes(WidgetFontAttributes.Bold)));
        Assert.DoesNotContain("italic", Json(W.Text("a").Bold()));
    }

    [Fact]
    public void Max_lines_and_shrinking_are_their_own_keys()
    {
        var json = Json(W.Text("a").MaxLines(2).ShrinkToFit(0.8));
        Assert.Contains("\"maxLines\":2", json);
        Assert.Contains("\"shrink\":0.8", json);
        Assert.Throws<ArgumentOutOfRangeException>(() => W.Text("a").MaxLines(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => W.Text("a").ShrinkToFit(1.5));
    }

    [Fact]
    public void Stacks_skip_a_child_left_out()
    {
        var hasGraph = false;
        var stack = W.HStack(W.Text("value"), hasGraph ? W.Sparkline([1, 2]) : null, W.Text("unit"));
        Assert.Equal(2, stack.Children.Count);
        Assert.Equal(2, W.VStack(4, null, W.Text("a"), W.Text("b")).Children.Count);
        Assert.Single(W.ZStack(W.Ring(0.5), null).Children);
    }

    [Fact]
    public void Defaults_are_left_out()
    {
        var json = Json(W.Text("a"));
        Assert.DoesNotContain("bold", json);
        Assert.DoesNotContain("font", json);
        Assert.DoesNotContain("italic", json);
    }
}
