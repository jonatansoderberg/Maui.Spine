# Plugin.Maui.Spine.Authentication

Sign-in for a Spine app against its own server, which runs `Plugin.Maui.Spine.Server.Authentication`:
- Sign in with Apple's own sheet on iOS and Mac Catalyst, and email and password in a ready-made sheet.
- Any OpenID Connect provider the server brokers, in the system browser (Apple on Android and Windows too).
- `[RequiresSignIn]` on a page or a command.
- The refresh token in secure storage, and an `HttpClient` handler that adds the access token and refreshes it.
- Without a Spine server, directly at an OpenID Connect provider (Auth0, Entra ID, Keycloak) with PKCE.

```bash
dotnet add package Plugin.Maui.Spine.Authentication
```

```csharp
builder.UseSpine(o => o.AddAuthentication(a =>
{
    a.Server = new Uri("https://api.example.com/auth");
    a.CallbackScheme = "se.example.app";
    a.AddApple();
    a.AddEmailPassword();
}));

builder.Services.AddHttpClient<OrdersApi>(c => c.BaseAddress = new("https://api.example.com"))
                .AddSpineAuthentication();
```

```csharp
[NavigableRegion(Title = "Orders"), RequiresSignIn]
public partial class OrdersPage { … }
```

Sign in with Google comes from `Plugin.Maui.Spine.Authentication.Google`.

Docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/authentication.md
