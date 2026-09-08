# Issue #178 — Spine.Push v1: Orientera som drivare — taggar, handler, backend-sändning

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/178
**Branch:** issue/178-spine-push-v1-orientera-som-drivare-taggar-handler
**Status:** In Progress

## Vad som går att göra här, och vad som inte gör det

Det här issuet är till större delen kod, men två delar ligger utanför den här maskinen och jag vill vara tydlig med det innan planen:

- **Del 5, registreringarna utanför repot** — Push Notifications på App ID:t, APNs-nyckeln, nya provisioneringsprofiler, Firebase-projektet — är ditt arbete i Apples och Googles portaler. Jag kan förbereda allt koden behöver och skriva ner exakt vad som ska göras, men inte göra det.
- **Milstolpen, en push från Azure Functions till en fysisk iPhone**, kräver enhet, profil och signeringsidentitet. Ingen av dem finns här. Jag kan verifiera allt fram till APNs, och lämna det sista steget till dig.

**Del 6 är redan klar.** Mätningen av `MyStartActivity.Layout` mot 4 KB-taket gjordes i #176: **1225 byte** `content-state`, **1315 byte** hela APNs-payloaden, mot taket 4096. 32 % av budgeten. Ingen kompakt form och ingen `Patch` behövs, och ett test i `Plugin.Maui.Spine.Server.Tests` håller Orienteras layout under halva budgeten.

## Plan

En commit per steg.

### Steg 1 — Table Storage-registret

Flyttades hit från #176. `AzureTablePushInstallationStore` i `Plugin.Maui.Spine.Server`: partition på plattform, radnyckel installations-id, och en sekundär tabell tagg → installation så en vanlig fråga inte blir en full scan. `UseAzureTableStore(connectionString)` i `SpinePushOptions`.

### Steg 2 — taggar ur Orienteras preferenser

`NotificationPreferences.Enabled` blir taggar: `kind:entry-closing`, `kind:pm-published`, `kind:start-time-published`, `kind:live-started`, `kind:results-published`. Plus `user:<id>` från `IPeopleSource.GetMeAsync`, `competition:<id>` för anmälda tävlingar och `person:<id>` för Min grupp.

`NotificationService.RefreshAsync` behåller den lokala planen för det tidsstyrda — `TimeToLeave` kan bara enheten veta — och anropar `IPushService.SetTagsAsync` för det händelsestyrda. Det som i dag schemaläggs lokalt och också kan komma som push tas bort ur den lokala planen, så användaren inte får samma sak två gånger.

### Steg 3 — `OrienteraPushHandler`

Navigerar på `Route`: `competition/<id>`, `live/<id>`, `results/<id>`. Svarar `None` i förgrunden när den sida som redan visas är den notisen gäller, annars `Banner | Sound | List`.

`AppleNotificationScheduler` sätter i dag `UNUserNotificationCenter.Current.Delegate` i sin konstruktor, vilket krockar med paketet. Den raden och `ForegroundPresenter` tas bort — beslutet från #177.

### Steg 4 — appens uppsättning

`UseSpinePush` i `MauiProgram` med backend-adressen, `WhenAsked` och kanalen `competitions`. `RequestPermissionAsync` bakom samma switch i `NotificationSheet` som i dag frågar om lokala notiser. `aps-environment` i `Platforms/iOS/Entitlements.plist` bredvid App Group — Orientera äger sin fil, så det gemensamma steget validerar den i stället för att skriva den.

### Steg 5 — backend

`AddSpinePush` i `Program.cs` med Table Storage på `AzureWebJobsStorage`. `PushFunctions.cs` med två `[Function]` som vidarebefordrar `PUT`/`DELETE /api/push/installations/{id}` till `SpinePushEndpoints.HandleAsync`. En timer-function som jämför resultatstatus i `ResponseCache` och skickar `kind:results-published && competition:<id>`. Nycklarna i `local.settings.example.json` med tomma värden.

### Steg 6 — dokumentation

`samples/Orientera/docs/` får en sida om vad som måste registreras utanför repot, med förstudiens §9 som underlag, så du har en checklista när du går till portalerna.

## Open Questions

Inga öppna. Avgjorda 2026-09-08:

1. **Backend pollar `results-published`** i det här issuet. Live-start blir en andra timer-function när den första visat sig fungera mot enhet.
2. **`ApplicationId` blir `se.cosmomedia.orientera`**, bytt före App ID-registreringen. App-gruppen följer med som `group.se.cosmomedia.orientera`.
3. **Aspire kör den lokala backenden**, med Azurite som en resurs i AppHosten i stället för en global installation.

Dessutom: du har en fysisk iPhone men ingen Android-enhet, så Android verifieras i emulatorn. Och du loggar in i Apples portal i en lokal webbläsare, varefter jag registrerar det som krävs — se noteringen under Decisions om APNs-nyckeln.

## Changes

<!-- Uppdateras per steg -->

### Steg 1 — `ApplicationId` och Aspire

- `se.cosmomedia.orientera` i `Orientera.csproj`, app-gruppen `group.se.cosmomedia.orientera` i entitlements, och wikins exempel.
- Nytt `samples/Orientera.AppHost`: Azurite som containerresurs och backenden som `AddAzureFunctionsProject`, startade med ett kommando och med Aspire-dashboarden för loggar.
- `samples/Orientera.Backend/Properties/launchSettings.json` tillagd, så porten blir 7071 och inte slumpad.
- Verifierat: `dotnet run` i AppHosten ger Azurite igång, Functions-värden igång, och `GET http://localhost:7071/api/health` svarar 200.

### Steg 2 — Table Storage-registret

- `AzureTablePushInstallationStore`: partition på plattform, radnyckel installations-id, och en andra tabell `{prefix}Tags` med tagg som partition och installations-id som radnyckel.
- `PushTagExpression.RequiredTags` — de taggar varje träff måste bära. Ett uttryck som har en sådan blir en indexläsning i stället för en scan.
- `SpinePushOptions.UseAzureTableStore(connectionString)`, och `Validate()` kräver inte längre en plattform: ett register utan transport är en riktig uppsättning.
- Tolv tester mot Azurite (den AppHosten startar), som skippar när ingen emulator svarar. Verifierat: alla tolv gröna mot Azurite, och tabellerna städas bort efter varje test.

### Steg 3 — Apple-registreringarna och APNs-nyckeln

- Registrerat i portalen: App ID `se.cosmomedia.orientera` med Push Notifications och App Groups, `se.cosmomedia.orientera.widgets` med App Groups, och gruppen `group.se.cosmomedia.orientera`.
- Widget-extensionens bundle-suffix bytt från `SpineWidgets` till `widgets`.
- APNs-nyckel `Q2G76H33BW` (Sandbox & Production, Team Scoped), inlagd i AppHostens user-secrets som Aspire-parametrar och vidarebefordrad till backenden som `Push__Apple__*`.
- **Verifierat mot skarpa APNs:** en påhittad men välformad device-token ger `BadDeviceToken`, inte `InvalidProviderToken`. Provider-token, ES256-signaturen och `apns-topic` är alltså accepterade av Apple.
- Buggen det avslöjade: `ApnsJwt` byggde huvud och claims med `JsonSerializer` på en `Dictionary`, vilket kastar i en trimmad eller AOT-host. Skrivs nu fält för fält med `Utf8JsonWriter`.

### Steg 4 — taggarna ur Orienteras preferenser

- `PushTags` översätter `NotificationPreferences.Enabled` till `kind:`-taggar, plus `user:<id>`, `competition:<id>` för anmälda tävlingar och `person:<id>` för Min grupp.
- `NotificationService.RefreshAsync` sätter taggarna vid varje omplanering, och utelämnar det backenden levererar som push ur den lokala planen.
- `IPushRegistration` som söm mot Spine.Push, av samma skäl som `INotificationScheduler` finns: mappen kompileras in i testerna på ren .NET, där MAUI-paketet inte existerar.
- Sju tester för taggarna.

### Steg 5 — `OrienteraPushHandler`

- `PushRoute` läser `competition/<id>`, `live/<id>` och `results/<id>`. Alla tre öppnar `EventDetailsPage`: det är där PM, live och resultat finns i dag, och separata sidor vore separata routes.
- `OnScreen` — vilken tävling som visas just nu, satt av sidan själv, så en notis om den inte bannrar över den.
- `AppleNotificationScheduler.ForegroundPresenter` borttagen. Paketet äger `UNUserNotificationCenter.Current.Delegate`, och systemet lämnar lokala notiser till samma delegat.
- `aps-environment` i `Platforms/iOS/Entitlements.plist`. Det gemensamma entitlements-steget validerar filen i stället för att skriva den, och byggena stannade tills den fanns — precis som avsett.

### Steg 6 — appens uppsättning

- `UseSpinePush` med backendadressen, `WhenAsked`, kanalen `competitions` och handlern. `SpinePush.Install()` före `UIApplication.Main`.
- `RequestPermissionAsync` bakom samma reglage i `NotificationSheet` som frågar om lokala notiser.
- `IPushService.IsRegistered` tillagd i paketet.
- Android: `BaseAddressAndroid` (10.0.2.2), `SupportedOSPlatformVersion` 23 och `SpinePushEnabled=false` tills det finns ett Firebase-projekt.

### Steg 7 — backenden

- `PushFunctions`: `PUT`/`DELETE /api/push/installations/{id}` vidare till `SpinePushEndpoints.HandleAsync`, och timern `AnnounceResultsPublished` var femte minut.
- `ResultsPublished.Pending` — den rena regeln för vad som är nyheter — och `AnnouncedStore`, ett varaktigt register i Table Storage.
- `AddSpinePush` i `Program.cs` med Table Storage på `AzureWebJobsStorage` och APNs-nyckeln ur konfigurationen. Nycklarna i `local.settings.example.json`.
- Sex tester för regeln.
- Verifierat mot Aspire med Azurite: `PUT` ger 204 och raden hamnar i `OrienteraPushInstallations` **utan** `user:`-taggen, fel id ger 400, `DELETE` ger 204, båda functionsen indexeras, och en manuellt triggad timer läste den riktiga kalendern och bokförde de tävlingar som fått resultat det senaste dygnet.

### Steg 8 — dokumentation

- `samples/Orientera/docs/push-uppsattning.md`: vad som är registrerat hos Apple, vad som saknas hos Google, backendens fyra nycklar lokalt och i drift, och vad som medvetet inte är gjort.

## Decisions

- **`ApplicationId` bytt till `se.cosmomedia.orientera`.** Ändrat i `Orientera.csproj` och app-gruppen i `Platforms/iOS/Entitlements.plist`, plus exemplen i wikin. Testfixturerna i `Plugin.Maui.Spine.Server.Tests` säger fortfarande `com.companyname.orientera`; de är godtyckliga strängar i pakettester och inte en referens till Orientera, så de lämnas.
- **Azurite körs på de välkända portarna, annars startar inte backenden.** Första försöket lät `RunAsEmulator()` slumpa portarna, och Functions-värden dog med `Connection refused (127.0.0.1:10001)`. Orsaken: `WithHostStorage` talar om för *värden* var lagringen finns, men backendens egna kö- och blobklienter läser `UseDevelopmentStorage=true` ur `local.settings.json`, vilket alltid betyder 10000–10002. Med `WithBlobPort`/`WithQueuePort`/`WithTablePort` proxar Aspires DCP de portarna till containern, och båda vägarna hittar rätt.
- **`AddAzureFunctionsProject`, inte `AddProject`.** Ett mellanläge där backenden kördes som ett vanligt projekt provades och fungerade, men Functions-integrationen ger värdlagringen och resursreferenserna gratis. Felet låg aldrig i integrationen utan i portarna.
- **APNs-nyckeln kan bara laddas ner en gång, och Apple tillåter högst två aktiva.** När vi kommer till portalen skapar jag inte nyckeln utan att du sagt till: `.p8`-filen finns bara att hämta i samma ögonblick den skapas, och en förlorad nyckel måste återkallas och ersättas.
- **Nyckeln ligger i AppHostens user-secrets som Aspire-parametrar, inte i backendens `local.settings.json`.** Den filen ligger i repots träd och är lätt att committa av misstag. AppHosten skickar `Parameters:apple-*` vidare som miljövariabler `Push__Apple__*`, så backenden läser dem som vanlig konfiguration. Själva `.p8`-filen har aldrig passerat genom mig: kommandot som läser in den kördes av dig.
- **Ett taggindex, inte en scan — men bara när uttrycket tillåter det.** `ReferencedTags` duger inte som index: `!muted` nämner `muted` men matchar de installationer som *inte* har den. Därför `RequiredTags`, som är union för `&&`, snitt för `||` och tom för `!`. Saknas en sådan tagg — `PushTarget.All`, eller ett uttryck som bara är negationer — scannas partitionerna, vilket är rätt för just de fallen.
- **`InvalidateAsync` scannar.** Det finns inget index på handtaget. Det läses bara när en transport rapporterar en död token, vilket är sällsynt bredvid att skicka, så en scan är en bättre affär än en tredje tabell att hålla i synk.
- **Alla tre routerna öppnar samma sida.** `competition/`, `live/` och `results/` går till `EventDetailsPage`, för det är där PM, live och resultat finns i appen i dag. Routen är ändå tre olika strängar: avsändaren är en server som inte ska veta vad appens sidor heter, och den dagen live får en egen sida behöver backenden inte ändras.
- **Bara `results-published` tas ur den lokala planen.** `PushTags.Pushed` innehåller den enda typ backenden faktiskt skickar. Taggarna sätts för alla fem händelsestyrda typerna ändå, så en installation redan bär rätt tagg den dag backenden får en avsändare till — men den lokala planen tappar ingenting i förväg.
- **`IsRegistered` blev en egenskap i paketet, inte en gissning i appen.** Första försöket var `Status is Authorized or Provisional` i Orientera, vilket är sant i glappet mellan beviljat tillstånd och en token som ännu inte kommit — och just då hade appen slutat planera resultatnotisen lokalt utan att backenden kunde nå den. Att skicka dubbelt är dåligt; att inte skicka alls är värre. Paketet vet svaret: tillstånd, token *och* en registrering backenden svarat ja på.
- **Timern jämför mot ett varaktigt register, inte mot svarscachen.** Planen sa `ResponseCache`, men den är TTL-minne: varje omstart hade annonserat samma resultat igen. `AnnouncedStore` är en tabell bredvid registret. Fönstret på ett dygn finns för att första körningen efter en utrullning annars hade skickat hela bakåtkatalogen på en gång, till alla.
- **En tävling bokförs som annonserad oavsett utfall.** Att försöka igen var femte minut i ett dygn hjälper ingen: de telefoner som inte var registrerade då är inte registrerade nu, och de som var det har den redan.
- **`user:`-taggar filtreras bort i backenden.** Registreringsendpointen är öppen, som resten av backendens API, så en tagg är bara ett önskemål. `kind:` och `competition:` är inte hemliga; `user:` skulle låta vem som helst lyssna som någon annan. Filtret tas bort den dag registreringen autentiseras.
- **Push är avstängt på Android i Orientera.** Det finns inget Firebase-projekt, och utan `google-services.json` stoppar byggkontrollen. `SpinePushEnabled=false` för Android låter appen byggas och köras som vanligt; Android hämtar bara aldrig någon token. Sample-appen i #180 är den som visar Android-vägen.
- **Reflektionsfri JSON går inte att slå på för hela testprojektet.** `JsonSerializer.IsReflectionEnabledByDefault=false` som permanent skydd provades och fäller `Azure.Data.Tables`, som serialiserar sina entiteter med reflektion. Switchen fäller alltså ett tredjepartsbibliotek snarare än vår kod, och är därmed för trubbig. Fixen i `ApnsJwt` står kvar; skyddet får vara kommentaren i koden och verifieringen mot skarpa APNs.

## Verifiering

Kört och grönt:

- Bygg: Orientera iOS och Android, backenden, apphosten, push-sample-appen (iOS och Android) och sample-servern.
- Tester: `Plugin.Maui.Spine.Server.Tests` och `Orientera.Tests`, inklusive tolv nya mot Azurite och tretton nya för taggar, routes och resultatregeln.
- APNs: provider-token verifierad mot skarpa Apple — `BadDeviceToken` på en påhittad enhet.
- Backenden mot Aspire med Azurite: registrering, taggfilter, felhantering och en manuellt triggad resultattimer mot den riktiga kalendern.

Kvar, och utanför den här maskinen: provisioneringsprofil och en push hela vägen till en fysisk iPhone — det är milstolpen. Android saknar Firebase-projekt och verifieras när det finns. Windows-TFM:en går inte att bygga här.
