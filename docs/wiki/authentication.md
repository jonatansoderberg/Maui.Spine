# Sign-in in the app

```bash
dotnet add package Plugin.Maui.Spine.Authentication
```

`Plugin.Maui.Spine.Authentication` signs the user in against the app's own server, which runs [`Plugin.Maui.Spine.Server.Authentication`](authentication-server.md), or directly at an OpenID Connect provider ([without a Spine server](#without-a-spine-server)):
- Sign in with Apple's own sheet on iOS and Mac Catalyst, and email and password in a ready-made sheet.
- Sign-in in the system browser: any provider the server brokers, and Apple on Android and Windows.
- [Sign in with Google](#sign-in-with-google) with `Plugin.Maui.Spine.Authentication.Google`: Credential Manager's sheet on Android, the browser elsewhere.
- [Sign in with Microsoft](#sign-in-with-microsoft) with `Plugin.Maui.Spine.Authentication.Microsoft`: MSAL with the broker, via the Spine server or straight to an Entra-protected API.
- [Passkeys](#passkeys) with `Plugin.Maui.Spine.Authentication.Passkeys`: sign-in, sign-up and AutoFill through the platform's own passkey sheet, on iOS and Mac Catalyst.
- `[RequiresSignIn]` on a page or a command.
- The refresh token in secure storage.
- An `HttpClient` handler that adds the access token and refreshes it.

The design is in [the proposal](../proposals/spine-authentication.md), §5.1–§5.3.

---

## Setup

```csharp
builder.UseSpine(o => o.AddAuthentication(a =>
{
    a.Server = new Uri("https://api.example.com/auth");   // MapSpineAuthentication's prefix on the server
    a.AddApple();
    a.AddEmailPassword(e => e.AllowSignUp = true);
    a.LinkPushNotifications = true;                       // opt-in: register for push as the signed-in user
}));

builder.Services.AddHttpClient<OrdersApi>(c => c.BaseAddress = new("https://api.example.com"))
                .AddSpineAuthentication();
```

- **`UseSpine` registers the package** when the app references it, and `o.AddAuthentication(…)` configures it. Nothing else is called.
- **Configuration errors stop the app at startup.** Without `Server`, or without a way to sign in, `UseSpine` throws with a message that says what is missing.
- **Sign in with Apple** needs two things:
  - `<SpineSignInWithApple>true</SpineSignInWithApple>` in the app's project, which adds the `com.apple.developer.applesignin` entitlement;
  - the Sign in with Apple capability on the App ID, then a provisioning profile made again.

  The entitlement is never added unless the app asks, because a profile without the capability stops the app from installing. On the server, `Apple(o => o.BundleIds.Add(…))`.

## Browser sign-in

```csharp
o.AddAuthentication(a =>
{
    a.Server = new Uri("https://api.example.com/auth");
    a.CallbackScheme = "se.example.app";        // the app's URL scheme: {scheme}://callback
    a.AddOpenIdConnect("corp", "Contoso");      // a provider the server brokers as OpenIdConnect("corp", …): "Continue with Contoso"
    a.AddApple();                               // Apple's sheet on iOS and Mac Catalyst, the browser elsewhere
});
```

```xml
<SpineAuthenticationCallbackScheme>se.example.app</SpineAuthenticationCallbackScheme>
```

- **The flow.** The app makes a PKCE verifier and a state, and opens `{Server}/{provider}/start` in the system browser. The server sends the user to the provider and back, then returns to the app with a one-time code. The app checks the state and trades the code and the verifier at `POST {Server}/token`. Tokens never pass through the browser.
- **The browser** is MAUI's `WebAuthenticator` on iOS, Mac Catalyst and Android: `ASWebAuthenticationSession` and Custom Tabs. On Windows, where `WebAuthenticator` does not work in .NET 10, the default browser returns to a loopback address (`http://127.0.0.1:{port}/`, RFC 8252) and shows "You can close this window". A closed tab sends nothing back there, so the sign-in gives up after five minutes.
- **`CallbackScheme`** is lowercase (Android matches the scheme as written). `UseSpine` throws at startup when a browser sign-in is configured without it, except on Windows.
- **Android** needs no code. The project property gives the package's callback activity an intent filter for `{scheme}://callback`, and Custom Tabs its `<queries>` entry, through a manifest overlay. The filter takes only the host `callback`, so the app's own links on the same scheme still go to the app.
- **iOS** asks for nothing and shares no cookies with Safari (`EphemeralBrowserSession`, on by default). Turned off, iOS first asks "… wants to use … to sign in", and a provider may already know the user.
- **The server** lists the scheme in `RedirectSchemes` and sets `AllowLoopbackRedirects` for Windows; Google needs its `WebClientId` and `WebClientSecret`. See [the server's page](authentication-server.md).
- **Closing the browser** is `Canceled`, and so is cancelling at the provider (`access_denied`). Another state in the answer is `Failed`.

`SignInAsync()` opens the browser directly when a browser provider is the only way to sign in. Otherwise the sheet shows "Continue with …" per provider, and `SignInAsync("google")` opens one directly.

## Sign in with Google

```bash
dotnet add package Plugin.Maui.Spine.Authentication.Google
```

```csharp
o.AddAuthentication(a =>
{
    a.Server = new Uri("https://api.example.com/auth");
    a.CallbackScheme = "se.example.app";
    a.AddApple();
    a.AddGoogle(g => g.ServerClientId = "….apps.googleusercontent.com");   // the OAuth Web client's id
});
```

- **Android** shows Credential Manager's own sheet. It tries first the Google accounts that signed in to the app before, picked without a tap when there is one, then every account on the device. The ID token carries the server's nonce (`GET /auth/nonce`) and goes to `POST {Server}/google/token`.
- **iOS, Mac Catalyst and Windows** go through the server's broker in the browser, as [above](#browser-sign-in). The GoogleSignIn iOS binding is no longer published, so there is no native sheet there.
- **Without Credential Manager** (Android before 6.0, or Android 13 and older without Google Play services), Google goes through the broker in the browser. So an app with Google sets `CallbackScheme` on Android too, and `UseSpine` throws there without it.
- **Sign-out** clears Credential Manager's state too, so the next sign-in does not pick the same account by itself.
- **Errors.**
  - A closed sheet is `Canceled`.
  - No Google account on the device is `Failed` with a message that says so.
  - Any other Credential Manager error is `Failed` with its type.
- **The server** lists the Web client's id in `Google(o => o.ClientIds)`, and as `WebClientId` with its secret for the broker.
- **App Store Review Guideline 4.8.** An app on iOS or Mac Catalyst that offers Google must also offer Sign in with Apple, so `UseSpine` throws there without `AddApple()`. `a.AppleNotRequired = true` is for an app outside the App Store.
- **Only with a Spine server**, not with `UseIdentityProvider`.

The Google Cloud steps (a Web client, and an Android client with the package name and the signing key's SHA-1) are in [the package's README](../../src/Plugin.Maui.Spine.Authentication.Google/README.md).

## Sign in with Microsoft

```bash
dotnet add package Plugin.Maui.Spine.Authentication.Microsoft
```

Two ways, both through MSAL.

**Through the Spine server** (`AddMicrosoft`): MSAL's ID token goes to `POST {Server}/microsoft/token`. The user is then a Spine user like any other, with Spine's tokens. The server configures `Microsoft(o => { o.ClientId = …; o.Tenant = …; })`.

```csharp
o.AddAuthentication(a =>
{
    a.Server = new Uri("https://api.example.com/auth");
    a.AddApple();
    a.AddMicrosoft(m => m.ClientId = "…");
});
```

**Without a Spine server** (`UseMicrosoft`, in place of `Server`): the session's tokens are MSAL's.
- `GetAccessTokenAsync` and the `HttpClient` handler hand out an access token for `Scopes`, for an API that Entra protects.
- MSAL keeps the tokens and renews them silently.
- When MSAL needs the user again (a revoked sign-in, a changed password), the app signs out and raises `SignedOut`.

```csharp
o.AddAuthentication(a => a.UseMicrosoft(m =>
{
    m.ClientId = "…";
    m.Tenant = "contoso.onmicrosoft.com";
    m.Scopes.Add("api://orders/access_as_user");
}));
```

- **The broker.**
  - **iOS and Android:** Microsoft Authenticator or Company Portal when one is installed, which gives single sign-on and the device-bound tokens that conditional access asks for; otherwise the system browser.
  - **Windows:** WAM.
  - **Mac Catalyst:** MSAL has no build for it, so its sign-in runs in Spine's browser layer and returns to `{CallbackScheme}://callback`.
  - `m.UseBroker = false` keeps to the system browser.
- **A remembered account** signs in without a prompt when MSAL can renew its token.
- **Sign-out** removes the account from MSAL's cache too.
- **The cache.** MSAL keeps it in the Keychain on iOS (in the app's own keychain group, so no entitlement) and on Mac Catalyst, in its own storage on Android, and in a DPAPI-protected file on Windows.
- **App Store Review Guideline 4.8** applies to `AddMicrosoft` as to Google. `UseMicrosoft` is for apps whose users sign in with their organization's account, which the guideline exempts, so it is not checked.

The Entra setup, a redirect URI per platform and the iOS Info.plist entries (the build stops without them), is in [the package's README](../../src/Plugin.Maui.Spine.Authentication.Microsoft/README.md).

## Passkeys

```bash
dotnet add package Plugin.Maui.Spine.Authentication.Passkeys
```

```csharp
o.AddAuthentication(a =>
{
    a.Server = new Uri("https://api.example.com/auth");
    a.AddApple();
    a.AddPasskeys();                           // a => a.AddPasskeys(p => p.AllowSignUp = false) without sign-up
});
```

```xml
<SpinePasskeyDomain>api.example.com</SpinePasskeyDomain>
```

A passkey belongs to a domain, the server's `Passkeys(p => p.RelyingPartyId = "api.example.com")`, and iOS lets the app use it only when the two vouch for each other:
- **The app** has the domain in its Associated Domains entitlement as `webcredentials:api.example.com`. `SpinePasskeyDomain` adds it, and the App ID needs the Associated Domains capability, then a provisioning profile made again.
- **The domain** names the app in `/.well-known/apple-app-site-association`, which `app.MapSpinePasskeyAssociations()` serves from `AppleAppIds` (`TEAMID.bundle.id`). Apple's CDN fetches it, so the server must be public over https.

What the sheet offers:
- **"Sign in with a passkey"**: the platform's sheet lists the device's passkeys for the domain (and passkeys on a nearby phone), with no username typed.
- **"Create account with a passkey"**: a name or an email, then the sheet makes the passkey and the account signs in. The email is shown on the login, unverified, and is not the account's email.
- **AutoFill** on iOS: while the email field shows, the keyboard's suggestions offer the device's passkeys, and picking one signs in.
- **"Add a passkey"** on a signed-in account: `LinkAsync("passkey")`. An account can have a passkey on each device; `UnlinkAsync("passkey", login.Subject)` removes one (a lost phone's), `UnlinkAsync("passkey")` all of them, never the last way in.

The same through `ISpineAuth`: `SignInAsync("passkey")`, `SignUpWithPasskeyAsync(displayName, email)`, and `SignInWithPasskeyAutoFillAsync(cancellationToken)` for an app's own sign-in page, started when its username field shows and cancelled when the page goes. That field needs the `Username` text content type (the sheet sets it on iOS).

- **Platforms:** iOS and Mac Catalyst 16 or later. Android (#601) and Windows (#602) are not there yet; the sheet leaves passkeys out there, and the methods answer `Failed`.
- **The simulator** needs a signed build (`CodesignKey` and a profile for the App ID) so the app has an application identifier, and Face ID enrolled (Features ▸ Face ID ▸ Enrolled). Without the identifier iOS answers error 1004.

## Without a Spine server

```csharp
o.AddAuthentication(a =>
{
    a.UseIdentityProvider(p =>
    {
        p.Authority = new Uri("https://example.eu.auth0.com/");
        p.ClientId = "…";                                   // a native (public) client, no secret
        p.Name = "auth0";
        p.AuthorizeParameters["audience"] = "https://api.example.com";
    });
    a.CallbackScheme = "se.example.app";
});
```

The app signs in directly at an OpenID Connect provider (Auth0, Entra ID, Keycloak, …) as a public client with PKCE, in the same system browser.
- **The protocol.** Discovery from `/.well-known/openid-configuration`, the code exchange, refresh with `offline_access` (in `Scopes` by default), and RFC 7009 revocation at sign-out when the provider has a `revocation_endpoint`.
- **The user** comes from the ID token: `sub`, `name`, `email`. The app checks its `iss`, `aud`, `exp` and nonce. Its signature is not checked: the token comes straight from the token endpoint over TLS (OIDC Core §3.1.3.7). So the `Authority` and the discovered token endpoint must be https (http only on localhost), and the discovery document's `issuer` must be the `Authority`. An API that receives the access token validates that one as usual.
- **The access token** that `GetAccessTokenAsync` and the handler hand out is the provider's. Refresh, sign-out and `[RequiresSignIn]` work as with a Spine server; a refresh the provider refuses (`invalid_grant`) signs out.
- **Not in this mode:** `Server`, `AddApple`, `AddEmailPassword` and `AddOpenIdConnect`, which `UseSpine` refuses at startup, and `DeleteAccountAsync` and the password methods, which answer `Failed`. The push link needs a push server that accepts the provider's token.

## `ISpineAuth`

```csharp
public sealed partial class AccountViewModel(ISpineAuth auth) : ViewModelBase
{
    [RelayCommand]
    private async Task SignIn()
    {
        var result = await auth.SignInAsync();
        if (result.Status == SignInStatus.Failed) Error = result.Error;
    }
}
```

| Member | Does |
|---|---|
| `User`, `IsSignedIn`, `UserChanged` | Who is signed in. The stored session is read when the app starts, and `UserChanged` is raised once it is known, and after every sign-in and sign-out. |
| `SignInAsync()` | Apple's own sheet when Apple is the only way on an Apple platform; otherwise the sign-in sheet. Answers at once when already signed in. Two callers at once share one sheet. |
| `SignInAsync("apple")` | Apple's sheet directly, or the browser where there is no sheet. |
| `SignInAsync("google")` | The browser sign-in for a provider added with `AddOpenIdConnect`. |
| `Providers` | The ways to sign in, each with whether it is native (Apple's sheet) or in the browser. |
| `SignInWithPasswordAsync`, `SignUpAsync`, `VerifyEmailAsync`, `ResendCodeAsync`, `ForgotPasswordAsync`, `ResetPasswordAsync` | Email and password, for an app with its own sign-in page. |
| `GetAccessTokenAsync()` | An access token with at least a minute left, refreshed when needed. |
| `CreateHandler()` | The handler for an `HttpClient` built without `IHttpClientFactory`. |
| `SignOutAsync()` | Signs out here and revokes the refresh token on the server. |
| `DeleteAccountAsync()` | Deletes the account on the server, which revokes Apple's tokens and every session, and signs out. The App Store asks an app with sign-up to offer it. |
| `LinkAsync("google")`, `UnlinkAsync("google")` | Adds a provider to the signed-in user through its sheet or the browser, or removes one; see [One account, several ways in](#one-account-several-ways-in). |
| `CreateAccountAsync(pending)`, `LinkPendingAsync(pending)` | What becomes of a provider account the server asked about (`ChooseAccount`). The sign-in sheet calls them by itself. |
| `SignedIn`, `SignedOut` | Raised after a sign-in, and after a sign-out, a deletion or a refresh the server refused. A link or an unlink raises only `UserChanged`. |

Every step answers a `SignInResult`:
- `SignedIn`, with the user;
- `Canceled`;
- `CodeSent`: an email with a code is on its way;
- `Deleted`;
- `ChooseAccount`, with `Pending`: the provider account is new to a server that asks first (below);
- `Failed`, with `Error` set to the server's `SpineAuthErrors` value, such as `invalid_credentials`. When the server could not be reached or is not a Spine server, `Error` names the path and the HTTP status instead, so a wrong `Server` does not look like a wrong password.

## `[RequiresSignIn]`

```csharp
[NavigableRegion(Title = "Orders"), RequiresSignIn]
public partial class OrdersPage { … }

[RequiresSignIn]
[RelayCommand]
private Task Like() => …;
```

The guard runs before the page is created or the command runs. When nobody is signed in, the sign-in sheet opens first; once the user signs in, the navigation or the command goes on, with its parameter. Closing the sheet leaves the user where they were. It runs before `[RequiresUnlock]` (order 100 before 200).

On a command, it runs wherever Spine runs the command: a page action, a menu or action sheet row, `Tap.Command` and `{PageCommand}`. A plain `Command="{Binding LikeCommand}"` hands the command to the button directly and does not ask, so use `Command="{PageCommand Like}"`.

A guarded tab is covered by a "Sign in to see this." screen with a Sign in button while it waits.

## The sign-in sheet

`SignInSheet` is a `[NavigableSheet]` at medium and full height:
- one button per provider, in one shape (48 high, radius 8, the logo on the left, "Continue with …") with each brand's colours for the light and dark theme: Apple black or white, Google white or #131314 with its outline, Microsoft white or #2F2F2F;
- "Continue with email", which opens the email and password fields (they show at once when there are no providers);
- "Create account", "Forgot password?" and the step for the emailed code;
- "Is this your first time here?" after a provider account the server does not know (`AskBeforeNewAccount`): "Create a new account", or "I've signed in before", which shows the other ways and adds the new provider to the account the user signs in to.

The logos are SVGs in the package: Apple's drawn for Spine (Apple's downloadable artwork is licensed for mock-ups only), Google's G as Google ships it, Microsoft's four squares to its spec.

The server's errors are shown in the sheet, such as a wrong password or a code that is wrong or old, and the X closes it. The text is in English and Swedish (keys `Spine.Authentication.*`), and an app overrides it as any Spine string. `AllowSignUp` and `AllowReset` hide the links when the server does not allow them.

For an app's own design, `a.SignInPage<MySignInSheet>()` takes any `[NavigableSheet]` that returns a `SignInResult` (`INavigableWithResult<SignInResult>`) and signs in through `ISpineAuth`.

**`SignInButton`** is the sheet's button as a control of its own, for an app's sign-in page or "Link …" buttons:

```xml
<SignInButton Provider="google" Command="{Binding SignInCommand}" CommandParameter="google" />
<SignInButton Provider="microsoft" Text="Link Microsoft" Command="{Binding LinkCommand}" CommandParameter="microsoft" />
```

`Provider` picks the logo and colours: `apple`, `google`, `microsoft`, `password` (the password symbol), or anything else for the neutral outline without a logo. `Text` defaults to "Continue with …" ("Continue with email" for `password`). `Command`, `CommandParameter` and `Clicked` work as on a `Button`. The logos are in the package, so no icon package is needed.

## One account, several ways in

The server's `Id` is the user, whichever provider they sign in with. `User.Logins` lists the ways in: the provider, the email and name it gave, and when it was added.

```csharp
await auth.LinkAsync("google");    // Google's sheet or the browser; the same user after, with a new login
await auth.UnlinkAsync("google");  // never the last way in: Failed with last_login

foreach (var login in auth.User!.Logins!)
    Console.WriteLine($"{login.Provider} {login.Email} {login.LinkedAt}");
```

A provider account that already belongs to another user is refused with `identity_in_use`, and nothing is merged: the user signs in to that account and removes it there first.

**A first sign-in.** With `a.AskBeforeNewAccount = true` on the server, a provider account it does not know makes no user. The result is `ChooseAccount`, and the sign-in sheet asks:
- "Create a new account": `CreateAccountAsync(result.Pending)`;
- "I've signed in before": the user signs in another way, then `LinkPendingAsync(result.Pending)` adds the new provider to that account. When that sign-in is new too, the sheet says so and asks again.

An app with its own sign-in page handles `ChooseAccount` the same way. The question is valid for ten minutes. Linking needs a Spine server; without one the methods answer `Failed`.

## The user's details

`ISpineAuth.User` carries what the providers say about the user:

| | |
|---|---|
| `Id` | The server's own id: the same however the user signs in, unlike the providers' ids (Microsoft's `sub` differs per app). |
| `DisplayName`, `GivenName`, `FamilyName` | From the first sign-in: Apple's sheet (once only), Google's and Microsoft's tokens, the sign-up form. |
| `Email` | Only an address someone vouched for: an emailed code, Apple, Google, or a Microsoft work or school account whose domain the tenant verified. Personal Microsoft accounts get none. |
| `Organization` | A Microsoft work or school account's tenant id, or a Google Workspace domain; null for personal accounts. |
| `SignInMethods` | Every way the user can sign in to the account, such as `apple`, `google`, `password`. |
| `Logins` | The same ways with their details: the provider, the email and name it gave, when it was added. |
| `Provider` | How the user signed in this time. |

**The picture.** `GetPictureAsync()` gives the signed-in user's picture as bytes (JPEG or PNG), or null:
- Google's comes from `PictureUrl`, an address the server keeps;
- Microsoft's comes from Graph (`/me/photo`), with `User.Read`, which MSAL asks for at sign-in (`m.Picture`, on by default);
- Apple and email and password have none.

It is kept on the device for a day, and forgotten at sign-out.

```csharp
if (await auth.GetPictureAsync() is { } picture)
    Avatar.Source = ImageSource.FromStream(() => new MemoryStream(picture));
```

**Without a Spine server,** the session fills these from the provider's ID token (`UseIdentityProvider`) or from MSAL (`UseMicrosoft`). With MSAL, `Email` is the account's sign-in name, which is not verified.

## Tokens

- **The refresh token and the user** are kept in `SecureStorage` (the Keychain on Apple, the Keystore on Android), under `StorageKey`. The access token is kept in memory only.
- **Refresh.** An access token with less than a minute left is refreshed before it is handed out, one refresh at a time; callers that arrive meanwhile wait for the same one. This matters because the server treats a refresh token presented twice as stolen and signs that sign-in out everywhere.
- **A refusal signs out.** A refresh the server refuses (revoked, reused, expired) signs the user out and raises `SignedOut`. Offline or a server error keeps the session, and the call goes without a token.
- **The HTTP handler.** `AddSpineAuthentication()` on an `HttpClient` builder adds `Authorization: Bearer …` to every request. On a 401 it refreshes and sends the request once more. A request made while nobody is signed in goes without a token.

## Push

With `a.LinkPushNotifications = true` and `Plugin.Maui.Spine.PushNotifications` referenced, the device registers with the signed-in user's access token. It registers again on every sign-in and sign-out, as long as the app sets no `AuthorizationHeader` of its own. The server takes the user from the token (`SpinePushNotificationsOptions.UserId`), so `PushTarget.User(id)` reaches the devices the user is signed in on. The link is off by default (Q11).

## Sample

The Showcase's Sign-in page runs against the push sample server's `/auth` on `http://localhost:5100`, whose email codes are written to its log. It shows:
- signing in and out;
- a `[RequiresSignIn]` page (Members);
- a `[RequiresSignIn]` command that calls `/sample/me` through a client with `AddSpineAuthentication()`;
- deleting the account.

Passkeys are offered only in a build with `-p:SpinePasskeyDomain=spine-showcase-e41ddd.azurewebsites.net`, against the push sample server in Azure (`-p:SpineSampleServer=https://spine-showcase-e41ddd.azurewebsites.net/`), whose association file names the Showcase. The Sign-in page then has "Add a passkey".

Sign in with Apple is offered only in a build with `-p:SpineSignInWithApple=true`, which adds the entitlement; a profile for the Showcase's App ID must then have the capability, as `Spine Showcase Development` does. The simulator needs an Apple ID signed in under Settings. Google is offered only in a build with `-p:SpineGoogleServerClientId=<the Web client's id>`, because it needs a Google Cloud project: the push sample server reads `Auth:Google:WebClientId` and `Auth:Google:WebClientSecret`, and the Android client needs the debug key's SHA-1. Without Sign in with Apple, that build sets `AppleNotRequired`. The Showcase's callback scheme is `se.cosmomedia.spineshowcase`.

## Not verified yet

- Sign in with Apple's sheet on a device. It ran in the iOS simulator with a real Apple ID: the sheet, the server's check against Apple's keys, the names and the email. Apple's token revocation at account deletion needs the Sign in with Apple key on the server and is not run.
- Android, Windows and Mac Catalyst at run time: they are only compiled. The iOS simulator ran sign-up, the code, `[RequiresSignIn]` on a page and on a command, a call through the handler, sign-out, and a restart that kept the session.
- The browser sign-in ran in the iOS simulator against a local OpenID Connect stub, without a Spine server: cancel, sign-in, a restart that kept the session, a refresh and a revocation at sign-out. The brokered flow ran only in tests, against a real `Server.Authentication` broker with the browser stubbed. Not run yet: a real provider (Google, Apple through the broker, Auth0 or Entra), Custom Tabs and the callback activity on Android (the merged manifest was checked), and the Windows loopback.
- Sign in with Microsoft on Android, Windows (WAM) and Mac Catalyst, and with Microsoft Authenticator installed.
- Sign in with Google's Credential Manager on Android. The broker ran against the real Google in the iOS simulator: the sign-in, the code traded at Google, the picture, the names and the verified email.
- The push link with a real push token.
- Passkeys (#522) ran on the owner's iPhone and in the iPhone 17 simulator against the push sample server in Azure: a new account with a passkey, sign-out, sign-in with "Sign in with a passkey", and on the iPhone sign-in through AutoFill's suggestion above the email field. Not run yet: "Add a passkey" to an account and removing one on a device, and Mac Catalyst.
- Linking (#575) ran in the iOS simulator against the push sample server: a password account, Apple linked from the Sign-in page and unlinked, the last login refused, and after an unlink a new Apple sign-in through "Is this your first time here?" and "I've signed in before" with the password, ending with one account with both. Linking Google and Microsoft from the app ran only in tests, through the sheet's path and the browser broker.
