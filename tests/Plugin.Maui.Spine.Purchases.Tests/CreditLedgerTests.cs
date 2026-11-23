using Xunit;

namespace Plugin.Maui.Spine.Purchases.Tests;

public enum CreditPurchase
{
    Live,
}

public enum CreditProduct
{
    [StoreProduct(ProductKind.Subscription, Apple = "live.month")]
    [Grants(CreditPurchase.Live)]
    LiveMonthly,

    [StoreProduct(ProductKind.Consumable, Apple = "match")]
    [Adds(Credit.Match, 1)]
    OneMatch,
}

public enum Credit
{
    [FreeCredits(1)]
    [UnlimitedWith(CreditPurchase.Live)]
    Match,
}

public enum ConsumableWithoutAdds
{
    [StoreProduct(ProductKind.Consumable, Apple = "x")]
    Nothing,
}

public enum AddsOnOneTime
{
    [StoreProduct(ProductKind.OneTime, Apple = "x")]
    [Adds(Credit.Match, 1)]
    Wrong,
}

public class CreditLedgerTests
{
    private static readonly PurchaseCatalog Catalog = PurchaseCatalog.Create<CreditPurchase, CreditProduct, Credit>();
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static CreditDefinition Match => Catalog.Credit(Credit.Match);

    [Fact]
    public void The_catalog_reads_credits_and_what_products_add()
    {
        Assert.Equal(1, Match.Free);
        Assert.Equal([Catalog.Purchase(CreditPurchase.Live)], Match.UnlimitedWith);
        Assert.Equal(1, Assert.Single(Catalog.Product(CreditProduct.OneMatch).Adds).Amount);
        Assert.Equal(CreditProduct.OneMatch, Assert.Single(Catalog.ProductsAdding(Match)).Value);
    }

    [Fact]
    public void A_consumable_must_add_and_only_a_consumable_may()
    {
        Assert.Contains("adds nothing", Assert.Throws<InvalidOperationException>(PurchaseCatalog.Create<CreditPurchase, ConsumableWithoutAdds, Credit>).Message);
        Assert.Contains("is a OneTime", Assert.Throws<InvalidOperationException>(PurchaseCatalog.Create<CreditPurchase, AddsOnOneTime, Credit>).Message);
    }

    [Fact]
    public void Adds_without_a_credit_enum_throws()
    {
        Assert.Contains("no credit enum", Assert.Throws<InvalidOperationException>(PurchaseCatalog.Create<CreditPurchase, CreditProduct>).Message);
    }

    [Fact]
    public void The_free_grant_is_missing_once()
    {
        var grants = CreditLedger.MissingGrants([], Catalog, Now).ToList();
        Assert.Equal(1, CreditLedger.Balance(grants, Match));
        Assert.Empty(CreditLedger.MissingGrants(grants, Catalog, Now));
    }

    [Fact]
    public void A_spend_takes_a_credit_once_per_reference()
    {
        List<LedgerEntry> entries = [.. CreditLedger.MissingGrants([], Catalog, Now)];

        var (status, entry) = CreditLedger.Spend(entries, Catalog, Match, "match:1", 1, Now);
        Assert.Equal(SpendStatus.Spent, status);
        entries.Add(entry!);

        Assert.Equal(SpendStatus.AlreadyUnlocked, CreditLedger.Spend(entries, Catalog, Match, "match:1", 1, Now).Status);
        Assert.Equal(SpendStatus.NotEnough, CreditLedger.Spend(entries, Catalog, Match, "match:2", 1, Now).Status);
        Assert.Equal(0, CreditLedger.Balance(entries, Match));
    }

    [Fact]
    public void An_active_subscription_makes_it_unlimited()
    {
        List<LedgerEntry> entries =
        [
            new("owned:LiveMonthly", LedgerEntryKind.Owned, Now.AddDays(-1), Product: "LiveMonthly", ExpiresAt: Now.AddDays(29), WillRenew: true),
        ];

        var (status, entry) = CreditLedger.Spend(entries, Catalog, Match, "match:1", 1, Now);
        Assert.Equal(SpendStatus.Unlimited, status);
        Assert.Null(entry);
        Assert.Contains("Match", CreditLedger.Answer(entries, Catalog, Now).Unlimited);
    }
}
