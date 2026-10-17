# The push sample server

The server behind the Spine Showcase and the push sample app. It covers:
- push (`Plugin.Maui.Spine.Server`);
- sign-in (`Plugin.Maui.Spine.Server.Authentication`);
- the AI features (`Plugin.Maui.Spine.Server.AI`);
- the remote widget.

It runs on the Mac for the simulator, and in Azure for a phone.

## On the Mac

```bash
dotnet run --launch-profile http
```

It listens on `http://localhost:5100`, which is the Showcase's default (`SpineSampleServer`). Android reaches it through `adb reverse tcp:5100 tcp:5100`. Credentials come from user secrets (`UserSecretsId` `maui-spine-push-sample-server`), never the repo:
- **Push:** `Push:Apple:TeamId`, `KeyId`, `PrivateKey`, `BundleId`; `Push:Fcm:ServiceAccount`; `Push:Wns:*`.
- **Sign-in:** `Auth:Google:WebClientId`, `Auth:Google:WebClientSecret`; `Auth:Microsoft:ClientId`; `Auth:Apple:*` for Apple's browser sign-in.
- **AI:** `Ai:Providers:openai:ApiKey`, `Ai:Providers:anthropic:ApiKey`.

A section left empty turns that platform or feature off; the startup log says which. Locally, everything is open: anyone may send a push to any device, and email codes are written to the log.

## In Azure

The Showcase on a phone talks to `https://spine-showcase-e41ddd.azurewebsites.net`, set up in #581:
- **Hosting:** App Service on Linux, an F1 plan (free; it sleeps after about 20 minutes, so the first call after a pause wakes it).
- **Place:** resource group `rg-spine-showcase` in Sweden Central, in the owner's Visual Studio Enterprise subscription.
- **Storage:** an account for Table storage.

```bash
dotnet build ../MauiSpineSampleApp -c Release -f net10.0-ios -r ios-arm64 \
  -p:SpineSampleServer=https://spine-showcase-e41ddd.azurewebsites.net/ …
```

`appsettings.Production.json` sets `Sample:Hosted`. With it:
- **Sign-in:** `/auth` is rate limited per IP (30 a minute).
- **Accounts:** users are kept in Table storage (`Auth:Storage`), so they survive a restart.
- **Passkeys:** `Auth:Passkeys:RelyingPartyId` is the Azure host, and `/.well-known/apple-app-site-association` names the Showcase (`<team id>.se.cosmomedia.spineshowcase`, the team id from `Auth:Apple:TeamId` or `Push:Apple:TeamId`). A Showcase built with `-p:SpinePasskeyDomain=spine-showcase-e41ddd.azurewebsites.net` uses them.
- **Push:** `/send`, `/installations` and `/channels` need a signed-in user, and reach and list only that user's devices. Broadcasts are not sent.
- **AI:** the paid features (Story, Quick, Picture, Voice) answer signed-in users only, within their daily caps. Assistant and Summary use the free demo provider.
- **Avatars:** `GET /sample/avatars` lists the avatars it hands out (id, version from the package's manifest, file, size, SHA-256), and `GET /sample/avatars/kairo.spineavatar` serves Kairo (9.8 MB, version 3.0.0) to the Showcase's Avatars page, which keeps it and updates it when the list names a later version. The file is published with the server (linked from `samples/Avatars`, the repository's one copy of every avatar); nothing else is served there.

The push register and the AI usage stay in memory, so they reset when the server sleeps. Apps register again when they start.

### Settings

App settings, where `:` becomes `__`:
- `Sample__Hosted`
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, so the rate limit sees the caller's address
- `Auth__Issuer` and `Auth__PublicUrl` (the app's https address, plus `/auth`)
- `Auth__SigningKey` (a P-256 PKCS#8 PEM made for Azure, not the Mac's)
- `Auth__Storage` (the storage account's connection string)
- the push, sign-in and AI keys above

Set them from a file, so that no value is printed:

```bash
az webapp config appsettings set -g rg-spine-showcase -n spine-showcase-e41ddd --settings @settings.json -o none
```

Google's Web client lists both redirect URIs, `http://localhost:5100/auth/google/callback` and `https://spine-showcase-e41ddd.azurewebsites.net/auth/google/callback`, so Google sign-in works against both.

### Deploy

```bash
dotnet publish -c Release -o out
(cd out && zip -qr ../server.zip .)
az webapp deploy -g rg-spine-showcase -n spine-showcase-e41ddd --src-path server.zip --type zip
```

The log, email codes included:

```bash
az webapp log tail -g rg-spine-showcase -n spine-showcase-e41ddd
```
