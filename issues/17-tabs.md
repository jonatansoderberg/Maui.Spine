# Issue #17 — Orientera M0 etapp 4: flikarna med fake-data

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/17
**Branch:** issue/17-tabs (stackad på issue/15-domain-core)
**Status:** Completed

## Plan

Bygg de fem flikarna mot etapp 3:s domänkärna, i planens ordning, med etapp 1:s tokens.
Allt läses via källinterfacen och all färg via `{DynamicResource}`.

## Changes

- `Presentation/Format.cs` (ny): svensk formatering av tider, delta, placeringar, datum och
  ålder på live-data. ViewModels lämnar färdiga strängar till XAML.
- `Controls/ChipView.cs` (ny): valbar chip, används av snabbfilter, resultatflikar och
  live-urval.
- **Tävlingar** (`EventsPage`): relevanssorterad lista av grupperade eventkort, sju
  snabbfilter, favoritstjärna direkt i listan, designat tomt läge, `EventFilterSheet` med
  typed result (`EventFilter`).
- **Tävlingsdetalj** (`EventDetailsPage`, typed param `CompetitionId`): hero → För dig →
  snabbhandlingar → PM-briefing → tävlingsinfo → dokument. Primär CTA sätts av Context
  Engine. `ChooseClassSheet` (typed result), `PredictionInfoSheet` (typed param).
- **Hem** (`HomePage`): kontextstyrda block via `DataTemplateSelector` — Live nu, Nästa för
  mig, Senaste resultat, Min grupp, discovery, utveckling. Max fyra block.
- **Live** (`LivePage`): Min grupp / Min klass / Alla, jag-highlight, ★ för Min grupp,
  15-sekunderspolling med "uppdaterad för X sek sedan".
- **Resultat** (`ResultsPage` + `ResultsDetailPage`): mina resultat, och per tävling
  Översikt / Sträckor / Analys med färgkodade tapp, största-tapp-kort, uppskattad bomtid och
  teoretisk tid, `CompareRunnerSheet` med head-to-head.
- **Jag** (`ProfilePage`): profil, Sverigelistan med räknande resultat och utfallsvarning,
  serieställning med strukna rundor, Min grupp med `FollowRunnerSheet`, dev-verktygen.
- `IEventSource`: lokala favoriter (`GetFavouritesAsync`/`ToggleFavouriteAsync`).
- `TimeMachineClock`: tiden går nu vidare från den satta punkten i stället för att frysa.

## Decisions

- **Gruppera först, sortera sedan.** Kalendern grupperas innan relevansordningen läggs på, så
  en återkommande serie tar en plats i listan och inte sex. Gruppens relevans är den högsta
  bland dess tillfällen.
- **En chip åt gången.** Snabbfiltren är presets, inte en matris — kombinationer hör hemma i
  filter-sheeten. Det håller listan förutsägbar och chip-raden på en rad.
- **Träningar döljs som default.** Det är den enskilt största bruskällan i Eventor-kalendern;
  `ShowTraining` i filtret tar tillbaka dem.
- **Inga dubbletter på Hem.** En tävling som redan visas som "Live nu" plockas bort ur "Nästa
  för mig". Utan det blev de två översta blocken samma tävling.
- **Badges som inte upprepar varandra.** `ShowContextBadge` stängs av när ett explicit
  märke redan säger samma sak (Live, Anmäld).
- **Live och resultat är samma seedade lopp.** Live-vyn frågar hur långt ett lopp hunnit vid
  `now`; resultatlistan frågar hur det slutade. Pollingen skriver bara om de värden som
  ändrats, så listan aldrig byggs om under fingret.
- **Klockan går.** `TimeMachineClock` sätter en förskjutning i stället för att frysa tiden,
  annars hade Live tickat på en stillbild.
- **`ChipView` i stället för `DataTrigger` för valda chips.** En `DataTrigger` sparar värdet
  den ersatte, en gång. Efter ett temabyte återställer den den *gamla* temafärgen — omarkerade
  chips blev ljusa pillerformer på mörk bakgrund. Kontrollen växlar i stället mellan två
  färdigstylade Borders, så varje färg fortsätter gå via `{DynamicResource}`. **Regel framåt:
  använd inte `DataTrigger` för att sätta temafärger på ytor som lever länge.**
- **Externa destinationer öppnas explicit** med `Launcher` och ↗-ikon. M0:s dokument pekar på
  platshållar-URL:er, så ett misslyckat öppnande sväljs i stället för att krascha sidan.
- **Kartläget i Tävlingar är stubbat.** Kartval är M4; M0 låtsas inte.

## Justeringar som verifieringen tvingade fram

- `RelevanceEngine.TemporalScore` behandlade en pågående tävling som "förfluten" och lät
  morgondagens event ranka över den som just då sprangs. Nu ger ett pågående lopp 1.0.
- `SplitAnalyzer.MistakeRatioThreshold` sänktes 1.30 → 1.25. Vid 1.30 flaggades inte ens en
  bom på nästan två minuter i seedens medeldistans, och analysvyn visade "Bomtid 0:00" —
  tröskeln var strängare än vad en orienterare skulle kalla ett solklart tapp.

## Fynd som matats tillbaka till Spine

- [#18](https://github.com/jonatansoderberg/Maui.Spine/issues/18) — typad navigation kan inte
  kombinera parameter och resultat. `CompareRunnerSheet` använder ett DI-registrerat
  överlämningsobjekt (`ComparisonRequest`) tills det finns.
- [#13](https://github.com/jonatansoderberg/Maui.Spine/issues/13) — inaktiv tillbaka-chevron
  på tab-rotsidor (från etapp 1, fortfarande öppen).

## Verifiering

- iPhone 17 Pro-simulator (iOS 26.2), light och dark: alla fem flikar, tävlingsdetalj med
  PM-briefing och källchip, sträcktabell med tabulära siffror, analys med bomdetektion,
  jämför-sheet med head-to-head, tidsmaskinen, temabyte i runtime.
- `dotnet test samples/Orientera.Tests`: 75 tester gröna.
- `dotnet build -f net10.0-android`: OK.

## Kvar till etapp 5

Light/dark-svep över alla vyer, VoiceOver/TalkBack-pass på kärnflödena, körverifiering på
Android-emulator, och dokumentation av designriktningen som utfall.
