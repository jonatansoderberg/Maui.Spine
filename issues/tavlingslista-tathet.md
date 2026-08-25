# Tävlingslistan: täta rader med datumstöd

**GitHub:** _issue ej skapad än_
**Branch:** issue/tavlingslista-tathet
**Status:** Completed

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

## Öppen fråga att ta ställning till — **avgjord: ingen bandhuvud**

Nya Eventor har ett **band** som sektionshuvud: mörkblå rad med "v 34" och "Augusti". Vårt
sektionshuvud är en diskret `SectionLabel`. Ett band ger listan tydligare ryggrad men mer
färg. Avgör när raderna är byggda och det går att se dem tillsammans — inte innan.

Sett tillsammans med raderna: **behåll etiketten.** Se `## Decisions`.

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

Raden byggs i XAML i `ItemTemplate`, inte på `Controls/ListRow.cs`. `ListRow` har en ledande
kolumn, en textkolumn, en värdekolumn och en chevron. Den här raden har *två* ledande kolumner
(datum, märke), ingen chevron och en badgerad som bara ibland finns. Att göra `ListRow` generisk
nog vore en abstraktion för ett enda anrop.

**Rutnätet** (innehållet, med stjärnan som syskon utanför precis som i dag):

```
kolumner:  44        Auto   *                        Auto     44 (stjärnans yta)
rad 0:     [24]      [◆]    Trimtex Cup #4
rad 1:     [mån]     [♛]    Medel · Valbo AIF · …    12 km
rad 2:                      ANMÄLD  MIN GRUPP
```

- Datumkolumnen och märkeskolumnen spänner raderna och toppjusteras.
- Titeln och metaraden får `TailTruncation`.
- Avståndet flyttar ut till höger i egen kolumn. Det är relevanspremissen och får inte vara
  det som trunkeras bort — högerställt bildar det dessutom en läsbar kolumn nedför listan.
- Badgeraden ritas bara när det finns en badge (`HasBadges`).

**Kortet försvinner.** `CollectionView` får `SurfaceCard` som bakgrund och går kant till kant,
`ItemSpacing` blir 0, och varje rad avslutas med en hårfin `Divider`. Det är den delade ytan.

**Datumets kollapsregel.** `EventCard.ShowDate` sätts när sektionerna byggs: rita datumet om
det skiljer sig från raden ovanför, och alltid på sektionens första rad. Samma regel för alla
filter — `ForYou` kollapsar nästan aldrig, en kalender kollapsar hårt.

**Månaden.** Ett blott dagnummer räcker i en kalender där sektionshuvudet säger "September",
men inte i "Mest relevant", som varken har månad i huvudet eller ordning i datumen. Därför en
tredje mikrorad i datumkolumnen — månadens förkortning — ritad efter *samma* regel: när den
skiljer sig från raden ovanför. I en kalender syns den en gång per månad, i "För dig" när
månaden hoppar.

**Nivåordet.** Punkt 4 i mätningen är att Eventor bara visar det som gäller. "Nationell" på var
tredje rad säger inget som filtret inte redan säger. Nivån skrivs ut som ord bara när den
skiljer ut sig — mästerskap och internationell — och står då bredvid pokalen (P8). Nivån finns
kvar i skärmläsarmeningen för varje rad.

**Disciplinen** behåller sitt ord (P8) som metaradens första led, rakt under sitt märke.

**Skärmläsaren.** `Accessibility` är oförändrad i sin form och läser fortfarande hela datumet
via `DateLabel`, som blir en ren talsträng och slutar ritas. Stjärnan förblir syskon.

## Changes

- `EventsPage.View.xaml` — kortet ersatt av en rad på en delad yta. `CollectionView` fick
  `SurfaceCard` som bakgrund och går kant till kant, `ItemSpacing` är 0, och varje rad slutar
  med en hårfin `Divider`. Rutnätet är `40 / 20 / * / 44`: datumkolumn, märkeskolumn, innehåll,
  och 44 pt reserverade åt stjärnan.
- Titeln fick `TailTruncation` och `BodyStrongLabel` i stället för `Heading2Label`.
- Metaraden är *en* rad — disciplinen som ord, klubbmärket, klubben och distriktet — med
  **avståndet högerställt i egen kolumn**, så att trunkeringen äter distriktet och aldrig km.
- Badgeraden ritas bara när det finns en badge (`HasBadges`).
- `EventCard` — `DateLabel` ersatt av `DayLabel`, `WeekdayLabel`, `MonthLabel` för kolumnen och
  `SpokenDate` för skärmläsaren. Nya `Date`, `ShowDate`, `ShowMonth`, `MetaLine`, `HasBadges`.
  `OccurrenceLabel`/`IsRecurring` blev `SpanLabel`/`HasSpan` och bär nu både "6 tillfällen" och
  "25–30 aug." — allt datumkolumnen inte kan hålla.
- `EventTimeline` — kollapsregeln som `DrawsDate`/`DrawsMonth`, två rena funktioner över två
  datum. `EventSection.Append` sätter flaggorna från dem när raden läggs in.
- `Format` — `DayNumber`, `Weekday`, `MonthShort`.
- `Typography.xaml` — `MicroLabel` för månaden under veckodagen.
- `EventTimelineTests` — fem fall för kollapsregeln (första raden, samma dag, ny dag i samma
  månad, månadsbyte åt båda hållen, samma månad ett år senare).

## Decisions

**Raden byggdes inte på `ListRow`.** `ListRow` är `[identitet] [primär/sekundär] [värde] [→]`.
Den här raden har två ledande kolumner, ingen chevron och en badgerad som ibland saknas. Att
göra `ListRow` generisk nog hade varit en abstraktion för ett enda anrop.

**Nivåordet togs bort ur raden.** Det var punkt 4 i mätningen — "bara det som gäller visas" —
och den första versionen som behöll ordet visade `Natt · Mästerskap · OK Ha…`: nivån trängde ut
arrangören. Nivån säger dessutom ingenting nytt just där den är intressant, eftersom pokalen
står i märkeskolumnen och titeln redan börjar med "DM". "Nationell" på varannan rad säger det
filterchipsen säger. **Ordet finns kvar i skärmläsarmeningen** (`MetaLabel`), som varken har
pokalen i synfältet eller chipsen. Disciplinen behöll sitt ord — den var uttryckligen skyddad.

**En tredje mikrorad i datumkolumnen: månaden.** Uppdragsbeskrivningens anatomi har två rader,
och de räcker i en kalender där sektionshuvudet säger "September". De räcker inte i "Mest
relevant", som varken har månad i huvudet eller ordning i datumen — där stod annars "4", "6",
"24" utan att något sa att de två första är september. Månaden ritas efter *samma* regel som
datumet: när den skiljer sig från raden ovanför. I en kalender syns den en gång per månad.

**Året jämförs, inte bara månadsnumret.** Augusti 2027 under augusti 2026 hade annars lämnat
"24 mån" stående för ett datum tolv månader bort. Egen testfall.

**Datumkolumnen bär en dag, inte ett spann.** Ett flerdagarsarrangemang och en serie får sitt
spann som badge ("25–30 aug.", "6 tillfällen") i stället för i kolumnen. En ryggrad som byter
bredd rad för rad är ingen ryggrad, och "24–26" i tabulär siffra gör kolumnen bredare för alla.

**Kollapsregeln bor i `EventTimeline`, inte i vyn.** Testprojektet länkar in `Services/Grouping`
men kan inte referera MAUI-typerna, så en regel som satt i `EventSection` inte hade gått att
testa. Två rena funktioner över `DateOnly?` och `DateOnly` gick.

**Klubbmärket behölls.** Det är dekor i tillgänglighetsträdet och kostar 26 pt på en rad som
trunkerar, men det lades dit medvetet (#46). Kolumnen är fast 20 pt så att metaraden börjar på
samma x oavsett om klubben har ett märke — en `Auto`-kolumn hade fått texten att vandra i sidled
nedför listan.

**Sektionshuvudet förblir en diskret `SectionLabel` — inget mörkblått band.** Frågan gick att
avgöra först med raderna på plats, och svaret raderna ger är att bandet inte behövs: **listan
har redan fått sin ryggrad**, och den är datumkolumnen. Ett band hade lagt en andra, konkurrerande
struktur ovanpå den. Eventor behöver bandet just för att deras datumkolumn är svagare — deras
sektion är en vecka, vår är "Denna vecka" och "September", som är samma information i ord. Till
det kommer D1: mättad färg bär handling och det som brinner, och ett fält som återkommer var
sjätte rad är varken. Beslutet är billigt att riva upp — det är en `Style` i
`GroupHeaderTemplate`.

## Mätning (iPhone 17, simulator, "För dig")

| | Tävlingar helt synliga per skärm |
|---|---|
| Före | **3** (fjärde delvis) |
| Efter | **6** (sjunde delvis) |

Raden gick från ~167 pt till ~93 pt. Kravet var minst en fördubbling.

Verifierat i körning utöver antalet: kollapsen (två tävlingar 30 aug under "Gästrikland" — den
andra lämnar kolumnen tom), månadsbytet i "Mest relevant" (SEP → AUG), spannbadgen
("25–30 aug."), mörkt tema, att stjärnan fortfarande växlar utan att öppna tävlingen, och att
raden i övrigt öppnar den.

## Kvar att titta på (utanför det här uppdraget)

- `Format.DateRange` behåller månadsförkortningens punkt — "25–30 AUG." i versal badge. Samma
  sort som #115; `DateInSentence` och `Deadline` trimmar den redan. Har ett testfall som låser
  nuvarande beteende, och flera anropare.
- "DM, lång, Gästrikland" (Storviks IF) visar **6905 km**. Fanns före det här arbetet och ser ut
  som en tävling utan koordinater.
