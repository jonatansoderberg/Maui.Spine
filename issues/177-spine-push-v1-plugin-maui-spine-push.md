# Issue #177 — Spine.Push v1: Plugin.Maui.Spine.Push för iOS och Android

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/177
**Branch:** issue/177-spine-push-v1-plugin-maui-spine-push
**Status:** In Progress

## Plan

Klientpaketet enligt förstudien §5, §6.1, §7.1 och §7.2. Serverkontraktet finns i #176. En commit per steg.

### Steg 0 — spiken: `class_addMethod` mot `fetchCompletionHandler:` ✅ KLAR

Utfallet står under Changes. Kort: vägen håller, men metoderna måste läggas till **före `UIApplication.Main`**, vilket kostar en rad i appens `Program.cs`.

### Steg 0 (som den planerades)

Issuet säger att den ska spikas först, och den avgör hur steg 2 ser ut. Verifierat i förväg: `IiOSLifecycleBuilder` i MAUI 10.0.50 saknar **alla fyra** krokar — `RegisteredForRemoteNotifications`, `FailedToRegisterForRemoteNotifications`, `DidReceiveRemoteNotification` och `WillPresentNotification` — så någon egen väg behövs.

Spiken görs som en slängfil i `MauiSpineSampleApp`: lägg till `didReceiveRemoteNotification:fetchCompletionHandler:` på appens `AppDelegate`-klass med `class_addMethod` och en `[UnmanagedCallersOnly]`-funktion, kör i simulatorn och skicka `xcrun simctl push` med en `content-available`-payload. Det svåra är blockargumentet: completion-handlern är ett Objective-C-block som måste anropas från den ohanterade funktionen. Simulatorn räcker för att bevisa hela kedjan utom själva tokenutdelningen.

Faller spiken tas fallbacken: dokumenterade overrides i appens egen `AppDelegate` som anropar `SpinePush.Ios.*`-forwarders. Det är vad alla andra ramverk kräver ändå.

### Steg 1 — plattformsneutralt: `UseSpinePush`, `IPushService`, `IPushHandler`

- `SpinePushOptions` med `Backend`, `AuthorizationHeader`, `Permission` (`WhenAsked` | `Provisional` | `AtLaunch`), `Channels.Add(id, name, importance)`, `UseHandler<T>()`.
- `IPushService` med `Status`, `InstallationId`, `Tags`, `RequestPermissionAsync`, `SetTagsAsync`/`AddTagsAsync`/`RemoveTagsAsync`, `RefreshAsync`, `UnregisterAsync`, `OpenSettingsAsync`.
- `IPushHandler` med `OnReceivedAsync(PushMessage, PushContext) → PushPresentation` och `OnOpenedAsync(PushMessage, string? action)`, plus `PushMessage`, `PushContext`, `PushKind`, `PushPresentation` enligt §5.2.
- Installations-id i `SecureStorage`, skapat en gång.
- `PushRegistrationClient` som gör `PUT`/`DELETE /push/installations/{id}` mot `Backend`, med `PushJson` från `Common`. Skickar bara när något ändrats — token, taggar, versioner, Live Activity-tokens — annars en gång per dygn för `ExpiresAt`. `platform:`, `os:` och `app:`-taggarna sätts av paketet.
- `UseSpinePush` följer `UseSpineWidgets`: partiell `ConfigurePlatform`, resten i `ConfigureLifecycleEvents`.

### Steg 2 — iOS och Mac Catalyst

Delegatmetoderna på appens `AppDelegate` enligt spikens utfall, `UNUserNotificationCenter.Delegate` satt av paketet, token upp i installationen, `Route` levererad efter att Spine-hosten skapats även vid kallstart.

### Steg 3 — Android

`SpinePushMessagingService : FirebaseMessagingService` i paketet, kanaler i `OnApplicationCreate`, notisen ritad från data-meddelandet med `NotificationCompat` och `BigTextStyle`, `PendingIntent` med `Route`, rättighet via `Permissions.PostNotifications`. Beroenden `Xamarin.Firebase.Messaging` och `Xamarin.GooglePlayServices.Basement` — inget av det finns i repot i dag.

### Steg 4 — targets

Apple: `aps-environment` i entitlements och `UIBackgroundModes: remote-notification` via `PartialAppManifest`; på Catalyst skrivs båda nyckelvarianterna. Android: manifest-overlay med `MESSAGING_EVENT` och `POST_NOTIFICATIONS`, och ett tydligt fel när `<GoogleServicesJson>` saknas.

### Steg 5 — Live Activities och widgets

`LiveActivityPushTokens` blir default när `UseSpineWidgets` är på; push-to-start- och per-aktivitetstokens skickas upp vid start, förgrund, bakgrundskörning och tokenbyte. `spine.kind=liveactivity` kör `ILiveActivityService` på Android, `spine.kind=widget` kör `IWidgetService.RefreshAsync(kind)`.

### Steg 6 — `docs/wiki/push.md`

Steg för steg ur förstudiens §9: Apple-portalen, Firebase, entitlements, och test med `xcrun simctl push`.

## Open Questions

Inga öppna. Avgjorda 2026-09-08:

1. **Entitlements** — alternativ b: ett gemensamt steg som både Widgets och Push bidrar nycklar till. #177 rör därmed Widgets-paketet.
2. **Orienteras `ForegroundPresenter`** — tas bort i #178, inte här.
3. **Standard-`PushPresentation`** — `Banner | Sound | List`.
4. **Fallback** — dokumenterade overrides i appens `AppDelegate` är godtagbart om spiken faller.

## Changes

### Steg 0 — spiken, resultat

Körd som slängkod i `MauiSpineSampleApp` mot iPhone 17 Pro-simulatorn. Referenskopian ligger i sessionens scratchpad. Fem frågor, fem svar:

1. **`class_addMethod` fungerar.** Selektorn `application:didReceiveRemoteNotification:fetchCompletionHandler:` fanns inte på `AppDelegate`, lades till, och `class_getInstanceMethod` hittar den efteråt.
2. **Finns metoden redan lämnas den orörd.** Med en `[Export]`-deklarerad implementation i appens `AppDelegate` rapporterade proben `selector already implemented: True` och `class_addMethod -> False`. Det är precis den gren designen behöver, och den är nu verifierad i stället för antagen.
3. **Blockanropet fungerar.** Ett syntetiserat Objective-C-block anropat genom funktionspekaren 16 byte in i blockliteralen nådde den hanterade kroppen, och argumentet `7` kom fram intakt. Det var den enskilt största tekniska risken i issuet.
4. **`UNUserNotificationCenter`-vägen fungerar end-to-end.** `xcrun simctl push` med en alert gav `WillPresentNotification` med hela `userInfo`, inklusive `spine.kind` och `spine.route`. Förgrundsvägen och `PushPresentation` går alltså att bygga och verifiera här.
5. **Klassen finns före `UIApplication.Main`.** `objc_lookUpClass("AppDelegate")` svarar i `Main`, och `class_addMethod` lyckas där.

Och en sak som **inte** går att verifiera här: `xcrun simctl push` levererar inte tysta pushar (`content-available`) till `didReceiveRemoteNotification:fetchCompletionHandler:` i simulatorn. Det visades genom att en helt vanlig `[Export]`-deklarerad implementation inte heller fälldes ut — det är alltså inte `class_addMethod` som fallerar, utan simulatorn som inte levererar. Den vägen kan bara verifieras på fysisk enhet, i #178.

### Steg 1 — det plattformsneutrala lagret

- Nytt `src/Plugin.Maui.Spine.Push/` som refererar kärnan och `Common`. Inlagt i `Spine.slnx`.
- `SpinePushOptions` med `Backend`, `AuthorizationHeader`, `Permission`, `Channels`, `Expiry` och `UseHandler<T>()`.
- `IPushService`, `PushStatus`, och `IPushHandler` med `PushMessage`, `PushContext`, `PushKind` och `PushPresentation` enligt §5.2. `PushMessage.From` läser en payloads databag till ett meddelande.
- `IPushPlatform` — sömmen mot plattformarna: `Status`, `Handle`, `Environment`, `WidgetToken`, `RequestPermissionAsync`, `OpenSettingsAsync` och `HandleChanged`.
- `PushRegistrationClient` gör `PUT`/`DELETE` mot backend och behandlar ett nätverksfel som normalfallet, inte som ett undantag att kasta vidare.
- `PushService` äger installations-id:t i `SecureStorage`, taggarna i `Preferences`, och avgör när backend behöver höra av sig: en SHA-256 över allt som är värt att berätta, plus en dygnspuls. Live Activity-tokens hämtas genom `ILiveActivityService` **om** appen också använder Widgets — interfacet ligger i `Common`, så paketen behöver inte känna varandra.
- `UseSpinePush` följer `UseSpineWidgets`: partiell `ConfigurePlatform`.

## Decisions

- **MAUI:s lifecycle-API saknar krokarna, verifierat.** En probe som anropar `RegisteredForRemoteNotifications`, `FailedToRegisterForRemoteNotifications`, `DidReceiveRemoteNotification` och `WillPresentNotification` på `IiOSLifecycleBuilder` ger fyra `CS1061` mot MAUI 10.0.50. Issuets påstående stämmer, och `class_addMethod` eller overrides är alltså de enda vägarna.
- **`Route` navigeras av appen, inte av paketet.** Förstudien säger att routen är "en Spine-route som `INavigationService` kan öppna", men `INavigationService` är helt generisk — `NavigateToAsync<TPage>()` — och har ingen uppslagning från sträng till sida. Att lägga till en vore en ny funktion i kärnan, utanför det här issuet. Paketet levererar därför routen till `IPushHandler.OnOpenedAsync`, precis som widgetarna lämnar sin länk till `IWidgetLinkHandler`. Samma mönster, och appen behåller kontrollen.
- **Registrering hoppas över när ingenting rört sig.** Ett fingeravtryck över token, taggar, versioner och Live Activity-tokens jämförs mot det senast skickade; `UpdatedAt` och `ExpiresAt` ingår inte, eftersom de ändras vid varje bygge och skulle göra jämförelsen meningslös. En dygnspuls skickar ändå, så servern ser att enheten lever.
- **Metoderna läggs till före `UIApplication.Main`, inte i `FinishedLaunching`.** `UIApplication` läser av vilka callbacks delegaten har när den sätts, och delegaten sätts av `UIApplication.Main` innan `FinishedLaunching` körs. Att lägga till metoden efteråt kan därför missas av UIKit — och eftersom simulatorn inte levererar tysta pushar går det inte att mäta här. Spiken visade att `objc_lookUpClass("AppDelegate")` svarar redan i `Main`, så paketet lägger till metoderna där. Priset är en rad i appens `Program.cs`, `SpinePush.Ios.Install()` före `UIApplication.Main`, vilket fortfarande är mycket lättare än de fyra overrides fallbacken hade krävt. Raden dokumenteras i `docs/wiki/push.md`.
- **Objective-C-interop utan bindningsprojekt har redan precedens i repot.** `WidgetPlatform.iOS.cs` anropar bryggan med sju `objc_msgSend`-`DllImport`:ar. Spiken bygger vidare på samma mönster; det nya är blockargumentet.

## Verifiering

Simulatorn räcker längre än man tror: `xcrun simctl push` levererar en payload och driver både delegatmetoden och `UNUserNotificationCenter`-delegaten, så handler, `PushMessage`-form och `Route`-navigering går att verifiera här. Android verifieras i emulatorn med `adb`-intents och ett testmeddelande.

**Det som inte går att verifiera på den här maskinen:** riktig APNs-registrering ger ingen token i simulatorn, och en fysisk enhet med profil och push-entitlement saknas — ingen signeringsidentitet finns, se minnesanteckningen `ios-build-environment`. Milstolpen "push till en fysisk iPhone" ligger i #178. Windows-TFM:en går inte att bygga här.
