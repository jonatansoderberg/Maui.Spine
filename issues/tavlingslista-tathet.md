# Tävlingslistan: täta rader med datumstöd

**GitHub:** _issue ej skapad än_
**Branch:** issue/tavlingslista-tathet
**Status:** Not started

> Det här är en **uppdragsbeskrivning**, skriven för att kunna utföras utan kännedom om
> samtalet den kom ur. Läs `CLAUDE.md` först — kodstandard, commit-språk och
> issue-arbetsflödet gäller.

## Uppdraget

Bygg om tävlingslistan i `samples/Orientera` från ett kort per tävling till **täta rader med
datumstöd i vänsterkanten**. Ingen ny vy, ingen lägesväxel — samma lista, tätare form.

## Varför

Listan är för blaffig. Varje tävling är ett `Card` med fyra staplade rader plus en badgerad,
och på en iPhone 17 ryms **tre tävlingar per skärm**. Nya Eventor (`https://eventor.se`,
"Arrangemangskalender") visar **7–9** i samma yta med samma information, och den jämförelsen
är vad som utlöste det här arbetet.

## Vad nya Eventor gör bättre (mätt 2026-08-24)

1. **Datumet är en ryggrad, inte en etikett.** En smal kolumn längst till vänster med
   dagnumret stort och veckodagen under. Den ritas **en gång per dag** — följande tävlingar
   samma dag lämnar kolumnen tom. Vi skriver i dag "MÅNDAG" inuti varje kort.
2. **Inga kort.** Rader på en delad yta med hårfina avdelare, kant till kant. Vår `Card` per
   rad kostar padding × 2, radie och yta för varje enskild tävling.
3. **Disciplinmärket har egen kolumn** i stället för att ligga inbäddat i en metamening.
4. **Bara det som gäller visas.** Ingen chip för "inte mästerskap". Vi skriver alltid ut nivå
   och distans.

**Kopiera inte:** deras disciplin är enbart en ikon med alt-texten "Event discipline". Det är
sämre än vårt — behåll ordet bredvid märket (P8, D1).

## Radens anatomi (beslutad)

```
MEST RELEVANT
──────────────────────────────────────
 24  ◆  Trimtex Cup #4              ☆
 mån    Valbo AIF · Gästrikland · 12 km
        ANMÄLD
──────────────────────────────────────
 29  ◆♛ DM, medel, Gästrikland      ☆
 lör    Ockelbo OK · Gästrikland · 41 km
──────────────────────────────────────
 30  ◇  DM, stafett, Gästrikland    ★
 sön    Ockelbo OK · Gästrikland · 41 km
        ANMÄLAN ÖPPEN
──────────────────────────────────────
```

- **Datumkolumn** — dagnummer i tabulär siffra, veckodag under i `CaptionLabel`.
- **Märkeskolumn** — disciplinens form, och mästerskapspokalen under den när den finns.
- **Innehåll** — titel på en rad med `TailTruncation`; metaraden som *en* rad
  (klubb · distrikt · avstånd); badgeraden **bara när det finns en badge**.
- **Stjärnan** — 44 pt, oförändrad.
- Avdelare i stället för kort.

## Regeln som får båda ordningarna att fungera

Det finns en fälla här. `QuickFilter.ForYou` ("För dig", förvalet) är **relevansordnad** och
ligger i en enda sektion, "Mest relevant" — datumen hoppar. Alla andra filter är
**kronologiska** och sektioneras av `EventTimeline.NameFor` ("Denna vecka", "September").

Regeln som täcker båda, och som är exakt den nya Eventor använder:

> **Rita datumet om det skiljer sig från raden ovanför.**

I en kalender kollapsar det hårt; i "För dig" kollapsar det nästan aldrig. Samma grid, ingen
förgrening på filter.

## Vad som inte får tappas

Nya Eventor vet inte var du bor och vet inte vem du är. Vi gör det, och det är hela
relevanspremissen:

- **Avståndet till arenan** ("12 km").
- **Ditt läge** — ANMÄLD, Min grupp, Live, och kontexttillståndet.
- **Stjärnan** (intressemarkering).
- **Disciplinen som ord** bredvid märket, inte bara märket.
- **En beskrivning per rad för skärmläsaren.** Kortet är i dag *ett* element
  (`EventCard.Accessibility`) — annars blir varje rad sex svep. Stjärnan måste förbli ett
  **syskon** till raden, inte ett barn: en `Description` på en layout gör dess barn oåtkomliga
  på iOS. Kommentaren som förklarar det står redan i `EventsPage.View.xaml`.

## Filer

- `samples/Orientera/Features/Events/EventsPage.View.xaml` — `ItemTemplate` och
  `GroupHeaderTemplate`. Huvudarbetet.
- `samples/Orientera/Features/Events/EventCard.cs` — `DateLabel` blir dagnummer + veckodag,
  och raden behöver veta om datumet ska ritas.
- `samples/Orientera/Features/Events/EventsPage.ViewModel.cs` — sätter den flaggan när
  sektionerna byggs (`BuildAsync`, runt rad 305).
- `samples/Orientera/Presentation/Format.cs` — om dagnummer/veckodag behöver egna hjälpare.

Överväg om raden ska byggas på `Controls/ListRow.cs`. Den har anatomin
`[identitet] [primär/sekundär] [värde] [→]`, men den här raden behöver *två* ledande kolumner
(datum och märke) och ingen värdekolumn. Bedöm — tvinga inte in den.

## Öppen fråga att ta ställning till

Nya Eventor har ett **band** som sektionshuvud: mörkblå rad med "v 34" och "Augusti". Vårt
sektionshuvud är en diskret `SectionLabel`. Ett band ger listan tydligare ryggrad men mer
färg. Avgör när raderna är byggda och det går att se dem tillsammans — inte innan.

## Krav

- Följ `docs/design/designprinciper.md`: P9 (alla listrader har samma anatomi), P8, P10, och
  D1 (grönt bär handling, orange bara det som brinner).
- **Inga `DataTrigger` för temafärger.** En trigger minns färgen den ersatte och återställer
  fel tema efter ett byte. Se `ChipView` och `ListRow` för mönstret med två förbyggda utseenden.
- Kommentarer bara där *varför* inte är uppenbart.

## Klart när

1. `dotnet build samples/Orientera/Orientera.csproj` går rent för `net10.0-ios`,
   `net10.0-android` och `net10.0-maccatalyst` — **noll `MAUIG2045`** (bindningskontrollen;
   den hittade tre döda bindningar senast och är värd att läsa).
2. `dotnet test` från `samples/Orientera.Tests` är grönt.
3. Appen är **körd i simulatorn** och antalet tävlingar per skärm är räknat före och efter.
   Målet är minst en fördubbling. Det här steget är inte valfritt: de tre senaste
   UI-felen i det här repot syntes varken i bygget eller i testerna, bara i att köra appen.
4. Skärmläsaren läser en rad som en mening, och stjärnan går fortfarande att nå.
5. Den här filen har en `## Changes` och en `## Decisions` enligt `CLAUDE.md`.

## Plan
<!-- Skrivs innan kod. -->

## Changes

## Decisions
