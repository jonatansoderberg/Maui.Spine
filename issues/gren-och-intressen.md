# Gren som egen dimension, och vad jag är intresserad av

**GitHub:** _issue ej skapad än_
**Branch:** issue/okand-arena (staplad)
**Status:** Steg 1 klart (gren som axel). Steg 2 kvar (Jag-inställningarna).

## Problemet

"MTBO-träning Källviken" står i listan som **Sprint**. Grenen är helt borta: `Discipline` blandar
distans (Sprint, Medel, Lång, Ultralång), ljus (Natt), tävlingsform (Stafett) och *en* gren
(Indoor) i en enda uppräkning. Det går därför inte att välja bort MTBO, och den som filtrerar på
"Sprint" får MTBO-sprintar på köpet.

## Vad datakällan säger (avläst 2026-08-24)

Nya Eventors klientkod har två skilda uppräkningar. Ordagrant ur `eventor.se/assets/index-*.js`:

```js
e0 = { UNKNOWN: "Unknown", FOOT: "Foot", INDOOR: "Indoor", MOUNTAIN_BIKE: "MountainBike",
       PRE_O: "PreO", SHOOTING: "Shooting", SKI: "Ski" }          // gren

Gm = { UNKNOWN: "Unknown", LONG: "Long", SPRINT: "Sprint", MIDDLE: "Middle",
       ULTRA_LONG: "UltraLong", PRE_O: "PreO", TEMP_O: "TempO",
       PRE_OSPRINT: "PreOSprint" }                                 // distans
```

Tre saker att lägga märke till:

1. **Grenen är en lista på arrangemanget**, inte ett värde: `disciplines: Discipline[]`. Deras
   egen ikonkod väljer *en* att rita, i prioritetsordning
   `Indoor → Foot → Ski → MountainBike → PreO`.
2. **Indoor är en gren hos dem, inte en distans.** Hos oss är den en `Discipline`. Vi har den på
   fel axel.
3. **Natt och Stafett är inte distanser** i modellen — natt är `raceLightCondition`, stafett är
   `eventForm` (`RelaySingleDay` …). Men deras *filter* visar dem ändå under "Distanser", precis
   som vår `Discipline` slår ihop dem. Den sammanslagningen är alltså inte vårt påhitt, och den
   kan stå kvar.

Klassificeringen är en tredje uppräkning: `Championship, Club, CourseOfTheWeek, International,
Local, National, Regional, Unknown` — vilket är exakt de sju "Tävlingstyp"-alternativen i deras
filter, och vad vår `CompetitionLevel` redan speglar.

**Ej verifierat:** om det *klassiska* API:et vi läser (`eventor.orientering.se/api/events`) skickar
med grenen. Dokumentationssidan listar bara query-parametrar, inte elementets barn, och nyckeln i
`local.settings.json` ger 403 så jag kunde inte hämta ett riktigt svar. Se "Öppen fråga" nedan.

## Förslag till modell

```
Competition
  Sport      Foot | Indoor | MountainBike | Ski | PreO | Shooting   (gren)
  Discipline Sprint | Middle | Long | UltraLong | Night | Relay      (som i dag, minus Indoor)
```

`Sport` som **ett** värde, inte en mängd, valt med Eventors egen prioritetsordning. Ett
arrangemang som är både OL och MTBO blir OL — vilket är den ofarliga riktningen: den som valt bort
MTBO tappar inget som också är OL.

`Discipline.Indoor` flyttar till `Sport.Indoor`. En inomhussprint blir `Sport.Indoor` +
`Discipline.Sprint`, vilket är vad den är.

**Var grenen kommer ifrån**, i fallande ordning:

1. `<DisciplineId>` ur XML:en, om elementet finns.
2. Namnet — "MTBO", "MTB-O", "Skid-O", "PreO", "TempO", "indoor". Det är redan så `Indoor` härleds
   i dag, med en kommentar som säger varför.
3. `Sport.Foot`, som är vad nio av tio arrangemang är.

## Två saker, inte ett

Ditt önskemål delar sig i två mekanismer som inte ska blandas:

- **Grenar jag inte håller på med ska inte synas.** Ett hårt filter. Om jag inte cyklar vill jag
  aldrig se MTBO, inte ens rankat lågt.
- **Former jag gillar ska ligga högt.** En vikt. "Indoor + OL Sprint" är inte ett filter — jag
  vill fortfarande se DM lång, bara längre ned.

Båda bor under **Jag**, som två listor: *Mina grenar* (kryss) och *Det jag helst springer*
(gren + distans-kombinationer). Den första går in i `EventFilter` som ett förval som alltid
gäller; den andra går in i `RelevanceEngine` som en egen term bredvid geografi, tid och nivå — och
in i Hems sortering.

## Beslut om förval (avgjort)

**"Mina grenar" frågas i onboardingen**, med OL förkryssat. Rätt lista från första skärmen, och
den som cyklar blir aldrig av med sina tävlingar utan att ha sagt något. Behöver en väg in för
den som redan kört appen — inställningen under Jag är den vägen.

**"Det jag helst springer" är gren + distans-par**, inte två listor: man kan gilla inomhussprint
utan att gilla skogssprint.

**Och listan är sorterbar, inte kryssad.** Man vill kunna ha med många, viktade. Positionen är
vikten — plats ett väger tyngst — vilket också gör att listan säger vad den gör utan att någon
måste förstå en skala. `RelevanceEngine` läser index, inte ett bockat/obockat.

## Changes — steg 1: grenen som egen axel

- `Sport` i domänen: `Foot, Indoor, MountainBike, Ski, PreO, Shooting`, ordagrant Eventors
  uppräkning. `Competition.Sport` med `Foot` som förval.
- **`Discipline.Indoor` är borta.** Indoor är en gren hos källan, inte en distans, och satt på fel
  axel hos oss. "Hallsberg Indoor sprint" är nu `Sport.Indoor` + `Discipline.Sprint`, vilket är
  vad den är.
- `SportNames.In(name)` — grenen ur namnet, samma väg som Indoor redan lästes.
- `EventorNormalizer.SportOf` — läser `<DisciplineId>` först där elementet finns, med Eventors
  egen prioritetsordning, och faller tillbaka på namnet. Båda vägarna slutar i `Foot`.
- `Format.Sport` (tom för OL) och `Format.SportOrDefault` (med ord, för chippet).
- Radens metarad skriver ut grenen när den inte är OL: "MTBO · Sprint · OK Kåre".
- `EventFilter.Sports` som egen mängd, med egen `FilterSection` "Gren" i arket och egna
  borttagbara fasettchips.
- Inomhusglyfen tas ur bruk med axeln den satt på; `ArenaImageKey` och `ArenaImage` läser
  `Sport.Indoor` i stället.
- **`TolerantEnumConverter`** — se nedan.

## Decisions

**En gren per tävling, inte Eventors lista.** Deras `disciplines` är en array; vi tar ett värde
med deras egen prioritetsordning (`Indoor → Foot → Ski → MountainBike → PreO`), den de själva
använder för att välja ikon. Den löser mot `Foot`, vilket är den ofarliga riktningen: den som
stängt av MTBO tappar inget som också är OL.

**Grenen är ett ord, inte ett märke.** Märkeskolumnen har redan disciplinens form och
mästerskapspokalen; ett tredje märke gör raden oläslig, och MTBO/Skid-O/PreO skulle behöva
nyritade former. Ordet står först i metaraden och bara när grenen inte är OL — samma regel som
nivån följer.

**Okända enum-namn läses som förvalet i stället för att kasta.** Det här hittades genom att
flytta Indoor: paket och cachade svar överlever den appversion som skrev dem, kalendern
avserialiseras i ett stycke, och ett enda ord den nya versionen inte känner igen tömde hela
listan. Ett värde vi inte kan namnge är ett fält som blir fel; att kasta är hela skärmen.
`TolerantEnumConverter` i `OrienteraJson`, med två testfall.

## Kvar — steg 2

- **Onboarding:** ett steg "Vilka grenar håller du på med?" med OL förkryssat.
- **Jag:** *Mina grenar* (hårt filter, går in i `EventFilter` som ett stående förval) och
  *Det jag helst springer* (sorterbar lista av gren+distans-par).
- **`RelevanceEngine`:** en term som läser positionen i den listan, bredvid geografi, tid och nivå.
- **Hem:** samma ordning i blockens sortering.
