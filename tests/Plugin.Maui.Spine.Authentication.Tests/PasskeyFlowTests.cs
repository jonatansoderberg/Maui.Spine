using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.Maui.Spine.Server.Authentication;
using Plugin.Maui.Spine.Server.Authentication.Tests;
using Xunit;

namespace Plugin.Maui.Spine.Authentication.Tests;

/// <summary>Passkeys from the app's side: the flows against a real Spine.Server.Authentication, with a software authenticator as the sheet (#522).</summary>
public sealed class PasskeyFlowTests : IAsyncLifetime
{
    private const string Domain = "api.test";

    private readonly SoftwareAuthenticator _authenticator = new($"https://{Domain}", Domain);
    private WebApplication _server = null!;
    private SpineAuthFlows _flows = null!;
    private SpineAuthSession _session = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISpineAuthEmailSender>(new Mailbox());
        builder.Services.AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();
            a.EmailPassword(p => p.RequireVerifiedEmail = false);
            a.Passkeys(p => p.RelyingPartyId = Domain);
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

    /// <summary>The software authenticator as the platform's sheet; <see cref="Canceled"/> closes it instead.</summary>
    private sealed class Sheet(SoftwareAuthenticator authenticator) : IPasskeyCeremony
    {
        public bool Canceled { get; set; }

        public bool? AutoFill { get; private set; }

        public Task<string?> CreateAsync(string options, CancellationToken cancellationToken) =>
            Task.FromResult(Canceled ? null : authenticator.Create(options));

        public Task<string?> GetAsync(string options, bool autoFill, CancellationToken cancellationToken)
        {
            AutoFill = autoFill;
            return Task.FromResult(Canceled ? null : authenticator.Get(options));
        }
    }

    [Fact]
    public async Task A_passkey_account_signs_up_signs_out_and_signs_in_again()
    {
        var sheet = new Sheet(_authenticator);

        var signedUp = await _flows.PasskeyRegisterAsync(sheet, new SpinePasskeyNewAccount("Bo", "bo@example.com"), default);
        Assert.Equal(SignInStatus.SignedIn, signedUp.Status);
        Assert.Equal("passkey", signedUp.User!.Provider);
        Assert.Equal("passkey", _session.User!.Provider);

        await _session.SignOutAsync();
        var signedIn = await _flows.PasskeySignInAsync(sheet, autoFill: true, default);

        Assert.Equal(SignInStatus.SignedIn, signedIn.Status);
        Assert.Equal(signedUp.User.Id, signedIn.User!.Id);
        Assert.True(sheet.AutoFill);
    }

    [Fact]
    public async Task A_passkey_joins_the_signed_in_user_without_a_new_sign_in()
    {
        var anna = await _flows.SignUpAsync("anna@example.com", "correct horse", "Anna", default);
        var signIns = 0;
        _session.SignedIn += (_, _) => signIns++;

        var linked = await _flows.PasskeyRegisterAsync(new Sheet(_authenticator), null, default, link: await _session.GetAccessTokenAsync());

        Assert.Equal(SignInStatus.SignedIn, linked.Status);
        Assert.Equal(anna.User!.Id, linked.User!.Id);
        Assert.Equal(["passkey", "password"], linked.User.Logins!.Select(l => l.Provider).Order().ToArray());
        Assert.Equal(0, signIns);
    }

    [Fact]
    public async Task Closing_the_sheet_cancels()
    {
        var sheet = new Sheet(_authenticator) { Canceled = true };

        Assert.Equal(SignInStatus.Canceled, (await _flows.PasskeyRegisterAsync(sheet, new SpinePasskeyNewAccount("Bo", null), default)).Status);
        Assert.Equal(SignInStatus.Canceled, (await _flows.PasskeySignInAsync(sheet, autoFill: false, default)).Status);
    }

    [Fact]
    public async Task A_removed_passkey_fails_with_its_own_error()
    {
        var sheet = new Sheet(_authenticator);
        var signedUp = await _flows.PasskeyRegisterAsync(sheet, new SpinePasskeyNewAccount("Bo", null), default);
        var users = _server.Services.GetRequiredService<IUserResolver>();
        foreach (var login in await users.GetLoginsAsync(signedUp.User!.Id)) await users.DeleteLoginAsync(login);

        var result = await _flows.PasskeySignInAsync(sheet, autoFill: false, default);

        Assert.Equal(SignInStatus.Failed, result.Status);
        Assert.Equal(SpineAuthErrors.UnknownPasskey, result.Error);
    }

    [Fact]
    public async Task A_new_account_without_a_name_or_email_fails_before_the_sheet()
    {
        var sheet = new Sheet(_authenticator);

        var result = await _flows.PasskeyRegisterAsync(sheet, new SpinePasskeyNewAccount(null, null), default);

        Assert.Equal(SignInStatus.Failed, result.Status);
        Assert.Equal(SpineAuthErrors.InvalidRequest, result.Error);
        Assert.Empty(_authenticator.Ids);
    }
}
