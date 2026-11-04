using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Layouts.Tests;

/// <summary>Glukos's Live Activity as a model: what <see cref="GlucoseReference"/> works out inline, worked out once.</summary>
public sealed record GlucoseModel(
    string Name, Uri? Link,
    string Value, string? Arrow, string? Symbol, WidgetColor Color,
    string Unit, string? Below, WidgetColor BelowColor,
    DateTimeOffset MeasuredAt,
    IReadOnlyList<(DateTimeOffset At, double Value)> LastHour, DateTimeOffset GraphFrom, double TargetLow, double TargetHigh,
    string Message)
{
    public const string InRange = "#34C759";
    public const string High = "#FF9F0A";
    public const string Low = "#FF3B30";

    public bool HasGraph => LastHour.Count > 1;
}
