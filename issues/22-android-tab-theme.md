# Issue #22 — Android: tabbaren följer inte ett temabyte

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/22
**Branch:** issue/22-android-tab-theme
**Status:** Completed

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

**Pixel Tablet-emulator (API 36), Orientera.** Barens färg är avläst ur skärmbilderna pixel för
pixel, inte bedömd med ögat:

| Läge | Barens färg |
|---|---|
| Start i mörkt | `#141218` |
| Byte till ljust, utan omstart | `#FEF7FF` |
| Tillbaka till mörkt | `#141218` |

Före fixen stod baren kvar mörk när sidan blev ljus — reproducerat med skärmbilder innan något
ändrades.

**En rättning som mätningen tvingade fram.** Första utfallet läste `colorSurfaceContainer` först.
Baren följde temat, men fick en annan mörk nyans efter första bytet (`#211F26`) än vid start
(`#141218`) och matchade sedan aldrig en nystartad app. Vyn är inflaterad med `colorSurface`, så
det är den som ska läsas först. Det syntes bara för att färgen mättes; med ögat hade båda passerat
som "mörk".

## Det som gjorde felsökningen lång

Tre fällor, värda att skriva ned:

1. **Fast Deployment.** APK:n innehåller inga assemblies — de pushas separat av `dotnet build
   -t:Run`. Ett `adb install` av APK:n ger en app som antingen kraschar med *"No assemblies found
   in .__override__"* eller kör kvar på en tidigare deploys kod. Det är därför appen länge visade
   gammal text. Bygg med `-p:EmbedAssembliesIntoApk=true` för att kunna installera med `adb`.
2. **macOS `strings` saknar `-e`.** Alla kontroller av om koden fanns i binären returnerade tyst
   noll; .NET lagrar stränglitteraler i UTF-16. Gjordes om i python.
3. **Emulatorn delas** med en annan app som återkommande tar förgrunden.
