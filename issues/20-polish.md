# Issue #20 — Orientera M0 etapp 5: polish och validering

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/20
**Branch:** issue/20-polish (stackad på issue/17-tabs)
**Status:** Completed

## Plan

Kvalitetssäkra etapp 1–4: light/dark-svep, skärmläsarpass på kärnflödena, körverifiering på
båda plattformarna, och designriktningens utfall dokumenterat.

## Changes

- `Presentation/Format.cs`: uppläsbara former — `SpokenTime`, `SpokenDelta`, `SpokenPlace`.
- Rubriknivåer sätts från typografistilarna: `Heading1Label` → Level1, `Heading2Label` och
  `SectionLabel` → Level2. `NumericHeading2Label` nollställer nivån igen — den ärver storleken
  från Heading2 men ett värde är ingen rubrik.
- Listrader (eventkort, resultatrader, live-rader, jämförelsekandidater, sträckor,
  head-to-head) exponerar en `Accessibility`-mening och blir ett element för skärmläsaren.
- `ChipView` bär sitt valda tillstånd i beskrivningen ("För dig, valt filter").
- Favoritstjärnan flyttades utanför kortets `Border` och fick 44 pt-mål och en
  tillståndsspecifik etikett ("Spara … som favorit" / "Ta bort … från favoriter").
- Dekorativa glyfer (initialer, ↗) togs ur tillgänglighetsträdet; dokumentrader säger
  "Öppnas utanför appen" innan användaren lämnar appen.
- `SemanticScreenReader.Announce` när live-urvalet byts, eftersom listan byts under läsaren.
- `TabBarSpacer` + `ScreenPadding` med Android-värde som håller innehållet ovanför den
  native tab-baren (workaround för Spine #21).
- Hero-layouten i tävlingsdetaljen: stjärnan flyttad till metaraden.
- `samples/Orientera.Tests`: `Presentation/` länkas in, `FormatTests` täcker de uppläsbara
  formerna och skrivformerna. 91 tester totalt.
- `docs/design/utfall-m0.md` (ny) och regelavsnitt i `docs/design/design-system.md`.

## Decisions

- **Ett kort är ett element för skärmläsaren.** Sex etiketter per kort blir sex svep per rad;
  en lista blir oanvändbar. Beskrivningen sätts på kortets `Border`. Följden är att knappar
  *inne i* kortet blir onåbara på iOS — därför ligger favoritstjärnan utanför Bordern.
- **Tider och placeringar får en egen uppläst form.** "38:33" läses som ett klockslag och
  "3:e" som "3 e". `Format.SpokenTime`/`SpokenPlace` säger "38 minuter 33 sekunder" och
  "plats 3". Det är ett medvetet undantag från regeln att inte sätta `Description` på en
  `Label`: här ska texten *ersättas*, inte upprepas.
- **Uppskattningar sägs, inte bara färgas.** `EstimateInk` finns inte för en skärmläsare, så
  beskrivningarna innehåller orden — "trolig bom, uppskattat 1 minut 43 sekunder".
- **Rubriknivåer sätts i stilarna, inte per instans.** En `SectionLabel` *är* en
  sektionsrubrik. Det gör att nya vyer får rätt struktur utan att någon kommer ihåg det.
- **Skärmläsarpasset verifierades på Androids tillgänglighetsträd**, inte genom att lyssna.
  `adb shell uiautomator dump` ger varje nods `content-desc` och är därmed ett objektivt
  protokoll — VoiceOver går inte att styra huvudlöst på iOS-simulatorn på ett tillförlitligt
  sätt. `SemanticProperties` är samma API på båda plattformarna.

## Fynd som matats tillbaka till Spine

- [#21](https://github.com/jonatansoderberg/Maui.Spine/issues/21) — **Android: tab-sidor
  förskjuts inte ovanför Material-baren.** Sista elementet i varje scrollbar vy låg permanent
  bakom baren. Motsäger tab-host-dokumentationens uttalade kontrakt. Workaround i appen tills
  vidare.
- [#22](https://github.com/jonatansoderberg/Maui.Spine/issues/22) — **Android: native
  tab-baren byter inte tema live.** Appens innehåll följer systemtemat direkt, baren först vid
  omstart.
- [#13](https://github.com/jonatansoderberg/Maui.Spine/issues/13) och
  [#18](https://github.com/jonatansoderberg/Maui.Spine/issues/18) kvarstår sedan tidigare.

## Verifiering

| Vad | Hur |
|-----|-----|
| Light/Dark, alla vyer | iPhone 17 Pro-simulator (iOS 26.2) och Pixel 10 Pro-emulator (API 36), båda teman, samtliga flikar, tävlingsdetalj, resultatvyns tre lägen och sheets |
| Temabyte i runtime | Systembyte medan appen kör; appens innehåll följer med (Androids native tab-bar gör det inte — #22) |
| Tillgänglighetsträd | `adb shell uiautomator dump` på Tävlingar, Live och sträcktabellen; varje kort och rad har en fullständig svensk mening, chips bär valt tillstånd, favoritknappen är en egen nod |
| Domänlogik | 91 tester gröna |
| Android | Byggd, installerad och körd på emulator, inte bara byggverifierad |

## Kvar

`TabBarSpacer`-workaroundet tas bort när #21 är löst. Map-inslaget i designriktningen kan
först bedömas när kartvyerna byggs i M4 — se [utfall-m0.md](../samples/Orientera/docs/design/utfall-m0.md).
