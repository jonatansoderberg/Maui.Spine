using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Layouts.Tests;

/// <summary>Glukos's <c>GlucoseActivity.Layout</c> as it is written in C# today, over the same model: what the two files must equal.</summary>
public static class GlucoseReference
{
    static readonly WidgetColor Ink = WidgetColor.Primary;
    static readonly WidgetColor Muted = WidgetColor.Secondary;
    static readonly WidgetColor Warning = WidgetColor.FromHex("#FF9500");

    public static LiveActivityLayout NoValue(GlucoseModel m) => new()
    {
        LockScreen = W.VStack(6, W.Text(m.Name).Caption().Color(Muted), W.Text(m.Message).Headline().Bold().Color(Ink)).Padding(14),
        Background = WidgetColor.FromHex("#000000"),
        Link = m.Link,
        Android = new LiveUpdateText(Title: m.Name, Body: m.Message),
        Small = W.VStack(2, W.Text(m.Name).Caption().Color(Muted), W.Text(m.Message).Headline().Bold().Color(Ink)),
        ExpandedLeading = W.Icon("drop", m.Color),
        ExpandedCenter = W.Text(m.Message).Caption().Color(Ink).Centered(),
        CompactLeading = W.Icon("drop", m.Color),
        CompactTrailing = W.Text("–").Headline().Bold().Color(Muted),
        Minimal = W.Icon("drop", m.Color),
    };

    public static LiveActivityLayout Reading(GlucoseModel m)
    {
        var unit = W.Text(m.Unit).Caption().Bold().Color(Muted);
        var shared = new Dictionary<string, WidgetNode>
        {
            ["title"] = W.Text(m.Name).FontSize(15).Bold().Color(Ink),
            ["age"] = W.Relative(m.MeasuredAt, ago: true).Caption().Color(Muted).Trailing().SingleLine(),
            ["unit"] = m.Below is null ? unit : W.HStack(4, unit, W.Text($"· {m.Below}").Caption().Bold().Color(m.BelowColor)),
        };
        if (m.LastHour.Count > 1)
            shared["graph"] = W.Sparkline(m.LastHour, m.GraphFrom, m.MeasuredAt, TimeSpan.FromMinutes(5), WidgetColor.FromHex(GlucoseModel.InRange), 56)
                .Band(m.TargetLow, m.TargetHigh, WidgetColor.FromHex(GlucoseModel.High), WidgetColor.FromHex(GlucoseModel.Low))
                .Area()
                .Dot();
        var hasGraph = shared.ContainsKey("graph");

        WidgetNode Reading(double size)
        {
            var number = W.Text(m.Value).FontSize(size).Bold().FontDesign(TextDesign.Rounded).Color(m.Color);
            WidgetNode direction = m.Arrow is null
                ? W.Icon("exclamationmark.circle", Warning, size * 0.6)
                : m.Symbol is not null
                    ? W.Icon(m.Symbol, m.Color, size * 0.7)
                    : W.Text(m.Arrow).FontSize(Math.Round(size * 0.7)).Bold().Color(m.Color);
            return W.HStack(6, number, direction);
        }

        WidgetNode Row(double size, double graphHeight)
        {
            var reading = Reading(size);
            return hasGraph
                ? W.VStack(2, W.HStack(12, W.VStack(reading).Fill(), W.VStack(W.Use("graph", graphHeight)).Fill()), W.Use("unit"))
                : W.VStack(2, reading, W.Use("unit"));
        }

        return new LiveActivityLayout
        {
            Shared = shared,
            LockScreen = W.VStack(10, W.HStack(8, W.Use("title"), W.Spacer(), W.Use("age")), Row(40, 56)).Padding(16),
            Background = WidgetColor.FromHex("#000000"),
            Link = m.Link,
            Android = new LiveUpdateText(
                Title: $"{m.Name} {m.Value} {m.Arrow ?? "!"}",
                Body: string.Join(" · ", new[] { m.Unit, m.Below }.Where(s => s is not null)),
                Chip: $"{m.Value}{m.Arrow ?? "!"}"),
            ExpandedLeading = W.HStack(0, W.Use("title")).Padding(6),
            ExpandedTrailing = W.HStack(0, W.Spacer(), W.Use("age")).Padding(6),
            ExpandedBottom = W.HStack(0, Row(40, 48)).Padding(10),
            CompactLeading = W.Icon("drop", m.Color),
            CompactTrailing = W.Text($"{m.Value} {m.Arrow ?? "!"}").Headline().Bold().FontDesign(TextDesign.Rounded).Color(m.Color),
            Minimal = W.Text(m.Value).Headline().Bold().FontDesign(TextDesign.Rounded).Color(m.Color),
            Small = hasGraph
                ? W.HStack(12, W.VStack(2, W.Use("title"), Reading(26), W.Use("unit")).Fill(),
                    W.VStack(4, W.HStack(0, W.Spacer(), W.Use("age")), W.Use("graph", 36)).Fill())
                : W.VStack(2, W.HStack(8, W.Use("title"), W.Spacer(), W.Use("age")), Reading(26), W.Use("unit")),
        };
    }
}
