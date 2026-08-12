# Issue #117 — SP-11d: vilken prognosvariant gissar närmast?

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/117
**Branch:** issue/117-prediction-variants
**Status:** Completed

## Ribban flyttades först

SP-11 och SP-11c dömde modellen mot kravet "håller fyra av fem och täcker klart mindre än halva
fältet". Det kravet är upphävt: prognosen får vara **ungefärlig och kul** så länge appen visar hur
osäker den är. Därmed blir frågan inte om modellen duger utan **vilken variant som gissar närmast**.

## Fem varianter, samma underlag

`eventor-2026.json` (#113). Bredd mäts vid **samma täckning**, 80 % — annars kan vilken modell som
helst se bra ut genom att bli bredare. Medianfelet är hur långt intervallets mitt hamnar från den
faktiska placeringen.

| Variant | Bredd vid 80 % | Medianfel | Prognoser |
|---|---|---|---|
| Utan ranking | 57,8 % | 2,5 platser | 2 876 |
| Ranking som reserv | 52,8 % | 2,5 | 3 264 |
| Ranking före tunn form | 58,8 % | 2,5 | 3 264 |
| **Sortera på Sverigelistan** | **50,9 %** | **3,5** | 3 264 |
| **Blandning (k = 2)** | **51,4 %** | **2,5** | 3 264 |

Blandningens vikt sveptes: rankingen väger `k / (k + egna lopp)`.

| k | Bredd vid 80 % | Medianfel |
|---|---|---|
| 0,5 | 54,4 % | 2,5 |
| 1 | 55,7 % | 2,5 |
| **2** | **51,4 %** | **2,5** |
| 4 | 54,5 % | 3,0 |
| 8 | 50,1 % | 3,0 |

## Verdikt

**Blandningen vid k = 2 vinner**, men knappt, och det är den viktigaste delen av svaret.

- **Din idé — sortera fältet på Sverigelistan — ger smalast intervall av alla**, 50,9 %. Den
  betalar med att hamna en hel placering längre från sanningen, 3,5 mot 2,5.
- **Blandningen tar båda**: 51,4 % bredd med bibehållet medianfel 2,5.
- **Skillnaderna är små.** 50,9 mot 51,4 mot 52,8 procent ligger inom det osäkerhetsintervall
  mätningen själv har — täckningen är ±2 procentenheter när man omsamplar per lopp. Ordningen
  mellan varianterna är verklig men marginalen är det knappt.

Praktiskt: **valet av variant spelar mindre roll än hur resultatet presenteras.** Alla fem landar
på ungefär halva fältet för att träffa fyra av fem gånger, och mitten hamnar två till tre
placeringar fel.

## Changes

- `Predictions/RunnerForm.cs` — `Blend(watched, ranked, rankingWeight)`.
- `RankingPriorBacktest` — två nya varianter (`RankingFirst`, `Blended`), medianfel som mått, och
  tester som låser ordningen mellan varianterna i stället för marginalerna.
- Verdikttestet från #113 är ersatt: ribban det vilade på finns inte längre.

## Decisions

- **`BlendPivot = 2`** — svept, inte vald. Vid 2 väger rankingen två tredjedelar för någon vi sett
  ett lopp och en tredjedel för någon vi sett fyra.
- **Testerna låser ordningen, inte gapen.** Att blandningen slår reserven är robust; att den gör
  det med 1,4 procentenheter är det inte, och inget nedströms bör luta sig mot den siffran.
- **`SpreadBand` är fortfarande orörd.** Konstanten 3,5 sattes på LiveResults-underlaget och ger
  74–95 % breda intervall här. Att ställa om den hör ihop med att koppla in prognosen, inte med att
  jämföra varianter — sveptalen ovan är relativa och oberoende av den.

## Exempel: prognos mot utfall

Tjällmoträffen 2026-08-11, blandningen vid det svepta bandet. Det här är vad en användare skulle se.

```
H21 — 43 startande, träff 28/39
  1–5      1     Jerker Lysell        3 lopp
  5–35     2   X Johan Aronsson       6 lopp
  1–25     3     Emil Ljungemyr       4 lopp
  1–28     6     Erik Berzell         ranking
  1–4     13   X Edvin Åtting         5 lopp

H45 — 13 startande, träff 9/12
  2–6      1   X Oskar Gustafsson     4 lopp
  1–5      2     Magnus Palm          8 lopp
  1–6      3     Erik Sandh           3 lopp
 11–12     5   X Martin Hammarlund    8 lopp
 13–13    13     Joakim Kjellberg     3 lopp

H50 — 31 startande, träff 24/25
  1–5      1     Peter Ettling        11 lopp
  1–18     2     Kristian Algers      ranking
  1–3      4   X Björn Beckius        5 lopp
  6–23    11     Pontus Johnsson      4 lopp
```

**Det exemplen visar, som ingen aggregerad siffra gör:** intervallets användbarhet står och faller
med fältets storlek. I H45 med 13 startande är "2–6" och "1–5" faktiskt informativt. I H21 med 43
är "1–25" formellt rätt och säger ingenting. Modellen träffar oftast, men i stora fält träffar den
genom att inte påstå något.

## Verifiering

`dotnet test`: **279 gröna**.

Ingen simulatorkörning — prognosen är fortfarande inte inkopplad i appen.

## Kvar

- **Bandet måste ställas om** innan något visas: 3,5 spridningar ger 74–95 % breda intervall på det
  här underlaget.
- **Presentationen är det som avgör**, inte varianten. Ett intervall på halva fältet bör troligen
  inte skrivas som "7:e–13:e" utan som något grövre och ärligare, och kanske bara visas i fält där
  det säger något.
