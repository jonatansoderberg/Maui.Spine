using System.Text.Json;
using Plugin.Maui.Spine.Services;
using Xunit;

namespace Plugin.Maui.Spine.Core.Tests;

/// <summary>
/// A search result's target is stored as the page's type name and the parameter as JSON, and read
/// back when the result is tapped — possibly by a later version of the app.
/// </summary>
public class SearchRecordsTests
{
    public sealed record CompetitionId(int Value);
    public sealed record Unstorable(Action Callback);

    public sealed class CompetitionPage : INavigableWithParameter<CompetitionId>;
    public sealed class SettingsPage : INavigable;
    public sealed class TwoParameterPage : INavigableWithParameter<CompetitionId>, INavigableWithParameter<string>;
    public sealed class UnstorablePage : INavigableWithParameter<Unstorable>;

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Default;
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 2, 0, 0, TimeSpan.Zero);

    private static Type? Pages(string name) =>
        new[] { typeof(CompetitionPage), typeof(SettingsPage), typeof(TwoParameterPage) }.FirstOrDefault(t => t.FullName == name);

    private static SearchRecord Store(SearchableItem item)
    {
        var record = SearchRecords.From(item, Json, Now);
        using var stream = new MemoryStream();
        SearchRecords.Write(stream, [record]);
        stream.Position = 0;
        return SearchRecords.Read(stream)[item.Id];
    }

    [Fact]
    public void Parameter_survives_the_round_trip()
    {
        var record = Store(new SearchableItem("c-42", "Night sprint", NavigationTarget.To<CompetitionPage, CompetitionId>(new CompetitionId(42)),
            Description: "8 Oct", Icon: "figure.run", Keywords: ["orienteering"]));

        var target = SearchRecords.Resolve(record, Pages, Json, out var problem);

        Assert.Null(problem);
        Assert.Equal(typeof(CompetitionPage), target!.Page);
        Assert.Equal(typeof(CompetitionId), target.ParameterType);
        Assert.Equal(new CompetitionId(42), target.Parameter);
        Assert.Equal(["orienteering"], record.Keywords);
        Assert.Equal("8 Oct", record.Description);
    }

    [Fact]
    public void Page_without_parameter()
    {
        var target = SearchRecords.Resolve(Store(new SearchableItem("settings", "Settings", NavigationTarget.To<SettingsPage>())), Pages, Json, out var problem);

        Assert.Null(problem);
        Assert.Equal(typeof(SettingsPage), target!.Page);
        Assert.Null(target.ParameterType);
    }

    [Fact]
    public void A_page_with_two_parameter_types_gets_the_stored_one()
    {
        var target = SearchRecords.Resolve(Store(new SearchableItem("t", "Two", NavigationTarget.To<TwoParameterPage, string>("hello"))), Pages, Json, out _);

        Assert.Equal(typeof(string), target!.ParameterType);
        Assert.Equal("hello", target.Parameter);
    }

    [Fact]
    public void A_page_that_is_gone_says_so()
    {
        var record = Store(new SearchableItem("c-1", "Gone", NavigationTarget.To<CompetitionPage, CompetitionId>(new CompetitionId(1))));

        Assert.Null(SearchRecords.Resolve(record, _ => null, Json, out var problem));
        Assert.Contains(typeof(CompetitionPage).FullName!, problem);
        Assert.Contains("no longer exists", problem);
    }

    [Fact]
    public void A_page_that_no_longer_takes_the_parameter_says_so()
    {
        var record = Store(new SearchableItem("c-1", "Changed", NavigationTarget.To<CompetitionPage, CompetitionId>(new CompetitionId(1))))
            with { Page = typeof(SettingsPage).FullName! };

        Assert.Null(SearchRecords.Resolve(record, Pages, Json, out var problem));
        Assert.Contains("SettingsPage no longer takes", problem);
    }

    [Fact]
    public void Json_that_no_longer_fits_says_so()
    {
        var record = Store(new SearchableItem("c-1", "Old", NavigationTarget.To<CompetitionPage, CompetitionId>(new CompetitionId(1))))
            with { Parameter = "\"not an object\"" };

        Assert.Null(SearchRecords.Resolve(record, Pages, Json, out var problem));
        Assert.Contains("CompetitionId no longer reads", problem);
    }

    [Fact]
    public void A_parameter_that_cannot_be_stored_fails_when_indexed()
    {
        var item = new SearchableItem("u", "Unstorable", NavigationTarget.To<UnstorablePage, Unstorable>(new Unstorable(() => { })));

        var e = Assert.Throws<NotSupportedException>(() => SearchRecords.From(item, Json, Now));
        Assert.Contains("\"u\"", e.Message);
        Assert.Contains(nameof(Unstorable), e.Message);
    }

    [Theory]
    [InlineData("", "Title")]
    [InlineData("id", " ")]
    public void Id_and_title_are_required(string id, string title) =>
        Assert.Throws<ArgumentException>(() => SearchRecords.From(new SearchableItem(id, title, NavigationTarget.To<SettingsPage>()), Json, Now));

    [Fact]
    public void Same_content_ignores_when_it_was_written()
    {
        var item = new SearchableItem("s", "Settings", NavigationTarget.To<SettingsPage>(), Keywords: ["a", "b"]);
        var first = SearchRecords.From(item, Json, Now);
        var later = SearchRecords.From(item, Json, Now.AddDays(1));

        Assert.True(first.SameContent(later));
        Assert.False(first.SameContent(later with { Title = "Other" }));
        Assert.False(first.SameContent(later with { Keywords = ["a"] }));
    }

    [Fact]
    public void Page_entries_are_told_apart_by_their_id()
    {
        Assert.True(SearchRecords.From(new SearchableItem(SearchRecords.PageId(typeof(SettingsPage)), "Settings", NavigationTarget.To<SettingsPage>()), Json, Now).IsPage);
        Assert.False(SearchRecords.From(new SearchableItem("settings", "Settings", NavigationTarget.To<SettingsPage>()), Json, Now).IsPage);
    }
}
