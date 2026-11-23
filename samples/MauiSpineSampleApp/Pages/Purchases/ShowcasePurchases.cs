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
}
