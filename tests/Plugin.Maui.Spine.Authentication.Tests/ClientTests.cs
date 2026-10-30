using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Server.Authentication;
using Xunit;

namespace Plugin.Maui.Spine.Authentication.Tests;

internal sealed class Mailbox : ISpineAuthEmailSender
{
    private readonly List<SpineAuthEmail> _sent = [];
    private int _taken;

    public Task SendAsync(SpineAuthEmail email, CancellationToken cancellationToken = default)
    {
        lock (_sent) _sent.Add(email);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The next code not yet taken. The server sends after it answers, so the code may arrive a moment later; waiting
    /// for a new one keeps a test from reading an earlier email's code meanwhile.
    /// </summary>
    public async Task<string> CodeAsync()
    {
        for (var i = 0; i < 200; i++)
        {
            lock (_sent)
            {
                var codes = _sent.Where(e => e.Code is not null).ToList();
                if (codes.Count > _taken) return codes[_taken++].Code!;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("No code was sent.");
    }
}

/// <summary>Counts the requests to one path, to see how many refreshes reached the server.</summary>
internal sealed class Counter(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private int _refreshes;

    public int Refreshes => _refreshes;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath == "/auth/token") Interlocked.Increment(ref _refreshes);
        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class ClientTests : IAsyncLifetime
{
    private const string Email = "anna@example.com";
    private const string Password = "correct horse";

    private WebApplication _server = null!;
    private Mailbox _mail = null!;
    private Counter _counter = null!;
    private readonly InMemorySpineAuthStorage _storage = new();
    private readonly FakeTimeProvider _clientTime = new(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _mail = new Mailbox();
        builder.Services.AddSingleton<ISpineAuthEmailSender>(_mail);
        builder.Services.AddAuthorization();
        builder.Services.AddSpineAuthentication(a =>
        {
            a.Issuer = "https://api.test";
            a.SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();
            a.EmailPassword();
            a.UseInMemoryStore();
        });

        _server = builder.Build();
        _server.UseAuthentication();
        _server.UseAuthorization();
        _server.MapSpineAuthentication("/auth");
        _server.MapGet("/me", (ClaimsPrincipal user) => user.FindFirst("sub")!.Value).RequireAuthorization();
        await _server.StartAsync();

        _counter = new Counter(_server.GetTestServer().CreateHandler());
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private SpineAuthClient Client() => new(new HttpClient(_counter) { BaseAddress = new Uri("http://localhost/") }, new Uri("http://localhost/auth/"));

    private (SpineAuthSession Session, SpineAuthFlows Flows) NewApp()
    {
        var client = Client();
        var session = new SpineAuthSession(client, _storage, _clientTime, NullLogger.Instance);
        return (session, new SpineAuthFlows(client, session));
    }

    private async Task<(SpineAuthSession Session, SpineAuthFlows Flows)> SignedUpAsync()
    {
        var app = NewApp();
        Assert.Equal(SignInStatus.CodeSent, (await app.Flows.SignUpAsync(Email, Password, "Anna", default)).Status);
        var verified = await app.Flows.VerifyAsync(Email, await _mail.CodeAsync(), default);
        Assert.Equal(SignInStatus.SignedIn, verified.Status);
        return app;
    }

    [Fact]
    public async Task Sign_up_and_the_code_sign_in_and_the_session_is_kept()
    {
        var (session, _) = await SignedUpAsync();

        Assert.Equal("Anna", session.User!.DisplayName);
        Assert.NotNull(_storage.Value);
        Assert.DoesNotContain("accessToken", _storage.Value, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await session.GetAccessTokenAsync());
    }

    [Fact]
    public async Task A_restarted_app_knows_the_user_at_once_and_refreshes_on_first_use()
    {
        var (first, _) = await SignedUpAsync();
        var refreshesBefore = _counter.Refreshes;

        var (restarted, _) = NewApp();
        await restarted.LoadAsync();
        Assert.Equal(first.User!.Id, restarted.User!.Id);
        Assert.Equal(refreshesBefore, _counter.Refreshes);

        var token = await restarted.GetAccessTokenAsync();
        Assert.NotNull(token);
        Assert.Equal(refreshesBefore + 1, _counter.Refreshes);
    }

    [Fact]
    public async Task Parallel_callers_share_one_refresh()
    {
        await SignedUpAsync();
        var (restarted, _) = NewApp();
        var before = _counter.Refreshes;

        var tokens = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => restarted.GetAccessTokenAsync()));

        // A second refresh with the same token would be a reuse, and the server would end the sign-in.
        Assert.Equal(before + 1, _counter.Refreshes);
        Assert.Single(tokens.Distinct());
        Assert.NotNull(tokens[0]);
    }

    [Fact]
    public async Task An_access_token_is_refreshed_when_under_a_minute_is_left()
    {
        var (session, _) = await SignedUpAsync();
        var first = await session.GetAccessTokenAsync();

        _clientTime.Advance(TimeSpan.FromMinutes(14.5));
        var second = await session.GetAccessTokenAsync();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task A_refresh_the_server_refuses_signs_out()
    {
        var (session, _) = await SignedUpAsync();
        var signedOut = 0;
        session.SignedOut += (_, _) => signedOut++;

        // The same sign-in, restored elsewhere from the stored session, signs out: that revokes it on the server.
        var (other, _) = NewApp();
        await other.SignOutAsync();
        _clientTime.Advance(TimeSpan.FromMinutes(15));

        Assert.Null(await session.GetAccessTokenAsync());
        Assert.Null(session.User);
        Assert.Equal(1, signedOut);
        Assert.Null(_storage.Value);
    }

    [Fact]
    public async Task A_read_error_at_startup_keeps_the_stored_session_for_the_next_launch()
    {
        await SignedUpAsync();
        var stored = _storage.Value;
        var session = new SpineAuthSession(Client(), new FailingStorage(), _clientTime, NullLogger.Instance);

        await session.LoadAsync();

        Assert.Null(session.User);
        Assert.Equal(stored, _storage.Value);
    }

    [Fact]
    public async Task A_caller_that_gives_up_does_not_abandon_the_refresh()
    {
        await SignedUpAsync();
        var (restarted, _) = NewApp();
        using var gaveUp = new CancellationTokenSource();
        gaveUp.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restarted.GetAccessTokenAsync(gaveUp.Token));
        Assert.NotNull(await restarted.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Sign_out_revokes_the_refresh_token_on_the_server()
    {
        var (session, _) = await SignedUpAsync();
        var stored = _storage.Value!;

        await session.SignOutAsync();
        Assert.Null(session.User);

        // The token from before the sign-out no longer works anywhere.
        await _storage.SaveAsync(stored);
        var (copy, _) = NewApp();
        Assert.Null(await copy.GetAccessTokenAsync());
    }

    [Fact]
    public async Task The_handler_adds_the_token_and_retries_once_after_a_401()
    {
        var (session, _) = await SignedUpAsync();
        var refusals = new RefuseFirst(_server.GetTestServer().CreateHandler());
        using var http = new HttpClient(new SpineAuthHandler(session) { InnerHandler = refusals }) { BaseAddress = new Uri("http://localhost/") };

        var response = await http.GetAsync("/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(session.User!.Id, await response.Content.ReadAsStringAsync());
        Assert.Equal(2, refusals.Calls);
    }

    [Fact]
    public async Task Signed_in_but_unable_to_refresh_the_handler_fails_the_request_as_offline()
    {
        await SignedUpAsync();
        var client = new SpineAuthClient(new HttpClient(new Outage(_counter), disposeHandler: false) { BaseAddress = new Uri("http://localhost/") }, new Uri("http://localhost/auth/"));
        var copy = new SpineAuthSession(client, _storage, _clientTime, NullLogger.Instance);
        using var http = new HttpClient(new SpineAuthHandler(copy) { InnerHandler = _server.GetTestServer().CreateHandler() }) { BaseAddress = new Uri("http://localhost/") };

        // A restarted app: the user is known, the access token must be fetched, and the server is out of reach.
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("/me"));

        // The session is kept for when the server is back.
        Assert.NotNull(copy.User);
        Assert.NotNull(_storage.Value);
    }

    [Fact]
    public async Task Without_a_session_the_handler_sends_no_token()
    {
        var (session, _) = NewApp();
        using var http = new HttpClient(new SpineAuthHandler(session) { InnerHandler = _server.GetTestServer().CreateHandler() }) { BaseAddress = new Uri("http://localhost/") };

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/me")).StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_fails_with_the_servers_error()
    {
        await SignedUpAsync();
        var (_, flows) = NewApp();

        var result = await flows.PasswordAsync(Email, "wrong password", default);

        Assert.Equal(SignInStatus.Failed, result.Status);
        Assert.Equal(SpineAuthErrors.InvalidCredentials, result.Error);
    }

    [Fact]
    public async Task An_unverified_account_gets_a_code_instead_of_a_failure()
    {
        var (_, flows) = NewApp();
        await flows.SignUpAsync(Email, Password, null, default);

        Assert.Equal(SignInStatus.CodeSent, (await flows.PasswordAsync(Email, Password, default)).Status);
    }

    [Fact]
    public async Task A_forgotten_password_is_reset_with_the_code()
    {
        await SignedUpAsync();
        var (_, flows) = NewApp();

        Assert.Equal(SignInStatus.CodeSent, (await flows.ForgotAsync(Email, default)).Status);
        var reset = await flows.ResetAsync(Email, await _mail.CodeAsync(), "a new password", default);

        Assert.Equal(SignInStatus.SignedIn, reset.Status);
    }

    [Fact]
    public async Task A_server_that_is_not_spine_names_the_path_and_the_status()
    {
        var client = new SpineAuthClient(new HttpClient(_server.GetTestServer().CreateHandler()), new Uri("http://localhost/wrong/"));
        var result = await new SpineAuthFlows(client, new SpineAuthSession(client, _storage, _clientTime, NullLogger.Instance)).PasswordAsync(Email, Password, default);

        Assert.Equal(SignInStatus.Failed, result.Status);
        Assert.Contains("/wrong/password/signin", result.Error);
        Assert.Contains("404", result.Error);
    }

    [Fact]
    public async Task Deleting_the_account_signs_out()
    {
        var (session, flows) = await SignedUpAsync();

        Assert.Equal(SignInStatus.Deleted, (await flows.DeleteAccountAsync(default)).Status);
        Assert.Null(session.User);
        Assert.Equal(SignInStatus.Failed, (await NewApp().Flows.PasswordAsync(Email, Password, default)).Status);
    }

    [Fact]
    public async Task A_stored_session_that_cannot_be_read_starts_signed_out()
    {
        await _storage.SaveAsync("{ not json");
        var (session, _) = NewApp();

        await session.LoadAsync();

        Assert.Null(session.User);
        Assert.Null(_storage.Value);
    }

    private sealed class FailingStorage : ISpineAuthStorage
    {
        public Task<string?> LoadAsync() => throw new InvalidOperationException("The Keychain is locked.");
        public Task SaveAsync(string value) => Task.CompletedTask;
        public Task ClearAsync() => throw new InvalidOperationException("Must not clear.");
    }

    /// <summary>The token endpoint out of reach, as on a phone without network or with the server down for a moment.</summary>
    private sealed class Outage(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath == "/auth/token"
                ? throw new HttpRequestException("No route to host.")
                : base.SendAsync(request, cancellationToken);
    }

    /// <summary>Answers 401 to the first request, as a server does when it no longer takes an access token.</summary>
    private sealed class RefuseFirst(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/me" && ++Calls == 1) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
