# Issue #581 — Showcase: host the sample server in Azure, and the server address as a build property

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/581
**Branch:** issue/581-showcase-server-in-azure
**Status:** In Progress

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

## Decisions

- 2026-10-16, owner: F1 (free); all AI providers, the paid ones for signed-in users only; push endpoints reach only the caller's own devices in Azure.
