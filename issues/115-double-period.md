# Issue #115 — Dubbel punkt i varningen om resultat som faller ur

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/115
**Branch:** issue/115-double-period
**Status:** Completed

## Plan

Sverigelistan-kortet skrev *"Ett räknande resultat faller ur 19 sep.."* — meningen satte punkt och
månadsförkortningen bar redan en egen.

Den uppenbara fixen, att stryka meningens punkt, är fel. Uppmätt i `sv-SE` på .NET 10:

```
19 jan. | 19 feb. | 19 mars | 19 apr. | 19 maj  | 19 juni
19 juli | 19 aug. | 19 sep. | 19 okt. | 19 nov. | 19 dec.
```

**Åtta av tolv** månader förkortas med punkt; mars, maj, juni och juli gör det inte. Utan meningens
punkt hade raden slutat oavslutat en tredjedel av året i stället för att sluta dubbelt två
tredjedelar. Datumet måste alltså lämna ifrån sig förkortningens punkt och låta meningen sätta sin.

## Changes

- `Presentation/Format.cs` — `DateInSentence(DateOnly)`: `d MMM` utan avslutande punkt.
- `Features/Profile/ProfilePage.ViewModel.cs` — varningens singularfall använder den.
- `FormatTests` — ett test med både en månad som bär punkt och en som inte gör det, och hela
  meningen som den renderas.

## Decisions

- **Hjälparen ligger i `Format`, inte i vymodellen.** Det här är en egenskap hos svenska datum, inte
  hos Sverigelistan-kortet, och nästa mening som slutar med ett datum ska inte behöva upptäcka det
  igen.
- **`ExpiryText` i radmallen lämnas orörd.** Den skriver `faller ur 19 sep.` utan egen punkt — där
  hör punkten till förkortningen och raden är en etikett, ingen mening. Ingen dubblering finns att
  rätta.
- **Den andra grenen i samma switch** — *"2 räknande resultat faller ur inom kort."* — har inget
  datum och är oförändrad.

## Verifiering

`dotnet test`: **279 gröna** (278 + 1 nytt).

**I simulatorn (iPhone 17 Pro) mot skarp Eventor-data:** märket läser
*"Ett räknande resultat faller ur 19 sep."* med en punkt.

Sidnoteringen från körningen: rankingen hade uppdaterats sedan gårdagens körning — 1921:a i Sverige
och 204:e i H45 mot 1914 och 203 — vilket i förbifarten bekräftar att kedjan hämtar färskt och inte
cachat.
