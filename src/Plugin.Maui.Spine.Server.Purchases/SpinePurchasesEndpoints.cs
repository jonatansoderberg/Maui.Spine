using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.Purchases;

/// <summary>The purchases endpoints as handlers, for ASP.NET Core (<see cref="SpinePurchasesServerExtensions.MapSpinePurchases"/>) and Azure Functions.</summary>
public static class SpinePurchasesEndpoints
{
    /// <summary><c>GET {prefix}/credits</c>: the caller's balances, unlocked references and unlimited credits.</summary>
    public static async Task<IResult> CreditsAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var ledger = request.HttpContext.RequestServices.GetRequiredService<IPurchaseLedger>();
        if (await ledger.CallerAsync(request) is not { } caller)
            return Results.Unauthorized();

        NoStore(request);
        return Results.Json(await ledger.GetCreditsAsync(caller, cancellationToken), PurchasesWireJson.Default.CreditsAnswer);
    }

    /// <summary><c>POST {prefix}/credits/spend</c>: spends credits on a reference once; a second spend on it costs nothing.</summary>
    public static async Task<IResult> SpendAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var ledger = (PurchaseLedger)request.HttpContext.RequestServices.GetRequiredService<IPurchaseLedger>();
        if (await ledger.CallerAsync(request) is not { } caller)
            return Results.Unauthorized();

        SpendRequest? spend;
        try
        {
            spend = await request.ReadFromJsonAsync(PurchasesWireJson.Default.SpendRequest, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest("The body is not a spend: { \"credit\": \"…\", \"reference\": \"…\", \"amount\": 1 }.");
        }

        if (spend is not { Credit.Length: > 0, Reference.Length: > 0 and <= 256 } || spend.Amount < 1)
            return Results.BadRequest("A spend needs a credit, a reference of at most 256 characters and an amount of at least 1.");
        if (ledger.Catalog.CreditNamed(spend.Credit) is not { } credit)
            return Results.BadRequest($"There is no credit '{spend.Credit}'.");

        NoStore(request);
        var answer = await ledger.SpendAsync(caller, credit.Value, spend.Reference, spend.Amount, cancellationToken);
        return Results.Json(answer, PurchasesWireJson.Default.SpendAnswer);
    }

    /// <summary><c>POST {prefix}/revenuecat</c>: RevenueCat's webhook, checked against its Authorization header.</summary>
    public static async Task<IResult> RevenueCatAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var services = request.HttpContext.RequestServices;
        var options = services.GetRequiredService<SpinePurchasesServerOptions>();
        if (string.IsNullOrEmpty(options.RevenueCatWebhookAuthorization))
            return Results.NotFound();
        if (!RevenueCatWebhook.IsAuthorized(options.RevenueCatWebhookAuthorization, request.Headers.Authorization.ToString()))
            return Results.Unauthorized();

        JsonDocument body;
        try
        {
            body = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        using (body)
            return await services.GetRequiredService<RevenueCatWebhook>().ApplyAsync(body.RootElement, cancellationToken) ? Results.Ok() : Results.BadRequest();
    }

    private static void NoStore(HttpRequest request) => request.HttpContext.Response.Headers.CacheControl = "no-store";
}

/// <summary>Registers and maps Spine purchases on the server.</summary>
public static class SpinePurchasesServerExtensions
{
    /// <summary>Registers <see cref="IPurchaseLedger"/> and the webhook for an app that sells no credits.</summary>
    public static IServiceCollection AddSpinePurchases<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TPurchase,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TProduct>(
        this IServiceCollection services, Action<SpinePurchasesServerOptions> configure)
        where TPurchase : struct, Enum
        where TProduct : struct, Enum =>
        services.Add(PurchaseCatalog.Create<TPurchase, TProduct>(), configure);

    /// <summary>
    /// Registers <see cref="IPurchaseLedger"/> and the webhook for the app's purchase, product and credit enums: the same
    /// enums the app registers with <c>UseSpinePurchases</c>, in a project both reference.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddSpinePurchases&lt;AppPurchase, AppProduct, AppCredit&gt;(o =>
    /// {
    ///     o.UseAzureTableStore(builder.Configuration["Purchases:Storage"]!);
    ///     o.RevenueCatWebhookAuthorization = builder.Configuration["RevenueCat:WebhookAuthorization"];
    /// });
    /// app.MapSpinePurchases();   // /purchases/credits, /purchases/credits/spend, /purchases/revenuecat
    /// </code>
    /// </example>
    public static IServiceCollection AddSpinePurchases<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TPurchase,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TProduct,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TCredit>(
        this IServiceCollection services, Action<SpinePurchasesServerOptions> configure)
        where TPurchase : struct, Enum
        where TProduct : struct, Enum
        where TCredit : struct, Enum =>
        services.Add(PurchaseCatalog.Create<TPurchase, TProduct, TCredit>(), configure);

    private static IServiceCollection Add(this IServiceCollection services, PurchaseCatalog catalog, Action<SpinePurchasesServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new SpinePurchasesServerOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(catalog);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => options.StoreFactory!(sp));
        services.AddSingleton<PurchaseLedger>();
        services.AddSingleton<IPurchaseLedger>(static sp => sp.GetRequiredService<PurchaseLedger>());
        services.AddSingleton<RevenueCatWebhook>();
        services.AddLogging();
        return services;
    }

    /// <summary>
    /// Maps <c>GET {prefix}/credits</c>, <c>POST {prefix}/credits/spend</c> and <c>POST {prefix}/revenuecat</c>. The credit
    /// endpoints know the caller from the signed-in user or the installation header; the webhook from its Authorization header.
    /// </summary>
    public static IEndpointRouteBuilder MapSpinePurchases(this IEndpointRouteBuilder endpoints, string prefix = "/purchases")
    {
        var root = prefix.TrimEnd('/');
        endpoints.MapGet($"{root}/credits", (HttpRequest request, CancellationToken ct) => SpinePurchasesEndpoints.CreditsAsync(request, ct));
        endpoints.MapPost($"{root}/credits/spend", (HttpRequest request, CancellationToken ct) => SpinePurchasesEndpoints.SpendAsync(request, ct));
        endpoints.MapPost($"{root}/revenuecat", (HttpRequest request, CancellationToken ct) => SpinePurchasesEndpoints.RevenueCatAsync(request, ct));
        return endpoints;
    }
}
