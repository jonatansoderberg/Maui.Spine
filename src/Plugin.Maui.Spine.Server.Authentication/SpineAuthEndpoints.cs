using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Plugin.Maui.Spine.Server.Authentication;

/// <summary>
/// The sign-in endpoints, in a form both hosts can use: ASP.NET routing calls them through
/// <see cref="SpineAuthEndpointRouteBuilderExtensions.MapSpineAuthentication"/>, and an Azure Functions isolated worker
/// calls them from its own <c>[Function]</c>s. Bodies and answers are the records in <c>Plugin.Maui.Spine.Authentication</c>.
/// </summary>
public static class SpineAuthEndpoints
{
    /// <summary><c>GET /auth/nonce</c>: a one-time nonce for a native sign-in request.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A <see cref="SpineNonce"/>.</returns>
    public static async Task<IResult> NonceAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var nonce = await Service<IdTokenValidator>(request).CreateNonceAsync(cancellationToken);
        return NoStore(request, Results.Json(nonce, SpineAuthJson.Options));
    }

    /// <summary><c>POST /auth/{provider}/token</c>: an ID token from the platform's sign-in sheet, traded for Spine tokens.</summary>
    /// <param name="request">The incoming request; its body is a <see cref="SpineIdTokenRequest"/>.</param>
    /// <param name="provider">The provider's name, from the route.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns><see cref="SpineTokens"/>, or a <see cref="SpineAuthProblem"/>: 404 unknown provider, 401 refused token.</returns>
    public static async Task<IResult> IdTokenAsync(HttpRequest request, string provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);

        if (await BodyAsync(request, SpineAuthJsonTypes.IdToken, cancellationToken) is not { } body) return BadBody();
        return Answer(request, await Service<SpineAccounts>(request).SignInWithIdTokenAsync(provider.ToLowerInvariant(), body, cancellationToken));
    }

    /// <summary>
    /// <c>POST /auth/token</c>: a refresh token traded for a new pair (<see cref="SpineRefreshRequest"/>), or a browser
    /// sign-in's one-time code traded with its PKCE verifier (<see cref="SpineCodeExchange"/>). A refresh token presented
    /// twice revokes its sign-in everywhere.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns><see cref="SpineTokens"/>, or 401 <c>invalid_grant</c>.</returns>
    public static async Task<IResult> RefreshAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        JsonElement json;
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            json = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return BadBody();
        }

        if (json.ValueKind != JsonValueKind.Object) return BadBody();

        if (json.TryGetProperty("code", out _))
        {
            SpineCodeExchange? exchange;
            try
            {
                exchange = json.Deserialize(SpineAuthJsonTypes.CodeExchange);
            }
            catch (JsonException)
            {
                return BadBody();
            }

            return exchange is { Code: not null, CodeVerifier: not null, RedirectUri: not null }
                ? Answer(request, await Service<SpineBroker>(request).RedeemAsync(exchange, cancellationToken))
                : BadBody();
        }

        SpineRefreshRequest? body;
        try
        {
            body = json.Deserialize(SpineAuthJsonTypes.Refresh);
        }
        catch (JsonException)
        {
            return BadBody();
        }

        if (body is not { RefreshToken.Length: > 0 }) return BadBody();

        return await Service<SpineTokenService>(request).RefreshAsync(body.RefreshToken, cancellationToken) is { } tokens
            ? Answer(request, SignInOutcome.Ok(tokens))
            : Answer(request, SignInOutcome.Refused(401, SpineAuthErrors.InvalidGrant));
    }

    /// <summary>
    /// <c>GET /auth/{provider}/start</c>: opens a browser sign-in. The query carries <c>redirect_uri</c> (a custom scheme from
    /// <see cref="SpineAuthOptions.RedirectSchemes"/> or an http loopback address), <c>code_challenge</c> with
    /// <c>code_challenge_method=S256</c>, and the app's <c>state</c>. Answers with a redirect to the provider.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="provider">The provider's name, from the route.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A redirect to the provider, or a <see cref="SpineAuthProblem"/>: 404 for a provider without a browser sign-in, 400 for a bad query.</returns>
    public static Task<IResult> StartAsync(HttpRequest request, string provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);
        return Service<SpineBroker>(request).StartAsync(provider.ToLowerInvariant(), request.Query, cancellationToken);
    }

    /// <summary>
    /// <c>GET</c> or <c>POST /auth/{provider}/callback</c>: where the provider returns. Answers with a redirect to the
    /// app's <c>redirect_uri</c> carrying a one-time <c>code</c> (or an <c>error</c>) and the app's <c>state</c>.
    /// </summary>
    /// <param name="request">The incoming request: a query, or a form for providers that post (Apple).</param>
    /// <param name="provider">The provider's name, from the route.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A redirect to the app, or a plain 400 page when the sign-in expired or was already used.</returns>
    public static Task<IResult> CallbackAsync(HttpRequest request, string provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);
        return Service<SpineBroker>(request).CallbackAsync(provider.ToLowerInvariant(), request, cancellationToken);
    }

    /// <summary><c>POST /auth/revoke</c>: signs out by revoking the refresh token's sign-in. Answers 204 whether or not the token was known.</summary>
    /// <param name="request">The incoming request; its body is a <see cref="SpineRefreshRequest"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>204.</returns>
    public static async Task<IResult> RevokeAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await BodyAsync(request, SpineAuthJsonTypes.Refresh, cancellationToken) is not { RefreshToken.Length: > 0 } body) return BadBody();

        await Service<SpineTokenService>(request).RevokeAsync(body.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// <c>DELETE /auth/account</c>: deletes the signed-in user. Runs <see cref="SpineAuthOptions.OnAccountDeleting"/>,
    /// revokes Apple's tokens, revokes every Spine sign-in and removes the user.
    /// </summary>
    /// <param name="request">The incoming request, with the user's access token.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>204, or 401 without a valid access token.</returns>
    public static async Task<IResult> DeleteAccountAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await request.GetSpineUserIdAsync() is not { } userId) return Results.Unauthorized();

        await Service<SpineAccounts>(request).DeleteAsync(userId, request.HttpContext.RequestServices, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// <c>POST /auth/password/{action}</c>: <c>signup</c>, <c>verify</c>, <c>resend</c>, <c>signin</c>, <c>forgot</c>
    /// or <c>reset</c>, each with its own body (<see cref="SpinePasswordSignUp"/>, <see cref="SpineEmailCode"/>,
    /// <see cref="SpineEmailRequest"/>, <see cref="SpinePasswordSignIn"/>, <see cref="SpineEmailRequest"/>,
    /// <see cref="SpinePasswordReset"/>).
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="action">The step, from the route.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// <see cref="SpineTokens"/> when the user is signed in; 202 when an email was (or may have been) sent; otherwise a
    /// <see cref="SpineAuthProblem"/>. 404 when email and password sign-in is not configured.
    /// </returns>
    public static async Task<IResult> PasswordAsync(HttpRequest request, string action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(action);

        if (Service<SpineAuthOptions>(request).EmailPasswordOptions is null)
            return Answer(request, SignInOutcome.Refused(404, SpineAuthErrors.UnknownProvider));

        var accounts = Service<SpineAccounts>(request);

        SignInOutcome? outcome = action.ToLowerInvariant() switch
        {
            "signup" => await BodyAsync(request, SpineAuthJsonTypes.SignUp, cancellationToken) is { } b ? await accounts.SignUpAsync(b, cancellationToken) : null,
            "verify" => await BodyAsync(request, SpineAuthJsonTypes.Code, cancellationToken) is { } b ? await accounts.VerifyEmailAsync(b, cancellationToken) : null,
            "resend" => await BodyAsync(request, SpineAuthJsonTypes.Email, cancellationToken) is { } b ? await accounts.ResendAsync(b, cancellationToken) : null,
            "signin" => await BodyAsync(request, SpineAuthJsonTypes.SignIn, cancellationToken) is { } b ? await accounts.SignInWithPasswordAsync(b, cancellationToken) : null,
            "forgot" => await BodyAsync(request, SpineAuthJsonTypes.Email, cancellationToken) is { } b ? await accounts.ForgotAsync(b, cancellationToken) : null,
            "reset" => await BodyAsync(request, SpineAuthJsonTypes.Reset, cancellationToken) is { } b ? await accounts.ResetAsync(b, cancellationToken) : null,
            _ => SignInOutcome.Refused(404, SpineAuthErrors.InvalidRequest, $"There is no password step '{action}'."),
        };

        return outcome is null ? BadBody() : Answer(request, outcome);
    }

    /// <summary><c>GET /auth/.well-known/jwks.json</c>: the public keys of Spine's access tokens, for other services that check them.</summary>
    /// <param name="request">The incoming request.</param>
    /// <returns>A JWK set.</returns>
    public static IResult Jwks(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Results.Text(Service<SpineTokenService>(request).Jwks(), "application/json");
    }

    private static T Service<T>(HttpRequest request) where T : notnull => request.HttpContext.RequestServices.GetRequiredService<T>();

    private static async Task<T?> BodyAsync<T>(HttpRequest request, JsonTypeInfo<T> type, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(request.Body, type, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult BadBody() =>
        Results.Json(new SpineAuthProblem(SpineAuthErrors.InvalidRequest, "The body is missing or not the expected JSON."), SpineAuthJson.Options, statusCode: 400);

    private static IResult Answer(HttpRequest request, SignInOutcome outcome)
    {
        if (outcome.Tokens is { } tokens) return NoStore(request, Results.Json(tokens, SpineAuthJson.Options));
        if (outcome.Problem is null) return Results.StatusCode(outcome.Status);

        return Results.Json(outcome.Problem, SpineAuthJson.Options, statusCode: outcome.Status);
    }

    private static IResult NoStore(HttpRequest request, IResult result)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return result;
    }
}

/// <summary>Reads the Spine user behind a request.</summary>
public static class SpineAuthHttpRequestExtensions
{
    /// <summary>
    /// The signed-in user's id (<c>sub</c>) from the request's Spine access token, or <see langword="null"/>. Works with
    /// ASP.NET's authentication middleware and without it, as in Azure Functions. A deleted user's access token keeps
    /// working until it expires (<see cref="SpineAuthOptions.AccessTokenLifetime"/>).
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <returns>The user id, or <see langword="null"/> without a valid Spine access token.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddSpinePushNotifications(o => o.UserId = request => request.GetSpineUserIdAsync());
    /// </code>
    /// </example>
    public static async ValueTask<string?> GetSpineUserIdAsync(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The scheme's handler keeps its result for the request, so this costs nothing after the middleware ran.
        var result = await request.HttpContext.AuthenticateAsync(SpineAuthDefaults.AuthenticationScheme);
        return result.Succeeded && result.Principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value is { Length: > 0 } id ? id : null;
    }
}

/// <summary>Maps the sign-in endpoints onto an ASP.NET Core application.</summary>
public static class SpineAuthEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps every sign-in endpoint under <paramref name="prefix"/>: <c>GET nonce</c>, <c>POST {provider}/token</c>,
    /// <c>GET {provider}/start</c>, <c>GET</c>/<c>POST {provider}/callback</c>,
    /// <c>POST token</c>, <c>POST revoke</c>, <c>DELETE account</c>, <c>POST password/{action}</c> and
    /// <c>GET .well-known/jwks.json</c>.
    /// </summary>
    /// <param name="endpoints">The application's route builder.</param>
    /// <param name="prefix">The path the endpoints live under.</param>
    /// <returns>The group, so the app can add rate limiting: <c>.RequireRateLimiting("auth")</c>.</returns>
    public static RouteGroupBuilder MapSpineAuthentication(this IEndpointRouteBuilder endpoints, string prefix = "/auth")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var group = endpoints.MapGroup(prefix.TrimEnd('/'));
        group.MapGet("/nonce", (HttpRequest request, CancellationToken ct) => SpineAuthEndpoints.NonceAsync(request, ct));
        group.MapPost("/token", (HttpRequest request, CancellationToken ct) => SpineAuthEndpoints.RefreshAsync(request, ct));
        group.MapPost("/revoke", (HttpRequest request, CancellationToken ct) => SpineAuthEndpoints.RevokeAsync(request, ct));
        group.MapDelete("/account", (HttpRequest request, CancellationToken ct) => SpineAuthEndpoints.DeleteAccountAsync(request, ct));
        group.MapPost("/password/{action}", (HttpRequest request, string action, CancellationToken ct) => SpineAuthEndpoints.PasswordAsync(request, action, ct));
        group.MapGet("/{provider}/start", (HttpRequest request, string provider, CancellationToken ct) => SpineAuthEndpoints.StartAsync(request, provider, ct));
        group.MapMethods("/{provider}/callback", ["GET", "POST"], (HttpRequest request, string provider, CancellationToken ct) => SpineAuthEndpoints.CallbackAsync(request, provider, ct));
        group.MapPost("/{provider}/token", (HttpRequest request, string provider, CancellationToken ct) => SpineAuthEndpoints.IdTokenAsync(request, provider, ct));
        group.MapGet("/.well-known/jwks.json", (HttpRequest request) => SpineAuthEndpoints.Jwks(request));

        return group;
    }
}

/// <summary>The contracts' type infos, so the endpoints read bodies without reflection.</summary>
internal static class SpineAuthJsonTypes
{
    public static JsonTypeInfo<SpineIdTokenRequest> IdToken => Info<SpineIdTokenRequest>();
    public static JsonTypeInfo<SpineRefreshRequest> Refresh => Info<SpineRefreshRequest>();
    public static JsonTypeInfo<SpineCodeExchange> CodeExchange => Info<SpineCodeExchange>();
    public static JsonTypeInfo<SpinePasswordSignUp> SignUp => Info<SpinePasswordSignUp>();
    public static JsonTypeInfo<SpinePasswordSignIn> SignIn => Info<SpinePasswordSignIn>();
    public static JsonTypeInfo<SpineEmailCode> Code => Info<SpineEmailCode>();
    public static JsonTypeInfo<SpineEmailRequest> Email => Info<SpineEmailRequest>();
    public static JsonTypeInfo<SpinePasswordReset> Reset => Info<SpinePasswordReset>();

    private static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)SpineAuthJson.Options.GetTypeInfo(typeof(T));
}
