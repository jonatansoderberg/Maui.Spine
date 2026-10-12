using System.ComponentModel;
using System.Globalization;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// How often a highlight plays: once, a number of times, until it is turned off, or until the view is tapped.
/// The default value means the kind's own default: <see cref="Forever"/> for Edge, Trace and Breathe, which
/// show a state, and <see cref="Once"/> for the others, which are hints.
/// </summary>
/// <example>
/// <code>
/// &lt;Button Highlight.Kind="Pulse" Highlight.Repeat="UntilTapped" /&gt;
/// &lt;Button Highlight.Kind="Sheen" Highlight.Repeat="3" /&gt;
/// </code>
/// </example>
[TypeConverter(typeof(HighlightRepeatConverter))]
public readonly record struct HighlightRepeat
{
    /// <summary>One cycle.</summary>
    public static HighlightRepeat Once => Times(1);

    /// <summary>Until <see cref="Highlight.IsActiveProperty"/> turns false or the kind is cleared.</summary>
    public static HighlightRepeat Forever { get; } = new() { Count = -1 };

    /// <summary>Until the view is tapped (or turned off). Marks the key seen when it ends by the tap.</summary>
    public static HighlightRepeat UntilTapped { get; } = new() { Count = -1, EndsOnTap = true };

    /// <summary><paramref name="count"/> cycles, at least one.</summary>
    public static HighlightRepeat Times(int count) => new() { Count = Math.Max(1, count) };

    /// <summary>The number of cycles; -1 for no end of its own, 0 for the kind's default.</summary>
    public int Count { get; private init; }

    /// <summary>Whether a tap on the view ends it.</summary>
    public bool EndsOnTap { get; private init; }

    /// <summary>Reads "Once", "Forever", "UntilTapped" or a number of cycles, ignoring case.</summary>
    /// <exception cref="FormatException">The text is none of those.</exception>
    public static HighlightRepeat Parse(string text)
    {
        var value = text.Trim();
        if (value.Equals(nameof(Once), StringComparison.OrdinalIgnoreCase))
            return Once;
        if (value.Equals(nameof(Forever), StringComparison.OrdinalIgnoreCase))
            return Forever;
        if (value.Equals(nameof(UntilTapped), StringComparison.OrdinalIgnoreCase))
            return UntilTapped;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
            return Times(count);

        throw new FormatException($"'{text}' is not a highlight repeat. Use Once, Forever, UntilTapped or a number of cycles above zero.");
    }

    /// <summary>"Once", "Forever", "UntilTapped", the number of cycles, or "Default".</summary>
    public override string ToString() => this switch
    {
        { EndsOnTap: true } => nameof(UntilTapped),
        { Count: < 0 } => nameof(Forever),
        { Count: 0 } => "Default",
        { Count: 1 } => nameof(Once),
        _ => Count.ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>Converts XAML text such as "UntilTapped" or "3" to a <see cref="HighlightRepeat"/>.</summary>
public sealed class HighlightRepeatConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text ? HighlightRepeat.Parse(text) : base.ConvertFrom(context, culture, value);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        value is HighlightRepeat repeat && destinationType == typeof(string)
            ? repeat.ToString()
            : base.ConvertTo(context, culture, value, destinationType);
}
