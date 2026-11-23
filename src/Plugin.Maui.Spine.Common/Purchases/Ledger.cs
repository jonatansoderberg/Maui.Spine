using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>What a ledger entry records.</summary>
public enum LedgerEntryKind
{
    /// <summary>Free credits, once per caller (<see cref="FreeCreditsAttribute"/>), or a grant from the app's own server code.</summary>
    Grant,

    /// <summary>Credits from a bought consumable.</summary>
    Purchase,

    /// <summary>Credits spent on a reference, which is unlocked for good.</summary>
    Spend,

    /// <summary>A subscription or one-time product the caller owns, as the store last reported it.</summary>
    Owned,
}

/// <summary>
/// One row of a caller's ledger. Its <see cref="Id"/> is unique per caller and written once, so a webhook that arrives
/// twice, a spend sent twice or a free grant asked for twice counts once. <see cref="LedgerEntryKind.Owned"/> rows are
/// the exception: they are replaced as the store reports changes.
/// </summary>
/// <param name="Id">Unique per caller: <c>grant:LiveMatch</c>, <c>txn:…</c>, <c>spend:LiveMatch:match:123</c>, <c>owned:ProMonthly</c>.</param>
/// <param name="Kind">What it records.</param>
/// <param name="At">When.</param>
/// <param name="Credit">The credit's id, for grants, purchases and spends.</param>
/// <param name="Amount">Credits added (grant, purchase) or taken (spend).</param>
/// <param name="Reference">What a spend unlocked: <c>match:123</c>.</param>
/// <param name="Product">The product's member name, for purchases and owned products.</param>
/// <param name="ExpiresAt">An owned subscription's end.</param>
/// <param name="WillRenew">An owned subscription renews.</param>
/// <param name="Phase">An owned subscription's phase.</param>
public sealed record LedgerEntry(
    string Id,
    LedgerEntryKind Kind,
    DateTimeOffset At,
    string? Credit = null,
    int Amount = 0,
    string? Reference = null,
    string? Product = null,
    DateTimeOffset? ExpiresAt = null,
    bool WillRenew = false,
    PurchasePhase Phase = PurchasePhase.Regular)
{
    /// <summary>The id of the free grant of <paramref name="credit"/>.</summary>
    public static string GrantId(CreditDefinition credit) => $"grant:{credit.Id}";

    /// <summary>The id of a spend of <paramref name="credit"/> on <paramref name="reference"/>.</summary>
    public static string SpendId(CreditDefinition credit, string reference) => $"spend:{credit.Id}:{reference}";

    /// <summary>The id of a store transaction.</summary>
    public static string TransactionId(string transaction) => $"txn:{transaction}";

    /// <summary>The id of an owned product's row.</summary>
    public static string OwnedId(ProductDefinition product) => $"owned:{product.Value}";
}

/// <summary>How a spend ended.</summary>
public enum SpendStatus
{
    /// <summary>A credit was taken and the reference is unlocked.</summary>
    Spent,

    /// <summary>The reference was unlocked before; nothing was taken.</summary>
    AlreadyUnlocked,

    /// <summary>An <see cref="UnlimitedWithAttribute"/> purchase is active; nothing was taken.</summary>
    Unlimited,

    /// <summary>Too few credits; nothing was taken.</summary>
    NotEnough,

    /// <summary>The server could not be reached or refused; nothing is known to have been taken.</summary>
    Failed,
}

/// <summary>
/// A caller's credits worked out from the ledger: balances, unlocked references, and what a spend does. The same logic
/// for the server and the test store.
/// </summary>
public static class CreditLedger
{
    /// <summary>The balance of <paramref name="credit"/>.</summary>
    public static int Balance(IEnumerable<LedgerEntry> entries, CreditDefinition credit) =>
        entries.Where(e => e.Credit == credit.Id).Sum(e => e.Kind == LedgerEntryKind.Spend ? -e.Amount : e.Kind is LedgerEntryKind.Grant or LedgerEntryKind.Purchase ? e.Amount : 0);

    /// <summary>True when a spend of <paramref name="credit"/> on <paramref name="reference"/> is in the ledger.</summary>
    public static bool IsUnlocked(IEnumerable<LedgerEntry> entries, CreditDefinition credit, string reference) =>
        entries.Any(e => e.Kind == LedgerEntryKind.Spend && e.Credit == credit.Id && e.Reference == reference);

    /// <summary>The products the ledger says the caller owns, for <see cref="PurchaseResolver"/>.</summary>
    public static IReadOnlyList<OwnedProduct> Owned(IEnumerable<LedgerEntry> entries, PurchaseCatalog catalog) =>
        [.. entries
            .Where(e => e.Kind == LedgerEntryKind.Owned && e.Product is not null)
            .Select(e => catalog.ProductNamed(e.Product!) is { } product
                ? new OwnedProduct(product.Value, e.At, e.ExpiresAt, e.WillRenew, e.Phase)
                : null)
            .OfType<OwnedProduct>()];

    /// <summary>True while one of the credit's <see cref="UnlimitedWithAttribute"/> purchases is active.</summary>
    public static bool IsUnlimited(IEnumerable<LedgerEntry> entries, PurchaseCatalog catalog, CreditDefinition credit, DateTimeOffset now)
    {
        if (credit.UnlimitedWith.Count == 0)
            return false;
        var states = PurchaseResolver.Resolve(catalog, Owned(entries, catalog), now);
        return credit.UnlimitedWith.Any(p => states[p.Value].IsActive);
    }

    /// <summary>The free grants the ledger does not have yet.</summary>
    public static IEnumerable<LedgerEntry> MissingGrants(IEnumerable<LedgerEntry> entries, PurchaseCatalog catalog, DateTimeOffset now)
    {
        var ids = entries.Select(e => e.Id).ToHashSet();
        return catalog.Credits
            .Where(c => c.Free > 0 && !ids.Contains(LedgerEntry.GrantId(c)))
            .Select(c => new LedgerEntry(LedgerEntry.GrantId(c), LedgerEntryKind.Grant, now, c.Id, c.Free));
    }

    /// <summary>
    /// What a spend of <paramref name="amount"/> on <paramref name="reference"/> does, and the entry to write when it takes
    /// credits. The free grants must be in <paramref name="entries"/> already.
    /// </summary>
    public static (SpendStatus Status, LedgerEntry? Entry) Spend(IReadOnlyList<LedgerEntry> entries, PurchaseCatalog catalog, CreditDefinition credit,
        string reference, int amount, DateTimeOffset now)
    {
        if (IsUnlocked(entries, credit, reference))
            return (SpendStatus.AlreadyUnlocked, null);
        if (IsUnlimited(entries, catalog, credit, now))
            return (SpendStatus.Unlimited, null);
        if (Balance(entries, credit) < amount)
            return (SpendStatus.NotEnough, null);
        return (SpendStatus.Spent, new LedgerEntry(LedgerEntry.SpendId(credit, reference), LedgerEntryKind.Spend, now, credit.Id, amount, reference));
    }

    /// <summary>The answer to a caller who asks for their credits.</summary>
    public static CreditsAnswer Answer(IReadOnlyList<LedgerEntry> entries, PurchaseCatalog catalog, DateTimeOffset now) => new(
        catalog.Credits.ToDictionary(c => c.Id, c => Balance(entries, c)),
        [.. entries.Where(e => e.Kind == LedgerEntryKind.Spend && e.Reference is not null).Select(e => $"{e.Credit}:{e.Reference}")],
        [.. catalog.Credits.Where(c => IsUnlimited(entries, catalog, c, now)).Select(c => c.Id)]);
}

/// <summary>A caller's credits on the wire: <c>GET …/credits</c>.</summary>
/// <param name="Balances">Each credit's balance, by its id.</param>
/// <param name="Unlocked">The unlocked references, as <c>credit:reference</c>.</param>
/// <param name="Unlimited">The credits a purchase makes free right now.</param>
public sealed record CreditsAnswer(Dictionary<string, int> Balances, List<string> Unlocked, List<string> Unlimited);

/// <summary>A spend on the wire: <c>POST …/credits/spend</c>.</summary>
/// <param name="Credit">The credit's id.</param>
/// <param name="Reference">What it unlocks: <c>match:123</c>.</param>
/// <param name="Amount">How many credits it costs.</param>
public sealed record SpendRequest(string Credit, string Reference, int Amount = 1);

/// <summary>The answer to a spend.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Credits">The caller's credits after it.</param>
public sealed record SpendAnswer(SpendStatus Status, CreditsAnswer Credits);

/// <summary>The purchases wire format.</summary>
[JsonSerializable(typeof(CreditsAnswer))]
[JsonSerializable(typeof(SpendRequest))]
[JsonSerializable(typeof(SpendAnswer))]
[JsonSerializable(typeof(List<LedgerEntry>))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class PurchasesWireJson : JsonSerializerContext;

/// <summary>
/// The key a caller's ledger is kept under on the server, and the RevenueCat app user id: the same text on both sides, so
/// RevenueCat's webhook lands in the ledger of the caller who bought.
/// </summary>
public static class CallerKeys
{
    /// <summary>A signed-in user: <c>user:{id}</c>.</summary>
    public static string User(string id) => $"user:{id}";

    /// <summary>An app installation without a signed-in user: <c>installation:{id}</c>.</summary>
    public static string Installation(string id) => $"installation:{id}";
}
