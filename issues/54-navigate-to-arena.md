# Issue #54 — Tävlingsdag: "Navigera" öppnar klassväljaren

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/54
**Branch:** issue/54-navigate-to-arena
**Status:** Completed

## Plan

Kontextmotorn sätter `ContextState.RaceDay → ContextAction.Navigate` med knapptexten "Navigera",
men switchen i `EventDetailsPage.ViewModel.PrimaryAction` saknade ett fall för `Navigate` och föll
ner i `default:`, som öppnar klassväljaren. Knappen sa alltså en sak och gjorde en annan.

Navigera ska ta löparen till arenan: telefonens kartapp, med arenans koordinat och tävlingens
plats som etikett.

## Changes

- `EventDetailsPage.ViewModel` — eget fall för `ContextAction.Navigate` som anropar
  `NavigateToArena`, och `default:` gör numera ingenting i stället för att öppna klassväljaren.
- `NavigateToArena` lämnar över arenan till `Map.OpenAsync` med `NavigationMode.Driving` och
  tävlingens plats som namn. En tävling utan koordinat leder ingenstans och gör ingenting.

## Decisions

- **`default:` gör ingenting.** Det var den egentliga buggen: ett standardfall som gör något helt
  annat än vad knappen säger väntar bara på nästa tillstånd som saknar sitt eget fall. Varje
  `ContextAction` som kan bli en knapptext har nu ett eget fall, och den som lägger till ett
  tillstånd får en knapp som inte gör något — vilket är lättare att upptäcka än en knapp som gör
  fel sak.
- **Kartappen, inte en egen navigering.** Orientera ska inte bygga svängbeskrivningar. Frågan
  "hur tar jag mig dit" har telefonens karta redan svaret på, och `Launcher`-mönstret för externa
  destinationer finns redan på sidan (PM-länkarna).
- **Koordinat 0,0 räknas som ingen koordinat.** Eventor lämnar den tom för en del tävlingar, och
  ekvatorn söder om Ghana är ingen arena.

## Verifiering

`dotnet test`: 214 gröna (ändringen ligger i en vymodell utanför testprojektet).

**iPhone 17 Pro-simulator (iOS 26.2):** tidsmaskinen till 15 augusti 08:50, före första start —
Norrlandsmästerskapen Lång står då i **TÄVLINGSDAG** med knappen **Navigera**. Ett tryck lämnar
appen och öppnar Apple Maps; statusraden visar "◀ Orientera". Före ändringen öppnades **Välj
klass**.

Maps landar i "Choose Start" i stället för i en färdig rutt, eftersom platstjänster är avslagna i
simulatorn och en körrutt behöver en startpunkt. Det är simulatorns tillstånd, inte appens —
destinationen är överlämnad korrekt.
