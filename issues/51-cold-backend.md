# Issue #51 — Kall backend svarar långsammare än appens tidsgräns

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/51
**Branch:** issue/51-cold-backend
**Status:** Completed

## Plan

Ärendet beskrev två fel: att backend hämtar hela organisationslistan (2,2 MB, 3 074
organisationer) innan den kan besvara en kalender, och att ett avbrutet anrop i appen inte ger
något felläge.

## Changes

- `Orientera.Backend/Eventor/DirectoryWarmup.cs` — en `IHostedService` som hämtar
  organisationslistan medan värden startar. Registrerad i `Program.cs`.
- `BackendSourceTests` — två nya tester som spikar skillnaden mellan en källa som inte svarar och
  en anropare som ger upp.

Ingen appkod ändrad.

## Decisions

- **Nedladdningen måste ske ändå; frågan är vem som väntar.** `EventorSource.GetCompetitionsAsync`
  väntar in `DirectoryAsync` innan den svarar (rad 42) — klubbnamn, distrikt och märken kommer
  därifrån, och en kalender utan klubbnamn är inte en kalender. Att svara med tom katalog hade
  gjort svaret snabbt och sämre. Uppvärmningen flyttar bara väntan från första löparen till
  värdens uppstart.
- **En misslyckad uppvärmning stoppar inte starten.** En backend som vägrar starta för att
  Eventor är segt är sämre än en kall: alla andra rutter fungerar, och första riktiga anropet
  försöker igen.
- **Andra halvan var redan rätt.** Ärendet påstod att `BackendSource` medvetet inte översätter
  avbrott till `SourceUnavailableException`. Det stämmer inte om den nuvarande koden: den fångar
  `TaskCanceledException` när anroparens token *inte* är avbruten, vilket är precis en
  `HttpClient`-timeout. Beteendet fanns men var otestat, så det är testat nu i båda riktningarna
  i stället för ändrat.

## Verifiering

`dotnet test`: 242 gröna (240 + 2 nya).

**Mot skarp Eventor genom BFF-stubben:** första kalenderanropet i en kall process tog **1,3 s**,
nästa 0,002 s. Beroendet är alltså verkligt — hela organisationslistan hämtas före kalendern —
men Eventor svarade snabbt idag.

**Inte verifierat:** att tidsgränsen faktiskt slås ut. Symtomet i ärendet, en kall backend som tar
över tjugo sekunder, gick inte att återskapa idag. Fixen är riktig oavsett — hämtningen börjar vid
start i stället för vid första anropet — men jag har inte sett den rädda ett anrop som annars hade
gett upp. `DirectoryWarmup` har heller inte körts i en riktig Functions-värd; Core Tools saknas på
maskinen och BFF-stubben har ingen värd som kör `IHostedService`. Det som är verifierat är att
klassen kompilerar, är registrerad, och att den kalla kostnaden den flyttar är mätt.
