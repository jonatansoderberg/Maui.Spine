# Issue #197 — Spine.Push: flytta in lokala notiser i ramverket och harmonisera dem med push

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/197
**Branch:** issue/197-lokala-notiser-i-ramverket
**Status:** Completed

## Plan

Grenen går i två steg: förstudien först, godkännande, sedan implementationen. Ordningen är issuens egen — den svåra frågan är API-ytan mot en app som redan har båda vägarna, inte plattformsanropen.

### Steg 1 — Förstudie: `docs/proposals/spine-local-notifications.md`

Samma form som `spine-push.md`: status- och frågerad överst, slutsats i tabell, avsnitt per fråga, leveranssteg sist. Den ska svara på:

- **Var API:t bor.** Förslaget är i `Plugin.Maui.Spine.Push`, inte i ett eget paket. Tillstånd, kanaler och mottagning är redan där, och issuens tre återkommande problem är alla att lokalt och push i dag är två halvor av samma sak.
- **Ett tillstånd, ett anrop.** `IPushService.RequestPermissionAsync` anropar redan `UNUserNotificationCenter.RequestAuthorizationAsync` (`ApplePushPlatform.iOS.cs:48`) respektive `Permissions.PostNotifications` (`AndroidPushPlatform.cs:49`) — exakt vad en lokal schemaläggare behöver. Att verifiera: att en app **utan** `Backend` kan använda den, och att `RegisterForRemoteNotifications` (`ApplePushPlatform.iOS.cs:56`) inte ska köras för en app som bara notifierar lokalt.
- **Mottagningen delad.** Spine.Push äger redan `UNUserNotificationCenter.Current.Delegate` (`SpinePushExtensions.iOS.cs:69`), och systemet lämnar lokala notiser till samma delegat. En lokal notis som öppnas ska alltså nå `IPushHandler.OnOpenedAsync` med en `PushMessage` av samma form, och `spine.route` ska betyda samma sak. Frågan förstudien avgör: om `PushMessage`/`PushContext` behöver en markör för *lokal* eller om appen inte ska behöva veta.
- **Kanaler delade.** `SpinePushOptions.AddChannel` skapar kanalerna vid start (`PushNotifications.CreateChannels`); en lokal notis ska nå dem genom att namnge en kanal-id, och postas genom samma byggare som push så ikon, `CollapseId` och öppna-intent beter sig lika.
- **Idempotent schemaläggning.** Orienteras `SyncAsync(plan)` lyfts som den är: enheten görs lika med planen, allt annat avbokas. En sak ska göras bättre än förlagan — `AndroidNotificationScheduler` håller sin `_scheduled`-lista i minnet, så alarm som schemalagts av en tidigare process aldrig avbokas. Ramverket persisterar id:na.
- **Dubbleringen.** `IPushService.IsRegistered` finns redan och är rätt signal. Förstudien beskriver mönstret i dokumentationen i stället för att bygga en abstraktion för det — ramverket kan inte veta vad appen vill dubblera.
- **Gränsen.** Spine äger tillstånd, kanaler, schemaläggning och mottagning. Appen äger vad som ska notifieras och när. Orienteras `NotificationPlanner` och `PushTags` stannar i appen.
- **Plattformsgränser att skriva ut:** inexakta alarm på Android (`SCHEDULE_EXACT_ALARM` delas inte ut för det här), alarm som försvinner vid omstart, och vad som gäller på Mac Catalyst och Windows.

### Steg 2 — Implementation (efter godkänd förstudie)

Preliminär form, fastställs av förstudien:

- `src/Plugin.Maui.Spine.Push/ILocalNotificationService.cs` — `IsSupported`, `SyncAsync(plan, ct)`, `CancelAllAsync(ct)`, `PendingAsync(ct)`, plus en `LocalNotification`-record vars fält speglar `PushMessage` (`Id`, `At`, `Title`, `Body`, `Route`, `Channel`, `Data`).
- `Platforms/iOS/AppleLocalNotifications.iOS.cs` — `UNCalendarNotificationTrigger`, hela pending-mängden ersatt i stället för diffad.
- `Platforms/Android/AndroidLocalNotifications.cs` + mottagarklass — `AlarmManager`, inexakta alarm, posten byggd genom `PushNotifications` så kanal och öppna-intent delas. Manifestposten via samma overlay som resten av paketet.
- `Services/UnsupportedLocalNotifications.cs` — säger nej i stället för att låtsas, som `UnsupportedPushPlatform`.
- Registrering i `SpinePushExtensions.UseSpinePush`.
- `samples/MauiSpinePushSampleApp` — en sida som schemalägger, listar pending och avbokar, med rader i `PushLog` så vägen går att observera.
- `docs/wiki/push.md` — avsnitt om lokala notiser, och om valet mellan de två vägarna.

Orientera migreras **inte** i den här grenen; det blir en egen issue när API:t står.

## Open Questions

Inga öppna. Båda frågorna besvarade: markören ska finnas, och Orientera migreras i en egen issue efteråt.

## Changes

- `docs/proposals/spine-local-notifications.md` — förstudien skriven (rev 1), sedan uppdaterad med §11 om avvikelserna i implementationen.
- `PushKeys.Source` + `PushKeys.Sources.Local`, och `PushMessage.IsLocal` som läser dem.
- `ILocalNotificationService` och `LocalNotification` i `Plugin.Maui.Spine.Push`; `UnsupportedLocalNotifications` för plattformar utan implementation.
- `LocalNotificationPayload` — data-påsen en schemalagd notis bär, samma nycklar som en push, så öppningsvägen är oförändrad.
- `AppleLocalNotifications` — `UNTimeIntervalNotificationTrigger`, hela pending-mängden ersatt, 64-taket loggat när planen är större.
- `AndroidLocalNotifications` + `SpineLocalNotificationReceiver` + `SpineLocalNotificationBootReceiver` + `LocalNotificationStore`; `RECEIVE_BOOT_COMPLETED` deklareras av paketet.
- `PushNotifications.StableId` (FNV-1a) ersätter `string.GetHashCode` för collapse-id och alarmens request-koder.
- `SpinePushRemote` i targets: `false` tar bort kravet på `google-services.json` och `aps-environment`. Vid körning hoppas fjärrregistreringen över helt när `Backend` är `null`.
- Registrering i `UseSpinePush` på båda plattformarna.
- `samples/MauiSpinePushSampleApp` — sidan **Lokalt** (planera tre, planera bara en, avboka, läs om planen) och `SamplePushHandler` som visar `IsLocal` i loggen.
- `LocalNotificationPayload.At` (`spine.at`) — ögonblicket följer med notisen, eftersom Apples `NextTriggerDate` inte går att läsa tillbaka.
- Samplet frågar om tillstånd när det inte är beviljat, inte bara vid `NotDetermined`: Android har inget sådant läge.
- `docs/wiki/push.md` — avsnittet *Local notifications* med API, valet mellan halvorna, appen utan fjärrpush, och vad plattformarna gör.

## Decisions

- **Förstudie först, implementation i samma gren.** Vald omfattning: förstudien godkänns innan någon kod skrivs, men båda landar under #197.
- **Markören är en nyckel i data-påsen, inte ett C#-fält.** `spine.source = local`, läst som `PushMessage.IsLocal`. Öppningsvägen går genom intent-extras på Android och `UserInfo` på iOS; ett fält vid sidan om påsen hade tappats vid processgränsen och behövt en egen väg tillbaka. `PushKind` rörs inte — en lokal notis *är* en `Alert`, det som skiljer är avsändaren.
- **`SyncAsync(plan)` som enda skrivväg, ingen `ShowAsync` i v1.** Formen som fungerat i Orientera. "Visa nu" läggs till när något faktiskt kräver den.
- **`UNTimeIntervalNotificationTrigger` i stället för kalendertrigger.** `At` är ett absolut ögonblick; en kalendertrigger fyrar på väggklockan i den tidszon enheten då befinner sig i.
- **Boot-mottagare på Android.** En app som notifierar om något i morgon bitti ska inte tystna av en omstart i natt.
- **Orientera migreras i en egen issue:** [#222](https://github.com/jonatansoderberg/Maui.Spine/issues/222). Migreringen är beviset på att API-ytan bär och förtjänar att vara ett eget steg.
- **Ögonblicket bärs i nyttolasten i stället för att läsas ur triggern.** `UNTimeIntervalNotificationTrigger.NextTriggerDate` svarar *nu plus intervallet* vid varje avläsning — upptäckt i simulatorn, där en väntande notis flyttade sig framåt varje gång planen lästes om.
- **Mac Catalyst blev inte stött.** `Platforms/iOS/**` kompileras inte för `net10.0-maccatalyst` — verifierat i den byggda dll:en — så Spine.Push saknar Apple-implementation där redan i dag, trots att wikin påstår att v1 täcker Catalyst. Att rätta det handlar om push på Catalyst, inte om lokala notiser, så det lämnas utanför den här grenen.
- **Runtime styrs av `Backend`, inte av en andra egenskap.** `SpinePushRemote` är byggflaggan; vid körning räcker `Backend is null`, eftersom registrering ändå är omöjlig utan den. Ingen ny runtime-egenskap behövdes, och de två kan inte hamna i otakt.
- **`SpinePushRemote` tar inte bort Firebase-beroendet**, bara konfigurationen. Paketets `PackageReference` följer med oavsett, så Androids minSdk 23 står kvar — den kontrollen är därför inte gated.
- **Slug förkortad till `197-lokala-notiser-i-ramverket`.** Den mekaniska varianten av titeln blev `spinepush-flytta-in-lokala-notiser-i-ramverket-och` efter 50 tecken; övriga filer i `issues/` använder korta, läsbara slugs.
