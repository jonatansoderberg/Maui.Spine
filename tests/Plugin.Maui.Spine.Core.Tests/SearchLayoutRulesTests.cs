using Plugin.Maui.Spine.Presentation;
using Xunit;

namespace Plugin.Maui.Spine.Core.Tests;

/// <summary>Where a page's search field goes, Windows' title bar included (#480).</summary>
public class SearchLayoutRulesTests
{
    [Fact]
    public void An_automatic_field_goes_in_the_title_bar_when_the_page_shows_it()
    {
        Assert.Equal(SearchLayout.TitleBar, Resolve(titleBar: true));
    }

    [Fact]
    public void The_title_bar_needs_no_header_bar()
    {
        Assert.Equal(SearchLayout.TitleBar, Resolve(headerBar: false, titleBar: true));
    }

    [Fact]
    public void Without_the_title_bar_the_field_goes_in_the_row_as_in_v1()
    {
        Assert.Equal(SearchLayout.Row, Resolve(titleBar: false));
    }

    [Fact]
    public void Without_the_title_bar_or_a_header_bar_there_is_no_field()
    {
        Assert.Equal(SearchLayout.None, Resolve(headerBar: false, titleBar: false));
    }

    [Fact]
    public void Top_keeps_the_row_even_where_the_title_bar_could_take_the_field()
    {
        Assert.Equal(SearchLayout.Row, Resolve(SearchPlacement.Top, titleBar: true));
    }

    [Fact]
    public void A_sheet_keeps_its_row_even_where_the_title_bar_could_take_the_field()
    {
        Assert.Equal(SearchLayout.Row, Resolve(inSheet: true, titleBar: true));
    }

    [Fact]
    public void A_wide_iPad_or_Mac_region_has_the_field_at_the_trailing_end()
    {
        Assert.Equal(SearchLayout.Trailing, Resolve(trailing: true));
        Assert.Equal(SearchLayout.Row, Resolve(inSheet: true, trailing: true));
        Assert.Equal(SearchLayout.Row, Resolve(SearchPlacement.Top, trailing: true));
        Assert.Equal(SearchLayout.None, Resolve(headerBar: false, trailing: true));
    }

    private static SearchLayout Resolve(
        SearchPlacement placement = SearchPlacement.Automatic,
        bool inSheet = false,
        bool headerBar = true,
        bool titleBar = false,
        bool trailing = false) =>
        SearchLayoutRules.Resolve(placement, inSheet, headerBar, titleBar, trailing);
}
