# Issue #33 — Orientera M2 — LiveResults, SP-04-matchning och identifierad person

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/33
**Branch:** issue/33-liveresults
**Status:** Completed

## Plan

M2:s kärna: live på riktig data. Det svåra är inte att hämta LiveResults utan att veta *vilken*
LiveResults-tävling som hör till vilket Eventor-event (SP-04), och vilken löpare i listan som är
jag eller någon i Min grupp.

1. `LiveResultsClient` + normalisering till `LiveSnapshot`/`LiveEntry`.
2. `CompetitionMatcher` — SP-04, med förtroendemått och rätt att svara "ingen matchning".
3. `RunnerIdentity` — löparmatchning på namn och klubb, lokalt i appen.
4. Lokal identitet: jag pekar ut mig själv; inget konto, inget som lämnar telefonen.
5. BFF-endpoints för live och appens `ILiveSource` mot dem.

## Open Questions

Inga kvar. Identiteten är lokal eftersom Eventors inloggning är organisationscentrerad och hör
till M5 — och eftersom live- och resultatlistor ändå identifierar en löpare på namn och klubb.

## Changes

- `Orientera.Domain/RunnerIdentity` — namn och klubb normaliserade till en jämförbar identitet.
  Namnet avgör; klubben skiljer bara namnar åt. Hanterar "Efternamn, Förnamn", versaler,
  diakriter, bindestreck och dubbla mellanslag.
- `ILiveSource.GetSnapshotAsync` tar nu en valfri klass. En klass är ett anrop uppåt; allt annat
  kostar ett anrop per klass i tävlingen, och det är LiveResults enda sökväg.
- `Orientera.Backend/LiveResults`:
  - `LiveResultsClient` — publikt API, ingen nyckel, och en `Repair` som escapar de råa
    kontrolltecken tjänsten skickar inuti strängvärden.
  - `LiveResultsNormalizer` — hundradels sekunder, starttid som klockslag, radiokontroller,
    statuskoder och de fält som är tal, sträng eller tom sträng om vartannat.
  - `CompetitionMatcher` — SP-04: datum som villkor, arrangör och namn som signaler, tröskel
    0,6 och inget svar när två kandidater är lika bra.
  - `LiveSource` — matchning, klasser och rader, var och en med sin cachetid.
- `Functions/LiveFunctions` — `/api/live`, `/api/competitions/{id}/live` och
  `/api/competitions/{id}/live/match`.
- `Upstream/UpstreamUnavailableException` — ersätter `EventorUnavailableException`; nu har BFF:en
  två källor som kan vara nere.
- Appen: `BackendSource` implementerar `ILiveSource`; `LivePage` matchar mig och Min grupp via
  `RunnerIdentity` i stället för person-id; `LocalIdentityStore` + `IdentitySheet` ("Vem är du?")
  och `DataSourceInfo` som säger på skärmen vilken källa appen kör mot.
- Tester: `RunnerIdentityTests`, `CompetitionMatcherTests`, `LiveResultsNormalizerTests` — 27
  nya, 186 totalt. Fixturerna är **inspelade skarpa svar** från LiveResults.
- Eventor-fixturen har fått Norrlandsmästerskapen medel (2026-08-09), tävlingen som faktiskt
  finns i LiveResults — så att de två fixturuppsättningarna berättar samma historia.

## Decisions

- **Löparmatchningen sker i appen, inte i backend.** Alternativet vore att skicka mitt namn och
  hela Min grupps namn till en server för att få raderna märkta. Det vore personuppgifter ut ur
  telefonen utan vinst.
- **Ingen matchning är ett giltigt svar.** Två kandidater inom 0,05 i förtroende ger inget svar
  alls: en helg där varje lopp heter samma sak är precis det fallet, och fel livelista under
  rätt namn är värre än ingen.
- **Datum är ett villkor, inte en signal.** En tävling körs på sin dag.
- **Klubben kan inte krävas.** "Gävle OK" och "Gävle Orienteringsklubb" är samma klubb för en
  människa och olika strängar för en dator, så en okänd eller avvikande klubb får inte ensam
  bryta en matchning — men två löpare med samma namn får inte slås ihop.
- **Identiteten är lokal och räcker inte till anmälningar.** Namn och klubb pekar ut mig i en
  start- eller resultatlista. Att hävda en *anmälan* kräver Eventors auth-modell, som är M5.
- **Cachen ligger på backend, inte i telefonen.** LiveResults cachar 15 sekunder och säger det;
  BFF:en speglar den tiden och delar ett anrop mellan alla klienter.

## Vad körningen avslöjade

- **Payloaden är inte giltig JSON.** `getcompetitions` innehåller råa tabbar inuti
  tävlingsnamn, och varje strikt parser vägrar hela svaret. Utan `Repair` finns ingen kalender
  alls. Fixturen behåller tabben så att regeln inte kan tappas bort.
- **Placeringen saknades för dem som gått i mål.** Jag satte `Position` bara för löpare ute på
  banan, men livelistan sorterar på den — resultatet blev en lista i godtycklig ordning på
  skärmen. `Position` är nu ställningen i klassen i båda lägena.
- **"M0 kör på fake-data" stod kvar på Jag-sidan** även när appen körde mot backend. Texten
  kommer nu ur `DataSourceInfo`: en demokörning ska inte kunna läsas som skarp data.

## Verifiering

- **186 tester gröna**, varav 27 nya.
- **Skarp SP-04-matchning:** Eventor-fixturens Norrlandsmästerskapen medel (Gävle OK,
  2026-08-09) matchas mot LiveResults tävling 37308 med förtroende 1,0 — hämtat live ur hela
  landets livekalender (7 562 tävlingar).
- **Appen mot riktig live-data** på iPhone 17 Pro-simulator (iOS 26.2): Live visar
  "Norrlandsmästerskapen, medel · LIVE · uppdaterad för 0 sek sedan" med riktiga löpare, klubbar,
  klasser, tider och "Kontroll 1088" för dem med radiopassering men utan måltid.
- **Identiteten:** med namn och klubb satta till en löpare som faktiskt finns i listan visar
  Min grupp exakt den raden, accentmarkerad som *jag* — matchad på namn och klubb, utan id och
  utan att något lämnat telefonen. Jag-sidan visar samma identitet.

## Kvar

Notisgrunden är M2:s andra halva och bygger på den här matchningen. Live-vyn sorterar fortfarande
på placering rakt över klassgränserna när man valt "Alla", vilket blev synligt först med riktig
data — värt en egen genomgång av livelistans gruppering.
