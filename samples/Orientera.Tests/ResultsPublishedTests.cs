using Orientera.Backend.Push;

namespace Orientera.Tests;

/// <summary>
/// The one notification no phone can work out for itself. Sending it twice is worse than sending it
/// late, so the rule for what counts as news is the feature.
/// </summary>
public class ResultsPublishedTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private static Competition Competition(string id, DateTimeOffset? results) => new()
    {
        Id = new CompetitionId(id),
        Name = $"Tävling {id}",
        Organiser = "Gävle OK",
        District = "Gästrikland",
        Place = "Gävle",
        Location = new GeoPoint(60.6749, 17.1413),
        Discipline = Discipline.Sprint,
        Level = CompetitionLevel.District,
        FirstStart = Now.AddDays(-1),
        LastFinish = Now.AddDays(-1).AddHours(5),
        Schedule = new CompetitionSchedule { ResultsPublishedAt = results },
    };

    private static IReadOnlyList<string> Pending(
        IEnumerable<Competition> competitions, params string[] announced) =>
        [.. ResultsPublished
            .Pending(competitions, announced.Select(a => new CompetitionId(a)).ToHashSet(), Now)
            .Select(c => c.Id.Value)];

    [Fact]
    public void A_competition_without_results_is_not_news()
    {
        Assert.Empty(Pending([Competition("a", null)]));
    }

    [Fact]
    public void Freshly_published_results_are_news()
    {
        Assert.Equal(["a"], Pending([Competition("a", Now.AddMinutes(-3))]));
    }

    [Fact]
    public void The_back_catalogue_is_not_announced_on_the_first_run()
    {
        Assert.Empty(Pending([Competition("old", Now - ResultsPublished.Window.Add(TimeSpan.FromMinutes(1)))]));
    }

    [Fact]
    public void Results_dated_in_the_future_wait_until_they_are_published()
    {
        Assert.Empty(Pending([Competition("a", Now.AddHours(1))]));
    }

    [Fact]
    public void What_has_already_been_sent_is_not_sent_again()
    {
        Assert.Empty(Pending([Competition("a", Now.AddMinutes(-3))], "a"));
    }

    [Fact]
    public void The_oldest_publication_goes_first()
    {
        var pending = Pending(
        [
            Competition("later", Now.AddMinutes(-3)),
            Competition("earlier", Now.AddHours(-5)),
        ]);

        Assert.Equal(["earlier", "later"], pending);
    }
}
