# Issue #44 — Livelox (SP-07)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/44
**Branch:** issue/44-livelox
**Status:** Completed — i den omfattning API:et tillåter

## Plan

Ärendet planerade tre saker: matchning Eventor→Livelox på datum/position/namn, normalisering av
IOF XML-banor till `Course`/`Control`, och en deep-link. Besked från Livelox (Mats) plus egna
anrop mot API:et ändrade två av dem.

## Vad API:et faktiskt svarar

Verifierat med nyckeln, inte antaget:

| Anrop | Utfall |
|---|---|
| `GET /events/EventorSweden%3A{eventorId}-1` | **200** med namn, url, klasser och deras viewer-länkar |
| samma för ett id Livelox inte har | **404** |
| `GET /orienteering/courses/iofxml?eventId=…` | **403** — *"You do not have the scope 'courses.read'"* |
| kart- och ruttendpoints | **404** — de finns inte |

Tre slutsatser:

1. **Ingen matchning behövs.** Livelox adresserar svenska tävlingar med Eventors eget id:
   `EventorSweden:{id}-{etapp}`. Hela SP-04-liknande gissningen på datum och position utgår.
2. **Bandata är inte omöjlig — den är scope-spärrad.** Endpointen finns och svarar 403 på
   *scope*, inte 404. Det är en fråga till Livelox, inte ett tekniskt hinder.
3. **Kartor och rutter finns inte att hämta.** Inga endpoints alls, i linje med vad Livelox
   säger: de behåller dem för upphovsrätt, attribution och integritet.

## Changes

- `Orientera.Domain/Sources/Livelox.cs` — `LiveloxLink`, `LiveloxClass`, `ILiveloxSource`.
- `Orientera.Backend/Livelox/LiveloxSource.cs` — slår upp tävlingen, cachar ett dygn.
- `Orientera.Backend/Functions/LiveloxFunctions.cs` — `GET /api/competitions/{id}/livelox`.
- `Configuration/LiveloxOptions.cs` + `local.settings.example.json`.
- `BackendSource` / `FakeDataSource` / `UnreliableSource` — kontraktet genom appen.
- `EventDetailsPage` — ett **Vägval**-kort som öppnar Livelox, med attribution.

## Decisions

- **Ingen matchare.** Det fanns inget att gissa. `LiveloxSource` har därför ingen motsvarighet
  till `CompetitionMatcher`, och kommentaren i filen säger varför så att nästa läsare inte bygger
  en.
- **Etapp 1.** Eventors kalender bär inget etappnummer, så flerstegstävlingar länkar till första
  loppet i stället för att gissa vilken etapp löparen menar.
- **404 är ett svar.** "Livelox har aldrig hört talas om den här tävlingen" är ett faktum om
  tävlingen, inte ett fel i anropet — och det cachas lika länge som ett träffat svar.
- **Ett tomt event är ingen länk.** Ett Livelox-event utan deltagare och utan karta är ett skal;
  kortet visas bara när det finns något på andra sidan.
- **Livelox får inte fälla sidan.** Ett uteblivet svar loggas och blir inget kort. En
  tävlingssida som inte laddar för att en frivillig länk inte kunde kontrolleras vore värre än
  en sida utan länken.
- **Demoläget svarar tomt.** En länk från en påhittad tävling till någon annans riktiga event
  vore demot som ljuger om omvärlden.

## Verifiering

`dotnet test`: 250 gröna.

**Mot skarpt Livelox-API via BFF-stubben:** Norrlandsmästerskapen lång → 233 deltagare, karta
finns, klasser med viewer-länkar. Höglandets Veteran-OL → 5 deltagare. Ett påhittat id → 404.

**iPhone 17 Pro-simulator (iOS 26.2):** tävlingssidan för NM lång visar **VÄGVAL → "233 löpares
vägval i Livelox"** med raden "Kartor och rutter visas av Livelox" under.

**En krasch under vägen:** `ILiveloxSource` registrerades inte i DI, så tävlingssidan dog vid
aktivering. De smala gränssnitten pekas ut ett och ett i `MauiProgram`; ett nytt måste läggas till
för hand.

## Kvar att fråga Livelox

Scopet `courses.read` på nyckeln. Med det blir punkt 2 i ärendets ursprungliga omfattning möjlig:
banor och kontroller till `GetCourseAsync`, utan att röra karträttigheter.
