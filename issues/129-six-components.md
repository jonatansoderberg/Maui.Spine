# Issue #129 — Etapp B: sex komponenter som gör åtta sidor till en app

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/129
**Branch:** issue/129-six-components
**Status:** In Progress

## Plan

Etapp B ur [redesign-02-natur-och-energi.md](../samples/Orientera/docs/design/redesign-02-natur-och-energi.md) §4.
Grenen utgår från `issue/127-green-tokens`, inte `master`, eftersom komponenterna konsumerar tokens
som ännu inte är mergade (#127 / PR #128). Rebasas på `master` när den mergats.

**Ingen sida byggs om här.** Etapp B slutar när de sex komponenterna finns och står på
designsystemsidan i båda teman. Sidorna byts en i taget i etapp C, där varje sida också lagar
sina egna fynd (D6).

### Formen: C#-kontroller, som `ChipView`

`Controls/ChipView.cs` är förlagan och dess kommentar säger varför: en `DataTrigger` minns värdet
den ersatte *en gång*, så efter ett temabyte återställer den det gamla temats färg. Alla sex
komponenterna byggs därför i C# med `SetDynamicResource`, och växlar mellan färdigstylade element
i stället för att flippa egenskaper.

### 1. `IdentityView` (P8, D3)

Ersätter fyra kopior av samma `Border`+`Image` (`EventsPage:105`, `EventDetailsPage:68`,
`LivePage:172`, `ResultsDetailPage:140`) och initialcirkeln som bara finns på Profil
(`ProfilePage.View.xaml:19`).

```
Source     ImageSource?   bild eller klubbmärke
Fallback   string         initialer när bilden saknas
Size       double         14–56 pt; anropen i dag använder 14, 15, 18 och 56
Shape      Circle | Rounded   person respektive klubb
```

Ordningen är källa → initialer → `AvatarBackground` tom platta. Aldrig en tom cirkel i en lista
där andra rader har bild.

**Komponenten känner inte sin källa.** Den tar en `ImageSource`, inte ett person-id och inte en
butik. `LocalIdentityStore`/`LocalGroupStore` matar den via vy-modellen precis som i dag, och M5
kan byta till en server utan att någon vy ändras (D3, risk N2).

### 2. `ListRow` (P9)

`[identitet] [primär text / sekundär text] [värde] [→]`. Kolumnbredderna får skilja mellan vyer,
ordningen aldrig. Byggs som en `Grid` med fyra fasta platser där tomma platser kollapsar.

Skärmläsarregeln ur `design-system.md` gäller: raden är **ett** element med en
`SemanticProperties.Description`, och eventuella knappar ligger utanför den — en Description på en
layout gör barnen onåbara på iOS.

### 3. `SegmentBar` (B)

Omsluter `ChipView`, ersätter den inte. I dag ligger lösa chip-rader i `ResultsDetailPage:12–16`,
`LivePage:38–43` och `EventsPage:31`. `SegmentBar` tar en `ItemsSource`, en vald post och ett
kommando, och äger rullningen när posterna inte får plats.

### 4. `HeroImage` (P7)

Slår upp `terrain_<disciplin>_<terräng>` → `terrain_<disciplin>_default` → fallback, enligt
regeln i `Resources/Images/terrain/README.md`. Bilderna finns (provisoriskt) sedan #127.

Alltid en `HeroScrim`-gradient i underkanten, så att märken ovanpå klarar kontrastkravet.

**Fallbacken sätts utifrån.** `ArenaMap` bor i `Features/Events/`, och en komponent i `Controls/`
ska inte bero på en feature-katalog. `HeroImage` exponerar därför `Fallback` som innehåll, och
detaljsidan skickar in kartrutan. Då är det fortfarande kartan som är sann geografi (D2), utan
att beroendet vänds fel väg.

### 5. `StateView` (P10)

Ersätter åtta `ActivityIndicator` och tre olikformade tomma lägen
(`ResultsPage:10`, `LivePage:49`, `EventsPage:45`).

**Läget är ett värde, inte tre bindningar.** En `ViewState`-enum — `Loading`, `Content`, `Empty`,
`Error` — gör det omöjligt att visa tomt läge medan en hämtning pågår, vilket är exakt det
testkörningen fann på resultatsidan. Det är den halva av P10 som inte går att uppnå med tre
`IsVisible`.

Fyra innehållsegenskaper: `Skeleton`, `Content`, `EmptyView`, `ErrorView`. Felläget bär vad som
gick fel, vad som ändå fungerar, och ett kommando som försöker igen.

### 6. `HandoffCard` (P11)

Ersätter `Launcher.OpenAsync` i `EventDetailsPage.ViewModel.cs:345`. Säger vart man går, vad som
följer med, och bär extern-länkikonen.

**Rör inte `EventorEntrySheet`.** Den öppnar redan appens egen webbvy i stället för Safari, och
kommentaren i filen säger varför: Safari har egen kakburk och extern öppning loggar ut användaren.
Anmälans mellanlandning är etapp C steg 2 och använder `HandoffCard` som *skärm*, men med ett
löfte som stannar i appen (D5).

### Designsystemsidan

Varje komponent får ett exempel, granskat i båda teman — `StateView` med alla fyra lägen.
Det är sidan som redan är appens enda ställe där tokens granskas i verkligt ljus, och blir nu
samma sak för komponenter.

## Open Questions

*Båda besvarade 2026-08-17: F1 → (a), F2 → `ListRow` gäller rader, inte kort. Se **Decisions**.*

**F1 — Ryms livetabellen i `ListRow`s anatomi?**
`LivePage` visar plats, namn, klubb, tid och differens — fler värden än P9:s fyra platser.
Tre vägar:
- **(a)** Värdekolumnen får bära ett *värdeblock* (tid över, differens under) — anatomin hålls,
  och samma grepp fungerar för resultatlistan.
- **(b)** Livetabellen är ett eget mönster som skrivs ned som ett undantag från P9.
- **(c)** Livetabellen behåller sin nuvarande Grid tills etapp C steg 4.

Jag förordar **(a)**: den håller regeln som gör åtta listor till en app, och tabellens andra värde
är i praktiken en kvalificering av det första. Blir det trångt på små skärmar visar (a) det direkt
på designsystemsidan, innan någon sida byggts om.

**F2 — Ska `ListRow` ersätta korten på Hem och Tävlingar, eller bara raderna?**
Konceptets P9 talar om listrader. Hem och Tävlingar visar kort med märken, knapp och flera rader
text — de är inte rader och tvingas de in i anatomin blir det en sjunde variant med undantag.
Mitt förslag: `ListRow` gäller resultat, live, följning, notiser och klubbaktiviteter; korten
förblir kort och får sin enhetlighet ur `IdentityView` och märkesstilarna i stället.

## Changes

Sex komponenter i `Controls/`, alla i C# med `SetDynamicResource` efter `ChipView`s mönster:

1. **`IdentityView`** — bild → initialer → kollapsad plats. `Circle` för person, `Rounded` för klubb.
2. **`ListRow`** — fyra platser med en tvåradig värdekolumn (F1a). Raden är ett element för
   skärmläsaren; barnen tas ur trädet och beskrivningen sätts ihop av radens egen text när ingen
   anges.
3. **`SegmentBar`** — omsluter `ChipView`, med `Segment`-posten som bär text, nyckel och om den är
   valbar. Rullar horisontellt utan rullningslist.
4. **`HeroImage`** — uppslag disciplin+terräng → disciplin+default → `Fallback`, med
   `HeroScrim`-gradient i underkanten.
5. **`StateView`** — `ViewState`-enum och fyra innehållsplatser. Tomt och fel ritas av komponenten
   när inget skickas in, så en sida får de fyra lägena för en egenskap i stället för fyra layouter.
6. **`HandoffCard`** — säger vart man går och vad som följer med. `StaysInApp` avgör märket.

Dessutom:

- **`Presentation/RowGlyph.cs`** — pilen vidare och rutan med pilen ut, ritade som `Path` av samma
  skäl som `DisciplineGlyph`: en rasteriserad ikon bär det tema den bakades i.
- **Designsystemsidan** har ett exempel på var och en, `StateView` med alla fyra lägena samtidigt.
  `DesignSystemPageViewModel` fick en segmentlista att mata bar-exemplet med.
- **`TextMuted` lagad.** Nyckeln refererades på två ställen i `Components.xaml` men har aldrig
  funnits i något tema: disciplinmärket utan träff i triggarna och sidladdaren ritades i MAUI:s
  standardfärg. Båda pekar nu på `TextSecondary`.

**Verifierat:** `dotnet build` grön för maccatalyst och ios. Kört på iPhone 17 Pro (iOS 26) i båda
teman — alla sex komponenterna granskade på designsystemsidan. Ingen sida är ändrad.

## Decisions

- **F1 → (a): värdekolumnen bär två rader.** `Value` över `ValueDetail`. Livetabellen och
  resultatlistan visar båda en tid och något som kvalificerar den, och att ge det paret en egen
  plats är vad som låter dem behålla anatomin i stället för att få en femte kolumn.
- **F2 → `ListRow` gäller rader, inte kort.** Korten på Hem och Tävlingar får sin enhetlighet ur
  `IdentityView` och märkesstilarna. Att tvinga in dem hade gett en sjunde variant med undantag.
- **Tom identitet kollapsar.** P8 förbjuder den tomma cirkeln uttryckligen — en platta utan
  innehåll läser som data som inte kom fram. Utan både bild och initialer döljs platsen och raden
  sluter sig över den.
- **`IdentityView` lånar inte `ClubBadge`-stilen.** Först gjorde den det, och två fel följde direkt:
  ett lokalt satt `StrokeShape` och en lokalt satt `BackgroundColor` rangerar över en stils setters
  och lämnas aldrig tillbaka, så en vy som börjat som cirkel behöll den runda bakgrunden och tappade
  hårlinjen när den blev klubbmärke. Utseendet berodde alltså på i vilken ordning egenskaperna
  sattes. Båda formerna anges nu i sin helhet. `ClubBadge`-stilen står kvar tills etapp C flyttat
  de fyra sista anropen.
- **`HeroImage` äger inte sin fallback.** Kartrutan (`ArenaMap`) bor i `Features/Events/`, och en
  komponent i `Controls/` som sträckte sig dit hade vänt beroendet fel väg. Sidan skickar in den.
- **De bundlade bildnamnen står i koden.** MAUI plattar ut bildresurser och erbjuder inget sätt att
  fråga om en finns; ett namn utan fil ritar ingenting alls, vilket är sämre än att falla tillbaka.
  Listan och katalogens README beskriver samma mängd.
- **`StateView.Body`, inte `Content`.** `ContentView.Content` är upptagen av komponentens egen rot.
