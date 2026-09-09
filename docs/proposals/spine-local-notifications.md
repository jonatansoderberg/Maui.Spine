# Lokala notiser i Spine.Push (förstudie, rev 1)

**Status:** Genomförd. Implementationen följde förslaget; avvikelserna står i §11. Issue: [#197](https://github.com/jonatansoderberg/Maui.Spine/issues/197), dokumentation i [`docs/wiki/push.md`](../wiki/push.md).
**Fråga:** Kan lokala notiser bo i ramverket i stället för i varje app, och kan de dela tillstånd, kanaler och mottagning med push utan att appen behöver veta vilken väg en notis kom?
**Svar:** Ja, och utan ett nytt paket. `Plugin.Maui.Spine.Push` äger redan allt utom schemaläggningen: tillståndsanropen är samma systemanrop, kanalerna skapas redan vid start, och `UNUserNotificationCenter`-delegaten som tar emot push tar emot lokala notiser också. Det som saknas är ett `ILocalNotificationService` med en idempotent `SyncAsync`, en plattformsklass per sida, och en markör i nyttolasten så att appen **kan** skilja vägarna åt när den vill.

---

## 1. Slutsatsen i korthet

| Fråga | Svar | Belägg |
|---|---|---|
| Eget paket eller i Spine.Push? | **I Spine.Push.** Tillstånd, kanaler och mottagning är redan där; ett separat paket skulle behöva referera dem ändå och appen skulle få två halvor att hålla ihop — vilket är precis problemet issuen beskriver. | §2 |
| Ett tillstånd, ett anrop? | **Ja.** `IPushService.RequestPermissionAsync` anropar redan exakt de systemanrop en lokal schemaläggare behöver. Ingen andra fråga behövs, och ingen ska erbjudas. | `ApplePushPlatform.iOS.cs:48`, `AndroidPushPlatform.cs:49` |
| Kan en app använda bara lokalt, utan backend? | **Ja i koden, nej i byggkedjan i dag.** Runtime klarar `Backend = null`, men targets kräver `google-services.json` på Android och skriver `aps-environment` på Apple. Behöver ett läge. | §5, `Plugin.Maui.Spine.Push.targets` |
| Når en öppnad lokal notis samma handler? | **Ja, gratis.** Båda öppningsvägarna läser en data-påse: iOS ur `UserInfo`, Android ur intent-extras. En lokal notis som bär samma `spine.*`-nycklar går genom samma kod. | `SpinePushExtensions.iOS.cs:88`, `PushNotifications.Read` |
| Ska appen kunna se att den är lokal? | **Ja.** `spine.source = local` i data-påsen, läst som `PushMessage.IsLocal`. En nyckel och inte en C#-flagga, för att den ska överleva processgränsen. | §4 |
| Vad äger appen fortfarande? | Vad som ska notifieras och när. Orienteras `NotificationPlanner` och `PushTags` flyttar inte in. | §7 |

---

## 2. Vad som redan finns

| Del | Var | Vad det betyder för lokalt |
|---|---|---|
| Tillstånd | `ApplePushPlatform.RequestPermissionAsync` → `UNUserNotificationCenter.RequestAuthorizationAsync`; `AndroidPushPlatform` → `Permissions.PostNotifications` | Samma anrop en lokal schemaläggare skulle göra. Orienteras `INotificationScheduler.RequestPermissionAsync` är en andra väg till samma systemdialog och kan tas bort. |
| Kanaler | `PushNotifications.CreateChannels` vid `OnApplicationCreate` | Kanalerna finns redan när en lokal notis ska postas. Den behöver bara nå dem. |
| Notisen ritas i appen | `PushNotifications.Show` — kanal, ikon (`spine_push_icon`), `BigTextStyle`, `CollapseId`, öppna-intent | En lokal notis ska ritas av samma metod, annars ser de två vägarna olika ut utan att någon bestämt det. |
| Öppning, iOS | `NotificationDelegate.DidReceiveNotificationResponse` → `PushPayload.Read(UserInfo)` → `OpenedAsync` | Systemet lämnar lokala notiser till samma delegat. Inget att bygga. |
| Öppning, Android | `OnCreate`/`OnNewIntent` → `PushNotifications.Read(intent)` → `OpenedAsync` | Kräver `spine.kind` i extras — vilket en lokal notis postad av `PushNotifications.Show` får automatiskt. |
| Förgrundspresentation | `IPushHandler.OnReceivedAsync` returnerar `PushPresentation` | Gäller redan lokala notiser på iOS, eftersom `WillPresentNotification` är samma delegat. Android har ingen motsvarighet: en postad notis visas. |
| `IsRegistered` | `IPushService` | Signalen appen behöver för att välja vilken halva som levererar vad. Finns, dokumenterad, oanvänd i ramverket. |

Det enda som saknas är alltså **schemaläggningen** och **API-ytan** — inte plattformsintegrationen.

---

## 3. API-ytan

```csharp
/// <summary>The notifications this device shows on its own, without a server.</summary>
public interface ILocalNotificationService
{
    bool IsSupported { get; }

    /// <summary>Makes the device's pending notifications equal <paramref name="plan"/>.</summary>
    Task SyncAsync(IEnumerable<LocalNotification> plan, CancellationToken cancellationToken = default);

    /// <summary>What is still to come, as the device has it.</summary>
    Task<IReadOnlyList<LocalNotification>> PendingAsync(CancellationToken cancellationToken = default);

    Task CancelAllAsync(CancellationToken cancellationToken = default);
}

public sealed record LocalNotification
{
    /// <summary>Derived from what the notification is about, not generated: re-planning replaces.</summary>
    public required string Id { get; init; }
    public required DateTimeOffset At { get; init; }
    public required string Title { get; init; }
    public string? Body { get; init; }

    /// <summary>The page to open, the same <c>spine.route</c> a push carries.</summary>
    public string? Route { get; init; }

    /// <summary>The channel id from <see cref="SpinePushOptions.AddChannel"/>; the first one when null.</summary>
    public string? Channel { get; init; }

    /// <summary>Anything else the handler should see when it is opened.</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }
}
```

**Varför `SyncAsync` och inte `Schedule`/`Cancel` per notis.** Det är formen som visat sig fungera i Orientera: planen byggs om från data som ändå lästs, enheten görs lika med den, och allt annat avbokas. En tävling som flyttades eller avanmäldes slutar notifiera i stället för att fyra från ett gammalt schema. Ett diff-API vore en till plats där ett inaktuellt schema kan överleva.

**Varför ingen `ShowAsync` för "visa nu" i v1.** Ingen av de kända användningarna behöver den — appen kör ju, och kan visa vad den vill i sitt eget UI. Den kan läggas till när något faktiskt kräver den; att lägga till en metod är billigare än att ta bort en.

`PendingAsync` finns för att en app ska kunna visa "det här kommer" utan att räkna om planen, och för att göra samplet observerbart.

---

## 4. Markören: lokalt eller push

Appen ska kunna skilja dem åt — annars kan en handler inte logga vägen, och en app som både schemalägger och tar emot push kan inte felsöka vilken halva som levererade. Men skillnaden får inte kosta något för den som inte bryr sig.

**Förslag:** en nyckel i data-påsen, inte ett fält vid sidan om.

```csharp
// PushKeys
public const string Source = "spine.source";
public static class Sources { public const string Local = "local"; }

// PushMessage
public bool IsLocal => Data.GetValueOrDefault(PushKeys.Source) == PushKeys.Sources.Local;
```

Skälet är processgränsen. På Android går en öppnad notis genom intent-extras och läses tillbaka med `PushNotifications.Read`; på iOS genom `UserInfo` och `PushPayload.Read`. En C#-egenskap vid sidan om data-påsen hade tappats i båda fallen och behövt en egen väg tillbaka. En nyckel överlever gratis, precis som `spine.route` gör.

`PushKind` rörs inte. En lokal notis *är* en `Alert`; det som skiljer är vem som skickade den, inte vad den är.

---

## 5. Tillstånd, och appen som bara notifierar lokalt

`IPushService.RequestPermissionAsync` blir det enda tillståndsanropet. Två saker måste rättas för att det ska hålla för en app utan backend:

1. **`RegisterForRemoteNotifications` körs villkorslöst** när tillstånd ges (`ApplePushPlatform.iOS.cs:55`) och vid start (`SpinePushExtensions.iOS.cs:48`). Utan `aps-environment` misslyckas den och loggar. Förslag: hoppa över fjärrregistreringen när `SpinePushOptions.Backend` är `null` — appen har ändå ingen att registrera sig hos.
2. **Byggkedjan kräver Firebase.** `_SpinePushAndroidCheck` felar utan `google-services.json`, och `SpinePushEnabled=false` stänger av entitlements och kontrollen — men också, i dag, hela paketets plattformsdel. Förslag: en egenskap till, `SpinePushRemote` (default `true`), där `false` behåller lokala notiser och tar bort Firebase-kravet och `aps-environment`. `SpinePushEnabled=false` fortsätter betyda "inget alls".

Punkt 2 är den enda i hela förslaget som rör MSBuild. Den är värd att göra: annars kostar en lokal notis ett Firebase-projekt. Vad den *inte* tar bort är Firebase-beroendet självt — det följer med paketet oavsett — så Androids `SupportedOSPlatformVersion` 23 står kvar.

---

## 6. Schemaläggning per plattform

### Apple

`UNUserNotificationCenter`, hela pending-mängden ersatt i stället för diffad — planen är billig att bygga om.

En sak görs annorlunda än i Orienteras förlaga: **`UNTimeIntervalNotificationTrigger`, inte `UNCalendarNotificationTrigger`**. `LocalNotification.At` är ett absolut ögonblick; en kalendertrigger byggd av lokala komponenter fyrar på den väggklockan i den tidszon enheten *då* befinner sig i, vilket är fel svar för "en timme innan start" om användaren reser dit. Intervallet från nu är den korrekta avbildningen. Notiser vars tid redan passerat hoppas över i stället för att fyra direkt.

`UserInfo` fylls med `spine.*`-nycklarna, inklusive `spine.source`, så öppningsvägen i §2 fungerar oförändrad.

### Android

`AlarmManager` med inexakta alarm (`Set(AlarmType.RtcWakeup, …)`). Exakta alarm kräver `SCHEDULE_EXACT_ALARM`, som Android delar ut för väckarklockor och kalenderhändelser — ingen av de här notiserna är värd att vara rätt på minuten, de är värda att komma fram.

Alarmet väcker en `BroadcastReceiver` i paketet som postar notisen genom `PushNotifications.Show`, så kanal, ikon och öppna-intent är desamma som för push. Mottagaren deklareras i paketets manifest-overlay, som resten av Spine.Push.

**Det som ska göras bättre än förlagan:** `AndroidNotificationScheduler` håller sin lista över schemalagda id:n i minnet (`_scheduled`), så ett alarm som satts av en tidigare process aldrig avbokas — en notis som planerats bort fyrar ändå efter en omstart av appen. Ramverket persisterar planen (`Preferences`, JSON via en `JsonSerializerContext` som `WidgetJsonContext`), och `SyncAsync` avbokar mot den persisterade mängden.

Persistensen är också vad `PendingAsync` läser på Android: det finns inget API för att fråga `AlarmManager` vad som är satt. På Apple läses systemet, som är sanningen där.

**Omstart av enheten nollställer alarm.** Alternativen är `RECEIVE_BOOT_COMPLETED` med en mottagare som lägger tillbaka planen, eller att låta appens nästa start göra det. Förslag: **boot-mottagaren**, eftersom en app som notifierar om något i morgon bitti annars tystnar av en omstart i natt, och appen inte har någon anledning att öppnas emellan. Behörigheten är normal och kräver ingen dialog.

---

## 7. Dubbleringen, och gränsen mot appen

Ramverket kan inte veta vad appen vill dubblera — bara appen vet vilka av dess notissorter backend också skickar. `IPushService.IsRegistered` är signalen, och mönstret som fungerat är fyra rader:

```csharp
var plan = MyPlanner.Plan(state);                       // appens domän
if (_push.IsRegistered) plan = [.. plan.Where(n => !PushedKinds.Contains(n.Kind))];
await _local.SyncAsync(plan);
```

Det stannar i dokumentationen. En ramverksabstraktion för det skulle behöva ett begrepp för "sort" som ramverket inte har och inte bör skaffa: `NotificationKind` är Orienteras domän, inte Spines.

Gränsen blir alltså: **Spine äger tillstånd, kanaler, schemaläggning, presentation och mottagning. Appen äger vad som ska notifieras och när.**

---

## 8. Plattformar

| | Läge |
|---|---|
| iOS | Fullt. Förgrundspresentation genom `OnReceivedAsync`, som för push. |
| Mac Catalyst | **Inget.** `Platforms/iOS/**` kompileras inte för maccatalyst, så paketet har ingen Apple-implementation där — det gäller push i dag också, trots vad wikin påstår. `IsSupported` är `false`. |
| Android | Fullt. Inexakta alarm, boot-mottagare, notisen ritad av `PushNotifications.Show`. |
| Windows | `UnsupportedLocalNotifications` säger nej, som `UnsupportedPushPlatform` gör för push. Kan bli `AppNotificationBuilder` + `ToastNotifier` senare. |

---

## 9. Vad som inte går, och vad som inte är verifierat

1. **Inexakta alarm är inexakta.** Android kan skjuta upp dem i doze; "dags att åka" kan komma några minuter sent. Det är rätt avvägning, men det ska stå i wikin så att ingen bygger en tidtagning på det.
2. **Ingen leveransgaranti efter force-quit på iOS** — men till skillnad från tysta pushar fyrar ett schemalagt `UNNotificationRequest` ändå, eftersom systemet äger det. Det är just därför lokalt är en reservväg för push och inte tvärtom.
3. **64 pending-notiser per app på iOS.** En plan större än så kapas av systemet, tyst. `SyncAsync` bör sortera på tid och skicka de närmaste — och logga när planen är större.
4. **Catalyst-beteendet är oprövat här.** Som allt Apple-nära i det här repot kan det inte verifieras utan enhet och profil (se `ios-build-environment`).
5. **Tidszonsbytet** i §6 är ett resonemang, inte ett mätvärde; värt ett test på enhet innan det skrivs som sanning i wikin.
6. **`SpinePushRemote`-läget** rör targets som redan är känsliga (entitlements slås ihop med Widgets). Ändringen ska verifieras på båda plattformarna och med båda paketen installerade.

---

## 10. Leveransplan

1. **API + Apple** — `ILocalNotificationService`, `LocalNotification`, `PushKeys.Source`/`PushMessage.IsLocal`, `AppleLocalNotifications`, registrering i `UseSpinePush`, `UnsupportedLocalNotifications`.
2. **Android** — alarm, mottagare, persisterad plan, boot-mottagare, manifest-overlay.
3. **Läget utan fjärrpush** — `SpinePushRemote`, och `RegisterForRemoteNotifications` som hoppas över utan backend.
4. **Sample** — en sida i `MauiSpinePushSampleApp` som schemalägger, listar pending, avbokar, och loggar i `PushLog`; en rad som visar `IsLocal` när notisen öppnas.
5. **Wiki** — avsnitt i `docs/wiki/push.md` om lokala notiser, gränsen mot appen, och dubbleringsmönstret.
6. **Orientera** — [#222](https://github.com/jonatansoderberg/Maui.Spine/issues/222): `INotificationScheduler` och dess två plattformsklasser tas bort, `NotificationPlanner` och `PushTags` behålls. Det är beviset på att API-ytan bär, och det är därför det är ett eget steg och inte en fotnot.

---

## 11. Avvikelser i implementationen

1. **Mac Catalyst blev inte stött**, tvärtemot §8 rev 1. `Platforms/iOS/**` kompileras inte för `net10.0-maccatalyst`, vilket betyder att Spine.Push saknar Apple-implementation där redan i dag. Att rätta det är sitt eget arbete — det handlar om push på Catalyst, inte om lokala notiser — så `IsSupported` är `false` och `UnsupportedLocalNotifications` svarar.
2. **Runtime styrs av `Backend`, inte av en andra egenskap.** `SpinePushRemote` är byggflaggan; vid körning räcker det att `SpinePushOptions.Backend` är `null`, eftersom registrering ändå är omöjlig utan den. Ingen ny runtime-egenskap behövdes.
3. **`NextTriggerDate` går inte att läsa tillbaka.** På en `UNTimeIntervalNotificationTrigger` svarar den *nu plus intervallet* varje gång den läses, så en väntande notis såg ut att flytta sig längre bort ju oftare man tittade. Ögonblicket följer därför med i nyttolasten (`spine.at`) och läses därifrån.
4. **En bugg i push rättades på vägen.** Notis-id:t för en `CollapseId` räknades med `string.GetHashCode`, som saltas per process — så en collapse-id ersatte bara en notis som postats av samma körning av appen. `PushNotifications.StableId` (FNV-1a) används nu både där och för alarmens request-koder, som har samma krav.

---

## 12. Verifierat på enhet

Android (emulator, API 36): tillståndsdialogen kommer en gång och från push-tjänsten; planen skrivs till `Preferences`; alarmen bokas (`*walarm*:plugin.maui.spine.push.LOCAL_NOTIFICATION` i `dumpsys alarm`, med det inexakta fönstret synligt som `window=+11s`); i förgrunden frågas handlern och svarar `None`, så inget postas; i bakgrunden postas notisen på kanalen `news`; ett tryck på den ger `opened (lokal) route: log` och navigerar dit.

iOS (simulator, iOS 26.4): planen läses tillbaka med rätt tider som står stilla mellan avläsningar; en notis som fyrat faller ur planen; i förgrunden går den genom `OnReceivedAsync` och loggas som `Alert (lokal)`; i bakgrunden visas den som banner. Trycket på bannern gick inte att få till med simulatorns syntetiska tapp — vägen dit är densamma som push redan använder (`DidReceiveNotificationResponse`), och nyttolasten är bevisat intakt genom förgrundsleveranserna.
