# Plugin.Maui.Spine.Authentication.Microsoft

Sign in with Microsoft (personal accounts, work and school accounts) for a Spine app, through MSAL with the broker:
- Microsoft Authenticator or Company Portal on iOS and Android when one is installed, otherwise the system browser.
- WAM on Windows.
- Spine's browser layer on Mac Catalyst, for which MSAL has no build.

```bash
dotnet add package Plugin.Maui.Spine.Authentication.Microsoft
```

**Through the Spine server:** MSAL's ID token goes to `POST {Server}/microsoft/token`, like Apple's and Google's.

```csharp
builder.UseSpine(o => o.AddAuthentication(a =>
{
    a.Server = new Uri("https://api.example.com/auth");
    a.CallbackScheme = "se.example.app";          // Mac Catalyst
    a.AddApple();                                  // App Store Review Guideline 4.8
    a.AddMicrosoft(m => m.ClientId = "…");
}));
```

On the server: `Microsoft(o => { o.ClientId = "…"; o.Tenant = "common"; })`.

**Without a Spine server:** MSAL's own access token goes to an API that Entra protects.

```csharp
builder.UseSpine(o => o.AddAuthentication(a => a.UseMicrosoft(m =>
{
    m.ClientId = "…";
    m.Tenant = "contoso.onmicrosoft.com";
    m.Scopes.Add("api://orders/access_as_user");
})));
```

## Entra setup

1. **The app registration.** In the Entra admin center: App registrations → New registration. Choose the account types (`Tenant` follows: `common` for any account, `organizations`, `consumers`, or one tenant).
2. **Redirect URIs** under Authentication → Add a platform → "Mobile and desktop applications", one per platform:
   - **iOS:** `msauth.{bundle id}://auth`.
   - **Android:** `msauth://{package}/{signature hash}`. The hash is the base64 SHA-1 of the key that signs the app:
     ```bash
     keytool -exportcert -alias androiddebugkey -keystore ~/.android/debug.keystore -storepass android | openssl sha1 -binary | openssl base64
     ```
     A Play Store build uses Play App Signing's key. The package computes the hash at run time; Entra needs it registered.
   - **Windows with WAM:** `ms-appx-web://microsoft.aad.brokerplugin/{client id}`.
   - **Mac Catalyst:** `{CallbackScheme}://callback`.
3. **The app's Info.plist on iOS.** The build stops with this list when it is missing (`SpineMicrosoftCheckInfoPlist=false` skips the check):
   - `LSApplicationQueriesSchemes` with `msauthv2` and `msauthv3`;
   - a `CFBundleURLTypes` entry with the scheme `msauth.{bundle id}`.
4. **Android** needs no manifest entries; the package adds MSAL's activity and the `<queries>` for the brokers.
5. **With `UseMicrosoft`,** the API's scopes: an exposed API (`api://…/access_as_user`), or Microsoft Graph's `User.Read`.

`m.UseBroker = false` keeps to the system browser everywhere (and WAM off on Windows).

**App Store Review Guideline 4.8.** With `AddMicrosoft`, `UseSpine` throws on iOS and Mac Catalyst without `AddApple()` (or `AppleNotRequired`). `UseMicrosoft` is not checked: it suits apps whose users sign in with their organization's account, which 4.8 exempts. An App Store app that offers personal Microsoft accounts (`Tenant = "common"` or `"consumers"`) without a Spine server has to answer 4.8 itself.

Docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/authentication.md#sign-in-with-microsoft
