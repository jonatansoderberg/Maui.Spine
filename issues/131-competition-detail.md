# Issue #131 — Etapp C steg 1: tävlingsdetaljsidan

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/131
**Branch:** issue/131-competition-detail
**Status:** In Progress

## Plan

Etapp C steg 1 ur [redesign-02-natur-och-energi.md](../samples/Orientera/docs/design/redesign-02-natur-och-energi.md) §4.
Grenen utgår från `issue/129-six-components`; kedjan är #128 → #130 → den här.

Sidan får konceptets form och lagar sina egna sju fynd i samma vända (D6).

## Changes

### Konceptets form

- **`HeroImage` överst**, edge-to-edge, med `HeroScrim` i underkanten. Kartan flyttar ner i en egen
  sektion, "Arenan", där den är information i stället för dekor.
- **`ListRow` i startfältet**: ordningstalet i identitetens plats, poängen i värdets och
  riksplaceringen på raden under. `IsHighlighted` markerar läsarens egen rad.
- **`HandoffCard`** för Livelox och för varje dokument — de ersätter två nästan identiska
  `Border`-block med samma pil-i-hörnet.
- **Mellanlandning före anmälan**: ny `EntryHandoffSheet` som säger vad som händer och vilken klass
  som följer med, innan Eventors formulär öppnas i appens webbvy.
- **Skelett i sidans egen form** i stället för snurran: hero, rubrikrad, metarad och två kort.
  `IsLoaded` på `OrienteraViewModel` gör att innehåll och skelett aldrig kan visas samtidigt.

### Fynden

1. **Kartan ritar klart.** Mapsui hämtar brickor för den storlek kartan hade när `Map` sattes, och
   en vy i en `ScrollView` mäts innan den får sin höjd — så kartan bad om brickor för en höjd den
   ännu inte fått, och nedre halvan förblev vit så länge sidan var öppen. `OnSizeAllocated`
   centrerar om när den verkliga höjden kommer.
2. **"första start 00:00" borta.** `Competition.HasFirstStart` säger om starttiden är satt; ett
   datum utan tid kommer som midnatt. Sidan skriver "starttid ej satt" i stället.
3. **"Visa tävling" borta från tävlingens egen sida.** `ContextAction.ShowCompetition` ger ingen
   primär knapp — en handling som leder till sidan man står på är ingen handling.
4. **"torsdag 20:e aug" → "torsdag 20 aug".** Ordinalformen togs bort ur `Format.Deadline`.
5. **Klassvalet** delas i Åldersklasser och Banor, den valda ligger först i sin grupp med bock, och
   löparens egen klass närmast efter.
6. **Fältrubrikens tomma ranking borta.** "utan ranking" på varje rad var en kolumn som mest sa att
   den var tom; riksplaceringen visas när den finns och före lottningen inte alls.
7. **Villkoret står under den släckta knappen** — "finns när tävlingen startat", "finns efter
   målgång".

### Dessutom

- **"Anmälan öppnar …"** när anmälan har ett stängningsdatum men inte öppnat än. Sidan visade bara
  stängningen bredvid ett tillstånd som sa "Upptäckt", och läsaren fick gissa vilken halva som
  gällde. Det är den verkliga orsaken bakom fynd 3, inte bara knappens text.
- `ListRow.IsHighlighted` och stilen `BodyAccentLabel` — läsarens egen rad i en lista med andra.

**Verifierat:** build grön för maccatalyst och ios. Kört på iPhone 17 Pro (iOS 26) i båda teman:
hero, "starttid ej satt", deadline utan ordinal, ingen "Visa tävling"-knapp, kartan hel, villkoren
under de släckta knapparna, klassvalet med bock, mellanlandningen och vidare in i Eventors formulär.

## Decisions

- **Hero har ingen karta som fallback på den här sidan.** P7 säger att hero degraderar till
  kartrutan, men kartan står redan i sin egen sektion längre ned. Två `ArenaMap` på samma sida hade
  hämtat brickor två gånger, och den bakom bilden syns aldrig. `HeroImage` döljer numera sin
  fallback när en bild finns, och kollapsar helt när varken bild eller fallback finns.
- **En `_default`-bild per disciplin.** Terrängtypen finns inte i domänen — den skulle komma ur
  PM-extraktionen (M3, SP-10, inte byggd). Utan `<disciplin>_default` hade hero fallit till
  ingenting för de flesta tävlingar, och konceptets viktigaste grepp aldrig synts.
- **Midnatt betyder "ingen tid satt".** Det är encodingen källan redan använder, och alternativet —
  att göra `FirstStart` nullbar — hade rört `Date`, sorteringen och varje kallare i appen för att
  uttrycka samma sak.
- **Mellanlandningen returnerar ett svar i stället för att öppna webbvyn själv.** Ark ovanpå ark är
  en form att undvika; detaljsidan tar emot ja/nej och öppnar `EventorEntrySheet` som förut.

## Öppna fynd som hör till nästa steg

- `EventorEntrySheet` säger "Du är redan inloggad här" oavsett om sessionen finns. Utan inloggning
  möts man av Eventors "Du behöver vara inloggad för att anmäla dig" under en rubrik som påstår
  motsatsen. Hör till etapp C steg 2 tillsammans med annonsväggen och klassen i URL:en.
- Panelens kryss ligger ovanpå förklaringstexten i klassvalet — Spines panelmall, etapp E.
