# Selling through the stores

[Purchases](purchases.md) describes the code. This page is the rest: App Store Connect, Google Play, RevenueCat and App Review, from an empty account to an approved version. It comes from Hockeymatch (one-time purchases and credits, both stores) and Glukos (a subscription, iOS only), both set up in November 2026. Most of what took time was here, not in the app.

The order that works:

1. [The accounts](#the-accounts): agreements, bank, tax, on both stores. Nothing sells before.
2. [The products](#the-products) in App Store Connect and Play Console, with the ids on `[StoreProduct]`.
3. [RevenueCat](#revenuecat): the project, the apps, the keys, the entitlements, the store notifications and the webhook.
4. [The app](#the-app): the public SDK keys, terms and privacy links that open from anywhere.
5. [Trying it](#trying-it) in the sandbox and with a license tester, until a purchase reaches the server's ledger.
6. [App Review](#app-review): the purchases go in with a version, through the web.

## The accounts

**App Store Connect → Business:**

- the **Paid Apps Agreement** must be *Active*. Until then sandbox purchases fail too;
- a **bank account**, and the **tax forms** (W-8BEN outside the US, with the certificate of foreign status);
- the agreements end with the Apple Developer Program membership. A membership that is not renewed stops the sales on its renewal date.

**Google Play Console → Manage account → Payments profile:**

- a payments profile (individual or organisation), with a **bank account verified by a micro-deposit**: Google sends a small amount, under 0.35 of the currency, within a few days, and its exact amount is entered under *Payments profile → Verify*. Until then the profile says "Your bank account hasn't been verified";
- the US tax info.
- Health apps need an **organisation** account on Play. Glukos was rejected twice on a personal account and sells on iOS only.

**The Small Business Program** (Apple's 15 % instead of 30 %) is applied for separately. When it is approved, switch it on in each RevenueCat App Store app (*Apple Small Business Program*), so RevenueCat's revenue figures use 15 %.

## The products

The ids are the ones on `[StoreProduct]`, and every product needs a name, a price and availability in each store.

**App Store Connect** (the app → *In-App Purchases*, or *Subscriptions* for a subscription group):

- **Through the API** they can all be made and changed: `inAppPurchasesV2`, `inAppPurchaseLocalizations`, `inAppPurchasePriceSchedules`, `inAppPurchaseAvailabilities`; for subscriptions `subscriptionGroups`, `subscriptions`, `subscriptionPrices` (every territory, equalised from the base), `subscriptionIntroductoryOffers` for a free trial. An Admin API key (`.p8`, key id, issuer id) signs an ES256 JWT. A script that finds what exists and makes only what is missing can run again after every change.
- **A review screenshot** of the paywall is required for each product; a review note on what it unlocks and where helps the reviewer. Without the screenshot a product stays *Missing Metadata* and cannot be submitted. Upload it with `inAppPurchaseAppStoreReviewScreenshots` (or `subscriptionAppStoreReviewScreenshots`): reserve, `PUT` the parts to the upload operations, then `PATCH` with `uploaded: true` and the MD5.
- **Take the screenshot on a phone** signed in to a sandbox account of the right storefront. The simulator's sandbox storefront is the US one, so its paywall shows dollars. `xcrun devicectl device capture screenshot --device <udid> --destination paywall.png` captures a phone over Wi-Fi.
- **The product's state** goes *Missing Metadata* → *Ready to Submit* once the screenshot, the localisation and the price are there.
- **The app's own price** (free, base country) and **availability** must also be set before a version can be submitted: `appPriceSchedules` and `/v2/appAvailabilities`. Hockeymatch had neither until the submission asked for them.

**Play Console** (the app → *Monetize with Play*):

- one-time products and consumables under *One-time products*, each with a purchase option; subscriptions under *Subscriptions* with base plans (`pro:month` on `[StoreProduct]` is the subscription `pro` and its base plan `month`);
- a price entered in a VAT country is the price before VAT: 79 kr in Sweden is entered as 63.20;
- products can only be made after an AAB with the `BILLING` permission is on a track. Plugin.RevenueCat brings the permission.

## RevenueCat

One RevenueCat **project per app**, with an **App Store app** and a **Play Store app** in it.

**The App Store app:**

- **In-App Purchase key** (App Store Connect → Users and Access → Integrations → In-App Purchase): required. With StoreKit 2 (Purchases 5 and later) transactions are not recorded without it. One key serves every app of the team. RevenueCat says "Valid credentials" when it works.
- **Apple Server Notification URL**: copy it from the app (*Reveal*) into App Store Connect → the app → App Information → *App Store Server Notifications*, as both the **Production** and the **Sandbox** URL, **Version 2**. Without it RevenueCat learns of renewals, cancellations and refunds late, or only when the app opens. The URL holds a secret: paste it by hand, never through a script or a log. RevenueCat says "No notifications received" until the first event after that.
- *App Store Connect API* key: optional, for importing products. Spine asks for the products by id, so it is not needed.

**The Play Store app:**

- **Service account credentials**: a Google Cloud service account with the Pub/Sub Admin and Monitoring Viewer roles and the Play Android Developer, Developer Reporting and Pub/Sub APIs on, invited in Play Console (Users and permissions) with *View app information*, *View financial data* and *Manage orders and subscriptions*. One account serves every app on the Play account. RevenueCat's check failed for about an hour after the invitation, until Google's permissions had spread.
- **Google developer notifications**: RevenueCat makes the Pub/Sub topic (*Connect to Google*); Play Console → *Monetization setup* → Real-time developer notifications points at it. Send a test notification from there; RevenueCat shows "Last received".

**Products and entitlements:** import or add the products in each app. Make **one entitlement per member of the purchase enum**, its identifier the member's `[PurchaseId]` (or its name), and attach the products whose `[Grants]` give it. Consumables have no entitlement. **No offerings are needed**: Spine asks the store for the products by id.

**The webhook to the app's server** (project → Integrations → Webhooks): `https://…/purchases/revenuecat`, with an Authorization value that the server has as `RevenueCatWebhookAuthorization`. One webhook covers both stores. Keep the value in a key vault; RevenueCat's *Send test event* must get a 200.

**The public SDK keys** (`appl_…`, `goog_…`) are under API keys. They are public and go in the app's build; the secret keys (`sk_…`) never do.

## The app

- **The keys** in the build: `r.AppleApiKey` / `r.GoogleApiKey`. Hockeymatch reads them from `appsettings.json`, Glukos from MSBuild properties. Without them an app can fall back to the test store, which charges nothing; only `AllowTestStoreInRelease` lets that through in a Release build.
- **Terms and privacy must be public pages.** A link into a private repository is a rejection. Apple's standard licence (`https://www.apple.com/legal/internet-services/itunes/dev/stdeula/`) is the terms on iOS when the app has no terms of its own; App Store Connect's *License Agreement* then stays Apple's standard one. Spine requires `Terms` on every platform, and Apple's licence does not fit a Play build, so Hockeymatch links Google Play's terms of service there:

  ```csharp
  #if ANDROID
      .Terms(new Uri("https://play.google.com/about/play-terms/"))
  #else
      .Terms(new Uri("https://www.apple.com/legal/internet-services/itunes/dev/stdeula/"))
  #endif
  ```
- **The RevenueCat app user id** is the Spine user or installation (`user:{id}` / `installation:{id}`), the same key the server's ledger uses, so a webhook event lands with the right caller. An event for RevenueCat's anonymous id is skipped.

## Trying it

**Apple:**

- a **sandbox account** (App Store Connect → Users and Access → Sandbox), signed in on the phone under **Settings → Developer → Sandbox Apple Account** on iOS 26, not under App Store;
- a sandbox subscription renews every few minutes (a month in 5 minutes), up to 12 times, so a renewal reaches the webhook within minutes;
- **the simulator** shows the products and prices through RevenueCat without a StoreKit configuration file, from the US sandbox storefront. That is enough for a layout and for a review screenshot in dollars; buy on a phone;
- TestFlight builds buy in the sandbox too, without being charged.

**Google:**

- **license testers** (Play Console → Manage account → License testing, an email list) buy without being charged, on a build installed from a test track (internal testing is enough).;
- an emulator works when it has the Play Store and is signed in as a tester.

**Done when** a purchase on each store shows in RevenueCat (sandbox data) *and* reaches the server: a `txn:` row (consumable) or an owned product in the ledger. Only then turn on anything that checks the ledger, such as paid push tags (`AllowTagsAsync`). A build still on the test store sells without the server hearing of it.

## App Review

**The first purchases of an app go in with an app version**, and only through the web:

1. On the version's page, **Add for Review**. This makes a draft submission with the version in it.
2. On each in-app purchase's page, and each subscription's, **Add for Review** → the draft submission. For subscriptions, add the **subscription group** as well (its page has the same button); without it the draft says "Your auto-renewable subscription must be submitted with its subscription group".
3. **Submit for Review** in the draft. Everything turns *Waiting for Review*.

The API refuses this: `inAppPurchaseSubmissions` answers `STATE_ERROR.FIRST_CONSUMABLE_MUST_BE_SUBMITTED_ON_VERSION` (or the non-consumable and subscription equivalents), and `reviewSubmissionItems` takes no purchases. Later purchases can go through the API on their own.

**Before the version can be submitted:**

- **App Privacy** (no API): an app on Spine's purchases collects at least *Purchases → Purchase History*, and *Identifiers → Device ID* when the installation id is RevenueCat's user id or the push installation. Both for App Functionality, not for tracking. Whether they are "linked to the user" is the developer's call; they are tied together by the installation id.
- **Age rating**, including the questions added in 2026 (`sharedLocation`, `datingAndRelationships`: booleans in `ageRatingDeclarations`).
- **Content rights** (`contentRightsDeclaration` on the app) when the app shows other people's content.
- **China mainland** asks new apps for a security assessment and an AIGC declaration; leave it out of availability unless the app is meant for it.
- **The review notes** say what each purchase unlocks, where it is in the app, and how to try it without paying (Glukos: "Plus can be bought and restored in the sandbox with any sandbox account").

**Play:** a closed test with testers for 14 days, then production access; each closed release is reviewed. Products need no review of their own.

## See also

- [Purchases](purchases.md): the code, the paywall, credits and the server
- [Releasing](releasing.md)
- The `spine-purchases` agent skill (`.claude/skills/spine-purchases`)
