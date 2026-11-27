using System.Net.Http.Json;
using System.Text.Json;
using AsyncAwaitBestPractices;
using Plugin.Maui.Spine.Authentication;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// The caller's credits: balances, the references they have unlocked, and spending a credit on a reference once. Kept on
/// the app's server (<c>MapSpinePurchases()</c>), or on the device with the test store. Members of the app's credit enum,
/// registered with <c>UseSpinePurchases&lt;TPurchase, TProduct, TCredit&gt;</c>.
/// </summary>
/// <example>
/// <code>
/// if (await credits.UnlockAsync(AppCredit.LiveMatch, $"match:{match.Id}"))
///     await FollowLiveAsync(match);
/// </code>
/// </example>
public interface ICredits
{
    /// <summary>The balance of <paramref name="credit"/>, from the last answer.</summary>
    int Balance<TCredit>(TCredit credit) where TCredit : struct, Enum;

    /// <summary>True when <paramref name="reference"/> is unlocked with <paramref name="credit"/>, or the credit is unlimited.</summary>
    bool IsUnlocked<TCredit>(TCredit credit, string reference) where TCredit : struct, Enum;

    /// <summary>True while a purchase makes <paramref name="credit"/> free (<see cref="UnlimitedWithAttribute"/>).</summary>
    bool IsUnlimited<TCredit>(TCredit credit) where TCredit : struct, Enum;

    /// <summary>Raised on the main thread when the balances, the unlocked references or the unlimited credits change.</summary>
    event EventHandler? Changed;

    /// <summary>Asks for the credits again.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Spends <paramref name="amount"/> on <paramref name="reference"/>; a reference unlocked before costs nothing.</summary>
    Task<SpendStatus> SpendAsync<TCredit>(TCredit credit, string reference, int amount = 1, CancellationToken cancellationToken = default) where TCredit : struct, Enum;

    /// <summary>
    /// True when <paramref name="reference"/> is unlocked, or becomes unlocked now: a credit is spent, or, with none left, the
    /// paywall offers more credits and the purchases that make the credit unlimited.
    /// </summary>
    Task<bool> UnlockAsync<TCredit>(TCredit credit, string reference, CancellationToken cancellationToken = default) where TCredit : struct, Enum;

    /// <summary>
    /// Forgets everything this device bought, spent and was given, for testing: on the test store the device's ledger; on
    /// a server, the caller's ledger there, where the server allows it (<c>SpinePurchasesServerOptions.AllowForget</c>).
    /// What the store itself holds (a non-consumable, a subscription) stays. False when the server would not.
    /// </summary>
    Task<bool> ForgetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Where the credits live: the app's server, or the device on the test store.</summary>
internal interface ICreditsBackend
{
    Task<CreditsAnswer> GetAsync(CancellationToken cancellationToken);
    Task<SpendAnswer> SpendAsync(SpendRequest spend, CancellationToken cancellationToken);

    /// <summary>A consumable was bought; true when its credits are in the answer of <see cref="GetAsync"/>.</summary>
    Task<bool> PurchasedAsync(ProductDefinition product, CancellationToken cancellationToken);

    /// <summary>Forgets the device's ledger; false when the backend would not.</summary>
    Task<bool> ForgetAsync(CancellationToken cancellationToken);
}

/// <summary>Credits on the app's server: <c>GET credits</c>, <c>POST credits/spend</c>; purchases arrive through RevenueCat's webhook.</summary>
internal sealed class ServerCredits(Uri server, CreditOptions options, IServiceProvider services) : ICreditsBackend
{
    private readonly HttpClient _http = new(options.HttpHandler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly Uri _root = server.AbsoluteUri.EndsWith('/') ? server : new Uri(server.AbsoluteUri + "/");

    public async Task<CreditsAnswer> GetAsync(CancellationToken cancellationToken)
    {
        using var request = await RequestAsync(HttpMethod.Get, "credits", null, cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(PurchasesWireJson.Default.CreditsAnswer, cancellationToken)
            ?? throw new InvalidOperationException("Spine purchases: the server answered no credits.");
    }

    public async Task<SpendAnswer> SpendAsync(SpendRequest spend, CancellationToken cancellationToken)
    {
        using var request = await RequestAsync(HttpMethod.Post, "credits/spend", JsonContent.Create(spend, PurchasesWireJson.Default.SpendRequest), cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(PurchasesWireJson.Default.SpendAnswer, cancellationToken)
            ?? throw new InvalidOperationException("Spine purchases: the server answered no spend.");
    }

    public async Task<bool> PurchasedAsync(ProductDefinition product, CancellationToken cancellationToken)
    {
        // The store's webhook reaches the server a moment after the purchase; ask until the credits are there.
        var before = await GetAsync(cancellationToken);
        foreach (var wait in (int[])[1, 2, 3, 4, 5, 8])
        {
            await Task.Delay(TimeSpan.FromSeconds(wait), cancellationToken);
            var now = await GetAsync(cancellationToken);
            if (product.Adds.Any(a => now.Balances.GetValueOrDefault(a.Credit.Id) > before.Balances.GetValueOrDefault(a.Credit.Id)))
                return true;
        }
        return false;
    }

    public async Task<bool> ForgetAsync(CancellationToken cancellationToken)
    {
        using var request = await RequestAsync(HttpMethod.Post, "credits/forget", null, cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(_root, path)) { Content = content };
        var authorization = options.AuthorizationHeader is { } header ? await header(cancellationToken)
            : services.GetService<ISignedInSession>() is { } session ? await session.GetAuthorizationHeaderAsync(cancellationToken)
            : null;
        if (authorization is { Length: > 0 })
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation(SpineInstallation.Header, await Installation.IdAsync());
        return request;
    }
}

/// <summary>Credits on the device, for the test store: the same ledger as the server, kept in <see cref="Preferences"/>.</summary>
internal sealed class LocalCredits(PurchaseCatalog catalog, AppPurchases purchases, TimeProvider time) : ICreditsBackend
{
    private const string Key = "Spine.Purchases.TestStore.Ledger";
    private readonly Lock _gate = new();

    public Task<CreditsAnswer> GetAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
            return Task.FromResult(CreditLedger.Answer(WithOwned(WithGrants()), catalog, time.GetUtcNow()));
    }

    public Task<SpendAnswer> SpendAsync(SpendRequest spend, CancellationToken cancellationToken)
    {
        var credit = catalog.CreditNamed(spend.Credit) ?? throw new ArgumentException($"Spine purchases: no credit '{spend.Credit}'.");
        lock (_gate)
        {
            var entries = WithOwned(WithGrants());
            var now = time.GetUtcNow();
            var (status, entry) = CreditLedger.Spend(entries, catalog, credit, spend.Reference, spend.Amount, now);
            if (entry is not null)
            {
                entries = [.. entries, entry];
                Save([.. Read(), entry]);
            }
            return Task.FromResult(new SpendAnswer(status, CreditLedger.Answer(entries, catalog, now)));
        }
    }

    public Task<bool> PurchasedAsync(ProductDefinition product, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            var transaction = Guid.NewGuid().ToString("N");
            Save([.. Read(), .. product.Adds.Select(a =>
                new LedgerEntry($"{LedgerEntry.TransactionId(transaction)}:{a.Credit.Id}", LedgerEntryKind.Purchase, now, a.Credit.Id, a.Amount, Product: product.Value.ToString()))]);
        }
        return Task.FromResult(true);
    }

    /// <summary>Forgets the device's ledger, with the test store's Reset.</summary>
    public void Reset()
    {
        lock (_gate)
            Save([]);
    }

    public Task<bool> ForgetAsync(CancellationToken cancellationToken)
    {
        Reset();
        return Task.FromResult(true);
    }

    private List<LedgerEntry> WithGrants()
    {
        var entries = Read();
        var missing = CreditLedger.MissingGrants(entries, catalog, time.GetUtcNow()).ToList();
        if (missing.Count == 0)
            return entries;
        entries.AddRange(missing);
        Save(entries);
        return entries;
    }

    // The store's owned products decide what is unlimited, as RevenueCat's webhook does on the server.
    private List<LedgerEntry> WithOwned(List<LedgerEntry> entries) =>
        [.. entries, .. purchases.Owned.Select(o => new LedgerEntry($"owned:{o.Product}", LedgerEntryKind.Owned, o.PurchasedAt,
            Product: o.Product.ToString(), ExpiresAt: o.ExpiresAt, WillRenew: o.WillRenew, Phase: o.Phase))];

    private static List<LedgerEntry> Read()
    {
        try
        {
            return Preferences.Get(Key, null) is { Length: > 0 } json
                ? JsonSerializer.Deserialize(json, PurchasesWireJson.Default.ListLedgerEntry) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void Save(List<LedgerEntry> entries) =>
        Preferences.Set(Key, JsonSerializer.Serialize(entries, PurchasesWireJson.Default.ListLedgerEntry));
}

/// <summary><see cref="ICredits"/> over a backend, with the last answer kept on the device so balances show at once.</summary>
internal sealed class AppCredits : ICredits
{
    private readonly PurchaseCatalog _catalog;
    private readonly ICreditsBackend _backend;
    private readonly AppPurchases _purchases;
    private readonly string _key;
    private CreditsAnswer _answer;

    public AppCredits(PurchaseCatalog catalog, ICreditsBackend backend, AppPurchases purchases)
    {
        _catalog = catalog;
        _backend = backend;
        _purchases = purchases;
        _key = $"Spine.Purchases.Credits.{backend.GetType().Name}";
        _answer = Load(_key) ?? new CreditsAnswer([], [], []);
        purchases.Changed += (_, _) => RefreshAsync().SafeFireAndForget(e => Log("refresh after a purchase change", e));
    }

    public ICreditsBackend Backend => _backend;

    public event EventHandler? Changed;

    public int Balance<TCredit>(TCredit credit) where TCredit : struct, Enum =>
        _answer.Balances.GetValueOrDefault(_catalog.Credit(credit).Id);

    public bool IsUnlocked<TCredit>(TCredit credit, string reference) where TCredit : struct, Enum =>
        IsUnlimited(credit) || _answer.Unlocked.Contains($"{_catalog.Credit(credit).Id}:{reference}");

    public bool IsUnlimited<TCredit>(TCredit credit) where TCredit : struct, Enum => Unlimited(_catalog.Credit(credit));

    internal bool IsUnlimited(CreditDefinition credit) => Unlimited(credit);

    /// <summary>The server's answer makes the credit unlimited, whatever the store says: a purchase the server owns for the caller.</summary>
    internal bool IsUnlimitedByServer(CreditDefinition credit) => _answer.Unlimited.Contains(credit.Id);

    internal int Balance(CreditDefinition credit) => _answer.Balances.GetValueOrDefault(credit.Id);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Apply(await _backend.GetAsync(cancellationToken));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log("refresh", e);
        }
    }

    public Task<SpendStatus> SpendAsync<TCredit>(TCredit credit, string reference, int amount = 1, CancellationToken cancellationToken = default) where TCredit : struct, Enum =>
        Spend(_catalog.Credit(credit), reference, amount, cancellationToken);

    public async Task<bool> ForgetAsync(CancellationToken cancellationToken = default)
    {
        if (!await _backend.ForgetAsync(cancellationToken))
            return false;
        await RefreshAsync(cancellationToken);
        return true;
    }

    public Task<bool> UnlockAsync<TCredit>(TCredit credit, string reference, CancellationToken cancellationToken = default) where TCredit : struct, Enum =>
        Unlock(_catalog.Credit(credit), reference, cancellationToken);

    public async Task<bool> Unlock(CreditDefinition credit, string reference, CancellationToken cancellationToken)
    {
        if (Unlimited(credit) || _answer.Unlocked.Contains($"{credit.Id}:{reference}"))
            return true;

        var status = await Spend(credit, reference, 1, cancellationToken);
        if (status is SpendStatus.Spent or SpendStatus.AlreadyUnlocked or SpendStatus.Unlimited)
            return true;
        if (status != SpendStatus.NotEnough)
            return false;

        // Nothing left: offer credits and the purchases that make the credit unlimited, then try once more.
        if (!await _purchases.OpenPaywallAsync(new PaywallRequest(null) { Credit = credit.Value }, cancellationToken))
            return false;
        status = await Spend(credit, reference, 1, cancellationToken);
        return status is SpendStatus.Spent or SpendStatus.AlreadyUnlocked or SpendStatus.Unlimited;
    }

    public async Task<SpendStatus> Spend(CreditDefinition credit, string reference, int amount, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        try
        {
            var answer = await _backend.SpendAsync(new SpendRequest(credit.Id, reference, amount), cancellationToken);
            Apply(answer.Credits);
            return answer.Status;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log("spend", e);
            return SpendStatus.Failed;
        }
    }

    /// <summary>A consumable was bought: the credits are added (test store) or awaited from the server.</summary>
    public async Task PurchasedAsync(ProductDefinition product, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _backend.PurchasedAsync(product, cancellationToken))
                Log("purchase", new TimeoutException($"The credits of {product.Value} have not reached the server yet; they show at the next refresh."));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log("purchase", e);
        }
        await RefreshAsync(cancellationToken);
    }

    // The server's answer, or a purchase this device already knows is active (no wait for the webhook).
    private bool Unlimited(CreditDefinition credit) =>
        _answer.Unlimited.Contains(credit.Id) || credit.UnlimitedWith.Any(p => _purchases.State(p.Value).IsActive);

    private void Apply(CreditsAnswer answer)
    {
        var changed = !Same(_answer, answer);
        _answer = answer;
        Preferences.Set(_key, JsonSerializer.Serialize(answer, PurchasesWireJson.Default.CreditsAnswer));
        if (!changed)
            return;

        if (MainThread.IsMainThread)
            Changed?.Invoke(this, EventArgs.Empty);
        else
            MainThread.BeginInvokeOnMainThread(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private static bool Same(CreditsAnswer a, CreditsAnswer b) =>
        a.Balances.Count == b.Balances.Count && a.Balances.All(kv => b.Balances.GetValueOrDefault(kv.Key, int.MinValue) == kv.Value)
        && a.Unlocked.Order().SequenceEqual(b.Unlocked.Order())
        && a.Unlimited.Order().SequenceEqual(b.Unlimited.Order());

    private static CreditsAnswer? Load(string key)
    {
        try
        {
            return Preferences.Get(key, null) is { Length: > 0 } json ? JsonSerializer.Deserialize(json, PurchasesWireJson.Default.CreditsAnswer) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Log(string what, Exception e) =>
        System.Diagnostics.Debug.WriteLine($"[Spine] Credits: {what} failed: {e}");
}

/// <summary>The app installation's id, shared with Spine's other packages; created on first use.</summary>
internal static class Installation
{
    private static string? _id;

    public static async Task<string> IdAsync()
    {
        if (_id is not null)
            return _id;
        if (await SecureStorage.Default.GetAsync(SpineInstallation.IdKey) is { Length: > 0 } existing)
            return _id = existing;
        var created = Guid.NewGuid().ToString("N");
        await SecureStorage.Default.SetAsync(SpineInstallation.IdKey, created);
        return _id = created;
    }
}
