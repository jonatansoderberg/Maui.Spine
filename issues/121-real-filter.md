# Issue #121 — Filterarket blir ett riktigt filter

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/121
**Branch:** issue/121-real-filter
**Status:** Completed

Stänger #92 (flera distrikt) och #94 (fritextsök och tidsintervall). De hörde ihop: båda handlade
om att filtret var en samling presets i stället för ett filter.

## Changes

- `Features/Events/EventFilter.cs` — `Districts`, `Query`, `Period`, samt `Window(today)` och
  `Matches(competition)` som räknar ut vad de betyder.
- `Features/Events/EventsPage` — sökfältet på sidan, distrikts- och periodvillkoren i
  `PassesAdvanced`, och filterknappen som säger hur många val som är satta.
- `Features/Events/EventFilterSheet` — distrikt som chip, tidsintervall som picker, och arket
  öppnar med det som faktiskt är valt.
- `Services/Local/DistrictStore.cs` — distriktsvalet på telefonen.
- `EventFilterTests` — fyra tester.

## Decisions

- **Sökrutan står på sidan, inte i arket.** Man skriver och ser resultatet direkt. I ett ark måste
  man stänga för att se om sökningen träffade, och öppna igen för att ändra den.
- **Arket öppnar med det som är valt.** Det gjorde det inte förut: arket visade tomt över ett
  aktivt filter, och att trycka "Visa tävlingar" rensade tyst allt användaren hade valt. Det var en
  bugg som fanns innan de nya fälten och som blir värre av dem.
- **Tidsintervall som grova val, inte två datumväljare.** "Denna månad", "Nästa månad", "Inom tre
  månader", "Resten av året". Det är hur man tänker om en tävlingskalender, det matchar arkets
  övriga väljare, och två datumväljare i ett bottenark är en dålig plats att pilla med datum på.
- **Distrikten kommer ur kalendern i handen**, inte ur en lista över Sveriges alla distrikt. Man
  erbjuds de distrikt det finns något att se i.
- **Bara distrikten sparas.** Var man vill titta är en stående preferens; vad man sökte förra
  veckan är det inte. En kvarglömd sökning som döljer hela kalendern vid nästa start är en bugg
  användaren inte kan se orsaken till.
- **Sökningen läser namn, arrangör, plats och distrikt** — det en människa skriver — och aldrig
  id:n.

## Verifiering

`dotnet test`: **289 gröna** (285 + 4 nya).

**I simulatorn:**

- Sökningen: "hemlingby" smalnar listan till Natt-KM i Hemlingby.
- Distriktet: Dalarna valt lämnar Höstträffen (Falu OK) kvar och tar bort resten.
- Filterknappen visar **"Filter (1)"**.
- Distriktsvalet överlevde en omstart av appen, vilket är hela poängen med att spara det.

### Tre saker körningen avslöjade

1. **`SearchBar` går inte att färga på iOS.** Den ritades som en svart platta över appens yta och
   viker sig inte för `BackgroundColor` — bakgrunden kommer från `UISearchBar` själv. Utbytt mot en
   `Entry` med appens egen stil och `ClearButtonVisibility`, som ger samma sak utan den svarta
   plattan.
2. **`FlexLayout` med `Wrap` ritade chippen men tog inte emot tryck.** Barnen hamnade utanför den
   yta layouten mätte upp inuti den lodräta `ScrollView`:en, och träffytan följer mätningen och inte
   det som syns. Ett chip som ser tryckbart ut och inte är det ger ingen ledtråd alls. Utbytt mot
   samma vågräta `ScrollView` med `HorizontalStackLayout` som chippraden på Tävlingar använder.
3. **`FilterLabel` var bunden till ingenting.** Rubrikknappen skapades en gång med den fasta texten
   "Filter", så räknaren har aldrig synts. Det gick att leva med när filtret var små justeringar;
   med ett distrikt eller en period kan filtret dölja nästan hela kalendern, och då läses en kort
   lista som en trasig kalender i stället för en filtrerad. Knappen byggs nu om när filtret ändras.

## Kvar

- **Sökning utanför kalenderfönstret.** Allt här är lokalt över det fönster appen redan har.
  Vill man söka längre bort krävs att BFF:en exponerar `fromDate`/`toDate` —
  `EventorSource.GetCompetitionsAsync` tar redan emot dem.
- **Snabbfiltret "Gästrikland"** är kvar och kommer fortfarande från identiteten. Det är en genväg
  till ett distrikt, och den fungerar; flervalet i arket är för dem som vill mer.
