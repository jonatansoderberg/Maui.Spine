using Plugin.Maui.Spine.Core;
using Xunit;

namespace Plugin.Maui.Spine.Core.Tests;

/// <summary>A search started while the field is hidden shows it for as long as the search goes on.</summary>
public class PageSearchTests
{
    [Fact]
    public void Starting_a_search_shows_a_hidden_field_and_ending_it_hides_the_field_again()
    {
        var search = new PageSearch { IsVisible = false };

        search.IsActive = true;
        Assert.True(search.IsVisible);

        search.IsActive = false;
        Assert.False(search.IsVisible);
    }

    [Fact]
    public void The_field_is_shown_before_the_search_starts_and_hidden_after_it_ends()
    {
        var search = new PageSearch { IsVisible = false };
        var order = new List<string>();
        search.PropertyChanged += (_, e) => order.Add($"{e.PropertyName}={(e.PropertyName == nameof(PageSearch.IsActive) ? search.IsActive : search.IsVisible)}");

        search.IsActive = true;
        search.IsActive = false;

        Assert.Equal(["IsVisible=True", "IsActive=True", "IsActive=False", "IsVisible=False"], order);
    }

    [Fact]
    public void A_field_that_was_shown_already_stays_when_the_search_ends()
    {
        var search = new PageSearch();

        search.IsActive = true;
        search.IsActive = false;

        Assert.True(search.IsVisible);
    }

    [Fact]
    public void A_field_the_page_shows_during_the_search_stays_when_the_search_ends()
    {
        var search = new PageSearch { IsVisible = false };

        search.IsActive = true;
        search.IsVisible = false;
        search.IsVisible = true;
        search.IsActive = false;

        Assert.True(search.IsVisible);
    }
}
