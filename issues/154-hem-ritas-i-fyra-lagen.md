# Issue #154 — Hem ritas i fyra lägen

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/154
**Branch:** issue/154-hem-ritas-i-fyra-lagen
**Status:** In Progress

## Plan

Etapp 5 i [redesign-04-hem.md](../samples/Orientera/docs/design/redesign-04-hem.md), och den
sista. Grenad ur [#153](153-hem-ritas-om-hjalte-live-yta-och-sektionsrubriker.md).

Hem hade två av P10:s fyra lägen, och det ena var just det principen förbjuder: en
`ActivityIndicator` ovanpå innehållet. `StateView` finns sedan riktning 02 och gör redan hela
jobbet — det som saknades var att sidan använde den.

## Changes

### `Features/Home/HomePage.ViewModel.cs` ✅

- `State` — ett värde härlett ur `IsLoading`, `IsOffline` och `HasContent`, i den ordningen.
  Ingenting är tomt medan svaret är okänt, och ingenting är offline medan en hämtning pågår.
- `ReloadCommand` — knappen i offline-läget.
- Aliaset `using ViewState = Orientera.Controls.ViewState;`, eftersom MAUI har ett eget.

### `Features/Home/HomePage.View.xaml` ✅

`StateView` bär sidans nedre hälft, med fyra innehåll:

| Läge | Vad som ritas |
|---|---|
| Laddar | Skelett i blockens form: ett högt kort, sedan två med var sin rubrikstapel ovanför |
| Har data | `CollectionView` med blocken |
| Tomt | En mening om varför, och en knapp till kalendern |
| Offline | "Ingen anslutning", vad som ändå fungerar, och "Försök igen" |

`ActivityIndicator`-en är borta, och den fristående offline-stapeln vid sidan av listan med den.

### Verifiering ✅

- Build grön, testsviten grön (536).
- Kört på iPhone 17-simulator i ljust och mörkt läge: **Har data** mot demodatat, **Offline** mot
  en backend som inte svarar, och **Laddar** mot en oroutbar adress så att skelettet står kvar
  länge nog att granskas.

### Efterjustering — hjälten växer och kortet lägger sig över ✅

Bilden ska gå ned till knappt halva skärmen och första kortet ska överlappa den.

- **`Features/Home/HomeHero.cs`** — hjälten bruten ur sidans XAML till en egen vy, eftersom den
  numera behövs på fyra ställen: som listans huvud i innehållsläget, och överst i vart och ett av
  de tre andra lägena.
- **`HomePage.ViewModel.cs`** — `HeroHeight` (46 % av skärmen, ur `DeviceDisplay`) och
  `HeroOverlap` (negativ underkant på huvudet).
- **`HomePage.View.xaml`** — hjälten in i `CollectionView.Header`; listan blev helbleed och de sex
  mallarna bär sin egen sidmarginal i stället.
- **`HomePage.cs`** — flikattributets `SafeAreaEdges` tillbaka till standard, se nedan.

## Decisions

- **Offline är sidans fel-läge, inte ett femte.** P10:s fel-läge kräver vad som gick fel, vad som
  ändå fungerar, och en väg att försöka igen — vilket är exakt vad offline-texten redan sa, minus
  knappen. Ett femte läge hade varit samma tre delar med ett annat namn.

- **Tomt läge fick en knapp och inte bara en mening.** `StateView.EmptyHint` är text; P10 kräver
  en väg vidare. Slotten `EmptyView` finns för precis det, så komponenten behövde inte ändras.

- **Kortavståndet i listan gick från 12 till 16.** Sektionsrubrikerna sitter numera ovanför korten
  och inte inuti dem, och med tolv punkter mellan blocken låg föregående korts underkant lika nära
  rubriken som rubriken låg sitt eget kort.

- **Hjälten flyttade in i listans huvud, och skrollar därmed bort med korten.** Det är överlappet
  som kräver det. En hjälte som står still tvingar korten att antingen klippas mot dess underkant
  på väg upp — mitt på ett fotografi, vilket läser som trasigt snarare än som djup — eller att
  täcka hälsningen. Fällan som stod dokumenterad i koden (en header som mätts som tom växer inte)
  undviks av att höjden är satt och känd innan något bundits.

- **Bilden går under statusfältet, och överlappet är halva hjälten.** Att hjälten skrollar betyder
  att korten passerar under statusfältet, där klockan hamnar ovanpå ett kort — det är hur en
  helbleed-sida beter sig på iOS, och det är valt framför att låta bilden börja under fältet.
  Alternativet, en permanent mörk remsa bakom statusfältet (Apples scroll edge), kostar ett mörkt
  band över vita kort i ljust läge och är inte byggt.

- **Överlappet är en andel och inte ett punktmått**, av samma skäl som höjden: halva hjälten är
  halva hjälten på varje skärm. Bilden ritas i hela sin höjd — det är kortet som lägger sig över
  dess nedre hälft, inte bilden som krymper.

- **Höjden är 46 % av skärmen och inte ett punktmått.** "Knappt halva skärmen" är ett förhållande:
  fyrahundra punkter är nästan hela en iPhone SE och en tredjedel av en iPad.

- **Skelettet ritar två block, inte fyra.** Hem visar högst fyra, men med hjälten på halva skärmen
  ligger redan det tredje under kanten — ett skelett för något som ändå inte syns är en rad kod som
  aldrig läses.
