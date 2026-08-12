# Issue #119 — Startfältet enligt Sverigelistan

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/119
**Branch:** issue/119-start-field
**Status:** Completed

## Varför inte en prognos

Placeringsprognosen mättes tre gånger — #40, #113, #117 — och kom fram till samma sak varje gång:
för att träffa fyra av fem måste intervallet täcka omkring halva fältet. I H45 med 13 startande är
"2–6" upplysande. I H21 med 43 är "1–25" formellt rätt och säger ingenting.

Den enklaste och ärligaste formen av samma information är att inte förutsäga någonting: **visa
startfältet sorterat på Sverigelistan** och låt läsaren dra sin egen slutsats. Ingen modell, ingen
kalibrering, inget band att ställa in.

## Hur den fungerar

1. `GET /api/starts/event?eventId=X` — startlistan bär namn, `personId`, klubbens **id** och
   starttid. Klubb-id:t står i listan, så ingen namnuppslagning mot organisationsregistret behövs.
2. Klubbarna i fältet slås upp på Sverigelistans klubbsidor genom sessionen, **fyra parallellt**,
   cachade tolv timmar och delade mellan klasser och användare.
3. Sortera på poäng. Den som listan inte känner hamnar sist, utan påhittad placering.

Ett fält på 40 löpare spänner över ett dussin klubbar — en sida per klubb, inte en per löpare.

## Changes

- `Domain/StartField.cs` — `StartFieldRunner`.
- `Ranking/StartFieldSource.cs` — startlistan, klubbuppslagen och sorteringen.
- `Functions/StartFieldFunctions.cs` — `GET /api/competitions/{id}/field?class=…`.
- `Sources.cs` + `IOrienteraSource` + `UnreliableSource` + `FakeDataSource` + `BackendSource` +
  `MauiProgram` — det nya smala gränssnittet på de sex ställen appen kräver för hand.
- `Features/Events/EventDetailsPage` — sektionen "Fältet enligt Sverigelistan" för din klass.
- `Orientera.Backend.csproj` — `InternalsVisibleTo`, så startlistläsaren kan testas utan att bli
  publikt API.
- `StartFieldTests` + `FakeStartFieldTests` — fem tester.

## Decisions

- **Klubbsidor, inte löparsidor.** En sida per klubb i stället för en per löpare: fältet i #113
  kostade 2 209 hämtningar, det här kostar ett dussin per tävling och de delas mellan alla som
  tittar på samma lopp.
- **Fyra parallellt.** Sekventiellt tog elva klubbar 10,9 s — för nära appens timeout på 20 s för
  ett större fält. Fyra i taget är snabbt nog och fortfarande ett artigt antal samtidiga anrop.
- **Sorterat på poäng, inte på riksplacering.** Poängen är det listan räknar; riksplaceringen är en
  följd av den och saknas oftare.
- **Utan ranking sist, utan nummer.** Raden får "—" i stället för en plats i ordningen. Att sortera
  in någon på gissad nivå vore precis den påhittade precisionen som gjorde prognosen oanvändbar.
- **Kräver publicerad startlista.** Anmälningslistan (`/api/entries`) bär varken namn eller klass i
  klartext, bara `personId` och `eventClassId`. Innan startlistan finns visas ingenting.

## Verifiering

`dotnet test`: **283 gröna** (278 + 5 nya).

**Skarpt mot Eventor**, Tjällmoträffen 2026-08-11:

```
H45, 17 startande — 17 av 17 rankade, 10,9 s kallt
  1 Magnus Palm         KFUM Örebro OK     19,52  riks 274
  2 Erik Sandh          OK Roxen           33,11  riks 760
  3 Oskar Gustafsson    Leksands OK        36,21  riks 875
  4 Niklas Barsk        Björkfors GOIF     38,02  riks 942

H21, 44 startande — 40 av 44 rankade, 7,7 s
  1 Jerker Lysell        7,69
  2 Erik Berzell        10,41
  3 Leo Johansson       10,48
```

**Mot facit:** i H45 slutade de fyra som listan rankade högst på platserna 2, 3, 1 och 4. I H21 vann
Jerker Lysell, som listan hade först.

**I appen:** verifierad i simulatorn i demoläget — sektionen visar fältet med den egna raden i
accentfärg och "11 av 14 finns på listan" — och mot skarp backend, där en lokal tävling utan
publicerad startlista korrekt visar ingenting alls.

### Vad körningen avslöjade

**Jag höll på att felsöka en bindning som var hel.** Sektionen syntes inte i demoläget, och min
första gissning var `IsVisible`-bindningen. Den var rätt: demoidentiteten står i D45, och demodatat
har inga D45-löpare, så fältet var tomt och sektionen dolde sig precis som den ska. Det syntes först
när jag tvingade fram den och den var *fylld* med D21-rader. Rätt beteende ser ut som en bugg när
man inte vet vilken klass som är vald.

## Kvar

- **Anmälda före startlistan.** `/api/entries` har `personId` men varken namn eller klassnamn — det
  senare kräver `/api/eventclasses` per tävling. Görbart, men en egen sak.
- **Sessionen krävs.** Utan `Ranking:DemoSessionPersonId` svarar sektionen tomt, eftersom klubbsidor
  anonymt är nästan tomma (13 % täckning, mätt i #111).
