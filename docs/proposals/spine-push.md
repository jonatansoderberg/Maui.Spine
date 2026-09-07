# Spine.Push — remote push från Spine, klient och server (förstudie, rev 1)

**Status:** Proposal — inget implementerat. Issues per leveranssteg i §11: [#175](https://github.com/jonatansoderberg/Maui.Spine/issues/175) (projektstruktur), [#176](https://github.com/jonatansoderberg/Maui.Spine/issues/176) (server), [#177](https://github.com/jonatansoderberg/Maui.Spine/issues/177) (klient), [#178](https://github.com/jonatansoderberg/Maui.Spine/issues/178) (Orientera), [#179](https://github.com/jonatansoderberg/Maui.Spine/issues/179) (v2).
**Fråga:** Kan Spine göra remote push lika enkelt som widgets blev: registrering, rättigheter, taggar, popup- och tysta notiser, koppling till Live Activities och widgets, på alla plattformar Spine stödjer — och kan ett litet .NET-bibliotek på servern skicka allt detta utan att appen behöver veta hur APNs, FCM och WNS skiljer sig?
**Svar:** Ja, med tre paket: `Plugin.Maui.Spine.Push` i appen, `Plugin.Maui.Spine.Server` på servern och det delade `Plugin.Maui.Spine.Common`. Rekommendationen är att **skicka direkt till APNs, FCM v1 och WNS** från serverbiblioteket och äga enhetsregistret själv, i stället för Azure Notification Hubs. Skälen står i §3; kortversionen är att ANH inte kan skicka de pushtyper Spine redan behöver (Live Activity, iOS 26-widgetar, broadcast-kanaler), inte når MAUI-appar på Windows och inte fått en SDK-release sedan februari 2024.

---

## 1. Slutsatsen i korthet

| Fråga | Svar | Belägg |
|---|---|---|
| Azure Notification Hubs eller eget? | **Eget**, direkt mot plattformarna, med enhetsregistret i en tabell. ANH kan bli en valfri transport senare om någon kräver det. | ANH kan inte sätta `apns-topic` per anrop, så Live Activity- och widget-push kräver ett separat hub per pushtyp och är officiellt "not supported". ANH:s WNS-stöd kräver Partner Center-identitet, som Windows App SDK-push inte har. SDK 4.2.0 är från feb 2024. Telemetri kostar $200/mån (Standard). §3, [1][3][8][23] |
| Kan Spine ta hand om registrering och rättigheter utan plattformskod i appen? | **Ja.** MAUI:s lifecycle-API saknar hooks för remote notifications på iOS (bara `FinishedLaunching`, `PerformFetch`, `SceneOpenUrl` m.fl. finns), så paketet lägger till delegatmetoderna på appens `AppDelegate`-klass i runtime med `class_addMethod`, samma objc-runtime-väg som widgetbryggan. Android-tjänsten och manifestposterna kommer från paketet via samma manifest-overlay som widgets. | Kontroll av `Microsoft.Maui.dll` 10.0.50 (net10.0-ios26.0): inga `RemoteNotification`-symboler. `Plugin.Maui.Spine.Widgets.targets` gör redan overlay + `PartialAppManifest`. |
| Taggar? | Ja, med ANH:s uttryckssyntax (`&&`, `||`, `!`, parenteser) men utvärderat på **vår** server, så gränsen på 20 taggar per uttryck försvinner. Appen sätter taggar; servern kan lägga till egna (t.ex. `user:<id>`) när den tar emot registreringen. | §5.3 |
| Popup och tysta notiser? | Båda. Ett meddelande på servern blir `alert` på APNs, data-meddelande på FCM, toast eller raw på WNS. Förgrund på iOS styrs av handlerns returvärde (`Banner`, `List`, `None`). Tysta: `content-available` + `apns-push-type: background` respektive data-only med hög prioritet. | §5.2, [6][13][28] |
| Live Activities och widgets? | Live Activities: tokens finns redan i Spine.Widgets; Spine.Push skickar upp dem och servern har `UpdateLiveActivityAsync(target, layout)`. Android 16 Live Updates drivs av samma data-meddelande. Widgets: iOS 26 har `WidgetPushHandler` + `apns-push-type: widgets`; Android och Windows via data-/raw-push som kör `IWidgetService.RefreshAsync`. | §6, [14][24][33][46] |
| Cross-platform? | iOS och Android fullt. Mac Catalyst: samma APNs-kod, entitlement-nyckeln behöver verifieras. Windows: WNS via Entra ID; opaketerad app får bara förgrundsleverans. | §7 |

Det som **inte** går: ingen push i iOS-simulatorn (bara `xcrun simctl push` lokalt), ingen garanti om leverans av tysta pushar (båda plattformarna stryper), inga Live Activities på Catalyst, ingen bakgrundsaktivering på opaketerad Windows.

---

## 2. Vad som finns i Spine i dag

| Del | Läge | Konsekvens för push |
|---|---|---|
| `Plugin.Maui.Spine.Widgets`: Live Activity-tokens | Bryggan lyssnar på `pushToStartTokenUpdates` och per aktivitet; C# läser `GetPushToStartTokenAsync` / `GetPushTokenAsync`. Kräver push-entitlement, som ingen sample har. | Spine.Push levererar entitlementen och skickar upp tokens automatiskt. |
| `LiveActivityLayout.ToJson()`, `WidgetTimeline.ToJson()` | Finns, men projektet bygger bara plattforms-TFM:er (`net10.0-ios` osv). Wikin påstår att "the plugin's model project has no platform dependency" — det stämmer inte i dag. | **Förutsättning:** bryt ut `Core/` + `Serialization/` till `Plugin.Maui.Spine.Common` (`net10.0`, ingen MAUI-referens), som widgetpaketet, pushpaketet och `Plugin.Maui.Spine.Server` refererar. |
| Bakgrundskörningar (`BGAppRefreshTask`, alarm) | Finns. | Används för att skicka upp tokens som roterat medan appen låg nere. |
| Lifecycle-koppling i paket | `ConfigureLifecycleEvents` i `SpineWidgetsExtensions.iOS.cs`. | Samma mönster för `UseSpinePush`. |
| MSBuild-targets | Skriver `PartialAppManifest`, `Host.entitlements`, Android-manifest-overlay. | Spine.Push behöver detsamma: `aps-environment`, `UIBackgroundModes: remote-notification`, FCM-tjänsten i manifestet. |
| Orientera | Lokala notiser (`INotificationScheduler`), `POST_NOTIFICATIONS` i manifestet, per-typ-preferenser, Azure Functions-backend utan push. | Drivaren: preferenserna blir taggar, backendens LiveResults-poller blir avsändare. §8 |

---

## 3. Leveransväg: ANH, FCM som paraply, eller direkt

### 3.1 Jämförelse

| | Azure Notification Hubs | FCM som enda backend (även iOS) | Direkt: APNs + FCM v1 + WNS |
|---|---|---|---|
| Registrering/taggar | Installations-API med taggar (max 60/installation) och tagguttryck (max 20 OR / 10 AND / 6 blandat) | Topics (max 5 per villkor) eller egen tabell | Egen tabell; uttryck utan gräns |
| APNs `alert`/`background` | Ja | Ja (via `apns`-block) | Ja |
| Live Activity (`liveactivity`) | **Nej officiellt.** Hack: separat hub med bundle-id `<app>.push-type.liveactivity` [8][9] | Ja, `apns.live_activity_token` — men kräver **Firebase iOS SDK i appen** för en FCM-token [14] | Ja, en header |
| iOS 26 widget-push (`widgets`) | Odokumenterat; samma hack antagligen | Odokumenterat | Ja |
| Broadcast-kanaler (iOS 18, `/4/broadcasts`) | Nej | Inget belägg | Ja |
| Windows (MAUI = Windows App SDK) | **Nej** — kräver Partner Center Package SID, som WinAppSDK-push inte har [23] | Nej — FCM saknar Windows-klient [16] | Ja, WNS + Entra ID [18] |
| Mac Catalyst | Som iOS | Firebase-SDK:n stöder inte Catalyst fullt ut | Som iOS |
| Telemetri/kvitton | Bara på Standard ($200/mån) | Ingen | Svaret per anrop: `BadDeviceToken`, `Unregistered`, 410 → vi rensar själva |
| Underhållsläge | SDK 4.2.0 feb 2024, sista bloggpost sep 2024 [1][2] | `FirebaseAdmin` 3.6.0 juli 2026, aktivt [11] | Apple/Google/Microsoft-API:er, stabila i år |
| Kostnad | Free 1M/500 enheter, Basic $10, Standard $200 [10] | Gratis | Gratis; en tabell i Storage |
| Kod att skriva | Liten, men hacks för allt utöver alert | Liten på Android, stor på iOS (Firebase-binding) | ES256-JWT + HTTP/2 mot APNs (~150 rader), `FirebaseAdmin` mot FCM, OAuth2 client credentials mot WNS |

### 3.2 Beslut

**Direkt.** De delar som ANH löser — tokenlagring, utsändning, taggmatchning — är en tabell och en loop i vår skala, och det ANH inte löser är precis det Spine särskiljer sig med: Live Activities, widgets, Windows. Klientkontraktet (installations-id, plattform, handle, taggar) hålls formmässigt lika ANH:s `Installation`, och `IPushTransport` är en pluggbar seam, så en `Plugin.Maui.Spine.Server.AzureNotificationHubs` kan skrivas senare utan att röra appen.

Detta är också vad Microsoft själva pekar på för MAUI på Windows ("send directly via WNS with Entra ID", mars 2026) [23], och vad Expo gör för Live Activities (direkt mot APNs, inte via Expo Push) [42].

Mot FCM som paraply talar dessutom att Firebase-SDK:n på iOS drar in en binding på tiotals MB, kräver `GoogleService-Info.plist` och swizzlar `AppDelegate` — motsatsen till Spines "inget Xcode, inga bindningsprojekt".

---

## 4. Arkitektur

```
┌──────────── app (Plugin.Maui.Spine.Push) ────────────┐        ┌──────── server (Plugin.Maui.Spine.Server) ────────┐
│ IPushService        rättighet, token, taggar, status │  HTTP  │ MapSpinePush()   PUT/DELETE /installations │
│ IPushHandler        mottaget, öppnat, token-byte     │ ─────▶ │ IPushInstallationStore  tabell + uttryck   │
│ iOS: delegatmetoder via class_addMethod              │        │ IPushSender      SendAsync, SendSilentAsync,│
│ Android: SpinePushMessagingService (manifest-overlay)│ ◀───── │   UpdateLiveActivityAsync, RefreshWidgets  │
│ Windows: PushNotificationManager (Entra remote id)   │  push  │ IPushTransport   Apns | Fcm | Wns | (Anh)  │
│ Spine.Widgets-tokens skickas upp automatiskt         │        │ PushPayloads     ett meddelande → tre format│
└──────────────────────────────────────────────────────┘        └────────────────────────────────────────────┘
```

Paketen efter [#175](https://github.com/jonatansoderberg/Maui.Spine/issues/175) (namnen beslutade 2026-09-07: hela repot under `Plugin.Maui.Spine.<Vad>`, där prefixet betyder avsändare och inte beroende; roten `Spine` är upptagen på NuGet av en orelaterad animationsruntime och inget av Spines paket är ännu publicerat):

| Paket | Skikt | TFM | Beror på | I dag |
|---|---|---|---|---|
| `Plugin.Maui.Spine` | app, kärnan | ios, maccatalyst, android, windows | `Plugin.Maui.Spine.Svg` | oförändrat |
| `Plugin.Maui.Spine.Widgets` | app, kräver kärnan | ios, maccatalyst, android, windows | kärnan, `Common` | minus `Core/` och `Serialization/` |
| `Plugin.Maui.Spine.Push` | app, kräver kärnan | ios, maccatalyst, android, windows | kärnan, `Common`, valfritt Widgets | nytt (#177) |
| `Plugin.Maui.Spine.Common` | delat app och server | net10.0, ingen MAUI-referens | inget | nytt, utbrutet ur Widgets (#175) |
| `Plugin.Maui.Spine.Server` | server | net10.0 | `Common`, `FirebaseAdmin` | nytt (#176) |
| `Plugin.Maui.Spine.Svg` | fristående | ios, maccatalyst, android, windows | SkiaSharp, Svg.Skia | `Plugin.Maui.SvgImage` + `Plugin.Maui.SvgIcon` |
| `Plugin.Maui.Spine.Controls.HeroCollectionView` | fristående kontroll | ios, maccatalyst, android, windows | `Svg`, SkiaSharp | `Plugin.Maui.SpineControls` |
| `Plugin.Maui.Spine.Controls.AnimatedLabel` | fristående kontroll | ios, maccatalyst, android, windows | SkiaSharp | `Plugin.Maui.AnimatedLabel` |

Beroendegrafen går bara nedåt: kärnan beror på Svg, Widgets och Push på kärnan och Common, Server på Common. Inget fristående paket beror på kärnan, och Common beror på ingenting, vilket är det som gör att en Azure Functions-backend kan referera det. Kontrollpaketen delar namnrymden `Plugin.Maui.Spine.Controls`, ett paket-id per kontroll men en `using`.

Push-delen av det:

| Paket | Innehåll |
|---|---|
| `Plugin.Maui.Spine.Push` | `UseSpinePush`, `IPushService`, `IPushHandler`, plattformslager, `build/*.targets` |
| `Plugin.Maui.Spine.Common` | Det plattformsneutrala som app och server delar: widgetarnas trädmodell och serialisering (flyttas från Widgets), `PushInstallation`, `PushMessage`, JSON-nycklar, tagguttryckets parser. |
| `Plugin.Maui.Spine.Server` | `AddSpinePush`, transporter, register, endpoints (Minimal API + Azure Functions-hjälpare). Refererar `Common` för `LiveActivityLayout`. |

Enhetsregistret ligger på servern, inte hos en tredje part: appen känner bara sin egen backend, och backenden bestämmer vilka taggar en klient får sätta.

---

## 5. Klient-API

### 5.1 Registrering

```csharp
builder.UseSpinePush(o =>
{
    o.Backend = new Uri(backendAddress, "push/");            // Plugin.Maui.Spine.Server-endpoints
    o.Permission = PushPermissionMode.WhenAsked;             // eller Provisional / AtLaunch
    o.Channels.Add("competitions", "Tävlingar", PushChannelImportance.Default);
    o.Channels.Add("live", "Live", PushChannelImportance.Low);
    o.UseHandler<OrienteraPushHandler>();
});
```

```csharp
public interface IPushService
{
    PushStatus Status { get; }                     // NotDetermined, Denied, Authorized, Provisional, Unsupported
    string? InstallationId { get; }                // stabilt per installation, i SecureStorage
    IReadOnlyCollection<string> Tags { get; }

    Task<PushStatus> RequestPermissionAsync(CancellationToken cancellationToken = default);
    Task SetTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);
    Task AddTagsAsync(params string[] tags);
    Task RemoveTagsAsync(params string[] tags);
    Task RefreshAsync(CancellationToken cancellationToken = default);   // registrera om nu
    Task UnregisterAsync(CancellationToken cancellationToken = default);
    Task OpenSettingsAsync();                      // appens notisinställningar i OS:et
}
```

Vad paketet gör utan att appen ber om det:

- **Registrerar vid varje start och förgrund** — Apple säger uttryckligen att token aldrig ska cachas utan hämtas varje start [25]; Android-token byts i debug-byggen; WNS-kanaler går ut efter 30 dagar [34]. Registreringen (`PUT /installations/{id}`) skickas bara när något ändrats (token, taggar, appversion, OS-version, Live Activity-tokens), annars en gång per dygn för att förnya `ExpiresAt`.
- **Skiljer på "får skicka" och "har token":** på iOS kan `RegisterForRemoteNotifications` köras före rättighetsfrågan, så tysta pushar och Live Activity-uppdateringar fungerar även när användaren sagt nej till banners. `Status` speglar `UNNotificationSettings` respektive `AreNotificationsEnabled`.
- **`Provisional`** (iOS 12+): ingen dialog, notiserna landar tyst i Notification Center med "Behåll/Stäng av". På Android motsvaras det av att inte fråga alls förrän `RequestPermissionAsync` anropas. `AtLaunch` frågar i `FinishedLaunching`; avrådes i docs men finns för de appar där notiser är hela poängen.
- **Kanaler** skapas vid start med angiven vikt. Vikten kan inte höjas efteråt (användaren äger den) [29], därför deklareras de i options och inte ad hoc. Servern väljer kanal per meddelande med nyckeln `spine.channel`; okänd kanal faller tillbaka på den första.
- **Skickar upp Spine.Widgets-tokens** om `UseSpineWidgets` är på: push-to-start-token och per aktivitet (`kind` → token) läggs i installationen. Ingen kod i appen.

### 5.2 Mottagning

```csharp
public interface IPushHandler
{
    /// Förgrund, eller tyst push i bakgrunden. Returvärdet styr vad iOS visar när appen är öppen.
    Task<PushPresentation> OnReceivedAsync(PushMessage message, PushContext context);

    /// Användaren tryckte på notisen (eller en knapp i den). Körs på huvudtråden, även vid kallstart.
    Task OnOpenedAsync(PushMessage message, string? action);
}

public sealed record PushMessage(
    PushKind Kind,                                  // Alert, Silent, LiveActivity, Widget
    string? Title, string? Body,
    string? Route,                                  // t.ex. "competition/59691" — Spine-navigering
    IReadOnlyDictionary<string, string> Data,
    string? Channel, string? CollapseId);

public sealed record PushContext(bool IsForeground, bool IsColdStart, DateTimeOffset ReceivedAt, CancellationToken Deadline);

[Flags] public enum PushPresentation { None = 0, Banner = 1, List = 2, Sound = 4, Badge = 8 }
```

- **Popup + in-app.** Ett och samma `alert`-meddelande: i bakgrunden visar OS:et det; i förgrunden får handlern det och svarar `None` (visa själv i appen), `List` (bara i Notification Center) eller `Banner | Sound`. Standard utan handler är `Banner | Sound | List` — det Orienteras `ForegroundPresenter` redan gör.
- **Tyst.** `PushKind.Silent` når `OnReceivedAsync` med `IsForeground = false` och en `Deadline` på ~25 s (iOS ger 30, FCM ~20). Handlern synkar och returnerar. På iOS kräver det `UIBackgroundModes: remote-notification`, som targeten skriver.
- **Tapp → navigering.** `Route` är en Spine-route som `INavigationService` kan öppna; paketet levererar den även från kallstart (`launchOptions`, intent-extras) efter att Spine-hosten skapats, så handlern kan navigera direkt.
- **Android-notisen ritas av paketet**, från data-meddelandet: titel, text, `BigTextStyle`, kanal, liten ikon (`SpinePushIcon`-property, default appens `appiconfg`), `PendingIntent` med `Route`. Servern skickar alltså **data-only med hög prioritet** på Android, inte FCM:s `notification`-block, så förgrund och bakgrund beter sig likadant och kanalen väljs av oss. Det är samma val Shiny och OneSignal gjort; priset är att Doze kan fördröja normalprioriterade meddelanden [13][28].

### 5.3 Taggar

Taggar är strängar som i ANH (`[A-Za-z0-9_@#.:-]`, ≤120 tecken). Konvention med prefix, som Orientera:

```
user:121330   club:124   district:12   competition:59691
kind:results-published   kind:live-started   lang:sv   platform:ios
```

`platform:*`, `os:*`, `app:<version>` sätts av paketet. Servern får ett policy-hook — `o.AllowTags = (installation, tags) => …` — så en klient inte kan tagga sig som någon annans `user:`.

---

## 6. Live Activities och widgets

### 6.1 Live Activities (iOS)

Kedjan finns till hälften. Spine.Push lägger till:

1. Push-entitlementen och `UseSpineWidgets(o => o.LiveActivityPushTokens = true)` blir default när båda paketen används.
2. Tokens skickas upp i installationen: `liveActivities: { pushToStart: "<hex>", activities: { "din-start:59691": "<hex>" } }`. Tokens roterar; de skickas vid start, förgrund, bakgrundskörning och när bryggan rapporterar en ny.
3. Servern: 

```csharp
await _push.UpdateLiveActivityAsync(
    PushTarget.Tags("user:121330"), kind: "din-start:59691",
    MyStartActivity.Layout(competition, start, now),        // samma C# som appen
    LiveActivityEvent.Update, staleAt: now.AddMinutes(10));

await _push.StartLiveActivityAsync(PushTarget.Tags("user:121330"), kind: "din-start:59691", layout,
    alert: new("Din start", "Startar om 30 min"));          // push-to-start, iOS 17.2+
```

Servern sätter `apns-push-type: liveactivity`, `apns-topic: <bundle>.push-type.liveactivity`, `timestamp`, `event`, `content-state.json` (dokumenterat format i wikin), `stale-date`, `dismissal-date`. Prioritet 5 som default; 10 bara när anroparen ber om det, eftersom 10 räknas mot timbudgeten [24]. `SpineWidgetsFrequentUpdates` höjer budgeten.

**Android 16 Live Updates:** samma anrop skickar ett data-meddelande `spine.kind=liveactivity`, `spine.activity=<kind>`, `spine.layout=<json>` med hög prioritet; `SpinePushMessagingService` anropar `ILiveActivityService` (`StartAsync`/`UpdateAsync`/`EndAsync`) i appens process. OneSignal gör exakt detta i produktion, appen behöver inte vara i förgrunden [21]. Payloadtaket är 4 KB på båda plattformarna, så layouten måste vara kompakt; `LiveActivityLayout.ToJson()` får en minifierad form.

**Broadcast-kanaler (iOS 18+):** en aktivitet kan prenumerera på en kanal (`pushType: .channel(id)`) och servern skickar ett anrop till alla, i stället för en push per token — rätt modell för "alla som följer tävling X" [26][27]. Kräver kanalhantering på servern (`POST /1/apps/{bundle}/channels`, max 10 000 kanaler) och en `channelId`-parameter i `StartAsync`. Version 2.

### 6.2 Widgets

| Plattform | Mekanism | Vad Spine.Push gör |
|---|---|---|
| iOS 26 | `WidgetPushHandler.pushTokenDidChange` i extensionet, `apns-push-type: widgets`, `{"aps":{"content-changed":true}}` [46] | Extensionet får push-entitlement och skriver token till App Group; appen skickar upp den som `widgetToken`. `RefreshWidgetsAsync(target)` på servern. Budgeterat av systemet, ersätter inte tidslinjen. Version 2 — kräver att Swift-extensionet byggs med iOS 26-SDK som `pushHandler`. |
| iOS ≤ 25 | Ingen widget-push | `SendSilentAsync` → handlern kör `IWidgetService.RefreshAsync`. Best effort. |
| Android | Data-meddelande `spine.kind=widget`, `spine.widget=<kind>` | Tjänsten kör `IWidgetService.RefreshAsync(kind)`; hög prioritet väcker Doze. |
| Windows | WNS raw | Förgrund: `PushReceived` → `RefreshAsync`. Bakgrund kräver paketering + PFN-mappning [18]. |

Fjärrkällan (`RemoteSource`) kvarstår som det budgetsnålaste sättet att hålla en widget färsk; push är det som gör att ändringen syns *nu*.

---

## 7. Plattformslager

### 7.1 iOS och Mac Catalyst

- **Delegatmetoderna** `application:didRegisterForRemoteNotificationsWithDeviceToken:`, `application:didFailToRegisterForRemoteNotificationsWithError:` och `application:didReceiveRemoteNotification:fetchCompletionHandler:` läggs till på appens `AppDelegate`-klass i `FinishedLaunching` med `class_addMethod` och `[UnmanagedCallersOnly]`-funktioner. Finns metoden redan (appen har egna overrides) lämnas den orörd och en varning loggas med instruktionen att vidarebefordra till `SpinePush.Ios.*`. MAUI:s `MauiUIApplicationDelegate` exporterar inga av dem, så kollisionen är osannolik.
- `UNUserNotificationCenter.Current.Delegate` sätts av paketet (`willPresent`, `didReceive`). Har appen redan en delegat (Orienteras `ForegroundPresenter`) tar paketet över och erbjuder samma beteende via handlern; Orientera tar bort sin.
- **Entitlements:** targeten skriver `aps-environment` i `Host.entitlements` (`development` för Debug, `production` för Release; `SpinePushEnvironment` för att styra). För Catalyst är nyckeln oklar — Apple listar `aps-environment` för iOS och `com.apple.developer.aps-environment` för macOS och ingen av dem nämner Catalyst [36][37]; targeten skriver **båda** på Catalyst tills en enhetstest avgjort saken.
- **Info.plist:** `UIBackgroundModes: remote-notification` via `PartialAppManifest`.
- Token som hex, gemener, utan bindestreck. `IsSupported = false` i simulatorn.

### 7.2 Android

- Paketet innehåller `SpinePushMessagingService : FirebaseMessagingService` (`OnNewToken`, `OnMessageReceived`) och targeten lägger den i manifest-overlayen med `com.google.firebase.MESSAGING_EVENT`, plus `POST_NOTIFICATIONS`. Beroenden: `Xamarin.Firebase.Messaging` 125.1.1 (net10.0-android36) och `Xamarin.GooglePlayServices.Basement` (levererar `GoogleServicesJson`-build-action) [31][32].
- Appen bidrar med **en** rad: `<GoogleServicesJson Include="Platforms\Android\google-services.json" />`. Targeten felar med tydlig text om filen saknas.
- Rättighet via `Permissions.PostNotifications` (finns i MAUI 10) [30]; under API 33 alltid beviljad. `Status = Denied` när `AreNotificationsEnabled()` är falskt även om permission finns (användaren kan stänga av i inställningar).
- Kanaler skapas i `Application.OnCreate`-hooken (`AddAndroid(a => a.OnApplicationCreate(...))`) så de finns innan första meddelandet.

### 7.3 Windows

- `PushNotificationManager.Default.CreateChannelAsync(remoteId)` där `remoteId` är objekt-id:t för Entra-appens service principal; kanal-URI:n är handle [18]. `PushReceived` prenumereras före `Register()`.
- **Opaketerad app (Orienteras `WindowsPackageType=None`)**: stöds, men bara förgrundsleverans; ingen COM-aktivering, ingen bakgrund. Paketerad (MSIX eller "external location") kräver dessutom PFN→AppId-mappning som begärs via e-post till Microsoft och behandlas veckovis. `IsSupported()` är falskt för self-contained och förhöjda processer. Dokumenteras; `Status = Unsupported` i de lägena.
- Toast ritas av WNS (`wns/toast` med XML som servern bygger från titel/text/route); tysta som `wns/raw`.

---

## 8. Server-API

```csharp
services.AddSpinePush(o =>
{
    o.Apple(a =>
    {
        a.TeamId = cfg["Push:Apple:TeamId"]; a.KeyId = cfg["Push:Apple:KeyId"];
        a.PrivateKey = cfg["Push:Apple:PrivateKey"];         // innehållet i .p8
        a.BundleId = "com.companyname.orientera";
        a.Environment = ApnsEnvironment.PerInstallation;     // appen rapporterar sandbox/production
    });
    o.Android(f => f.ServiceAccountJson = cfg["Push:Fcm:ServiceAccount"]);
    o.Windows(w => { w.TenantId = …; w.ClientId = …; w.ClientSecret = …; });
    o.UseAzureTableStore(cfg["Push:Storage"]);               // eller UseInMemoryStore() i test
    o.AllowTags = (installation, tags) => tags.Where(t => !t.StartsWith("user:") || t == $"user:{installation.UserId}");
});
```

```csharp
public interface IPushSender
{
    Task<PushResult> SendAsync(PushTarget target, PushNotification notification, CancellationToken ct = default);
    Task<PushResult> SendSilentAsync(PushTarget target, IReadOnlyDictionary<string, string> data, CancellationToken ct = default);
    Task<PushResult> StartLiveActivityAsync(PushTarget target, string kind, LiveActivityLayout layout, PushAlert alert, LiveActivityOptions? options = null, CancellationToken ct = default);
    Task<PushResult> UpdateLiveActivityAsync(PushTarget target, string kind, LiveActivityLayout layout, LiveActivityEvent @event = LiveActivityEvent.Update, LiveActivityOptions? options = null, CancellationToken ct = default);
    Task<PushResult> RefreshWidgetsAsync(PushTarget target, string? kind = null, CancellationToken ct = default);
}

public sealed record PushNotification
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string? Route { get; init; }
    public string? Channel { get; init; }                     // Android-kanal, iOS thread-id
    public string? CollapseId { get; init; }                  // apns-collapse-id / collapse_key / X-WNS-Tag
    public TimeSpan? TimeToLive { get; init; }                // apns-expiration / ttl / X-WNS-TTL
    public PushPriority Priority { get; init; } = PushPriority.High;
    public PushInterruption Interruption { get; init; } = PushInterruption.Active;  // iOS: passive/active/time-sensitive
    public int? Badge { get; init; }
    public string? Sound { get; init; }
    public IReadOnlyDictionary<string, string> Data { get; init; } = Empty;
    public Action<ApnsPayload>? Apple { get; init; }          // valfri plattformsspecifik justering
    public Action<FcmMessage>? Android { get; init; }
    public Action<WnsToast>? Windows { get; init; }
}

// Mål
PushTarget.Tags("kind:results-published && competition:59691")
PushTarget.Installation(id)
PushTarget.User("121330")            // = Tags("user:121330")
PushTarget.All
```

- **`PushResult`** listar per installation: `Sent`, `Invalid` (token borttagen: `BadDeviceToken`, `Unregistered`, `UNREGISTERED`, 410), `Throttled`, `Failed(reason)`. Det är den telemetri ANH tar $200/mån för.
- **Transporter:** `ApnsTransport` (HTTP/2, `SocketsHttpHandler`, ES256-JWT som förnyas efter 50 min — Apple kräver 20–60 min [17], en anslutning per miljö), `FcmTransport` (`FirebaseAdmin` 3.6, `SendEachForMulticastAsync` i buntar om 500 [12]), `WnsTransport` (client credentials mot `login.microsoftonline.com`, `scope=https://wns.windows.com/.default`). Alla bakom `IPushTransport` så ANH eller OneSignal kan pluggas in.
- **Registret** (`IPushInstallationStore`): `Upsert`, `Delete`, `Query(tagExpression)`, `Prune(olderThan)`, `Invalidate(handle)`. Tagguttrycket parsas till ett predikat och körs över kandidaterna för de plattformar som berörs; Table Storage-implementationen partitionerar på plattform och håller en sekundär tabell tag→installation för de vanliga uttrycken. En Cosmos- eller SQL-implementation är ~100 rader när någon behöver den.
- **Endpoints:** `app.MapSpinePush("/push")` för Minimal API. För Azure Functions (isolated), som Orientera.Backend, finns `SpinePushEndpoints.HandleAsync(HttpRequest)` att anropa från en `[Function]` — samma kontrakt: `PUT /push/installations/{id}`, `DELETE /push/installations/{id}`. Autentisering är backendens sak (hook `o.Authenticate = req => …`); Spine.Push skickar `Authorization` från `o.AuthorizationHeader` i klienten.

### 8.1 Orientera som drivare

- Preferenserna i `NotificationPreferencesStore` blir taggar: `kind:entry-closing`, `kind:pm-published`, … plus `user:<id>` och `competition:<id>` för anmälda tävlingar och `person:<id>` för Min grupp. `NotificationService.RefreshAsync` behåller den lokala planen för det som är tidsstyrt (dags att åka) och anropar `SetTagsAsync` för det som är händelsestyrt.
- Backenden: en timer-function pollar LiveResults/Eventor (cachen finns redan) och skickar `kind:results-published && competition:59691` när resultaten kommer, `UpdateLiveActivityAsync(User(id), "din-start:…")` när en följd löpare stämplar. Det är precis det scenariot wikin i dag säger att "appen ensam inte kan nå på iOS".
- `AppleNotificationScheduler.ForegroundPresenter` ersätts av handlerns returvärde.

---

## 9. Steg för steg: allt som måste registreras och ställas in

Instruktionerna är skrivna för Orientera men gäller alla Spine-appar. Kursiv text är sådant som görs en gång per app, resten per miljö (dev/prod).

### 9.1 Apple (iOS och Mac Catalyst)

1. *App ID:* [developer.apple.com → Certificates, Identifiers & Profiles → Identifiers](https://developer.apple.com/account/resources/identifiers/list). Öppna appens App ID (`com.companyname.orientera`), bocka **Push Notifications**, spara. Gör detsamma för widget-extensionets App ID (`…SpineWidgets`) om iOS 26-widgetpush ska användas.
2. *APNs-nyckel:* Keys → **+** → namn "Orientera push", bocka **Apple Push Notifications service (APNs)** → Continue → Register → **Download** `.p8` (kan bara laddas ner en gång). Anteckna **Key ID** (på nyckelns sida) och **Team ID** (Membership). En nyckel gäller alla appar i teamet, båda miljöerna, alla pushtyper; max två aktiva [17].
3. *Provisioneringsprofiler* regenereras efter steg 1 (automatisk signering i Xcode/VS gör det själv; manuella profiler måste laddas ner igen). Utan ny profil saknas `aps-environment` i signaturen och `didFailToRegister` svarar "no valid aps-environment".
4. **Entitlements i projektet:** Spine.Push-targeten skriver `aps-environment` i `Host.entitlements` (som widgets gör med App Group). Har appen egen `CodesignEntitlements` (Orientera) läggs i stället till:
   ```xml
   <key>aps-environment</key>
   <string>development</string>   <!-- production i Release; targeten varnar om de inte matchar -->
   ```
5. **Servern:** `Push:Apple:TeamId`, `Push:Apple:KeyId`, `Push:Apple:PrivateKey` (hela `.p8`-innehållet, med eller utan PEM-ramar), `Push:Apple:BundleId`. Lägg dem i Key Vault / app settings, aldrig i repot.
6. **Miljö:** ett debug-bygge på enhet ger sandbox-tokens, TestFlight/App Store production-tokens; token från den ena fungerar aldrig i den andra [25]. Klienten rapporterar miljö i installationen så servern väljer värd (`api.sandbox.push.apple.com` / `api.push.apple.com`).
7. **Test utan server:** `xcrun simctl push booted com.companyname.orientera payload.apns` levererar en payload till simulatorn (ingen riktig token behövs) — rätt sätt att testa handlern, `Route` och Live Activity-JSON lokalt. Riktig push kräver fysisk enhet.
8. *Catalyst:* `EnableCodeSigning=true` även i Debug, annars kommer ingen rättighetsdialog [35].

### 9.2 Android (Firebase)

1. *Firebase-projekt:* [console.firebase.google.com](https://console.firebase.google.com) → Add project (Analytics kan stängas av).
2. *Android-app i projektet:* Project settings → Your apps → Android → package name = `ApplicationId` (`com.companyname.orientera`). Ladda ner **`google-services.json`** → `Platforms/Android/google-services.json`, och i csproj:
   ```xml
   <GoogleServicesJson Include="Platforms\Android\google-services.json" Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'" />
   ```
   Ett projekt kan ha flera Android-appar (dev-`applicationIdSuffix`), en JSON täcker alla.
3. *Servicekonto för servern:* Project settings → **Service accounts** → **Generate new private key** → JSON. Det är FCM HTTP v1-identiteten (legacy server key är avvecklad sedan juni 2024). Servern: `Push:Fcm:ServiceAccount` = JSON-innehållet.
4. **Manifest:** targeten lägger tjänsten och `POST_NOTIFICATIONS`. Orientera har redan permissionen; dubbletten mergas bort.
5. **Kanaler och ikon:** deklarera kanaler i `UseSpinePush`; lägg en monokrom `notification_icon` (Orientera har en) och peka `SpinePushIcon` på den — annars ritar Android en vit fyrkant.
6. **Test:** Firebase Console → Messaging → "Send test message" mot en token (loggas i debug) för att se att tjänsten träffas; datanycklarna sätts under "Additional options". Emulatorn med Google Play-image fungerar.
7. *Utan Google Play* (Huawei m.fl.): ingen FCM. `Status = Unsupported`; version 2 kan lägga till HMS bakom samma `IPushTransport`.

### 9.3 Windows (WNS via Entra ID)

1. *App registration:* [portal.azure.com → Microsoft Entra ID → App registrations → New](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade). Namn "Orientera push", **Accounts in any organizational directory (multitenant)** — kravet från WinAppSDK [18]. Anteckna **Application (client) ID** och **Directory (tenant) ID**.
2. *Klienthemlighet:* Certificates & secrets → New client secret → kopiera värdet direkt. Servern: `Push:Wns:TenantId`, `ClientId`, `ClientSecret`.
3. *Service principal-id:* Enterprise applications → sök appen → **Object ID** (inte client id). Det är `remoteId` som appen skickar till `CreateChannelAsync`; sätts i `UseSpinePush(o => o.Windows.RemoteId = …)`.
4. **Paketering:** opaketerad (`WindowsPackageType=None`) ger förgrundsleverans, inget mer. För bakgrund: paketera (MSIX) och mejla PFN + Entra AppId till `Win_App_SDK_Push@microsoft.com` för mappning (veckovis handläggning) [18].
5. **Test:** `curl` med bearer-token mot kanal-URI:n, `X-WNS-Type: wns/toast`. Kanaler går ut efter 30 dagar och 410 tas bort av transporten.

### 9.4 Servern (Orientera.Backend, Azure Functions)

1. `Plugin.Maui.Spine.Server` som PackageReference; `builder.Services.AddSpinePush(o => …)` i `Program.cs` med värdena ovan ur `local.settings.json`/App settings.
2. En function-fil `PushFunctions.cs` med två `[Function]` som vidarebefordrar `PUT`/`DELETE /api/push/installations/{id}` till `SpinePushEndpoints.HandleAsync`.
3. Storage: `Push:Storage` = samma Storage-konto som `AzureWebJobsStorage`; tabellerna `SpinePushInstallations` och `SpinePushTags` skapas vid start.
4. En sändare: timer-trigger som jämför senaste resultat-/startlistestatus i cachen och anropar `IPushSender`.
5. Hemligheter: `.p8`, service-account-JSON och Entra-secret läggs i Key Vault med referenser i App settings. `local.settings.example.json` får nycklarna med tomma värden, som för Eventor.

### 9.5 Appen (MauiProgram)

```csharp
builder.UseSpineWidgets();
builder.UseSpinePush(o =>
{
    o.Backend = new Uri(backendAddress!, "push/");
    o.Permission = PushPermissionMode.WhenAsked;
    o.Channels.Add("competitions", "Tävlingar", PushChannelImportance.Default);
    o.UseHandler<OrienteraPushHandler>();
});
```

och `RequestPermissionAsync()` bakom samma switch i `NotificationSheet` som i dag frågar om lokala notiser.

---

## 10. Hur andra ramverk gör

| Ramverk | Klient-API | Taggar | Tysta | Live Activity / widget | Server | Kommentar |
|---|---|---|---|---|---|---|
| **Shiny.Push 5.5** (aug 2026) [38][39] | `AddPush<TDelegate>()`, `IPushManager.RequestAccess()`, `PushDelegate.OnEntry/OnReceived/OnNewToken` | Bara med ANH/FCM-provider (`Tags.SetTags`) | Ja | Nej | Inget; ANH eller FCM | Närmast Spine i modell. Kräver `IntentFilter` på aktiviteten och egna AppDelegate-overrides. 5.0 lade till macOS, Windows (WNS), Blazor. |
| **Plugin.Firebase.CloudMessaging 4.0** [40] | `CrossFirebaseCloudMessaging.Current`, events `TokenChanged/NotificationReceived/NotificationTapped`, topics | Topics | Ja | Nej | FCM | Firebase-SDK på iOS (stor binding). |
| **Microsofts MAUI+ANH-guide** [15] | Egen `IDeviceInstallationService`/`INotificationRegistrationService`, AppDelegate-exports, egen `FirebaseMessagingService` | ANH-taggar, buntade om 20 | Ja (`Silent`) | Nej | ASP.NET Core → ANH | ~600 rader appkod per app. Ingen Windows. |
| **OneSignal .NET 6.2** [19][20][21] | `OneSignal.Initialize`, `Notifications.RequestPermissionAsync`, `User.AddTag`, `Login(externalId)` | Ja | Ja | **Ja**: `OneSignal.LiveActivities.*`, Android 16 Live Updates via push | SaaS + REST | Bästa funktionsbredden, men SaaS och NSE + App Group krävs. Bevisar att Live Updates via FCM-data fungerar. |
| **Expo Notifications** [41][22] | `getExpoPushTokenAsync`, `setNotificationHandler` (returnerar presentation), `addNotificationResponseReceivedListener`, `setNotificationChannelAsync` | Nej (egen server) | Ja (task manager) | Tokens via `expo-widgets`, skickas **direkt till APNs** | Expo Push Service (tickets/receipts) | Presentationsmodellen ("handler returnerar vad som visas") är den Spine.Push kopierar. |
| **Flutter firebase_messaging** [43] | `getToken/onTokenRefresh`, `requestPermission` (inkl. provisional), `onMessage/onMessageOpenedApp/getInitialMessage`, isolat för bakgrund | Topics | Ja | `live_activities`-plugin ger tokens; native SwiftUI krävs | FCM | Förgrund på Android kräver `flutter_local_notifications` — samma slutsats som vår "paketet ritar notisen". |
| **Capacitor** [44] | `register()`, events `registration/pushNotificationReceived/ActionPerformed`, `createChannel` | Nej | Android ja, **iOS nej** | Nej | Egen | |

Gemensamt för de som fungerar bra: ett **handler-objekt** i stället för lösa events, **presentationen bestäms av handlern**, token-refresh är osynlig, och Android-notisen ritas av biblioteket. Ingen av dem har widget-push; bara OneSignal har Live Activities i .NET.

---

## 11. Leveransplan

**v1 — kärnan (#175, #176, #177, #178)**
1. Projektstruktur ([#175](https://github.com/jonatansoderberg/Maui.Spine/issues/175)): `Plugin.Maui.Spine.Common` (net10.0) bryts ut ur Widgets; kontrollerna får ett paket var under `Plugin.Maui.Spine.Controls.<Vad>` (`HeroCollectionView`, `AnimatedLabel`); `SvgImage` + `SvgIcon` → `Plugin.Maui.Spine.Svg`; wikin rättas. Förutsättning för serverbiblioteket.
2. `Plugin.Maui.Spine.Common` + `Plugin.Maui.Spine.Server`: APNs- och FCM-transport, in-memory- och Table Storage-register, tagguttryck, `MapSpinePush`, Functions-hjälpare, `PushResult`. Testbart utan app: integrationstest mot APNs sandbox med en riktig token.
3. `Plugin.Maui.Spine.Push` iOS + Android: `UseSpinePush`, rättigheter, registrering, handler, kanaler, targets (entitlements, plist, manifest). Live Activity-tokens upp i installationen; `UpdateLiveActivityAsync` på servern. Android Live Update via data-push.
4. Orientera: taggar från preferenserna, handler med navigering, backend-poller som skickar. **Första riktiga milstolpen är en push till en fysisk iPhone från Azure Functions** — det kan inte verifieras på den här maskinen (ingen signeringsidentitet, se `ios-build-environment`).

**v2 — bredd (#179)**
- Windows (WNS/Entra, förgrund; dokumenterad väg till bakgrund) och Mac Catalyst (entitlement verifierad på enhet).
- iOS 26 widget-push (`WidgetPushHandler` i extensionet, `RefreshWidgetsAsync`).
- Broadcast-kanaler för Live Activities (iOS 18).
- Notification Service Extension för bilder/`mutable-content` — i samma `swiftc`-stil som widget-extensionet.

**v3 — valfritt**
- `Plugin.Maui.Spine.Server.AzureNotificationHubs` som transport för de som redan har ett hub.
- Web Push (Blazor) och HMS.

---

## 12. Risker och det som inte är verifierat

1. **`class_addMethod` på `AppDelegate`.** Aldrig gjort i det här repot; objc-runtime-vägen är beprövad (widgetbryggan använder `objc_msgSend`) men delegatmetoder med block-argument (`fetchCompletionHandler:`) måste anropas korrekt från `[UnmanagedCallersOnly]`. Fallback är dokumenterade overrides i appens `AppDelegate` — det alla andra ramverk kräver.
2. **Ingen APNs-miljö här.** Simulatorn ger inga tokens; allt på Apple-sidan verifieras först på enhet med profil. `xcrun simctl push` täcker handler och payload-format.
3. **Catalyst-entitlementen** (`aps-environment` vs `com.apple.developer.aps-environment`) är oklar hos Apple själva; vi skriver båda och testar.
4. **Data-only på Android** beror på att FCM startar processen; vissa OEM:er (batterioptimering) fördröjer eller stoppar normalprioriterade meddelanden. Alert-meddelanden går alltid med hög prioritet; tysta med normal.
5. **4 KB payload** gäller Live Activity-layouten över push. `LiveActivityLayout.ToJson()` behöver en kompakt form; en layout med många noder kan behöva delas i "start med hela layouten lokalt, uppdatera med bara texterna" — i så fall en `LiveActivityLayout.Patch`. Avgörs när Orienteras layout mätts.
6. **ANH-uttryckskompatibilitet** är en avsikt, inte ett löfte: vår parser stödjer samma operatorer men ingen övre gräns; `$InstallationId:`-taggen mappas till `PushTarget.Installation`.
7. **Windows-bakgrund** kräver paketering och ett mejl till Microsoft; opaketerade Spine-appar får bara förgrund. Det är Microsofts begränsning, inte vår.
8. **WidgetKit push-token** — Apple säger inte om den är per extension eller per widget-kind; designen för `RefreshWidgetsAsync(kind)` kan behöva bli "alla widgets".

---

## 13. Referenser

Verifierat lokalt: `Microsoft.Maui.dll` 10.0.50 (inga remote-notification-hooks i `iOSLifecycleBuilder`), `Microsoft.iOS.Ref 26.2` (`UNAuthorizationOptions.Provisional`, `UNNotificationSettings`, `RegisterForRemoteNotifications`; inget WidgetKit-namespace), `Microsoft.Android.Ref 36.1` (`Manifest.Permission.PostNotifications`, `NotificationManager.CanPostPromotedNotifications`, `Notification.ProgressStyle`), `Plugin.Maui.Spine.Widgets` (Live Activity-tokens, targets, lifecycle-mönster).

1. ANH-bloggen (sista inlägg sep 2024): https://devblogs.microsoft.com/azure-notification-hubs/
2. NuGet `Microsoft.Azure.NotificationHubs` 4.2.0 (feb 2024): https://www.nuget.org/packages/Microsoft.Azure.NotificationHubs
3. ANH FAQ (kvitton, 30-minutersregeln, ett hub per miljö): https://learn.microsoft.com/en-us/azure/notification-hubs/notification-hubs-push-notification-faq
4. ANH Installations vs Registrations: https://learn.microsoft.com/en-us/azure/notification-hubs/notification-hubs-push-notification-registration-management
5. ANH taggar och uttryck (60/20/10/6): https://learn.microsoft.com/en-us/azure/notification-hubs/notification-hubs-tags-segment-push-message
6. ANH iOS 13-headers (`apns-push-type`, `apns-priority`): https://learn.microsoft.com/en-us/azure/notification-hubs/push-notification-updates-ios-13
7. ANH VoIP-hacket (separat hub, `apns-topic` kan inte styras): https://learn.microsoft.com/en-us/azure/notification-hubs/voip-apns
8. Microsoft Q&A: Live Activity via ANH "not supported" (2024): https://learn.microsoft.com/en-us/answers/questions/1851974/sending-apns-push-notifications-of-live-activity-t
9. Microsoft Q&A (2023): https://learn.microsoft.com/en-us/answers/questions/1156616/are-live-activity-push-notifications-for-apns-supp
10. ANH-priser: https://azure.microsoft.com/en-us/pricing/details/notification-hubs/
11. NuGet `FirebaseAdmin` 3.6.0: https://www.nuget.org/packages/FirebaseAdmin
12. `SendEachForMulticastAsync`, 500 per anrop: https://firebase.google.com/docs/reference/admin/dotnet/class/firebase-admin/messaging/multicast-message
13. FCM prioritet, collapse, TTL: https://firebase.google.com/docs/cloud-messaging/customize-messages/set-message-type
14. FCM Live Activity (`live_activity_token`): https://firebase.google.com/docs/cloud-messaging/customize-messages/live-activity
15. Microsofts MAUI+ANH-guide: https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/push-notifications
16. Firebase C++ (Messaging stubbad på desktop): https://firebase.google.com/docs/cpp/setup
17. APNs token-baserad anslutning (JWT 20–60 min, max 2 nycklar): https://developer.apple.com/documentation/usernotifications/establishing-a-token-based-connection-to-apns
18. Windows App SDK push (Entra, opaketerat, PFN-mappning): https://learn.microsoft.com/en-us/windows/apps/develop/notifications/push-notifications/push-quickstart
19. OneSignal .NET SDK 6.2: https://www.nuget.org/packages/OneSignalSDK.DotNet
20. OneSignal Live Activities: https://documentation.onesignal.com/docs/en/cross-platform-live-activity-setup
21. OneSignal Android Live Updates via push: https://documentation.onesignal.com/docs/en/android-live-notifications
22. Expo Push Service: https://docs.expo.dev/push-notifications/sending-notifications/
23. Microsoft Q&A (mars 2026): ANH + MAUI Windows omöjligt, skicka direkt via WNS: https://learn.microsoft.com/en-us/answers/questions/5811501/how-do-i-configure-a-notification-hub-to-send-noti
24. ActivityKit push (event, stale-date, budget, `input-push-channel`): https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications
25. Apple: registrera varje start, cacha aldrig token: https://developer.apple.com/documentation/usernotifications/registering-your-app-with-apns
26. APNs kanalhantering: https://developer.apple.com/documentation/usernotifications/sending-channel-management-requests-to-apns
27. APNs broadcast: https://developer.apple.com/documentation/usernotifications/sending-broadcast-push-notification-requests-to-apns
28. FCM Android: notification vs data, förgrund/bakgrund: https://firebase.google.com/docs/cloud-messaging/android/receive
29. Android-kanaler (vikt kan inte höjas): https://developer.android.com/develop/ui/views/notifications/channels
30. MAUI `Permissions.PostNotifications`: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/appmodel/permissions?view=net-maui-10.0
31. NuGet `Xamarin.Firebase.Messaging` 125.1.1: https://www.nuget.org/packages/Xamarin.Firebase.Messaging
32. NuGet `Xamarin.GooglePlayServices.Basement`: https://www.nuget.org/packages/Xamarin.GooglePlayServices.Basement
33. Android 16 Live Updates: https://developer.android.com/develop/ui/views/notifications/live-update
34. WNS-översikt (30 dagars kanaler, två auth-modeller): https://learn.microsoft.com/en-us/windows/apps/develop/notifications/push-notifications/wns-overview
35. .NET iOS/Catalyst user notifications: https://learn.microsoft.com/en-us/dotnet/ios/app-fundamentals/user-notifications
36. `aps-environment`: https://developer.apple.com/documentation/bundleresources/entitlements/aps-environment
37. `com.apple.developer.aps-environment`: https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.aps-environment
38. NuGet `Shiny.Push` 5.5: https://www.nuget.org/packages/Shiny.Push
39. Shiny push-docs: https://shinylib.net/push/
40. Plugin.Firebase.CloudMessaging: https://github.com/TobiasBuchholz/Plugin.Firebase/blob/development/docs/cloud_messaging.md
41. expo-notifications: https://docs.expo.dev/versions/latest/sdk/notifications/
42. Expo widgets/Live Activities (direkt mot APNs): https://expo.dev/blog/ios-widgets-and-live-activities-in-expo
43. Flutter firebase_messaging: https://firebase.google.com/docs/cloud-messaging/flutter/receive
44. Capacitor push: https://capacitorjs.com/docs/apis/push-notifications
45. Apple: provisional authorization: https://developer.apple.com/documentation/usernotifications/asking-permission-to-use-notifications
46. WidgetKit push (iOS 26, `WidgetPushHandler`): https://developer.apple.com/documentation/widgetkit/updating-widgets-with-widgetkit-push-notifications
