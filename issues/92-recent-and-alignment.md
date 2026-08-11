# Nyligen i tävlingslistan, och en feljusterad sektionsrubrik

**Branch:** issue/92-recent-and-alignment
**Status:** Completed

Två små saker efter #56, båda upptäckta av användaren i körning.

## Changes

- `EventTimeline` — det som kördes **igår** arkiveras inte längre utan ligger kvar i listan under
  rubriken **Nyligen**, överst. Allt äldre går till "Tidigare"-chippet.
- `EventsPage.View.xaml` — sektionsrubrikens vänsterkant flyttad från 4 till 16, så den följer
  kortens kant i stället för listans.

## Decisions

- **En dag bakåt, inte tio.** Första förslaget var ~10 dagar; användaren valde två dagar (idag
  och igår). Dagens tävlingar låg redan kvar under "Denna vecka" med "IDAG" på kortet, så det
  som faktiskt behövde ändras var gårdagen — den man fortfarande letar sitt eget resultat i.
- **Rubriken följer korten.** Listan har inget eget vänsterled att hålla sig till; kortens kant
  är den linje ögat läser efter.

## Verifiering

`dotnet test`: 246 gröna (244 + 2 nya).

**iPhone 17 Pro-simulator (iOS 26.2):** "MEST RELEVANT", "DENNA VECKA", "NÄSTA VECKA" och
"AUGUSTI" ligger i linje med korten. **Nyligen** syns inte i demoläget — det seedade datat har
ingen tävling igår, så regeln är bara täckt av testerna.
