using Plugin.Maui.Spine.Common;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class PushTagExpressionTests
{
    private static bool Match(string expression, params string[] tags) =>
        PushTagExpression.Parse(expression).Matches(tags);

    [Fact]
    public void A_single_tag_matches_when_the_installation_carries_it()
    {
        Assert.True(Match("kind:pm-published", "kind:pm-published", "user:121330"));
        Assert.False(Match("kind:pm-published", "kind:results-published"));
    }

    [Fact]
    public void And_requires_both()
    {
        const string e = "kind:results-published && competition:59691";
        Assert.True(Match(e, "kind:results-published", "competition:59691"));
        Assert.False(Match(e, "kind:results-published"));
        Assert.False(Match(e, "competition:59691"));
    }

    [Fact]
    public void Or_requires_either()
    {
        const string e = "competition:59691 || competition:59692";
        Assert.True(Match(e, "competition:59692"));
        Assert.False(Match(e, "competition:1"));
    }

    [Fact]
    public void Not_inverts()
    {
        Assert.True(Match("!muted", "user:1"));
        Assert.False(Match("!muted", "muted"));
        Assert.True(Match("kind:a && !muted", "kind:a"));
        Assert.False(Match("kind:a && !muted", "kind:a", "muted"));
    }

    [Fact]
    public void And_binds_tighter_than_or()
    {
        // a || (b && c) — true on a alone, false on b alone.
        const string e = "a || b && c";
        Assert.True(Match(e, "a"));
        Assert.False(Match(e, "b"));
        Assert.True(Match(e, "b", "c"));
    }

    [Fact]
    public void Parentheses_override_precedence()
    {
        const string e = "(a || b) && c";
        Assert.False(Match(e, "a"));
        Assert.True(Match(e, "a", "c"));
        Assert.True(Match(e, "b", "c"));
    }

    [Fact]
    public void Not_applies_to_a_group()
    {
        const string e = "!(a || b)";
        Assert.False(Match(e, "a"));
        Assert.False(Match(e, "b"));
        Assert.True(Match(e, "c"));
    }

    [Fact]
    public void Whitespace_around_operators_is_optional()
    {
        Assert.True(Match("a&&b", "a", "b"));
        Assert.True(Match("  a   &&   b  ", "a", "b"));
        Assert.True(Match("!(a||b)", "c"));
    }

    [Fact]
    public void Tags_are_compared_with_ordinal_equality()
    {
        Assert.False(Match("user:ABC", "user:abc"));
        Assert.True(Match("user:ABC", "user:ABC"));
    }

    [Fact]
    public void There_is_no_limit_on_the_number_of_tags()
    {
        var tags = Enumerable.Range(0, 500).Select(i => $"t{i}").ToArray();
        var expression = string.Join(" || ", tags);
        Assert.True(Match(expression, "t499"));
        Assert.False(Match(expression, "t500"));
    }

    [Fact]
    public void Referenced_tags_lists_every_tag_once()
    {
        var e = PushTagExpression.Parse("a && (b || !a)");
        Assert.Equal(["a", "b"], e.ReferencedTags.OrderBy(t => t, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("a && b", "a,b")]
    [InlineData("a && !b", "a")]
    [InlineData("(a || b) && c", "c")]
    [InlineData("a || b", "")]
    [InlineData("!a", "")]
    [InlineData("a || (a && b)", "a")]
    public void Required_tags_are_the_ones_a_match_cannot_do_without(string expression, string expected)
    {
        // This is what lets a register use a tag index instead of scanning every installation.
        var required = PushTagExpression.Parse(expression).RequiredTags.OrderBy(t => t, StringComparer.Ordinal);
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), required);
    }

    [Fact]
    public void Nothing_is_required_by_match_all()
    {
        Assert.Empty(PushTagExpression.MatchAll.RequiredTags);
    }

    [Fact]
    public void A_required_tag_really_is_present_in_every_match()
    {
        // The property the index relies on: no tag set can satisfy the expression without them.
        var expression = PushTagExpression.Parse("kind:news && (team:red || team:blue) && !muted");

        Assert.Equal(["kind:news"], expression.RequiredTags);
        Assert.False(expression.Matches(["team:red"]));
        Assert.True(expression.Matches(["kind:news", "team:red"]));
    }

    [Fact]
    public void Match_all_matches_anything_including_no_tags()
    {
        Assert.True(PushTagExpression.MatchAll.Matches([]));
        Assert.True(PushTagExpression.MatchAll.Matches(["a"]));
    }

    [Fact]
    public void To_string_keeps_the_grouping_it_parsed()
    {
        Assert.Equal("(a || b) && c", PushTagExpression.Parse("(a||b) && c").ToString());
        Assert.Equal("a || b && c", PushTagExpression.Parse("a || b && c").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a &&")]
    [InlineData("&& a")]
    [InlineData("!")]
    [InlineData("(a")]
    [InlineData("a)")]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("a b")]
    public void Invalid_expressions_are_rejected(string expression)
    {
        Assert.False(PushTagExpression.TryParse(expression, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Throws<FormatException>(() => PushTagExpression.Parse(expression));
    }

    [Fact]
    public void The_error_says_where_the_problem_is()
    {
        PushTagExpression.TryParse("a && (b || c", out _, out var error);
        Assert.Contains("5", error);
    }
}
