using Plugin.Maui.Spine.Presentation;
using Xunit;

namespace Plugin.Maui.Spine.Core.Tests;

/// <summary>Where a page's search field goes, Windows' title bar included (#480).</summary>
public class SearchLayoutRulesTests
{
    [Fact]
    public void An_automatic_field_goes_in_the_title_bar_when_the_page_shows_it_and_it_hosts_the_page()
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
    public void A_page_the_title_bar_does_not_host_keeps_its_row_though_it_shows_the_title_bar()
    {
        // A nested region's page, the option off, or the app's own Window.TitleBar.
        Assert.Equal(SearchLayout.Row, Resolve(titleBarShown: true, titleBarHostsPage: false));
        Assert.Equal(SearchLayout.None, Resolve(headerBar: false, titleBarShown: true, titleBarHostsPage: false));
    }

    [Fact]
    public void A_hosted_page_that_hides_the_title_bar_keeps_its_row()
    {
        Assert.Equal(SearchLayout.Row, Resolve(titleBarShown: false, titleBarHostsPage: true));
        Assert.Equal(SearchLayout.None, Resolve(headerBar: false, titleBarShown: false, titleBarHostsPage: true));
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

    // titleBar: the page shows the title bar and the title bar hosts it; the two parts can be set apart.
    private static SearchLayout Resolve(
        SearchPlacement placement = SearchPlacement.Automatic,
        bool inSheet = false,
        bool headerBar = true,
        bool titleBar = false,
        bool? titleBarShown = null,
        bool? titleBarHostsPage = null,
        bool trailing = false) =>
        SearchLayoutRules.Resolve(placement, inSheet, headerBar, titleBarShown ?? titleBar, titleBarHostsPage ?? titleBar, trailing);
}
