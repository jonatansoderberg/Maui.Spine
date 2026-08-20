# Switch svarar först på ett tryck som håller kvar (iOS)

**GitHub:** _issue ej skapad än_
**Branch:** issue/switch-instant-tap
**Status:** Completed

## Plan

Rapporten: kontroller i en sheet känns inte klickbara, man måste hålla kvar en stund — switcharna i
notisinställningarna i Orientera som exempel. Misstanken var att sheeten tar över inmatningen för
sina egna gester (dra för att ändra storlek, dra för att stänga).

Mätningen på iPhone 17 Pro-simulatorn (iOS 26.2) motsäger den misstanken:

| Test i notissheeten | Utfall |
|---|---|
| Ögonblickligt tryck (0 ms) på X-knappen i sheeten | stänger — knappen svarar direkt |
| Ögonblickligt tryck (0 ms) på switch | ingen reaktion |
| Tryck 16 ms / 50 ms på switch | slår om |
| Tryck 90 ms med 12 pt vertikalt glid | slår om |
| Första trycket direkt efter att sheeten öppnats | slår om |
| Samma 0 ms-tryck på en switch i Apples Inställningar (Kamera → Rutnät) | **ingen reaktion** |

Sheeten fördröjer alltså ingenting — knappen i samma sheet svarar på 0 ms. Det är `UISwitch` som
ignorerar en beröring som börjar och slutar inom samma varv i körslingan, och den gör det lika mycket
i Apples egna appar. Men eftersom det är just i en sheet, omgiven av draggester, som beteendet syns
och läses som "sheeten åt trycket", ska Spine ändå ta hand om det: ett ögonblickligt tryck på en
switch ska slå om.

## Changes

- `Extensions/SwitchHandlerExtensions.Apple.cs` (ny) — `SwitchHandler.Mapper` hänger en
  `SpineInstantSwitchRecognizer` på varje `UISwitch` på iOS och Mac Catalyst. Den avgör trycket när
  beröringen släpps, oavsett hur kort den var, och gör anspråk på beröringen så att en pan längre upp
  i trädet (sheet-drag, sheet-stängning, interaktiv bakåtsvep) inte hinner ta den ifrån kontrollen.

### Följdfråga: listan såg klippt ut i botten

Mätt i samma sheet: kortet klipptes vid 818 pt och sheetens underkant låg vid ~868 pt — 50 pt
dött band emellan. De 50 var hemindikatorns 34 pt, som `ApplySafeAreaPadding` lade på eftersom
sheet-sidor ärver `SafeAreaEdges.All`, plus `CollectionView`:ns egna `Margin`-botten på 16 pt.
Klippningen i sig är vanlig scroll-beteende, men på `Medium`-detenten scrollar iOS inte listan för
sig — ett drag uppåt fäller ut sheeten först — så den halva raden låg kvar som halv ovanför ett
tomt band.

- `NotificationSheet.cs` — `SafeAreaEdges` utan `Bottom`, så listan går ända ut till sheetens kant.
- `NotificationSheet.View.xaml` — `Margin="16,0,16,0"` (bottenmarginalen borta) och en `Footer` vars
  höjd är `SafeAreaInsets.Bottom`, så sista raden kan scrollas fri från hemindikatorn.

Samma mönster som tabbsidorna redan använder — de tar bottenkanten ur `SafeAreaEdges` i
`options.TabDefaults` och betalar klarningen i sin egen `CollectionView.Footer`.

## Decisions

- **Ligger på handlern, inte på sheeten.** En switch beter sig likadant var den än står; att låta
  fixen gälla bara i en sheet hade gjort samma kontroll olika på olika sidor.
- **Rörelse förbi 10 pt låter UIKit ta över.** Då är det ett drag i knoppen, en scroll i listan eller
  ett drag i sheeten — verifierat att alla tre fortfarande fungerar, och att ett drag inte slår om
  switchen av misstag.
- **`CancelsTouchesInView` avgör dubbelslaget.** När gesten känns igen annulleras beröringen inne i
  `UISwitch`, så kontrollens egen spårning inte slår om en andra gång vid ett normallångt tryck.
  Som skydd även mot en omslagning UIKit gör på annan väg läses värdet av ett varv senare: har något
  annat redan flyttat switchen räknas det som trycket, och den här gesten gör ingenting.
- **Att dra i en switch flyttar fortfarande sheeten.** Det är iOS eget beteende för en sheet vars
  innehåll inte är scrollat, och trycket — det som var trasigt — är opåverkat.

## Verifiering

`dotnet test`: 394 gröna.

**iPhone 17 Pro-simulator (iOS 26.2), Orientera från den här grenen:**

| Test | Utfall |
|---|---|
| Ögonblickligt tryck (0 ms) på switch i sheeten | **slår om** |
| Tryck 220 ms på samma switch | slår om **en** gång — inget dubbelslag |
| Drag uppåt med start på en switch | sheeten fäller ut, ingen switch slår om |
| Drag nedåt i sheeten | stänger som förut |
| Stäng och öppna sheeten igen | läget ligger kvar — bindningen fick värdet |

**Listans botten, samma simulator:**

| Test | Utfall |
|---|---|
| `Medium`-detenten | listan går ända ut till sheetens kant, inget dött band |
| `FullScreen`-detenten | alla sex rader syns, som förut |

**Inte verifierat:** Mac Catalyst (bygger, men är inte körd) och Android, som inte är berörd av
ändringen. Inte heller sidfotens klarning mot hemindikatorn — sex rader får plats på
`FullScreen`-detenten, så listan scrollar aldrig så långt idag. Sidfoten finns för när listan växer.
