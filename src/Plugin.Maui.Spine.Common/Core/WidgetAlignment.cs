using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// How a stack lines its children up across its axis: a VStack along its width, an HStack along its height, a ZStack in
/// both. Start and End are the leading and trailing edges in a VStack and the top and bottom in an HStack.
/// </summary>
public enum WidgetAlignment
{
    /// <summary>The leading edge (VStack), the top (HStack), the middle of the leading edge (ZStack).</summary>
    Start,
    /// <summary>The middle; an HStack's and a ZStack's default.</summary>
    Center,
    /// <summary>The trailing edge (VStack), the bottom (HStack), the middle of the trailing edge (ZStack).</summary>
    End,
    /// <summary>An HStack's text on its first baseline. Android cannot line baselines up across views and keeps to the top.</summary>
    Baseline,
    /// <summary>A ZStack's top leading corner.</summary>
    TopStart,
    /// <summary>The middle of a ZStack's top edge.</summary>
    Top,
    /// <summary>A ZStack's top trailing corner.</summary>
    TopEnd,
    /// <summary>A ZStack's bottom leading corner.</summary>
    BottomStart,
    /// <summary>The middle of a ZStack's bottom edge.</summary>
    Bottom,
    /// <summary>A ZStack's bottom trailing corner.</summary>
    BottomEnd,
}

/// <summary>Where one child sits across a VStack, as MAUI's <c>LayoutOptions</c>; the stack's <see cref="WidgetAlignment"/> otherwise.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetLayoutOptions>))]
public enum WidgetLayoutOptions
{
    /// <summary>At the leading edge.</summary>
    [JsonStringEnumMemberName("start")] Start,
    /// <summary>In the middle.</summary>
    [JsonStringEnumMemberName("center")] Center,
    /// <summary>At the trailing edge.</summary>
    [JsonStringEnumMemberName("end")] End,
    /// <summary>Across the whole width.</summary>
    [JsonStringEnumMemberName("fill")] Fill,
}

/// <summary>How a text's lines are aligned within its width, as MAUI's <c>HorizontalTextAlignment</c>.</summary>
public enum WidgetTextAlignment
{
    /// <summary>At the leading edge, the default.</summary>
    Start,
    /// <summary>Centred.</summary>
    Center,
    /// <summary>At the trailing edge.</summary>
    End,
}

/// <summary>Bold and italic, as MAUI's <c>FontAttributes</c>.</summary>
[Flags]
public enum WidgetFontAttributes
{
    /// <summary>Regular.</summary>
    None = 0,
    /// <summary>Bold weight.</summary>
    Bold = 1,
    /// <summary>Italics.</summary>
    Italic = 2,
}

/// <summary>How <see cref="W.Elapsed"/> writes the time since a moment.</summary>
public enum ElapsedFormat
{
    /// <summary>"18 min, 35 sec", counting every second. A clock on Android.</summary>
    Precise,
    /// <summary>"18:35", for regions with no room for words.</summary>
    Clock,
    /// <summary>"5 minutes", "1 hour, 5 minutes", counting each minute. iOS 18 and later; as <see cref="Precise"/> before, a clock on Android.</summary>
    Minutes,
    /// <summary>"now", then "2 minutes ago", "3 hours ago", rounded to whole units. iOS 18 and later; as <see cref="Precise"/> before, a clock on Android.</summary>
    Ago,
}
