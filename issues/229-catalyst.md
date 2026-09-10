# Issue #229 — Spine.Push: Mac Catalyst — Apple-koden kompileras inte för maccatalyst

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/229
**Branch:** issue/229-catalyst
**Status:** Completed

## Plan

1. **Apple-koden kompileras för båda.** MAUI:s `SingleProject.targets` sätter `ExcludeFromCurrentConfiguration=true` på *alla* filer under `Platforms/` och släpper bara in den aktuella plattformens egen mapp — `Platforms/iOS` för ios, `Platforms/MacCatalyst` för maccatalyst. Kod som delas mellan de två kan alltså inte ligga i någon av dem, och inte heller i en egen `Platforms/Apple/`. Filerna flyttas till `Apple/` och skyddas med `#if IOS || MACCATALYST`.
2. **Fjärrpush på Catalyst följer signeringen** (Jonatans val): `aps-environment` och bakgrundsläget skrivs för maccatalyst bara när appen anger en profil (`CodesignProvision`). Utan profil får appen lokala notiser och startar.
3. **Körningen hoppar över APNs-registreringen** när push inte är konfigurerat i bygget — nyckeln `SpinePushEnvironment` i `Info.plist`, som targets redan skriver, saknas då. Samma regel täcker `SpinePushRemote=false` på iOS.
4. **Push-samplets Catalyst-entitlements** blir av med push-nycklarna — samplet har ingen Mac-profil.
5. **Verifiering:** lokala notiser i push-samplet på Mac Catalyst, på den här maskinen. Fjärrpush kräver en Mac-profil med push, som inte finns här.
6. **Wikin:** `push.md` påstår att v1 täcker Catalyst; rättas till vad som faktiskt gäller.

## Open Questions

Inga.

## Changes

- `Platforms/iOS/*.iOS.cs` → `Apple/*.Apple.cs`, varje fil inom `#if IOS || MACCATALYST`. Catalyst-dll:en innehåller nu `ApplePushPlatform` och `AppleLocalNotifications`; bygget går igenom för maccatalyst, iOS och Android.
- `Plugin.Maui.Spine.Push.targets`: `_SpinePushApple` gäller iOS som förut, maccatalyst bara när `CodesignProvision` är satt.
- `ApplePushPlatform.IsRemoteConfigured` — `SpinePushEnvironment` finns i Info.plist. `RequestPermissionAsync`, `Start` och `Resume` hoppar över APNs-registreringen när den saknas.
- Push-samplets `Platforms/MacCatalyst/Entitlements.plist`: push-nycklarna och App Group borttagna, med en kommentar om varför.
- **Orientera bygger för Catalyst** — felet från `_SpineWriteEntitlements` är borta.

## Verifiering

- Bygge för maccatalyst, iOS och Android; Catalyst-dll:en innehåller Apple-implementationen. Orientera bygger för Catalyst.
- Push-samplet på Mac Catalyst, signerat med utvecklingsidentiteten och utan profil: signaturen har bara sandbox och nätverk, `SpinePushEnvironment` saknas i Info.plist, och loggen visar noll APNs-fel — registreringen hoppades över.
- **Lokala notiser på Mac Catalyst, sett av Jonatan:** tillståndsfrågan kom, notisen "Spine lokalt" kom som macOS-notis, och ett klick öppnade appens Logg.
- Fjärrpush på Catalyst är inte verifierad — datorn saknar en Mac-profil med push.

## Decisions

- **`Apple/` utanför `Platforms/`, inte en MSBuild-target som vänder på MAUI:s metadata.** Att böja `ExcludeFromCurrentConfiguration` hänger på en intern detalj i MAUI; ändras den går Catalyst tyst sönder igen — precis så det här felet uppstod. `#if IOS || MACCATALYST` är vanlig C#, och samma mönster som Orienteras schemaläggare använde.
- **Följ signeringen** (Jonatans val) i stället för en ny egenskap eller att kräva nyckeln som i dag.
