# Issue #581 — Showcase: host the sample server in Azure, and the server address as a build property

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/581
**Branch:** issue/581-showcase-server-in-azure
**Status:** In review

The Showcase's server address is hard-coded to `http://localhost:5100`. On an iPhone that is the phone itself, so sign-in, the AI pages and the server calls do nothing there. Host the push sample server in Azure, so the Showcase works on a phone, and on other people's, without the Mac running.

Asked for by the owner on 2026-10-16. The owner chose:
- App Service on Linux at **F1** (free). It sleeps after about 20 minutes, so the first call after a pause wakes it.
- **All AI providers** (demo, OpenAI and Anthropic), with the paid ones for **signed-in users only** and Server.AI's daily caps.
- The push endpoints reach **only the caller's own devices** when the server runs in Azure.

## Plan

1. **The app.** The server address becomes a build property, `-p:SpineSampleServer=https://…`, read like the other build settings. The default stays `http://localhost:5100/`. It is used for `/auth`, `SampleApi`, `/ai` and Voice's session endpoint.
2. **The server, safe to host** (`Sample:Hosted=true` in Azure):
   - rate limiting on `/auth` per IP;
   - auth users in Azure Table storage (`UseAzureTableStore`) when a connection string is set, and a persistent signing key from the settings;
   - `/send`, `/installations` and `/channels` need a signed-in user, and reach and show only that user's devices (`user:<id>`);
   - the paid AI features are for signed-in users only (Voice's `Callers: Any` is the local default only);
   - the AI usage report stays loopback-only;
   - local runs on the Mac work as they do today.
3. **Azure** (subscription "Visual Studio Enterprise-prenumeration", Sweden Central): a resource group, a Linux F1 plan, a web app (HTTPS only, WebSockets on) and a storage account for Tables. The secrets go into the app's settings from user-secrets without being printed: APNs, FCM, the Google secret, the OpenAI and Anthropic keys, and a new signing key. Deploy with `dotnet publish` and `az webapp deploy`.
4. **Google:** add `https://<app>/auth/google/callback` to the Web client's redirect URIs (owner's OK).
5. **Verify on the iPhone:** email and password (the code from the log stream), Apple, Google, Microsoft, linking, the AI pages and Voice.
6. **Docs:** the server's README and wiki, how to deploy and the build property, and the plan's verification backlog.

## Open Questions

## Changes

- **The app:** `SpineSampleServer`, a build property (default `http://localhost:5100/`) read like the other build settings, used for `/auth`, `SampleApi`, `/ai` and Voice. The Sign-in page's summary mentions it.
- **The server,** with `Sample:Hosted` in `appsettings.Production.json`:
  - `/auth` is rate limited per IP;
  - users go to Table storage when `Auth:Storage` is set;
  - `/send`, `/installations` and `/channels` need a signed-in user and reach and list only that user's devices (`PushTarget.User`), and broadcasts are refused;
  - Story, Quick, Picture and Voice answer signed-in users only.
  
  Locally nothing changes.
- **Azure:**
  - resource group `rg-spine-showcase` (Sweden Central), Linux plan `plan-spine-showcase` (F1), web app `spine-showcase-e41ddd` (.NET 10, HTTPS only), storage account `stspineshowcasee41ddd`;
  - `Microsoft.Storage` registered on the subscription;
  - 15 app settings written from user-secrets and `~/.config/openai.env` through a temporary file, none printed;
  - a new signing key for Azure;
  - container logging on.
- **Google:** the Web client's redirect URIs are `http://localhost:5100/auth/google/callback` and `https://spine-showcase-e41ddd.azurewebsites.net/auth/google/callback`.
- **Found testing on the phone:**
  - The Sign-in page's Unlink asks first: an action sheet with a destructive "Unlink …". The owner found Google unlinked without remembering a tap; the server log showed the `DELETE /auth/link/google` call. A swipe starting on the icon did not reproduce it in the simulator.
  - Request logging is on in Azure (`Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics`), to see it if it happens again.
  - `SignInButton`'s Microsoft text is #1F1F1F, as Google's, instead of Microsoft's #5E5E5E, which looked disabled.
- **`README.md`** for the server: running locally, the settings, deploying, the log.

## Verification

- **Locally, Production with `Sample:Hosted`:** `/installations`, `/send` and `/channels` answer 401 without a sign-in, `/auth` answers 429 after 30 calls a minute, and Voice is off without a key.
- **In Azure:**
  - `/auth/nonce`, the JWKS, and `/google/start` redirecting with the Azure callback;
  - Table storage has one user with the Google and Apple logins after the owner's sign-ins.
- **The owner's iPhone,** a Release build with `SpineSampleServer` set to Azure:
  - Google and Apple sign-in and linking;
  - Microsoft through Authenticator, after #582.
- **Not run:**
  - email and password against Azure;
  - the AI pages and Voice against Azure;
  - push from Azure (the Showcase does not register for push).
- **Story is off in Azure:** there is no Anthropic key anywhere on the Mac. Add `Ai__Providers__anthropic__ApiKey` to turn it on.

## Decisions

- 2026-10-16, owner: F1 (free); all AI providers, the paid ones for signed-in users only; push endpoints reach only the caller's own devices in Azure.
- 2026-10-16: WNS settings were not copied to Azure. **Why:** there is no Windows client yet, and the WNS secret is per machine.
- 2026-10-16: the Azure server keeps the push register and AI usage in memory. **Why:** F1 sleeps, and apps register again at start. Accounts are what must survive, and they are in Table storage.
