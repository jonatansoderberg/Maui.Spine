using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Plugin.Maui.Spine.Server.Authentication;
using Xunit;

namespace Plugin.Maui.Spine.Authentication.Tests;

/// <summary>Linking, unlinking and the pending identity from the app, against a real Spine.Server.Authentication (#575).</summary>
public sealed class AccountLinkingTests : IAsyncDisposable
{
    private const string NativeClient = "google-web.apps.googleusercontent.com";
    private const string WebClient = "google-web";
    private const string Email = "anna@example.com";
    private const string Password = "correct horse";
    private static readonly Uri AppCallback = new("se.example.app://callback");

    private readonly RsaSecurityKey _googleKey = new(RSA.Create(2048)) { KeyId = "g1" };
    private WebApplication _server = null!;
    private HttpClient _http = null!;
    private SpineAuthFlows _flows = null!;
    private SpineAuthSession _session = null!;
    private string? _nonceSeen;
    private string _googleSubject = "google-1";

    private async Task StartAsync(bool askBeforeNewAccount = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var google = new OpenIdConnectConfiguration
        {
            Issuer = "https://accounts.google.com",
            AuthorizationEndpoint = "https://accounts.google.com/authorize",
            TokenEndpoint = "https://accounts.google.com/token",
        };
        google.SigningKeys.Add(_googleKey);

        builder.Services.AddSingleton<ISpineAuthEmailSender>(new Mailbox());
        builder.Services.AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();
            a.PublicUrl = "http://localhost/auth";
            a.RedirectSchemes.Add("se.example.app");
            a.Google(o =>
            {
                o.ClientIds.Add(NativeClient);
                o.WebClientId = WebClient;
                o.WebClientSecret = "secret";
                o.Configuration = new StaticConfigurationManager<OpenIdConnectConfiguration>(google);
            });
            a.EmailPassword(p => p.RequireVerifiedEmail = false);
            a.AskBeforeNewAccount = askBeforeNewAccount;
            a.UseInMemoryStore();
        });
        builder.Services.AddHttpClient(Plugin.Maui.Spine.Server.Authentication.IdTokenValidator.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { id_token = GoogleToken(_nonceSeen!, WebClient), access_token = "x" }),
            }));

        _server = builder.Build();
        _server.MapSpineAuthentication("/auth");
        await _server.StartAsync();

        _http = new HttpClient(_server.GetTestServer().CreateHandler()) { BaseAddress = new Uri("http://localhost/") };
        var client = new SpineAuthClient(_http, new Uri("http://localhost/auth/"));
        _session = new SpineAuthSession(client, new InMemorySpineAuthStorage(), TimeProvider.System, NullLogger.Instance);
        _flows = new SpineAuthFlows(client, _session);
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null) await _server.DisposeAsync();
    }

    private string GoogleToken(string nonce, string audience = NativeClient) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "https://accounts.google.com",
        Audience = audience,
        Expires = DateTime.UtcNow.AddMinutes(10),
        Claims = new Dictionary<string, object> { ["sub"] = _googleSubject, ["nonce"] = nonce, ["email"] = "bo@example.com", ["email_verified"] = true },
        SigningCredentials = new SigningCredentials(_googleKey, SecurityAlgorithms.RsaSha256),
    });

    private sealed class Sheet(Func<string, string> token) : INativeSignIn
    {
        public Task<NativeCredential?> SignInAsync(string? nonce, CancellationToken cancellationToken) =>
            Task.FromResult<NativeCredential?>(new NativeCredential(token(nonce!)));

        public Task SignedOutAsync() => Task.CompletedTask;
    }

    private Sheet GoogleSheet => new(nonce => GoogleToken(nonce));

    private async Task<BrowserAnswer?> FollowAsync(Uri start, Uri callback)
    {
        var toGoogle = await _http.GetAsync(start);
        Assert.Equal(HttpStatusCode.Redirect, toGoogle.StatusCode);
        var google = Query.Of(toGoogle.Headers.Location!);
        _nonceSeen = google["nonce"];

        var back = await _http.GetAsync($"/auth/google/callback?code=google-code&state={Uri.EscapeDataString(google["state"])}");
        return new BrowserAnswer(callback, Query.Of(new Uri(back.Headers.Location!.OriginalString)));
    }

    private async Task<SpineUser> SignedUpWithPasswordAsync()
    {
        var result = await _flows.SignUpAsync(Email, Password, "Anna", default);
        Assert.Equal(SignInStatus.SignedIn, result.Status);
        return result.User!;
    }

    private async Task<string> AccessTokenAsync() => (await _session.GetAccessTokenAsync())!;

    private static string[] Providers(SpineUser user) => user.Logins!.Select(l => l.Provider).Order().ToArray();

    [Fact]
    public async Task Googles_sheet_links_to_the_signed_in_user_without_a_new_sign_in()
    {
        await StartAsync();
        var anna = await SignedUpWithPasswordAsync();
        var signIns = 0;
        var changes = 0;
        _session.SignedIn += (_, _) => signIns++;
        _session.Changed += (_, _) => changes++;

        var result = await _flows.NativeAsync("google", GoogleSheet, default, link: await AccessTokenAsync());

        Assert.Equal(SignInStatus.SignedIn, result.Status);
        Assert.Equal(anna.Id, result.User!.Id);
        Assert.Equal(["google", "password"], Providers(result.User));
        Assert.Equal(["google", "password"], Providers(_session.User!));
        Assert.Equal((0, 1), (signIns, changes));
    }

    [Fact]
    public async Task The_browser_links_through_the_broker_with_a_ticket()
    {
        await StartAsync();
        var anna = await SignedUpWithPasswordAsync();
        var browser = new ScriptedBrowser(FollowAsync, AppCallback);

        var result = await _flows.BrowserAsync("google", browser, default, link: await AccessTokenAsync());

        Assert.Equal(SignInStatus.SignedIn, result.Status);
        Assert.Equal(anna.Id, result.User!.Id);
        Assert.Equal("password", result.User.Provider);
        Assert.Equal(["google", "password"], Providers(result.User));
        Assert.True(Query.Of(browser.Opened!).ContainsKey("link"));
    }

    [Fact]
    public async Task A_google_account_of_another_user_is_refused_with_identity_in_use()
    {
        await StartAsync();
        await _flows.NativeAsync("google", GoogleSheet, default);
        await _flows.SignUpAsync("other@example.com", Password, null, default);

        var result = await _flows.NativeAsync("google", GoogleSheet, default, link: await AccessTokenAsync());

        Assert.Equal(SignInStatus.Failed, result.Status);
        Assert.Equal(SpineAuthErrors.IdentityInUse, result.Error);
        Assert.Equal("password", _session.User!.Provider);
    }

    [Fact]
    public async Task Unlinking_removes_a_login_but_never_the_last()
    {
        await StartAsync();
        await SignedUpWithPasswordAsync();
        await _flows.NativeAsync("google", GoogleSheet, default, link: await AccessTokenAsync());

        var unlinked = await _flows.UnlinkAsync("google", default);
        Assert.Equal(SignInStatus.SignedIn, unlinked.Status);
        Assert.Equal(["password"], Providers(unlinked.User!));

        var last = await _flows.UnlinkAsync("password", default);
        Assert.Equal(SignInStatus.Failed, last.Status);
        Assert.Equal(SpineAuthErrors.LastLogin, last.Error);
    }

    [Fact]
    public async Task Linking_needs_someone_signed_in()
    {
        await StartAsync();

        var (token, problem) = await _flows.LinkingAsAsync(default);

        Assert.Null(token);
        Assert.Equal(SignInStatus.Failed, problem!.Status);
        Assert.Equal(SignInStatus.Failed, (await _flows.UnlinkAsync("google", default)).Status);
    }

    [Fact]
    public async Task A_new_google_account_asks_and_a_new_account_is_made_on_request()
    {
        await StartAsync(askBeforeNewAccount: true);

        var asked = await _flows.NativeAsync("google", GoogleSheet, default);

        Assert.Equal(SignInStatus.ChooseAccount, asked.Status);
        Assert.Equal("google", asked.Pending!.Provider);
        Assert.Equal("bo@example.com", asked.Pending.Email);
        Assert.Null(_session.User);

        var created = await _flows.CreatePendingAsync(asked.Pending, default);
        Assert.Equal(SignInStatus.SignedIn, created.Status);
        Assert.Equal(["google"], Providers(created.User!));
    }

    [Fact]
    public async Task A_new_google_account_joins_the_account_the_user_signs_in_to()
    {
        await StartAsync(askBeforeNewAccount: true);
        var anna = await SignedUpWithPasswordAsync();
        await _session.SignOutAsync();

        var asked = await _flows.BrowserAsync("google", new ScriptedBrowser(FollowAsync, AppCallback), default);
        Assert.Equal(SignInStatus.ChooseAccount, asked.Status);

        Assert.Equal(SignInStatus.SignedIn, (await _flows.PasswordAsync(Email, Password, default)).Status);
        var linked = await _flows.LinkPendingAsync(asked.Pending!, default);

        Assert.Equal(anna.Id, linked.User!.Id);
        Assert.Equal(["google", "password"], Providers(linked.User));

        await _session.SignOutAsync();
        Assert.Equal(anna.Id, (await _flows.NativeAsync("google", GoogleSheet, default)).User!.Id);
    }

    [Fact]
    public async Task A_sign_up_email_is_still_a_code_not_a_choice()
    {
        await StartAsync(askBeforeNewAccount: true);
        await _server.DisposeAsync();
        _server = null!;

        // With verification on, a sign-up answers 202 with no body: an email went out, nothing to choose.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISpineAuthEmailSender>(new Mailbox());
        builder.Services.AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();
            a.EmailPassword();
            a.AskBeforeNewAccount = true;
            a.UseInMemoryStore();
        });
        _server = builder.Build();
        _server.MapSpineAuthentication("/auth");
        await _server.StartAsync();
        var client = new SpineAuthClient(new HttpClient(_server.GetTestServer().CreateHandler()), new Uri("http://localhost/auth/"));
        var flows = new SpineAuthFlows(client, new SpineAuthSession(client, new InMemorySpineAuthStorage(), TimeProvider.System, NullLogger.Instance));

        Assert.Equal(SignInStatus.CodeSent, (await flows.SignUpAsync(Email, Password, null, default)).Status);
    }
}
