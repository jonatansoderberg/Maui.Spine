using Xunit;

namespace Plugin.Maui.Spine.Purchases.Tests;

public enum TestPurchase
{
    [PurchaseId("pro")]
    Pro,

    Supporter,
}

public enum TestProduct
{
    [StoreProduct(ProductKind.Subscription, Apple = "pro.month", Google = "pro:month")]
    [Grants(TestPurchase.Pro)]
    ProMonthly,

    [StoreProduct(ProductKind.Subscription, Apple = "pro.year")]
    [Grants(TestPurchase.Pro)]
    ProYearly,

    [StoreProduct(ProductKind.OneTime, Apple = "pro.lifetime")]
    [Grants(TestPurchase.Pro)]
    ProLifetime,

    [StoreProduct(ProductKind.OneTime, Apple = "supporter")]
    [Grants(TestPurchase.Supporter)]
    Supporter,
}

public enum WrongGrant
{
    [StoreProduct(ProductKind.OneTime)]
    [Grants(TestProduct.ProMonthly)]
    Broken,
}

public enum MissingKind
{
    NoAttribute,
}

public class PurchaseCatalogTests
{
    private static readonly PurchaseCatalog Catalog = PurchaseCatalog.Create<TestPurchase, TestProduct>();

    [Fact]
    public void Reads_ids_kinds_store_ids_and_grants()
    {
        Assert.Equal("pro", Catalog.Purchase(TestPurchase.Pro).Id);
        Assert.Equal("Supporter", Catalog.Purchase(TestPurchase.Supporter).Id);

        var monthly = Catalog.Product(TestProduct.ProMonthly);
        Assert.Equal(ProductKind.Subscription, monthly.Kind);
        Assert.Equal("pro:month", monthly.IdFor(StorePlatform.Google));
        Assert.Null(monthly.IdFor(StorePlatform.Microsoft));
        Assert.Equal([Catalog.Purchase(TestPurchase.Pro)], monthly.Grants);

        Assert.Equal(TestProduct.ProYearly, Catalog.ProductForStoreId(StorePlatform.Apple, "pro.year")!.Value);
        Assert.Equal(3, Catalog.ProductsGranting(Catalog.Purchase(TestPurchase.Pro)).Count());
    }

    [Fact]
    public void A_member_of_another_enum_throws_with_its_name()
    {
        var e = Assert.Throws<ArgumentException>(() => Catalog.Purchase(TestProduct.ProMonthly));
        Assert.Contains("TestProduct.ProMonthly", e.Message);
        Assert.Contains("TestPurchase", e.Message);
    }

    [Fact]
    public void A_grant_of_the_wrong_enum_throws()
    {
        var e = Assert.Throws<InvalidOperationException>(PurchaseCatalog.Create<TestPurchase, WrongGrant>);
        Assert.Contains("WrongGrant.Broken", e.Message);
    }

    [Fact]
    public void A_product_without_a_kind_throws()
    {
        var e = Assert.Throws<InvalidOperationException>(PurchaseCatalog.Create<TestPurchase, MissingKind>);
        Assert.Contains("[StoreProduct]", e.Message);
    }
}

public class PurchaseResolverTests
{
    private static readonly PurchaseCatalog Catalog = PurchaseCatalog.Create<TestPurchase, TestProduct>();
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static PurchaseState Pro(params OwnedProduct[] owned) => PurchaseResolver.Resolve(Catalog, owned, Now)[TestPurchase.Pro];

    [Fact]
    public void Nothing_bought_is_none()
    {
        Assert.Equal(PurchaseState.None, Pro());
    }

    [Fact]
    public void A_running_subscription_is_active_until_its_end()
    {
        var state = Pro(new OwnedProduct(TestProduct.ProMonthly, Now.AddDays(-3), Now.AddDays(27), true));

        Assert.True(state.IsActive);
        Assert.Equal(ProductKind.Subscription, state.Kind);
        Assert.Equal(Now.AddDays(27), state.ExpiresAt);
        Assert.True(state.WillRenew);
        Assert.Equal(TestProduct.ProMonthly, state.Product);
    }

    [Fact]
    public void An_ended_subscription_is_not_active_but_still_known()
    {
        var state = Pro(new OwnedProduct(TestProduct.ProMonthly, Now.AddDays(-40), Now.AddDays(-10), false));

        Assert.False(state.IsActive);
        Assert.Equal(ProductKind.Subscription, state.Kind);
    }

    [Fact]
    public void A_grace_period_keeps_it_active_after_the_end_and_billing_retry_does_not()
    {
        Assert.True(Pro(new OwnedProduct(TestProduct.ProMonthly, Now.AddDays(-31), Now.AddDays(-1), true, PurchasePhase.GracePeriod)).IsActive);
        Assert.False(Pro(new OwnedProduct(TestProduct.ProMonthly, Now.AddDays(-31), Now.AddDays(5), true, PurchasePhase.BillingRetry)).IsActive);
    }

    [Fact]
    public void A_one_time_purchase_wins_over_a_subscription()
    {
        var state = Pro(
            new OwnedProduct(TestProduct.ProMonthly, Now.AddDays(-3), Now.AddDays(27), true),
            new OwnedProduct(TestProduct.ProLifetime, Now.AddDays(-1), null, false));

        Assert.Equal(ProductKind.OneTime, state.Kind);
        Assert.Null(state.ExpiresAt);
        Assert.False(state.WillRenew);
        Assert.Equal(TestProduct.ProLifetime, state.Product);
    }

    [Fact]
    public void A_renewing_subscription_wins_over_one_that_ends_later()
    {
        var state = Pro(
            new OwnedProduct(TestProduct.ProYearly, Now.AddDays(-100), Now.AddDays(265), false),
            new OwnedProduct(TestProduct.ProMonthly, Now.AddDays(-3), Now.AddDays(27), true));

        Assert.Equal(TestProduct.ProMonthly, state.Product);
    }

    [Fact]
    public void Each_purchase_follows_its_own_grants()
    {
        var states = PurchaseResolver.Resolve(Catalog, [new OwnedProduct(TestProduct.Supporter, Now, null, false)], Now);

        Assert.True(states[TestPurchase.Supporter].IsActive);
        Assert.False(states[TestPurchase.Pro].IsActive);
    }

    [Fact]
    public void A_renewing_subscription_beside_a_lifetime_purchase_overlaps()
    {
        OwnedProduct[] owned =
        [
            new(TestProduct.ProMonthly, Now.AddDays(-3), Now.AddDays(27), true),
            new(TestProduct.ProLifetime, Now.AddDays(-1), null, false),
        ];

        var overlap = Assert.Single(PurchaseResolver.OverlappingSubscriptions(Catalog, owned, Now));
        Assert.Equal(TestProduct.ProMonthly, overlap.Product);
    }

    [Fact]
    public void A_cancelled_subscription_beside_a_lifetime_purchase_does_not_overlap()
    {
        OwnedProduct[] owned =
        [
            new(TestProduct.ProMonthly, Now.AddDays(-3), Now.AddDays(27), false),
            new(TestProduct.ProLifetime, Now.AddDays(-1), null, false),
        ];

        Assert.Empty(PurchaseResolver.OverlappingSubscriptions(Catalog, owned, Now));
    }
}
