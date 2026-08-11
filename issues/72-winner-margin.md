# Issue #72 — Resultat: vinnaren har ingen tid efter vinnaren

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/72
**Branch:** issue/72-winner-margin
**Status:** Completed

## Plan

"Efter vinnaren +0:00" är sant men innehållslöst för den som vann, och det som faktiskt är
intressant — marginalen ner till tvåan — står ingenstans. Rubriken blir "Före tvåan" för en
förstaplats, och vinnarens rad i resultatlistan lämnar deltafältet tomt.

## Changes

- `ResultsDetailPage.ViewModel` — `BehindLabel` växlar mellan "Efter vinnaren" och "Före tvåan".
  `MarginToRunnerUp` räknar tvåans tid minus vinnarens, mot nästa **godkända** resultat.
  `BehindSpoken` följer med. `IsWinner` styr färgen.
- Resultatlistan — vinnarens rad har inget delta i stället för `+0:00`.
- `ResultsDetailPage.View.xaml` — rubriken binds i stället för att stå som text, och värdet blir
  grönt för en marginal före tvåan.

## Decisions

- **Marginalen mäts mot nästa godkända resultat.** En felstämplad tvåa är inte tvåa. `Place == 2`
  och `Status == Ok` är samma villkor som resultatlistan rankar på.
- **Grönt, inte rött.** `NegativeDelta` betyder tid man tappat. En marginal före tvåan är
  motsatsen, och att visa den i samma röda som ett tapp skulle säga fel sak utan ord.
- **Tomt, inte `−1:52`, i listan.** Ett minustecken mitt i en kolumn av plusvärden läser man som
  ett fel. Vinnaren är redan markerad av sin placering och sin tid överst.
- **Ingen tvåa ger `—`.** En klass med en enda startande har ingen marginal att visa. Rubriken
  står kvar som "Före tvåan" och värdet blir tankstreck, i stället för en nolla som ser ut som en
  mätning.

## Verifiering

`dotnet test`: 214 gröna (oförändrat — ändringen ligger i en vymodell, som inte kompileras in i
testprojektet).

**iPhone 17 Pro-simulator (iOS 26.2) mot skarp Eventor-data**, Norrlandsmästerskapen sprint, H45,
med identiteten satt till två olika löpare i samma klass:

- **Som vinnare** (Olov Vikström): "Före tvåan **1:52**" i grönt, och hans rad i listan har inget
  delta. Marginalen stämmer mot fältet — tvåan gick 15:20 mot vinnarens 13:28.
- **Som tvåa** (Göran Tronde): "Efter vinnaren **+1:52**" i rött, oförändrat.
