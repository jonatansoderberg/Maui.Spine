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

## Decisions

- **Offline är sidans fel-läge, inte ett femte.** P10:s fel-läge kräver vad som gick fel, vad som
  ändå fungerar, och en väg att försöka igen — vilket är exakt vad offline-texten redan sa, minus
  knappen. Ett femte läge hade varit samma tre delar med ett annat namn.

- **Tomt läge fick en knapp och inte bara en mening.** `StateView.EmptyHint` är text; P10 kräver
  en väg vidare. Slotten `EmptyView` finns för precis det, så komponenten behövde inte ändras.

- **Kortavståndet i listan gick från 12 till 16.** Sektionsrubrikerna sitter numera ovanför korten
  och inte inuti dem, och med tolv punkter mellan blocken låg föregående korts underkant lika nära
  rubriken som rubriken låg sitt eget kort.

- **Skelettet ritar tre block, inte fyra.** Hem visar högst fyra, men det fjärde ligger under
  skärmkanten på en 812-punkters skärm — ett skelett för något som ändå inte syns är en rad kod
  som aldrig läses.
