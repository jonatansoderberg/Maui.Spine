# Issue #159 — Android: en sida som utesluter toppen ritas ändå under statusfältet

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/159
**Branch:** issue/159-android-sida-som-utesluter-toppen
**Status:** In Progress

## Plan

Fyndet kom ur verifieringen av Hems skrollbeteende på Android. En sida som utesluter den övre
kanten ritas ändå under statusfältet, medan samma sida bleedar korrekt på iOS.

Felet ligger i Spine och inte i appen, så det lagas där.

## Changes

### `Presentation/NavigationRegion.cs` ✅

`UpdateContainerMargin` kompenserade plattformens egen förskjutning bara på iOS. Android får samma
kompensation — men **bara i överkanten**.

## Decisions

- **Bara toppen på Android.** iOS drar tillbaka alla fyra kanter, för där lägger MAUI förskjutningen
  på alla fyra. På Android äger Material-baren underkanten: den lägger navigeringsinsetet på sig
  själv och rapporterar sin egen höjd som varje flikssidas botteninset (`ApplyTabBarInset`). Att dra
  containern nedåt där hade dragit innehåll under en bar som inte är målad för att ritas under.

- **Metodens dokumentation stämde inte med koden.** Den påstod att Android hanterades. Nu gör den
  det, och kommentaren beskriver vad som faktiskt sker på respektive plattform.

## Verifiering ✅

Uppmätt på Android 17 (Pixel-emulator, 3x) före och efter:

| | Före | Efter |
|---|---|---|
| Hem (utesluter toppen) — var bilden börjar | 52,0 dp | **0,0 dp** |
| Tävlingar (inkluderar toppen) — rubrikens plats | oförändrad | oförändrad |
| Underkanten, sista kortet mot flikraden | fritt | fritt |

Uteslutna vägar, alla uppmätta och förkastade innan orsaken hittades:

- Spine får rätt värde: `edges=Left, Right insetTop=52` i `ApplySafeAreaPadding`.
- `SpineTabPage`s nativa vy ligger på `screenY=0` med `PaddingTop=0`.
- MAUI-nivåns `SafeAreaEdges="None"` på sidan, på dess rotlayout och på `NavigationRegion`s
  container ändrar ingenting.

Build grön för iOS, Mac Catalyst och Android. iOS kontrollerad efteråt: hjälten bleedar som förut.

## Open Questions

- **Ett andra Android-fynd, inte åtgärdat här:** listsidornas underkant får ett extra tomt band
  ovanför flikraden — uppmätt på Tävlingar som ett fält av listans egen yta under sista raden,
  ovanpå en bar som redan tar sin plats. iOS har det inte, eftersom dess flikrad flyter över
  innehållet. Misstanken är att botteninsetet betalas två gånger, men det är inte utrett och
  hör till sitt eget issue.
