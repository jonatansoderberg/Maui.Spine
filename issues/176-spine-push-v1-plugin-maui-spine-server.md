# Issue #176 — Spine.Push v1: Plugin.Maui.Spine.Server — transporter, register, tagguttryck och endpoints

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/176
**Branch:** issue/176-spine-push-v1-plugin-maui-spine-server
**Status:** In Progress

## Plan

Serversidan av Spine.Push enligt förstudien §3, §4 och §8. Bygger på `Plugin.Maui.Spine.Common` från #175. En commit per steg, som i #175.

### Steg 0 — röj CS1591 i Common

#175 gjorde fyra medlemmar publika utan XML-kommentarer, och `Common` har `GenerateDocumentationFile`. Sju varningar i dag: `LiveActivity`-konstruktorn, `SpineWidgetsOptions.BackgroundRefreshHandler` och fem på `WidgetJson`. Röjs först, så de nya kontrakten läggs till i ett projekt utan brus.

### Steg 1 — kontrakt i `Plugin.Maui.Spine.Common/Push/`

- `PushInstallation` — `Id`, `Platform`, `Handle`, `ApnsEnvironment`, `Tags`, `UserId`, `AppVersion`, `OsVersion`, `LiveActivityTokens` (`PushToStart` + `Activities` per kind), `WidgetToken`, `ExpiresAt`. Formmässigt lik ANH:s `Installation` enligt §3.2.
- `PushPlatform` (`Apple`, `Android`, `Windows`), `ApnsEnvironment` (`Sandbox`, `Production`, `PerInstallation`).
- `PushKeys` — konstanterna `spine.kind`, `spine.route`, `spine.channel`, `spine.activity`, `spine.layout`, `spine.widget`.
- `PushTagExpression` — parser för ANH-syntaxen `&&`, `||`, `!`, parenteser, ingen övre gräns. Tokenizer → rekursiv descent → ett träd som utvärderas mot `IReadOnlySet<string>`. `$InstallationId:`-taggen mappas till `PushTarget.Installation` (§12 punkt 6).
- Källgenererad JSON-kontext för installationen, som `WidgetJsonContext`.

Tester: tagguttryckets parser och utvärdering, inklusive prioritet mellan operatorerna och felaktiga uttryck.

### Steg 2 — projektet `Plugin.Maui.Spine.Server` och registret

- Nytt `net10.0`-projekt, refererar bara `Common`. `FrameworkReference` till `Microsoft.AspNetCore.App` (se Decisions).
- `IPushInstallationStore` — `Upsert`, `Delete`, `Get`, `Query(tagExpression)`, `Prune(olderThan)`, `Invalidate(handle)`.
- `InMemoryPushInstallationStore`.
- `AddSpinePush(o => …)` och `SpinePushOptions` med `Apple(...)`, `Android(...)`, `UseInMemoryStore()`, `AllowTags`, `Authenticate`.

### Steg 3 — `IPushSender` och payloadbyggarna

- `PushTarget` (`Tags`, `Installation`, `User`, `All`), `PushNotification`, `PushAlert`, `PushPriority`, `PushInterruption`, `LiveActivityOptions`, `LiveActivityEvent`.
- `PushResult` med en post per installation: `Sent`, `Invalid`, `Throttled`, `Failed(reason)`.
- `PushPayloads` — ett meddelande blir `alert` på APNs och data-only med hög prioritet på FCM (§5.2, §8).
- `PushSender` som slår ihop registerfrågan med transporterna och samlar resultatet.

Tester: payload per plattform, Live Activity-kuvertet (`timestamp`, `event`, `stale-date`, `dismissal-date`, `content-state`), och en storleksvakt mot 4 KB.

### Steg 4 — `ApnsTransport`

HTTP/2 via `SocketsHttpHandler`, en `HttpClient` per miljö. ES256-JWT från `.p8` via `ECDsa.ImportFromPem`, förnyad efter 50 minuter (Apple kräver 20–60, §8). Headers per pushtyp: `apns-push-type`, `apns-topic`, `apns-priority`, `apns-expiration`, `apns-collapse-id`. Live Activity använder `apns-topic: <bundle>.push-type.liveactivity` och prioritet 5 som default.

Statusmappning: `BadDeviceToken`, `Unregistered` och 410 → `Invalid` och token tas bort; 429 → `Throttled`.

Tester: JWT:ns signatur och förnyelsefönster utan nätverk, samt header- och payloadbygge per pushtyp.

### Steg 5 — `FcmTransport`

`FirebaseAdmin` 3.6, `SendEachForMulticastAsync` i buntar om 500. `UNREGISTERED` → `Invalid`. Alert med hög prioritet, tyst med normal (§12 punkt 4).

### Steg 6 — endpoints

`app.MapSpinePush("/push")` för Minimal API och `SpinePushEndpoints.HandleAsync(HttpRequest)` för Azure Functions isolated. `PUT /push/installations/{id}` och `DELETE /push/installations/{id}`. Autentisering genom `o.Authenticate`-hooken; `AllowTags` filtrerar vad klienten får sätta.

### Steg 7 — wiki och README

Ny `docs/wiki/push-server.md` och rader i README:s dokumentations- och beroendetabell.

## Open Questions

Inga öppna. Avgjorda 2026-09-08:

1. **Registret** — in-memory räcker i det här issuet. `AzureTablePushInstallationStore` flyttar till #178.
2. **Testprojektet** — `tests/Plugin.Maui.Spine.Server.Tests` enligt förslaget.
3. **Windows** — `WnsTransport` väntar till v2/#179. `IPushTransport` hålls öppen.

## Changes

<!-- Uppdateras per steg -->

### Steg 0 — CS1591 i Common

- XML-kommentarer på `LiveActivity`-konstruktorn, `SpineWidgetsOptions.BackgroundRefreshHandler` och de fem publika medlemmarna på `WidgetJson`. `Common` bygger nu utan varningar.

### Steg 1 — kontrakt i Common

- `Push/PushInstallation.cs` — `PushInstallation`, `LiveActivityTokens`, `PushPlatform`, `ApnsEnvironment`, plus `PushJson` med källgenererad kontext så app och server delar wire-format.
- `Push/PushKeys.cs` — de sex `spine.*`-nycklarna och `PushKeys.Kinds` med `liveactivity` och `widget`.
- `Push/PushTagExpression.cs` — tokenizer och rekursiv descent för ANH-syntaxen. `Parse`, `TryParse` med felmeddelande och position, `Matches`, `ReferencedTags`, `MatchAll`, och en `ToString` som behåller den gruppering den läste.
- Nytt testprojekt `tests/Plugin.Maui.Spine.Server.Tests` (xunit, centrala paketversioner), inlagt i `Spine.slnx`. 27 tester: operatorprioritet, parenteser, negering, ordinal jämförelse, 500 taggar i ett uttryck, tio ogiltiga uttryck, och rundtur för installationens JSON.

### Steg 2 — serverprojektet och registret

- Nytt `src/Plugin.Maui.Spine.Server/` (`net10.0`, `FrameworkReference` till `Microsoft.AspNetCore.App`, refererar bara `Common`). Inlagt i `Spine.slnx`.
- `IPushInstallationStore` med `UpsertAsync`, `GetAsync`, `DeleteAsync`, `QueryAsync`, `PruneAsync`, `InvalidateAsync`. `QueryAsync` tar en valfri plattformslista och strömmar träffarna.
- `InMemoryPushInstallationStore` över en `ConcurrentDictionary`, med `TimeProvider` inskjuten så utgång går att testa utan att vänta.
- `SpinePushOptions` med `Apple(...)`, `Android(...)`, `UseInMemoryStore()`, `UseStore(...)`, `AllowTags` och `Authenticate`. `Validate()` körs i `AddSpinePush` och säger vilken uppgift som saknas.
- `AddSpinePush(o => …)` registrerar options och registret som singletons.
- 20 nya tester: registrets sex operationer, utgång mot en `FakeTimeProvider`, plattformsfiltret, och att en felkonfigurerad server avvisas med ett meddelande som pekar ut vad som fattas.

### Steg 3 — IPushSender och payloadbyggarna

- `PushTarget` med `Tags`, `Installation`, `User` och `All`. `User(id)` är taggen `user:<id>`; `Installation` är ett eget fall i stället för ANH:s `$InstallationId:`-tagg, så registret kan slå upp direkt.
- `PushNotification`, `PushAlert`, `PushPriority`, `PushInterruption`, `LiveActivityEvent`, `LiveActivityOptions`.
- `PushResult` med en `PushDelivery` per installation och räknare för `Sent`, `Invalid`, `Throttled`, `Failed`.
- `IPushTransport` och `PushEnvelope` — sömmen som gör att ANH eller OneSignal kan läggas till senare.
- `ApnsPayload` och `FcmMessage` som muterbara byggare, nåbara från `PushNotification.Apple` och `.Android`.
- `PushPayloads` med alert, tyst, Live Activity och widgetuppdatering per plattform, plus en storleksvakt mot APNs 4 KB.
- `PushSender` som löser upp målet mot registret, grupperar på plattform, bygger en payload per plattform och tar bort de registreringar en transport rapporterar döda.
- 33 nya tester, 80 totalt. Bland dem att båda plattformarna bär samma `spine.*`-nycklar, att Live Activity-kuvertet får rätt topic, event och `stale-date`, och att Orienteras verkliga layout ryms med marginal.

### Steg 4 — ApnsTransport

- `ApnsJwt` — ES256 över `.p8`-nyckeln via `ECDsa.ImportFromPem`, signaturen som rå `r||s` (`IeeeP1363FixedFieldConcatenation`) som JWT kräver, inte DER. Samma token återanvänds i 50 minuter och byts sedan; Apples fönster är 20–60.
- `ApnsTransport` — HTTP/2 med `SocketsHttpHandler`, en förfrågan per token med upp till 32 i luften samtidigt. Sätter `apns-topic`, `apns-push-type`, `apns-priority`, `apns-collapse-id` och `apns-expiration`.
- Miljön per installation som default: sandbox-token går till sandbox-värden. En pinnad `Environment` i optionsen kör över det.
- Statusmappning: `BadDeviceToken`, `Unregistered`, `DeviceTokenNotForTopic` och 410 → `Invalid`; 429 och 503 → `Throttled`; resten → `Failed` med Apples egen `reason`. Ett nätverksfel blir `Failed`, inte ett kastat undantag.
- Registreras i `AddSpinePush` när `Apple(...)` är konfigurerat.
- 20 nya tester, 100 totalt: JWT:ns header, claims, verifierad signatur och förnyelsefönster, samt transporten mot en stubbad handler för headers, värdval och varje statusmappning.

### Steg 5 — FcmTransport

- `FirebaseAdmin` 3.6.0, en namngiven `FirebaseApp` så en värd som redan skapat standardappen lämnas ifred.
- `SendEachForMulticastAsync` i buntar om 500. Alltid data-only, aldrig FCM:s `notification`-block.
- `FcmMessageReader` läser tillbaka det `FcmMessage.ToJson()` skriver, så payloadlagret kan testas för sig och transporten ändå fyller i SDK:ns egna typer.
- Statusmappning: `Unregistered`, `SenderIdMismatch`, `InvalidArgument` → `Invalid`; `QuotaExceeded`, `Unavailable` → `Throttled`; resten → `Failed`. Ett fel som fäller hela bunten ger `Failed` per installation i stället för ett kastat undantag.
- Registreras i `AddSpinePush` när `Android(...)` är konfigurerat.
- 14 nya tester, 114 totalt.

### Steg 6 — endpoints

- `SpinePushEndpoints.HandleAsync(HttpRequest)` gör hela arbetet och hämtar registret och optionsen ur `request.HttpContext.RequestServices`, så den fungerar likadant under Minimal API och under en Functions-`[Function]`.
- `MapSpinePush("/push")` mappar `PUT` och `DELETE` på `{prefix}/installations/{id}` till samma metod.
- `PUT` läser kroppen som en `PushInstallation`, kräver att id:t i kroppen och i sökvägen är samma, avvisar en registrering utan handle, kör `AllowTags` och skriver. `DELETE` tar bort och är förlåtande mot något som inte finns. `Authenticate` som säger nej ger 401.
- 19 nya tester, 133 totalt: varje statuskod, att taggpolicyn körs innan skrivningen, att servern stämplar `UpdatedAt` själv, och sju varianter av hur id:t läses ur sökvägen.

### Steg 7 — wiki och README

- Ny `docs/wiki/push-server.md`: varför registret ligger hos en själv, `AddSpinePush`, `IPushSender`, målen, tagguttrycken, Live Activities med 4 KB-taket, widgetuppdatering, registret, endpointsen för båda värdarna, och en tabell över vad som inte ingår i v1.
- Rad i README:s dokumentationstabell.

## Decisions

- **Kompakt JSON-form för `LiveActivityLayout` behövs inte i v1.** Issuets tredje fråga skulle avgöras när Orienteras layout mätts mot 4 KB. Mätt: `MyStartActivity.Layout` med verkliga strängar ger **1225 byte** `content-state`, och **1315 byte** för hela APNs-payloaden inklusive `timestamp`, `event` och `stale-date`. Det är 32 % av taket, med 2781 byte kvar — layouten skulle behöva tredubblas för att slå i det. Ingen `Patch`-form och inga korta nyckelnamn i v1; storleksvakten i steg 3 fångar det den dag en layout växer sig för stor.
- **Servern stämplar `UpdatedAt`, inte klienten.** En enhet med fel datum skulle annars kunna se nyregistrerad ut, och det är precis vad `PruneAsync` går efter. Kroppens värde skrivs över.
- **`MulticastMessage.Tokens` används trots att den är märkt föråldrad.** FirebaseAdmin 3.6.0 säger "Deprecated. Use Fids instead", men de två egenskaperna har separata backing-fält och bara `Tokens` viks ut till meddelanden per enhet: med `Fids` satt lämnar `GetMessageList()` `Message.Token` som `null`, verifierat genom reflektion. Ett byte hade alltså skickat tokenlösa meddelanden. `Tokens` behålls med en lokal `#pragma` och en kommentar, och ett test kontrollerar utvikningen så att det smäller den dag SDK:n faktiskt kopplar in `Fids`.
- **`GoogleCredential.FromJson` bytt mot `CredentialFactory.FromJson<ServiceAccountCredential>`.** Den förra är föråldrad av säkerhetsskäl i den Google.Apis.Auth som FirebaseAdmin 3.6 drar in.
- **`FcmTransport.StatusFor` tar felkoden, inte undantaget.** `FirebaseMessagingException` har intern konstruktor och går inte att skapa i ett test. Med `MessagingErrorCode?` som parameter är mappningen testbar, och signaturen säger tydligare vad den beror på.
- **`HttpClient` går att skjuta in i `ApnsTransport`.** Utan det hade transporten bara kunnat testas mot Apple. Nu stubbas svaren och varje statusmappning, header och värdval verifieras utan nätverk; den riktiga konstruktorn bygger fortfarande sin egen HTTP/2-klient.
- **`PushKeys` fick tre nycklar till: `spine.title`, `spine.body` och `spine.collapse`.** Issuet räknar upp sex, men Android skickas data-only enligt §5.2 — paketet ritar notisen själv så att förgrund och bakgrund beter sig lika och Spine väljer kanalen. Då måste titel, text och collapse-id resa som data, annars kan `PushMessage` inte fyllas i på Android. `Kinds` fick av samma skäl `alert` och `silent`, som §5.2:s `PushKind` räknar upp.
- **`RefreshWidgetsAsync` är en tyst push på iOS, inte pushtypen `widgets`.** Den senare kräver att widget-extensionet byggs mot iOS 26-SDK:n som `pushHandler`, vilket §6.2 lägger i v2. Tills dess väcker en tyst push appen som bygger om widgetarna — best effort, precis som §6.2 beskriver.
- **Storleksvakten kastar i stället för att varna.** En för stor payload ger annars en 413 från APNs långt från orsaken. `PushPayloads` mäter den byggda payloaden och kastar med byteantalet och gränsen i meddelandet. Ett test håller dessutom Orienteras layout under halva budgeten, så att marginalen inte tyst äts upp.
- **`InternalsVisibleTo` till testprojektet.** `SpinePushOptions.FilterTags` är det endpointsen anropar, inte något konsumenter rör, men regeln är värd att testa direkt i stället för genom HTTP. `Orientera.Backend` gör redan samma sak för sin startlisteläsare, så mönstret finns i repot. Det här är inte samma sak som beslutet i #175, som gällde publik yta i `Common`.
- **Registret utelämnar utgångna installationer i `QueryAsync`, men tar inte bort dem.** Borttagningen är `PruneAsync` sak, som körs när backenden vill. En utgången registrering får alltså aldrig en push, men den ligger kvar tills någon städar — annars hade en läsning haft en sidoeffekt.
- **Taggar jämförs ordinalt.** `user:ABC` och `user:abc` är olika taggar. Alternativet, att jämföra utan skiftlägeshänsyn, döljer att en tagg är en ogenomskinlig sträng som klienten och servern måste komma överens om tecken för tecken. Testat.
- **Ett serverpaket, inte två.** `MapSpinePush` kräver ASP.NET Core, men `Orientera.Backend` kör Functions isolated med `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore`, alltså samma `HttpRequest`. En `FrameworkReference` till `Microsoft.AspNetCore.App` räcker därför för båda värdarna, och paketet behöver inte delas i `Server` och `Server.AspNetCore`.
