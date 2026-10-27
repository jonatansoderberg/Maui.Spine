using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// <c>POST /auth/link/ticket</c>: a one-time ticket (<see cref="SpineLinkTicket"/>) for linking a provider through the
    /// browser. <c>GET /auth/{provider}/start?…&amp;link=&lt;ticket&gt;</c> carries it, and the callback adds the provider to
    /// the signed-in user instead of signing in.
    /// </summary>
    /// <param name="request">The incoming request, with the user's access token.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A <see cref="SpineLinkTicket"/>, or 401 without a valid access token.</returns>
    public static async Task<IResult> LinkTicketAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await CallerAsync(request) is not { } caller) return Results.Unauthorized();
        return NoStore(request, Results.Json(await Service<SpineAccounts>(request).CreateLinkTicketAsync(caller, cancellationToken), SpineAuthJson.Options));
    }

    /// <summary>
    /// <c>POST /auth/link/{provider}</c>: an ID token from the provider's sheet (<see cref="SpineIdTokenRequest"/>) adds the
    /// provider to the signed-in user. Answers with new tokens, whose user lists the logins. <c>POST /auth/link/password</c>
    /// with a <see cref="SpinePasswordSignIn"/> adds email and password instead; an email other than the account's own
    /// replaces it, unverified.
    /// </summary>
    /// <param name="request">The incoming request, with the user's access token.</param>
    /// <param name="provider">The provider's name, from the route; <c>password</c> for email and password.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// <see cref="SpineTokens"/>, or a <see cref="SpineAuthProblem"/>: 401 without a valid access token or with a refused ID
    /// token, 404 unknown provider, 409 <c>identity_in_use</c> when another account signs in with it. For <c>password</c>:
    /// 202 when a code went to the email to verify it, 400 <c>weak_password</c>, 409 <c>email_taken</c> when another account
    /// has the email, 409 when the account has a password already, 501 when the app's user store cannot change an email.
    /// </returns>
    public static async Task<IResult> LinkAsync(HttpRequest request, string provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);

        if (await CallerAsync(request) is not { } caller) return Results.Unauthorized();
        if (provider.Equals(SpineAuthDefaults.PasswordProvider, StringComparison.OrdinalIgnoreCase))
        {
            return await BodyAsync(request, SpineAuthJsonTypes.SignIn, cancellationToken) is { } password
                ? Answer(request, await Service<SpineAccounts>(request).AddPasswordAsync(password, caller, cancellationToken))
                : BadBody();
        }

        if (await BodyAsync(request, SpineAuthJsonTypes.IdToken, cancellationToken) is not { } body) return BadBody();
        return Answer(request, await Service<SpineAccounts>(request).LinkWithIdTokenAsync(provider.ToLowerInvariant(), body, caller, cancellationToken));
    }

    /// <summary><c>DELETE /auth/link/{provider}</c>: removes a provider, or the password, from the signed-in user.</summary>
    /// <param name="request">The incoming request, with the user's access token.</param>
    /// <param name="provider">The provider's name, from the route; <c>password</c> for the password.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// <see cref="SpineTokens"/>, or a <see cref="SpineAuthProblem"/>: 401 without a valid access token, 404 when the user has no
    /// such login, 409 <c>last_login</c> for the last way to sign in, 501 when the app's user store cannot delete logins.
    /// </returns>
    public static async Task<IResult> UnlinkAsync(HttpRequest request, string provider, CancellationToken cancellationToken = default) =>
        await UnlinkAsync(request, provider, null, cancellationToken);

    /// <summary>
    /// <c>DELETE /auth/link/{provider}/{subject}</c>: removes one login of a provider from the signed-in user, such as one
    /// passkey of several (<see cref="SpineLoginInfo.Subject"/>).
    /// </summary>
    /// <param name="request">The incoming request, with the user's access token.</param>
    /// <param name="provider">The provider's name, from the route.</param>
    /// <param name="subject">The login's subject, from the route; <see langword="null"/> removes every login of the provider.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>As <see cref="UnlinkAsync(HttpRequest, string, CancellationToken)"/>.</returns>
    public static async Task<IResult> UnlinkAsync(HttpRequest request, string provider, string? subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);

        if (await CallerAsync(request) is not { } caller) return Results.Unauthorized();
        return Answer(request, await Service<SpineAccounts>(request).UnlinkAsync(provider.ToLowerInvariant(), caller, cancellationToken, subject));
    }

    /// <summary>
    /// <c>POST /auth/pending/{action}</c> with a <see cref="SpinePendingRequest"/>: what becomes of a pending identity, the
    /// 202 answer to a first sign-in when <see cref="SpineAuthOptions.AskBeforeNewAccount"/> is on. <c>create</c> makes a
    /// new account for it; <c>link</c> adds it to the signed-in user, who has just proved an existing account.
    /// </summary>
    /// <param name="request">The incoming request; for <c>link</c>, with the user's access token.</param>
    /// <param name="action">The choice, from the route.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// <see cref="SpineTokens"/>, or a <see cref="SpineAuthProblem"/>: 401 <c>invalid_grant</c> for an unknown, used or expired
    /// ticket, or without an access token for <c>link</c>; 409 <c>identity_in_use</c>.
    /// </returns>
    public static async Task<IResult> PendingAsync(HttpRequest request, string action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(action);

        var accounts = Service<SpineAccounts>(request);
        switch (action.ToLowerInvariant())
        {
            case "create":
                return await BodyAsync(request, SpineAuthJsonTypes.Pending, cancellationToken) is { Ticket.Length: > 0 } create
                    ? Answer(request, await accounts.CreatePendingAsync(create, cancellationToken))
                    : BadBody();
            case "link":
                if (await CallerAsync(request) is not { } caller) return Results.Unauthorized();
                return await BodyAsync(request, SpineAuthJsonTypes.Pending, cancellationToken) is { Ticket.Length: > 0 } link
                    ? Answer(request, await accounts.LinkPendingAsync(link, caller, cancellationToken))
                    : BadBody();
            default:
                return Answer(request, SignInOutcome.Refused(404, SpineAuthErrors.InvalidRequest, $"There is no pending step '{action}'."));
        }
    }

    /// <summary>
    /// <c>POST /auth/passkey/{action}</c>, the passkey ceremonies: <c>register/options</c> then <c>register</c> make a
    /// passkey, for the signed-in user (with an access token) or for a new account (a <see cref="SpinePasskeyNewAccount"/>
    /// body, without one); <c>signin/options</c> then <c>signin</c> sign in with one. The options answer a
    /// <see cref="SpinePasskeyChallenge"/>; the second step takes a <see cref="SpinePasskeyResponse"/>.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="action">The step, from the route.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// A <see cref="SpinePasskeyChallenge"/> or <see cref="SpineTokens"/>, or a <see cref="SpineAuthProblem"/>: 404 when
    /// passkeys are not configured, 403 <c>signup_closed</c>, 401 for an expired ceremony or a refused or unknown passkey.
    /// </returns>
    public static async Task<IResult> PasskeyAsync(HttpRequest request, string action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(action);

        if (Service<SpineAuthOptions>(request).PasskeyOptions is null)
            return Answer(request, SignInOutcome.Refused(404, SpineAuthErrors.UnknownProvider));

        var passkeys = Service<SpinePasskeys>(request);
        switch (action.ToLowerInvariant())
        {
            case "register/options":
                var caller = await CallerAsync(request);
                if (caller is null && request.Headers.Authorization.Count > 0) return Results.Unauthorized();

                var newAccount = caller is null ? await BodyAsync(request, SpineAuthJsonTypes.PasskeyNewAccount, cancellationToken) : null;
                if (caller is null && newAccount is null) return BadBody();

                var (challenge, refusal) = await passkeys.RegisterOptionsAsync(newAccount, caller, cancellationToken);
                return challenge is null ? Answer(request, refusal!) : NoStore(request, Results.Json(challenge, SpineAuthJson.Options));
            case "signin/options":
                return NoStore(request, Results.Json(await passkeys.SignInOptionsAsync(cancellationToken), SpineAuthJson.Options));
            case "register":
                return await BodyAsync(request, SpineAuthJsonTypes.PasskeyResponse, cancellationToken) is { ChallengeId.Length: > 0 } registration
                    ? Answer(request, await passkeys.RegisterAsync(registration, cancellationToken))
                    : BadBody();
            case "signin":
                return await BodyAsync(request, SpineAuthJsonTypes.PasskeyResponse, cancellationToken) is { ChallengeId.Length: > 0 } signIn
                    ? Answer(request, await passkeys.SignInAsync(signIn, cancellationToken))
                    : BadBody();
            default:
                return Answer(request, SignInOutcome.Refused(404, SpineAuthErrors.InvalidRequest, $"There is no passkey step '{action}'."));
        }
    }

    /// <summary>
    /// <c>GET /.well-known/apple-app-site-association</c>: the iOS and macOS apps that may use the domain's passkeys
    /// (<see cref="SpinePasskeyOptions.AppleAppIds"/>, as <c>webcredentials</c>).
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <returns>The association file, or 404 when passkeys are not configured.</returns>
    public static IResult AppleAppSiteAssociation(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Service<SpineAuthOptions>(request).PasskeyOptions is not { } passkeys) return Results.NotFound();

        var file = new JsonObject
        {
            ["webcredentials"] = new JsonObject { ["apps"] = new JsonArray([.. passkeys.AppleAppIds.Select(id => JsonValue.Create(id))]) },
        };
        return Results.Text(file.ToJsonString(), "application/json");
    }

    /// <summary>
    /// <c>GET /.well-known/assetlinks.json</c>: the Android apps that may use the domain's passkeys
    /// (<see cref="SpinePasskeyOptions.AndroidApps"/>, as <c>get_login_creds</c>).
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <returns>The statement list, or 404 when passkeys are not configured.</returns>
    public static IResult AssetLinks(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Service<SpineAuthOptions>(request).PasskeyOptions is not { } passkeys) return Results.NotFound();

        var file = new JsonArray([.. passkeys.AndroidApps.Select(app => (JsonNode)new JsonObject
        {
            ["relation"] = new JsonArray("delegate_permission/common.get_login_creds"),
            ["target"] = new JsonObject
            {
                ["namespace"] = "android_app",
                ["package_name"] = app.PackageName,
                ["sha256_cert_fingerprints"] = new JsonArray([.. app.Sha256Fingerprints.Select(f => JsonValue.Create(f))]),
            },
        })]);
        return Results.Text(file.ToJsonString(), "application/json");
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

    // The signed-in user and the provider their access token was issued for, which the new tokens keep.
    private static async ValueTask<SpineCaller?> CallerAsync(HttpRequest request)
    {
        var result = await request.HttpContext.AuthenticateAsync(SpineAuthDefaults.AuthenticationScheme);
        return result.Succeeded && result.Principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value is { Length: > 0 } id
            ? new SpineCaller(id, result.Principal.FindFirst(SpineAuthDefaults.ProviderClaim)?.Value ?? SpineAuthDefaults.PasswordProvider)
            : null;
    }

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
        if (outcome.PendingIdentity is { } pending) return NoStore(request, Results.Json(pending, SpineAuthJson.Options, statusCode: 202));
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
    /// <c>POST token</c>, <c>POST revoke</c>, <c>DELETE account</c>, <c>POST password/{action}</c>, <c>POST link/ticket</c>,
    /// <c>POST</c>/<c>DELETE link/{provider}</c>, <c>DELETE link/{provider}/{subject}</c>, <c>POST pending/{action}</c>, <c>POST passkey/{**action}</c> and
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
        group.MapPost("/link/ticket", (HttpRequest request, CancellationToken ct) => SpineAuthEndpoints.LinkTicketAsync(request, ct));
        group.MapPost("/link/{provider}", (HttpRequest request, string provider, CancellationToken ct) => SpineAuthEndpoints.LinkAsync(request, provider, ct));
        group.MapDelete("/link/{provider}", (HttpRequest request, string provider, CancellationToken ct) => SpineAuthEndpoints.UnlinkAsync(request, provider, ct));
        group.MapDelete("/link/{provider}/{subject}", (HttpRequest request, string provider, string subject, CancellationToken ct) => SpineAuthEndpoints.UnlinkAsync(request, provider, subject, ct));
        group.MapPost("/pending/{action}", (HttpRequest request, string action, CancellationToken ct) => SpineAuthEndpoints.PendingAsync(request, action, ct));
        group.MapPost("/passkey/{**action}", (HttpRequest request, string action, CancellationToken ct) => SpineAuthEndpoints.PasskeyAsync(request, action, ct));
        group.MapGet("/.well-known/jwks.json", (HttpRequest request) => SpineAuthEndpoints.Jwks(request));

        return group;
    }

    /// <summary>
    /// Maps <c>/.well-known/apple-app-site-association</c> and <c>/.well-known/assetlinks.json</c> at the site's root, so
    /// iOS, macOS and Android let the apps in <see cref="SpinePasskeyOptions"/> use passkeys for its domain. Map them on
    /// the host that <see cref="SpinePasskeyOptions.RelyingPartyId"/> names; an app with its own association files adds
    /// <c>webcredentials</c> and <c>get_login_creds</c> to those instead.
    /// </summary>
    /// <param name="endpoints">The application's route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSpinePasskeyAssociations(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/.well-known/apple-app-site-association", (HttpRequest request) => SpineAuthEndpoints.AppleAppSiteAssociation(request));
        endpoints.MapGet("/.well-known/assetlinks.json", (HttpRequest request) => SpineAuthEndpoints.AssetLinks(request));
        return endpoints;
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
    public static JsonTypeInfo<SpinePendingRequest> Pending => Info<SpinePendingRequest>();
    public static JsonTypeInfo<SpinePasskeyNewAccount> PasskeyNewAccount => Info<SpinePasskeyNewAccount>();
    public static JsonTypeInfo<SpinePasskeyResponse> PasskeyResponse => Info<SpinePasskeyResponse>();

    private static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)SpineAuthJson.Options.GetTypeInfo(typeof(T));
}
