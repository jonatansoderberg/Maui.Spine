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

## Decisions

- **Kompakt JSON-form för `LiveActivityLayout` behövs inte i v1.** Issuets tredje fråga skulle avgöras när Orienteras layout mätts mot 4 KB. Mätt: `MyStartActivity.Layout` med verkliga strängar ger **1225 byte** `content-state`, och **1315 byte** för hela APNs-payloaden inklusive `timestamp`, `event` och `stale-date`. Det är 32 % av taket, med 2781 byte kvar — layouten skulle behöva tredubblas för att slå i det. Ingen `Patch`-form och inga korta nyckelnamn i v1; storleksvakten i steg 3 fångar det den dag en layout växer sig för stor.
- **Ett serverpaket, inte två.** `MapSpinePush` kräver ASP.NET Core, men `Orientera.Backend` kör Functions isolated med `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore`, alltså samma `HttpRequest`. En `FrameworkReference` till `Microsoft.AspNetCore.App` räcker därför för båda värdarna, och paketet behöver inte delas i `Server` och `Server.AspNetCore`.
