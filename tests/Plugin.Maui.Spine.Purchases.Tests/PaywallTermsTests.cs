using Xunit;

namespace Plugin.Maui.Spine.Purchases.Tests;

public class PaywallTermsTests
{
    private static readonly Uri Everywhere = new("https://example.se/terms");
    private static readonly Uri Play = new("https://example.se/play-terms");

    [Fact]
    public void One_link_serves_every_store()
    {
        var links = new StoreLinks { Everywhere = Everywhere };

        Assert.Equal(Everywhere, links.For(StorePlatform.Apple));
        Assert.Equal(Everywhere, links.For(StorePlatform.Google));
        Assert.Equal(Everywhere, links.For(StorePlatform.Microsoft));
    }

    [Fact]
    public void A_store_link_comes_before_the_one_for_every_store()
    {
        var links = new StoreLinks { Everywhere = Everywhere, Google = Play };

        Assert.Equal(Play, links.For(StorePlatform.Google));
        Assert.Equal(Everywhere, links.For(StorePlatform.Apple));
    }

    [Fact]
    public void A_store_without_a_link_has_none()
    {
        var links = new StoreLinks { Apple = PaywallTerms.AppleStandardEula };

        Assert.Equal(PaywallTerms.AppleStandardEula, links.For(StorePlatform.Apple));
        Assert.Null(links.For(StorePlatform.Google));
    }

    [Fact]
    public void Apple_standard_eula_is_apples_page()
    {
        Assert.Equal("https://www.apple.com/legal/internet-services/itunes/dev/stdeula/", PaywallTerms.AppleStandardEula.AbsoluteUri);
    }

    [Fact]
    public void Current_store_on_a_desktop_test_run_is_not_google()
    {
        Assert.NotEqual(StorePlatform.Google, StoreLinks.Current);
    }
}
