using System.Globalization;
using System.Xml.Linq;

namespace Orientera.Backend.Eventor;

/// <summary>
/// Reading helpers for Eventor's XML. Everything matches on local name so a namespaced
/// response parses the same as an unnamespaced one, and every accessor tolerates absence —
/// an optional element that is missing is the normal case, not an error.
/// </summary>
internal static class EventorXml
{
    public static XElement? Child(this XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    public static IEnumerable<XElement> Children(this XElement? parent, string localName) =>
        parent?.Elements().Where(e => e.Name.LocalName == localName) ?? [];

    public static IEnumerable<XElement> Deep(this XElement? parent, string localName) =>
        parent?.Descendants().Where(e => e.Name.LocalName == localName) ?? [];

    public static string? Text(this XElement? parent, string localName) =>
        Clean(parent.Child(localName)?.Value);

    public static string? Attr(this XElement? element, string name) =>
        Clean(element?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value);

    public static bool Flag(this XElement? element, string name) =>
        bool.TryParse(element.Attr(name), out bool value) && value;

    /// <summary>An Eventor <c>&lt;Date&gt;</c> + <c>&lt;Clock&gt;</c> pair, resolved in the federation's zone.</summary>
    public static DateTimeOffset? Moment(this XElement? element, TimeZoneInfo zone)
    {
        if (element.Text("Date") is not { } date || !DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var day))
            return null;

        var clock = element.Text("Clock");

        var time = clock is not null && TimeOnly.TryParse(clock, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : TimeOnly.MinValue;

        var local = day.ToDateTime(time);

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>
    /// A duration as Eventor writes it: <c>31:12</c>, <c>1:02:33</c> or plain seconds.
    /// </summary>
    public static TimeSpan? Duration(string? text)
    {
        if (Clean(text) is not { } value)
            return null;

        var parts = value.Split(':');

        if (parts.Length > 3 || !parts.All(p => int.TryParse(p, CultureInfo.InvariantCulture, out _)))
            return null;

        int[] numbers = [.. parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture))];

        return numbers.Length switch
        {
            1 => TimeSpan.FromSeconds(numbers[0]),
            2 => new TimeSpan(0, numbers[0], numbers[1]),
            _ => new TimeSpan(numbers[0], numbers[1], numbers[2]),
        };
    }

    public static double? Number(string? text) =>
        double.TryParse(Clean(text), CultureInfo.InvariantCulture, out double value) ? value : null;

    public static int? Integer(string? text) =>
        int.TryParse(Clean(text), CultureInfo.InvariantCulture, out int value) ? value : null;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
