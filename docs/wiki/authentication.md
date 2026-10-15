# Sign-in in the app

```bash
dotnet add package Plugin.Maui.Spine.Authentication
```

`Plugin.Maui.Spine.Authentication` signs the user in against the app's own server, which runs [`Plugin.Maui.Spine.Server.Authentication`](authentication-server.md):
- Sign in with Apple's own sheet on iOS and Mac Catalyst, and email and password in a ready-made sheet.
- `[RequiresSignIn]` on a page or a command.
- The refresh token in secure storage.
- An `HttpClient` handler that adds the access token and refreshes it.

The design is in [the proposal](../proposals/spine-authentication.md), §5.1–§5.3. Browser sign-in (Apple on Android and Windows, Google, any OpenID Connect provider) and a mode without a Spine server come with #557.

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
| `SignInAsync("apple")` | Apple's sheet directly. |
| `SignInWithPasswordAsync`, `SignUpAsync`, `VerifyEmailAsync`, `ResendCodeAsync`, `ForgotPasswordAsync`, `ResetPasswordAsync` | Email and password, for an app with its own sign-in page. |
| `GetAccessTokenAsync()` | An access token with at least a minute left, refreshed when needed. |
| `CreateHandler()` | The handler for an `HttpClient` built without `IHttpClientFactory`. |
| `SignOutAsync()` | Signs out here and revokes the refresh token on the server. |
| `DeleteAccountAsync()` | Deletes the account on the server, which revokes Apple's tokens and every session, and signs out. The App Store asks an app with sign-up to offer it. |
| `SignedIn`, `SignedOut` | Raised after a sign-in, and after a sign-out, a deletion or a refresh the server refused. |

Every step answers a `SignInResult`:
- `SignedIn`, with the user;
- `Canceled`;
- `CodeSent`: an email with a code is on its way;
- `Deleted`;
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
- Sign in with Apple's button (on Apple platforms, when `AddApple()`);
- email and password;
- "Create account", "Forgot password?" and the step for the emailed code.

The server's errors are shown in the sheet, such as a wrong password or a code that is wrong or old, and the X closes it. The text is in English and Swedish (keys `Spine.Authentication.*`), and an app overrides it as any Spine string. `AllowSignUp` and `AllowReset` hide the links when the server does not allow them.

For an app's own design, `a.SignInPage<MySignInSheet>()` takes any `[NavigableSheet]` that returns a `SignInResult` (`INavigableWithResult<SignInResult>`) and signs in through `ISpineAuth`.

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

Sign in with Apple is offered only in a build with `-p:SpineSignInWithApple=true`, because the Showcase's App ID does not have the capability yet.

## Not verified yet

- Sign in with Apple's sheet on a device, which needs the capability on the App ID.
- Android, Windows and Mac Catalyst at run time: they are only compiled. The iOS simulator ran sign-up, the code, `[RequiresSignIn]` on a page and on a command, a call through the handler, sign-out, and a restart that kept the session.
- The push link with a real push token.
