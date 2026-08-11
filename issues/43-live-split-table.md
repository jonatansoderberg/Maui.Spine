# Issue #43 — Live: show the full radio-control split table, not just the last control

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/43
**Branch:** issue/43-live-split-table
**Status:** Completed

## Plan

Livelistan visade en rad per löpare med den senast passerade radiokontrollen. Den vy en van
live-tittare läser är sträcktabellen: en kolumn per radiokontroll med ackumulerad tid, placering
vid kontrollen och tid efter ledaren, plus måltiden sist. Utvecklingen — 3:a vid 79, 6:a vid 88 —
syns bara när kontrollerna står bredvid varandra.

`getclassresults` levererar redan datan. Arbetet är domän, normalisering och vy.

**Domän.** `LiveEntry` bär alla passeringar (`Passings`: kontroll, ackumulerad tid, placering,
tid efter ledaren) i stället för bara den sista. `LiveSnapshot` bär radiokontrollerna i ordning.

**Normalisering.** `LiveResultsNormalizer` läser `splitcontrols` för kolumnerna och `_place` /
`_timeplus` per kontroll i stället för att kasta dem. `LiveSource` samlar ihop kontrollerna
klassvis.

**Fake-data.** Fake-källan får radiokontroller — en delmängd av banans kontroller, som i
verkligheten — så tabellen har något att visa i demot och i tidsmaskinen.

**Vy.** En tabell med frusen namnkolumn och horisontellt scrollande kontrollkolumner.
Klassrubriken bär kolumnrubrikerna, eftersom radiokontrollerna hör till klassens bana.

## Changes

- `Sources.cs` — `LiveControl` (kod och den kod som står på skärmen i skogen) och `LivePassing`
  (kontroll, ackumulerad tid, placering, tid efter). `LiveEntry.Passings` ersätter
  `LastControlNumber` / `ElapsedAtLastControl`; `LastPassing` är den härledda sista.
  `LiveEntry.FinishBehind` bär `timeplus` i mål. `LiveSnapshot.Controls` är kontrollerna per
  klass, med `ControlsFor(klass)` för uppslaget.
- `LiveResultsNormalizer` — `Controls(payload)` läser `splitcontrols`. Passeringarna byggs i
  kontrollernas baneordning i stället för att sorteras på tid, och tar med `_place` och
  `_timeplus`. `Passing`-hjälpstrukten är borta; domänens `LivePassing` räcker.
- `LiveSource` — en `ClassResults` per klass, så kontroller och rader kommer ur samma svar och
  samma cache-post.
- `FakeDataSource` — radiokontroller per klass (var tredje kontroll, aldrig den sista som är
  målet), passeringar med placering och tid efter ledaren räknade bland dem som passerat
  kontrollen vid tidpunkten, och `FinishBehind` mot klassens vinnartid.
- `LivePage.ViewModel` — `LiveCell` per kolumn med tid, `(placering) +efter` och ledarmarkering;
  `LiveRow.Cells` byggs en gång per rad och skrivs över vid varje poll. `LiveClassGroup` bär
  klassens kolumnrubriker. `TableWidth` säger hur brett tabellen scrollar.
- `LivePage.View.xaml` — tabellen ligger i en horisontell `ScrollView`; namnkolumnen skjuts
  tillbaka med `ScrollX` och målas sist, så kontrollerna scrollar in under den.
- `HomePage.ViewModel` — "Du är vid kontroll …" säger kontrollens skyltade kod i stället för
  tidtagningssystemets interna (1079 → 79).
- Tester — `splitcontrols` blir kolumner i baneordning, en löpare bär alla passeringar med
  placering och tid efter, vinnaren har ingen tid efter, en felstämpling är tidtagen men inte
  placerad. Fake-källan: radiokontrollerna är en delmängd av banan utan målet, och en löpare i
  skogen har passerat några av dem. 212 gröna.

## Decisions

- **Kontrollerna hör till klassen, inte till tävlingen.** Issuen föreslog kontrollerna på
  snapshotten, men klasserna springer olika banor: i Norrlandsmästerskapen medel har D20 och D21
  kontrollerna 79 och 88 medan Blå 3,0 inte har någon alls. Kolumnrubrikerna sitter därför i
  klassrubriken, som listan redan grupperar på (#38).
- **Ingen placeringskolumn i namnkolumnen.** Den första körningen visade tre "1:a" efter varandra
  i H21 — alla tre korrekta, men mätta vid var sin kontroll: en hade gått i mål först, en ledde
  sin senaste radio, en var först fram till en radio ingen annan hunnit till. Ett tal som betyder
  olika saker på rader som står intill varandra är värre än inget tal. Placeringen står nu i den
  kolumn den gäller, och ordningen i listan är rangordningen — samma sak LiveOL gör.
- **Kort med skugga blev en tabell med linjer.** Tolv kolumner ryms inte i kortlayoutens
  polstring, och ett kort som är bredare än skärmen har hörn man aldrig ser. Raderna ligger nu på
  en sammanhängande yta med `Divider` mellan, samma språk som Sträckor i Resultat.
- **Namnkolumnen fryses med `TranslationX`, inte med en andra ScrollView.** Två scrollytor som
  ska hållas i synk är två gånger så många chanser att hacka. Här finns en enda scrollyta;
  namnblocket översätts med dess `ScrollX` och målas efter cellerna, så kontrollerna glider in
  under det. Bredderna (156 pt fruset, 82 pt per kolumn) står som konstanter i vymodellen och som
  samma tal i vyn — de måste vara överens, annars scrollar tabellen fel.
- **Fake-datat får radiokontroller, inte en kolumn per kontroll.** En tävling har radio vid ett
  par kontroller, inte vid alla. Var tredje kontroll ger två till fyra radiokontroller per bana,
  vilket är den storleksordning verkligheten har — och det gör att livepositionen i demot
  uppdateras lika sällan som den gör på riktigt.
- **En felstämpling är tidtagen men inte placerad.** LiveResults svarar `-` på `_place` för dem;
  fake-källan gör nu samma sak i stället för att räkna fram en placering som ändå inte gäller.

## Verifiering

`dotnet test`: 212 gröna.

**iPhone 17 Pro-simulator (iOS 26.2), fake-data.** Tabellen bygger upp sig per klass: D21 har
kolumnerna 44, 50, 43, 53 och MÅL, H14 två kontroller och mål. Ida Franzén står 7:a vid 44, 6:a
vid 50 och 4:a vid 53 — utvecklingen som issuen ville åt, på en rad. Horisontell scroll fungerar
och namnkolumnen står stilla medan kontrollerna glider in under den; vertikal scroll fungerar
inuti den horisontella scrollytan och behåller sidoläget. Ledaren i varje kolumn är accentfärgad,
jag-raden har sin accentton.

Körningen avslöjade tre saker:

1. **Tabellen visades inte alls först.** `HasRows` sattes från en `OnIsEmptyChanged`-hook, och
   `IsEmpty` var redan `false` — hooken körde aldrig. Nu är `HasRows` härledd med
   `NotifyPropertyChangedFor`, som inte kan hamna ur synk.
2. **Placeringskolumnen ljög** (se Decisions ovan). Det syntes först på riktig data i skogen, med
   en klass där tre löpare var etta samtidigt.
3. **Tvåsiffriga placeringar radbröts** i den smala kolumnen ("13:" / "e") innan kolumnen togs
   bort helt.

**Mot skarp LiveResults-data** via BFF-stubben (`EVENTOR_LIVE=1`, Norrlandsmästerskapen medel
2026-08-09): D20 visar 79, 88 och MÅL med riktiga placeringar och tider efter ledaren — Frida
Olsson 3:a vid 79 och 6:a vid 88. Klubbmärkena (#46) står kvar bredvid klubbnamnet i den frusna
kolumnen. Alicia Ivarsson har sina två radiotider och `—` i mål med "Bröt" under klubben. Klassen
Blå 3,0 saknar radiokontroller helt och får bara en MÅL-kolumn, vilket är fallet issuen kallade
"ibland noll". Ljust och mörkt tema kontrollerade båda.

Kvarstår, utanför den här issuen: en löpare som aldrig startat får `Start 00:00`, eftersom
LiveResults svarar `0` på starttid och `LiveEntry.StartTime` inte kan vara okänd. Det syntes
tydligare nu än förr men fanns redan före den här ändringen.
