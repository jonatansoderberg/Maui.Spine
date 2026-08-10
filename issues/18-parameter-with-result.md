# Issue #18 — Typed navigation cannot combine a parameter with a result

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/18
**Branch:** issue/18-parameter-with-result
**Status:** Completed

## Plan

Lägg till en överlagring som både bär en parameter in och returnerar ett resultat, och bevisa
den genom att ta bort överlämningsobjektet i Orientera.

## Changes

- `Core/INavigationService.cs`: `NavigateToWithResultAsync<TPage, TParam, TResult>(TParam param)`
  med constraint på både `INavigableWithParameter<TParam>` och `INavigableWithResult<TResult>`.
- `Services/NavigationService.cs`: båda överlagringarna delar nu en gemensam kärna,
  `NavigateToWithResultCoreAsync`, som tar en valfri leveransdelegat för parametern.
- `docs/wiki/navigation-results.md`: noten om att det inte gick är ersatt med den nya
  överlagringen och ett picker-exempel.
- `samples/Orientera`: `ComparisonRequest` är inte längre ett DI-registrerat
  överlämningsobjekt utan en vanlig record som skickas som navigeringsparameter.
  `CompareRunnerSheet` deklarerar båda interfacen.

## Decisions

- **En gemensam kärna, inte två kopior.** Skillnaden mellan överlagringarna är ett anrop;
  att duplicera hela metoden hade betytt två ställen att hålla i synk när sheet- och
  region-vägarna ändras.
- **Parametern levereras efter `SetViewModelMeta` och före presentationen**, samma punkt som i
  `NavigateToAsync<TPage, TParam>`. Kontraktet i dokumentationen — parametern är framme före
  `OnAppearingAsync` — gäller därmed likadant för båda vägarna.
- **Typordningen är `<TPage, TParam, TResult>`.** Sidan först som i alla andra anrop, sedan in
  och ut i den ordning de inträffar.
- **Ingen leveransdelegat för den gamla överlagringen** (`null`) i stället för en tom delegat,
  så den befintliga vägen inte får ett extra allokerat anrop per navigering.

## Verifiering

iPhone 17 Pro-simulator (iOS 26.2): `CompareRunnerSheet` öppnas från Resultat → Analys →
"Jämför med vinnaren", får sin `ComparisonRequest` som parameter, listar rätt startfält
(D21, utan mig, med Min grupp-märkning) och returnerar vald löpare — head-to-head-tabellen
fylls med rätt jämförelse.

`samples/Orientera` och `samples/MauiSpineSampleApp` bygger för iOS och Android.
