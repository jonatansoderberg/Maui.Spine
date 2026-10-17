# Plugin.Maui.Spine.Authentication.Passkeys

Passkeys for Spine sign-in, through the platform's own passkey sheet:
- "Sign in with a passkey" and "Create account with a passkey" in the sign-in sheet.
- Passkeys offered by AutoFill above the sheet's email field (iOS).
- "Add a passkey" to a signed-in account: `LinkAsync("passkey")`.

iOS and Mac Catalyst 16 or later. Android and Windows are not there yet; the sheet leaves passkeys out there.

```bash
dotnet add package Plugin.Maui.Spine.Authentication.Passkeys
```

```csharp
builder.UseSpine(o => o.AddAuthentication(a =>
{
    a.Server = new Uri("https://api.example.com/auth");
    a.AddApple();
    a.AddPasskeys();
}));
```

```xml
<SpinePasskeyDomain>api.example.com</SpinePasskeyDomain>
```

`SpinePasskeyDomain` adds the Associated Domains entitlement (`webcredentials:api.example.com`). The App ID needs the Associated Domains capability, then a provisioning profile made again.

On the server, `Plugin.Maui.Spine.Server.Authentication` checks the passkeys and names the app in `apple-app-site-association`:

```csharp
a.Passkeys(p =>
{
    p.RelyingPartyId = "api.example.com";
    p.AppleAppIds.Add("TEAMID.se.example.app");
});

app.MapSpinePasskeyAssociations();
```

See [Passkeys](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/authentication.md#passkeys) in the wiki.
