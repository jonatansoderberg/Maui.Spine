using Orientera.Backend.Ranking;
using Orientera.Domain.Ranking;

namespace Orientera.Tests;

/// <summary>
/// The Sverigelistan parser, against a real club page saved from Eventor.
/// </summary>
/// <remarks>
/// This is the most fragile code in the backend: it reads a page layout nobody promised us, and
/// the fixture is the only thing standing between a silent layout change and a table full of
/// wrong points. The values below are read off the real page on purpose — a test that only
/// compared the parser to itself would pass while the site moved underneath it.
/// </remarks>
public class RankingPageParserTests
{
    private static IReadOnlyList<RankingRow> Parse() =>
        RankingPageParser.Parse("124", File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ranking", "club-124.html")));

    [Fact]
    public void A_club_page_yields_its_runners()
    {
        var rows = Parse();

        Assert.Equal(35, rows.Count);
        Assert.All(rows, r => Assert.Equal("124", r.ClubId));
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
    }

    [Fact]
    public void A_row_carries_what_the_page_states()
    {
        var first = Parse()[0];

        Assert.Equal("16695", first.RunnerId);
        Assert.Equal("Isa Envall", first.Name);
        Assert.Equal("D21", first.Class);
        Assert.Equal(1, first.ClubRank);
        Assert.Equal(5, first.NationalRank);
        Assert.Equal(3.30, first.Points, 2);
    }

    /// <summary>Swedish decimals, parsed as Swedish rather than as whatever the server runs as.</summary>
    [Fact]
    public void Points_are_read_with_a_comma()
    {
        var second = Parse()[1];

        Assert.Equal(5.66, second.Points, 2);
    }

    /// <summary>
    /// The id is what makes this usable at all: without it a row could only be matched on a name
    /// and club, which is the ambiguity SP-02 wrongly concluded was unavoidable.
    /// </summary>
    [Fact]
    public void Every_row_carries_the_runner_id() =>
        Assert.All(Parse(), r => Assert.Matches(@"^\d+$", r.RunnerId));

    /// <summary>
    /// The club is two tables, and both number from one. Read flat the page yields two runners
    /// ranked first, and "17:e i klubben" cannot be told from the other 17th.
    /// </summary>
    [Fact]
    public void Both_halves_of_the_club_are_numbered_from_one()
    {
        var rows = Parse();

        Assert.Equal(12, rows.Count(r => r.Section is RankingSection.Women));
        Assert.Equal(23, rows.Count(r => r.Section is RankingSection.Men));
        Assert.Equal(2, rows.Count(r => r.ClubRank == 1));
    }

    [Fact]
    public void A_row_knows_which_table_it_stood_in()
    {
        var rows = Parse();

        Assert.Equal(RankingSection.Women, rows[0].Section);

        var firstMan = rows[12];
        Assert.Equal("Simon Harden", firstMan.Name);
        Assert.Equal(1, firstMan.ClubRank);
        Assert.Equal(RankingSection.Men, firstMan.Section);
    }

    [Fact]
    public void The_header_row_is_not_a_runner() =>
        Assert.DoesNotContain(Parse(), r => r.Name is "Namn");

    /// <summary>A page that is not a ranking page is empty, not an exception.</summary>
    [Fact]
    public void Something_that_is_not_a_club_page_yields_nothing() =>
        Assert.Empty(RankingPageParser.Parse("124", "<html><body><p>Sidan finns inte</p></body></html>"));
}
