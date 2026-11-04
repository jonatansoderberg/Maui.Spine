using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Widgets.Layouts;

/// <summary>What Reading.layout.xml shows: a value with its trend, the last hour as a graph, worked out once.</summary>
public sealed record ReadingModel(
    string Name, Uri? Link,
    string Value, string? Arrow, string? Symbol, WidgetColor Color,
    string Unit, string? Below, WidgetColor BelowColor,
    DateTimeOffset MeasuredAt,
    IReadOnlyList<(DateTimeOffset At, double Value)> LastHour, DateTimeOffset GraphFrom, double TargetLow, double TargetHigh)
{
    public const string InRange = "#2BB894";
    public const string High = "#F2B312";
    public const string Low = "#F2643D";

    public bool HasGraph => LastHour.Count > 1;

    public string GraphDescription => $"The last hour, from {LastHour[0].Value:0.0} to {LastHour[^1].Value:0.0}";

    /// <summary>A reading a minute old, falling, a little above the target, with the hour before it.</summary>
    public static ReadingModel Sample(DateTimeOffset now) => new(
        "Alex", null, "10,4", "↘", "ArrowDownRight", WidgetColor.FromHex(High),
        "mmol/L", "High", WidgetColor.FromHex(High),
        now.AddMinutes(-1),
        [.. Enumerable.Range(0, 13).Select(i => (now.AddMinutes(-61 + 5 * i), 8.5 + 3 * Math.Sin(i / 2.5)))], now.AddMinutes(-61), 3.9, 10.0);
}
