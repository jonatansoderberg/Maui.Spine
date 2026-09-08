using Orientera.Services.Notifications;

namespace Orientera.Tests;

/// <summary>
/// The tags are the whole privacy story: the backend targets an expression and never learns who is
/// entered in what, so what the phone puts in its tag list is what the backend can reach it by.
/// </summary>
public class PushTagsTests
{
    private static readonly PersonId Me = new("me");

    private static NotificationPreferences Preferences(params NotificationKind[] enabled)
    {
        var preferences = NotificationPreferences.Default;

        foreach (var kind in enabled)
            preferences = preferences.With(kind, true);

        return preferences;
    }

    private static IReadOnlyList<string> For(
        NotificationPreferences preferences,
        IEnumerable<CompetitionId>? competitions = null,
        IEnumerable<PersonId>? group = null) =>
        PushTags.For(preferences, Me, competitions ?? [], group ?? []);

    [Fact]
    public void Nothing_enabled_is_no_tags_at_all()
    {
        Assert.Empty(For(NotificationPreferences.Default, [new CompetitionId("38412")]));
    }

    [Fact]
    public void The_enabled_kinds_become_tags()
    {
        var tags = For(Preferences(NotificationKind.PmPublished, NotificationKind.ResultsPublished));

        Assert.Contains("kind:pm-published", tags);
        Assert.Contains("kind:results-published", tags);
        Assert.DoesNotContain("kind:entry-closing", tags);
    }

    [Fact]
    public void Time_to_leave_is_not_a_tag_because_no_server_can_know_it()
    {
        var tags = For(Preferences(NotificationKind.TimeToLeave, NotificationKind.ResultsPublished));

        Assert.Equal(["kind:results-published", "user:me"], tags);
    }

    [Fact]
    public void The_kinds_with_no_data_behind_them_are_not_tags_either()
    {
        Assert.Empty(For(Preferences(NotificationKind.RankingChanged, NotificationKind.PredictionAvailable)));
    }

    [Fact]
    public void Entries_and_group_are_tagged_alongside_the_kinds()
    {
        var tags = For(
            Preferences(NotificationKind.ResultsPublished),
            [new CompetitionId("38412"), new CompetitionId("38413")],
            [new PersonId("kalle")]);

        Assert.Equal(
            ["competition:38412", "competition:38413", "kind:results-published", "person:kalle", "user:me"],
            tags);
    }

    [Fact]
    public void The_same_preferences_give_the_same_tags_in_the_same_order()
    {
        var preferences = Preferences(NotificationKind.LiveStarted, NotificationKind.ResultsPublished);
        var competitions = new[] { new CompetitionId("b"), new CompetitionId("a") };

        Assert.Equal(For(preferences, competitions), For(preferences, competitions.Reverse()));
    }

    [Fact]
    public void Only_results_published_is_delivered_as_push_today()
    {
        Assert.Equal([NotificationKind.ResultsPublished], PushTags.Pushed);
    }
}
