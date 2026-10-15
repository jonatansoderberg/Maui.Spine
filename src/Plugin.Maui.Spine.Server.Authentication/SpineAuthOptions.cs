using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Plugin.Maui.Spine.Server.Authentication;

/// <summary>Names shared by the package and the app.</summary>
public static class SpineAuthDefaults
{
    /// <summary>The JwtBearer scheme that accepts Spine's access tokens. <c>AddSpineAuthentication</c> makes it the default.</summary>
    public const string AuthenticationScheme = "Spine";

    /// <summary>The claim that names how the user signed in: <c>apple</c>, <c>google</c>, <c>password</c>, …</summary>
    public const string ProviderClaim = "provider";

    /// <summary>The provider name of email and password sign-in.</summary>
    public const string PasswordProvider = "password";
}

/// <summary>Everything about sign-in on the server: the tokens, the providers and the stores.</summary>
public sealed partial class SpineAuthOptions
{
    internal Dictionary<string, SpineIdTokenProviderOptions> Providers { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Func<IServiceProvider, ISpineAuthStore>? StoreFactory { get; private set; }
    internal Func<IServiceProvider, IUserResolver>? UsersFactory { get; private set; }
    internal SpineEmailPasswordOptions? EmailPasswordOptions { get; private set; }
    private bool _ownUsers;

    /// <summary>The <c>iss</c> of Spine's access tokens, usually the server's base URL.</summary>
    public string? Issuer { get; set; }

    /// <summary>The <c>aud</c> of Spine's access tokens. Unset, the same as <see cref="Issuer"/>.</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// The private key that signs access tokens: an EC P-256 key as PKCS#8 PEM (<c>-----BEGIN PRIVATE KEY-----</c>).
    /// Keep it in user secrets or Key Vault. <c>openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt</c> makes one.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Public keys of earlier signing keys, as PEM, still accepted and published while the tokens they signed live.
    /// Rotate by moving the old key's public half here and putting a new <see cref="SigningKey"/> in place.
    /// </summary>
    public IList<string> PreviousSigningKeys { get; } = [];

    /// <summary>How long an access token works. 15 minutes by default.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a refresh token works unused. Each refresh starts it again. 60 days by default.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(60);

    /// <summary>How long a nonce from <c>GET /auth/nonce</c> can be used. 10 minutes by default.</summary>
    public TimeSpan NonceLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Signs a user in to an existing account with the same email when a provider vouches for the email
    /// (<c>email_verified</c>). Off by default: whoever controls the provider account then controls the app account.
    /// </summary>
    public bool LinkByVerifiedEmail { get; set; }

    /// <summary>
    /// Runs before an account is deleted, so the app can delete its own data for the user. An exception here stops
    /// the deletion and answers 500.
    /// </summary>
    public Func<string, IServiceProvider, CancellationToken, Task>? OnAccountDeleting { get; set; }

    /// <summary>Sign in with Apple from the native sheet on iOS and Mac Catalyst.</summary>
    /// <param name="configure">Sets the bundle ids and, for revocation on account deletion, the key.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthOptions Apple(Action<SpineAppleOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var apple = new SpineAppleOptions();
        configure(apple);
        Providers["apple"] = apple;
        return this;
    }

    /// <summary>Google ID tokens, from Credential Manager on Android.</summary>
    /// <param name="configure">Sets the client ids the tokens are issued to.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthOptions Google(Action<SpineIdTokenProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var google = new SpineIdTokenProviderOptions
        {
            Authority = "https://accounts.google.com",
            ValidIssuers = { "https://accounts.google.com", "accounts.google.com" },
            TrustEmail = true,
        };
        configure(google);
        Providers["google"] = google;
        return this;
    }

    /// <summary>Microsoft ID tokens, from MSAL.</summary>
    /// <param name="configure">Sets the client id and the tenant.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <remarks>MSAL picks its own nonce, so a Microsoft token is accepted without one unless <see cref="SpineIdTokenProviderOptions.RequireNonce"/> is set.</remarks>
    public SpineAuthOptions Microsoft(Action<SpineMicrosoftOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var microsoft = new SpineMicrosoftOptions();
        configure(microsoft);
        microsoft.Authority ??= $"https://login.microsoftonline.com/{microsoft.Tenant}/v2.0";
        Providers["microsoft"] = microsoft;
        return this;
    }

    /// <summary>ID tokens from any OpenID Connect provider with discovery.</summary>
    /// <param name="name">The provider's name in the route, <c>POST /auth/{name}/token</c>: lower case letters, digits and dashes.</param>
    /// <param name="configure">Sets the authority and the client ids.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthOptions OpenIdConnect(string name, Action<SpineIdTokenProviderOptions> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (!ProviderName().IsMatch(name) || name == SpineAuthDefaults.PasswordProvider)
            throw new ArgumentException($"'{name}' is not a provider name: use lower case letters, digits and dashes, and not 'password'.", nameof(name));

        var provider = new SpineIdTokenProviderOptions();
        configure(provider);
        Providers[name] = provider;
        return this;
    }

    /// <summary>Email and password, with verification codes and lockout. The app registers an <see cref="ISpineAuthEmailSender"/>.</summary>
    /// <param name="configure">Changes the defaults.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthOptions EmailPassword(Action<SpineEmailPasswordOptions>? configure = null)
    {
        EmailPasswordOptions = new SpineEmailPasswordOptions();
        configure?.Invoke(EmailPasswordOptions);
        return this;
    }

    /// <summary>Keeps users and tokens in the process. For tests and samples: everyone is signed out on restart.</summary>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthOptions UseInMemoryStore()
    {
        var tokens = new InMemorySpineAuthStore();
        var users = new InMemoryUserResolver();
        StoreFactory = _ => tokens;
        if (!_ownUsers) UsersFactory = _ => users;
        return this;
    }

    /// <summary>Keeps users and tokens in Azure Table Storage.</summary>
    /// <param name="connectionString">The storage account; <c>UseDevelopmentStorage=true</c> for Azurite.</param>
    /// <param name="prefix">Prepended to every table name, so several apps can share an account.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthOptions UseAzureTableStore(string connectionString, string prefix = "SpineAuth")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        StoreFactory = _ => new AzureTableSpineAuthStore(connectionString, prefix);
        if (!_ownUsers) UsersFactory = _ => new AzureTableUserResolver(connectionString, prefix);
        return this;
    }

    /// <summary>Keeps tokens in a store of your own.</summary>
    /// <param name="factory">Builds the store from the application's services.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthOptions UseStore(Func<IServiceProvider, ISpineAuthStore> factory)
    {
        StoreFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    /// <summary>Keeps users in the app's own store; tokens stay in the store picked with <c>Use…Store</c>.</summary>
    /// <param name="factory">Builds the resolver from the application's services.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthOptions UseUsers(Func<IServiceProvider, IUserResolver> factory)
    {
        UsersFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        _ownUsers = true;
        return this;
    }

    internal string EffectiveAudience => Audience ?? Issuer!;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
            throw new InvalidOperationException("AddSpineAuthentication: set Issuer, usually the server's base URL.");

        if (string.IsNullOrWhiteSpace(SigningKey))
            throw new InvalidOperationException(
                "AddSpineAuthentication: set SigningKey to an EC P-256 private key in PKCS#8 PEM. " +
                "`openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt` makes one; keep it in user secrets or Key Vault.");

        if (StoreFactory is null || UsersFactory is null)
            throw new InvalidOperationException("AddSpineAuthentication: pick a store with UseInMemoryStore(), UseAzureTableStore(...) or UseStore(...) and UseUsers(...).");

        if (AccessTokenLifetime <= TimeSpan.Zero || RefreshTokenLifetime <= AccessTokenLifetime)
            throw new InvalidOperationException("AddSpineAuthentication: AccessTokenLifetime must be positive and shorter than RefreshTokenLifetime.");

        if (Providers.Count == 0 && EmailPasswordOptions is null)
            throw new InvalidOperationException("AddSpineAuthentication: configure at least one way to sign in: Apple(...), Google(...), Microsoft(...), OpenIdConnect(...) or EmailPassword().");

        foreach (var (name, provider) in Providers)
            provider.Validate(name);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$")]
    private static partial Regex ProviderName();
}

/// <summary>A provider whose app-side SDK hands over an OpenID Connect ID token.</summary>
public class SpineIdTokenProviderOptions
{
    /// <summary>The issuer's base URL. Its <c>/.well-known/openid-configuration</c> gives the signing keys.</summary>
    public string? Authority { get; set; }

    /// <summary>
    /// The client ids tokens may be issued to: the token's <c>aud</c> must be one of them. For Apple, the bundle ids;
    /// for Google, the Android, iOS and web client ids the app uses.
    /// </summary>
    public IList<string> ClientIds { get; } = [];

    /// <summary>The accepted <c>iss</c> values. Unset, the issuer from discovery.</summary>
    public IList<string> ValidIssuers { get; } = [];

    /// <summary>
    /// Requires a nonce from <c>GET /auth/nonce</c> in the token, used once. On by default: without it, a token
    /// someone else obtained could be traded in until it expires.
    /// </summary>
    public bool RequireNonce { get; set; } = true;

    /// <summary>The provider puts the SHA-256 of the nonce, as lower-case hex, in the token (Apple does).</summary>
    public bool HashNonce { get; set; }

    /// <summary>
    /// The provider's <c>email_verified</c> is trusted for <see cref="SpineAuthOptions.LinkByVerifiedEmail"/>.
    /// On for Apple and Google, off for Microsoft and others.
    /// </summary>
    public bool TrustEmail { get; set; }

    internal IConfigurationManager<OpenIdConnectConfiguration>? Configuration { get; set; }

    internal virtual void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(Authority) && Configuration is null)
            throw new InvalidOperationException($"AddSpineAuthentication: provider '{name}' has no Authority.");

        if (ClientIds.Count == 0)
            throw new InvalidOperationException($"AddSpineAuthentication: provider '{name}' has no ClientIds; a token for any app would be accepted.");
    }
}

/// <summary>Sign in with Apple.</summary>
public sealed class SpineAppleOptions : SpineIdTokenProviderOptions
{
    /// <summary>Apple's issuer.</summary>
    public SpineAppleOptions()
    {
        Authority = "https://appleid.apple.com";
        ValidIssuers.Add("https://appleid.apple.com");
        HashNonce = true;
        TrustEmail = true;
    }

    /// <summary>The app bundle ids; the same list as <see cref="SpineIdTokenProviderOptions.ClientIds"/>.</summary>
    public IList<string> BundleIds => ClientIds;

    /// <summary>The Apple developer team id. With <see cref="KeyId"/> and <see cref="PrivateKey"/>, Apple's tokens are kept and revoked when the account is deleted.</summary>
    public string? TeamId { get; set; }

    /// <summary>The id of a Sign in with Apple key from the developer portal.</summary>
    public string? KeyId { get; set; }

    /// <summary>The key's <c>.p8</c> file content, PKCS#8 PEM.</summary>
    public string? PrivateKey { get; set; }

    internal bool CanRevoke => !string.IsNullOrWhiteSpace(TeamId) && !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(PrivateKey);
}

/// <summary>Microsoft accounts and Entra ID.</summary>
public sealed class SpineMicrosoftOptions : SpineIdTokenProviderOptions
{
    internal const string ConsumersTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";

    /// <summary>MSAL picks its own nonce.</summary>
    public SpineMicrosoftOptions() => RequireNonce = false;

    /// <summary><c>common</c> (default), <c>organizations</c>, <c>consumers</c> or a tenant id.</summary>
    public string Tenant { get; set; } = "common";

    /// <summary>The app registration's client id; the same list as <see cref="SpineIdTokenProviderOptions.ClientIds"/>.</summary>
    public string? ClientId
    {
        get => ClientIds.FirstOrDefault();
        set
        {
            ClientIds.Clear();
            if (value is not null) ClientIds.Add(value);
        }
    }

    internal bool AcceptsTenant(string? tenantId) => tenantId is { Length: > 0 } tid && Tenant.ToLowerInvariant() switch
    {
        "common" => true,
        "organizations" => tid != ConsumersTenant,
        "consumers" => tid == ConsumersTenant,
        _ => string.Equals(tid, Tenant, StringComparison.OrdinalIgnoreCase),
    };
}

/// <summary>Email and password sign-in.</summary>
public sealed class SpineEmailPasswordOptions
{
    /// <summary>New accounts can be made with <c>POST /auth/password/signup</c>. On by default.</summary>
    public bool AllowSignUp { get; set; } = true;

    /// <summary>
    /// A new account must enter the code sent to its email before it can sign in. On by default, and then sign-up
    /// answers the same whether or not the email is taken, so it does not reveal who has an account.
    /// </summary>
    public bool RequireVerifiedEmail { get; set; } = true;

    /// <summary>The shortest password accepted. 8 by default.</summary>
    public int MinPasswordLength { get; set; } = 8;

    /// <summary>Wrong passwords in a row before the account is locked. 5 by default.</summary>
    public int MaxFailedSignIns { get; set; } = 5;

    /// <summary>How long a locked account stays locked. 15 minutes by default.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long an emailed code works. 15 minutes by default.</summary>
    public TimeSpan CodeLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
