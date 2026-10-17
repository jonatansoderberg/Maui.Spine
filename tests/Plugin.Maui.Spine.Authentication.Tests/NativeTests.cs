using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Plugin.Maui.Spine.Server.Authentication;
using Xunit;

namespace Plugin.Maui.Spine.Authentication.Tests;

/// <summary>A provider's own sheet (Credential Manager for Google) against a real Spine.Server.Authentication.</summary>
public sealed class NativeTests : IAsyncLifetime
{
    // Credential Manager issues the ID token for the web client the app names as its serverClientId.
    private const string ServerClientId = "google-web.apps.googleusercontent.com";
    private const string MicrosoftClient = "msal-app";
    private const string Tenant = "3f1d0c8e-0000-4000-8000-000000000001";
    private static readonly string MicrosoftIssuer = $"https://login.microsoftonline.com/{Tenant}/v2.0";

    private readonly RsaSecurityKey _googleKey = new(RSA.Create(2048)) { KeyId = "g1" };
    private WebApplication _server = null!;
    private SpineAuthFlows _flows = null!;
    private SpineAuthSession _session = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var google = new OpenIdConnectConfiguration { Issuer = "https://accounts.google.com" };
        google.SigningKeys.Add(_googleKey);
        var microsoft = new OpenIdConnectConfiguration { Issuer = MicrosoftIssuer };
        microsoft.SigningKeys.Add(_googleKey);

        builder.Services.AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();
            a.Google(o =>
            {
                o.ClientIds.Add(ServerClientId);
                o.Configuration = new StaticConfigurationManager<OpenIdConnectConfiguration>(google);
            });
            a.Microsoft(o =>
            {
                o.ClientId = MicrosoftClient;
                o.Configuration = new StaticConfigurationManager<OpenIdConnectConfiguration>(microsoft);
            });
            a.UseInMemoryStore();
        });

        _server = builder.Build();
        _server.MapSpineAuthentication("/auth");
        await _server.StartAsync();

        var http = new HttpClient(_server.GetTestServer().CreateHandler()) { BaseAddress = new Uri("http://localhost/") };
        var client = new SpineAuthClient(http, new Uri("http://localhost/auth/"));
        _session = new SpineAuthSession(client, new InMemorySpineAuthStorage(), TimeProvider.System, NullLogger.Instance);
        _flows = new SpineAuthFlows(client, _session);
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private string GoogleToken(string nonce, string audience = ServerClientId) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "https://accounts.google.com",
        Audience = audience,
        Expires = DateTime.UtcNow.AddMinutes(10),
        Claims = new Dictionary<string, object> { ["sub"] = "google-7", ["nonce"] = nonce, ["email"] = "eva@example.com", ["email_verified"] = true, ["name"] = "Eva" },
        SigningCredentials = new SigningCredentials(_googleKey, SecurityAlgorithms.RsaSha256),
    });

    [Fact]
    public async Task The_sheets_id_token_signs_in_with_the_servers_nonce()
    {
        var sheet = new FakeSheet(nonce => new NativeCredential(GoogleToken(nonce), "Eva"));

        var result = await _flows.NativeAsync("google", sheet, default);

        Assert.Equal(SignInStatus.SignedIn, result.Status);
        Assert.Equal("google", result.User!.Provider);
        Assert.Equal("eva@example.com", result.User.Email);
        Assert.NotNull(sheet.Nonce);
        Assert.NotNull(await _session.GetAccessTokenAsync());
    }

    [Fact]
    public async Task A_provider_with_its_own_nonce_sends_none()
    {
        // MSAL puts a nonce of its own in Microsoft's token; the server's nonce would not match it.
        var msal = new FakeSheet(_ => new NativeCredential(new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = MicrosoftIssuer,
            Audience = MicrosoftClient,
            Expires = DateTime.UtcNow.AddMinutes(10),
            Claims = new Dictionary<string, object> { ["sub"] = "ms-1", ["tid"] = Tenant, ["nonce"] = "msal-own", ["name"] = "Mo" },
            SigningCredentials = new SigningCredentials(_googleKey, SecurityAlgorithms.RsaSha256),
        })), usesNonce: false);

        var result = await _flows.NativeAsync("microsoft", msal, default);

        Assert.Equal(SignInStatus.SignedIn, result.Status);
        Assert.Equal("microsoft", result.User!.Provider);
        Assert.Null(msal.Nonce);
    }

    [Fact]
    public async Task A_closed_sheet_is_a_cancel()
    {
        var result = await _flows.NativeAsync("google", new FakeSheet(_ => null), default);

        Assert.Equal(SignInStatus.Canceled, result.Status);
        Assert.Null(_session.User);
    }

    [Fact]
    public async Task A_token_for_another_nonce_is_refused()
    {
        var result = await _flows.NativeAsync("google", new FakeSheet(_ => new NativeCredential(GoogleToken("someone-elses"))), default);

        Assert.Equal(SignInStatus.Failed, result.Status);
        Assert.Null(_session.User);
    }

    [Fact]
    public async Task A_token_for_another_client_is_refused()
    {
        var sheet = new FakeSheet(nonce => new NativeCredential(GoogleToken(nonce, "other-app"), AccountId: "acct-1"));
        var result = await _flows.NativeAsync("google", sheet, default);

        Assert.Equal(SignInStatus.Failed, result.Status);
        Assert.Equal("acct-1", sheet.Refused);   // forgotten, so the next sign-in asks instead of sending it again
    }

    private sealed class FakeSheet(Func<string, NativeCredential?> answer, bool usesNonce = true) : INativeSignIn
    {
        public string? Nonce { get; private set; }

        public bool UsesNonce => usesNonce;

        public string? Refused { get; private set; }

        public Task RefusedAsync(NativeCredential credential)
        {
            Refused = credential.AccountId;
            return Task.CompletedTask;
        }

        public Task<NativeCredential?> SignInAsync(string? nonce, CancellationToken cancellationToken)
        {
            Nonce = nonce;
            return Task.FromResult(answer(nonce ?? ""));
        }

        public Task SignedOutAsync() => Task.CompletedTask;
    }
}

public sealed class NativeOptionsTests
{
    private static SpineAuthenticationOptions WithGoogle(bool supported) =>
        new SpineAuthenticationOptions { Server = new Uri("https://api.test/auth"), CallbackScheme = "se.example.app" }
            .AddNative(new NativeProvider("google", supported, _ => throw new NotSupportedException()), "Google");

    [Fact]
    public void A_native_provider_is_also_a_browser_provider_for_the_platforms_without_its_sheet()
    {
        var android = WithGoogle(supported: true);
        var ios = WithGoogle(supported: false);

        Assert.Equal("google", Assert.Single(android.BrowserProviders).Name);
        Assert.False(android.IsBrowser(android.BrowserProviders[0]));
        Assert.True(ios.IsBrowser(ios.BrowserProviders[0]));
    }

    [Fact]
    public void Google_on_the_App_Store_needs_Sign_in_with_Apple()
    {
        var options = WithGoogle(supported: false);
        Assert.Contains("4.8", Assert.Throws<InvalidOperationException>(() => options.Validate(returnsToScheme: true, appStore: true)).Message);

        options.Validate(returnsToScheme: true, appStore: false);

        options.AppleNotRequired = true;
        options.Validate(returnsToScheme: true, appStore: true);

        options.AppleNotRequired = false;
        options.AddApple().Validate(returnsToScheme: true, appStore: true);
    }

    [Fact]
    public void Google_needs_a_Spine_server()
    {
        var options = new SpineAuthenticationOptions { CallbackScheme = "se.example.app" }
            .AddNative(new NativeProvider("google", true, _ => throw new NotSupportedException()), "Google")
            .UseIdentityProvider(p => { p.Authority = new Uri("https://id.example.com/"); p.ClientId = "app"; });

        Assert.Contains("AddGoogle", Assert.Throws<InvalidOperationException>(() => options.Validate(returnsToScheme: true)).Message);
    }
}
