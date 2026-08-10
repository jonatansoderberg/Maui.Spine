# Issue #21 — Android: tab pages are not inset above the Material bottom bar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/21
**Branch:** issue/21-android-tab-bar-inset
**Status:** Completed

## Plan

Ge tab-sidor på Android en bottenförskjutning som motsvarar den native barens höjd, och ta
bort workaroundet i Orientera så att fixen verifieras i stället för att döljas.

## Changes

- `Platforms/Android/SpineTabbedHostPage.Android.cs`:
  - `ApplyTabBarInset()` mäter `BottomNavigationView.Height`, räknar om till dp och sätter det
    som bottenoverride på varje tabs `TabInsetsProvider`.
  - Prenumererar på barens `LayoutChange` så värdet följer med när baren mäts om — den har
    ingen höjd förrän första layoutpasset, och den mäter om vid rotation och när
    systemfältens insets ändras (gestpinne kontra treknappsnavigering).
  - `InitializeEdgeToEdgeInsets` behåller 0 som startvärde och anropar `ApplyTabBarInset()`.
- `samples/Orientera`: `TabBarSpacer`/`ScreenPadding`-workaroundet borttaget.
  `ListBottomSpacer` (16) finns kvar som vanlig listmarginal.

## Decisions

- **Baren mäts, den antas inte.** Materialbarens höjd varierar med densitet, teckenstorlek,
  om etiketter visas och vilket navigeringsläge enheten kör. En konstant hade varit fel på
  någon enhet.
- **Barens höjd inkluderar redan systemets navigeringsinset.** Materialkomponenten lägger
  själv på den som padding i edge-to-edge-läge, så innehållet ska förskjutas med barens höjd
  och ingenting mer — annars dubbelräknas insetet.
- **Startvärdet är 0, inte fönstrets botteninset.** Systemets navigeringsfält ligger *bakom*
  baren; att rapportera det innan baren mätts hade gett fel förskjutning i ett par frames.

## Vad som var fel

Kommentaren i koden beskrev rätt problem men drog fel slutsats:

> on Android the opaque Material bar owns the bottom edge, so regions must not pad for the
> system navigation bar underneath it

Att inte förskjuta för systemets navigeringsfält är riktigt — det ligger bakom baren. Men
`SetBottomOverride(0)` tog bort förskjutningen för **baren själv**, som är ogenomskinlig och
ritas över sidans yta. Följden var att sista elementet i varje scrollbar vy låg permanent
bakom baren och inte gick att scrolla fram.

## Verifiering

- Pixel 10 Pro-emulator (API 36), Orientera utan workaround: sista raden i Jag (ScrollView)
  och i Tävlingar (CollectionView) hamnar ovanför baren med rätt marginal.
- iPhone 17 Pro-simulator (iOS 26.2): oförändrad — ändringen är Android-specifik.
- `dotnet build` för net10.0-android och net10.0-ios: OK.
