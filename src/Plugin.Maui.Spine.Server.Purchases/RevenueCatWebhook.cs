using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.Purchases;

/// <summary>
/// RevenueCat's webhook into the ledger: consumables add credits once per transaction, and subscriptions and one-time
/// products are written as the caller's owned products.
/// </summary>
internal sealed class RevenueCatWebhook(PurchaseCatalog catalog, IPurchaseLedgerStore store, ILogger<RevenueCatWebhook> logger)
{
    public static bool IsAuthorized(string? expected, string? received)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(received))
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(received));
    }

    /// <summary>Applies one webhook body. False when it was not a RevenueCat event at all.</summary>
    public async Task<bool> ApplyAsync(JsonElement body, CancellationToken cancellationToken)
    {
        if (!body.TryGetProperty("event", out var e) || e.ValueKind != JsonValueKind.Object)
            return false;

        var type = Text(e, "type");
        var caller = Text(e, "app_user_id");
        var productId = Text(e, "new_product_id") is { } changed && type == "PRODUCT_CHANGE" ? changed : Text(e, "product_id");

        if (type is null or "TEST" || caller is null || productId is null)
            return true;

        if (caller.StartsWith("$RCAnonymousID", StringComparison.Ordinal))
        {
            logger.LogWarning("Spine purchases: RevenueCat sent {Type} for the anonymous user {User}; it lands in no ledger. Let the app set the RevenueCat app user id to its caller key (Plugin.Maui.Spine.Purchases.RevenueCat does by default).", type, caller);
            return true;
        }

        var platform = Text(e, "store") switch
        {
            "APP_STORE" or "MAC_APP_STORE" => StorePlatform.Apple,
            "PLAY_STORE" => StorePlatform.Google,
            _ => (StorePlatform?)null,
        };
        if (platform is not { } p || Find(p, productId) is not { } product)
        {
            logger.LogWarning("Spine purchases: RevenueCat sent {Type} for {Product} in {Store}, which no product's [StoreProduct] names; it was skipped.", type, productId, Text(e, "store"));
            return true;
        }

        var at = Time(e, "event_timestamp_ms") ?? Time(e, "purchased_at_ms") ?? DateTimeOffset.UtcNow;

        if (product.Kind == ProductKind.Consumable)
        {
            if (type != "NON_RENEWING_PURCHASE")
                return true;
            var transaction = Text(e, "transaction_id") ?? Text(e, "id") ?? throw new InvalidOperationException("A RevenueCat purchase event without a transaction id.");
            foreach (var add in product.Adds)
            {
                var entry = new LedgerEntry($"{LedgerEntry.TransactionId(transaction)}:{add.Credit.Id}", LedgerEntryKind.Purchase, at, add.Credit.Id, add.Amount, Product: product.Value.ToString());
                if (!await store.AddAsync(caller, entry, cancellationToken))
                    logger.LogInformation("Spine purchases: transaction {Transaction} was credited before; RevenueCat sent it again.", transaction);
            }
            return true;
        }

        var existing = (await store.ReadAsync(caller, cancellationToken)).FirstOrDefault(x => x.Id == LedgerEntry.OwnedId(product));
        var expires = product.Kind == ProductKind.OneTime ? null : Time(e, "expiration_at_ms") ?? existing?.ExpiresAt;
        var phase = Text(e, "period_type") switch
        {
            "TRIAL" => PurchasePhase.Trial,
            "INTRO" => PurchasePhase.IntroOffer,
            _ => PurchasePhase.Regular,
        };

        LedgerEntry? owned = type switch
        {
            "INITIAL_PURCHASE" or "RENEWAL" or "PRODUCT_CHANGE" or "UNCANCELLATION" or "SUBSCRIPTION_EXTENDED" or "NON_RENEWING_PURCHASE" =>
                Owned(product, existing?.At ?? at, expires, product.Kind == ProductKind.Subscription, phase),
            "CANCELLATION" when Text(e, "cancel_reason") == "CUSTOMER_SUPPORT" =>
                Owned(product, existing?.At ?? at, at, false, phase),          // a refund: it ends now
            "CANCELLATION" =>
                Owned(product, existing?.At ?? at, expires, false, existing?.Phase ?? phase),
            "EXPIRATION" =>
                Owned(product, existing?.At ?? at, expires ?? at, false, existing?.Phase ?? phase),
            "BILLING_ISSUE" =>
                Owned(product, existing?.At ?? at, expires, true,
                    Time(e, "grace_period_expiration_at_ms") is { } grace && grace > DateTimeOffset.UtcNow ? PurchasePhase.GracePeriod : PurchasePhase.BillingRetry),
            _ => null,
        };

        if (owned is not null)
            await store.UpsertAsync(caller, owned, cancellationToken);
        else
            logger.LogDebug("Spine purchases: RevenueCat event {Type} needs nothing in the ledger.", type);
        return true;
    }

    private static LedgerEntry Owned(ProductDefinition product, DateTimeOffset at, DateTimeOffset? expires, bool willRenew, PurchasePhase phase) =>
        new(LedgerEntry.OwnedId(product), LedgerEntryKind.Owned, at, Product: product.Value.ToString(), ExpiresAt: expires, WillRenew: willRenew, Phase: phase);

    // Play reports a subscription as "pro" or "pro:month" depending on the event; one product with that base id is enough.
    private ProductDefinition? Find(StorePlatform platform, string id)
    {
        if (catalog.ProductForStoreId(platform, id) is { } exact)
            return exact;
        var matches = catalog.Products.Where(p => p.IdFor(platform) is { } own && own.Split(':')[0] == id.Split(':')[0]).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static DateTimeOffset? Time(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
}
