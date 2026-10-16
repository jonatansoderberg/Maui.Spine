using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Authentication;

internal sealed class SpineAuth : ISpineAuth
{
    private readonly SpineAuthenticationOptions _options;
    private readonly SpineAuthSession _session;
    private readonly IBrowserSignIn _browser;
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Lazy<INativeSignIn>> _natives;
    private Task<SignInResult>? _signingIn;
    private Task _forgetting = Task.CompletedTask;

    public SpineAuth(
        SpineAuthenticationOptions options, SpineAuthSession session, IBrowserSignIn browser, IServiceProvider services, ILogger<SpineAuth> logger)
    {
        _options = options;
        _session = session;
        _browser = browser;
        _services = services;
        _logger = logger;

        session.SignedIn += (_, user) => SignedIn?.Invoke(this, user);
        session.SignedOut += (_, e) =>
        {
            SignedOut?.Invoke(this, e);
            _forgetting = ForgetNativeAccountsAsync();
            try
            {
                Pictures.Clear();
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Spine sign-in: the signed-out user's picture could not be removed.");
            }
        };
        session.Changed += (_, e) => UserChanged?.Invoke(this, e);

        var providers = new List<SignInProvider>();
        if (options.Direct is { } direct) providers.Add(new SignInProvider(direct.Name, !direct.UsesBrowser));
        if (options.Apple) providers.Add(new SignInProvider("apple", AppleSignIn.IsSupported));
        if (options.EmailPassword is not null) providers.Add(new SignInProvider("password", false));
        providers.AddRange(options.BrowserProviders.Select(p => p with
        {
            IsNative = options.NativeProviders.Any(n => n.Name == p.Name && n.IsSupported && !n.UsesBrowser),
        }));
        Providers = providers;

        _natives = options.NativeProviders.Where(p => p.IsSupported)
            .ToDictionary(p => p.Name, p => new Lazy<INativeSignIn>(() => p.Create(services)));
    }

    // Only in the mode with a Spine server: resolved when used, so the mode without one never builds a client for it.
    private SpineAuthFlows Flows => _services.GetRequiredService<SpineAuthFlows>();

    private SpineAuthClient Client => _services.GetRequiredService<SpineAuthClient>();

    private PictureCache Pictures => _services.GetRequiredService<PictureCache>();

    private bool Direct => _options.Direct is not null;

    private Task<SignInResult> NeedsServer(string what) =>
        Task.FromResult(SignInResult.Failure($"{what} needs a Spine server; this app signs in with {_options.Direct?.Method}."));

    public SpineUser? User => _session.User;

    public IReadOnlyList<SignInProvider> Providers { get; }

    public event EventHandler<SpineUser>? SignedIn;
    public event EventHandler? SignedOut;
    public event EventHandler? UserChanged;

    public async Task<SignInResult> SignInAsync(CancellationToken cancellationToken = default)
    {
        await _session.LoadAsync();
        if (User is { } user) return SignInResult.Success(user);

        // Two guards (a tab and a command) asking at once share one sheet. It runs on its own: a caller that gives up
        // stops waiting for it without closing it on the other.
        Task<SignInResult> signingIn;
        lock (_gate)
        {
            signingIn = _signingIn ??= SignInOnceAsync(CancellationToken.None);
        }

        try
        {
            return await signingIn.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (_gate)
            {
                if (_signingIn == signingIn) _signingIn = null;
            }
        }
    }

    public Task<SignInResult> SignInAsync(string provider, CancellationToken cancellationToken = default)
    {
        var name = provider.ToLowerInvariant();

        if (_options.Direct is { } direct && string.Equals(name, direct.Name, StringComparison.OrdinalIgnoreCase))
            return DirectAsync(cancellationToken);

        return name == "password" && _options.EmailPassword is not null ? OpenSheetAsync() : WithAsync(name, null, cancellationToken);
    }

    public async Task<SignInResult> LinkAsync(string provider, CancellationToken cancellationToken = default)
    {
        if (Direct) return await NeedsServer("Linking accounts");

        var name = provider.ToLowerInvariant();
        if (name == "password") return SignInResult.Failure("A password is added by signing up with the account's email, not linked.");

        var (token, problem) = await Flows.LinkingAsAsync(cancellationToken);
        return problem ?? await WithAsync(name, token, cancellationToken);
    }

    public Task<SignInResult> UnlinkAsync(string provider, CancellationToken cancellationToken = default) =>
        Direct ? NeedsServer("Linking accounts") : Flows.UnlinkAsync(provider.ToLowerInvariant(), cancellationToken);

    public Task<SignInResult> CreateAccountAsync(SpinePendingIdentity pending, CancellationToken cancellationToken = default) =>
        Direct ? NeedsServer("Linking accounts") : Flows.CreatePendingAsync(pending, cancellationToken);

    public Task<SignInResult> LinkPendingAsync(SpinePendingIdentity pending, CancellationToken cancellationToken = default) =>
        Direct ? NeedsServer("Linking accounts") : Flows.LinkPendingAsync(pending, cancellationToken);

    // A provider's sheet or the browser; with link (the user's access token) the provider joins that user.
    private Task<SignInResult> WithAsync(string name, string? link, CancellationToken cancellationToken) => name switch
    {
        "apple" when _options.Apple => AppleSignIn.IsSupported ? AppleAsync(cancellationToken, link) : BrowserAsync("apple", cancellationToken, link),
        _ when _natives.TryGetValue(name, out var native) => NativeAsync(name, native.Value, cancellationToken, link),
        _ when _options.BrowserProviders.Any(p => p.Name == name) => BrowserAsync(name, cancellationToken, link),
        _ => Task.FromResult(SignInResult.Failure($"'{name}' is not a way this app signs in; add it in o.AddAuthentication(...).")),
    };

    public Task<SignInResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
        Direct ? NeedsServer("Email and password") : Flows.PasswordAsync(email, password, cancellationToken);

    public Task<SignInResult> SignUpAsync(string email, string password, string? displayName = null, CancellationToken cancellationToken = default) =>
        Direct ? NeedsServer("Email and password") : Flows.SignUpAsync(email, password, displayName, cancellationToken);

    public Task<SignInResult> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default) =>
        Direct ? NeedsServer("Email and password") : Flows.VerifyAsync(email, code, cancellationToken);

    public Task<SignInResult> ResendCodeAsync(string email, CancellationToken cancellationToken = default) =>
        Direct ? NeedsServer("Email and password") : Flows.ResendAsync(email, cancellationToken);

    public Task<SignInResult> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default) =>
        Direct ? NeedsServer("Email and password") : Flows.ForgotAsync(email, cancellationToken);

    public Task<SignInResult> ResetPasswordAsync(string email, string code, string newPassword, CancellationToken cancellationToken = default) =>
        Direct ? NeedsServer("Email and password") : Flows.ResetAsync(email, code, newPassword, cancellationToken);

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => _session.GetAccessTokenAsync(cancellationToken);

    public DelegatingHandler CreateHandler(HttpMessageHandler? inner = null) =>
        new SpineAuthHandler(_session) { InnerHandler = inner ?? new HttpClientHandler() };

    public async Task<byte[]?> GetPictureAsync(CancellationToken cancellationToken = default)
    {
        await _session.LoadAsync();
        if (User is not { } user) return null;

        Func<CancellationToken, Task<byte[]?>>? source =
            Direct ? _services.GetRequiredService<IDirectSignIn>().PictureAsync
            : _natives.TryGetValue(user.Provider, out var native) ? native.Value.PictureAsync
            : null;
        return await Pictures.GetAsync(user, source, cancellationToken);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _session.SignOutAsync(cancellationToken);
        await _forgetting;
    }

    public Task<SignInResult> DeleteAccountAsync(CancellationToken cancellationToken = default) =>
        Direct ? NeedsServer("Deleting the account") : Flows.DeleteAccountAsync(cancellationToken);

    private Task<SignInResult> SignInOnceAsync(CancellationToken cancellationToken)
    {
        if (Direct) return DirectAsync(cancellationToken);

        // One way to sign in, with a sheet of its own (Apple's) or the browser: no Spine sheet in front of it.
        var ways = (_options.Apple ? 1 : 0) + (_options.EmailPassword is null ? 0 : 1) + _options.BrowserProviders.Count;
        if (ways == 1 && _options.Apple) return AppleSignIn.IsSupported ? AppleAsync(cancellationToken) : BrowserAsync("apple", cancellationToken);
        if (ways == 1 && _options.BrowserProviders.Count == 1) return SignInAsync(_options.BrowserProviders[0].Name, cancellationToken);
        return OpenSheetAsync();
    }

    private async Task<SignInResult> BrowserAsync(string provider, CancellationToken cancellationToken, string? link = null)
    {
        try
        {
            var result = await Flows.BrowserAsync(provider, _browser, cancellationToken, link);
            if (result.Status == SignInStatus.Failed) _logger.LogWarning("Spine sign-in: the browser sign-in with {Provider} failed: {Error}", provider, result.Error);
            return result;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Spine sign-in: the browser sign-in with {Provider} could not start.", provider);
            return SignInResult.Failure(e.Message);
        }
    }

    private async Task<SignInResult> NativeAsync(string provider, INativeSignIn native, CancellationToken cancellationToken, string? link = null)
    {
        // A sign-in straight after a sign-out must not find the old account still remembered.
        await _forgetting;
        try
        {
            var result = await Flows.NativeAsync(provider, native, cancellationToken, link);
            if (result.Status == SignInStatus.Failed) _logger.LogWarning("Spine sign-in: the sign-in with {Provider} failed: {Error}", provider, result.Error);
            return result;
        }
        catch (PlatformNotSupportedException e)
        {
            _logger.LogInformation("Spine sign-in: {Provider}'s own sheet is not available here ({Reason}); signing in through the browser.", provider, e.Message);
            return await BrowserAsync(provider, cancellationToken, link);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Spine sign-in: the sign-in with {Provider} failed.", provider);
            return SignInResult.Failure(e.Message);
        }
    }

    // A provider that remembers who signed in (Credential Manager's auto-select) would sign the same account straight
    // back in; a sign-out here is a sign-out there.
    private async Task ForgetNativeAccountsAsync()
    {
        foreach (var (name, native) in _natives)
        {
            try
            {
                await native.Value.SignedOutAsync();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Spine sign-in: {Provider} could not forget the signed-out account.", name);
            }
        }
    }

    private async Task<SignInResult> DirectAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (tokens, problem) = await _services.GetRequiredService<IDirectSignIn>().SignInAsync(cancellationToken);
            if (tokens is null)
            {
                if (problem!.Status == SignInStatus.Failed) _logger.LogWarning("Spine sign-in: the identity provider's sign-in failed: {Error}", problem.Error);
                return problem;
            }

            return SignInResult.Success(await _session.AcceptAsync(tokens));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Spine sign-in: the identity provider's sign-in could not start.");
            return SignInResult.Failure(e.Message);
        }
    }

    internal async Task<SignInResult> AppleAsync(CancellationToken cancellationToken, string? link = null)
    {
        if (!AppleSignIn.IsSupported) return await BrowserAsync("apple", cancellationToken, link);

        if (await Client.NonceAsync(cancellationToken) is not { } nonce)
            return SignInResult.Failure($"GET {new Uri(Client.Root, "nonce")} did not answer with a nonce.");

        AppleCredential? credential;
        try
        {
            credential = await AppleSignIn.RequestAsync(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nonce.Nonce))), cancellationToken);
        }
        catch (InvalidOperationException e)
        {
            _logger.LogWarning(e, "Spine sign-in: Sign in with Apple failed.");
            return SignInResult.Failure(e.Message);
        }

        if (credential is null) return SignInResult.Canceled;

        var result = await Flows.IdTokenAsync("apple", new SpineIdTokenRequest(credential.IdentityToken, nonce.Nonce, credential.AuthorizationCode, credential.Name, credential.GivenName, credential.FamilyName), cancellationToken, link);
        if (result.Status == SignInStatus.Failed) _logger.LogWarning("Spine sign-in: the server refused Apple's token: {Error}", result.Error);
        return result;
    }

    private async Task<SignInResult> OpenSheetAsync()
    {
        var navigation = _services.GetRequiredService<INavigationService>();
        var page = _options.SignInPageType ?? typeof(SignInSheet);

        // NavigateToWithResultAsync is generic in the page; an app's own sheet is only known as a Type here.
        var open = typeof(INavigationService).GetMethods()
            .Single(m => m.Name == nameof(INavigationService.NavigateToWithResultAsync) && m.GetGenericArguments().Length == 2)
            .MakeGenericMethod(page, typeof(SignInResult));

        var outcome = await (Task<NavigationResult<SignInResult>>)open.Invoke(navigation, null)!;
        return outcome is { IsSuccess: true, Value: { } result } ? result : SignInResult.Canceled;
    }
}
