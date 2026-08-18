# Issue #127 — Etapp A: grön bas, orange som signal — tokens, kontrast och bildbank

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/127
**Branch:** issue/127-green-tokens
**Status:** In Progress

## Plan

Etapp A ur [redesign-02-natur-och-energi.md](../samples/Orientera/docs/design/redesign-02-natur-och-energi.md) §4.
Ingen vy ritas om här. Det som byggs är underlaget: paletten, de nya tokens etapp B behöver,
och bildbanken. Två saker i utgångsläget styr hur arbetet måste läggas upp:

- **Alla accentanvändningar går redan genom tokens.** Tjugofyra träffar på `AccentAction`/`AccentSubtle`,
  varenda en `{DynamicResource}`, samlade i `Styles.xaml`, `Components.xaml` och sex sidor. Färgbytet är
  därför ett byte i två filer — inte en genomgång av appen.
- **Tabbstilen är undantaget, och det är redan uppmätt.** `design-system.md:61–65` dokumenterar varför
  `#E8590C` ligger som literal: `SpineTabBarStyle` appliceras vid handler-creation och läses inte om vid
  temabyte, så per-tema-tokens fungerar inte där. Den literalen ska byta värde, inte försvinna.

### 1. Paletten

Mätt med WCAG-formeln mot de fyra ytorna. Förslag:

| Token | Light | Dark | Not |
|---|---|---|---|
| `AccentAction` | `#1B5E3F` | `#58C99A` | 7.72 / 8.16 på kort, 7.20 / 9.08 på sidbakgrund |
| `AccentSubtle` | `#EAF2ED` | `#1E2E26` | accent på fyllningen: 6.77 / 6.94 |
| `TextOnAccent` | `#FFFFFF` | `#08150F` | 7.72 / 9.10 på accentfyllningen |
| `SignalUrgent` | `#C2410C` | `#FF7A33` | dagens `AccentAction`, oförändrade värden — 5.18 / 6.44 |
| `LinkInk` | `#1D4ED8` | `#7FB2FF` | 6.70 / 7.75 på kort |
| `BrandTint` | `#2E8B57` | `#2E8B57` | samma i båda teman, se §3 |
| `HeroScrim` | `#B3000000` | `#CC000000` | gradientens nedre stopp, dekor |
| `SkeletonBase` | `#ECECE8` | `#262A2E` | dekor, bär aldrig text |
| `AvatarBackground` | `#E6EDE9` | `#2A322D` | `TextPrimary` ovanpå: 14.22 / 11.87 |

Att `SignalUrgent` blir dagens orange *oförändrad* är poängen med hur bytet läggs upp: nyansen är redan
kontrastverifierad och redan låst, den byter bara namn och tappar rollen som varumärke.

### 2. Filerna

1. `Resources/Styles/LightTheme.xaml` + `DarkTheme.xaml` — nya värden på `AccentAction`, `AccentSubtle`,
   `TextOnAccent`; sex nya nycklar. Båda filerna har 28 nycklar i dag och ska ha 34 efteråt.
2. `AccentSubtle`-fyllningen är i dag varm (`#FDF1EA`/`#3A2A20`) och måste med i bytet — annars blir det
   grön text på orange fyllning i chip och badge.
3. `MauiProgram.cs:63` — `SelectedColor` läser `BrandTint` ur resursordboken i stället för literalen, och
   kommentaren ovanför skrivs om mot den nya mätningen.
4. `Orientera.csproj:42` och `:45` — `#E8590C` → `BrandTint`-hexen. De bakas vid build och kan
   inte läsa en token; de får en kommentar som pekar hit. `Resources/Svg/arena_control.svg`
   undantas: se **Decisions**.
5. Live-märket (`BadgeLive`, `Components.xaml:118`) byter från `AccentAction` till `SignalUrgent` — det är
   den första av de tre tillåtna användningarna.

### 3. `BrandTint` — varför en till nyckel

Tabbarens tint, appikonen och splashen behöver *en* färg som fungerar mot både den ljusa och den mörka
bakgrunden, eftersom ingen av de tre kan byta med temat. Uppmätt mot samma ytor som dagens orange mättes mot:

| Kandidat | Mot ljus bar `#FFFFFF` | Mot mörk bar `#1B1E21` |
|---|---|---|
| `#E8590C` (i dag) | 3.58 | 4.68 |
| **`#2E8B57`** | **4.25** | **3.94** |
| `#2F9E5F` | 3.40 | 4.92 |
| `#1B5E3F` (`AccentAction` light) | 7.72 | 2.17 ❌ |

`AccentAction` duger alltså inte som tabbtint — precis som per-tema-orangen inte dög. `#2E8B57` är den
kandidat som ligger jämnast över båda och klarar 3:1 med marginal åt båda hållen.

### 4. Dokumenten

- [designprinciper.md](../samples/Orientera/docs/design/designprinciper.md) §9: besluten 6–11 (D1–D6) skrivs in
  i samma tabellform som 1–5. P2 i §2 skrivs om enligt redesigndokumentets §2.
- [design-system.md](../samples/Orientera/docs/design/design-system.md): tokentabellen, kontrasttabellen och
  stycket om `#E8590C` uppdateras. Dokumentet är märkt **LÅST 2026-08-10**; det får en ny låsrad för
  redesignen i stället för att status tyst ändras.

### 5. Bildbanken (D2)

Ett tiotal terrängbilder under `Resources/Images/terrain/`, namngivna `<disciplin>-<terrängtyp>.jpg` så att
uppslaget blir en regel och inte en tabell. Licensfilen läggs bredvid dem, som `Resources/Fonts/Inter-OFL.txt`.
Själva uppslaget bor i `HeroImage` och byggs i etapp B — här läggs filerna och namnregeln.

### 6. Verifiering

`Features/Dev/DesignSystemPage` är redan appens specimen för tokens och får raderna för de nya. Svepet görs i
båda teman på simulator, som vid M0-låset.

## Open Questions

*Alla tre besvarade 2026-08-17 — se **Decisions**. Kvar står bara att bilderna behöver väljas av
en människa, se **Changes** punkt 7.*

**F1 — Var tar `SignalUrgent` sin tredje plats, och när?** → **(a)**
D1 ger orange tre användningar: live pågår, deadline inom ett dygn, tapp mot vinnaren. Live-märket finns i dag
och kan byta direkt. De andra två gör det inte:
- *Deadline inom ett dygn* finns inte som visuellt läge alls — närmast är `ClosesSoon` i
  `ProfilePage.ViewModel.cs:356`, som mäter **sju dygn**, och `HomePage.ViewModel.cs:391`, som mäter fjorton.
- *Tapp mot vinnaren* är i dag `NegativeDelta` på åtta ställen, i sidor som etapp C bygger om (resultat, hem, profil).

Två vägar:
- **(a)** Etapp A definierar tokenet och kopplar live-märket; deadline och tapp kopplas i den sida där de
  byggs om (etapp C steg 1 och 3), och räkningen "exakt tre" verifieras när etapp C är klar.
- **(b)** Etapp A byter alla åtta `NegativeDelta`-träffar nu.

Jag förordar **(a)**: (b) rör XAML som etapp C ändå skriver om, vilket är exakt det D6 finns för att undvika.
Följden är att avslutskriteriet "`SignalUrgent` på exakt tre ställen" flyttar från etapp A till etapp C — och
att `NegativeDelta` behöver ett eget svar där: vad betyder rött när tapp blivit orange? Bom? Struken?

**F2 — Avslutskriteriet "ingen färgliteral utanför temafilerna" kan inte gälla bokstavligt.**
Appikon och splash bakas vid build och kan inte läsa en `ResourceDictionary`. Förslag:
kriteriet blir "inga färgliteraler i runtime-XAML eller C#", och byggtidsassets bär `BrandTint`-hexen med en
kommentar som pekar på temafilen. Alternativet är ett byggsteg som genererar dem — mer maskineri än frågan väger.

**F3 — Godkänns paletten i §1 och `BrandTint` i §3?** Talen är räknade, men nyansvalet är ett smakbeslut.

## Changes

1. **Paletten bytt** i `LightTheme.xaml` och `DarkTheme.xaml`. `AccentAction` `#1B5E3F`/`#58C99A`,
   `AccentSubtle` `#EAF2ED`/`#1E2E26`, `TextOnAccent` dark `#08150F`. Nio nya nycklar:
   `SignalUrgent`, `TextOnSignal`, `LinkInk`, `HeroScrim`, `SkeletonBase`, `AvatarBackground`,
   `BrandTint`, `Primary`, `PrimaryDark`. Nyckelsetet gick från 28 till 37 i båda filerna.
2. **Live-märket** (`Components.xaml`) fyller med `SignalUrgent` i stället för `AccentAction` —
   den första av de tre tillåtna användningarna. Ny stil `BadgeOnSignalLabel` för texten ovanpå,
   och fem anropsställen bytte till den.
3. **Tabbtinten** läser `BrandTint` ur temaordboken (`MauiProgram.cs`) i stället för `#E8590C`.
4. **Byggtidsassets** bär `#2E8B57` med en kommentar som pekar på temafilen: `Orientera.csproj`
   (ikon + splash).
5. **Designsystemsidan** fick `SignalUrgent`, `LinkInk` och en sektion "Dekor och identitet" med
   `SkeletonBase`, `AvatarBackground`, `HeroScrim` och `BrandTint`.
6. **Dokumenten:** designprinciper §9 har beslut 6–11, princip 2 i §2 omskriven; design-system.md
   har ny palett, ny kontrasttabell, ett avsnitt om orange som signal och ett om färgen som inte
   kan byta med temat. Statusraden säger nu "paletten omlåst 2026-08-17".
7. **Bildbanken:** `Resources/Images/terrain/` med namnregel, uppslagsordning och licensmall
   (`README.md` + `terrain-licenses.txt`), och csproj-globben som tar in `*.jpg` därifrån.
   Alla elva filerna finns, men **provisoriskt**: stiliserade lager genererade av
   `generate-placeholders.py` i samma katalog, deterministiskt och utan tredjepartsrättigheter.
   De fyller uppslagets båda första steg så att `HeroImage` kan byggas och granskas i etapp B.
   D2 gäller fortfarande — de ska bytas mot kurerade fotografier, och licensfilen säger det.

**Verifierat:** `dotnet build` grön för maccatalyst och ios. Ingen färgliteral kvar i runtime-XAML
eller C# (grep över `Features`, `Controls`, `Services`, `App.xaml`, `MauiProgram.cs` ger noll
träffar). Kört på iPad Pro 13" (iOS 26) i båda teman: tabbtint, primär CTA, chip, avatarplats och
statusmärken gröna; live-märket orange; PM-märket i mörkt läge läsbart.

## Decisions

- **F1 → (a).** `SignalUrgent` definieras nu och kopplas till live-märket. Deadline inom ett dygn
  och tapp mot vinnaren kopplas i den sida där de byggs om (etapp C steg 1 och 3), eftersom
  alternativet vore att skriva om XAML som etapp C ändå skriver om — precis det D6 finns för.
  **Följd:** avslutskriteriet "`SignalUrgent` på exakt tre ställen" hör hemma i etapp C, inte här.
  I dag är antalet ett i produktionskod (`BadgeLive`) plus ett på designsystemsidan.
  **Öppet för etapp C:** vad `NegativeDelta` betyder när tapp mot vinnaren blivit orange.
- **F2 → kriteriet omformulerat.** "Ingen färgliteral utanför temafilerna" gäller runtime-XAML och
  C#. Appikon, splash och den inbäddade SVG:n bakas vid build och kan inte slå upp en
  `ResourceDictionary`; de upprepar `BrandTint`-hexen med en kommentar som pekar hem. Ett
  byggsteg som genererar dem vore mer maskineri än frågan väger.
- **`BrandTint` som egen nyckel.** Tre ytor kan inte byta med temat — tabbtinten (sätts vid
  handler-creation), ikonen och splashen. De behöver en nyans som klarar 3:1 mot både den ljusa
  och den mörka baren. `#2E8B57` ger 4.25 / 3.94; `AccentAction` ger 7.72 / 2.17 och duger inte.
  Samma värde deklareras i båda temafilerna, så `MauiProgram` kan läsa det ur vilken som helst.
- **`TextOnSignal` vid sidan av `TextOnAccent`.** Accentens mörka bläck blev grönt (`#08150F`) när
  accenten blev grön, men orangen är varm och hade redan ett eget verifierat bläck (`#1A1208`).
  Att dela på dem behåller båda mätningarna i stället för att kompromissa bort en av dem.
- **`Primary` / `PrimaryDark` deklareras fast de är dubbletter.** Spines `PageActionView` läser
  dem ur `Application.Resources` och faller annars tillbaka på MAUI-mallens lila — det är därför
  "Tid" och "Filter" stod i systemfärg i testkörningen. Båda nycklarna finns i båda temafilerna,
  så resultatet blir rätt oavsett vilken ordbok som är monterad och oberoende av i vilken ordning
  temabytets händelsehanterare råkar köra. Två fynd ur testkörningen lagade på köpet.
- **Kontrollsymbolen står utanför paletten.** `arena_control.svg` byttes först till `BrandTint`
  tillsammans med ikonen och splashen, och det var fel: kontrollen är orienteringens tecken, inte
  appens. Vit uppe till vänster och orange nedanför diagonalen ser likadan ut på varje karta i
  världen och läses utan att förklaras — en kontroll i varumärkets färg är inte längre en kontroll.
  Den behåller `#E8590C`, inte som rest av den gamla accenten utan som sportens färg, och ska inte
  städas in i något token. (Rättat i etapp C steg 1, se #131.)
- **PM-märket i mörkt läge behövde ingen egen justering.** Det var `AccentAction` på
  `AccentSubtle`, 5.28 med den orange paletten. Med den gröna blir samma par 6.94.
