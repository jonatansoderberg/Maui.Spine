using System.Text.Json;

namespace Orientera.Tests;

/// <summary>
/// Stored packages and cached responses outlive the app version that wrote them. The calendar is
/// read in one piece, so a single word this version does not know used to empty the whole list.
/// </summary>
public class OrienteraJsonTests
{
    private sealed record Row(Discipline Discipline, Sport Sport, string Name);

    [Fact]
    public void An_enum_name_this_version_does_not_know_reads_as_the_default()
    {
        // "Indoor" was a Discipline until it moved to the sport axis. Packages saved before that
        // still say so.
        const string json = """{"discipline":"Indoor","sport":"Foot","name":"Karlstad Indoor"}""";

        var row = JsonSerializer.Deserialize<Row>(json, OrienteraJson.Options);

        Assert.NotNull(row);
        Assert.Equal(default, row.Discipline);
        Assert.Equal(Sport.Foot, row.Sport);

        // And the rest of the object survives, which is the whole point.
        Assert.Equal("Karlstad Indoor", row.Name);
    }

    [Fact]
    public void Names_it_does_know_still_round_trip()
    {
        var row = new Row(Discipline.UltraLong, Sport.MountainBike, "MTBO-träning");

        var json = JsonSerializer.Serialize(row, OrienteraJson.Options);

        Assert.Contains("UltraLong", json);
        Assert.Contains("MountainBike", json);
        Assert.Equal(row, JsonSerializer.Deserialize<Row>(json, OrienteraJson.Options));
    }
}
