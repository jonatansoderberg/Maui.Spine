# Issue #59 — Notiser: Av/På är en knapp, ska vara en switch

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/59
**Branch:** issue/59-notification-switch
**Status:** Completed

## Plan

Varje notistyp slogs av och på med en knapp märkt "Av" eller "På" — tvetydigt (*är* den av, eller
stänger jag av den?) och avvikande: `EventFilterSheet` i samma app använder redan `Switch`.

Knapparna fanns bara för att switcharna troddes vara döda i en sheet ([#36](https://github.com/jonatansoderberg/Maui.Spine/issues/36)).
Den diagnosen visade sig vara fel — se `issues/36-switch-in-sheet-template.md` — så blockeringen
finns inte, och raden kan bli det den skulle varit.

## Changes

- `NotificationSheet.View.xaml` — `Switch` med `IsToggled` i stället för knapp med `Text` och
  `Command`.
- `NotificationRow` — `Requested` (vad användaren bad om) och `Settle` (skriva ett värde utan att
  fråga igen). `StateText` är borta.
- `NotificationSheetViewModel` — `ToggleCommand` ersatt av `ApplyAsync`, som frågar systemet om
  tillstånd, sparar, och ställer tillbaka switchen om svaret är nej.

## Decisions

- **Switchen rör sig först, appen städar efter.** En tvåvägsbindning innebär att kontrollen redan
  har flyttat sig när koden får veta det. Vid nekat tillstånd ställs den tillbaka via `Settle`,
  som stänger av återkopplingen så att tillbakaställningen inte läses som en ny begäran. Samma
  väg används när sparade värden läses in — ett lagrat läge är inte något användaren just bad om.
- **Statusraden bär förklaringen.** Switchen kan bara visa på eller av; *varför* den slog tillbaka
  får plats i raden ovanför, med vad man gör åt det ("Slå på dem där först"). Raden återställs
  vid nästa lyckade ändring så att ett gammalt nekande inte blir kvar.
- **Uppläst form säger inte läget två gånger.** En switch läses av skärmläsaren som på eller av av
  sig själv. `Accessibility` säger därför vad inställningen är till för — etikett och förklaring —
  och ingenting om var den står.

## Verifiering

`dotnet test`: 221 gröna (ren vy- och vymodelländring).

**iPhone 17 Pro-simulator (iOS 26.2):** switcharna visas på varje rad, "Anmälan stänger snart"
slår om till på, och läget står kvar efter att sheeten stängts och öppnats igen.

**Inte verifierat:** nekat systemtillstånd. Simulatorn beviljade notistillstånd utan dialog, så
återställningsvägen (`Settle(false)` plus förklarande statusrad) är läst men inte körd. Den kräver
en enhet där tillstånd nekats i systeminställningarna.

**Testanmärkning:** en switch på iOS reagerar inte på ett ögonblickligt syntetiskt tryck. Verifiering
kräver ett tryck som håller kvar (~120 ms) eller ett drag över knoppen. Det var precis den
detaljen som gjorde att #36 filades.
