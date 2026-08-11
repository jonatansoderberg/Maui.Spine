# Issue #65 — Live: en löpare som aldrig startat visas som "Start 00:00"

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/65
**Branch:** issue/65-live-unknown-start-time
**Status:** Completed

## Plan

LiveResults svarar `0` på `start` för en löpare utan starttid, och eftersom `LiveEntry.StartTime`
är en icke-nullbar `DateTimeOffset` blir noll hundradelar sedan midnatt till tävlingsdagens
midnatt. Vyn får ett giltigt klockslag och visar "Start 00:00".

Kontraktet ska kunna säga att det inte vet:

- `LiveEntry.StartTime` blir `DateTimeOffset?`, och `StartOf` svarar null när `start` saknas.
- Statusraden i Live säger "Ej start" utan starttid, "Start 10:24" med.
- Hem säger samma sak när det är användaren själv.
- Sorteringen lägger dem utan starttid sist, där statusen redan placerar dem.

Fake-källan har alltid en starttid och ändras inte i sak.

## Changes

- `LiveEntry.StartTime` är `DateTimeOffset?` och inte längre `required` (se Decisions).
- `LiveResultsNormalizer.StartOf` svarar null när `start` saknas, i stället för att lägga till noll
  hundradelar på tävlingsdagens midnatt.
- `LivePageViewModel` — statusraden säger "Ej start" utan starttid, "Start 10:24" med. Sorteringen
  lägger dem utan starttid sist.
- `HomePage.ViewModel` — "Din start 10:24" när tiden finns, "Du står som ej start i D21" när den
  inte gör det.
- Fixturen `classresults-vit20.json` spelades in från skarpa svar: en hel klass med två löpare som
  aldrig startade. Den fanns inte i något tidigare inspelat svar.
- Tester — en löpare utan starttid har ingen, en med samma status men en starttid har kvar sin, och
  en livepost utan starttid överlever vägen över nätet.

## Decisions

- **Fältet är en tom sträng, inte noll.** Issuen antog `0`; skarp data säger `""`. Jag skannade alla
  trettio klasser i Norrlandsmästerskapen medel: tre löpare i tre klasser har `start: ""` med
  `status: 1`. Mekanismen är densamma — `Duration` svarar null för båda — men det var `?? TimeSpan.Zero`
  i `StartOf` som gjorde midnatt av det, inte tolkningen av fältet.
- **`StartTime` får inte vara `required`.** Det är den ändring körningen tvingade fram, och den är
  inte kosmetisk: `OrienteraJson` sätter `DefaultIgnoreCondition = WhenWritingNull`, så BFF:en
  utelämnar egenskapen helt för en löpare utan starttid. En `required`-egenskap som saknas i JSON
  får `System.Text.Json` att kasta, `BackendSource` gör en `SourceUnavailableException` av det, och
  hela livevyn visade **"Ingen anslutning"**. En obligatorisk egenskap som får vara null är en
  motsägelse över det här formatet — de andra frivilliga fälten på `LiveEntry` är inte heller
  `required`. `BackendSourceTests` pinnar det nu.
- **"Ej start", inte "Okänd starttid".** Livekällan rapporterar det så för en löpare som fanns i
  anmälan men aldrig kom iväg. Det är vad orienteraren kallar det, och det är vad listan ska säga.
- **De utan starttid hamnar sist.** Sorteringen använder `DateTimeOffset.MaxValue` för dem, vilket
  är samma plats som deras status redan ger dem.

## Verifiering

`dotnet test`: 214 gröna.

**iPhone 17 Pro-simulator (iOS 26.2) mot skarp data** via BFF-stubben: klassen Blå 3,0 i
Norrlandsmästerskapen medel visar Maria Falk sist med **"Ej start"** under klubben och `—` i
målkolumnen. Före ändringen stod det "Start 00:00" på samma rad.

Körningen avslöjade två saker:

1. **Hela vyn slog om till "Ingen anslutning"** så fort en klass med en icke-startande löpare
   hämtades. Det var `required` mot `WhenWritingNull` (se Decisions) — ett fel som inte syns i
   något test som bara går genom normaliseringen, och som ser ut som ett nätverksfel i appen.
2. **BFF-stubben i scratchpad serverade en förfrågan i taget**, så appens parallella anrop (live
   plus klass) fick "connection reset" och lästes också som offline. Det är stubbens brist, inte
   appens; den hanterar nu varje förfrågan för sig. Värt att minnas vid nästa verifiering.

Fallet "har inte startat än" — samma status, men med en starttid — finns bara i inspelad data just
nu, eftersom alla i den skarpa tävlingen har gått i mål. Det täcks av testet mot H21-fixturen.
