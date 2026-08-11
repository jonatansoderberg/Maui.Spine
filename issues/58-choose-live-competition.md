# Issue #58 — Live: går inte att välja tävling när flera pågår

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/58
**Branch:** issue/58-choose-live-competition
**Status:** Completed

## Plan

`RefreshAsync` tog `liveCompetitions.FirstOrDefault()`. Mot skarp data pågår just nu två
tävlingar — Norrlandsmästerskapen medel och distriktsstafetten — och stafetten gick inte att nå.
Tävlingsnamnet i sidhuvudet blir valbart, med samma mekanik som klassväljaren bredvid.

## Changes

- `Services/Context/LiveSelection.cs` — vilken tävling Live visar, i minnet.
- `Features/Live/ChooseCompetitionSheet.*` — arket, byggt som `ChooseClassSheet`.
- `LivePage.ViewModel` — `CanPickCompetition`, `PickCompetitionCommand`, och val som överlever
  en pollning.
- `LivePage.View.xaml` — namnet med ▾ och en `TapGestureRecognizer`, synligt bara vid fler än en.
- `EventDetailsPage` — "Följ live" lämnar sin tävling i `LiveSelection` innan den byter flik.

## Decisions

- **Ingen väljare vid en enda tävling.** Ett ▾ som öppnar en lista med ett alternativ är brus.
- **I minnet, inte på disk.** En pågående tävling är över på några timmar; att minnas den till
  nästa vecka hade öppnat fliken på något som var slut för länge sedan. Klassvalet sparas
  däremot per tävling, och det är rätt — en klass är samma klass nästa gång.
- **Klassfiltret nollas vid byte.** En klass vald i den förra tävlingen hör till den tävlingen.
  Att bära med den hade antingen filtrerat på en klass som inte finns eller, värre, på en som
  råkar heta likadant men är en annan startgrupp. `AdoptCompetitionAsync` rensar valet och går
  tillbaka till Min grupp när den nya tävlingen inte har något sparat.
- **`SwitchToTabAsync` tar ingen parameter,** och att lägga till en i Spine för det här hade varit
  ett plugin-ingrepp för ett appbehov. Sidan som vet vilken tävling som menas lämnar den i
  `LiveSelection`; fliken hämtar den när den visas.

## Verifiering

`dotnet test`: 242 gröna (ren vy- och vymodelländring).

**iPhone 17 Pro-simulator (iOS 26.2), mot skarp data via BFF-stubben:**

- Två pågående tävlingar → ▾ syns bredvid namnet, arket listar båda med arrangör och plats.
- Val av distriktsstafetten byter rubrik, och klasschipet går från "D45" till "Välj klass" med
  urvalet tillbaka på Min grupp — precis det ärendet bad om.
- Demoläget har en pågående tävling → ingen ▾, som avsett.

**Inte verifierat:** att "Följ live" bär med sin egen tävling. Tävlingarna som stubben rapporterar
som pågående är inte desamma som visar knappen i kalenderfönstret, så kombinationen gick inte att
köra. Koden är läst, inte sedd.

**Fynd under vägen:** "Följ live" på en tävling *utan* livekälla byter tyst till en annan tävling.
`KeepOnly` glömmer ett val som inte längre pågår, vilket är rätt, men skiljer inte på "tävlingen är
slut" och "tävlingen har aldrig haft live". Anmält som
[#89](https://github.com/jonatansoderberg/Maui.Spine/issues/89) i stället för att byggas ut här.
