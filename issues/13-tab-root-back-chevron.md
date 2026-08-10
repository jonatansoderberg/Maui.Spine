# Issue #13 — Header bar renders an inert back chevron on tab root pages

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/13
**Branch:** issue/13-tab-root-back-chevron
**Status:** Completed

## Plan

Låt tillbaka-knappen styras av om det finns något att gå tillbaka till, inte bara av
sidattributet.

## Changes

- `Presentation/NavigationRegionViewModel.cs`: `GetImplicitBackAction()` returnerar `null` när
  `BackEnabled()` är falskt, oavsett presentation.

## Decisions

- **Villkoret satt på stackdjupet, inte på presentationen.** Den gamla koden undantog bara
  sheets:

  ```csharp
  if (!BackEnabled() && Presentation == NavigationPresentation.SheetPresentation)
      return null;
  ```

  En region i botten av sin stack fick alltså en tillbaka-knapp trots att `BackAsync()`
  no-oppar där. Det gäller både en tab-rot och rotsidan i en app utan flikar — buggen var
  aldrig specifik för tab-hosten, den blev bara synlig där eftersom fem rotsidor är
  ständigt nåbara.
- **Stängknappen i sheets är opåverkad.** Den kommer från `GetImplicitCloseAction`, som har
  sitt eget villkor (`CloseEnabled()`, dvs. stackdjup 1). En sheet i botten av sin stack
  visade aldrig tillbaka och gör det fortfarande inte — den visar stäng.
- **`IsBackButtonVisible` behåller sin betydelse.** Attributet säger "den här sidan vill inte
  ha en tillbaka-knapp"; stackdjupet säger "det finns inget att gå tillbaka till". Båda måste
  vara uppfyllda, och de svarar på olika frågor.

## Verifiering

iPhone 17 Pro-simulator (iOS 26.2), Orientera:

| Läge | Före | Efter |
|------|------|-------|
| Tab-rot (Hem, Tävlingar, …) | grå chevron som inte gör något | ingen chevron |
| Pushad sida i en flik (tävlingsdetalj) | chevron, fungerar | oförändrad, fungerar |
| Sheet i botten av sin stack (tidsmaskinen) | stängkryss | oförändrad, stängkryss |

`samples/MauiSpineSampleApp` och `samples/Orientera` bygger för iOS och Android.
