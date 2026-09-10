# Issue #222 — Orientera: byt den egna notisschemaläggaren mot ramverkets ILocalNotificationService

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/222
**Branch:** issue/222-orientera-lokala-notiser
**Status:** Completed

## Plan

Migreringen är beviset på att API-ytan från #197 bär. Kodläsningen gav en sak issuen gissade fel om, och den styr formen — se **Decisions**.

### 1. Sömmen stannar, men krymper

`Orientera.Tests` kompilerar **hela** `..\Orientera\Services\Notifications\**\*.cs` på vanlig `net10.0`. Mappen måste därför förbli MAUI-fri, och `NotificationService` kan inte ta `ILocalNotificationService` direkt. Samma skäl som `IPushRegistration` finns av — inte att leveransen testas.

- `Services/Notifications/INotificationScheduler.cs` → `INotificationDelivery.cs`: `IsSupported`, `SyncAsync(plan, ct)`, `CancelAllAsync(ct)`. `RequestPermissionAsync` försvinner härifrån — det är hela poängen med #197:s första problem. `UnsupportedNotificationScheduler` försvinner: `ILocalNotificationService.IsSupported` svarar redan.
- Ny `Services/Push/SpineNotificationDelivery.cs` (utanför testernas glob, som `SpinePushRegistration`): mappar `PlannedNotification` → `LocalNotification` och lämnar över till `ILocalNotificationService`.

### 2. Det som tas bort

- `Services/Notifications/AppleNotificationScheduler.cs`
- `Services/Notifications/AndroidNotificationScheduler.cs` — inklusive `NotificationReceiver` och kanalen `orientera.competition`.
- Plattformsgrenarna i `MauiProgram.RegisterDomainServices`; en registrering blir kvar.

### 3. En dialog, inte två

`NotificationSheet.ViewModel` frågar i dag både `_scheduler.RequestPermissionAsync()` och `_push.RequestPermissionAsync()` — två vägar till samma systemdialog. Kvar blir en, genom `IPushRegistration`, vars `RequestPermissionAsync` byter från `Task` till `Task<bool>` så att arket fortfarande kan säga vad användaren svarade. `SpinePushRegistration` mappar `PushStatus.Authorized or Provisional` → `true`.

### 4. Spine.Push alltid, backend bara ibland

`RegisterPush` returnerar i dag tidigt utan backend, så `UseSpinePush` aldrig körs — och då finns ingen `ILocalNotificationService`. Anropet flyttas ut ur den grenen: `UseSpinePush` körs alltid, `Backend` sätts bara när adressen finns. Det är precis läget #197 byggde för — ingen fjärrregistrering, inget tokenletande — och demoläget får lokala notiser utan backend på köpet. `TryAddSingleton<IPushRegistration, NoPushRegistration>` blir därmed död och tas bort; typen stannar.

### 5. Kanalen delas

Lokala notiser postas på `competitions`, kanalen `RegisterPush` redan skapar för push. I dag har appen två kanaler som båda heter "Tävlingar" i systeminställningarna — en för varje halva. Efter det här: en.

### 6. Route

`PlannedNotification` får ingen ny egenskap; `SpineNotificationDelivery` sätter `Route` från `Kind` och `Competition`, så att en öppnad notis navigerar till tävlingen i stället för att bara öppna appen. `OrienteraPushHandler` hanterar redan `spine.route`.

### 7. Verifiering

`dotnet test samples/Orientera.Tests` — beviset på att mappen förblev MAUI-fri. Sedan Android-emulatorn: planera från arket, se alarmen i `dumpsys alarm`, och att notisen postas på `competitions`.

## Open Questions

Inga. Frågan issuen ställde — om sömmen behövdes — besvaras av testprojektets glob.

## Changes

- `INotificationScheduler` → `INotificationDelivery`: `IsSupported`, `SyncAsync(plan)`, `CancelAllAsync()`. Permissionsmetoden borta, `UnsupportedNotificationScheduler` borta.
- `AppleNotificationScheduler` och `AndroidNotificationScheduler` borttagna, inklusive `NotificationReceiver` och kanalen `orientera.competition`.
- Ny `Services/Push/SpineNotificationDelivery.cs` — mappar `PlannedNotification` → `LocalNotification`, sätter kanal `competitions` och route.
- `PushRoute.For(kind, competition)` — routen en lokal notis bär, så den öppnar samma sida som en pushad.
- `IPushRegistration.RequestPermissionAsync` returnerar `Task<bool>`; `SpinePushRegistration` mappar `Authorized or Provisional`.
- `NotificationSheet.ViewModel` frågar en gång i stället för två.
- `MauiProgram`: `UseSpinePush` körs alltid, `Backend` sätts bara när adressen finns; plattformsgrenarna och `TryAddSingleton<IPushRegistration, NoPushRegistration>` borta.
- `notification_icon.xml` → `spine_push_icon.xml`, namnet Spine letar efter. Nu ritas båda halvorna med Orienteras ikon i stället för att push faller tillbaka på launcher-ikonen.

## Verifiering

- `dotnet test samples/Orientera.Tests` — 568 tester, alla gröna. Det är beviset på att `Services/Notifications` förblev MAUI-fri.
- `dotnet build samples/Orientera` för `net10.0-android` och `net10.0-ios`.
- Android-emulator: appen startar med den nya kopplingen, notisarket visar "Notiserna ligger i telefonen…" från `INotificationDelivery.IsSupported`, och en switch slås på utan en andra tillståndsdialog. Taggarna skrivs fortfarande (`spine.push.tags` i prefs).
- **Inte verifierat med riktiga data:** backenden svarade 500 på den här maskinen, så `RefreshAsync` tog `SourceUnavailableException`-grenen och kom aldrig till `SyncAsync`. Att planen blir alarm är däremot verifierat i #197 med samma kod, genom push-samplet.

## Decisions

- **`INotificationDelivery` behålls som app-söm, tvärtemot vad #222 gissade.** Skälet är inte att leveransen testas, utan att `Orientera.Tests` kompilerar hela `Services/Notifications`-mappen på `net10.0`. En MAUI-typ där bryter testbygget. Sömmen krymper i stället till det den är: lämna över en plan.
- **`UseSpinePush` körs även utan backend.** Annars finns ingen `ILocalNotificationService` att leverera med i demoläget. `RefreshAsync` och `UnregisterAsync` vaktar redan på `Backend is null`, så vägen är säker.
- **Notisikonen bytte namn i stället för att följa med.** `AndroidNotificationScheduler` ritade med `Resource.Drawable.notification_icon`; Spine letar efter `spine_push_icon` och faller annars tillbaka på launcher-ikonen, som Android ritar som en vit klump. Bytet ger båda halvorna Orienteras starttriangel — push fick den på köpet, den hade den inte förut.
