# Issue #97 — Navigeringsremsan under tabbaren följer inte temabytet

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/97
**Branch:** issue/97-nav-strip-theme
**Status:** Completed

## Plan

Efter #22 följde Material-baren temat, men remsan under den — gestnavigeringens yta — stod kvar i
förra temats färg. Upptäckt av användaren i körning, och bekräftat genom att läsa färgen ur
skärmbilderna.

## Changes

- `SpineTabbedHostPage.Android.cs` — samma ombyggnad som färgar baren sätter nu också fönstrets
  bakgrund till samma yta.

## Decisions

- **Fönsterbakgrunden, inte `setNavigationBarColor`.** Den setter är no-op från Android 15 för
  appar som ritar edge-to-edge, och den här gör det (`SetDecorFitsSystemWindows(window, false)`).
  Systemet målar alltså ingenting över remsan; det som syns där är fönstrets egen bakgrund. Jag
  provade `setNavigationBarColor` först och mätte att remsan inte ändrade sig — det var mätningen
  som pekade ut rätt yta.
- **Samma färg som baren, inte en egen.** Remsan ligger direkt under baren och ska läsas som en
  fortsättning på den. Att lösa ut `colorBackground` separat hade gett två nyanser som nästan,
  men inte riktigt, matchar.

## Verifiering

**Pixel Tablet-emulator (API 36).** Färgerna avlästa ur skärmbilderna:

| Läge | Baren | Remsan |
|---|---|---|
| Start i mörkt | `#141218` | `#141218` |
| Byte till ljust, utan omstart | `#FEF7FF` | `#FEF7FF` |
| Tillbaka till mörkt | `#141218` | `#141218` |

Före ändringen: remsan `#141218` även i ljust läge.
