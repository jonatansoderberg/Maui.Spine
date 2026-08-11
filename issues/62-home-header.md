# Issue #62 — Hem: hälsningen klipps av

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/62
**Branch:** issue/62-home-header
**Status:** Completed

## Plan

Hälsningen låg i `CollectionView.Header`, och `Greeting` sätts först efter `await
_people.GetMeAsync()`. Headern hann mätas medan båda etiketterna var tomma, och en header som en
gång mätts som tom växer inte när innehållet kommer — rubriken klipptes på mitten och första
kortet ritades ovanpå den.

Rubriken flyttas ut ur listan till en egen rad i sidans `Grid`.

## Changes

- `HomePage.View.xaml` — hälsning och datum ligger i `Grid.Row="0"`, listan i rad 1.
  `CollectionView.Header` är borta.

## Decisions

- **Ut ur listan, inte "sätt texten tidigare".** Att sätta hälsningen före listan får innehåll
  botar den här instansen men inte nästa header som fylls asynkront — och skillnaden mellan
  demoläget och skarpt läge är just att fake-källan svarar synkront, så felet hade fortsatt vara
  osynligt i utveckling. En rad i ett `Grid` har en höjd som inte beror på när texten kommer.
- **Priset är att hälsningen står kvar när man scrollar.** Det är samma mönster som Live och
  Resultat redan använder för sina rubriker, så sidorna beter sig lika. Med fyra kort kostar det
  omkring nittio punkter permanent, vilket layouten tål.

## Verifiering

`dotnet test`: 214 gröna (ren XAML-ändring).

**iPhone 17 Pro-simulator (iOS 26.2), mot skarp backend** — där felet syntes: "Hej Jonatan" och
"tisdag 11 augusti" står hela, med kortet under. **Mot fake-datat:** samma sak med fyra block —
"Hej Elin", "lördag 15 augusti", och korten scrollar under rubriken.
