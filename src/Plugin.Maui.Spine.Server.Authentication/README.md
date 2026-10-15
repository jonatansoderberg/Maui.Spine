# Plugin.Maui.Spine.Server.Authentication

Sign-in on the server for Spine apps. The platform's own sign-in sheet hands the app an ID token. The server checks it against the provider's keys, issuer, audience and nonce, then issues the app's own short-lived ES256 access token and a rotating refresh token. Email and password come with verification codes, lockout and reset. Deleting an account revokes Apple's tokens. It runs on ASP.NET Core and in an Azure Functions isolated worker.

```bash
dotnet add package Plugin.Maui.Spine.Server.Authentication
```

```csharp
builder.Services.AddSingleton<ISpineAuthEmailSender, MyEmailSender>();
builder.Services.AddSpineAuthentication(a =>
{
    a.Issuer = "https://api.example.com";
    a.SigningKey = builder.Configuration["Auth:SigningKey"];   // EC P-256, PKCS#8 PEM
    a.Apple(o => o.BundleIds.Add("se.example.app"));
    a.Google(o => o.ClientIds.Add("….apps.googleusercontent.com"));
    a.EmailPassword();
    a.UseAzureTableStore(builder.Configuration["Storage"]);   // or UseInMemoryStore(); UseUsers(...) for your own users
});

app.UseAuthentication();
app.MapSpineAuthentication("/auth");   // nonce, {provider}/token, token, revoke, account, password/{action}, jwks
app.MapGet("/orders", (ClaimsPrincipal user) => …).RequireAuthorization();
```

- A refresh token works once. Presenting it again revokes that sign-in on every device.
- `o.UserId = request => request.GetSpineUserIdAsync()` in `AddSpinePushNotifications` tags a device `user:<id>` from the token, never from the body.

## Documentation

- [Sign-in on the server](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/authentication-server.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
