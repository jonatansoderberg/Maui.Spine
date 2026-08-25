# Issue #152 — Hem: komponenterna bakom den nya kortanatomin

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/152
**Branch:** issue/152-hem-komponenterna-bakom-den-nya-kortanatomin
**Status:** In Progress

## Plan

Etapp 2 i [redesign-04-hem.md](../samples/Orientera/docs/design/redesign-04-hem.md). Grenad ur
[#151](151-hem-tokens-och-typografi-for-den-nya-kortanatomin.md), som ännu inte ligger i master —
komponenterna läser dess tokens och textstilar och kan inte byggas före dem.

Fyra komponenter, byggda i `Controls/` och granskade på designsystemsidan innan Hem rörs. Ingen
sida ändras här; det är etapp 3.

## Changes

### `Controls/SectionHeader.cs` ✅

Rubrik i `Heading2Label` plus en valfri handlingslänk (`LinkActionLabel` + chevron). Tappet sitter
på länken, inte på raden: en beskrivning på en layout gör dess barn onåbara på iOS, och rubriken
ska förbli en rubrik skärmläsaren kan hoppa mellan. Utan `ActionText` ritas bara rubriken.

### `Controls/AvatarStack.cs` ✅

Överlappande `IdentityView` plus `+N` för resten av fältet. `Face(ImageSource?, string)` som
publik post — komponenten känner inte sin källa, av samma skäl som `IdentityView` tar en
`ImageSource` och inte ett person-id (D3).

`RingColor` är ytan bakom stacken, inte ett eget token: glappet mellan cirklarna *är* kortet som
lyser igenom, och det byter med ytan komponenten står på.

### `Controls/StatRow.cs` ✅

Två eller tre `Stat(Caption, Value, Unit)` med hårfina avdelare emellan. Värdet i H1-storlek och
inte Display: tre tal i Display bredvid varandra blir tre rubriker som konkurrerar, och raden ska
läsas som ett resultat. Enheten får en egen rad under värdet — "5:21" utan "min/km" är en tid.

### `Presentation/CourseGlyph.cs` + `Controls/CourseMark.cs` ✅

Banan i kortformat: starttriangel, sträcka och kontrollring. Ritad och inte bundlad, som de andra
märkena — en rasterbild bär den temafärg den bakades med, och den här ligger bakom text i båda.
Strecktjockleken är en andel av storleken; en hårlinje som är en sträcka vid 150 punkter är en
repa vid 60.

### `Features/Dev/DesignSystemPage` ✅

Specimen för alla fyra, i vy och vy-modell. `AvatarStack` visas två gånger — på live-kortets gröna
yta med överskott, och på kortytan utan — eftersom ringen är det enda som skiljer dem.

### Verifiering ✅

- Build grön för Mac Catalyst och iOS-simulator; de 13 varningarna är alla sedan tidigare.
- Testsviten grön (515).
- Kört på iPhone 17-simulator i båda temana. Två fynd rättade på plats, se nedan.

## Decisions

- **Överlappet är en femtedel av cirkeln, inte en tredjedel.** Först uppmätt på skärm klippte
  grannen framför av andra bokstaven i initialerna — och initialer är normalfallet så länge
  följning är lokal och ingen annans bild finns på telefonen. Överlappet måste rymma den
  identitet som är två tecken bred.

- **Starttriangeln pekar uppåt.** Först ritad med spetsen nedåt, vilket på skärm lästes som en
  pilspets tappad på kortet snarare än som en start. På en karta pekar triangeln dit löparen ska,
  och det är också det enda som binder ihop den med sträckan som går därifrån.

- **`CourseMark` fick egen geometri i stället för att skala upp `DisciplineGlyph`.** Det lilla
  märket är proportionerat för att läsas vid sexton punkter; vid tio gånger storleken blir dess
  streck ett rep och kontrollen ett cykelhjul. Samma vokabulär — sträcka in i en kontrollring —
  men ritat för en yta.

- **`Face` och `Stat` bor hos sina komponenter**, inte i `Presentation/`. De är komponenternas
  indata och beskriver ingen domän: en `Stat` vet inte om den är en placering eller en poäng.
