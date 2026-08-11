# Issue #89 — "Följ live" byter tyst tävling när livekälla saknas

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/89
**Branch:** issue/89-follow-live-honest
**Status:** Completed

## Plan

Tävlingssidan visade **Följ live** så snart tävlingen pågick enligt kalendern. Live kommer från
LiveResults, som bara har de tävlingar `CompetitionMatcher` matchat. Trycket landade då i
Live-fliken på en **annan** tävling, utan att något sades.

## Changes

- `EventDetailsPage.ViewModel` — `CanFollowLive` kräver nu både att tävlingen pågår *och* att den
  finns bland dem backend rapporterar som live.
- `HasPrimaryAction` — den stora knappen döljs när det enda tillståndet erbjuder är en åtgärd
  appen inte kan utföra.

## Decisions

- **Kalendern vet inte om LiveResults.** `ContextEngine` avgör tillstånd ur tider; den kan inte
  veta om någon sänder. Den listan finns däremot redan: `GetLiveCompetitionsAsync`, samma som
  live-fliken läser, alltså ett cachat anrop och inget nytt kontrakt.
- **Båda knapparna, inte bara den ena.** Snabbhandlingen och den stora primärknappen går genom
  samma `ContextAction.FollowLive`. Att bara grinda den ena hade lämnat kvar felet i den knapp
  som faktiskt trycks.
- **Att inte veta är inte att veta att det inte finns.** Vid `SourceUnavailableException` står
  knappen kvar och misslyckas som allt annat gör offline. Att dölja den vid nätverksfel hade
  gjort en tillfällig störning till ett saknat funktionsläge.

## Verifiering

`dotnet test`: 246 gröna.

**iPhone 17 Pro-simulator (iOS 26.2), båda hållen:**

- **Mot skarp data via stubben:** "Veckans bana – Hemlingby v 33" pågår enligt kalendern men
  saknar livekälla. LIVE-märket står kvar (det är sant att den pågår), den stora knappen är borta
  och snabbhandlingen Live är nedtonad.
- **Demoläget:** Norrlandsmästerskapen Lång har livekälla och visar "Följ live" som förut.

Utan det andra testet hade jag bara stängt av funktionen.
