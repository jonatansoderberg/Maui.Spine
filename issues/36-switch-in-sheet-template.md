# Issue #36 — Switch i en DataTemplate i en sheet reagerar inte på tryck (iOS)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/36
**Branch:** issue/36-switch-in-sheet-template
**Status:** Completed — inget fel fanns

## Plan

Ärendet beskrev en `Switch` i en `DataTemplate` i en sheet som ignorerade tryck på iOS, medan en
`Button` i samma mall svarade. Planen var att bygga en minimal reproduktion i Spines egen
sample-app — utan den går det inte att säga om felet ligger i Spines sheet-host, i MAUI:s
`Switch`-handler eller i kombinationen.

## Changes

- `samples/MauiSpineSampleApp/Pages/ToggleListSheet.*` — en sheet med alla tre fallen bredvid
  varandra: switchar i en `DataTemplate`, en switch bunden direkt mot vymodellen, och en knapp i
  samma mall. Varje inmatning skriver en rad högst upp, så en körning skiljer fallen åt.
- `MainPageOld` — en knapp som öppnar den.
- `MauiSpineSampleApp.csproj` — registrerar de nya filerna. Projektet tar bort alla
  `Pages\**\*.ViewModel.cs` och lägger tillbaka dem en och en; en ny vymodell som inte läggs
  till där kompileras inte, och felet syns bara som att XAML inte kan slå upp typen.

Ingen kod i `Plugin.Maui.Spine` är ändrad. Det fanns inget att ändra.

## Decisions

- **Ärendet beskrev inget fel.** Reproduktionen visar att switchen fungerar i alla tre lägena:
  i mallen, utanför mallen, i en sheet och på en vanlig sida. Bindningen skriver till vymodellen
  och kontrollen ritar om sig.
- **Felet låg i mätmetoden.** Den ursprungliga diagnosen gjordes genom att styra simulatorn med
  ögonblickliga syntetiska tryck — beröring ned och upp i samma ögonblick. `UISwitch` slår om
  via en gesture recognizer som kräver att beröringen varar en stund; `UIButton` spårar beröring
  direkt och svarar på ett nollångt tryck. Det var hela skillnaden mellan de två, och det såg
  ut som ett fel i mallen eftersom knappen i samma mall fungerade.
- **Så testas en switch på iOS utan finger:** ett tryck som håller kvar (~120 ms), eller ett drag
  över knoppen. Båda verifierade. Ett `tap` utan varaktighet ger falskt negativt.
- **Sample-sidan får stanna.** Den kostar nästan ingenting och är det som hindrar samma
  feldiagnos nästa gång. Slutsatsen står i `ToggleListSheetViewModel`, inte bara här — den som
  läser koden är den som behöver veta.

## Verifiering

**iPhone 17 Pro-simulator (iOS 26.2), Spines sample-app.** Utredningen i ordning:

| Steg | Utfall |
|---|---|
| `tap` på switch i mall, i sheet | ingen reaktion — felet reproducerat |
| `tap` på switch utanför mall, i sheet | ingen reaktion — **mallen var alltså inte inblandad** |
| `tap` på switch på en vanlig sida | ingen reaktion — **sheeten var inte heller inblandad** |
| pan- och pointer-gesterna i `NavigationRegion` bortkommenterade | ingen skillnad |
| drag över knoppen | **slår om** — kontrollen fungerar |
| tryck med 120 ms anhållstid, i mall, i sheet | **slår om**, "Rad 1 → True" |
| tryck med 120 ms anhållstid, utanför mall | **slår om**, "Direkt → True" |

De två första raderna motsäger ärendets egen avgränsning ("en switch bunden direkt mot
vymodellen i samma sheet fungerar"). Den observationen gjordes med samma trasiga mätmetod.

## Följd

`#59` var blockerad av det här ärendet: notisinställningarna i Orientera byggdes med Av/På-knappar
just för att switcharna troddes vara döda. Den blockeringen finns inte.
