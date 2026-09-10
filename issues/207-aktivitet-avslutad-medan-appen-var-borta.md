# Issue #207 — Spine.Widgets: en aktivitet som avslutas medan appen är nedstängd upptäcks aldrig som en händelse

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/207
**Branch:** issue/207-aktivitet-avslutad-medan-appen-var-borta
**Status:** Completed

## Plan

`LiveActivityService.Sync()` jämför plattformens lista mot `_active`, som är tom vid kallstart. En aktivitet som tog slut medan appen var nere upptäcks därför aldrig som en händelse — registreringen självläker, men appen vet inte vad som hände.

1. **Minnet över processgränsen.** Mängden id → kind sparas i `Preferences` (`spine.widgets.activities`) varje gång listan ändras. Vid processens första avstämning fylls `_active` med de ihågkomna aktiviteterna innan jämförelsen — då upptäcker den befintliga borta-logiken själv dem som saknas hos plattformen. Ingen ny jämförelse behövs.
2. **Händelsen.** `ILiveActivityService.ActivityEnded` (`Action<LiveActivity>`) höjs för varje aktivitet som tog slut utan att appen själv avslutade den: bortsvept, avslutad av en push, för gammal — eller slut medan appen inte körde. `ActivitiesChanged` höjs som förut. Appens egen `EndAsync` ger ingen händelse, eftersom anroparen redan vet.
3. **Ett bestämt ögonblick vid start.** `Reconcile` körs i dag vid `WillEnterForeground` och bryggans notis på iOS, `OnResume` på Android — inte vid kallstart på iOS. Den läggs till i `FinishedLaunching`. Det som `Adopt()` hittar (den körs i `Active`-gettern, där en händelse inte ska höjas) sparas och tillkännages vid nästa `Reconcile`.
4. **Samplet** prenumererar direkt efter `builder.Build()` i `MauiProgram` — MAUI skapar appen i `WillFinishLaunching`, före `FinishedLaunching`, så den som prenumererar där hör även om aktiviteter som tog slut medan appen var nere.
5. **Wikin**: `ActivityEnded`, och att man prenumererar tidigt för att höra om det som hände medan appen var nere.

## Open Questions

Inga. API:t beslutades av Jonatan: en ny händelse, inte bara `ActivitiesChanged` och inte en egenskap.

## Changes

- `ILiveActivityService.ActivityEnded` (`Action<LiveActivity>`), med dokumentation om att prenumerera direkt efter `builder.Build()`.
- `LiveActivityService`: minnet i `Preferences` (`spine.widgets.activities`, id → kind), skrivet vid varje ändring; vid processens första avstämning fylls listan från minnet innan jämförelsen. `Sync` returnerar vad som försvann; `Reconcile` höjer `ActivityEnded` för dem och `ActivitiesChanged` en gång; det `Adopt()` hittar i gettern sparas och tillkännages vid nästa `Reconcile`.
- iOS: `Reconcile` även i `FinishedLaunching`.
- Samplet: prenumererar i `MauiProgram` och loggar `<kind> slut utanför appen`; Live Activity-sidans egen gissning borttagen.
- `docs/wiki/widgets.md`: `ActivityEnded` i API-listan, och "vid nästa start" i stället för "vid nästa förgrund" på båda ställena.

## Verifiering

- **Android (emulator):** aktivitet startad, appen i bakgrunden, processen dödad (`pidof` tom). Notisen svept bort i skuggan — den försvann, och dess `DeleteIntent` startade en ny process. Den nya processen utgick från minnet, fann aktiviteten borta och höjde `ActivityEnded`: `15:05:33 live activity sample slut utanför appen`.

- **iOS (simulator, 26.4):** aktivitet startad (`D2FBD903…`), appen avslutad med `simctl terminate` 15:05:54. Aktiviteten svept bort på låsskärmen — SpringBoard 15:06:51: *Activity dismissed: D2FBD903…* — varpå `liveactivitiesd` startade appen i bakgrunden. Den nya processen fyllde listan från minnet vid `Reconcile` i `FinishedLaunching`, fann aktiviteten borta och höjde `ActivityEnded`: `15:06:55 live activity sample slut utanför appen`.

## Decisions

- **`ActivityEnded` i stället för en egenskap `EndedWhileAway`.** En händelse täcker både det som tar slut medan appen kör och det som tog slut medan den var nere, med samma kod i appen.
- **Ingen händelse för appens egen `EndAsync`.** Anroparen vet redan; händelsen är till för det appen inte såg.
