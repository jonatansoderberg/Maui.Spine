using Plugin.Maui.Spine.Purchases;

namespace MauiSpineSampleApp.Pages.Purchases;

/// <summary>What the Showcase checks: Pro unlocks features, Supporter only says thank you.</summary>
public enum ShowcasePurchase
{
    [PurchaseId("pro")]
    Pro,

    [PurchaseId("supporter")]
    Supporter,
}

// Shared with the Showcase server, which reads the same attributes for its credit ledger and RevenueCat's webhook.

/// <summary>What the Showcase sells. On the test store; the ids show what a real app writes.</summary>
public enum ShowcaseProduct
{
    [StoreProduct(ProductKind.Subscription, Apple = "se.cosmomedia.spineshowcase.pro.month", Google = "pro:month")]
    [Grants(ShowcasePurchase.Pro)]
    ProMonthly,

    [StoreProduct(ProductKind.Subscription, Apple = "se.cosmomedia.spineshowcase.pro.year", Google = "pro:year")]
    [Grants(ShowcasePurchase.Pro)]
    ProYearly,

    [StoreProduct(ProductKind.OneTime, Apple = "se.cosmomedia.spineshowcase.pro.lifetime", Google = "pro_lifetime")]
    [Grants(ShowcasePurchase.Pro)]
    ProLifetime,

    [StoreProduct(ProductKind.OneTime, Apple = "se.cosmomedia.spineshowcase.supporter", Google = "supporter")]
    [Grants(ShowcasePurchase.Supporter)]
    Supporter,

    [StoreProduct(ProductKind.Consumable, Apple = "se.cosmomedia.spineshowcase.match", Google = "match")]
    [Adds(ShowcaseCredit.LiveMatch, 1)]
    OneMatch,

    [StoreProduct(ProductKind.Consumable, Apple = "se.cosmomedia.spineshowcase.match3", Google = "match3")]
    [Adds(ShowcaseCredit.LiveMatch, 3)]
    ThreeMatches,
}

/// <summary>What the Showcase's credits buy: one live match each. The first is free, and Pro makes every match free.</summary>
public enum ShowcaseCredit
{
    [FreeCredits(1)]
    [UnlimitedWith(ShowcasePurchase.Pro)]
    LiveMatch,
}
