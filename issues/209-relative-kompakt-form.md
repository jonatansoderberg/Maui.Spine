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

## Verifiering

Byggt. Utseendet i Dynamic Island återstår att bekräfta på enhet.
