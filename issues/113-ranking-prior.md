# Issue #113 — SP-11c: prognosmodellen med Sverigelistan som prior

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/113
**Branch:** issue/113-ranking-prior
**Status:** Completed — negativt utfall, men mätbart bättre. Se Verdikt.

## Plan

SP-11 (#40) mätte att modellen inte dög och pekade ut Sverigelistan som första sak som skulle
flytta den. SP-11b (#111) mätte att rankingen verkligen förutsäger placering, ρ 0,54–0,89. Det här
mäter vad den är värd **inne i modellen**.

## Underlaget byggdes om

SP-11:s fixtur är LiveResults och identifierar löpare på namn och klubb. Rankingen hänger på
`personId`, så en namnmatchning hade lagt in sitt eget brus i just det som skulle mätas. Underlaget
är därför Eventors eget:

- **244 individuella tävlingar** 2026, klassificering 1–2, ur `/api/results/event`.
- **41 317 resultatrader**, 9 662 löpare. Trimmat till de 2 209 som startade i augustifönstret:
  **15 705 rader** över 187 tävlingar.
- **Rankinghistorik för 2 110 av dem** — varje resultat med datum och poäng, hämtat genom
  sessionen. 87 löpare har ingen sida ens då; de har inte betalat avgiften.

**Läckagefritt:** priorn räknas om som den stod dagen före varje lopp — de sex bästa av resultaten
i de tolv månaderna före. Loppet som ska förutsägas ligger aldrig i det som förutsäger det.

## Kalibreringen

Rankingen och modellen talar olika språk. Modellen jämför löpare på tidskvot mot vinnaren;
Sverigelistan är ungefär hur långt bakom en nationell standard man ligger. En rät linje, anpassad
på de löpare som har båda:

```
kvot = 1,0459 + 0,00550 × poäng        residualspridning 0,1104
```

Noll poäng landar på 1,046 — en löpare på riksstandarden är ungefär i nivå med vinnaren — och varje
hundra poäng lägger femtiofem procent på tiden. Att den är fysiskt rimlig är i sig ett kvitto på att
måtten hänger ihop.

Residualspridningen är den spridning en rankingbaserad form får. Vi har aldrig sett personen springa;
det vi inte vet om dem *är* skvättet kring linjen.

## Resultat

Tre lägen, mätta på samma data. Bredd vid **samma täckning** är det enda rättvisa måttet — varje
modell kan träffa oftare genom att bli bredare.

| Läge | Band | Täckning | Bredd | Prognoser |
|---|---|---|---|---|
| Utan ranking | ×0,6 | 83,3 % | **57,8 %** | 2 876 |
| Ranking när vi inte sett löparen | ×0,5 | 80,9 % | **52,8 %** | 3 264 |
| Ranking även i stället för tunn form | ×0,5 | 84,7 % | 58,8 % | 3 264 |

Andel av fältet med känd form: **86,6 %** mot 75,2 %.

## Verdikt

**Rankingen hjälper, och inte tillräckligt.**

Den gör två saker, båda mätta:

1. **13 % fler löpare får en prognos alls** — 3 264 mot 2 876 — och 86,6 % av fältet har en form
   mot 75,2 %. Det var precis vad SP-11 saknade.
2. **Intervallet krymper från 57,8 % till 52,8 % av fältet** vid samma träffsäkerhet.

Kravet är fortfarande att hålla fyra gånger av fem och täcka *klart mindre* än halva fältet.
52,8 % är inte det. Prognosen förblir därför ur appen och `GetPredictionAsync` svarar tomt.

Det är nedskrivet som ett test — `The_bar_the_product_needs_is_still_not_met` — som går sönder den
dag ribban nås. Då ska det här verdiktet läsas om, inte förbises.

## Vad mätningen avslöjade utöver frågan

- **En ranking ersätter inte lopp vi sett.** Att låta rankingen gå före en tunn form — under sex
  egna lopp — gav tillbaka mer än det tog: 58,8 % mot 52,8 %. Tre egna lopp säger mer om formen än
  sex rankingresultat gör. Rankingen fyller luckor; den förbättrar inte bevis.
- **Bandet är datasetberoende.** SP-11 landade på ±3,5 spridningar på LiveResults-underlaget. På
  det här underlaget ligger optimum kring ×0,5 av det. Konstanten är därför **oförändrad** —
  jämförelsen mellan lägena görs vid samma täckning och är oberoende av bandet, och att byta
  konstant på ett annat dataset än det som satte den vore att flytta något utan att veta vad.
- **Täckningen är inte problemet längre.** 86,6 % av fältet har en form. Det som är kvar är att
  formen — vår som rankingens — helt enkelt inte förutsäger en orienteringsplacering snävare än så.
  Ett lopp avgörs av vägval och bomtid, och det finns inte i någon lista.

## Changes

- `Predictions/RankingCalibration.cs` — linjen som läser Sverigelistan på kvotskalan, med
  minstakvadratanpassning och de anpassade talen som produktionsvärde.
- `Predictions/RunnerForm.cs` — `Ranked(...)`, en form för någon vi aldrig sett springa, och
  `FromRanking` som säger vilket slags bevis formen vilar på.
- `Predictions/PredictionModel.cs` — versionen heter `form-ranking-1`; förtroendet räknar en
  ranking som halva bevisvärdet av sett lopp, och drivkraften säger det rakt ut.
- `Fixtures/Backtest/eventor-2026.json` + `README-eventor.md` — det nya underlaget.
- `RankingPriorBacktest` — sex tester: kalibreringen, täckningen, jämförelsen vid samma täckning,
  att rankingen inte slår tunn form, att ingen prognos använder sitt eget lopp, och verdiktet.

## Verifiering

`dotnet test`: **272 gröna** (266 + 6 nya).

Ingen simulatorkörning: inget av det här syns i appen, med avsikt.

## Kvar

- **Vägval och bomtid** är det som avgör ett orienteringslopp och finns inte i någon lista. Det är
  troligen taket för den här sortens modell, inte en sak till att lägga in.
- **Fönstret är elva dagar.** Prognoserna görs på 18 tävlingar i augusti, eftersom löparsidorna
  bara hämtades för dem som sprang där. Ett bredare fönster kostar fler sidor.
