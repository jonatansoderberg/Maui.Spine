# Issue #109 — Klubbaktiviteter: stafetter och träningar med anmälningsstopp

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/109
**Branch:** issue/109-club-activities
**Status:** Completed

## Plan

Uppgiften löd "`/api/activities` finns i Eventors dokumenterade API — troligen ingen skrapning
alls". Första mätningen sa något annat.

## Det dokumenterade API:et svarar 403

| Anrop | Svar |
|---|---|
| `/api/organisation/115` | **200**, 1 263 byte |
| `/api/activities?organisationIds=115&fromDate=…&toDate=…` | **403** |
| `/api/activities` | **403** |
| `/api/activity/1` | **403** |

Samma nyckel, samma körning, sekunder isär. Det är alltså behörighet och inte parametrar:
endpointen finns i API:et men inte för oss. Den frågan är samma sort som rankingens — en fråga
till förbundet, inte ett tekniskt problem.

Efter beslut (alternativ 3) läses webbsidan i stället.

## Vad sidan ger

`/Activities?organisationId=115`, bakom samma session som Sverigelistan redan använder. Anonymt
svarar den med noll byte, så sessionen är inte en genväg utan enda vägen.

Sidan grupperar under en rubrik per organisation — klubben, distriktet, förbundet — och varje rad
har namn, starttid, anmälningsstopp, antal anmälda och en `Anmäl`-länk när det fortfarande går.

## Changes

- `Domain/ClubActivity.cs` — en aktivitet. Starttid är frivillig: en stafettanmälan är inget möte.
- `Activities/ActivityPageParser.cs` — läser rubriker och rader i dokumentordning.
- `Activities/ClubActivitySource.cs` — session, hämtning, cache en timme.
- `Functions/ActivityFunctions.cs` — `GET /api/activities`.
- `Eventor/EventorSession.cs` — **utbruten** ur `RunnerRankingSource`, som nu har två användare.
- `Sources.cs` + `IOrienteraSource` + `UnreliableSource` + `FakeDataSource` + `BackendSource` +
  `MauiProgram` — det nya smala gränssnittet, på de fem ställen appen kräver för hand.
- `Features/Profile/ProfilePage` — sektionen "Klubbaktiviteter", närmast deadline först.
- `FakeDataset` — fyra aktiviteter i demodatat.
- `ActivityPageParserTests` — sex tester mot en riktig sparad sida.

## Decisions

- **Datumen läses ur `title`, inte ur texten.** Sidan skriver "om 11 dagar" och "för 2 dagar sedan"
  för det som ligger nära, men den absoluta tiden står alltid i attributet
  (`söndag 23 augusti 2026 klockan 20:00`). Att läsa texten hade betytt kalenderaritmetik mot
  Eventors läsklocka i stället för mot vår.
- **Sessionen bröts ut.** `RunnerRankingSource` ägde inloggningen privat och nu behövde två källor
  den. `EventorSession` är den enda platsen som mintar en engångslänk, och därmed den enda plats
  där gränsen står skriven.
- **Jag-fliken, inte Hem eller Tävlingar.** Hem har fyra platser och en prioriteringsregel som en
  klubblista inte hör hemma i; Tävlingar är en grupperad `CollectionView` av tävlingskort och en
  aktivitet är inte en tävling. Jag-fliken är din klubbtillhörighet, och sidan är en enkel
  `ScrollView` där en sektion till inte kan välta något.
- **Bara det som ligger framåt.** En stafett som stängde i april är inget att göra något åt. Fem
  av tretton rader återstår skarpt.
- **`IsOpen` kommer från länken, inte från klockan.** Sidan visar `Anmäl` eller inte, och
  arrangören kan både stänga tidigt och öppna igen. Rödmarkeringen hänger på det, så appen skriker
  aldrig om något du ändå inte kan göra.
- **Cachetid en timme, inte tolv.** Anmälningar droppar in under dagen. Rankingens tolv timmar
  passar en lista som räknas om en gång per dygn; den här ändrar sig oftare än så.

## Verifiering

`dotnet test`: **272 gröna** (266 + 6 nya).

**Skarpt mot Eventor via BFF-stubben**, `GET /api/activities` — 13 aktiviteter, rätt grupperade och
med rätt tidszon (+02:00 på sommaren, +01:00 på februari 2027):

```
Gävle OK        | DM-Stafett 30/8 Ockelbo | stop 2026-08-23T20:00+02:00 |  6 anmälda | öppen True
Gävle OK        | 10-mila 2027, Västervik | stop 2027-02-10T00:00+01:00 | 11 anmälda | öppen True
Gästriklands OF | Träningsdag inför USM   | stop 2026-08-16T22:00+02:00 |  3 anmälda | öppen True
```

**I simulatorn (iPhone 17 Pro)**, mot skarp stubb och i demoläget. Fem aktiviteter, närmast först,
med den som stänger inom en vecka i rött.

### Vad körningen avslöjade

1. **Namnet upprepade organisationen.** Eventor döper distriktets rader till "Träningsdag inför USM
   (Gästriklands OF)", och raden under säger redan "Gästriklands OF". Parentesen kapas i vyn —
   parsern behåller det sidan skriver.
2. **Årtal saknades på det som ligger långt bort.** "anmälan stänger ons 10 feb." om ett datum 2027
   läses som februari i år. Stafettanmälningar stänger ett och ett halvt år i förväg, så datum i
   ett annat år får sitt årtal.
3. **Demodatat var inte självkonsekvent.** Två aktiviteter hade anmälningsstopp i framtiden men
   `IsOpen = false`, vilket dolde rödmarkeringen i det läge de flesta ser appen i. Rättat.

## Kvar

- **`/api/activities`** är det som borde användas. 403:an är mätt och dokumenterad, och blir en
  konkret punkt till förbundet vid sidan av rankingfrågan: *vad krävs för att en klubbnyckel ska
  få läsa sin egen klubbs aktiviteter?*
- **Anmälan sker i Eventor.** Raden länkar dit; appen har ingen inloggning att anmäla med.
