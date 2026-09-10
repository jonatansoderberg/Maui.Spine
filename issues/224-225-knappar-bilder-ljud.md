# Issue #224 + #225 — Knappar, bilder och ljud i notiser

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/224 · https://github.com/jonatansoderberg/Maui.Spine/issues/225
**Branch:** issue/224-225-knappar-bilder-ljud
**Status:** Completed

## Plan

En gren för båda issuena — de rör samma filer (`PushNotifications`, `AppleLocalNotifications`, `SpinePushOptions`, serverns `PushNotification`).

### Beslut som redan är fattade (av Jonatan)

- **Bakgrundsknappar och svarsfält ingår**, via en ny metod `IPushHandler.OnActionAsync(message, action, text)` med tom standardimplementation — inget befintligt går sönder.
- **NSE:n ingår**: bilder i push på iOS kräver en Notification Service Extension.
- **En gren, en PR** för båda.

### #224 — Knappar

**Deklaration, som kanaler.** iOS vill ha kategorier registrerade vid start, Android bygger knappar per notis. Samma asymmetri som kanaler, samma lösning:

```csharp
push.AddCategory("entry",
    new PushAction("enter", "Anmäl mig") { OpensApp = false },
    new PushAction("show", "Visa tävlingen"));
push.AddCategory("chat", new PushAction("reply", "Svara") { Reply = "Skriv ett svar" });
```

- `PushAction(Id, Title)` med `OpensApp` (default `true`), `Destructive`, `Reply` (placeholder; satt betyder svarsfält).
- `PushKeys.Category = "spine.category"`. `LocalNotification.Category`. Serverns `PushNotification.Category` → `aps.category` + `spine.category` i data.

**Mottagning — två metoder, en regel.** En knapp som öppnar appen går till `OnOpenedAsync(message, action)`, som redan finns och nu äntligen får ett värde. En knapp som inte öppnar appen, och ett svarsfält, går till nya `OnActionAsync(message, action, text)` — körs i appens process utan UI, som `IWidgetActionHandler`.

- **iOS:** kategorierna registreras med `SetNotificationCategories` i `FinishedLaunching`, bredvid delegaten. `UNNotificationAction` med `.Foreground` när `OpensApp`, `UNTextInputNotificationAction` för `Reply`. I `DidReceiveNotificationResponse` väljs metod efter knappens deklaration; för en bakgrundsknapp anropas `completionHandler` först när handlern är klar, så iOS inte fryser processen mitt i.
- **Android:** `PushNotifications.Show` bygger `AddAction` från deklarationen. `OpensApp` → `PendingIntent.GetActivity` med `spine.action` i extras, läst i `Opened(intent)`. Annars → `PendingIntent.GetBroadcast` till en ny `SpineNotificationActionReceiver`; `Reply` får en `RemoteInput` och en **mutable** `PendingIntent` (API 31 kräver det för `RemoteInput`). Notisen tas bort när handlern är klar — annars snurrar ett svarsfält för evigt.

### #225 — Bilder

`PushKeys.Image = "spine.image"`: en `https`-URL eller, för lokala notiser, en filsökväg.

| | Lokal | Push |
|---|---|---|
| iOS | `UNNotificationAttachment` från en **kopia** av filen — iOS flyttar in bilagan i sitt eget lager, så originalet skulle försvinna. | NSE: `aps.mutable-content = 1`, extensionet hämtar `spine.image` med `URLSession`, lägger till bilagan, och levererar oförändrat om hämtningen misslyckas eller tiden tar slut. |
| Android | `BigPictureStyle` + large icon från filen, i `PushNotifications.Show`. | Samma, men hämtad med `HttpClient` inom FCM:s deadline i `SpinePushMessagingService`. Misslyckas hämtningen visas notisen utan bild. |

**NSE:n** byggs som widget-extensionet: `native/ios/SpineNotificationService.swift` + `build/spine-push-build.sh` (swiftc, `-application-extension`, `_NSExtensionMain`), registrerad som ett andra `AdditionalAppExtensions`. Bundle-id `$(ApplicationId).SpineNotificationService`, egen profil på signerade enhetsbyggen — samma `find_profile` och samma felmeddelande som widgetarna.

**Opt-in: `SpinePushImages=true`.** Default av, trots att NSE:n ingår: den kräver ett eget App ID och en profil per enhetsbygge, och default-på skulle bryta varje befintligt enhetsbygge av en app som använder Spine.Push tills någon skapat dem. Gäller bara när `SpinePushRemote` också är på — en lokal notis behöver ingen NSE.

Server: `PushNotification.Image` (`Uri`) → `spine.image` i data på båda, `mutable-content: 1` på APNs.

### #225 — Ljud

- **Apple, per notis:** `LocalNotification.Sound` → `UNNotificationSound.GetSound(name)`; null är systemljudet. Push har redan `PushNotification.Sound` → `aps.sound`.
- **Android, per kanal:** `AddChannel(..., sound: "ding")` → `SetSound(android.resource://…/raw/ding)`. Oföränderligt efter att kanalen skapats; dokumenteras vid API:t och i wikin. `PushNotification.Sound` gäller inte Android, och det skrivs ut vid egenskapen.

### Samplet och dokumentationen

- Push-samplet: kategorin `sample` med en knapp som öppnar appen, en som inte gör det, och ett svarsfält. Lokalt-sidan får en notis med knappar och en med bild. Skicka-sidan får fält för kategori och bild-URL.
- `docs/wiki/push.md`: avsnitt om knappar, bilder och ljud, med asymmetrierna utskrivna.
- Servertester i `PushPayloadsTests` för `category`, `mutable-content` och `spine.image`.

### Verifiering

- Servertesterna.
- Android-emulator: knappar i skuggan, bakgrundsknapp och svarsfält når `OnActionAsync`, lokal bild visas.
- iOS-simulator: lokala knappar och bild; NSE:n med `xcrun simctl push` och `mutable-content` om simulatorn kör service-extensions — annars står det i PR:en vad som inte är sett.

## Open Questions

Inga. De tre designfrågorna besvarades innan planen skrevs.

## Changes

- `PushKeys.Category` och `PushKeys.Image` i kontraktet.
- Servern: `PushNotification.Category` och `.Image`; `ApnsPayload.Category` och `.MutableContent`; `Fill` skriver `spine.category`/`spine.image` på båda plattformarna och vägrar en bild som inte är `https`. Fyra nya tester i `PushPayloadsTests`.
- `PushAction`, `PushCategory`, `SpinePushOptions.AddCategory`; `PushChannel.Sound` och `AddChannel(..., sound:)`.
- `IPushHandler.OnActionAsync(message, action, text)` med tom standardimplementation; `PushMessage.Category` och `.Image`.
- `LocalNotification.Category`, `.Image`, `.Sound`, burna i nyttolasten så att planen läses tillbaka som den skrevs.
- iOS: kategorierna registreras i `FinishedLaunching`; `DidReceiveNotificationResponse` dirigerar en bakgrundsknapp till `OnActionAsync` och anropar `completionHandler` först när handlern är klar. Lokala notiser får kategori, bilaga (från en kopia) och ljud.
- Android: `PushNotifications` bygger knappar, bild (`BigPictureStyle`) och kanalljud; ny `SpineNotificationActionReceiver` för bakgrundsknappar och svar (`RemoteInput`, mutable `PendingIntent`); `Opened` läser knappen och tar ner notisen; bilden hämtas i både `SpinePushMessagingService` och den lokala mottagaren.
- NSE: `native/ios/SpineNotificationService.swift`, `build/spine-push-build.sh`, tre targets bakom `SpinePushImages`, och `native/**` i paketet.
- Samplet: kategorin `sample` (öppna, kvittera, svara), kanalen `chime` med eget ljud, en rik lokal notis på Lokalt-sidan, fält för kategori och bild på Skicka-sidan, `OnActionAsync` i handlern, `SpinePushImages` för simulatorbyggen. Genererade tillgångar: `sample_picture.png` och `ding.wav`.
- `docs/wiki/push.md`: avsnittet *Buttons, pictures and sound*.

## Verifiering

**Servertester:** 170 gröna, varav fyra nya (kategori, `mutable-content`, `spine.image` på båda, `http` vägras).

**Android (emulator, API 36)** — hela vägen:
- Lokal rik notis: kanal `chime` (eget ljud), tre knappar (*Öppna loggen* → `startActivity`, *Kvittera* och *Svara* → `broadcastIntent`), `BigPictureStyle` med tumnagel.
- *Kvittera* → `action (lokal) ack` i loggen, notisen borta.
- *Svara* med text via `RemoteInput` → `action (lokal) reply: "Hej fran adb"`.
- *Öppna loggen* → appen öppnas på Logg-sidan: `opened (lokal) route: log, action: open`, notisen borta.
- Riktig FCM-push genom sample-servern med `spine.category` och `spine.image`: `sent 1`, ritad med tre knappar och bilden hämtad från nätet (tumnagel 144×96).

**iOS (simulator, 26.4):**
- NSE:n byggs, bäddas in (`PlugIns/SpineNotificationService.appex`) och registreras hos PlugInKit med rätt extension point och principal class.
- **`xcrun simctl push` kör inte service-extensions.** Loggen visar att `CoreSimulatorBridge` lägger notisen direkt i SpringBoard med en `UNPushNotificationTrigger`; steget där iOS startar ett extension finns inte. Notisen kom fram — utan bild. Det är testverktygets gräns, inte kodens, och står nu i wikin.
- **En riktig APNs-push kör NSE:n, i simulatorn också.** Simulatorn på Apple silicon har en riktig sandbox-token. Eftersom den körande sample-servern var den gamla builden (den sätter inte `mutable-content`) startades en andra instans med den nya koden på port 5101, simulatorns installation registrerades där med tokenen ur appens kopiera-knapp, och pushen skickades med `category` och `image`. Enhetsloggen: `SpineNotificationService` startar, *Received replacement content … Handled temporary attachment YES*, SpringBoard *Copied attachment file into repository: identifier spine.image* och *Will deliver mutated notification content*.

**iOS — sett för hand (Jonatan):** den lokala rika notisen som banner på olåst skärm, utfälld — bilden och de tre knapparna syns, och *Kvittera* ger `action (lokal) ack` i Logg. Det bekräftar på iOS att kategorierna registreras, att bilagan kopieras och visas, och att en bakgrundsknapp når `OnActionAsync`.

**iOS — bara i loggen:** NSE-pushens bild. SpringBoard levererade den med bilaga (*attachmentCount=1*), men den notisen fälldes aldrig ut — den fick bara tryck på låsskärmen, *Hinting side swipe … didExecute? NO*, och där visar iOS varken bilaga eller knappar. Simulatorns `Categories.plist` för appen innehåller `sample` med `open` (Foreground), `ack` och `reply` (TextInput) — exakt vad samplet deklarerar.

## Decisions

- **`OnActionAsync` som standardmetod på `IPushHandler`, inte ett nytt interface.** En handler som inte bryr sig slipper implementera något, och knappar som öppnar appen går fortfarande till `OnOpenedAsync` — namnet stämmer då fortfarande.
- **NSE:n är opt-in (`SpinePushImages`).** Default-på skulle kräva ett nytt App ID och en profil för varje befintligt enhetsbygge.
- **`spine.image` är en publik nyckel.** Servern skriver den och appen läser den, samma kontrakt som `spine.route`.
- **Servern vägrar en `http`-bild i stället för att skicka den.** App Transport Security skulle släppa den tyst i extensionet, och en bild som aldrig syns är den svåraste buggen att hitta.
- **En notis kommer alltid fram.** Misslyckad hämtning, en avkodning som inte går och en deadline som löper ut lämnar texten som den var och skriver orsaken i loggen — på alla fyra ställena (NSE, Android-push, Android-lokal, iOS-lokal).
- **Samplet slår på NSE:n bara för simulatorn.** Jonatan bygger push-samplet till enhet; ett nytt App ID och en profil för `…SpineNotificationService` skulle annars krävas innan nästa enhetsbygge går igenom.
- **Samplets ljud får en egen kanal (`chime`).** Att ge `news` ett ljud hade inte märkts på en enhet där kanalen redan finns — exakt den fälla wikin varnar för.
