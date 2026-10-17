using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Plugin.Maui.Spine.Server.Authentication.Tests;

/// <summary>A provider that signs its own ID tokens, standing in for Apple, Google or Microsoft.</summary>
internal sealed class FakeIssuer(string issuer)
{
    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString("N")[..8] };

    public string Issuer { get; } = issuer;

    public IConfigurationManager<OpenIdConnectConfiguration> Configuration
    {
        get
        {
            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = Issuer,
                AuthorizationEndpoint = $"{Issuer}/authorize",
                TokenEndpoint = $"{Issuer}/token",
            };
            configuration.SigningKeys.Add(_key);
            return new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        }
    }

    public string Token(
        string subject, string audience, string? nonce = null, string? email = null, object? emailVerified = null,
        DateTime? expires = null, string? issuer = null, IDictionary<string, object>? extra = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = subject };
        if (nonce is not null) claims["nonce"] = nonce;
        if (email is not null) claims["email"] = email;
        if (emailVerified is not null) claims["email_verified"] = emailVerified;
        foreach (var (key, value) in extra ?? new Dictionary<string, object>()) claims[key] = value;

        var expiry = expires ?? DateTime.UtcNow.AddMinutes(10);
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience,
            IssuedAt = expiry.AddMinutes(-20),
            NotBefore = expiry.AddMinutes(-20),
            Expires = expiry,
            Claims = claims,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
    }

    public static string AppleNonce(string raw) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
}

internal sealed class RecordingEmailSender : ISpineAuthEmailSender
{
    public List<SpineAuthEmail> Sent { get; } = [];

    public Task SendAsync(SpineAuthEmail email, CancellationToken cancellationToken = default)
    {
        Sent.Add(email);
        return Task.CompletedTask;
    }

    public string LastCode => Sent.Last(e => e.Code is not null).Code!;
}

/// <summary>Apple's token endpoints: hands out a refresh token for any code and records every call.</summary>
internal sealed class FakeAppleEndpoints : HttpMessageHandler
{
    public List<(string Path, Dictionary<string, string> Form)> Calls { get; } = [];

    /// <summary>Answers a provider's token endpoint (any path ending in <c>/token</c> but Apple's revocation code exchange stays as it is).</summary>
    public Func<Dictionary<string, string>, HttpResponseMessage>? TokenEndpoint { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p[1]));
        Calls.Add((request.RequestUri!.AbsolutePath, form));

        if (TokenEndpoint is { } answer && request.RequestUri.AbsolutePath.EndsWith("/token")) return answer(form);

        return request.RequestUri.AbsolutePath == "/auth/token"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { refresh_token = "apple-refresh-1", id_token = "x" }) }
            : new HttpResponseMessage(HttpStatusCode.OK);
    }
}

/// <summary>A test server with Spine sign-in: Apple, Google, Microsoft and a custom provider on fake issuers, email and password, and a protected route.</summary>
internal sealed class AuthHost : IAsyncDisposable
{
    public const string Bundle = "se.example.app";
    public const string GoogleClient = "google-android";
    public const string GoogleWeb = "google-web";
    public const string ServicesId = "se.example.web";
    public const string MicrosoftClient = "msal-client";
    public const string Tenant = "11111111-2222-3333-4444-555555555555";
    public const string PasskeyDomain = "api.test";
    public const string AndroidFingerprint = "AB:CD:EF:01:23:45:67:89:AB:CD:EF:01:23:45:67:89:AB:CD:EF:01:23:45:67:89:AB:CD:EF:01:23:45:67:89";

    private AuthHost(WebApplication app, FakeTimeProvider time, FakeIssuer apple, FakeIssuer google, FakeIssuer microsoft, List<string> deleted)
    {
        App = app;
        Time = time;
        Http = app.GetTestClient();
        AppleIssuer = apple;
        GoogleIssuer = google;
        MicrosoftIssuer = microsoft;
        DeletedUsers = deleted;
    }

    public WebApplication App { get; }
    public FakeTimeProvider Time { get; }
    public HttpClient Http { get; }
    public FakeIssuer AppleIssuer { get; }
    public FakeIssuer GoogleIssuer { get; }
    public FakeIssuer MicrosoftIssuer { get; }
    public IReadOnlyList<string> DeletedUsers { get; }
    public RecordingEmailSender Email => App.Services.GetRequiredService<RecordingEmailSender>();
    public FakeAppleEndpoints AppleEndpoints => App.Services.GetRequiredService<FakeAppleEndpoints>();
    public InMemorySpineAuthStore Store => (InMemorySpineAuthStore)App.Services.GetRequiredService<ISpineAuthStore>();
    public IUserResolver Users => App.Services.GetRequiredService<IUserResolver>();

    public static async Task<AuthHost> StartAsync(Action<SpineAuthOptions>? configure = null, bool passkeys = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var issuers = (Apple: new FakeIssuer("https://appleid.apple.com"), Google: new FakeIssuer("https://accounts.google.com"),
            Microsoft: new FakeIssuer($"https://login.microsoftonline.com/{Tenant}/v2.0"));
        var deleting = new List<string>();

        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton<RecordingEmailSender>();
        builder.Services.AddSingleton<ISpineAuthEmailSender>(sp => sp.GetRequiredService<RecordingEmailSender>());
        builder.Services.AddSingleton<FakeAppleEndpoints>();
        builder.Services.AddAuthorization();
        builder.Services.AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.PublicUrl = "https://api.test/auth";
            a.RedirectSchemes.Add("se.example.app");
            a.AllowLoopbackRedirects = true;
            a.SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();
            a.Apple(o =>
            {
                o.BundleIds.Add(Bundle);
                o.ServicesId = ServicesId;
                o.TeamId = "TEAM123456";
                o.KeyId = "KEY1234567";
                o.PrivateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();
                o.Configuration = issuers.Apple.Configuration;
            });
            a.Google(o =>
            {
                o.ClientIds.Add(GoogleClient);
                o.WebClientId = GoogleWeb;
                o.WebClientSecret = "google-secret";
                o.Configuration = issuers.Google.Configuration;
            });
            a.Microsoft(o =>
            {
                o.ClientId = MicrosoftClient;
                o.Tenant = "organizations";
                o.Configuration = issuers.Microsoft.Configuration;
            });
            a.EmailPassword();
            if (passkeys)
            {
                a.Passkeys(p =>
                {
                    p.RelyingPartyId = PasskeyDomain;
                    p.RelyingPartyName = "Example";
                    p.AppleAppIds.Add($"TEAM123456.{Bundle}");
                    p.AndroidApps.Add(new SpinePasskeyAndroidApp(Bundle, [AndroidFingerprint]));
                });
            }
            a.UseInMemoryStore();
            a.OnAccountDeleting = (user, _, _) =>
            {
                deleting.Add(user);
                return Task.CompletedTask;
            };
            configure?.Invoke(a);
        });
        builder.Services.AddHttpClient(IdTokenValidator.HttpClientName).ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<FakeAppleEndpoints>());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapSpineAuthentication();
        app.MapSpinePasskeyAssociations();
        app.MapGet("/me", (ClaimsPrincipal user) => user.FindFirst("sub")!.Value).RequireAuthorization();
        await app.StartAsync();

        return new AuthHost(app, time, issuers.Apple, issuers.Google, issuers.Microsoft, deleting);
    }

    public async Task<string> NonceAsync() =>
        (await Http.GetFromJsonAsync<SpineNonce>("/auth/nonce", SpineAuthJson.Options))!.Nonce;

    public Task<HttpResponseMessage> PostAsync(string path, object body) =>
        Http.PostAsync(path, JsonContent.Create(body, body.GetType(), options: SpineAuthJson.Options));

    public async Task<SpineTokens> SignInWithAppleAsync(string subject = "apple-001", string? email = "anna@example.com", string? code = null)
    {
        var nonce = await NonceAsync();
        var token = AppleIssuer.Token(subject, Bundle, FakeIssuer.AppleNonce(nonce), email, email is null ? null : "true");
        var response = await PostAsync("/auth/apple/token", new SpineIdTokenRequest(token, nonce, code));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SpineTokens>(SpineAuthJson.Options))!;
    }

    public async Task<HttpResponseMessage> WithBearerAsync(HttpMethod method, string path, string accessToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Http.SendAsync(request);
    }

    public static async Task<SpineAuthProblem?> ProblemAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<SpineAuthProblem>(SpineAuthJson.Options);

    public async ValueTask DisposeAsync() => await App.DisposeAsync();
}
