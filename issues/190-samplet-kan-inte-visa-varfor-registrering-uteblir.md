# Issue #190 — Spine.Push: samplet kan inte visa varför en registrering uteblir

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/190
**Branch:** issue/190-samplet-kan-inte-visa-varfor-registrering-uteblir
**Status:** Completed

## Bakgrund

En genomkörning av `MauiSpinePushSampleApp` mot `MauiSpinePushSampleApp.Server` på en bootad
iPhone 17-simulator och en Pixel 10 Pro-emulator (API 36). iOS registrerade sig, tog emot en
`simctl push` och synkade taggar. Android skapade aldrig någon installation, och taggar tog inte.

## Fynd

1. **Android når inte servern på `10.0.2.2`.** Emulatorn har både `eth0` (10.0.2.15) och `wlan0`
   (10.0.2.17), och appens default-rutt går via wlan0: `default via 10.0.2.2 dev wlan0 table 1016`.
   Där är 10.0.2.2 den emulerade wifi-routern, inte värden — bara eth0:s 10.0.2.2 är
   qemu-gatewayen som NAT:ar till macOS. Verifierat: `Skicka` gav `Connection failure` även med
   servern bunden till `0.0.0.0` och brandväggen av; med `adb reverse` och `localhost` gick det fram.
2. **Ingen FCM-token på Android.** `google-services.json` är platshållaren, så Firebase Installations
   kan aldrig registrera sig. `RefreshAsync` bryter på `platform.Handle is null` och appen säger
   inget om varför.
3. **Spara-knappen i Taggar-arket syns inte när arket öppnas.** På iOS ligger den under vikningen på
   Medium-detent, på Android halvt bakom gestfältet.
4. **"Registrera om" kan inte tvinga fram en registrering.** `RefreshAsync` hoppar över när
   fingerprint är oförändrat och `Confirm` inte gått ut, så en app vars registrering försvunnit ur
   registret kan inte komma tillbaka förrän fönstret löpt ut.
5. **En rad döljer tre fel.** `RefreshAsync` returnerar `false` för "inget ändrat", "ingen token" och
   "servern nekade eller gick inte att nå".
6. **Token visas aldrig.** `IPushService` exponerar den inte, så samplen kan inte visa den — trots
   att `HomePageViewModel` har både `Token` och `CopyTokenCommand`.
7. **Skicka-sidans rubrik.** `SendPageViewModel.Title` skuggar `ViewModelBase.Title` (CS0108).

## Plan

Paketet först, samplet sedan, dokumentationen sist. En commit per steg.

## Changes

### Paketet

- `IPushService.Token` — den APNs- eller FCM-token registreringen inte kan gå ut utan. Utan den kunde
  samplen inte visa det enda som faktiskt saknades.
- `PushRegistrationResult` ersätter `bool` från `RefreshAsync`: `Sent`, `Unchanged`, `NoBackend`,
  `NoToken`, `Failed`. `SetTagsAsync`, `AddTagsAsync` och `RemoveTagsAsync` svarar med samma, så en
  app kan visa att taggarna sparats lokalt men aldrig nått servern.
- `RefreshAsync(force: true)` skickar även när ingenting ändrats.

### Samplet

- En adress, `localhost:5100`, för båda plattformarna. Android når den via `adb reverse`.
  `10.0.2.2`-specialfallet och hjälpmetoden som valde mellan dem är borta.
- Hem visar token, om servern har installationen, och varför det inte finns någon token när det inte
  gör det — plattformsberoende, eftersom orsaken skiljer sig.
- "Registrera om" tvingar. Taggar-arket öppnas i fullskärm med Spara fäst utanför scrollytan, och
  visar vad som kom av att spara.
- `SendPageViewModel.Title` → `NotificationTitle`; headern visade meddelandets titel (CS0108).

### Dokumentation

- `docs/wiki/push.md`: `adb reverse` i "Run the sample first" med varför `10.0.2.2` inte duger,
  `PushRegistrationResult`-tabellen, `force`, och `Confirm` i minuter i stället för "once a day".
  Påståendet att simulatorn inte ger någon device-token är borta — den gör det på Apple silicon.

## Decisions

- **`10.0.2.2` byts mot `adb reverse` i stället för att appen väljer adress.** Emulatorn har både
  `eth0` och `wlan0` med var sin 10.0.2.2, och appens trafik går över wlan0 där adressen är den
  emulerade routern. `adb reverse` fungerar dessutom likadant för en telefon i sladden, så en adress
  räcker för alla fall och hjälpmetoden i `MauiProgram` försvinner.
- **`RefreshAsync` returnerar en enum, inte `bool` plus loggning.** Tre olika utfall såg likadana ut
  för anroparen, och samplen skrev en mening som stämde för alla tre och förklarade ingen. Ett
  brytande API-byte, men paketet är inte släppt och `Plugin.Maui.Spine.Push` har en konsument.
- **Taggar-arket öppnas i fullskärm i stället för att Spara fästs vid Medium-kanten.** Arket lägger ut
  sitt innehåll mot hela skärmhöjden, inte mot detenten, så en fäst rad hamnar under kanten ändå.
  Medium är kvar som snäppläge.

## Verifiering

Bootad iPhone 17-simulator och Pixel 10 Pro-emulator (API 36), mot sample-servern på port 5100.

- Android: Hem visar `Token —`, `Registrerad nej` och raden om att `google-services.json` är en
  platshållare. Taggar-arket öppnas med Spara synlig; att spara svarar "Sparat lokalt. Ingen token,
  så servern har dem inte."
- iOS: Hem visar token och `Registrerad ja`. Registret tömdes bakom ryggen på appen med ett
  `DELETE`; "Registrera om" fick tillbaka raden direkt, vilket den inte kunde före ändringen.
- Skicka-sidans header visar "SKICKA".
- 163 tester gröna, `Plugin.Maui.Spine.Push` bygger på alla TFM:er, Orientera bygger.
