# Issue #180 — Spine.Push v1: sample-app och sample-server (MauiSpinePushSampleApp)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/180
**Branch:** issue/180-spine-push-v1-sample-app-och-sample-server
**Status:** In Progress

## Plan

Två nya projekt enligt förstudien §8.2, i standardsamplens stil: `SpineApplication<T>`, trefilsmönstret, `[NavigableRegion]`/`[NavigableSheet]`. Samplen är den första konsumenten av både `Plugin.Maui.Spine.Push` och `Plugin.Maui.Spine.Server`. En commit per steg.

### Steg 0 — Windows kraschar i dag, rättas först

`Plugin.Maui.Spine.Push` multitargetar Windows men har ingen implementation där: `ConfigurePlatform` är en tom partial, ingen `IPushPlatform` registreras, och första `GetRequiredService<IPushService>()` kastar. `PushStatus.Unsupported` finns redan i enumen men returneras aldrig — kopplingen glömdes i #177.

Åtgärd: en `UnsupportedPushPlatform` som svarar `Unsupported`, gör ingenting och registreras med `TryAddSingleton` **efter** `ConfigurePlatform`. Då tar plattformens egen implementation företräde där en finns, och varje TFM utan en degraderar förutsägbart i stället för att krascha. Det gäller alla konsumenter, inte bara samplen.

### Steg 1 — `samples/MauiSpinePushSampleApp.Server`

- Minimal API på `Plugin.Maui.Spine.Server` med `UseInMemoryStore()` och `MapSpinePush("/push")`.
- `POST /send` tar samma modell som `IPushSender` — mål, typ, titel, text, route, kanal, prioritet — och svarar med `PushResult`, så appen kan visa `Sent`/`Invalid`/`Throttled` per installation.
- `GET /installations` så samplen kan visa vad servern faktiskt känner till.
- `appsettings.Development.json` som mall med tomma nycklar och kommentarer om varifrån värdena kommer (förstudiens §9). Riktiga värden ur user-secrets eller miljövariabler, aldrig i repot.
- En `.http`-fil med samma anrop.

### Steg 2 — appens skelett

- MAUI-app med `UseSpine` + `UseSpineWidgets` + `UseSpinePush`, TFM:er som standardsamplen.
- `SpinePush.Install()` i `Platforms/iOS/Program.cs`, som wikin föreskriver.
- Backend-adressen ur inbäddad `appsettings.json`, som Orientera gör. Android-emulatorn når värden på `10.0.2.2`.
- `PushLog` — en singleton som handlern skriver till, och som **Logg**-sidan visar.
- `SamplePushHandler : IPushHandler` som loggar allt och svarar `None` i förgrunden så appen visar meddelandet själv.

### Steg 3 — sidorna

| Sida | Vad |
|---|---|
| **Hem** | Status, installations-id, token (kortad, kopierbar), och knapparna *Be om tillstånd*, *Registrera om*, *Avregistrera*, *Öppna inställningar* |
| **Taggar** (ark) | Switchar för `kind:news`, `kind:alerts`, `team:red`, `team:blue` som blir `SetTagsAsync`, plus ett fritt fält |
| **Skicka** | Formulär mot serverns `POST /send`: till mig eller tagguttryck, popup/tyst/Live Activity/widget, titel, text, route, kanal, prioritet |
| **Logg** | Tid, `Kind`, förgrund/bakgrund/kallstart, `Route`, `Data` och vad handlern svarade. Tapp navigerar till routen, vilket visar `OnOpenedAsync` |
| **Live Activity** | Starta en aktivitet lokalt, se dess token, låt servern uppdatera den |

### Steg 4 — dokumentation

`docs/wiki/push.md` får en "kör samplen först"-ingång, och README:s sample-avsnitt en rad om vad som krävs utanför repot: App ID med Push, `.p8`, `google-services.json`.

## Open Questions

Inga öppna. Avgjorda 2026-09-08:

1. **`google-services.json`** — en platshållarfil checkas in, så samplen bygger och startar direkt efter en klon.
2. **Windows** — TFM:en tas med; `Status` blir `Unsupported` och sidorna visar det.
3. **Standardsamplen** — ingen push där. Den fortsätter bygga utan Firebase-fil.

## Changes

<!-- Uppdateras per steg -->

### Steg 0 — Windows kraschar inte längre

- `UnsupportedPushPlatform` svarar `Unsupported` och registreras med `TryAddSingleton` efter `ConfigurePlatform`.

### Steg 1 — sample-servern

- `samples/MauiSpinePushSampleApp.Server`, Minimal API på `Plugin.Maui.Spine.Server` med `UseInMemoryStore()` och `MapSpinePush("/push")`.
- `POST /send` för alert, tyst, Live Activity och widgetuppdatering, med `PushResult` per installation i svaret. `GET /installations` visar vad registret håller, med token förkortad.
- `appsettings.Development.json` som mall med tomma nycklar och var värdena kommer ifrån, `send.http` med sex färdiga anrop, `launchSettings.json` på port 5100.
- **`AddSpinePush` vägrade starta utan plattform.** Rättat, se Decisions.
- Verifierat mot en körande server: tomt register → två `PUT` → registret visar båda → tagguttrycket `kind:news && !team:blue` matchar rätt → `DELETE` → en kvar. Allt över HTTP, vilket är första gången #176:s endpoints och register körts på riktigt.

### Steg 2 och 3 — appen och sidorna

- `samples/MauiSpinePushSampleApp` med `UseSpine` + `UseSpineWidgets` + `UseSpinePush`, `SpinePush.Install()` i `Program.cs`, backend-adress ur inbäddad `appsettings.json` med `10.0.2.2` för emulatorn.
- `PushLog` som handlern skriver till, `SamplePushHandler` som loggar allt och svarar `None` i förgrunden så Logg-sidan är beviset i stället för en banner över den.
- Fem sidor i trefilsmönstret: **Hem**, **Taggar** som ark, **Skicka**, **Logg**, **Live Activity**.
- Tre beroendeproblem som varje konsument hade gått på, alla lösta i paketet — se Decisions.
- **Verifierat i simulatorn:** appen startar, tillståndsdialogen ger `Authorized`, Taggar-arket öppnas, och en `xcrun simctl push` ger loggraden `Alert foreground "Resultat klara" — route: log, answered: None, competition=59691`. Hela klientkedjan från payload till UI.

## Decisions

- **Tre beroendeproblem mellan Firebase och MAUI, lösta i `Plugin.Maui.Spine.Push` i stället för i varje app.** Först `NU1107`: MAUI 10.0.50 pinnar `LiveData.Core` under 2.9.3 medan Firebase kräver minst 2.11.0.1 — löst med en direkt referens, som NuGet självt föreslår, till priset av `NU1608`-varningar om den brutna övre gränsen. Sedan `FragmentKt is defined multiple times`: `Fragment.Ktx` 1.8.x och `Fragment` 1.9.0 bär båda klassen, så D8 stannade — löst genom att pinna båda till 1.9.0. Och sist `minSdkVersion 21 cannot be smaller than version 23`: Firebase kräver minSdk 23 medan Spines eget golv är 21. Det sista höjs **inte** för hela repot, eftersom det bara är push som kräver det; i stället felar Push-targeten med exakt vad som ska läggas till, i stället för manifest-mergerns vägg av text.
- **En server utan konfigurerad plattform får starta.** `AddSpinePush` kastade "configure at least one of Apple(...) and Android(...)", vilket gör att sample-servern inte går att köra efter en klon — och det stämmer inte heller med koden: registret och endpointsen behöver ingen transport, bara utskicket gör det, och `PushSender` hoppar redan över plattformar utan transport. Kravet är borttaget. För att ingen ska undra varför en push inte kom fram svarar samplens `/send` med en förklaring när registret har enheter men ingen transport finns.
- **`UnsupportedPushPlatform` registreras med `TryAddSingleton` efter `ConfigurePlatform`.** Alternativet, att registrera den först och låta plattformen skriva över, fungerar också men gör ordningen till en osynlig regel som nästa plattformsimplementation kan bryta. Med `TryAdd` sist är regeln uttryckt i koden.

## Verifiering

Simulator: `xcrun simctl push` mot appen ska ge en rad i **Logg** med rätt `Kind` och `Route`, och ett tapp ska navigera. Emulator: `POST /send` från sample-servern, som också visar `PushResult` i appen. Sample-servern körs lokalt med `dotnet run`.

**Vad som inte går att verifiera här:** en riktig APNs-token kräver fysisk enhet med profil och push-entitlement, som inte finns på den här maskinen. FCM kräver ett Firebase-projekt. Kedjan hela vägen — `POST /send` till en fysisk iPhone — är milstolpen som ligger kvar i #178. Windows-TFM:en går inte att bygga här.
