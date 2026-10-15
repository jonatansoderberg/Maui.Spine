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
    private readonly SpineAuthFlows _flows;
    private readonly SpineAuthClient _client;
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private Task<SignInResult>? _signingIn;

    public SpineAuth(
        SpineAuthenticationOptions options, SpineAuthSession session, SpineAuthFlows flows, SpineAuthClient client,
        IServiceProvider services, ILogger<SpineAuth> logger)
    {
        _options = options;
        _session = session;
        _flows = flows;
        _client = client;
        _services = services;
        _logger = logger;

        session.SignedIn += (_, user) => SignedIn?.Invoke(this, user);
        session.SignedOut += (_, e) => SignedOut?.Invoke(this, e);
        session.Changed += (_, e) => UserChanged?.Invoke(this, e);

        var providers = new List<SignInProvider>();
        if (options.Apple) providers.Add(new SignInProvider("apple", AppleSignIn.IsSupported));
        if (options.EmailPassword is not null) providers.Add(new SignInProvider("password", false));
        Providers = providers;
    }

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

    public Task<SignInResult> SignInAsync(string provider, CancellationToken cancellationToken = default) => provider.ToLowerInvariant() switch
    {
        "apple" when _options.Apple => AppleAsync(cancellationToken),
        "password" when _options.EmailPassword is not null => OpenSheetAsync(),
        _ => Task.FromResult(SignInResult.Failure($"'{provider}' is not a way this app signs in; add it in o.AddAuthentication(...).")),
    };

    public Task<SignInResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
        _flows.PasswordAsync(email, password, cancellationToken);

    public Task<SignInResult> SignUpAsync(string email, string password, string? displayName = null, CancellationToken cancellationToken = default) =>
        _flows.SignUpAsync(email, password, displayName, cancellationToken);

    public Task<SignInResult> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default) =>
        _flows.VerifyAsync(email, code, cancellationToken);

    public Task<SignInResult> ResendCodeAsync(string email, CancellationToken cancellationToken = default) =>
        _flows.ResendAsync(email, cancellationToken);

    public Task<SignInResult> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default) =>
        _flows.ForgotAsync(email, cancellationToken);

    public Task<SignInResult> ResetPasswordAsync(string email, string code, string newPassword, CancellationToken cancellationToken = default) =>
        _flows.ResetAsync(email, code, newPassword, cancellationToken);

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => _session.GetAccessTokenAsync(cancellationToken);

    public DelegatingHandler CreateHandler(HttpMessageHandler? inner = null) =>
        new SpineAuthHandler(_session) { InnerHandler = inner ?? new HttpClientHandler() };

    public Task SignOutAsync(CancellationToken cancellationToken = default) => _session.SignOutAsync(cancellationToken);

    public Task<SignInResult> DeleteAccountAsync(CancellationToken cancellationToken = default) => _flows.DeleteAccountAsync(cancellationToken);

    private Task<SignInResult> SignInOnceAsync(CancellationToken cancellationToken) =>
        _options.Apple && _options.EmailPassword is null && AppleSignIn.IsSupported ? AppleAsync(cancellationToken) : OpenSheetAsync();

    internal async Task<SignInResult> AppleAsync(CancellationToken cancellationToken)
    {
        if (!AppleSignIn.IsSupported)
            return SignInResult.Failure("Sign in with Apple has no native sheet on this platform; the browser sign-in comes with #557.");

        if (await _client.NonceAsync(cancellationToken) is not { } nonce)
            return SignInResult.Failure($"GET {new Uri(_client.Root, "nonce")} did not answer with a nonce.");

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

        var result = await _flows.IdTokenAsync("apple", new SpineIdTokenRequest(credential.IdentityToken, nonce.Nonce, credential.AuthorizationCode, credential.Name), cancellationToken);
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
