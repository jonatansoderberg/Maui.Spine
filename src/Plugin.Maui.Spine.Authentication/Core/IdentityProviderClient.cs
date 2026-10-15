using System.Net;
using System.Text;
using System.Text.Json;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>An OpenID Connect provider the app signs in with directly, without a Spine server (a public client with PKCE).</summary>
public sealed class SpineIdentityProviderOptions
{
    /// <summary>The issuer, such as <c>https://example.eu.auth0.com/</c>; its <c>/.well-known/openid-configuration</c> gives the endpoints.</summary>
    public Uri? Authority { get; set; }

    /// <summary>The app's client id at the provider: a native or public client, without a secret.</summary>
    public string? ClientId { get; set; }

    /// <summary>The scopes asked for. <c>offline_access</c> brings a refresh token, so the user stays signed in.</summary>
    public string Scopes { get; set; } = "openid profile email offline_access";

    /// <summary>The name <see cref="SpineUser.Provider"/> carries, such as <c>auth0</c>.</summary>
    public string Name { get; set; } = "oidc";

    /// <summary>More parameters on the authorize request, such as Auth0's <c>audience</c> for an API's access token.</summary>
    public IDictionary<string, string> AuthorizeParameters { get; } = new Dictionary<string, string>();

    /// <summary>https, or http on this machine's loopback for a provider run during development.</summary>
    internal static bool IsSecure(Uri uri) => uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
}

/// <summary>The provider's discovery document: the parts the app uses.</summary>
internal sealed record ProviderEndpoints(string Issuer, Uri Authorize, Uri Token, Uri? Revocation);

/// <summary>Signs in at an OpenID Connect provider with PKCE and trades its refresh tokens; the session's <see cref="ITokenEndpoint"/> in the mode without a Spine server.</summary>
internal sealed class IdentityProviderClient(HttpClient http, SpineIdentityProviderOptions options, TimeProvider time) : ITokenEndpoint
{
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);

    private readonly Lock _gate = new();
    private Task<ProviderEndpoints>? _endpoints;

    /// <summary>Opens the provider's sign-in in the browser and trades the code it returns.</summary>
    /// <returns>The tokens, or why there are none.</returns>
    public async Task<(SpineTokens? Tokens, SignInResult? Problem)> SignInAsync(IBrowserSignIn browser, CancellationToken cancellationToken)
    {
        ProviderEndpoints endpoints;
        try
        {
            endpoints = await EndpointsAsync(cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
                                      || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (null, SignInResult.Failure($"The discovery document of {options.Authority} could not be read: {e.Message}"));
        }

        var pkce = Pkce.Create();
        var state = Pkce.Random();
        var nonce = Pkce.Random();

        var answer = await browser.AuthenticateAsync(callback =>
        {
            var parameters = new Dictionary<string, string>(options.AuthorizeParameters)
            {
                ["client_id"] = options.ClientId!,
                ["redirect_uri"] = callback.AbsoluteUri,
                ["response_type"] = "code",
                ["scope"] = options.Scopes,
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = pkce.Challenge,
                ["code_challenge_method"] = "S256",
            };
            return new Uri($"{endpoints.Authorize.AbsoluteUri}{(endpoints.Authorize.Query.Length > 0 ? "&" : "?")}{Pkce.Query(parameters)}");
        }, cancellationToken);

        if (SpineAuthFlows.BrowserProblem(answer, state) is { } problem) return (null, problem);

        var exchanged = await TokenAsync(endpoints, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = answer!.Values["code"],
            ["redirect_uri"] = answer.Callback.AbsoluteUri,
            ["client_id"] = options.ClientId!,
            ["code_verifier"] = pkce.Verifier,
        }, previousRefresh: null, user: null, nonce, cancellationToken);

        return exchanged.Tokens is { } tokens ? (tokens, null) : (null, SignInResult.Failure(exchanged.Error ?? "The provider refused the code."));
    }

    public async Task<ServerAnswer> RefreshAsync(string refreshToken, SpineUser? user, CancellationToken cancellationToken)
    {
        ProviderEndpoints endpoints;
        try
        {
            endpoints = await EndpointsAsync(cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
                                      || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ServerAnswer(null, 0, $"The discovery document of {options.Authority} could not be read: {e.Message}");
        }

        return await TokenAsync(endpoints, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = options.ClientId!,
        }, refreshToken, user, nonce: null, cancellationToken);
    }

    public async Task<ServerAnswer> RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        ProviderEndpoints endpoints;
        try
        {
            endpoints = await EndpointsAsync(cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
                                      || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ServerAnswer(null, 0, e.Message);
        }

        // A provider without RFC 7009 revocation has nothing to call; the token simply stays unused until it expires.
        if (endpoints.Revocation is not { } revocation) return new ServerAnswer(null, 200, null);

        try
        {
            using var response = await http.PostAsync(revocation, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = refreshToken,
                ["token_type_hint"] = "refresh_token",
                ["client_id"] = options.ClientId!,
            }), cancellationToken);
            return new ServerAnswer(null, (int)response.StatusCode, response.IsSuccessStatusCode ? null : $"POST {revocation} answered {(int)response.StatusCode}.");
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ServerAnswer(null, 0, $"POST {revocation}: {e.Message}");
        }
    }

    internal Task<ProviderEndpoints> EndpointsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            // A failed read is not kept: the next sign-in tries again.
            if (_endpoints is { IsFaulted: true } or { IsCanceled: true }) _endpoints = null;
            return _endpoints ??= DiscoverAsync(cancellationToken);
        }
    }

    private async Task<ProviderEndpoints> DiscoverAsync(CancellationToken cancellationToken)
    {
        var authority = options.Authority!.AbsoluteUri.TrimEnd('/');
        using var response = await http.GetAsync($"{authority}/.well-known/openid-configuration", cancellationToken);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        string Text(string name) => root.GetProperty(name).GetString() ?? throw new InvalidOperationException($"The discovery document has no {name}.");

        var endpoints = new ProviderEndpoints(
            Text("issuer"),
            new Uri(Text("authorization_endpoint")),
            new Uri(Text("token_endpoint")),
            root.TryGetProperty("revocation_endpoint", out var revocation) && revocation.GetString() is { Length: > 0 } r ? new Uri(r) : null);

        // The ID token's signature is not checked because it comes from the token endpoint over TLS; over plain http
        // anyone on the network could hand the app a user of their choosing.
        if (!SpineIdentityProviderOptions.IsSecure(endpoints.Token))
            throw new InvalidOperationException($"The token endpoint {endpoints.Token} is not https.");
        if (endpoints.Issuer.TrimEnd('/') != authority)
            throw new InvalidOperationException($"The discovery document's issuer {endpoints.Issuer} is not the Authority {options.Authority}.");

        return endpoints;
    }

    private async Task<ServerAnswer> TokenAsync(
        ProviderEndpoints endpoints, Dictionary<string, string> form, string? previousRefresh, SpineUser? user, string? nonce, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(endpoints.Token, new FormUrlEncodedContent(form), cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ServerAnswer(null, 0, $"POST {endpoints.Token}: {e.Message}");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonElement root;
            try
            {
                using var json = JsonDocument.Parse(text);
                root = json.RootElement.Clone();
            }
            catch (JsonException)
            {
                return new ServerAnswer(null, (int)response.StatusCode, $"POST {endpoints.Token} answered {(int)response.StatusCode} without JSON.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;

                // invalid_grant on a refresh is the provider's "this sign-in is over" (RFC 6749 §5.2), the session's 401.
                var status = error == "invalid_grant" ? (int)HttpStatusCode.Unauthorized : (int)response.StatusCode;
                return new ServerAnswer(null, status, error ?? $"POST {endpoints.Token} answered {(int)response.StatusCode}.");
            }

            if (!root.TryGetProperty("access_token", out var access) || access.GetString() is not { Length: > 0 } accessToken)
                return new ServerAnswer(null, 502, "The provider's answer has no access_token.");

            var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? seconds : 3600;
            var refresh = root.TryGetProperty("refresh_token", out var r) && r.GetString() is { Length: > 0 } newRefresh ? newRefresh : previousRefresh;

            if (root.TryGetProperty("id_token", out var id) && id.GetString() is { Length: > 0 } idToken)
            {
                if (UserFrom(idToken, endpoints.Issuer, nonce, out var problem) is not { } fromToken)
                    return new ServerAnswer(null, 502, problem);
                user = fromToken;
            }

            if (user is null) return new ServerAnswer(null, 502, "The provider's answer has no id_token; ask for the openid scope.");
            if (refresh is null) return new ServerAnswer(null, 502, "The provider's answer has no refresh_token; ask for the offline_access scope.");

            return new ServerAnswer(new SpineTokens(accessToken, time.GetUtcNow().AddSeconds(expiresIn), refresh, user), 200, null);
        }
    }

    /// <summary>
    /// The user an ID token names. It came straight from the token endpoint over TLS, so its signature is not checked
    /// here (OIDC Core §3.1.3.7); its issuer, audience, expiry and nonce are.
    /// </summary>
    private SpineUser? UserFrom(string idToken, string issuer, string? nonce, out string? problem)
    {
        problem = null;
        var parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            problem = "The id_token is not a JWT.";
            return null;
        }

        JsonElement claims;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            claims = json.RootElement.Clone();
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            problem = "The id_token's claims are not readable.";
            return null;
        }

        string? Claim(string name) => claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        var audiences = claims.TryGetProperty("aud", out var aud)
            ? aud.ValueKind == JsonValueKind.Array
                ? aud.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()).ToList()
                : aud.ValueKind == JsonValueKind.String ? [aud.GetString()] : []
            : [];

        if (Claim("iss") != issuer) problem = "The id_token is from another issuer.";
        else if (!audiences.Contains(options.ClientId)) problem = "The id_token is for another client.";
        else if (!claims.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expSeconds)
                 || DateTimeOffset.FromUnixTimeSeconds(expSeconds) < time.GetUtcNow() - Skew) problem = "The id_token has expired.";
        else if (nonce is not null && Claim("nonce") != nonce) problem = "The id_token's nonce is not this sign-in's.";
        else if (Claim("sub") is not { Length: > 0 }) problem = "The id_token has no sub.";

        return problem is null
            ? new SpineUser(Claim("sub")!, Claim("email"), Claim("name") ?? Claim("preferred_username"), options.Name)
            : null;
    }
}
