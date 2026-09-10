# Issue #231 — Spine.Push: broadcast-kanaler för Live Activities (iOS 18)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/231
**Branch:** issue/231-broadcast-kanaler
**Status:** Completed

## Plan

En kanal låter servern uppdatera en Live Activity hos alla som följer något — "alla som följer tävling X" — med en push i stället för en per aktivitet och token. APNs sprider den. Android saknar kanaler; där blir motsvarigheten ett FCM-topic (Jonatans val), så att samma anrop når båda.

**Common**
- `LiveActivity.Channel` och `ILiveActivityService.StartAsync(…, channel)`.
- `LiveActivityChannels.Topic(channel)`: FCM-topicet en kanal motsvarar. APNs kanal-id är base64, som FCM inte tillåter i topicnamn, så det skrivs om till base64url med prefix. Server och app använder samma funktion.
- `PushKeys.ActivityChannel`: kanalen i ett FCM-meddelande som startar en aktivitet.

**Widgets (klient)**
- iOS: bryggan startar med `pushType: .channel(id)` på iOS 18, annars som förut. En kanalaktivitet får ingen egen token.
- Kanalen sparas per aktivitet (egen nyckel bredvid id → kind), så att Android kan prenumerera om efter omstart.

**Push (klient)**
- Android: topic-prenumerationerna följer de aktiva aktiviteternas kanaler — prenumerera när en tillkommer, avsluta när den sista på kanalen tar slut. `IPushPlatform.SyncLiveActivityChannelsAsync`, anropad vid `ActivitiesChanged`.
- Ett FCM-start med `spine.activity-channel` startar aktiviteten på kanalen.

**Server**
- `IPushChannels` (Apple): skapa, lista, ta bort mot `api-manage-broadcast[.sandbox].push.apple.com:2196/2195`, `/1/apps/<bundle>/channels` och `/all-channels`. Lagringspolicy vid skapande: ingen, eller senaste meddelandet.
- `IPushSender.BroadcastLiveActivityAsync(channel, kind, layout, event, options)`: APNs `POST /4/broadcasts/apps/<bundle>` med `apns-channel-id`, FCM till topicet. Ett resultat per plattform, med kanalen i stället för installations-id; ingenting raderas ur registret.
- `LiveActivityOptions.Channel`: en push-to-start får `input-push-channel`, och FCM-start får `spine.activity-channel`.
- Miljö: kanaler finns per APNs-miljö och en broadcast har ingen installation att läsa miljön ur, så den tas ur `ApplePushOptions.Environment`, eller anges i anropet när den är `PerInstallation`.
- Tester för transporter, payloads och avsändare.

**Samplet och wikin**
- Sample-servern: skapa kanal och broadcasta; samplet: starta Live Activity på en kanal.
- `push-server.md`: kanaler, när kanal är rätt och när per-enhet-token är det; `widgets.md`: `StartAsync(channel:)`; raderna under *Not in v1* bort.

**Verifiering:** Broadcast-kapabiliteten på App ID:t (Jonatan loggar in i portalen, jag slår på den), sedan skapa kanal → starta på kanalen i simulatorn → broadcast via 5100-servern. Android: emulator, topic-meddelande.

## Open Questions

Inga.

## Changes

- Common: `LiveActivityChannels.Topic(channel)` (base64 → base64url med prefixet `spine-la-`, och ett tecken inget topic kan bära kastar), `PushKeys.ActivityChannel`, `LiveActivity.Channel` och `ILiveActivityService.StartAsync(…, channel)`.
- Server: `IPushChannels`/`ApnsChannels` (skapa, lista, ta bort; `PushChannelException` med status, orsak och sökväg), `IPushBroadcastTransport` som `ApnsTransport` (`/4/broadcasts/apps/<bundle>`, `apns-expiration` alltid satt) och `FcmTransport` (meddelande till topicet) implementerar, `IPushSender.BroadcastLiveActivityAsync`, `LiveActivityOptions.Channel` (`input-push-channel` på APNs-start, `spine.activity-channel` i varje FCM-meddelande), `PushEnvelope.ApnsEnvironment`, `ApplePushOptions.ChannelEnvironment`, `IPushChannels` i DI när Apple är konfigurerat.
- Widgets: kanalen genom `IWidgetPlatform.StartActivityAsync` till bryggan (`pushType: .channel(id)` på iOS 18, loggat och ignorerat före), och kanalminnet `spine.widgets.activity-channels` bredvid id → kind.
- Push: `IPushPlatform.FollowChannelsAsync`. Android prenumererar på och avslutar FCM-topics efter de aktiva aktiviteternas kanaler och sparar vad det följer; iOS gör ingenting. `PushService` anropar den vid `ActivitiesChanged`, och en FCM-start med `spine.activity-channel` startar på kanalen.
- Tester: kanalhanteringen (värdar och portar, sökvägar, policyn, huvudet, avslag, miljön), broadcast i båda transporterna, payloads, avsändaren och topicnamnet. 189 servertester gröna.
- Samplet: sample-servern har `POST /channels` (en kanal per körning, i sandlådan) och `kind: broadcast`; Live Activity-sidan har "Starta på kanal" och "Broadcast från servern".
- Wikin: `push-server.md` *Broadcast channels*, med när kanal är rätt och när token är det; `widgets.md` hänvisar dit; raderna under *Not in v1* borta.

## Verifiering

**Android (Pixel 10 Pro-emulator, API 37, riktig FCM via sample-servern på 5100):**
- `kind: liveactivity` till installationen med `broadcastChannel: test-231` → `Sent`; Live Update-notisen `spine-widgets:sample` postad 17:38:28.
- `kind: broadcast` till `test-231` → Apple `Failed BadChannelId` (inget APNs-id), Android `Sent`. Notisen uppdaterades 17:39:33 och visade "Broadcast via topic · 17:39:33" — enheten följde topicet `spine-la-test-231`, som den bara prenumererar på genom en aktivitet på kanalen.
- Live Activity-sidan visade "kör, id spine-widgets:sample" och "Adress: kanal test-231" — kanalen följde med push-starten in i `LiveActivity.Channel`.
- **Avslut lämnar topicet:** "Avsluta" → 0 Live Updates. En ny broadcast till `test-231` (Android `Sent`) gav fortfarande 0 efter 30 s; med topicet kvar hade den startat en ny, eftersom en uppdatering utan körande aktivitet blir en start.

**APNs:** sample-serverns `POST /channels` gav först `400 BroadcastFeatureNotEnabled` — Broadcast-kapabiliteten var inte påslagen för App ID:t. Felet syns med status, orsak och sökväg.
- Broadcast Capability påslagen under Push Notifications för `com.companyname.mauispinepushsampleapp` i portalen (Jonatan loggade in och bekräftade). Apple varnar att provisioneringsprofiler med App ID:t blir ogiltiga: **enhetsprofilen måste göras om före nästa enhetsbygge (#165)**. Simulatorbyggen använder ingen profil; serverns .p8-nyckel påverkas inte.
- `POST /channels` direkt efteråt → `200`, kanal `tvI2UK0vEfEAAA6GFnjo2Q==` i sandlådan.
- `kind: broadcast` till kanalen → **Apple `Sent`** (APNs `/4/broadcasts/apps/<bundle>` svarade 200 med `apns-channel-id`, `apns-push-type: liveactivity`, `apns-expiration: 0`) och Android `Sent` (topicet). Både kanalhanteringen och broadcast-anropet fungerar mot riktiga APNs.

**iOS (iPhone 17-simulator, iOS 26.2, riktig APNs sandbox):**
- "Starta på kanal" → appen hämtade kanalen från `/channels`; liveactivitiesd startade aktiviteten `0416FEB7…` med innehållskällan `broadcastPush(channel: "tvI2UK0v…")`, och apsd skickade en pubsub-prenumeration för kanalen på `…push-type.liveactivity` (development) som besvarades. Sidan visade "Adress: kanal tvI2UK0vEfEAAA6GFnjo2Q==".
- `kind: broadcast` 18:03:10 → Apple `Sent`. **Låsskärmen visade aktiviteten med "Broadcast till kanalen · 18:03:10"** — innehåll som bara broadcasten bar; aktiviteten startades med "Startad på kanal".
- Simulatorn CD388DF2 gick inte att få med skärm efter en headless `simctl boot`; verifieringen gjordes i 43C2 (iPhone 17), startad med Simulator.app igång.

## Decisions

- **FCM-topics som Android-motsvarighet** (Jonatans val). Samma `BroadcastLiveActivityAsync` når båda plattformarna.
- **En broadcast frågar inte registret, och raderar ingenting.** Apple och FCM vet vem som följer. Resultatet får en leverans per plattform med kanalen som id, så den som loggar ser vart det gick.
- **En broadcast kan inte starta.** Apple tillåter bara update och end till en kanal; start sker med `LiveActivityOptions.Channel` eller i appen. Anropet kastar hellre än att skicka något APNs avvisar.
- **Kanalen följer med i varje FCM-meddelande**, inte bara vid start: en omstartad enhet gör en uppdatering till en start, och en aktivitet utan kanal skulle aldrig lämna topicet.
- **Kanalminnet under egen nyckel.** Ett minne skrivet före kanalerna läses då som förut.
- **Miljön ur optionerna, annars i anropet.** En broadcast har ingen installation att läsa miljön ur; `PerInstallation` utan angiven miljö kastar med förklaring i stället för att gissa.
