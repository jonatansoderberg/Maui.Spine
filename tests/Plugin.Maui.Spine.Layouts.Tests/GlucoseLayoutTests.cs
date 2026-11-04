using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Layouts.Tests;

/// <summary>Glukos's two layout files write exactly the JSON its C# writes, in every case the C# has a branch for.</summary>
public class GlucoseLayoutTests
{
    static readonly DateTimeOffset At = new(2026, 11, 4, 9, 30, 0, TimeSpan.Zero);

    static GlucoseModel Model(string? arrow = "↘", string? symbol = "ArrowDownRight", string? below = null, int points = 13) =>
        new("Alexander", new Uri("se.cosmomedia.glucose://widget/glucose?person=p1"),
            "9,3", arrow, symbol, WidgetColor.FromHex("#34C759"),
            "mmol/L", below, WidgetColor.FromHex("#FF9F0A"),
            At,
            [.. Enumerable.Range(0, points).Select(i => (At.AddMinutes(-60 + 5 * i), 6.0 + Math.Sin(i) * 3))], At.AddHours(-1), 3.9, 10.0,
            "Ingen signal");

    public static TheoryData<string> Cases => ["current", "stale", "no symbol", "high", "no graph", "one point", "no link"];

    internal static GlucoseModel Case(string name) => name switch
    {
        "current" => Model(),
        "stale" => Model(arrow: null, symbol: null, below: "Inga nya värden"),
        "no symbol" => Model(symbol: null),
        "high" => Model(below: "Högt"),
        "no graph" => Model(points: 0),
        "one point" => Model(points: 1, below: "Lågt"),
        _ => Model() with { Link = null },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_reading_writes_todays_JSON(string name) =>
        Assert.Equal(GlucoseReference.Reading(Case(name)).ToJson(), GlucoseLayout.Build(Case(name)).ToJson());

    [Theory]
    [MemberData(nameof(Cases))]
    public void No_value_writes_todays_JSON(string name) =>
        Assert.Equal(GlucoseReference.NoValue(Case(name)).ToJson(), GlucoseNoValueLayout.Build(Case(name)).ToJson());

    [Fact]
    public void The_icons_it_can_show_are_listed() =>
        Assert.Equal(["drop", "exclamationmark.circle"], GlucoseLayout.Icons.Order());
}
