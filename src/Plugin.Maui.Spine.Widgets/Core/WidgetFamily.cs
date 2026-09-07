using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Widgets;

/// <summary>The sizes a widget can be placed in. Names follow WidgetKit; other platforms map the nearest size.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetFamily>))]
public enum WidgetFamily
{
    /// <summary>A square home-screen widget.</summary>
    Small,
    /// <summary>A wide home-screen widget, two squares side by side.</summary>
    Medium,
    /// <summary>A tall home-screen widget, four squares.</summary>
    Large,
    /// <summary>The widest home-screen widget (iPad and Mac only).</summary>
    ExtraLarge,
    /// <summary>A circular lock-screen accessory.</summary>
    AccessoryCircular,
    /// <summary>A rectangular lock-screen accessory.</summary>
    AccessoryRectangular,
    /// <summary>A single line of text above the lock-screen clock.</summary>
    AccessoryInline,
}
