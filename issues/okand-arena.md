# Arenan utan position: 6905 km till Guineabukten

**GitHub:** _issue ej skapad än_
**Branch:** issue/okand-arena
**Status:** Completed

## Felet

"DM, lång, Gästrikland" (Storviks IF) visade **6905 km** i en lista där grannarna visade 12–41 km.
6905 km är ungefär avståndet från Gävle till punkten (0, 0) i Guineabukten — Eventor hade inte
publicerat någon arenakoordinat, `GeoPoint` är en struct, och dess `default` är en riktig punkt på
jordklotet.

Felet fanns före omgörningen av listraderna. Det är inte bara kosmetiskt:

| Var | Vad som hände |
|---|---|
| Tävlingslistan | "6905 km" i avståndskolumnen |
| Tävlingssidan | "Resa: ~5178 min", "ca 6905 km fågelvägen" |
| Aviseringar | "Dags att åka" räknat från 86 timmars resa — larmet tre dygn för tidigt |
| Relevans | Geografipoängen klampades till noll, rätt utfall av fel skäl |
| Nära-filtret | Uteslöts, också av fel skäl |

## Vad som gjordes

Fixen sitter i formen, inte i data: riktig Eventor-data kommer alltid att innehålla tävlingar
utan koordinat, och en rättad rad i testdatan hade inte hjälpt.

- `GeoPoint.IsKnown` — origo är inte en plats. Att läsa nollan som "osatt" är att läsa den kodning
  källorna redan använder, precis som `HasFirstStart` läser midnatt.
- `Competition.HasArena` och `Competition.DistanceFrom(home)` som ger `double?`. Vakten hör hemma
  där, inte på de sex ställen som frågar — en oplacerad arena är inte *långt bort*, den är okänd,
  och vart och ett av de ställena måste säga olika saker om de två.
- `Format.Distance(double?)` ger tankstreck, samma som varje annan kolumn i appen.
- `EventCard` bär `DistanceKm` som `double?` och härleder både kolumnen ("—") och talsträngen
  ("avstånd okänt"). Ett tankstreck läses upp som "streck" eller inte alls.
- Radiefiltret och `Nära` utesluter nu oplacerade arenor uttryckligen.
- Resa-rutan på tävlingssidan ritas inte alls utan arena.
- Aviseringen "Dags att åka" planeras inte utan arena. Resten av tävlingens aviseringar gör det.

## Decisions

**Resa-rutan göms i stället för att säga "okänt".** Den är en bricka i en rad av fakta, och dess
frånvaro är tystare än en bricka vars enda innehåll är att den saknar innehåll.

**En halv nolla räknas som placerad.** `IsKnown` kräver bara att *en* av lat/long är skild från
noll. Ekvatorn och nollmeridianen går igenom bebodda platser; en källa som ger upp ger upp om båda.

**Relevansen ändrar inte utfall, bara skäl.** En oplacerad arena får noll på avstånd — vi kan inte
påstå att den är nära — och står kvar på distriktsandelen. Samma tal som klampningen gav, men nu
uttryckt.

## Tester

`ArenaPositionTests` (5), plus ett fall vardera i `FormatTests`, `EventFilterTests` och
`NotificationPlannerTests`. 495 gröna.
