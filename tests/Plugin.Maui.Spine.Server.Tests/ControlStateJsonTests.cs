using System.Text.Json;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>
/// A control's state is read by Swift on iOS (SpineControls.swift, ControlDocument) and back into C# by the
/// Android tile, so the names and the shape of the document are the contract.
/// </summary>
public class ControlStateJsonTests
{
    [Fact]
    public void A_toggle_is_written_with_the_names_the_native_control_reads()
    {
        var json = WidgetJson.Serialize(ControlState.Toggle("Goal alerts", true, "bell") with { Tint = WidgetColor.Orange });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("Goal alerts", root.GetProperty("title").GetString());
        Assert.Equal("bell", root.GetProperty("icon").GetString());
        Assert.True(root.GetProperty("isOn").GetBoolean());
        Assert.Equal("orange", root.GetProperty("tint").GetString());
    }

    [Fact]
    public void A_button_has_no_isOn_and_leaves_unset_fields_out()
    {
        var json = WidgetJson.Serialize(ControlState.Button("Goal Owls", "plus"));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("isOn", out _));
        Assert.False(root.TryGetProperty("status", out _));
        Assert.False(root.TryGetProperty("symbol", out _));
        Assert.False(root.TryGetProperty("tint", out _));
    }

    [Fact]
    public void A_state_comes_back_whole()
    {
        var state = ControlState.Button("Goal Owls", "plus", "NPO 2–1 RVF") with { Symbol = "hockey.puck", Tint = WidgetColor.FromHex("#1B5E3F") };

        var back = WidgetJson.DeserializeControl(WidgetJson.Serialize(state));

        Assert.Equal(state, back);
    }

    [Fact]
    public void Text_outside_ASCII_is_written_as_is()
    {
        var json = WidgetJson.Serialize(ControlState.Toggle("Målvakt", false, "bell", "Avstängd"));

        Assert.Contains("Målvakt", json);
        Assert.Contains("Avstängd", json);
    }

    [Fact]
    public void The_toggle_the_iOS_intent_flipped_reads_back_as_switched()
    {
        // SpineControlToggleIntent rewrites isOn with JSONSerialization and keeps the rest of the document.
        var flipped = """{"title":"Goal alerts","icon":"bell","isOn":false,"tint":"orange"}""";

        var back = WidgetJson.DeserializeControl(flipped);

        Assert.Equal(false, back!.IsOn);
        Assert.Equal(WidgetColor.Orange, back.Tint);
    }
}
