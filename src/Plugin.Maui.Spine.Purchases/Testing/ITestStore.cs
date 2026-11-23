namespace Plugin.Maui.Spine.Purchases.Testing;

/// <summary>
/// Controls the test store from a test or a Debug-only settings row: the store events a real store would send, without
/// waiting for them. Registered only with <c>UseTestStore()</c>. Every change raises <see cref="IAppPurchases.Changed"/>.
/// </summary>
public interface ITestStore
{
    /// <summary>What the test store says the user owns.</summary>
    IReadOnlyList<OwnedProduct> Owned { get; }

    /// <summary>Purchases waiting for <see cref="ApprovePending"/>.</summary>
    IReadOnlyList<Enum> PendingPurchases { get; }

    /// <summary>The next purchase answers <see cref="PurchaseStatus.Pending"/>, as Ask to Buy does.</summary>
    bool NextPurchasePending { get; set; }

    /// <summary>The next purchase fails with a store error.</summary>
    bool NextPurchaseFails { get; set; }

    /// <summary>Ends a subscription now: its period is over and it does not renew.</summary>
    void Expire(Enum product);

    /// <summary>Renews a subscription for another period, from now or from the end of the current one.</summary>
    void Renew(Enum product);

    /// <summary>Turns off a subscription's renewal, as the user does in the store; it stays active to the end of the period.</summary>
    void Cancel(Enum product);

    /// <summary>Takes a purchase away, as a refund does.</summary>
    void Refund(Enum product);

    /// <summary>Puts a subscription in a billing grace period: the payment failed and it stays active while the store retries.</summary>
    void StartGracePeriod(Enum product);

    /// <summary>Lets the pending purchases go through.</summary>
    void ApprovePending();

    /// <summary>Forgets every purchase, pending purchase and used trial.</summary>
    void Reset();
}
