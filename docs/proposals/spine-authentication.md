# Spine.Authentication — sign-in on every platform against the app's own server (proposal)

**Status:** Proposal, 2026-10-09. Not started. Roadmap: [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505). Issues: [#519](https://github.com/jonatansoderberg/Maui.Spine/issues/519) (client), [#520](https://github.com/jonatansoderberg/Maui.Spine/issues/520) (server), [#521](https://github.com/jonatansoderberg/Maui.Spine/issues/521) (Google and Microsoft), [#522](https://github.com/jonatansoderberg/Maui.Spine/issues/522) (passkeys, "maybe"). Depends on the navigation guard in [#506](https://github.com/jonatansoderberg/Maui.Spine/issues/506). Nothing here has been built or run; it rests on the Spine source in this worktree, package metadata from nuget.org and the documentation in [Sources](#11-sources). Visual concepts: https://claude.ai/artifact/J5TCSDk8Rk1vZNti9MBM3J (private until shared).
**Question:** Can adding sign-in to a Spine app be a few lines of options, with Sign in with Apple, Google, Microsoft, any OpenID Connect provider and email/password, native where the platform has a native sheet, on iOS, Mac Catalyst, Android and Windows, and with `[RequiresSignIn]` on a page?
**Answer:** Yes, as four packages. `Plugin.Maui.Spine.Authentication` holds Apple, generic OIDC, email/password, `ISpineAuth`, `[RequiresSignIn]` and a refreshing `HttpClient` handler, with no third-party dependency. `.Authentication.Google` and `.Authentication.Microsoft` carry the heavy native SDKs. `Plugin.Maui.Spine.Server.Authentication` sits next to the push backend, validates provider ID tokens and issues the app's own short-lived access tokens and rotating refresh tokens. The key design choice is that **every browser flow is brokered by the app's server**: the app opens the system browser on `{Server}/auth/{provider}/start`, the server is the confidential OIDC client, and the app gets a one-time code back that it trades for Spine tokens with PKCE. That one path covers Apple on Android and Windows, Google on iOS, any OIDC provider (and a BankID broker that speaks OIDC), and keeps all client secrets off the device. Native sheets (`ASAuthorizationController`, Credential Manager, MSAL's broker) produce an ID token that goes to the same server. Windows works on .NET 10 through a loopback redirect, not `WebAuthenticator`.

---

## 1. The conclusion in short

| Question | Answer | Evidence |
|---|---|---|
| Can MAUI's `WebAuthenticator` carry this? | **Not on Windows in .NET 10.** dotnet/maui#2702 was closed 2026-07-27 by PR #36640, merged to `net11.0` only. On iOS, Catalyst and Android it works and Spine uses it. | `gh api repos/dotnet/maui/pulls/36640`: base `net11.0` |
| Windows App SDK `OAuth2Manager` instead? | **No, not yet.** Learn (2026-08-30) says it is in the experimental channel only; Spine pins the stable `Microsoft.WindowsAppSDK` 1.8.260804001. | `Directory.Packages.props:62` |
| What does Windows use then? | RFC 8252 §7.3 loopback: the app listens on `http://127.0.0.1:{port}/`, opens the default browser, reads the redirect. Works unpackaged (the samples are `WindowsPackageType=None`) and needs no protocol registration. | `samples/MauiSpineSampleApp/MauiSpineSampleApp.csproj:38` |
| `AppleSignInAuthenticator`? | Not used. iOS only, no nonce control. Spine calls `ASAuthorizationController` directly, which also exists on Mac Catalyst and can combine Apple ID, saved passwords and passkeys in one sheet. | MAUI docs |
| Google on iOS natively? | **No.** `Xamarin.Google.iOS.SignIn` 5.0.2.4 was last published 2022-11-06. iOS gets the brokered browser flow. Android gets Credential Manager (`Xamarin.AndroidX.Credentials` 1.6.0.1, `…Identity.GoogleId` 1.1.0.13, both Microsoft). | nuget.org registration |
| Microsoft? | MSAL.NET (`Microsoft.Identity.Client` 4.90.1, MIT) with the broker: Authenticator or Company Portal on mobile, WAM on Windows. | nuget.org |
| Generic OIDC library in the app? | Not needed in v1, because the server is the OIDC client. `Duende.IdentityModel.OidcClient` 7.1.0 (Apache-2.0) is the candidate for a later client-only mode. | §4.1 |
| Server: `MapIdentityApi`? | Not enough: proprietary bearer tokens, no external-provider endpoints, no passkey endpoints. Spine's server package fills that gap and can use ASP.NET Core Identity as its user store. | Learn, Identity API |
| Sign-in UI | Platform sheet when it can show every configured method (Apple-only on Apple; Google-only on Android); a Spine `[NavigableSheet]` otherwise. | §5.3 |
| `[RequiresSignIn]` | A guard attribute on the #506 base. The guard awaits the sign-in sheet's result; the original navigation (with its parameter) then continues. | §5.3 |
| Push | The server stamps `PushInstallation.UserId` from the access token instead of trusting the body; the client sends the token through a contract in `.Common`. | §5.6 |
| App Store | Guideline 4.8: offering Google or Microsoft requires offering Sign in with Apple. Account deletion must revoke Apple tokens (TN3194). Both are built in. | §5.5 |

---

## 2. What Spine already has

| Part | Where | What it means for sign-in |
|---|---|---|
| Sheets with a result | `NavigationService.cs:169–258` (`NavigateToWithResultAsync`), `INavigableWithResult.cs`, `ReturnAsync` at `:264` | The sign-in sheet is an ordinary `[NavigableSheet]` page that returns a `SignInResult`. `ReturnAsync` closes the sheet before delivering the result (`:277–286`), so a guard that awaits it can push the target page into a settled host. |
| Sheet closing before a region push | `NavigationService.cs:146–154` (`WhenSheetClosed`) | The same wait applies when the guarded target is a region page opened from a sheet. |
| A login sheet in the sample | `samples/MauiSpineSampleApp/Pages/Sheets/LoginSheetPage.cs:3–6` | `[NavigableSheet(Title = "Log in", AllowedDetents = [Medium, FullScreen])]` with email and password fields that only close. The package's sheet is this page made real. |
| Root swap login ↔ main | `NavigationService.cs:337–364` (`SetRootCoreAsync`, comments "login → main app", "logout → login page") | An app that gates everything (not single pages) keeps doing this; `ISpineAuth.SignedOut` is the trigger. |
| Dismiss hooks | `ViewModelBase.cs:704` (`OnBackRequestedAsync`), `:709` (`OnCloseRequestedAsync`) | The sign-in sheet cancels a running browser flow when it is swiped away. |
| Header-bar actions | `Core/PageActionRole.cs` (`Confirm`, `Cancel`) | Cancel is the X page action in the sheet's header; "Continue" is the one footer primary action. |
| Module registration | `src/Plugin.Maui.Spine.PushNotifications/build/Plugin.Maui.Spine.PushNotifications.props`, `docs/wiki/packages.md` "Adding a module" | Each new package gets a `build/<id>.props` with `<SpineModule>`, so `UseSpine()` registers it only when referenced. |
| Interfaces in `.Common` resolved optionally | `Common/Core/IWidgetService.cs:4`; `PushNotificationService.cs:241` (`services.GetService(typeof(IWidgetService))`) | The pattern for the push link without a package reference between Authentication and PushNotifications. |
| `SecureStorage` | `PushNotificationService.cs:98–102` | Already used for the installation id; the refresh token goes there too. |
| Push register knows users | `Common/Push/PushInstallation.cs:76` (`UserId`), `Server/PushTarget.cs:46` (`PushTarget.User`), `Server/SpinePushNotificationsOptions.cs:118` (`AllowTags`), `:125` (`Authenticate`) | Sending to a user exists. But the client never sets `UserId` (`PushNotificationService.cs:227–245`), and the server stores the body's `UserId` as sent (`SpinePushNotificationsEndpoints.cs:71–75`), so a client could claim any user. |
| Client auth header for push | `PushNotifications/SpinePushNotificationsOptions.cs:101` (`AuthorizationHeader`) | Read on every call, so a rotating access token fits. |
| Windows single instance | `Platforms/Windows/MauiAppBuilderExtensions.Windows.cs:14–35` | Redirects only launch activations (`ILaunchActivatedEventArgs`). A protocol-activation redirect would need core work; the loopback redirect does not. |
| Server package | `Plugin.Maui.Spine.Server` (`AddSpinePushNotifications`, `MapSpinePushNotifications`, `UseInMemoryStore`/`UseAzureTableStore`) | The shape the auth server copies: options with `Validate()`, a store choice, endpoints usable from ASP.NET Core and Azure Functions. |
| Android browser | `Xamarin.AndroidX.Browser` 1.8.0.11 comes through `Microsoft.Maui.Essentials` 10.0.50 | Custom Tabs cost nothing extra. |

The core gets nothing from this proposal beyond what #506 already adds.

---

## 3. The platforms' building blocks

**Apple (iOS 15+, Catalyst 15+).** `ASAuthorizationController` with `ASAuthorizationAppleIDProvider` returns an identity token (a JWT signed by Apple, `aud` = bundle id) and a one-time authorization code. The same controller can take `ASAuthorizationPasswordProvider` (saved passwords) and `ASAuthorizationPlatformPublicKeyCredentialProvider` (passkeys) in one request, and `performAutoFillAssistedRequests` puts them in the keyboard bar. The nonce is sent to Apple as SHA-256 and to the backend raw. Name and email arrive only on the first authorization. iOS 26 adds `ASAuthorizationAccountCreationProvider`; whether it combines with the others in one controller is not verified. `ASWebAuthenticationSession` shows a "wants to use … to sign in" prompt unless `prefersEphemeralWebBrowserSession` is set, which then also drops Safari's cookies.

**Android (API 21; Credential Manager needs 23 with Play Services).** One `GetCredentialRequest` can hold `GetGoogleIdOption`, `GetPublicKeyCredentialOption` and `GetPasswordOption`, shown as one bottom sheet. Below Android 14 it runs through `Xamarin.AndroidX.Credentials.PlayServicesAuth` 1.6.0.2, which pulls Google Play Services. The old `play-services-auth` Google Sign-In is deprecated. There is no native Apple sign-in; Apple's web flow requires a Services ID and an https return URL, so it has to land on the server.

**Windows (10 17763).** No native sheet for Apple, Google or OIDC. `OAuth2Manager` is experimental. WAM (through MSAL) for Microsoft accounts and Entra. `webauthn.dll` for Windows Hello passkeys (`DSInternals.Win32.WebAuthn` 3.4.0 wraps it). The loopback redirect follows RFC 8252 §7.3 and works for Google and for the server broker; Apple does not accept loopback return URLs, which the broker hides.

**Server.** RFC 8252: native apps use the system browser with PKCE, never a WebView. The common mobile pattern is native SDK → ID token → server validates (JWKS, issuer, audience, expiry, nonce) → server issues its own session. Apple additionally requires revoking its tokens (`/auth/revoke`, client secret = a JWT signed with the Sign in with Apple key) when a user deletes the account.

**Ready-made libraries, and why they are not the base.** `Auth0.OidcClient.MAUI` 1.4.0 wraps OidcClient for Auth0. `Plugin.Firebase.Auth` dropped its built-in Apple, Google and Facebook providers in 4.0 (now 5.0.1). Supabase has `SignInWithIdToken`. Each ties the app to a hosted identity service; Spine's job is the native UI and the token plumbing against the app's own server. An app on one of those services can still use `[RequiresSignIn]` by registering its own `ISpineAuth`.

---

## 4. Alternatives and trade-offs

### 4.1 Where the browser flows run

| | A. App is the OIDC client (OidcClient) | B. Server brokers every browser flow | C. Hosted identity (Firebase, Supabase, Auth0) |
|---|---|---|---|
| Secrets on device | None with PKCE, but Apple's web flow and some brokers need a secret, so those break | None | None |
| Apple on Android/Windows | Needs an https return URL anyway | Yes: Apple posts to the server (`response_mode=form_post`) | Provider-dependent |
| Windows on .NET 10 | Own loopback code | Own loopback code | SDK-dependent |
| Token validation paths on the server | One per provider and per client id | One per provider | The vendor's |
| Works without the Spine server | Yes | No | Yes |
| Dependency in the app | `Duende.IdentityModel.OidcClient` | None | Vendor SDK |

**Recommendation: B.** The owner's scope already puts tokens on the app's own server (#520), and B is the only column where every provider works on every platform with one code path in the app. A is the later "no Spine server" mode (open question 1).

### 4.2 Which sheet the user sees

| Configured methods | iOS / Catalyst | Android | Windows |
|---|---|---|---|
| Apple only (+ saved passwords, passkeys) | `ASAuthorizationController` sheet, no Spine UI | Spine sheet → browser | Spine sheet → browser |
| Google only (+ passwords, passkeys) | Spine sheet → browser | Credential Manager sheet, no Spine UI | Spine sheet → browser |
| Microsoft only | MSAL broker UI | MSAL broker UI | WAM dialog |
| Anything mixed, any OIDC, email/password, sign-up | Spine `SignInSheet` with a button per provider; the button starts the native flow where there is one | same | same |

Before the Spine sheet opens, the package tries a silent sign-in where the platform has one (Credential Manager with auto-select, Apple's AutoFill-assisted request, MSAL `AcquireTokenSilent`). If it succeeds, no UI shows.

---

## 5. Proposed API surface

### 5.1 Registration

`UseSpine()` registers the package; configuration follows the convention of every other package (`UseSpinePushNotifications`, `UseSpineImages`): an idempotent `UseSpineAuthentication(a => …)`. Google and Microsoft add extension methods on the same options from their packages.

```csharp
builder.UseSpine()
       .UseSpineAuthentication(a =>
       {
           a.Server = new Uri("https://api.example.com/auth");   // MapSpineAuthentication's prefix
           a.AddApple();
           a.AddGoogle();                                        // Plugin.Maui.Spine.Authentication.Google
           a.AddMicrosoft(m => m.ClientId = "…");                // Plugin.Maui.Spine.Authentication.Microsoft
           a.AddOpenIdConnect("corp", "Contoso");                // authority and secret live on the server
           a.AddEmailPassword(e => e.AllowSignUp = true);
           a.CallbackScheme = "se.example.app";                  // iOS/Android redirect; Windows uses loopback
       });
```

The Android callback activity and its intent filter come from the package's `build/*.targets`, driven by an MSBuild property `SpineAuthenticationCallbackScheme`, the way Widgets adds its manifest entries. `UseSpineAuthentication` throws at startup when Google or Microsoft is configured without Apple on an Apple target, with the guideline number in the message; `a.AppleNotRequired = true` turns that off for apps outside the App Store.

### 5.2 `ISpineAuth` and the HTTP handler

```csharp
namespace Plugin.Maui.Spine.Authentication;

public interface ISpineAuth
{
    SpineUser? User { get; }
    bool IsSignedIn => User is not null;
    IReadOnlyList<SignInProvider> Providers { get; }

    /// <summary>Silent first, then the platform sheet or <see cref="SignInSheet"/>.</summary>
    Task<SignInResult> SignInAsync(CancellationToken cancellationToken = default);
    Task<SignInResult> SignInAsync(string provider, CancellationToken cancellationToken = default);
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
    Task DeleteAccountAsync(CancellationToken cancellationToken = default);

    event EventHandler<SpineUser>? SignedIn;
    event EventHandler? SignedOut;
}

public sealed record SpineUser(string Id, string? Email, string? DisplayName, string Provider);
public enum SignInStatus { SignedIn, Canceled, Failed }
public sealed record SignInResult(SignInStatus Status, SpineUser? User = null, string? Error = null);
```

`Error` carries the endpoint path and HTTP status when the server refused, so a misconfiguration is visible rather than looking like a cancel. The refresh token lives in `SecureStorage`; the access token only in memory. `GetAccessTokenAsync` refreshes when less than a minute is left, with one refresh in flight at a time.

```csharp
builder.Services.AddHttpClient<OrdersApi>(c => c.BaseAddress = new("https://api.example.com"))
                .AddSpineAuthentication();   // DelegatingHandler: bearer header, one refresh-and-retry on 401
```

A refresh that the server rejects (revoked, reused) signs the user out and raises `SignedOut`. The handler depends on `Microsoft.Extensions.Http`; `auth.CreateHandler()` covers apps without `IHttpClientFactory`.

### 5.3 `[RequiresSignIn]` and the sign-in sheet

The attribute derives from the base attribute #506 adds to the core; the names below follow the guards proposal's sketch and change with it.

```csharp
[NavigableRegion(Title = "Orders")]
[RequiresSignIn]
public partial class OrdersPage { public OrdersPage() => InitializeComponent(); }

public sealed class RequiresSignInAttribute : NavigationGuardAttribute;

internal sealed class SignInGuard(ISpineAuth auth) : INavigationGuard<RequiresSignInAttribute>
{
    public async ValueTask<bool> CanNavigateAsync(RequiresSignInAttribute attribute, NavigationGuardContext context) =>
        auth.IsSignedIn || (await auth.SignInAsync(context.CancellationToken)).Status is SignInStatus.SignedIn;
}
```

The guard runs inside `NavigateToAsync<TPage, TParam>(param)` before the page is resolved, so the parameter is still in the call frame. When the guard returns `true` the navigation simply continues to the original page; nothing has to be remembered and replayed. A cancel leaves the user where they were. Because shortcuts, search results and notification taps all navigate through `NavigationService`, they are guarded the same way, including on a cold start. The same attribute on a `[RelayCommand]` method uses #506's command guard ("Like" asks to sign in, then likes).

When the platform sheet cannot show every configured method, `SignInAsync` opens the package's sheet with `NavigateToWithResultAsync<SignInSheet, SignInResult>()`:

```csharp
[NavigableSheet(Title = "Sign in", AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public partial class SignInSheet : INavigableWithResult<SignInResult> { … }
```

Layout: provider buttons first (Apple's in Apple's prescribed style and order), then email and password with the platform's autofill content types, a footer "Continue", "Create account" and "Forgot password" as links, Cancel as the header X (`PageActionRole.Cancel`). Strings go through `SpineStrings` with English and Swedish defaults. If the guarded target is itself a sheet, the sign-in sheet returns first and the target opens as a fresh sheet (`ReturnAsync` closes before it delivers). An app that wants its own design registers `a.SignInPage<MySignInSheet>()`, any `[NavigableSheet]` that returns `SignInResult`.

### 5.4 Google and Microsoft packages

`.Authentication.Google`: `a.AddGoogle(g => g.ServerClientId = …)`. Android: Credential Manager with `GetGoogleIdOption` (nonce from the server), and `GetPasswordOption` for saved email/password credentials when email/password is configured. iOS, Catalyst, Windows: the brokered browser flow, no native code. The resulting ID token goes to `POST {Server}/google/token`.

`.Authentication.Microsoft`: `a.AddMicrosoft(m => { m.ClientId = …; m.Tenant = "common"; m.Scopes = […]; })`. MSAL with `WithBroker` (Authenticator / Company Portal on Android and iOS, WAM on Windows), `AcquireTokenSilent` first. Two modes: hand MSAL's ID token to the Spine server like the others, or (for Entra-protected APIs) let the HTTP handler send MSAL's own access token and skip Spine tokens. iOS needs the `msauthv2` query scheme and keychain group; Android the broker redirect URI with the signature hash. The package's README carries those steps.

### 5.5 Server: `Plugin.Maui.Spine.Server.Authentication`

```csharp
builder.Services.AddSpineAuthentication(a =>
{
    a.Issuer = "https://api.example.com";
    a.SigningKey = builder.Configuration["Auth:SigningKey"];          // or a JWK set for rotation
    a.AccessTokenLifetime = TimeSpan.FromMinutes(15);
    a.RefreshTokenLifetime = TimeSpan.FromDays(60);
    a.Apple(o => { o.TeamId = …; o.KeyId = …; o.PrivateKey = …; o.BundleIds = ["se.example.app"]; o.ServicesId = "se.example.web"; });
    a.Google(o => { o.ClientIds = [android, ios, web]; o.WebClientSecret = …; });
    a.Microsoft(o => { o.ClientId = …; o.Tenant = "common"; });
    a.OpenIdConnect("corp", o => { o.Authority = …; o.ClientId = …; o.ClientSecret = …; });
    a.EmailPassword();
    a.UseIdentity<AppUser>();                 // or a.UseUsers<MyUserResolver>()
    a.UseAzureTableStore(connectionString);   // refresh tokens and one-time codes; UseInMemoryStore() for tests
});

app.UseAuthentication();
app.MapSpineAuthentication("/auth");
```

| Endpoint | Does |
|---|---|
| `GET /auth/nonce` | A nonce for a native request, bound to the installation |
| `POST /auth/{provider}/token` | ID token from a native SDK + raw nonce → Spine tokens. Apple: JWKS from `appleid.apple.com/auth/keys`, `aud` ∈ bundle ids, `nonce` claim = SHA-256 of the raw nonce. Apple's authorization code is exchanged once to keep Apple's refresh token for revocation. |
| `GET /auth/{provider}/start`, `GET`/`POST /auth/{provider}/callback` | The broker: PKCE challenge and the app's redirect in, code exchange as confidential client, ID token validated, one-time code out to the app's scheme or loopback (loopback only to `127.0.0.1`/`[::1]`, scheme only from the configured list) |
| `POST /auth/token` | `authorization_code` (one-time code + verifier) or `refresh_token` → new access and refresh token |
| `POST /auth/password/{signin,signup,forgot,reset}` | Over `UserManager<TUser>` with lockout; the app supplies `IEmailSender<TUser>` |
| `POST /auth/revoke` | Sign-out: revokes the refresh-token family |
| `DELETE /auth/account` | Deletes through the user store, revokes Apple tokens, revokes all families |

Access tokens are JWTs; `AddSpineAuthentication` also registers a JwtBearer scheme for them as the default, so the app's own endpoints use `[Authorize]`. Refresh tokens are opaque, stored hashed, rotated on every use; presenting a rotated token again revokes the whole family. Users link by (provider, subject) and, when the provider vouches for a verified email, may be merged with an existing account only if the app turns that on (`a.LinkByVerifiedEmail`). Dependencies: `.Common`, `Microsoft.AspNetCore.Authentication.JwtBearer` (brings the OIDC metadata and JWKS retrieval), `Azure.Data.Tables` (already used by `.Server`); ASP.NET Core Identity is in the shared framework, the EF stores are the app's choice. No reference to `Plugin.Maui.Spine.Server`, so an app can have sign-in without push.

### 5.6 The push link

Server, in `Plugin.Maui.Spine.Server` (a small generic addition, no auth reference):

```csharp
builder.Services.AddSpinePushNotifications(o =>
{
    o.UserId = request => request.HttpContext.User.FindFirstValue("sub");   // new
});
```

When `UserId` is set, `UpsertAsync` replaces the body's `UserId` with the resolved one and replaces any `user:` tag with `user:<id>` (or none), so `PushTarget.User(id)` reaches exactly the devices that signed in. This closes the gap in `SpinePushNotificationsEndpoints.cs:71–75` for every app, signed-in or not.

Client: `.Common` gets a three-member contract, implemented by Authentication and resolved optionally by PushNotifications, as `IWidgetService` is today:

```csharp
public interface ISignedInSession
{
    string? UserId { get; }
    Task<string?> GetAuthorizationHeaderAsync(CancellationToken cancellationToken);
    event EventHandler? Changed;
}
```

If `AuthorizationHeader` is unset, PushNotifications uses the session's header, and on `Changed` calls `RefreshAsync(force: true)`. Sign-out re-registers without a header, which drops the user tag; a device offline at sign-out keeps the tag until its next registration (not solved in v1).

---

## 6. Passkeys

Marked "maybe" on 2026-10-09; this section is what saying yes would mean.

**Client.** Passkeys ride in the sheets that already exist: `ASAuthorizationPlatformPublicKeyCredentialProvider` in the same `ASAuthorizationController` as Apple ID and passwords; `GetPublicKeyCredentialOption` in the same Credential Manager request as Google and passwords; Windows Hello through `webauthn.dll`. The app fetches options JSON from the server, hands it to the platform, posts the result back. A passkey request also goes into the AutoFill-assisted request, so the username field offers it.

**Server.** .NET 10 Identity has the ceremony but no endpoints and no native-app story. `SignInManager.MakePasskeyCreationOptionsAsync` / `PerformPasskeyAttestationAsync` / `MakePasskeyRequestOptionsAsync` / `PasskeySignInAsync` keep their state in an authentication cookie, which a token-based app client does not carry; Spine would call `IPasskeyHandler<TUser>` (the four `Make…`/`Perform…` methods) and keep the state server-side under a challenge id. `IdentityPasskeyOptions.ServerDomain` must be set (the default trusts the Host header). Origins: iOS sends the relying party's https origin; Android sends `android:apk-key-hash:<base64url SHA-256 of the signing cert>`, which the default subdomain check rejects, so `ValidateOrigin` needs a list of allowed apk hashes (inferred from the WebAuthn spec and Android docs; not tried). The server must serve `/.well-known/apple-app-site-association` (`webcredentials`) and `/.well-known/assetlinks.json` (`get_login_creds`) on that domain; `MapSpineAuthentication` can serve both from options.

**Cost and risk.** A real domain per app, associated-domains entitlements and provisioning profiles that include them (the iOS signing setup has bitten before), and three platform implementations to test on devices. Windows Hello passkeys need Windows 10 1903+ for the API and Windows 11 22H2+ for synced passkeys (not verified in detail).

**Recommendation.** Do it after v1 as `Plugin.Maui.Spine.Authentication.Passkeys` plus endpoints in the server package behind `a.Passkeys(...)`, so the main packages ship without a domain requirement. iOS 26's account-creation provider (passkey-only sign-up) stays out until it is verified that it combines with the other providers.

---

## 7. Packages and what they pull in

| Package | Depends on | Pulls in | Platforms |
|---|---|---|---|
| `Plugin.Maui.Spine.Authentication` | core, `.Common` | `Microsoft.Extensions.Http`; AuthenticationServices and Custom Tabs are already present | all four |
| `Plugin.Maui.Spine.Authentication.Google` | `.Authentication` | Android: `Xamarin.AndroidX.Credentials` 1.6.0.1, `.Credentials.PlayServicesAuth` 1.6.0.2 (Play Services), `Xamarin.GoogleAndroid.Libraries.Identity.GoogleId` 1.1.0.13. Other platforms: nothing | all four |
| `Plugin.Maui.Spine.Authentication.Microsoft` | `.Authentication` | `Microsoft.Identity.Client` 4.90.x (+ `.Broker` on Windows for WAM) | all four |
| `Plugin.Maui.Spine.Server.Authentication` | `.Common` | `Microsoft.AspNetCore.Authentication.JwtBearer`, `Azure.Data.Tables` | `net10.0` |
| `Plugin.Maui.Spine.Authentication.Passkeys` (if accepted) | `.Authentication` | Android: `Xamarin.AndroidX.Credentials` (shared with Google). Windows: own P/Invoke or `DSInternals.Win32.WebAuthn` | all four |

`.Common` gets `ISignedInSession` and the request/response records shared by app and server. `Plugin.Maui.Spine.Server` gets the `UserId` resolver. The core gets nothing beyond #506.

---

## 8. Implementation steps

1. **Server first** (#520): `AddSpineAuthentication`, tokens and rotation, the store, email/password over Identity, Apple ID-token validation, tests against recorded Apple and Google tokens. The push `UserId` resolver in `.Server`.
2. **Client base** (#519): `ISpineAuth`, `SecureStorage`, the HTTP handler, `SignInSheet`, Apple native on iOS and Catalyst, the browser broker (`WebAuthenticator` on iOS/Catalyst/Android, loopback on Windows), the Android callback activity from MSBuild. `[RequiresSignIn]` once #506 has landed.
3. **Sample**: replace `LoginSheetPage` in `MauiSpineSampleApp` with `[RequiresSignIn]` on a page and a sample server next to `MauiSpinePushNotificationsSampleApp.Server`; device runs on the iPhone, an Android emulator with Play Services, and Windows unpackaged.
4. **Google and Microsoft** (#521), each with its README setup (client ids, signature hashes, MSAL redirect URIs).
5. **Push link** (`ISignedInSession` in `.Common`, PushNotifications reading it).
6. **Wiki** `docs/wiki/authentication.md`, `packages.md` rows, and a `spine-authentication` agent skill.
7. **Passkeys** (#522) if accepted.

---

## 9. What is not verified

- Apple's web flow posting to the server, then redirecting to a loopback URL on Windows: the redirect chain is standard, but not run.
- Whether `WebAuthenticator` on Android coexists with Spine's single-activity setup and Credential Manager's activity results.
- MSAL's WAM broker in an unpackaged MAUI app on Windows App SDK 1.8.
- Whether `ASAuthorizationAccountCreationProvider` (iOS 26) combines with Apple ID and passkey requests.
- The Android passkey origin format against .NET 10's `ValidateOrigin`.
- The exact current wording of App Review guideline 4.8 (it was relaxed in 2024 to "an equivalent privacy-focused option"; Sign in with Apple remains the simple way to satisfy it).

---

## 10. Open questions for the owner

1. **Server required?** Recommendation B makes the Spine server mandatory for browser flows. Is a later client-only OIDC mode (OidcClient, for apps on Auth0 or Entra External ID without a Spine server) wanted, or is "bring your own `ISpineAuth`" enough?
2. **Registration shape.** `UseSpineAuthentication(a => …)` per the package convention, or the issue's `o.AddAuthentication(…)` inside `UseSpine`?
3. **User store default.** ASP.NET Core Identity with EF Core, or a minimal Spine user store on Azure Tables like the push register, with Identity as the option?
4. **iOS browser prompt.** Ephemeral `ASWebAuthenticationSession` by default (no "wants to use … to sign in" prompt, but no shared Safari session), or the system default?
5. **Push link default.** Should the client send the signed-in token to the push backend automatically when both packages are present?
6. **Passkeys.** Yes/no for v2, and which domain the samples would associate.

---

## 11. Sources

- MAUI Web Authenticator (.NET 10): https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/communication/authentication?view=net-maui-10.0
- dotnet/maui#2702 and PR #36640 (merged to `net11.0`): https://github.com/dotnet/maui/issues/2702, https://github.com/dotnet/maui/pull/36640
- Windows App SDK `OAuth2Manager` (experimental channel only, page dated 2026-08-30): https://learn.microsoft.com/en-us/windows/apps/develop/security/oauth2
- WebAuthn on Windows: https://learn.microsoft.com/en-us/windows/win32/webauthn/-webauthn-portal; `DSInternals.Win32.WebAuthn`: https://www.nuget.org/packages/DSInternals.Win32.WebAuthn
- Duende OidcClient: https://docs.duendesoftware.com/identitymodel-oidcclient/, https://www.nuget.org/packages/Duende.IdentityModel.OidcClient
- MSAL.NET: https://www.nuget.org/packages/Microsoft.Identity.Client
- AndroidX Credentials bindings: https://www.nuget.org/packages/Xamarin.AndroidX.Credentials, https://www.nuget.org/packages/Xamarin.AndroidX.Credentials.PlayServicesAuth, https://www.nuget.org/packages/Xamarin.GoogleAndroid.Libraries.Identity.GoogleId
- Google iOS binding (last release 2022-11-06): https://www.nuget.org/packages/Xamarin.Google.iOS.SignIn
- Legacy Google Sign-In migration: https://developer.android.com/identity/sign-in/legacy-gsi-migration
- `Auth0.OidcClient.MAUI`: https://www.nuget.org/packages/Auth0.OidcClient.MAUI; `Plugin.Firebase.Auth`: https://www.nuget.org/packages/Plugin.Firebase.Auth
- ASP.NET Core passkeys (.NET 10): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0
- Identity API endpoints (`MapIdentityApi`): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0
- RFC 8252, OAuth 2.0 for Native Apps: https://rfc-editor.org/rfc/rfc8252
- Apple TN3194, account deletion and token revocation: https://developer.apple.com/documentation/technotes/tn3194-handling-account-deletions-and-revoking-tokens-for-sign-in-with-apple
- App Review Guidelines 4.8: https://developer.apple.com/app-store/review/guidelines/#login-services
