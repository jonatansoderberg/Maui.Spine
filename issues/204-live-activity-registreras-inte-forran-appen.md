# Issue #204 — Spine.Push: en startad eller avslutad Live Activity registreras inte förrän appen råkar registrera om sig

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/204
**Branch:** issue/204-live-activity-registreras-inte-forran-appen
**Status:** Completed

## Plan

`PushService` prenumererar redan på precis den här sortens händelse för enhetens token:

```csharp
platform.HandleChanged += _ => RefreshAsync().SafeFireAndForget();
```

Live Activities saknar motsvarigheten. Fixen är att ge dem en.

### Steg 1 — En händelse på `ILiveActivityService`

`event Action? ActivitiesChanged`, höjd av `LiveActivityService` när `_active` ändras: efter en lyckad
`StartAsync` och i `End` (som `EndAllAsync` går igenom). Adopt-vägen vid processtart behöver den inte
— registreringen vid launch läser ändå tokens.

### Steg 2 — `PushService` prenumererar

Samma rad som för `HandleChanged`, i konstruktorn. `ILiveActivityService` resolvas ur `services`;
den finns bara när appen också använder `Plugin.Maui.Spine.Widgets`, och `UseSpinePush` körs efter
`UseSpineWidgets` enligt wikin.

### Varför tokenens fördröjning inte behöver egen hantering

Issuet noterade att en aktivitets push-token inte finns i samma ögonblick som `StartAsync` returnerar.
Den behöver ingen egen mekanism: `PushService.LiveActivityTokensAsync` anropar
`activity.GetPushTokenAsync`, som går via `LiveActivityService.PollAsync` — tio försök med en halv
sekunds mellanrum. Refreshen väntar alltså själv ut token. Att höja händelsen vid start räcker.

### Verifiering

Fysisk iPhone: starta en aktivitet, **utan** att växla ut och in, och se att `/installations` får den
nya aktivitetstoken. Skicka sedan en `liveactivity`-push direkt — den ska landa. I dag krävs en
foreground eller ett tryck på "Registrera om" däremellan.

## Open Questions

Inga. Den enda avvägningen — att lägga en medlem på ett publikt interface — är avgjord under
Decisions.

## Changes

- `ILiveActivityService` fick `event Action? ActivitiesChanged`.
- `LiveActivityService` höjer den efter en lyckad `StartAsync` och i `End`, som `EndAllAsync` går
  igenom.
- `PushService` prenumererar i konstruktorn, med samma rad som redan finns för `HandleChanged`.

## Decisions

- **En händelse på interfacet, inte en poll och inte appens ansvar.** `PushService` har redan exakt
  det här mönstret för enhetens token — `platform.HandleChanged += _ => RefreshAsync()`. Att lägga
  ansvaret på appen, som i dag, betyder att varje app som både startar aktiviteter och använder push
  måste komma ihåg det, och glömmer den det finns inget felmeddelande som avslöjar saken.
- **Tokenens fördröjning behövde ingen egen mekanism.** Issuet flaggade att aktivitetens push-token
  inte finns när `StartAsync` returnerar. Men `GetPushTokenAsync` går via `PollAsync`, som väntar i
  upp till fem sekunder — refreshen väntar alltså ut token själv. Att höja händelsen direkt vid start
  räcker, och en andra mekanism för "token har anlänt" hade varit kod utan vinst.
- **Ingen debounce.** Fingerprintet i `RefreshAsync` avgör redan om något behöver skickas, så flera
  aktiviteter i rad kostar inga extra anrop till backend.
- **En medlem på ett publikt interface.** `ILiveActivityService` ligger i `Common` och har en enda
  implementation. Alternativet — att låta `PushService` polla `Active` — hade lagt en timer i
  paketet för att undvika en rad i ett kontrakt.

## Verifierat

Fysisk iPhone 16 Pro. Aktiviteten startades från Live Activity-sidan, appen lämnades **inte**, och
`/installations` visade en ny aktivitetstoken 26 sekunder senare — utan foreground-växling och utan
"Registrera om". En `liveactivity`-push direkt därefter gick fram.

Före ändringen låg den gamla token kvar tills appen råkade registrera om sig, och utskicket
besvarades med `sent 1` utan att något hände på enheten.
