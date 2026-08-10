# Orientera — implementationsplan

> Detaljerad plan för M0 (UX-prototyp) med blick framåt mot M1–M5.
> Förutsättning: **designprinciperna i [design/designprinciper.md](design/designprinciper.md) är avstämda innan etapp 2+ påbörjas.**

## Läget i Maui.Spine-repot

- Appen ligger i `samples/Orientera` och refererar Spine-pluginsen som projektreferenser (`Plugin.Maui.Spine`, `Plugin.Maui.SpineControls`, `Plugin.Maui.SvgIcon`, `Plugin.Maui.SvgImage`, ev. `Plugin.Maui.AnimatedLabel`).
- Spine-mönster som används: `SpineApplication`-rot, trefilsmönstret (`Page.cs` + `Page.View.xaml` + `Page.ViewModel.cs`), `[NavigableRegion]`/`[NavigableSheet]`, typed params/results, `SpineCollectionView`.
- **Tabbar:** Spine har ett tab-host-primitiv (`[NavigableTab]`, native `UITabBarController`/`BottomNavigationView`) sedan [PR #11](https://github.com/jonatansoderberg/Maui.Spine/pull/11). Orienteras fem flikar är deklarerade mot det. Se [docs/wiki/tab-host.md](../../../docs/wiki/tab-host.md).
- **Plattformsläge:** README anger iOS/macOS "in progress" för Spine, medan Orientera är phone-first iOS + Android. iOS-verifiering av Spine-primitiver ingår därför som explicit aktivitet i etapp 2 (risk R1).

## M0 — UX-prototyp

**Mål (DoD ur spec):** app körbar på iOS + Android; Hem, Tävlingar, Event detail, Live, Resultat, Analys och Jag med realistisk fake-data; Light + Dark; grupperade återkommande event; simulerbar context-state genom hela livscykeln; designriktning vald.

### Etapp 0 — Scaffold ✅

Projektskelett enligt Spine-mönstret, feature-mappar, placeholder-sidor för de fem flikarna, byggverifierat. Ingen design, ingen logik.

### Etapp 1 — Designavstämning (grind)

- ✅ **Grinden passerad 2026-08-10.** Beslut 1–5 tagna: Nordic + subtil Map + Performance i Resultat/Analys; tokenuppsättningen godkänd inklusive `EstimateInk`; Inter med tabulära siffror; klassiska tabbikoner + text; "Orientera" internt i M0 utan store-facing branding. Se `design/designprinciper.md`.
- Kodifiera tokens: `Resources/Styles/LightTheme.xaml` + `DarkTheme.xaml` (samma nyckelset, systemtema default), typografiresurser, korn av komponentstilar (kort, chip, badge, sektionsetikett).
- Eventuellt: snabb HTML-mockup per flik för att testa riktningen innan XAML.

**Inget under etapp 2+ startar innan denna grind är passerad.**

### Etapp 2 — Navigationsskal och tab-host

- ✅ **Tabb-frågan löst enligt alternativ A.** `SpineTabHost` byggdes som nytt primitiv i `Plugin.Maui.Spine` — `[NavigableTab]`, en region-stack per flik med bevarat state, native `UITabBarController` på iOS/Catalyst och Material `BottomNavigationView` på Android. Levererad i [PR #11](https://github.com/jonatansoderberg/Maui.Spine/pull/11) ([issue #10](https://github.com/jonatansoderberg/Maui.Spine/issues/10)); Orienteras fem flikar är deklarerade mot den. Dokumentation: [docs/wiki/tab-host.md](../../../docs/wiki/tab-host.md).
- Verifiera Spine på iOS: region-push/pop, sheets med detents, back-svep. Utfall matas in i Spine-repots issues.
  - ✅ **Verifierat 2026-08-10** (iPhone 17 Pro-sim, iOS 26.2): start, header bar, tab-host, region-push/pop och interaktiv back-swipe. Bottom sheets med detents verifierade med `TimeMachineSheet` (etapp 3) — medium- och fullscreen-detent, drag mellan dem, dimmad bakgrund och page actions fungerar.
  - Fynd: [#13](https://github.com/jonatansoderberg/Maui.Spine/issues/13) — header bar ritar en inaktiv tillbaka-chevron på tab-rotsidor (kosmetiskt).
- Deep-link-skelett (PWOS-schemaliknande `orientera://event/{id}`) kan vänta till M5, men URL-strukturen bestäms här.

### Etapp 3 — Fake-data-lager och domänkärna ✅

Domänmodeller läggs i appen (utbrytning till separat projekt kan ske vid M1 när backend-kontraktet formas):

- `Domain/`: `Competition`, `EventGroup`, `CompetitionProfile`, `Person`/`FollowedPerson`, `Entry`/`Start`/`Result`, `Split`/`LegAnalysis`, `SeriesStanding`, `RankingSnapshot`, `Prediction`, `Course`/`Control`/`Route`, `ContextState`.
- `Services/FakeData/`: en deterministisk seed-generator som producerar en realistisk Gästriklands-kalender (jfr PDF:ens exempel: Norrlandsmästerskapen-helgen, Veckans bana-serien, DM Sprint...), startfält, splits med inbyggda "bommar", Sverigelistan-poäng och prediction-intervall.
- `Services/Context/ContextEngine`: ren, unit-testbar state-maskin över spec:ens 11 states + signaler.
- `Services/Relevance/RelevanceEngine`: egen komponent med `ImportanceScore/PersonalScore/GeographicScore/TemporalScore`, viktning enligt kravtabellen.
- `Services/Grouping/EventGrouper`: normaliserad titel + arrangör + plats + klassificering + angränsande datum.
- **Dev-verktyg: "tidsmaskin".** En dev-sheet där man flyttar "nu" genom tävlingslivscykeln och ser Hem/CTA:er byta state (DoD-kravet "context-state kan simuleras").
- Unit-testprojekt för ContextEngine, RelevanceEngine, EventGrouper (NFR Testbarhet).

### Etapp 4 — Flikarna med fake-data ✅

Byggordning (varje punkt är leverbar för sig):

1. **Tävlingar** — SpineCollectionView-lista, snabbfilter-chips, grupperade event-kort, `EventFilterSheet` (typed result). Kart-läget stubas med platshållare i M0 (kartval är M4).
2. **Tävlingsdetalj** — vertikal kontextstyrd detalj (hero → För dig → snabbhandlingar → info → dokument), `ChooseClassSheet`. PM-briefing renderas från fake `CompetitionProfile` med käll-chip ("PM sida 2").
3. **Hem** — kontextstyrda block enligt prioriteringsregeln (Live nu → Nästa för mig → Senaste resultat → discovery/Min grupp/utveckling), max 3–4 block.
4. **Live** — lista med Min grupp/klass/alla-växling, jag-highlight, ★-favoriter, simulerad 15 s-uppdatering, "uppdaterad för X sek sedan".
5. **Resultat + Analys** — Översikt/Sträckor/Analys-flikar, färgkodade tapp, största-tapp-kort, `CompareRunnerSheet`, `PredictionInfoSheet` (intervall + förklaring). Performance-densitet.
6. **Jag** — profil, Sverigelistan-kort (poäng, trend, resultat som räknas/faller ur), Min grupp-hantering (`FollowRunnerSheet`), utvecklingsblock.

### Etapp 5 — M0-polish och validering

- Light/Dark-svep över allt; kontrastkontroll av tokens.
- VoiceOver/TalkBack-pass på kärnflödena.
- Körverifiering iOS-simulator + Android-emulator.
- Designriktningsbeslut dokumenteras som utfall i `design/`.

## M1–M5 — översikt och beroenden

| Fas | Kärnleveranser | Blockerande spikes |
|-----|----------------|--------------------|
| **M1 Eventor Core** | `Orientera.Backend` (Azure Functions, isolated): EventorAdapter + normalisering + cache; appen byter FakeData → BFF bakom samma interface; offline-tävlingspaket; fel→fallback | SP-01 (access/auth-modell) |
| **M2 Live & Personal** | LiveResultsAdapter + matchning; Live på riktig data; identifierad person; Min grupp; lokala favoriter; notis-grund | SP-04 (Eventor↔LiveResults-matchning) |
| **M3 Intelligence** | PM-pipeline (LLM-extraktion → `CompetitionProfile` med Value/Confidence/Source/Page); Sverigelistan; serier; prediction (deterministisk modell + backtest) | SP-02, SP-03, SP-10, SP-11 |
| **M4 Mapping & Analysis** | OmapsAdapter (rättighetsstyrd), kurs/kontroller, GPX/FIT-import, vägvalsanalys, Livelox deep-link | SP-05, SP-06, SP-07, SP-08, SP-12 |
| **M5 Productization** | Konto/sync, push, ev. anmälan, store-release | SP-01 (auth), SP-13 (namn) |

Arkitekturregel från dag ett: **alla datakällor bakom interface** (`IEventSource`, `ILiveSource`, ...) så att FakeData-implementationen lever kvar som test-/demo-läge genom hela produktens liv — bra både för utveckling och för Maui.Spine-demon.

## Risker

| # | Risk | Hantering |
|---|------|-----------|
| R1 | Spine på iOS är "in progress" — Orientera är phone-first iOS+Android | Tidig iOS-verifiering i etapp 2; fynd blir Spine-issues; Orientera driver ramverkets iOS-mognad (poängen med real-world sample) |
| R2 | ~~Inget tabb-primitiv i Spine~~ **Stängd** | Alternativ A byggd och mergad (Spine PR #11): `[NavigableTab]` + native tab-host |
| R3 | Extern dataåtkomst (Eventor auth, Sverigelistan, Omaps-rättigheter) obekräftad | M0 är helt fake-data; spikes körs parallellt; NFR Fallback (degradera till deep-link) är inbyggd princip |
| R4 | Namnet "Orientera" ej klarerat | SP-13 före store-release; koden använder namnet internt utan risk |
| R5 | Fake-data som inte känns realistisk gör M0-utvärderingen missvisande | Seed-datat modelleras på PDF:ens verkliga exempel (Gästrikland aug 2026) |

## Föreslagen arbetsordning närmast

1. ✅ **Designavstämning** (etapp 1-grinden) — besluten 1–5 tagna 2026-08-10, se [design/designprinciper.md](design/designprinciper.md).
2. ✅ Tab-host (alternativ A) + iOS-verifiering av Spine (sheets med detents kvarstår).
3. ✅ Tokens + tema-resurser.
4. ✅ Fake-data + ContextEngine + tester.
5. ✅ Flikarna i ordningen Tävlingar → Detalj → Hem → Live → Resultat → Jag.
6. Etapp 5: light/dark-svep, VoiceOver/TalkBack, Android-emulatorkörning.
