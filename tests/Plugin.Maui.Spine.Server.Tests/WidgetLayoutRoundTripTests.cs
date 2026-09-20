using System.Text.Json;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>
/// The layout has to survive the trip out to a device and back into a tree: on Android a Live Update
/// arrives as serialized layout in a data message and is rendered in the app's process.
/// </summary>
public class WidgetLayoutRoundTripTests
{
    private static readonly WidgetColor Brand = WidgetColor.FromHex("#1B5E3F");

    [Fact]
    public void A_styled_tree_comes_back_with_its_styling()
    {
        var layout = new LiveActivityLayout
        {
            LockScreen = W.VStack(4,
                W.Text("Gävle OK").Headline().Bold(),
                W.Text("Ute på banan").Caption().Secondary()),
        };

        var back = WidgetJson.DeserializeLayout(layout.ToJson());

        var stack = Assert.IsType<VStackNode>(back!.LockScreen);
        var first = Assert.IsType<TextNode>(stack.Children[0]);
        var second = Assert.IsType<TextNode>(stack.Children[1]);

        Assert.Equal("Gävle OK", first.Text);
        Assert.Equal(TextRole.Headline, first.Role);
        Assert.True(first.Bold);
        Assert.Equal(TextRole.Caption, second.Role);
        Assert.Equal(WidgetColor.Secondary, second.Color);
    }

    [Fact]
    public void A_centred_timer_keeps_its_prefix()
    {
        var faceOff = new DateTimeOffset(2026, 9, 19, 19, 43, 0, TimeSpan.Zero);
        var layout = new LiveActivityLayout
        {
            LockScreen = W.Timer(faceOff, prefix: "Nedsläpp om ").Caption().Centered(),
            ExpandedBottom = W.Relative(faceOff, compact: true, prefix: "Uppdaterad ").Centered(),
            ExpandedCenter = W.Text("P2 · 07:19\nSkott 12–9").Centered(),
        };

        var back = WidgetJson.DeserializeLayout(layout.ToJson())!;

        var timer = Assert.IsType<TimerNode>(back.LockScreen);
        Assert.Equal("Nedsläpp om ", timer.Prefix);
        Assert.True(timer.IsCentered);
        Assert.Equal(TextRole.Caption, timer.Role);
        Assert.Equal("Uppdaterad ", Assert.IsType<RelativeDateNode>(back.ExpandedBottom).Prefix);
        Assert.True(Assert.IsType<TextNode>(back.ExpandedCenter).IsCentered);
    }

    [Fact]
    public void Text_that_is_not_centred_says_nothing_about_it()
    {
        var json = new LiveActivityLayout { LockScreen = W.Timer(DateTimeOffset.UnixEpoch) }.ToJson();

        Assert.DoesNotContain("centered", json);
        Assert.DoesNotContain("prefix", json);
    }

    [Fact]
    public void A_filled_stack_says_so_and_a_plain_one_does_not()
    {
        var filled = new LiveActivityLayout
        {
            LockScreen = W.HStack(0, W.VStack(4, W.Text("Brynäs")).Fill(), W.VStack(4, W.Text("2–1")).Fill()),
        };

        var json = filled.ToJson();
        var back = WidgetJson.DeserializeLayout(json)!;
        var row = Assert.IsType<HStackNode>(back.LockScreen);

        Assert.True(Assert.IsType<VStackNode>(row.Children[0]).Fill);
        Assert.Equal(json, back.ToJson());
        Assert.DoesNotContain("fill", new LiveActivityLayout { LockScreen = W.VStack(4, W.Text("x")) }.ToJson());
    }

    [Fact]
    public void Every_region_and_node_kind_survives()
    {
        var layout = new LiveActivityLayout
        {
            LockScreen = W.HStack(8, W.Icon("fish", Brand), W.Spacer(), W.Divider()).Background(Brand).Padding(4).CornerRadius(6),
            ExpandedLeading = W.Icon("bell"),
            ExpandedTrailing = W.Timer(new DateTimeOffset(2026, 9, 12, 10, 24, 0, TimeSpan.Zero)).Bold(),
            ExpandedCenter = W.Text("Mitten"),
            ExpandedBottom = W.Progress(0.4, Brand),
            CompactLeading = W.Icon("clock"),
            CompactTrailing = W.Text("10:24").Caption(),
            Minimal = W.Icon("house"),
            Background = WidgetColor.Blue,
        };

        var json = layout.ToJson();

        Assert.Equal(json, WidgetJson.DeserializeLayout(json)!.ToJson());
    }

    [Fact]
    public void A_stack_keeps_its_box_and_the_layout_its_background()
    {
        var layout = new LiveActivityLayout
        {
            LockScreen = W.HStack(W.Text("Startad").Caption()).Background(Brand).Padding(6).CornerRadius(8),
            Background = WidgetColor.Blue,
        };

        var back = WidgetJson.DeserializeLayout(layout.ToJson())!;

        var stack = Assert.IsType<HStackNode>(back.LockScreen);
        Assert.Equal(Brand, stack.Background);
        Assert.Equal(6, stack.Padding);
        Assert.Equal(8, stack.CornerRadius);
        Assert.Equal(WidgetColor.Blue, back.Background);
    }

    [Fact]
    public void A_stack_without_a_box_writes_none_of_its_fields()
    {
        var json = new LiveActivityLayout { LockScreen = W.VStack(W.Text("Startad")) }.ToJson();

        Assert.DoesNotContain("padding", json);
        Assert.DoesNotContain("background", json);
        Assert.DoesNotContain("cornerRadius", json);
    }

    [Fact]
    public void A_timeline_carries_its_background_in_the_document()
    {
        using var document = JsonDocument.Parse(WidgetTimeline.Single(W.Text("Startad")).Background(Brand).ToJson());

        Assert.Equal("#1B5E3F", document.RootElement.GetProperty("background").GetString());
    }

    [Fact]
    public void Negative_padding_and_radius_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => W.VStack().Padding(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => W.VStack().CornerRadius(-1));
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("secondary")]
    [InlineData("accent")]
    [InlineData("green")]
    [InlineData("#1B5E3F")]
    public void Colors_read_back_to_what_they_were_written_as(string value)
    {
        Assert.Equal(value, WidgetColor.Parse(value).Value);
    }

    [Fact]
    public void A_colour_that_is_neither_a_name_nor_hex_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => WidgetColor.Parse("chartreuse"));
    }

    [Fact]
    public void A_timeline_writes_its_gradient_and_image_beside_the_color_field()
    {
        var json = WidgetTimeline.Single(W.Text("x"))
            .Background(new WidgetGradient([Brand, WidgetColor.Accent], WidgetGradientDirection.Diagonal))
            .BackgroundImage("bakgrund.png")
            .ToJson();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var gradient = root.GetProperty("backgroundGradient");

        Assert.False(root.TryGetProperty("background", out _));
        Assert.Equal(new[] { "#1B5E3F", "accent" }, gradient.GetProperty("colors").EnumerateArray().Select(c => c.GetString()).ToArray());
        Assert.Equal("Diagonal", gradient.GetProperty("direction").GetString());
        Assert.Equal("bakgrund.png", root.GetProperty("backgroundImage").GetString());
    }

    [Fact]
    public void A_color_and_a_gradient_replace_each_other()
    {
        var timeline = WidgetTimeline.Single(W.Text("x")).Background(new WidgetGradient([Brand, Brand])).Background(Brand);
        Assert.Equal(Brand, timeline.BackgroundColor);
        Assert.Null(timeline.BackgroundGradient);

        timeline.Background(new WidgetGradient([Brand, WidgetColor.Surface]));
        Assert.Null(timeline.BackgroundColor);
        Assert.NotNull(timeline.BackgroundGradient);
    }

    [Fact]
    public void A_gradient_needs_two_colors() =>
        Assert.Throws<ArgumentException>(() => new WidgetGradient([Brand]));

    [Fact]
    public void A_timeline_without_a_gradient_or_image_writes_neither_field()
    {
        using var document = JsonDocument.Parse(WidgetTimeline.Single(W.Text("x")).Background(Brand).ToJson());

        Assert.False(document.RootElement.TryGetProperty("backgroundGradient", out _));
        Assert.False(document.RootElement.TryGetProperty("backgroundImage", out _));
        Assert.Equal("#1B5E3F", document.RootElement.GetProperty("background").GetString());
    }

    [Fact]
    public void An_entry_writes_its_own_surface_on_the_entry()
    {
        var midnight = new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.FromHours(2));
        var json = new WidgetTimeline()
            .Background(Brand)
            .Add(midnight.AddDays(-1), W.Text("Torsdag"))
            .Add(midnight, W.Text("Midsommarafton"), new WidgetSurface(new WidgetGradient([Brand, WidgetColor.Accent], WidgetGradientDirection.Diagonal), "midsommar.png"))
            .Add(midnight.AddDays(1), W.Text("Midsommardagen"), new WidgetSurface(WidgetColor.Blue))
            .ToJson();

        using var document = JsonDocument.Parse(json);
        var entries = document.RootElement.GetProperty("entries");
        var eve = entries[1];
        var gradient = eve.GetProperty("backgroundGradient");

        Assert.Equal("#1B5E3F", document.RootElement.GetProperty("background").GetString());
        Assert.Equal(new[] { "#1B5E3F", "accent" }, gradient.GetProperty("colors").EnumerateArray().Select(c => c.GetString()).ToArray());
        Assert.Equal("Diagonal", gradient.GetProperty("direction").GetString());
        Assert.Equal("midsommar.png", eve.GetProperty("backgroundImage").GetString());
        Assert.False(eve.TryGetProperty("background", out _));
        Assert.Equal("blue", entries[2].GetProperty("background").GetString());
        Assert.False(entries[2].TryGetProperty("backgroundGradient", out _));
        Assert.False(entries[2].TryGetProperty("backgroundImage", out _));
    }

    [Fact]
    public void An_entry_without_a_surface_writes_only_its_date_and_trees()
    {
        using var document = JsonDocument.Parse(new WidgetTimeline()
            .BackgroundImage("bakgrund.png")
            .Add(DateTimeOffset.UtcNow, W.Text("x"))
            .Add(DateTimeOffset.UtcNow.AddHours(1), W.Text("y"), null)
            .ToJson());

        foreach (var entry in document.RootElement.GetProperty("entries").EnumerateArray())
            Assert.Equal(new[] { "date", "trees" }, entry.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void A_family_specific_entry_keeps_its_surface()
    {
        var timeline = new WidgetTimeline().Add(DateTimeOffset.UtcNow,
            new Dictionary<WidgetFamily, WidgetNode> { [WidgetFamily.Small] = W.Text("s"), [WidgetFamily.Medium] = W.Text("m") },
            new WidgetSurface("jul.png"));

        var entry = Assert.Single(timeline.Entries);
        Assert.Equal("jul.png", entry.Surface?.Image);
        Assert.Null(entry.Surface?.Color);
        Assert.Null(entry.Surface?.Gradient);

        using var document = JsonDocument.Parse(timeline.ToJson());
        var written = document.RootElement.GetProperty("entries")[0];
        Assert.Equal("jul.png", written.GetProperty("backgroundImage").GetString());
        Assert.True(written.GetProperty("trees").TryGetProperty("medium", out _));
    }

    [Fact]
    public void A_surface_is_a_color_or_a_gradient_and_needs_a_real_image_id()
    {
        var colored = new WidgetSurface(Brand, "bild.png");
        Assert.Equal(Brand, colored.Color);
        Assert.Null(colored.Gradient);
        Assert.Equal("bild.png", colored.Image);

        var graded = new WidgetSurface(new WidgetGradient([Brand, WidgetColor.Blue]));
        Assert.Null(graded.Color);
        Assert.NotNull(graded.Gradient);
        Assert.Null(graded.Image);

        Assert.Throws<ArgumentNullException>(() => new WidgetSurface((WidgetGradient)null!));
        Assert.Throws<ArgumentException>(() => new WidgetSurface(" "));
        Assert.Throws<ArgumentException>(() => new WidgetSurface(Brand, ""));
    }

    [Fact]
    public void Accented_and_full_color_survive_and_are_absent_when_unset()
    {
        var layout = new LiveActivityLayout
        {
            LockScreen = W.VStack(4, W.Text("Ledare").Accented(), W.Image("logo.png").FullColor(), W.Text("Plain")),
        };

        var json = layout.ToJson();
        var back = Assert.IsType<VStackNode>(WidgetJson.DeserializeLayout(json)!.LockScreen);

        Assert.True(back.Children[0].Accented);
        Assert.True(Assert.IsType<ImageNode>(back.Children[1]).FullColor);
        Assert.Null(back.Children[2].Accented);
        Assert.Single(json.Split("\"accented\"").Skip(1));
    }

    [Fact]
    public void The_system_colors_parse_and_come_back()
    {
        Assert.Equal(WidgetColor.Surface, WidgetColor.Parse("surface"));
        Assert.Equal(WidgetColor.OnAccent, WidgetColor.Parse("onAccent"));

        var layout = new LiveActivityLayout { LockScreen = W.Text("x").Color(WidgetColor.OnAccent), Background = WidgetColor.Surface };
        var back = WidgetJson.DeserializeLayout(layout.ToJson())!;

        Assert.Equal(WidgetColor.OnAccent, Assert.IsType<TextNode>(back.LockScreen).Color);
        Assert.Equal(WidgetColor.Surface, back.Background);
    }

    [Fact]
    public void System_background_and_action_color_survive_and_are_absent_when_unset()
    {
        var layout = new LiveActivityLayout { LockScreen = W.Text("x"), SystemBackground = true, ActionColor = WidgetColor.Green };
        var back = WidgetJson.DeserializeLayout(layout.ToJson())!;

        Assert.True(back.SystemBackground);
        Assert.Equal(WidgetColor.Green, back.ActionColor);

        var plain = new LiveActivityLayout { LockScreen = W.Text("x") }.ToJson();
        Assert.DoesNotContain("systemBackground", plain);
        Assert.DoesNotContain("actionColor", plain);
    }
}
