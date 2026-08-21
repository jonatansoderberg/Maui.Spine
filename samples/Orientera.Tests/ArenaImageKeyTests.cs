using Orientera.Backend.Arena;
using Orientera.Domain;
using Xunit;

namespace Orientera.Tests;

public class ArenaImageKeyTests
{
    private static Competition At(DateTimeOffset start, Discipline discipline = Discipline.Middle) => new()
    {
        Id = new CompetitionId("59691"),
        Name = "Trimtex Cup #4",
        Organiser = "Valbo AIF",
        District = "Gästrikland",
        Place = "Valbo",
        Location = new GeoPoint(60.6032, 16.9686),
        Discipline = discipline,
        Level = CompetitionLevel.Local,
        FirstStart = start,
        LastFinish = start.AddHours(2),
    };

    [Theory]
    [InlineData(1, ArenaSeason.Vinter)]
    [InlineData(3, ArenaSeason.Vinter)]
    [InlineData(4, ArenaSeason.Var)]
    [InlineData(8, ArenaSeason.Sommar)]
    [InlineData(10, ArenaSeason.Host)]
    [InlineData(12, ArenaSeason.Vinter)]
    public void Season_follows_the_month_of_the_race(int month, ArenaSeason expected) =>
        Assert.Equal(expected, ArenaImageKey.SeasonOf(new DateTimeOffset(2026, month, 15, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Night_races_get_their_own_image()
    {
        var day = ArenaImageKey.For(At(new DateTimeOffset(2026, 8, 24, 18, 30, 0, TimeSpan.Zero)), 1);
        var night = ArenaImageKey.For(
            At(new DateTimeOffset(2026, 8, 24, 21, 0, 0, TimeSpan.Zero), Discipline.Night), 1);

        Assert.False(day.Night);
        Assert.True(night.Night);
        Assert.NotEqual(day.BlobName, night.BlobName);
    }

    [Fact]
    public void Indoor_has_no_season_because_it_has_no_terrain() =>
        Assert.Equal(ArenaSeason.Inomhus,
            ArenaImageKey.For(At(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero),
                                 Discipline.Indoor), 1).Season);

    /// <summary>
    /// Renderare och prompt utvecklas. Höjs versionen utan att blobnamnet ändras serveras
    /// gamla bilder tyst vidare, och det syns inte förrän någon jämför två tävlingar.
    /// </summary>
    [Fact]
    public void A_new_renderer_version_is_a_new_image()
    {
        var when = new DateTimeOffset(2026, 8, 24, 18, 30, 0, TimeSpan.Zero);

        Assert.NotEqual(ArenaImageKey.For(At(when), 1).BlobName,
                        ArenaImageKey.For(At(when), 2).BlobName);
    }
}
