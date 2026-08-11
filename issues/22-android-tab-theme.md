# Issue #22 — Android: tabbaren följer inte ett temabyte

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/22
**Branch:** issue/22-android-tab-theme
**Status:** Fix skriven, **inte verifierad**

## Plan

Byter systemet mellan ljust och mörkt medan appen kör byter innehållet direkt, men Material-baren
i botten står kvar i förra temats yta tills appen startas om. MAUI-aktiviteten deklarerar `UiMode`
bland sina `ConfigurationChanges`, så Android återskapar den inte och ingenting inflaterar om
baren.

## Changes

- `SpineTabbedHostPage.Android.cs` — prenumererar på `Application.RequestedThemeChanged` och
  läser om barens färger ur aktivitetens tema när det byts: yta från `colorSurfaceContainer`
  (Material 3) med `colorSurface` som reserv, ikon- och texttoner från `colorOnSurface` och
  `colorOnSurfaceVariant`. Spines egna överskrivningar (`ApplyStyle`) läggs på efteråt, i samma
  ordning som vid uppkoppling.

## Decisions

- **Läs om temat, hårdkoda inte.** Aktivitetens tema *följer* konfigurationsändringen; det är bara
  de färger som redan lösts in i vyn som är gamla. Att slå upp dem igen räcker, och håller baren
  på vad appens Material-tema säger i stället för på en färg Spine valt åt den.
- **Attribut slås upp på namn.** Material-bindningen exponerar inte sina `Resource.Attribute`-
  konstanter, och en namnuppslagning löses mot det tema appen faktiskt använder i stället för mot
  en konstant som kanske inte finns i det.
- **Prenumerationen sägs aldrig upp.** Värden lever lika länge som fönstret, och en bar som slutar
  följa temat halvvägs in i en session är samma fel igen.

## Verifiering

**Felet är reproducerat**, på Pixel Tablet-emulator (API 36), med skärmbilder före och efter
`adb shell cmd uimode night no`: sidan blir ljus, baren står kvar svart.

**Fixen är inte verifierad.** Tre saker stod i vägen, och de är värda att skriva ned eftersom de
kostade mest tid av allt:

1. **`dotnet build` paketerar inte om APK:n när bara ett refererat projekt ändrats.** APK:ns
   tidsstämpel uppdateras, men innehållet är gammalt. Bara `dotnet clean` följt av ett nytt bygge
   gav en APK med rätt kod i sig.
2. **macOS `strings` saknar `-e`.** Alla mina kontroller av om koden fanns i binären returnerade
   tyst noll och var därmed värdelösa — .NET lagrar stränglitteraler i UTF-16. Kontrollerna gjordes
   om i python och visade då att bygget var korrekt hela tiden.
3. **Emulatorn delas med en annan app** som återkommande tar förgrunden, så skärmbilderna visade
   inte alltid Orientera.

Det som *är* fastställt: felet finns, orsaken i ärendet stämmer, koden kompilerar och ligger i den
byggda binären. Att baren faktiskt byter färg vid ett temabyte har jag inte sett med egna ögon,
och det ska ingen tro att jag har.

## Nästa steg

Kör med en ren emulator: `dotnet clean`, bygg, `adb uninstall`, `adb install`, starta appen, växla
`adb shell cmd uimode night no` och jämför skärmbilder. Blir baren fortfarande mörk är nästa
misstänkta att `RequestedThemeChanged` inte fyras på Android vid `UiMode`-ändring — då är
`Activity.OnConfigurationChanged` rätt hook i stället, och den kräver att MAUI:s aktivitet
exponerar den.
