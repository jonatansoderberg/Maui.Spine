# Sign-in on the server

```bash
dotnet add package Plugin.Maui.Spine.Server.Authentication
```

`Plugin.Maui.Spine.Server.Authentication` is the server half of sign-in for a Spine app. It follows the usual mobile pattern: the platform's own sign-in sheet hands the app an ID token, and the app's server checks that token and issues its own short-lived access token and rotating refresh token. Email and password sign-in is built in, with verification codes, lockout and password reset. Deleting an account revokes Apple's tokens, as the App Store requires.

The package is a plain `net10.0` library with a framework reference to ASP.NET Core, so a Minimal API and an Azure Functions isolated worker can both host it. It does not reference `Plugin.Maui.Spine.Server`, so an app can have sign-in without push. The design is in [the proposal](../proposals/spine-authentication.md), §5.5 and §5.6.

Where a platform has no native sheet for a provider (Apple on Android and Windows, Google on iOS, any OpenID Connect provider), the server brokers a sign-in through the system browser. See [Browser sign-in](#browser-sign-in). The app half is [`Plugin.Maui.Spine.Authentication`](authentication.md) (#519, browser sign-in #557).

---

## Setup

```csharp
builder.Services.AddSingleton<ISpineAuthEmailSender, MyEmailSender>();   // when EmailPassword() is on
builder.Services.AddSpineAuthentication(a =>
{
    a.Issuer = "https://api.example.com";
    a.SigningKey = builder.Configuration["Auth:SigningKey"];          // EC P-256, PKCS#8 PEM

    a.Apple(o =>
    {
        o.BundleIds.Add("se.example.app");
        o.TeamId = "…"; o.KeyId = "…"; o.PrivateKey = builder.Configuration["Auth:Apple:PrivateKey"];  // for revocation
    });
    a.Google(o => o.ClientIds.Add("….apps.googleusercontent.com"));
    a.Microsoft(o => { o.ClientId = "…"; o.Tenant = "common"; });
    a.OpenIdConnect("corp", o => { o.Authority = "https://login.example.com"; o.ClientIds.Add("…"); });
    a.EmailPassword();

    a.UseAzureTableStore(builder.Configuration["Storage"]);   // or UseInMemoryStore(); UseUsers(...) for your own users
});

app.UseAuthentication();
app.UseAuthorization();
app.MapSpineAuthentication("/auth").RequireRateLimiting("auth");
```

- **The signing key** is an EC P-256 private key in PKCS#8 PEM. `openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt` makes one. Keep it in user secrets or Key Vault. To rotate, move the old key's public half to `PreviousSigningKeys`, which are still accepted and published, and put a new `SigningKey` in place.
- **A JwtBearer scheme** named `Spine` (`SpineAuthDefaults.AuthenticationScheme`) is registered as the default. The app's own endpoints use `[Authorize]` or `RequireAuthorization()`, and `sub` is the user id. Claims are not remapped, so `sub`, `email`, `name` and `provider` keep their JWT names.
- **Startup checks.** `AddSpineAuthentication` throws when the issuer, the key, a store, a provider's `ClientIds` or a way to sign in is missing. The host does not start with `EmailPassword()` and no `ISpineAuthEmailSender`, or with a key that is not P-256. Sign in with Apple without `TeamId`, `KeyId` and `PrivateKey` logs a warning, because Apple's tokens cannot then be revoked.
- **Rate limiting** is the app's: `MapSpineAuthentication` returns the route group for `.RequireRateLimiting(...)`. Lockout covers one account. A limit per address stops someone trying one password against many accounts.

## Endpoints

Bodies and answers are the records in `Plugin.Maui.Spine.Authentication` in `.Common`: `SpineTokens`, `SpineIdTokenRequest`, `SpineAuthProblem` and the rest. They are serialized with `SpineAuthJson.Options` (camelCase, nulls left out), so the app and the server share them. Every answer that carries a token has `Cache-Control: no-store`.

| Endpoint | Does | Answers |
|---|---|---|
| `GET /auth/nonce` | A one-time nonce, kept on the server for `NonceLifetime` (10 min) | `SpineNonce` |
| `POST /auth/{provider}/token` | An ID token from the platform's sheet, plus the raw nonce, traded for Spine tokens | `SpineTokens`; 401 `invalid_token` with the reason; 404 `unknown_provider` |
| `GET /auth/{provider}/start` | Opens a browser sign-in: a redirect to the provider | a redirect; 400 or 404 `SpineAuthProblem` |
| `GET`/`POST /auth/{provider}/callback` | Where the provider returns: a redirect to the app with a one-time `code` | a redirect; a plain 400 page for an expired sign-in |
| `POST /auth/token` | A refresh token (`SpineRefreshRequest`) or a browser sign-in's code with its PKCE verifier (`SpineCodeExchange`) traded for a new pair | `SpineTokens`; 401 `invalid_grant` |
| `POST /auth/revoke` | Signs one device out by revoking its sign-in | 204, known token or not |
| `DELETE /auth/account` | Deletes the user behind the access token | 204; 401 without a token |
| `POST /auth/password/{signup,verify,resend,signin,forgot,reset}` | Email and password | see below |
| `POST /auth/link/{provider}` | With the user's access token: an ID token (`SpineIdTokenRequest`) adds the provider to that user | `SpineTokens`; 409 `identity_in_use` |
| `POST /auth/link/ticket` | With the user's access token: a one-time ticket (5 minutes) for linking through the browser, passed as `link=` to `/{provider}/start` | `SpineLinkTicket` |
| `DELETE /auth/link/{provider}/{subject}` | With the user's access token: removes one login of a provider, such as one passkey (`SpineLoginInfo.Subject`) | as below |
| `DELETE /auth/link/{provider}` | With the user's access token: removes a provider, or `password` | `SpineTokens`; 404; 409 `last_login`; 501 when the app's `IUserResolver` cannot delete logins |
| `POST /auth/pending/{create,link}` | A pending identity's ticket (`SpinePendingRequest`): a new account, or, with an access token, the account the user proved | `SpineTokens`; 401 `invalid_grant`; 409 `identity_in_use` |
| `POST /auth/passkey/register/options`, `/register` | A new passkey: for the user behind the access token, or without one for a new account (`SpinePasskeyNewAccount`); the WebAuthn options (`SpinePasskeyChallenge`), then the platform's answer (`SpinePasskeyResponse`) | `SpinePasskeyChallenge`, then `SpineTokens`; 403 `signup_closed`; 401 `invalid_token` for a refused passkey, `invalid_grant` for an expired ceremony |
| `POST /auth/passkey/signin/options`, `/signin` | A sign-in with any passkey the device has for the domain | `SpinePasskeyChallenge`, then `SpineTokens`; 401 `unknown_passkey`, `invalid_token` |
| `GET /auth/.well-known/jwks.json` | The public keys, for other services that check Spine's access tokens | a JWK set |

A refusal is a `SpineAuthProblem` whose `error` is one of `SpineAuthErrors`. `detail` says only what is safe to tell a developer: an ID token's reason ("the token is for another client id"), never whether an account exists.

## ID tokens

Each provider's token is checked against the provider's keys from OpenID Connect discovery (`{Authority}/.well-known/openid-configuration`). The keys are fetched again once when a token names a key the server has not seen. Then come the issuer, the audience (one of `ClientIds`), the expiry with two minutes of skew, and the nonce.

| Provider | Audience | Nonce | Email trusted for linking |
|---|---|---|---|
| `Apple(...)` | the bundle ids (`BundleIds`) | required; the token carries its SHA-256 as lower-case hex, so the app gives Apple the hash and sends the server the raw nonce | yes |
| `Google(...)` | the Android, iOS and web client ids | required, as it is | yes |
| `Microsoft(...)` | `ClientId`; the issuer is checked against the token's `tid` and `Tenant` (`common`, `organizations`, `consumers` or a tenant id) | not required: MSAL picks its own | no |
| `OpenIdConnect(name, ...)` | `ClientIds` | required unless `RequireNonce = false`; `HashNonce` for providers that hash it | only with `TrustEmail = true` |

A nonce is used once. Without one, an ID token that someone else got hold of could be traded in until it expires, which is why only Microsoft goes without one by default.

**Users.** A user is found by (provider, `sub`). A new provider and `sub` makes a new user. Its email is kept only when the provider vouches for it: a trusted provider (`TrustEmail`) and `email_verified`. Anyone can put any address in a Microsoft tenant of their own, and an account that held someone else's address could later be handed to them through a password reset. Names are cut at 100 characters, and an ID token may be at most 16 KB. With `LinkByVerifiedEmail = true`, they are linked instead to the existing account with the same email. That needs the provider to vouch for the email (`email_verified` and a trusted provider) and the existing account's own email to be verified, so an unverified sign-up cannot take over someone's account. When linking is off and another account owns the email, the new user is made without an email. The display name comes from the request on the first sign-in (Apple sends the name only once) or from the token's `name`.

**Microsoft's email** is kept only when the token says the tenant verified the address's domain (`xms_edov`). That claim comes only from work and school accounts, and only with the optional claims `email` and `xms_edov` on the app registration. Personal Microsoft accounts get no email.

**What the user carries** (`SpineUser`, in every token answer, after a refresh too):

| | From |
|---|---|
| `GivenName`, `FamilyName` | The request (Apple's sheet, the first time) or the token's `given_name` and `family_name`. Kept as first given, like the display name. |
| `PictureUrl` | The token's `picture`, https only and at most 2,048 characters (Google, and OpenID Connect providers that send one). It follows the provider, which is where the user changes it. Microsoft's picture has no address: the app fetches it from Graph. |
| `Organization` | A Microsoft work or school account's tenant id (`tid`, not the personal accounts' tenant), or a Google Workspace domain (`hd`). It follows each Google and Microsoft sign-in, so a personal account clears it; Apple and passwords leave it as it is. |
| `SignInMethods` | Every provider with a login on the account (`apple`, `google`, `password`, …), read from the store when the tokens are issued. |

The user store keeps the names, the picture and the organization: three columns more on the Azure Tables row, read as empty from rows written before.

**Apple's tokens.** With `TeamId`, `KeyId` and `PrivateKey` (the `.p8` of a Sign in with Apple key), the server trades the request's `authorizationCode` at `https://appleid.apple.com/auth/token`. Apple's refresh token is kept with the login. `DELETE /auth/account` revokes it at `/auth/revoke`, as [TN3194](https://developer.apple.com/documentation/technotes/tn3194-handling-account-deletions-and-revoking-tokens-for-sign-in-with-apple) requires. The client secret is a five-minute ES256 JWT signed with that key.

## Browser sign-in

RFC 8252 with the server as the confidential client: the provider's client secret never reaches the app, and the app holds only a two-minute code bound to its own PKCE verifier.

```csharp
a.PublicUrl = "https://api.example.com/auth";          // where a browser reaches MapSpineAuthentication's prefix
a.RedirectSchemes.Add("se.example.app");                // the app's own scheme
a.AllowLoopbackRedirects = true;                        // only with Windows clients
a.Apple(o => { o.BundleIds.Add("se.example.app"); o.ServicesId = "se.example.web"; o.TeamId = "…"; o.KeyId = "…"; o.PrivateKey = "…"; });
a.Google(o => { o.ClientIds.Add("…android…"); o.WebClientId = "…web…"; o.WebClientSecret = "…"; });
a.OpenIdConnect("corp", o => { o.Authority = "https://login.example.com"; o.WebClientId = "…"; o.WebClientSecret = "…"; });
```

1. **The app** makes a PKCE verifier. It opens the system browser (`WebAuthenticator`, or the default browser on Windows) on `GET /auth/{provider}/start?redirect_uri=…&code_challenge=…&code_challenge_method=S256&state=…`.
2. **The server** checks the redirect. It keeps the app's redirect, state and challenge under a state of its own for 10 minutes, and redirects to the provider's `authorization_endpoint` from discovery. It sends its own `nonce`, and its own PKCE challenge where the provider takes one.
3. **The provider** returns to `{PublicUrl}/{provider}/callback`. Register that URL as the web client's redirect URI; Apple posts the answer as a form.
4. **The server** takes its state once. It trades the provider's code at the `token_endpoint` with the client secret (for Apple, a five-minute ES256 JWT with the Services ID as `sub`) and checks the ID token as a native one, nonce included. It signs the user in under the same rules as a native sign-in, and redirects to the app's `redirect_uri` with a one-time `code` and the app's `state`.
5. **The app** checks its `state` and posts `{ "code", "codeVerifier", "redirectUri" }` to `POST /auth/token`. The code works once, for two minutes, only with the same redirect and only with the verifier whose SHA-256 was the challenge.

- **Redirects.** A redirect must use one of `RedirectSchemes`. These are custom schemes only: `http`, `https`, `javascript`, `data` and `file` are refused at startup.
  - With `AllowLoopbackRedirects`, a redirect may also be `http` to a loopback IP literal on any port (`127.0.0.1`, `[::1]`; not the name `localhost`, RFC 8252 §8.3), which is what a Windows app uses. It is off by default: any program on the device can listen on loopback and finish a sign-in it started itself (RFC 8252 §8.6).
  - A custom scheme has the same limit on a device where another app claims it. PKCE stops a stolen code, not an app that starts the flow itself.
  - A redirect may not carry a fragment, or `code`, `state` or `error` in its query. It is stored, compared and sent back in its escaped form.
  - Anything else answers 400, before any provider is involved.
- **Errors go to the app.** A cancel at the provider comes back as `error=access_denied`. A code the provider refuses, or an unreachable provider, comes back as `server_error`. An ID token that fails validation comes back as `invalid_token`. The provider's own messages go to the log only. A callback whose state is unknown, used or older than 10 minutes cannot be returned to the app, so it shows a plain page that says to go back and try again.
- **Per provider:** `WebClientId` turns the browser sign-in on; without it, `/start` answers 404.
  - Tokens issued to the web client are accepted only from the server's own exchange, not at `POST /auth/{provider}/token`. Google on Android, whose Credential Manager issues tokens for the web client as its `serverClientId`, needs that id in `ClientIds` as well.
  - The same account through the browser and through the native sheet is one user, because users go by the provider's `sub`.
  - Apple: `ServicesId` with `TeamId`/`KeyId`/`PrivateKey`. Scopes `name email`, posted back as a form, without PKCE (Apple does not document it). The name Apple posts on the first sign-in becomes the display name, and the refresh token from this exchange is kept for revocation.
  - Google and OpenID Connect: `WebClientId` and `WebClientSecret`, `openid email profile`, with PKCE.
  - `Scopes`, `FormPost` and `UsePkce` change those defaults.
- **`PublicUrl`** is required with any `WebClientId`. The request's `Host` is not trusted for the callback address.
- **Rate limiting.** `/start` writes one row to the store for each request, kept 10 minutes, so put the group behind `.RequireRateLimiting(...)`. An unreachable discovery document answers 502 `provider_unavailable`.

## Spine's tokens

- **The access token** is a JWT signed with ES256 and the key's id in `kid`. It carries `sub`, `iss`, `aud` (`Audience`, by default the issuer), `exp`, `jti`, `provider`, and `email` and `name` when known. It lasts `AccessTokenLifetime`, 15 minutes by default.
- **The refresh token** is 32 random bytes, and only its SHA-256 is stored. Each use returns a new pair and retires the old token. Unused, it expires after `RefreshTokenLifetime`, 60 days by default; each refresh starts that period again.
- **Reuse revokes the sign-in.** Every token rotated from one sign-in belongs to a family. Presenting a retired token again revokes the whole family, the newest token included, and logs a warning.
- **The grace window.** Within `RefreshReuseGrace` (30 seconds) of its use, a retired token still gives a new pair. That covers a client whose answer was lost to a timeout or a suspended app. `TimeSpan.Zero` turns the grace off. Other sign-ins of the same user, on other devices, are not affected. The app therefore refreshes one request at a time.
- **Password reset and account deletion** revoke every family the user has.

## Email and password

`EmailPassword(e => …)` turns on `POST /auth/password/{action}`. The app registers an `ISpineAuthEmailSender`; Spine writes no email itself. It hands over a `SpineAuthEmail` with a kind (`VerifyEmail`, `ResetPassword` or `AlreadyRegistered`), the address, and the code with its expiry. The sender is called without the request waiting, so an answer about an existing account takes no longer than one about a missing account. Hand the email to a queue or a mail service and return. An exception in the sender is logged.

| Step | Body | Answers |
|---|---|---|
| `signup` | `SpinePasswordSignUp` | 202 and a `VerifyEmail` code; tokens when `RequireVerifiedEmail` is off |
| `verify` | `SpineEmailCode` | tokens; 400 `invalid_code` |
| `resend` | `SpineEmailRequest` | 202, and a new code if the account is unverified |
| `signin` | `SpinePasswordSignIn` | tokens; 401 `invalid_credentials` (also while locked out); 403 `email_not_verified` (a new code is sent) |
| `forgot` | `SpineEmailRequest` | 202, and a `ResetPassword` code if the account exists |
| `reset` | `SpinePasswordReset` | tokens; every other session of the user is revoked |

- **Codes** are 8 characters from an alphabet without look-alikes. There is one live code per purpose and email: a new code replaces the last, and any attempt uses the code up, right or wrong. Ask for a new code after a typo. Codes work for `CodeLifetime` (15 minutes) and only for their purpose. Case, spaces and dashes are ignored when one is typed.
- **No account enumeration.** With `RequireVerifiedEmail` on (the default), sign-up answers 202 whether or not the email is taken; the owner of a taken email gets an `AlreadyRegistered` email instead. `forgot` and `resend` answer 202 for any address. A wrong password and an unknown email both answer `invalid_credentials` and take about as long.
- **Lockout.** `MaxFailedSignIns` (5) wrong passwords in a row lock password sign-in for `LockoutDuration` (15 minutes). A locked account answers `invalid_credentials` like a wrong password, so the lockout does not show which emails have accounts. Every change to a user is conditional on the version that was read (`SpineAuthUser.Version`), and a conflict is read again and decided again. Parallel guesses therefore all count, and a sign-in that read the old password cannot finish after a reset.
- **Passwords** are hashed with ASP.NET Core Identity's `PasswordHasher` and rehashed when its format moves on. They are `MinPasswordLength` (8) to 1024 characters long.
- **A user from Apple or Google** can add a password through `forgot` and `reset`, because the code proves the email.

## Linking accounts

A user has one `Id` and any number of logins: a provider and its subject, with the email and name it gave and when it was added (`SpineUser.Logins`). Linking adds one; the user's access token is the proof of who it goes to, and the answer is new tokens with the logins updated.

- **An identity that belongs to another user** is refused with 409 `identity_in_use`. Nothing is merged.
- **Unlinking** never removes the last way in (409 `last_login`). Removing `password` also clears the password hash. Apple's tokens are revoked.
- **A pending identity.** With `a.AskBeforeNewAccount = true`, a sign-in with a provider account the server does not know answers 202 with a `SpinePendingIdentity` (a ticket, the provider, a vouched email, a name, an expiry of `PendingIdentityLifetime`, 10 minutes) and makes no user. `POST /auth/pending/create` makes the account; `POST /auth/pending/link` with an access token adds it to the account the user just signed in to. It works for the ID-token endpoint and the browser broker's code exchange alike. `LinkByVerifiedEmail` still links first, without asking.
- `IUserResolver.DeleteLoginAsync` is a default interface method that throws `NotSupportedException`, so an app's own resolver still compiles; unlinking answers 501 until it implements it.

## Passkeys

```csharp
a.Passkeys(p =>
{
    p.RelyingPartyId = "api.example.com";                        // the domain the passkeys belong to; it cannot change later
    p.RelyingPartyName = "Example";
    p.AppleAppIds.Add("TEAMID.se.example.app");                  // apple-app-site-association (webcredentials)
    p.AndroidApps.Add(new("se.example.app", ["AB:CD:…"]));       // assetlinks.json (get_login_creds), and its origin
});

app.MapSpineAuthentication("/auth");
app.MapSpinePasskeyAssociations();                               // /.well-known/… at the site's root
```

WebAuthn is [Fido2NetLib](https://github.com/passwordless-lib/fido2-net-lib). The server asks for a discoverable passkey with user verification and no attestation, and accepts the origin `https://{RelyingPartyId}`, the Android apps' `android:apk-key-hash:` origins and any in `Origins`.

- **A passkey is a login:** provider `passkey`, the credential id as the subject, its public key and signature counter on `SpineAuthLogin`. `Logins`, unlinking and "never the last way in" work as for the other providers, and the built-in stores keep the key with the login. `DELETE /auth/link/passkey/{credential id}` removes one passkey (`SpineLoginInfo.Subject` carries the id), and `DELETE /auth/link/passkey` all of them.
- **The user handle** is the UTF-8 of the user's id, so a sign-in without a username names its user; the server checks it against the passkey's owner.
- **A ceremony works once,** within `ChallengeLifetime` (5 minutes), kept in the one-time store. A counter that does not move forward refuses the sign-in, as from a cloned authenticator; Apple's passkeys keep it at 0.
- **A new account made with a passkey** needs a name or an email for the platform's sheet. The email stays on the login, unverified, never as the account's email; a code verifies an email, as for a password account. `AllowSignUp = false` closes it (403 `signup_closed`).
- **The association files** are served only with `Passkeys(...)` configured. An app that serves its own `apple-app-site-association` adds `webcredentials` to it instead of mapping Spine's.

## Stores

`UseAzureTableStore(connectionString, prefix = "SpineAuth")` keeps users and tokens in Azure Table Storage:
- users: `{prefix}Users`, `{prefix}Emails` (the unique index, so a race of two sign-ups gives the email to one), `{prefix}Logins` and `{prefix}UserLogins`;
- tokens: `{prefix}Refresh`, `{prefix}Families` and `{prefix}OneTime`.

Keys from outside are base64url-encoded. `UseInMemoryStore()` is for tests and samples: everyone is signed out on restart.

`UseUsers(sp => …)` puts users in the app's own database through `IUserResolver`, while tokens stay in the chosen store. `IUserResolver.CreateAsync` returns `false` when the email is taken. `UpdateAsync` returns `false` when `SpineAuthUser.Version` (an ETag or row version) no longer matches. `ISpineAuthStore` is the token side, for a store of your own with `UseStore(...)`.

Expired refresh tokens and one-time values stay until `ISpineAuthStore.PruneAsync(now)` removes them. Run it now and then, for example from a timer trigger.

## Account deletion

`DELETE /auth/account` with the user's access token runs `OnAccountDeleting(userId, services, ct)` first, so the app can delete its own data. An exception there stops the deletion. It then revokes Apple's tokens, revokes every Spine sign-in and removes the user and their logins. Access tokens already issued keep working until they expire, at most `AccessTokenLifetime`. The same Apple id signing in again later is a new user.

## Push

`Plugin.Maui.Spine.Server` takes the user from the request instead of the registration body:

```csharp
builder.Services.AddSpinePushNotifications(o =>
{
    o.UserId = request => request.GetSpineUserIdAsync();
});
```

With `UserId` set, the body's `UserId` and any `user:` tags are replaced: `user:<id>` from the access token, or nothing without one. `PushTarget.User(id)` therefore reaches only devices that signed in as that user. A device registered by one user answers 403 to another user's registration, until it registers again without a user (signing out). `GetSpineUserIdAsync()` reads the user that ASP.NET's middleware already authenticated. Under Azure Functions, where no middleware runs, it checks the bearer token itself.

## Azure Functions

The handlers are public static methods on `SpineAuthEndpoints`: `NonceAsync`, `IdTokenAsync`, `StartAsync`, `CallbackAsync` (`GET` and `POST`), `RefreshAsync`, `RevokeAsync`, `DeleteAccountAsync`, `PasswordAsync`, `LinkTicketAsync`, `LinkAsync`, `UnlinkAsync`, `PendingAsync`, `PasskeyAsync`, `AppleAppSiteAssociation`, `AssetLinks` and `Jwks`. They take the `HttpRequest`, as Server.AI's do:

```csharp
[Function("AuthToken")]
public Task<IResult> Token([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/token")] HttpRequest request, CancellationToken ct) =>
    SpineAuthEndpoints.RefreshAsync(request, ct);
```

`AddSpineAuthentication` registers the JwtBearer scheme there too, and `DeleteAccountAsync` authenticates the request itself.

## With Server.AI

Server.AI's default caller is the authenticated user's `sub`, so a signed-in user gets the features' `SignedIn` limits, and realtime features answer them. Under ASP.NET Core this needs only `app.UseAuthentication()`. Under Functions, set `ai.Identify` to read `GetSpineUserIdAsync()`.

## Sample

The push sample server (`samples/MauiSpinePushNotificationsSampleApp.Server`) turns on Sign in with Apple for the Showcase's bundle id, plus email and password. The browser sign-in turns on when user secrets hold `Auth:Apple:ServicesId` (with `TeamId`, `KeyId` and `PrivateKey`) or `Auth:Google:WebClientId` and `WebClientSecret`. It returns to `se.cosmomedia.spineshowcase:` or a loopback address, at `http://localhost:5100/auth` unless `Auth:PublicUrl` says otherwise. The codes are written to its log by `LogEmailSender`. It also maps a protected `GET /sample/me`, and its push register takes the user from the token. `send.http` has the steps. Without `Auth:SigningKey` in user secrets, the sample makes a signing key at startup, so every session ends with a restart, just as the in-memory users do.

## Not verified yet

- A real Sign in with Apple token from the native sheet, and a real authorization-code exchange and revocation. The tests use a stub issuer and a stub of Apple's endpoints; a device run comes with the app half (#519).
- Real Google and Microsoft tokens.
- The browser sign-in against real providers. Against Apple's and Google's real discovery documents, `/start` led to their authorize pages; with placeholder client ids, neither got further. The callback, the code exchange and Apple's form_post ran only against stubs.
- The handlers under Azure Functions (only compiled).
