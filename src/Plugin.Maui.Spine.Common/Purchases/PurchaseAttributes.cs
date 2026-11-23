namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// The id of a purchase (a member of the app's purchase enum) in the store service: the RevenueCat entitlement id.
/// Without it the id is the member's name.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class PurchaseIdAttribute(string id) : Attribute
{
    /// <summary>The id.</summary>
    public string Id { get; } = id;
}

/// <summary>
/// What a member of the app's product enum is in the stores: its kind and its id in each store. A product without an id
/// for the running platform is not offered there.
/// </summary>
/// <example>
/// <code>
/// [StoreProduct(ProductKind.Subscription, Apple = "se.example.notes.pro.month", Google = "pro:month")]
/// [Grants(AppPurchase.Pro)]
/// ProMonthly,
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field)]
public sealed class StoreProductAttribute(ProductKind kind) : Attribute
{
    /// <summary>What kind of product it is.</summary>
    public ProductKind Kind { get; } = kind;

    /// <summary>The product id in App Store Connect, for iOS and Mac Catalyst.</summary>
    public string? Apple { get; init; }

    /// <summary>The product id in Play Console; <c>subscription:base-plan</c> for a subscription's base plan.</summary>
    public string? Google { get; init; }

    /// <summary>The Store ID of the add-on in Partner Center.</summary>
    public string? Microsoft { get; init; }
}

/// <summary>The purchases a product gives: members of the app's purchase enum.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class GrantsAttribute(params object[] purchases) : Attribute
{
    /// <summary>The purchases, as members of the app's purchase enum.</summary>
    public IReadOnlyList<object> Purchases { get; } = purchases;
}

/// <summary>How the stores sell a product, and so how long what it gives stays active.</summary>
public enum ProductKind
{
    /// <summary>Renews every period until the user cancels: an auto-renewable subscription, a Play subscription's base plan, a Microsoft subscription add-on.</summary>
    Subscription,

    /// <summary>Bought once and kept: a non-consumable, a Play one-time product, a durable add-on.</summary>
    OneTime,

    /// <summary>Bought once for a fixed time without renewing. Not supported yet.</summary>
    Pass,

    /// <summary>Used up and bought again: adds credits (<see cref="AddsAttribute"/>) to the caller's ledger and is never active.</summary>
    Consumable,
}

/// <summary>Where a purchase from a subscription is in its life.</summary>
public enum PurchasePhase
{
    /// <summary>Paid for, or a one-time purchase.</summary>
    Regular,

    /// <summary>In a free trial.</summary>
    Trial,

    /// <summary>In an introductory offer at a lower price.</summary>
    IntroOffer,

    /// <summary>The renewal payment failed, and the store keeps the subscription active while it retries.</summary>
    GracePeriod,

    /// <summary>The renewal payment failed, and the subscription is no longer active while the store retries.</summary>
    BillingRetry,
}
