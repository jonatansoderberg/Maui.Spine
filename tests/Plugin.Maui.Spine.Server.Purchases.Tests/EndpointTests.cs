using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Purchases;
using Xunit;

namespace Plugin.Maui.Spine.Server.Purchases.Tests;

public enum MatchPurchase
{
    [PurchaseId("live")]
    Live,
}

public enum MatchProduct
{
    [StoreProduct(ProductKind.Subscription, Apple = "se.example.live.month", Google = "live:month")]
    [Grants(MatchPurchase.Live)]
    LiveMonthly,

    [StoreProduct(ProductKind.Consumable, Apple = "se.example.match", Google = "match")]
    [Adds(MatchCredit.LiveMatch, 1)]
    OneMatch,

    [StoreProduct(ProductKind.Consumable, Apple = "se.example.match5", Google = "match5")]
    [Adds(MatchCredit.LiveMatch, 5)]
    FiveMatches,
}

public enum MatchCredit
{
    [FreeCredits(1)]
    [UnlimitedWith(MatchPurchase.Live)]
    LiveMatch,
}

public sealed class EndpointTests : IAsyncLifetime
{
    private const string WebhookAuth = "Bearer test-webhook";
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        builder.Services.AddSpinePurchases<MatchPurchase, MatchProduct, MatchCredit>(o =>
        {
            o.UseInMemoryStore();
            o.RevenueCatWebhookAuthorization = WebhookAuth;
        });
        _app = builder.Build();
        _app.MapSpinePurchases();
        await _app.StartAsync();
        _http = _app.GetTestClient();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private HttpRequestMessage As(string installation, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(SpinePurchasesServerOptions.InstallationHeader, installation);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<CreditsAnswer> CreditsAsync(string installation)
    {
        var response = await _http.SendAsync(As(installation, HttpMethod.Get, "/purchases/credits"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(PurchasesWireJson.Default.CreditsAnswer))!;
    }

    private async Task<SpendAnswer> SpendAsync(string installation, string reference)
    {
        var response = await _http.SendAsync(As(installation, HttpMethod.Post, "/purchases/credits/spend", new { credit = "LiveMatch", reference, amount = 1 }));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(PurchasesWireJson.Default.SpendAnswer))!;
    }

    private Task<HttpResponseMessage> WebhookAsync(string json, string auth = WebhookAuth)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/purchases/revenuecat") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("Authorization", auth);
        return _http.SendAsync(request);
    }

    private static string Event(string type, string user, string product, string store = "APP_STORE", string transaction = "t1", long? expires = null, string? cancelReason = null) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            api_version = "1.0",
            @event = new
            {
                type,
                app_user_id = user,
                product_id = product,
                store,
                transaction_id = transaction,
                purchased_at_ms = Now.ToUnixTimeMilliseconds(),
                expiration_at_ms = expires,
                period_type = "NORMAL",
                cancel_reason = cancelReason,
            },
        });

    [Fact]
    public async Task Nobody_known_is_401()
    {
        var response = await _http.GetAsync("/purchases/credits");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_new_caller_gets_the_free_credit_once()
    {
        Assert.Equal(1, (await CreditsAsync("phone-1")).Balances["LiveMatch"]);
        Assert.Equal(1, (await CreditsAsync("phone-1")).Balances["LiveMatch"]);
    }

    [Fact]
    public async Task A_spend_unlocks_its_reference_once_and_then_there_is_nothing_left()
    {
        var first = await SpendAsync("phone-2", "match:100");
        Assert.Equal(SpendStatus.Spent, first.Status);
        Assert.Equal(0, first.Credits.Balances["LiveMatch"]);
        Assert.Contains("LiveMatch:match:100", first.Credits.Unlocked);

        Assert.Equal(SpendStatus.AlreadyUnlocked, (await SpendAsync("phone-2", "match:100")).Status);
        Assert.Equal(SpendStatus.NotEnough, (await SpendAsync("phone-2", "match:101")).Status);
    }

    [Fact]
    public async Task A_bought_consumable_adds_credits_once_per_transaction()
    {
        var caller = CallerKeys.Installation("phone-3");
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(Event("NON_RENEWING_PURCHASE", caller, "se.example.match5", transaction: "t-5"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(Event("NON_RENEWING_PURCHASE", caller, "se.example.match5", transaction: "t-5"))).StatusCode);

        Assert.Equal(6, (await CreditsAsync("phone-3")).Balances["LiveMatch"]);
    }

    [Fact]
    public async Task A_play_consumable_is_found_by_its_play_id()
    {
        await WebhookAsync(Event("NON_RENEWING_PURCHASE", CallerKeys.Installation("phone-4"), "match", store: "PLAY_STORE", transaction: "GPA.1"));
        Assert.Equal(2, (await CreditsAsync("phone-4")).Balances["LiveMatch"]);
    }

    [Fact]
    public async Task An_active_subscription_makes_spending_free_until_it_ends()
    {
        var caller = CallerKeys.Installation("phone-5");
        await WebhookAsync(Event("INITIAL_PURCHASE", caller, "se.example.live.month", expires: Now.AddDays(30).ToUnixTimeMilliseconds()));

        var spend = await SpendAsync("phone-5", "match:200");
        Assert.Equal(SpendStatus.Unlimited, spend.Status);
        Assert.Equal(1, spend.Credits.Balances["LiveMatch"]);
        Assert.Contains("LiveMatch", spend.Credits.Unlimited);

        await WebhookAsync(Event("EXPIRATION", caller, "se.example.live.month", expires: Now.AddMinutes(-1).ToUnixTimeMilliseconds()));
        Assert.Equal(SpendStatus.Spent, (await SpendAsync("phone-5", "match:200")).Status);
    }

    [Fact]
    public async Task The_server_code_sees_the_subscription_and_the_unlocks()
    {
        var caller = CallerKeys.Installation("phone-6");
        var ledger = _app.Services.GetRequiredService<IPurchaseLedger>();
        Assert.False(await ledger.IsActiveAsync(caller, MatchPurchase.Live));

        await WebhookAsync(Event("INITIAL_PURCHASE", caller, "live:month", store: "PLAY_STORE", expires: Now.AddDays(30).ToUnixTimeMilliseconds()));
        Assert.True(await ledger.IsActiveAsync(caller, MatchPurchase.Live));
        Assert.True(await ledger.IsUnlockedAsync(caller, MatchCredit.LiveMatch, "match:any"));

        await WebhookAsync(Event("CANCELLATION", caller, "live:month", store: "PLAY_STORE", expires: Now.AddDays(30).ToUnixTimeMilliseconds(), cancelReason: "CUSTOMER_SUPPORT"));
        Assert.False(await ledger.IsActiveAsync(caller, MatchPurchase.Live));
    }

    [Fact]
    public async Task A_cancelled_renewal_stays_active_to_its_end()
    {
        var caller = CallerKeys.User("u-7");
        var ledger = _app.Services.GetRequiredService<IPurchaseLedger>();
        await WebhookAsync(Event("INITIAL_PURCHASE", caller, "se.example.live.month", expires: Now.AddDays(30).ToUnixTimeMilliseconds()));
        await WebhookAsync(Event("CANCELLATION", caller, "se.example.live.month", expires: Now.AddDays(30).ToUnixTimeMilliseconds(), cancelReason: "UNSUBSCRIBE"));

        Assert.True(await ledger.IsActiveAsync(caller, MatchPurchase.Live));
    }

    [Fact]
    public async Task A_grant_from_the_server_code_counts_once()
    {
        var caller = CallerKeys.Installation("phone-8");
        var ledger = _app.Services.GetRequiredService<IPurchaseLedger>();
        Assert.True(await ledger.GrantAsync(caller, MatchCredit.LiveMatch, 3, "demo"));
        Assert.False(await ledger.GrantAsync(caller, MatchCredit.LiveMatch, 3, "demo"));

        Assert.Equal(4, (await CreditsAsync("phone-8")).Balances["LiveMatch"]);
    }

    [Fact]
    public async Task The_webhook_refuses_a_wrong_authorization()
    {
        var response = await WebhookAsync(Event("NON_RENEWING_PURCHASE", CallerKeys.Installation("phone-9"), "se.example.match"), "Bearer wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, (await CreditsAsync("phone-9")).Balances["LiveMatch"]);
    }

    [Fact]
    public async Task An_anonymous_revenuecat_user_and_an_unknown_product_are_skipped()
    {
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(Event("NON_RENEWING_PURCHASE", "$RCAnonymousID:abc", "se.example.match"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(Event("NON_RENEWING_PURCHASE", CallerKeys.Installation("phone-10"), "se.example.unknown"))).StatusCode);
        Assert.Equal(1, (await CreditsAsync("phone-10")).Balances["LiveMatch"]);
    }

    [Fact]
    public async Task A_spend_of_an_unknown_credit_is_400()
    {
        var response = await _http.SendAsync(As("phone-11", HttpMethod.Post, "/purchases/credits/spend", new { credit = "Nope", reference = "x", amount = 1 }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
