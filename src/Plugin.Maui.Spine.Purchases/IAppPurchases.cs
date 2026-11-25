namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// The app's purchases from the store: what is active, the products for sale, buying, restoring and the paywall. Every
/// method takes members of the app's own enums, registered with <c>UseSpinePurchases&lt;TPurchase, TProduct&gt;</c>.
/// </summary>
/// <remarks>
/// The last known state is kept on the device, so <see cref="IsActive"/> answers at once at launch and offline; the store's
/// answer follows with <see cref="Changed"/>.
/// </remarks>
public interface IAppPurchases
{
    /// <summary>True while the purchase is active: a running or grace-period subscription, or a one-time purchase.</summary>
    bool IsActive<TPurchase>(TPurchase purchase) where TPurchase : struct, Enum;

    /// <summary>What is known about a purchase: active, the kind of product that gave it, until when, renewing or not, the phase.</summary>
    PurchaseState GetState<TPurchase>(TPurchase purchase) where TPurchase : struct, Enum;

    /// <summary>The renewing subscriptions that give nothing a one-time purchase does not already give.</summary>
    IReadOnlyList<OwnedProduct> OverlappingSubscriptions { get; }

    /// <summary>Raised on the main thread when the active purchases change: a purchase, a restore, a renewal, an expiry or a refund.</summary>
    event EventHandler<PurchasesChangedEventArgs>? Changed;

    /// <summary>
    /// Raised on the main thread after a paywall has closed with a purchase or a restore: the default paywall, the app's own
    /// paywall page, or one opened from <see cref="PurchasesSection"/>. The sheet is gone by then, so the app can open its own,
    /// such as a welcome or a thank-you. Not raised for <see cref="PurchaseAsync"/> called directly.
    /// </summary>
    event EventHandler<PaywallPurchasedEventArgs>? Purchased;

    /// <summary>Asks the store again for what the user owns.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>The products for sale on this platform, with prices in the store's currency; the last known list when the store does not answer.</summary>
    Task<IReadOnlyList<StoreProductInfo>> GetProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>Buys a product with the store's own sheet. A subscription bought while another of the same purchase is active is a change of plan.</summary>
    Task<PurchaseResult> PurchaseAsync<TProduct>(TProduct product, CancellationToken cancellationToken = default) where TProduct : struct, Enum;

    /// <summary>Asks the store for every purchase the user's store account has made.</summary>
    Task<PurchaseResult> RestoreAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the store's own subscription management.</summary>
    Task ManageAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the paywall with every offered product. True when a purchase became active.</summary>
    Task<bool> ShowPaywallAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the paywall with the products that grant <paramref name="purchase"/>. True when it is active afterwards.</summary>
    Task<bool> ShowPaywallAsync<TPurchase>(TPurchase purchase, CancellationToken cancellationToken = default) where TPurchase : struct, Enum;

    /// <summary>True at once when the purchase is active; otherwise opens the paywall for it and returns whether the user bought or restored it.</summary>
    Task<bool> RequireAsync<TPurchase>(TPurchase purchase, CancellationToken cancellationToken = default) where TPurchase : struct, Enum;
}

/// <summary>How a purchase or a restore ended.</summary>
public enum PurchaseStatus
{
    /// <summary>Bought, or restored: the purchase is active.</summary>
    Purchased,

    /// <summary>Waiting for someone else: Ask to Buy, or a payment Play has not received yet. <see cref="IAppPurchases.Changed"/> follows when it goes through.</summary>
    Pending,

    /// <summary>The user closed the store's sheet. Not an error; show nothing.</summary>
    Cancelled,

    /// <summary>A restore found nothing.</summary>
    NothingToRestore,

    /// <summary>The store refused or could not be reached; <see cref="PurchaseResult.ErrorMessage"/> says why.</summary>
    Failed,
}

/// <summary>The end of a purchase or a restore.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="ErrorCode">The store's code, when it failed.</param>
/// <param name="ErrorMessage">The store's message, when it failed.</param>
public sealed record PurchaseResult(PurchaseStatus Status, string? ErrorCode = null, string? ErrorMessage = null)
{
    /// <summary>Bought or restored.</summary>
    public static PurchaseResult Purchased { get; } = new(PurchaseStatus.Purchased);

    /// <summary>Waiting for approval or payment.</summary>
    public static PurchaseResult Pending { get; } = new(PurchaseStatus.Pending);

    /// <summary>Closed by the user.</summary>
    public static PurchaseResult Cancelled { get; } = new(PurchaseStatus.Cancelled);

    /// <summary>A restore that found nothing.</summary>
    public static PurchaseResult NothingToRestore { get; } = new(PurchaseStatus.NothingToRestore);

    /// <summary>A failure with the store's code and message.</summary>
    public static PurchaseResult Failed(string? code, string? message) => new(PurchaseStatus.Failed, code, message);
}

/// <summary>What a paywall ended with in <see cref="IAppPurchases.Purchased"/>.</summary>
/// <param name="purchase">The purchase the paywall was opened for, or null for one that offered every product.</param>
/// <param name="active">The purchases active once the paywall closed.</param>
public sealed class PaywallPurchasedEventArgs(Enum? purchase, IReadOnlyList<Enum> active) : EventArgs
{
    /// <summary>The purchase the paywall was opened for, or null for one that offered every product.</summary>
    public Enum? Purchase { get; } = purchase;

    /// <summary>The purchases active once the paywall closed.</summary>
    public IReadOnlyList<Enum> Active { get; } = active;
}

/// <summary>What changed in <see cref="IAppPurchases.Changed"/>.</summary>
/// <param name="purchases">The purchases that became active or stopped being active, or whose state changed.</param>
/// <param name="overlappingSubscription">True the first time a renewing subscription gives nothing a one-time purchase does not.</param>
public sealed class PurchasesChangedEventArgs(IReadOnlyList<Enum> purchases, bool overlappingSubscription) : EventArgs
{
    /// <summary>The purchases whose state changed.</summary>
    public IReadOnlyList<Enum> Purchases { get; } = purchases;

    /// <summary>True the first time a renewing subscription gives nothing a one-time purchase does not.</summary>
    public bool OverlappingSubscription { get; } = overlappingSubscription;
}

/// <summary>A length of time as the stores give it: a subscription's period or a free trial.</summary>
/// <param name="Count">How many units.</param>
/// <param name="Unit">The unit.</param>
public sealed record StorePeriod(int Count, StorePeriodUnit Unit)
{
    /// <summary>One week.</summary>
    public static StorePeriod Week { get; } = new(1, StorePeriodUnit.Week);

    /// <summary>One month.</summary>
    public static StorePeriod Month { get; } = new(1, StorePeriodUnit.Month);

    /// <summary>One year.</summary>
    public static StorePeriod Year { get; } = new(1, StorePeriodUnit.Year);

    /// <summary>A number of days.</summary>
    public static StorePeriod Days(int count) => new(count, StorePeriodUnit.Day);

    /// <summary>The period added to <paramref name="start"/>.</summary>
    public DateTimeOffset After(DateTimeOffset start) => Unit switch
    {
        StorePeriodUnit.Day => start.AddDays(Count),
        StorePeriodUnit.Week => start.AddDays(7 * Count),
        StorePeriodUnit.Month => start.AddMonths(Count),
        _ => start.AddYears(Count),
    };

    /// <summary>Roughly how many months the period is, for a per-month price.</summary>
    public decimal Months => Unit switch
    {
        StorePeriodUnit.Day => Count / 30m,
        StorePeriodUnit.Week => Count * 7 / 30m,
        StorePeriodUnit.Month => Count,
        _ => Count * 12,
    };
}

/// <summary>The unit of a <see cref="StorePeriod"/>.</summary>
public enum StorePeriodUnit
{
    /// <summary>Days.</summary>
    Day,

    /// <summary>Weeks.</summary>
    Week,

    /// <summary>Months.</summary>
    Month,

    /// <summary>Years.</summary>
    Year,
}

/// <summary>A product for sale, as the store describes it.</summary>
/// <param name="Product">The member of the app's product enum.</param>
/// <param name="Kind">What kind of product it is.</param>
/// <param name="Title">The store's name for it.</param>
/// <param name="Price">The price formatted by the store, in its currency.</param>
/// <param name="Amount">The price as a number, for a per-month comparison.</param>
/// <param name="CurrencyCode">The ISO 4217 code of the price.</param>
/// <param name="Period">A subscription's period; null for a one-time product.</param>
/// <param name="FreeTrial">The free trial the user is eligible for; null when there is none or the user has had it.</param>
public sealed record StoreProductInfo(
    Enum Product,
    ProductKind Kind,
    string Title,
    string Price,
    decimal Amount,
    string CurrencyCode,
    StorePeriod? Period = null,
    StorePeriod? FreeTrial = null);
