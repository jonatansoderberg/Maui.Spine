# Plugin.Maui.Spine.Authentication.Google

Sign in with Google for a Spine app, on top of `Plugin.Maui.Spine.Authentication`:
- **Android:** Credential Manager's own sheet. The ID token carries the server's nonce and goes to `POST {Server}/google/token`.
- **iOS, Mac Catalyst, Windows:** the server's broker in the system browser.

```bash
dotnet add package Plugin.Maui.Spine.Authentication.Google
```

```csharp
builder.UseSpine(o => o.AddAuthentication(a =>
{
    a.Server = new Uri("https://api.example.com/auth");
    a.CallbackScheme = "se.example.app";
    a.AddApple();                                                  // App Store Review Guideline 4.8
    a.AddGoogle(g => g.ServerClientId = "….apps.googleusercontent.com");
}));
```

## Google Cloud setup

In the Google Cloud console, under APIs & Services → Credentials (with an OAuth consent screen):

1. **A Web application client.**
   - Its id is the app's `ServerClientId`, and the server's `Google(o => { o.ClientIds.Add(id); o.WebClientId = id; o.WebClientSecret = …; })`.
   - Its redirect URI is the server's `{PublicUrl}/google/callback`, for the broker.
2. **An Android client.**
   - It takes the app's package name and the SHA-1 of the key that signs it.
   - For debug builds that key is `~/.android/debug.keystore` on a Mac (`keytool -list -v -keystore ~/.android/debug.keystore -alias androiddebugkey -storepass android`).
   - A Play Store build is signed by Play: use Play App Signing's SHA-1.
   - Without a matching Android client, Credential Manager answers with an error, not a sheet.
3. **The app** needs `<SpineAuthenticationCallbackScheme>` in its project and `a.CallbackScheme` for the browser on the other platforms.

Credential Manager needs Android 6.0 and Google Play services. The emulator needs a Google Play image with a signed-in Google account.

Docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/authentication.md#sign-in-with-google
