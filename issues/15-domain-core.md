# Issue #15 — Orientera M0 etapp 3: domänkärna, fake-data, motorer och tidsmaskin

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/15
**Branch:** issue/15-domain-core (stackad på issue/12-design-tokens)
**Status:** Completed

## Plan

1. Domänmodeller enligt kravdokumentens kärna, i appen (utbrytning vid M1).
2. Alla datakällor bakom interface från dag ett så FakeData överlever som demo-/testläge.
3. Tre motorer som rena, UI-fria komponenter: ContextEngine, RelevanceEngine, EventGrouper.
4. Deterministisk Gästriklandsseed modellerad på spec:ens egna exempel.
5. Tidsmaskin som dev-sheet — DoD-kravet att context-state kan simuleras genom hela livscykeln.
6. Unit-testprojekt.

## Changes

- `Domain/`: `Competition` + `CompetitionSchedule` + `CompetitionDocument`, `CompetitionProfile`
  + `ProfileFact`, `EventGroup`, `Person`/`FollowedPerson`, `CompetitionEntry`/`Start`/
  `CompetitionResult`, `Split`/`LegAnalysis`, `Series`/`SeriesStanding`, `RankingSnapshot`,
  `Prediction`, `Course`/`Control`/`Route`, `ContextState`/`ContextDecision`/`ContextInput`,
  `GeoPoint`, typade id:n.
- `Services/Sources/Sources.cs`: `IEventSource`, `IPeopleSource`, `IParticipationSource`,
  `ILiveSource`, `IProgressSource` samt `LiveSnapshot`/`LiveEntry`.
- `Services/Context/ContextEngine.cs`: ren state-maskin över de 11 tillstånden med CTA och
  svensk tillståndsetikett. `CompetitionContextService` bygger det personliga halvan av indata.
- `Services/Relevance/RelevanceEngine.cs`: fyra delpoäng med vikter (Personal 0.40,
  Importance 0.25, Geographic 0.20, Temporal 0.15) plus `Rank`.
- `Services/Grouping/EventGrouper.cs`: normaliserad titel + arrangör + plats + disciplin +
  nivå, uppdelat i körningar av angränsande datum.
- `Services/Analysis/SplitAnalyzer.cs`: sträckanalys, bomdetektion, teoretisk sluttid och
  stabilitetsindex.
- `Services/FakeData/`: `FakeDataset` (Gästriklandskalendern augusti 2026), `PlannedRun`,
  `RunGenerator`, `Deterministic`, `FakeDataSource` som implementerar alla fem interface.
- `Services/Time/Clock.cs`: `IClock`, `SystemClock`, `TimeMachineClock`.
- `Features/Dev/TimeMachineSheet`: `[NavigableSheet]` med Medium/FullScreen-detents; visar nu,
  tillstånd, CTA och elva hållplatser längs tävlingsresan.
- `MauiProgram.cs`: DI-registrering av klocka, källor och kontexttjänst; `sv-SE` som fast kultur.
- `samples/Orientera.Tests/`: 74 tester (xunit) över ContextEngine, RelevanceEngine,
  EventGrouper, SplitAnalyzer och själva seeden. Tillagt i `Spine.slnx`.

## Decisions

- **Allt härleds från tidsstämplar mot "nu".** `CompetitionSchedule` bär
  `PmPublishedAt`, `StartListPublishedAt`, `ResultsPublishedAt`, `SplitsPublishedAt`,
  `MapPublishedAt` och `CompetitionEntry.RegisteredAt`. Därför är hela livscykeln en funktion
  av klockan, och tidsmaskinen behöver inte mocka något — den flyttar bara `now`.
- **PM och startlista kräver anmälan för att lyfta tillståndet.** En oanmäld användare med
  publicerat PM stannar i `RegistrationOpen`, för då är "Anmäl dig" den handling som betyder
  något. Utan den regeln hoppar tillståndet över anmälan.
- **Live och resultat är två projektioner av samma `PlannedRun`.** Live frågar "hur långt hade
  det här loppet kommit vid tid T", resultatlistan frågar "hur slutade det". De kan inte
  motsäga varandra, och live blir en ren funktion av klockan i stället för en timer.
- **Tävlingens karaktär driver seeden, inte slumpen.** Namngivna löpare har skriptade
  `RunShape` (Elin tappar två minuter på sträcka 4 och 8 och blir femma); övriga får en stabil
  fart ur sitt id. Startplatser skriptas också, så Elin är ute på banan vid default-`now`.
- **`Deterministic` använder FNV-1a, inte `string.GetHashCode()`.** Den senare är randomiserad
  per process och hade gett en ny kalender vid varje start.
- **Appens klocka startar i ett kurerat ögonblick** (lördag 15 augusti 2026, 11:50) i stället
  för väggklockan, så seedens kalender alltid är aktuell och Live har löpare i skogen.
- **Bommar mäts mot löparens egen fart, inte mot segraren.** Någon som ligger 20 % efter
  överallt har inte gjort elva fel. Baslinjen är medianen av löparens kvoter mot bästa
  sträcktid; bara avvikelser från den egna farten flaggas, och bara över 20 sekunder.
- **`Entry` döptes till `CompetitionEntry`** — `Entry` krockar med `Microsoft.Maui.Controls.Entry`
  i appassemblyt.
- **Testprojektet kompilerar källfilerna direkt** (`<Compile Include="..\Orientera\Domain\**" />`)
  i stället för att referera appen. Domänen ska enligt planen ligga kvar i appen till M1, och
  appen är multi-targetad mot mobilramverk som ett `net10.0`-testprojekt inte kan referera.
  Koden är MAUI-fri, vilket länkningen också bevakar: den bryter om någon smyger in ett
  MAUI-beroende i domänen.
- **`FakeDataset.Instance` är `Lazy`.** Som `static readonly`-fältinitierare kördes den före
  id-fälten längre ned i klassen och gav varenda person samma `default(PersonId)` — en tyst
  och otäck bugg som testerna fångade direkt.
- **Kulturen är fast `sv-SE`** i stället för enhetens. Språket är svenska i M0 och seedens
  veckodagar och månader ska se likadana ut oavsett testenhet.

## Justeringar som testerna tvingade fram

- `EventGrouper` behöll "etapp" i gruppens titel eftersom alla ockurrenser delar ordet.
  Gemensamma prefixet trimmas nu på efterföljande ordningstal-ord.
- `RelevanceEngine.GeographicScore` la distriktsboosten ovanpå avståndspoängen, vilket
  klippte allt inom ~30 km till 1.0 och plattade ut avståndsordningen. Distriktet tar nu en
  fast andel av poängen i stället.

## Verifiering

- `dotnet test samples/Orientera.Tests`: 74 tester, alla gröna.
- iPhone 17 Pro-simulator (iOS 26.2): tidsmaskinen flyttar "nu", tillstånd och CTA följer med,
  "Nu"-markören hoppar rätt i listan, återställningen dyker upp när klockan är förskjuten.
- **Bottom sheet med detents verifierad på iOS** (medium + fullscreen, drag mellan dem, dimmad
  bakgrund) — det utestående etapp 2-momentet är därmed avklarat.
- `dotnet build -f net10.0-android`: OK.
