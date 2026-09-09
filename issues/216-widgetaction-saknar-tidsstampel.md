# Issue #216 — Spine.Widgets: en widgetknapps tap saknar sin tidsstämpel, och på iOS blir den fel

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/216
**Branch:** issue/216-widgetaction-saknar-tidsstampel
**Status:** Completed

## Plan

Tiden spelas redan in på iOS — `SpineWidgetIntent.perform` skriver `at` i `actions.jsonl`. Den
kastas bort i `TakeActions`, som läser två av tre fält, eftersom `WidgetAction` inte har någon plats
att bära den i. Fixen är att ge den en plats och sluta kasta bort det som redan finns.

### `WidgetAction` får tidpunkten

```csharp
public sealed record WidgetAction(string Kind, string ActionId, DateTimeOffset At);
```

### iOS: läs `at` i stället för att hoppa över det

`WidgetPlatform.TakeActions` returnerar `(string Kind, string ActionId, DateTimeOffset At)`. Fältet
är sekunder sedan epoch som en `double` — Swift skriver `Date.now.timeIntervalSince1970`. En rad
utan giltigt `at` faller tillbaka på "nu": en trasig rad ska inte tappa tappet.

`DrainActions` skickar tiden vidare till `HandleActionAsync`.

### Android: sätt den när receivern tar emot tappet

`SpineAppWidget.Tapped` körs i samma ögonblick som tappet, så `DateTimeOffset.Now` där är sant.

### `HandleActionAsync`

Tar emot `DateTimeOffset at` och lägger den i recorden.

### Följdändringar

- Push-samplets `OnActionAsync` använder `action.At` i stället för `DateTimeOffset.Now` — det är
  hela poängen med issuet, och samplet är där skillnaden går att se.
- Wikins Buttons-avsnitt: att `At` finns, och att den är tappets tid och inte handlerns.

## Open Questions

Inga.

## Changes

- `WidgetAction` fick `DateTimeOffset At`, med en doc-kommentar om varför den inte är samma sak som
  `DateTimeOffset.Now` i handlern.
- `WidgetPlatform.TakeActions` (iOS) returnerar tiden, läst ur `at` i `actions.jsonl` genom en ny
  `TappedAt`.
- `DrainActions` och `SpineAppWidget.Tapped` skickar den vidare; `HandleActionAsync` tar emot den.
- Push-samplets `OnActionAsync` stämplar med `action.At` i stället för `DateTimeOffset.Now`.
- Wikins Buttons-avsnitt beskriver `At` och varför den finns.

## Decisions

- **Tiden på recorden, inte en separat överlagring av `OnActionAsync`.** Ett tap *har* en tidpunkt;
  den hör hemma i beskrivningen av tappet. Alternativet — en andra metod på interfacet för handlers
  som bryr sig — hade lagt två vägar in i samma händelse.
- **Brytande ändring accepterad.** `WidgetAction` konstrueras bara av Spine; en app tar emot den.
  En handler som inte läser tiden märker ingenting, och den som gör det får rätt svar i stället för
  ett tyst fel.
- **Millisekunder, inte sekunder, från `at`.** Swift skriver `timeIntervalSince1970` som en `double`.
  `FromUnixTimeSeconds` tar en `long` och hade kastat bort decimalerna; `* 1000` behåller dem.
- **En rad utan `at` faller tillbaka på nu i stället för att kastas.** Ett tapp som försvinner är
  värre än ett tapp med fel tid — och fältet skrivs alltid, så vägen är till för en trunkerad fil.
- **Android sätter tiden i `Tapped`, inte i intentet.** Receivern körs i tappets ögonblick, så
  `DateTimeOffset.Now` där *är* tappets tid. Att lägga en extra i intentet hade varit två källor
  till samma sanning.

## Verifierat

**iOS-simulator (iPhone 17, iOS 26.4).** Appen terminerad, tap 10:51:12, appen öppnad 10:51:59.
Widgeten säger `Kvitterad 10:51:12`. Före ändringen sa den 10:44:00 i det motsvarande testet — alltså
dräneringens tid, 30 sekunder efter tappet.

**Android-emulator (API 37).** Appen force-stoppad, tap 10:56:21.99 enligt logcat, widgeten säger
`Kvitterad 10:56:22`. Oförändrat beteende, som avsett.
