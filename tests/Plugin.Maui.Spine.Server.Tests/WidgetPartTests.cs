using Plugin.Maui.Spine.Common;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>Parts (#750): what a node is, written as a key, and what Explain says of them.</summary>
public class WidgetPartTests
{
    static readonly DateTimeOffset At = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parts_and_an_icons_alternative_are_written()
    {
        var json = new LiveActivityLayout
        {
            LockScreen = W.VStack(W.Label("Alex").Part(ActivityPart.Subject), W.HStack(W.Label("9,3").Part(ActivityPart.Value), W.Icon("ArrowDownRight").AltText("↘").Part(ActivityPart.Symbol))),
        }.ToJson();
        Assert.Contains("\"part\":\"subject\"", json);
        Assert.Contains("\"part\":\"value\"", json);
        Assert.Contains("\"altText\":\"↘\"", json);
    }

    [Fact]
    public void Explain_warns_of_a_part_twice_and_of_no_notification_text()
    {
        var twice = new LiveActivityLayout { LockScreen = W.VStack(W.Label("a").Part(ActivityPart.Value), W.Label("b").Part(ActivityPart.Value)) };
        Assert.Contains(twice.Explain(WidgetPlatform.iOS), l => l.Contains("two nodes are Part value"));

        var silent = new LiveActivityLayout { LockScreen = W.VStack(W.Label("a"), W.Elapsed(At)) };
        Assert.Contains(silent.Explain(WidgetPlatform.Android), l => l.Contains("no Notification text"));
        Assert.DoesNotContain(twice.Explain(WidgetPlatform.Android), l => l.Contains("no Notification text"));
    }
}

/// <summary>Accessibility (#751): descriptions and decoration, and what Explain says of silent pictures.</summary>
public class WidgetAccessibilityTests
{
    [Fact]
    public void Description_and_decorative_are_written()
    {
        var json = new LiveActivityLayout
        {
            LockScreen = W.VStack(W.Icon("ArrowDownRight").Description("sjunker"), W.Image("logo-dif").Decorative()).Description("Alex, 9,3, sjunker"),
        }.ToJson();
        Assert.Contains("\"description\":\"sjunker\"", json);
        Assert.Contains("\"decorative\":true", json);
        Assert.Contains("\"description\":\"Alex, 9,3, sjunker\"", json);
    }

    [Fact]
    public void A_picture_without_words_is_named_silent()
    {
        var layout = new LiveActivityLayout { LockScreen = W.HStack(W.Icon("drop"), W.Icon("Warning").Description("varning"), W.Image("logo").Decorative()) };
        var lines = layout.Explain(WidgetPlatform.iOS);
        Assert.Single(lines, l => l.Contains("says nothing to a screen reader"));
    }
}
