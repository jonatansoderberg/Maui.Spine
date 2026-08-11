# Issue #53 — Live: välj klass i stället för att visa alla på en gång

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/53
**Branch:** issue/53-live-class-picker
**Status:** Completed

## Plan

"Alla" radar upp fyrtio klasser på en sida och kostar en förfrågan uppströms per klass. Chipsen
blir i stället **Min grupp / Min klass / Klass ▾**, där det sista öppnar ett ark med tävlingens
klasser. Valet sparas lokalt per tävling.

- `LiveScope.Everyone` blir `LiveScope.Class`. Med en vald klass hämtas bara den.
- `ChooseClassSheet` tar emot klasserna som navigeringsparameter i stället för att lista en
  hårdkodad uppsättning. Tävlingssidan skickar då tävlingens klasser, vilket den redan har.
- `LiveClassStore` minns vald klass per tävling, i telefonen, som identitet och notisval.

Klasslistan kommer från `Competition.Classes` som redan är fylld från Eventor — väljaren kostar
inget nytt anrop.

## Changes

- `LiveScope.Everyone` → `LiveScope.Class`. Urvalet hämtar och filtrerar på den valda klassen, så
  "Alla" och dess fyrtio förfrågningar per hämtning är borta.
- `LivePageViewModel` — `PickClassCommand` öppnar väljaren, `SelectedClass` bär valet,
  `ClassChipText` är chipets etikett och `CanPickClass` döljer chipet för en tävling utan klasser.
  `AdoptCompetitionAsync` läser tävlingens klasser och det sparade valet en gång per tävling.
- `LiveClassStore` — vald klass per tävling, i en JSON-fil bredvid identiteten och notisvalen.
- `ChooseClassSheet` tar emot en `ClassChoice` (klasserna och vad valet betyder) i stället för att
  lista en hårdkodad uppsättning. `EventDetailsPage` skickar tävlingens klasser och sin egen
  förklaring; Live skickar sina.
- `LivePage.View.xaml` — tredje chipet är väljaren, med den valda klassen som etikett.

## Decisions

- **Chipet är en väljare, inte ett filter.** Att trycka på det frågar alltid vilken klass, även när
  en klass redan är vald — annars skulle man behöva ett eget sätt att byta klass, och chipet skulle
  se ut som ett läge man kan slå på och av.
- **Klasserna kommer från tävlingens egen sida, inte från livelistan.** Kalenderns projektion av en
  tävling bär inga klasser (`EventorNormalizer` sätter `Classes = []` där); de fylls först i
  `GetCompetitionAsync`. Vymodellen hämtar därför tävlingen en gång per tävling — båda sidor
  cachar den, så det kostar ingenting efter första gången. Det upptäcktes i simulatorn: chipet
  syntes inte alls, eftersom `Classes` var tom.
- **Ett sparat val som klassen inte längre finns i ignoreras.** Tävlingar ändrar klassuppsättning,
  och ett filter som pekar på en klass som inte finns ger en tom lista utan förklaring.
- **Min grupp hämtar fortfarande brett.** En grupp springer i flera klasser och livekällan är bara
  sökbar per klass. Att smalna av även den — till de klasser gruppen faktiskt är anmäld i — är ett
  eget ärende.
- **Förklaringstexten följer med valet.** Samma ark används från två håll och betyder olika saker:
  "Klassen styr banan, startlistan och prediction" på tävlingssidan, "Livelistan visar klassen du
  väljer" i Live. Ett ark som påstår fel sak om sig självt är värre än två ark.

## Verifiering

`dotnet test`: 212 gröna.

**iPhone 17 Pro-simulator (iOS 26.2) mot skarp data** via BFF-stubben: chipet **Välj klass** öppnar
Norrlandsmästerskapens fyrtio riktiga klasser med Live-texten. Val av H21 gör chipet till "H21" och
listan till H21:s sträcktabell — Xoel Chamorro 12:a vid 79 och 7:a vid 88, precis den utveckling
#43 byggdes för. Valet överlever både flikbyte och omstart av appen: efter en kall start öppnar Live
direkt i H21.

**Mot fake-datat:** chipet finns, och det sparade valet från den skarpa tävlingen läcker inte in —
det är en annan tävling och har sitt eget minne. Tävlingssidans ark visar nu tävlingens egna
klasser med sin egen förklaring, oförändrad i övrigt.

Körningen avslöjade en sak: chipet syntes först inte alls, eftersom livelistans tävling kommer från
kalendern och kalenderns tävlingar saknar klasser. Det löstes med en hämtning av tävlingens egen
sida, en gång per tävling.

