using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Plugin.Maui.Spine.Server.Purchases;

/// <summary>The store, the credits and the RevenueCat webhook of <c>AddSpinePurchases</c>.</summary>
public sealed class SpinePurchasesServerOptions
{
    /// <summary>The header that carries the app's installation id; Plugin.Maui.Spine sends it on every call to its server.</summary>
    public const string InstallationHeader = "Spine-Installation";

    internal Func<IServiceProvider, IPurchaseLedgerStore>? StoreFactory { get; private set; }

    /// <summary>Keeps the ledgers in memory: for development and tests.</summary>
    public SpinePurchasesServerOptions UseInMemoryStore()
    {
        StoreFactory = static _ => new InMemoryPurchaseLedgerStore();
        return this;
    }

    /// <summary>Keeps the ledgers in Azure Table Storage, in the table <c>{prefix}Ledger</c>.</summary>
    public SpinePurchasesServerOptions UseAzureTableStore(string connectionString, string prefix = "SpinePurchases")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        StoreFactory = _ => new AzureTablePurchaseLedgerStore(connectionString, prefix);
        return this;
    }

    /// <summary>Keeps the ledgers in a store of the app's own.</summary>
    public SpinePurchasesServerOptions UseStore(Func<IServiceProvider, IPurchaseLedgerStore> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        StoreFactory = factory;
        return this;
    }

    /// <summary>
    /// The value RevenueCat sends in the webhook's Authorization header, as set in the RevenueCat project's webhook. Without
    /// it the webhook endpoint answers 404. Keep it in user secrets or the app's settings, not in code.
    /// </summary>
    public string? RevenueCatWebhookAuthorization { get; set; }

    /// <summary>
    /// Who is calling, as the ledger's key. Without it: <c>user:{sub}</c> for a request with a signed-in user (the JWT
    /// <c>sub</c> claim), <c>installation:{id}</c> for one with the <see cref="InstallationHeader"/>, and nobody otherwise,
    /// which the endpoints answer with 401. The RevenueCat app user id must be the same key, which
    /// Plugin.Maui.Spine.Purchases.RevenueCat does by default.
    /// </summary>
    public Func<HttpRequest, ValueTask<string?>>? Identify { get; set; }

    internal async ValueTask<string?> IdentifyAsync(HttpRequest request)
    {
        if (Identify is not null)
            return await Identify(request);

        var user = request.HttpContext.User;
        // Azure Functions and minimal hosts without UseAuthentication(): ask the default scheme (Spine's JWT bearer) here.
        if (user.Identity?.IsAuthenticated != true
            && request.HttpContext.RequestServices.GetService<IAuthenticationSchemeProvider>() is { } schemes
            && await schemes.GetDefaultAuthenticateSchemeAsync() is not null
            && await request.HttpContext.AuthenticateAsync() is { Succeeded: true, Principal: { } principal })
            user = principal;

        if (user.Identity?.IsAuthenticated == true && (user.FindFirst("sub") ?? user.FindFirst(ClaimTypes.NameIdentifier)) is { Value.Length: > 0 } subject)
            return CallerKeys.User(subject.Value);
        if (request.Headers[InstallationHeader].ToString() is { Length: > 0 and <= 128 } installation)
            return CallerKeys.Installation(installation);
        return null;
    }

    internal void Validate()
    {
        if (StoreFactory is null)
            throw new InvalidOperationException("AddSpinePurchases: pick a ledger store with UseInMemoryStore(), UseAzureTableStore(…) or UseStore(…).");
    }
}
