using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Plugin.Maui.Spine.Server.Authentication;

/// <summary>
/// The browser sign-in (RFC 8252 with the server as the confidential client): the app opens <c>/start</c> in the system
/// browser, the provider returns to <c>/callback</c>, and the app gets back a one-time code it trades with PKCE.
/// Every client secret stays on the server.
/// </summary>
internal sealed class SpineBroker(
    SpineAuthOptions options, ISpineAuthStore store, IUserResolver users, IdTokenValidator idTokens, SpineAccounts accounts,
    SpineTokenService tokens, AppleTokenClient? apple, IHttpClientFactory http, TimeProvider time, ILogger logger)
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);
    private const int MaxStateLength = 512;
    private const int MaxRedirectLength = 2048;
    private const int MaxAppleUserLength = 1024;

    // Apple's form holds a code, a state, an ID token and a small user object; nothing bigger is read.
    private static readonly FormOptions CallbackForm = new() { ValueLengthLimit = 16 * 1024, ValueCountLimit = 16, MultipartBodyLengthLimit = 64 * 1024 };

    private sealed record Pending(string Provider, string Redirect, string? AppState, string Challenge, string Nonce, string? Verifier);

    private sealed record Issued(string UserId, string Provider, string Challenge, string Redirect);

    public async Task<IResult> StartAsync(string providerName, IQueryCollection query, CancellationToken cancellationToken)
    {
        if (!options.Providers.TryGetValue(providerName, out var provider) || provider.WebClientId is not { } clientId)
            return Problem(404, SpineAuthErrors.UnknownProvider);

        if (Normalized(query["redirect_uri"].ToString()) is not { } redirect)
        {
            return Problem(400, SpineAuthErrors.InvalidRequest, options.AllowLoopbackRedirects
                ? "redirect_uri must use one of the server's RedirectSchemes or be an http loopback address, without code, state or error in its query."
                : "redirect_uri must use one of the server's RedirectSchemes, without code, state or error in its query.");
        }

        var challenge = query["code_challenge"].ToString();
        if (query["code_challenge_method"] != "S256" || challenge.Length != 43)
            return Problem(400, SpineAuthErrors.InvalidRequest, "code_challenge is required, with code_challenge_method=S256.");

        var appState = query["state"].ToString() is { Length: > 0 } s ? s : null;
        if (appState?.Length > MaxStateLength)
            return Problem(400, SpineAuthErrors.InvalidRequest, $"state is at most {MaxStateLength} characters.");

        string authorize;
        try
        {
            authorize = (await idTokens.ConfigurationAsync(providerName, provider, cancellationToken)).AuthorizationEndpoint;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Spine sign-in: {Provider}'s discovery document could not be read.", providerName);
            return Problem(502, SpineAuthErrors.ProviderUnavailable);
        }

        var state = Random();
        var nonce = Random();
        var verifier = provider.UsePkce ? Random() : null;
        var expiresAt = time.GetUtcNow() + StateLifetime;

        // One write per start: the nonce rides in the state, which is itself taken once.
        await store.SaveOneTimeAsync(StateKey(state),
            JsonSerializer.Serialize(new Pending(providerName, redirect, appState, challenge, nonce, verifier)), expiresAt, cancellationToken);

        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = options.CallbackUrl(providerName).ToString(),
            ["response_type"] = "code",
            ["scope"] = provider.Scopes,
            ["state"] = state,
            ["nonce"] = IdTokenValidator.Sent(provider, nonce),
        };

        if (provider.FormPost) parameters["response_mode"] = "form_post";
        if (verifier is not null)
        {
            parameters["code_challenge"] = Challenge(verifier);
            parameters["code_challenge_method"] = "S256";
        }

        return Results.Redirect(QueryHelpers.AddQueryString(authorize, parameters));
    }

    public async Task<IResult> CallbackAsync(string providerName, HttpRequest request, CancellationToken cancellationToken)
    {
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> answer;
        try
        {
            answer = request.HasFormContentType ? (await request.ReadFormAsync(CallbackForm, cancellationToken)).ToDictionary() : request.Query.ToDictionary();
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return Expired();
        }

        string? Get(string key) => answer.TryGetValue(key, out var value) && value.ToString() is { Length: > 0 } text ? text : null;

        if (Get("state") is not { } state
            || await store.TakeOneTimeAsync(StateKey(state), time.GetUtcNow(), cancellationToken) is not { } saved
            || JsonSerializer.Deserialize<Pending>(saved) is not { } pending
            || pending.Provider != providerName
            || !options.Providers.TryGetValue(providerName, out var provider))
        {
            return Expired();
        }

        if (Get("error") is { } error)
        {
            logger.LogInformation("Spine sign-in: {Provider} answered the browser sign-in with {Error}.", providerName, Printable(error));
            return ToApp(pending, ("error", error is "user_cancelled_authorize" or "access_denied" ? "access_denied" : "server_error"));
        }

        if (Get("code") is not { } code || await ExchangeAsync(providerName, provider, code, pending, cancellationToken) is not { } exchanged)
            return ToApp(pending, ("error", "server_error"));

        var refresh = providerName == "apple" && exchanged.RefreshToken is { } appleRefresh ? (appleRefresh, provider.WebClientId!) : ((string, string)?)null;
        // Apple posts the name once beside the code; it is read only from Apple's own form.
        var name = provider is SpineAppleOptions && request.HasFormContentType ? AppleName(Get("user")) : null;
        var (user, _) = await accounts.SignInUserAsync(
            providerName, new SpineIdTokenRequest(exchanged.IdToken, pending.Nonce, null, name), refresh, brokered: true, cancellationToken);

        if (user is null) return ToApp(pending, ("error", "invalid_token"));

        var appCode = Random();
        await store.SaveOneTimeAsync(CodeKey(appCode),
            JsonSerializer.Serialize(new Issued(user.Id, providerName, pending.Challenge, pending.Redirect)), time.GetUtcNow() + CodeLifetime, cancellationToken);

        return ToApp(pending, ("code", appCode));
    }

    public async Task<SignInOutcome> RedeemAsync(SpineCodeExchange request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Code) || string.IsNullOrEmpty(request.CodeVerifier)
            || await store.TakeOneTimeAsync(CodeKey(request.Code), time.GetUtcNow(), cancellationToken) is not { } saved
            || JsonSerializer.Deserialize<Issued>(saved) is not { } issued
            || issued.Redirect != Normalized(request.RedirectUri)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Challenge(request.CodeVerifier)), Encoding.ASCII.GetBytes(issued.Challenge))
            || await users.FindByIdAsync(issued.UserId, cancellationToken) is not { } user)
        {
            return SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant);
        }

        return SignInOutcome.Ok(await tokens.IssueAsync(user, issued.Provider, cancellationToken: cancellationToken));
    }

    /// <summary>The redirect as it will be stored, compared and sent back, or <see langword="null"/> when it is not allowed.</summary>
    internal string? Normalized(string? redirect)
    {
        if (redirect is not { Length: > 0 and <= MaxRedirectLength } || !Uri.TryCreate(redirect, UriKind.Absolute, out var uri) || uri.Fragment.Length > 0)
            return null;

        // The answer's own parameters are added by the server; a redirect that already carries them would reach the app twice.
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (query.ContainsKey("code") || query.ContainsKey("state") || query.ContainsKey("error")) return null;

        // RFC 8252 §7.3 and §8.3: loopback by IP literal, never by the name "localhost", over http on any port.
        var allowed = uri.Scheme == Uri.UriSchemeHttp
            ? options.AllowLoopbackRedirects && uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
              && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address)
            : options.RedirectSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);

        // AbsoluteUri is what the browser gets back: escaped, so it fits in a Location header whatever was typed.
        return allowed ? uri.AbsoluteUri : null;
    }

    private async Task<(string IdToken, string? RefreshToken)?> ExchangeAsync(
        string providerName, SpineIdTokenProviderOptions provider, string code, Pending pending, CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = (await idTokens.ConfigurationAsync(providerName, provider, cancellationToken)).TokenEndpoint;
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = options.CallbackUrl(providerName).ToString(),
                ["client_id"] = provider.WebClientId!,
                ["client_secret"] = provider is SpineAppleOptions ? apple!.ClientSecret(provider.WebClientId!) : provider.WebClientSecret!,
            };
            if (pending.Verifier is { } verifier) form["code_verifier"] = verifier;

            using var response = await http.CreateClient(IdTokenValidator.HttpClientName).PostAsync(endpoint, new FormUrlEncodedContent(form), cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Spine sign-in: {Provider} refused the browser sign-in's code ({Status}: {Answer}).", providerName, (int)response.StatusCode, text);
                return null;
            }

            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (!root.TryGetProperty("id_token", out var idToken) || idToken.GetString() is not { Length: > 0 } id)
            {
                logger.LogWarning("Spine sign-in: {Provider}'s token answer had no id_token.", providerName);
                return null;
            }

            return (id, root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or CryptographicException
                                      || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "Spine sign-in: {Provider}'s token endpoint could not be used.", providerName);
            return null;
        }
    }

    // Apple posts the name once, as JSON beside the code; it is the user's own input, so only the text is kept.
    private static string? AppleName(string? user)
    {
        if (user is not { Length: <= MaxAppleUserLength }) return null;

        try
        {
            using var json = JsonDocument.Parse(user);
            if (!json.RootElement.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.Object) return null;

            string? Part(string key) => name.TryGetProperty(key, out var part) && part.ValueKind == JsonValueKind.String ? part.GetString() : null;
            var full = string.Join(' ', new[] { Part("firstName"), Part("lastName") }.Where(p => !string.IsNullOrWhiteSpace(p)));
            return full.Length > 0 ? full : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult ToApp(Pending pending, (string Key, string Value) result)
    {
        var parameters = new Dictionary<string, string?> { [result.Key] = result.Value };
        if (pending.AppState is { } state) parameters["state"] = state;
        return Results.Redirect(QueryHelpers.AddQueryString(pending.Redirect, parameters));
    }

    private static IResult Expired() => Results.Content(
        "This sign-in has expired or was already used. Go back to the app and try again.", "text/plain", Encoding.UTF8, StatusCodes.Status400BadRequest);

    private static IResult Problem(int status, string error, string? detail = null) =>
        Results.Json(new SpineAuthProblem(error, detail), SpineAuthJson.Options, statusCode: status);

    private static string Printable(string text) =>
        new(text.Take(64).Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '?').ToArray());

    private static string Random() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    private static string Challenge(string verifier) => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string StateKey(string state) => SpineTokenService.Hash($"broker|{state}");

    private static string CodeKey(string code) => SpineTokenService.Hash($"code|{code}");
}
