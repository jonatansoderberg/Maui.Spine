# Issue #209 — Spine.Widgets: W.Relative är för bred för Dynamic Island

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/209
**Branch:** issue/209-relative-kompakt-form
**Status:** Completed

## Orsak

`W.Relative` blir `Text(date, style: .relative)`, och den stilen skriver alltid två enheter:
"18 min, 35 secs". Ön breddas efter sitt bredaste innehåll, så den långa texten till höger drog ut
hela ön och lämnade vänsterregionen med ett gap — som såg ut som ett layoutfel men var en följd av
textbredden.

## Changes

- `RelativeDateNode` fick `bool? Compact`. Renderaren väljer `.timer` i stället för `.relative` när
  den är satt, vilket ger `18:35`.
- `W.Relative(date, compact: false)` — valfri parameter, i samma stil som `W.Icon(name, color)`.
  Serialiseras bara när den är satt, tack vare `WhenWritingNull`.
- `.monospacedDigit()` på `relative`-fallet. `timer` hade den redan; utan den hoppar texten i sidled
  medan siffrorna tickar, vilket syns mest i ett smalt utrymme.
- Samplet använder långformen på låsskärmen och klockan i compact och expanded.
- Wikin fick "Choosing a time node" med de tre alternativen och regeln att välja efter utrymmet.

## Decisions

- **En flagga, inte en ny nod.** `.relative` och `.timer` visar samma sak i olika form; två nodtyper
  hade tvingat den som byter form att också byta typ och tappa sina stilar.
- **`bool?` i stället för `bool`.** `WhenWritingNull` håller då JSON oförändrad för alla som inte
  använder flaggan — inga nya fält i payloads som redan ligger nära APNs fyra kilobyte.
- **Långformen kvar som default.** Den läser bäst där det finns plats, och det är där de flesta
  träden hamnar. Den som behöver smalt behöver också veta om det.

## Vad undersökningen egentligen visade

Utgångspunkten var fel. Bredden följer inte strängens längd: **självuppdaterande text tar all plats
den erbjuds inuti en Live Activity.** Det är ett känt SwiftUI-fel sedan iOS 17, utan officiell fix,
och det gäller `.timer` och `.relative` lika mycket — vanlig `Text` har det inte.
[Apples forumtråd](https://developer.apple.com/forums/thread/723316)

Därför ändrade `compact: true` ingenting på bredden: den kortade strängen, inte anspråket.

`.fixedSize(horizontal: true, vertical: false)` provades i renderaren och gjorde det värre — texten
ritades som ingenting och bredden stod kvar. Borttagen igen, med en kommentar i koden så nästa person
inte upprepar försöket. Forumtrådens egen lösning är en hårdkodad `.frame(width:)` bred nog för det
längsta värdet, vilket ett ramverk inte kan välja åt en app.

## Verifierat på enhet

iPhone 16 Pro, tre mätningar:

| Compact-innehåll | Ön |
|---|---|
| `W.Relative(date)` — "18 min, 35 secs" | full bredd |
| `W.Relative(date, compact: true)` — "0:27" | full bredd |
| `W.Text("09:43")` | **rätt bredd** |

Slutsatsen är alltså inte en kodfix utan en layoutregel, och samplet följer den nu: tickande tid på
låsskärmen och i expanded, en kort stämpel i compact.

`compact: true` är ändå värd att behålla — den ger `18:35` i stället för `18 min, 35 secs` där
utrymmet är knappt men inte obefintligt, som i den expanderade vyns trailing-region.
