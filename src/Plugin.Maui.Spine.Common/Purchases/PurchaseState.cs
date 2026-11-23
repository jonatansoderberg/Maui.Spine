namespace Plugin.Maui.Spine.Purchases;

/// <summary>What is known about one purchase.</summary>
/// <param name="IsActive">True while it gives what it unlocks.</param>
/// <param name="Kind">The kind of product that gave it; null when nothing has been bought.</param>
/// <param name="ExpiresAt">When a subscription's period ends; null for a one-time purchase.</param>
/// <param name="WillRenew">For a subscription: false after the user has cancelled.</param>
/// <param name="Phase">Trial, intro offer, regular, grace period or billing retry.</param>
/// <param name="Product">The member of the app's product enum that gave it.</param>
public sealed record PurchaseState(
    bool IsActive,
    ProductKind? Kind,
    DateTimeOffset? ExpiresAt,
    bool WillRenew,
    PurchasePhase Phase,
    Enum? Product)
{
    /// <summary>Nothing bought.</summary>
    public static PurchaseState None { get; } = new(false, null, null, false, PurchasePhase.Regular, null);
}

/// <summary>
/// One product the user owns, as a store reports it: the input from which Spine works out every <see cref="PurchaseState"/>
/// through <see cref="GrantsAttribute"/>.
/// </summary>
/// <param name="Product">The member of the app's product enum.</param>
/// <param name="PurchasedAt">When it was bought, or when the subscription started.</param>
/// <param name="ExpiresAt">When a subscription's current period ends; null for a one-time product.</param>
/// <param name="WillRenew">For a subscription: false after the user has cancelled.</param>
/// <param name="Phase">Where a subscription is in its life.</param>
public sealed record OwnedProduct(
    Enum Product,
    DateTimeOffset PurchasedAt,
    DateTimeOffset? ExpiresAt,
    bool WillRenew,
    PurchasePhase Phase = PurchasePhase.Regular);
