# Issue #40 — Orientera M3 — prognos ur LiveResults-historik med backtest (SP-11)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/40
**Branch:** issue/40-prediction
**Status:** Completed — med ett negativt utfall, se Verdikt

## Plan

Bygga en deterministisk prognosmodell och mäta den mot lopp som redan sprungits. Underlaget
hämtas ur LiveResults, som är publikt, så spiken kan köras utan att vänta på någon källa.

## Changes

- `Fixtures/Backtest/swedish-2026.json` — **inspelat skarpt underlag**: 60 svenska tävlingar
  januari–augusti 2026, 5 556 resultatrader, 2 402 löpare, 725 med minst tre lopp.
- `Predictions/RunnerForm` — en löpares form som medianen av hens tidskvot mot vinnaren, plus
  en spridning ur kvartilerna. Under tre lopp finns ingen form.
- `Predictions/PredictionModel` — placeringsintervall ur fältets skattade former, plus
  förtroende och drivande faktorer i klartext.
- `PredictionBacktest` — 1 132 prognoser, var och en gjord enbart på resultat som fanns *före*
  den tävlingen.

## Vad backtesten avslöjade

- **Förtroendet var inverterat.** Första versionen belönade ett smalt intervall, och de
  "säkra" prognoserna träffade 62 % mot 76 % för alla. Smalt är modellen som *satsar*, inte som
  *vet*. Förtroendet räknas nu på hur mycket som är känt — egen historik och hur stor del av
  fältet som har form — och korrelerar därefter åt rätt håll.
- **Att fylla ut med okända löpare svällde intervallet.** Tidigt på säsongen har nästan ingen
  tre lopp, och intervallet blev hela fältet. De kända motståndarna skalas nu upp till fältets
  storlek i stället.

## Verdikt (SP-11)

**Modellen är inte bra nog att visa en löpare.** Mätt på 1 132 prognoser:

| Bandbredd | Träff i intervallet | Intervallets bredd |
|-----------|--------------------|--------------------|
| ±1 spridning | 44,6 % | smalt |
| ±2 spridningar | 58,8 % | — |
| ±3,5 spridningar | **72,4 %** | **57 % av fältet** |

Det är hela avvägningen: intervallet är antingen fel oftare än det är rätt, eller så brett att
det inte säger något. Kravet är ett intervall som är *ärligt och användbart* — i praktiken att
det håller fyra gånger av fem och täcker klart mindre än halva fältet. Modellen når inte dit.

Därför är prognosen **inte inkopplad i appen**. `BackendSource.GetPredictionAsync` svarar
fortsatt tomt, av samma skäl som resten av det ointegrerade gör det: hellre ingenting än ett
tal som ser säkert ut.

Testerna låser fast det uppmätta (70 % träff, 60 % bredd) så att en ändring av modellen måste
vara avsiktlig — inte att kraven är uppfyllda.

## Vad som skulle flytta modellen

- **Sverigelistan som prior** (SP-02). En löpares ranking finns för hela fältet, även för dem
  som saknar historik hos oss — det är precis det som saknas idag.
- **Motståndarnas spridning.** Modellen jämför min dåliga dag mot deras *median*, inte mot
  deras dåliga dag.
- **Klass- och distansspecifik form.** En sprintspecialist och en långlöpare har olika kvot i
  olika discipliner; här vägs allt ihop.
- **Mer historik per löpare.** 725 av 2 402 löpare hade tre lopp i underlaget; med en hel
  säsong blir formen stabilare.

## Verifiering

202 tester gröna, varav 4 nya (backtesten och dess spärrar).
