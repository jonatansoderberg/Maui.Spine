using System.Collections.Concurrent;

namespace Plugin.Maui.Spine.Server.Purchases;

/// <summary>
/// Each caller's purchases and credits on the server, for the app's own endpoints: whether a purchase is active, whether a
/// reference is unlocked, spending and granting credits. The caller is the ledger key (<see cref="CallerKeys"/>).
/// </summary>
/// <example>
/// <code>
/// app.MapGet("/matches/{id}/live", async (string id, HttpRequest request, IPurchaseLedger ledger) =>
///     await ledger.IsUnlockedAsync(await ledger.CallerAsync(request) ?? "", AppCredit.LiveMatch, $"match:{id}")
///         ? Results.Ok(…) : Results.StatusCode(402));
/// </code>
/// </example>
public interface IPurchaseLedger
{
    /// <summary>Who is calling, as the ledger key; null for nobody known.</summary>
    ValueTask<string?> CallerAsync(Microsoft.AspNetCore.Http.HttpRequest request);

    /// <summary>The caller's balances, unlocked references and unlimited credits. Gives the free credits the first time.</summary>
    Task<CreditsAnswer> GetCreditsAsync(string caller, CancellationToken cancellationToken = default);

    /// <summary>Spends <paramref name="amount"/> of <paramref name="credit"/> on <paramref name="reference"/>, once per reference.</summary>
    Task<SpendAnswer> SpendAsync(string caller, Enum credit, string reference, int amount = 1, CancellationToken cancellationToken = default);

    /// <summary>True when <paramref name="reference"/> is unlocked with <paramref name="credit"/>, or the credit is unlimited for the caller.</summary>
    Task<bool> IsUnlockedAsync(string caller, Enum credit, string reference, CancellationToken cancellationToken = default);

    /// <summary>True while the caller's <paramref name="purchase"/> is active, from what RevenueCat has reported.</summary>
    Task<bool> IsActiveAsync(string caller, Enum purchase, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives <paramref name="amount"/> of <paramref name="credit"/>: a gift, a refund of a spend, a demo account. Once per
    /// <paramref name="id"/>. False when that grant was given before.
    /// </summary>
    Task<bool> GrantAsync(string caller, Enum credit, int amount, string id, CancellationToken cancellationToken = default);

    /// <summary>Every entry of the caller's ledger, oldest first.</summary>
    Task<IReadOnlyList<LedgerEntry>> ReadAsync(string caller, CancellationToken cancellationToken = default);
}

internal sealed class PurchaseLedger(PurchaseCatalog catalog, IPurchaseLedgerStore store, SpinePurchasesServerOptions options, TimeProvider time) : IPurchaseLedger
{
    // One spend at a time per caller in this process, so two spends cannot both see the same last credit. Several server
    // instances can still race; a spend is one credit and the race needs two references at once, so it is accepted.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public PurchaseCatalog Catalog => catalog;

    public ValueTask<string?> CallerAsync(Microsoft.AspNetCore.Http.HttpRequest request) => options.IdentifyAsync(request);

    public async Task<CreditsAnswer> GetCreditsAsync(string caller, CancellationToken cancellationToken = default) =>
        CreditLedger.Answer(await ReadWithGrantsAsync(caller, cancellationToken), catalog, time.GetUtcNow());

    public async Task<SpendAnswer> SpendAsync(string caller, Enum credit, string reference, int amount = 1, CancellationToken cancellationToken = default)
    {
        var definition = catalog.Credit(credit);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);

        var gate = _locks.GetOrAdd(caller, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadWithGrantsAsync(caller, cancellationToken);
            var now = time.GetUtcNow();
            var (status, entry) = CreditLedger.Spend(entries, catalog, definition, reference, amount, now);

            if (entry is not null && !await store.AddAsync(caller, entry, cancellationToken))
                status = SpendStatus.AlreadyUnlocked;

            return new SpendAnswer(status, CreditLedger.Answer(entry is null ? entries : [.. entries, entry], catalog, now));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> IsUnlockedAsync(string caller, Enum credit, string reference, CancellationToken cancellationToken = default)
    {
        var definition = catalog.Credit(credit);
        var entries = await store.ReadAsync(caller, cancellationToken);
        return CreditLedger.IsUnlocked(entries, definition, reference) || CreditLedger.IsUnlimited(entries, catalog, definition, time.GetUtcNow());
    }

    public async Task<bool> IsActiveAsync(string caller, Enum purchase, CancellationToken cancellationToken = default)
    {
        var definition = catalog.Purchase(purchase);
        var entries = await store.ReadAsync(caller, cancellationToken);
        return PurchaseResolver.Resolve(catalog, CreditLedger.Owned(entries, catalog), time.GetUtcNow())[definition.Value].IsActive;
    }

    public Task<bool> GrantAsync(string caller, Enum credit, int amount, string id, CancellationToken cancellationToken = default)
    {
        var definition = catalog.Credit(credit);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return store.AddAsync(caller, new LedgerEntry($"grant:{definition.Id}:{id}", LedgerEntryKind.Grant, time.GetUtcNow(), definition.Id, amount), cancellationToken);
    }

    public Task<IReadOnlyList<LedgerEntry>> ReadAsync(string caller, CancellationToken cancellationToken = default) =>
        store.ReadAsync(caller, cancellationToken);

    /// <summary>The caller's ledger, with the free credits written the first time it is read.</summary>
    private async Task<IReadOnlyList<LedgerEntry>> ReadWithGrantsAsync(string caller, CancellationToken cancellationToken)
    {
        var entries = await store.ReadAsync(caller, cancellationToken);
        var missing = CreditLedger.MissingGrants(entries, catalog, time.GetUtcNow()).ToList();
        if (missing.Count == 0)
            return entries;

        foreach (var grant in missing)
            await store.AddAsync(caller, grant, cancellationToken);
        return await store.ReadAsync(caller, cancellationToken);
    }
}
