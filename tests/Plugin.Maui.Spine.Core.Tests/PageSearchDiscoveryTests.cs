using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.Spine.Services;
using Xunit;

namespace Plugin.Maui.Spine.Core.Tests;

/// <summary>How a <see cref="PageSearchAttribute"/> becomes a <see cref="PageSearch"/> that follows the view model's property.</summary>
public partial class PageSearchDiscoveryTests
{
    private partial class Towns : ObservableObject
    {
        [PageSearch(Placeholder = "Search towns", Submit = nameof(OpenFirstCommand), Placement = SearchPlacement.Top)]
        [ObservableProperty]
        public partial string Query { get; set; } = "Lu";

        public string? Opened { get; private set; }

        [RelayCommand]
        private void OpenFirst(string text) => Opened = text;
    }

    private partial class FieldStyle : ObservableObject
    {
        [PageSearch(Submit = nameof(GoAsync))]
        [ObservableProperty]
        private string _filter = "";

        [RelayCommand]
        private Task GoAsync() => Task.CompletedTask;
    }

    private class Plain : ObservableObject;

    private class NotAString : ObservableObject
    {
        [PageSearch]
        public int Query { get; set; }
    }

    private class TwoFields : ObservableObject
    {
        [PageSearch]
        public string First { get; set; } = "";

        [PageSearch]
        public string Second { get; set; } = "";
    }

    private class UnknownSubmit : ObservableObject
    {
        [PageSearch(Submit = "Nowhere")]
        public string Query { get; set; } = "";
    }

    [Fact]
    public void Takes_the_attribute_and_the_current_text()
    {
        var search = PageSearchDiscovery.Create(new Towns());

        Assert.NotNull(search);
        Assert.Equal("Lu", search.Text);
        Assert.Equal("Search towns", search.Placeholder);
        Assert.Equal(SearchPlacement.Top, search.Placement);
        Assert.True(search.IsVisible);
        Assert.False(search.IsActive);
    }

    [Fact]
    public void The_field_writes_the_property()
    {
        var towns = new Towns();
        var search = PageSearchDiscovery.Create(towns)!;

        search.Text = "Sund";

        Assert.Equal("Sund", towns.Query);
    }

    [Fact]
    public void The_property_writes_the_field()
    {
        var towns = new Towns();
        var search = PageSearchDiscovery.Create(towns)!;

        towns.Query = "Ö";

        Assert.Equal("Ö", search.Text);
    }

    [Fact]
    public void A_null_from_the_field_is_an_empty_query()
    {
        var towns = new Towns();
        var search = PageSearchDiscovery.Create(towns)!;

        search.Text = null!;

        Assert.Equal("", towns.Query);
    }

    [Fact]
    public void Submit_runs_the_named_command_with_the_text()
    {
        var towns = new Towns();
        var search = PageSearchDiscovery.Create(towns)!;

        search.SubmitCommand!.Execute("Sala");

        Assert.Equal("Sala", towns.Opened);
    }

    [Fact]
    public void A_toolkit_field_and_a_relay_command_method_are_found_by_the_toolkit_names()
    {
        var model = new FieldStyle();
        var search = PageSearchDiscovery.Create(model)!;

        search.Text = "x";

        Assert.Equal("x", model.Filter);
        Assert.Same(model.GoCommand, search.SubmitCommand);
    }

    [Fact]
    public void No_attribute_gives_no_search() => Assert.Null(PageSearchDiscovery.Create(new Plain()));

    [Fact]
    public void A_property_that_is_not_a_string_is_an_error()
    {
        var error = Assert.Throws<InvalidOperationException>(() => PageSearchDiscovery.Create(new NotAString()));
        Assert.Contains("NotAString.Query", error.Message);
    }

    [Fact]
    public void Two_declarations_are_an_error()
    {
        var error = Assert.Throws<InvalidOperationException>(() => PageSearchDiscovery.Create(new TwoFields()));
        Assert.Contains("one search field", error.Message);
    }

    [Fact]
    public void A_submit_that_names_no_command_is_an_error()
    {
        var error = Assert.Throws<InvalidOperationException>(() => PageSearchDiscovery.Create(new UnknownSubmit()));
        Assert.Contains("Nowhere", error.Message);
    }

    [Theory]
    [InlineData("_query", "Query")]
    [InlineData("m_query", "Query")]
    [InlineData("query", "Query")]
    public void Toolkit_property_names(string field, string property) => Assert.Equal(property, ToolkitNames.PropertyFor(field));
}
