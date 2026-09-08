# Push — vad som måste registreras utanför repot

Koden är klar och verifierad; det som återstår finns i Apples och Googles portaler och i
appinställningarna. Den här sidan är checklistan.

## Apple — klart

| Sak | Värde |
|-----|-------|
| Team ID | `7F2CQZ9T84` |
| App ID | `se.cosmomedia.orientera` — Push Notifications, App Groups |
| Widget-extensionens App ID | `se.cosmomedia.orientera.widgets` — App Groups |
| App Group | `group.se.cosmomedia.orientera` |
| APNs-nyckel | `Q2G76H33BW`, Sandbox & Production, Team Scoped (All Topics) |

Nyckeln är verifierad mot skarpa APNs: en välformad men påhittad device-token svarar
`BadDeviceToken`, inte `InvalidProviderToken`. Provider-token, ES256-signaturen och `apns-topic`
är alltså accepterade av Apple.

> `.p8`-filen går bara att ladda ner i samma ögonblick nyckeln skapas, och Apple tillåter högst två
> aktiva nycklar per konto. Tappas den bort måste den återkallas och ersättas — och varje backend
> som använde den slutar fungera.

### Kvar hos Apple

- **Provisioneringsprofil** för `se.cosmomedia.orientera` med Push Notifications, och en för
  widget-extensionen. Krävs för att bygga till en fysisk iPhone; simulatorn behöver ingen.
- **`aps-environment`** står på `development` i `Platforms/iOS/Entitlements.plist`. Ett
  App Store-bygge vill ha `production`. Utvecklingstoken fungerar bara mot sandlådan
  (`api.sandbox.push.apple.com`), produktionstoken bara mot `api.push.apple.com` — och Spine väljer
  värd efter vad varje installation registrerade sig med, så båda kan finnas i registret samtidigt.

## Google — inte påbörjat

Orientera har inget Firebase-projekt. Därför står `SpinePushEnabled=false` för Android i
`Orientera.csproj`: utan `google-services.json` stoppar byggkontrollen, och med den här av bygger
appen som vanligt medan Android helt enkelt aldrig hämtar någon token.

När det ska på:

1. Skapa ett Firebase-projekt och lägg till Android-appen med paketnamnet `se.cosmomedia.orientera`.
2. Ladda ner `google-services.json` till `Platforms/Android/` och lägg till
   `<GoogleServicesJson Include="Platforms\Android\google-services.json" />`.
3. Ta bort `SpinePushEnabled`-raden.
4. Hämta tjänstekontots JSON (Project settings → Service accounts → Generate new private key) och
   ge den till backenden som `Push__Android__ServiceAccountJson`.

## Backendens nycklar

Backenden läser dem som vanlig konfiguration. Dubbla understreck blir kolon:
`Push__Apple__TeamId` → `Push:Apple:TeamId`.

| Nyckel | Innehåll |
|--------|----------|
| `Push__Apple__TeamId` | `7F2CQZ9T84` |
| `Push__Apple__KeyId` | `Q2G76H33BW` |
| `Push__Apple__BundleId` | `se.cosmomedia.orientera` |
| `Push__Apple__PrivateKey` | Hela `.p8`-filens innehåll, PEM-huvudet och allt |

Utan `PrivateKey` startar backenden ändå: registret och endpointen finns, telefoner kan registrera
sig, det är bara ingen som skickar.

### Lokalt

Nyckeln ligger i apphostens user-secrets och inte i `local.settings.json` — den filen ligger i
repots träd och är lätt att committa av misstag. Apphosten skickar vidare dem som miljövariabler.

```bash
dotnet user-secrets --project samples/Orientera.AppHost set "Parameters:apple-team-id" "7F2CQZ9T84"
dotnet user-secrets --project samples/Orientera.AppHost set "Parameters:apple-key-id" "Q2G76H33BW"
dotnet user-secrets --project samples/Orientera.AppHost set "Parameters:apple-bundle-id" "se.cosmomedia.orientera"
dotnet user-secrets --project samples/Orientera.AppHost set "Parameters:apple-private-key" "$(cat ~/sökväg/AuthKey_Q2G76H33BW.p8)"
```

Sedan startar hela den lokala uppsättningen — Azurite och Functions-värden — med ett kommando:

```bash
dotnet run --project samples/Orientera.AppHost
```

### I drift

Samma fyra nycklar som appinställningar på Function-appen. `PrivateKey` hör hemma i Key Vault med
en referens från appinställningen, inte som klartext i portalen.

## Innan det kan skickas till en riktig telefon

1. Bygg och installera Orientera på iPhonen med en profil som har Push Notifications.
2. Slå på minst en notistyp i **Profil → Notiser**. Det är där tillståndet frågas, och tagg-
   registreringen följer med.
3. Kontrollera att registreringen kom fram: raden ska finnas i tabellen
   `OrienteraPushInstallations` med `Environment: Sandbox`.
4. Vänta på att resultat publiceras för en tävling du är anmäld till, eller trigga timern manuellt:

```bash
curl -X POST -H "Content-Type: application/json" -d '{"input":""}' http://localhost:7071/admin/functions/AnnounceResultsPublished
```

Timern bokför det den skickat i tabellen `OrienteraAnnounced`. En tävling som redan står där
annonseras inte igen — ta bort raden för att testa om.

## Det som medvetet inte är gjort

- **Registreringsendpointen är öppen**, som resten av backendens API. Därför filtreras `user:`-
  taggar bort: utan autentisering skulle vem som helst kunna lyssna som någon annan. `kind:` och
  `competition:` är inte hemliga — de säger bara vad telefonen vill höra om.
- **Bara `results-published` skickas.** Live-start blir en andra timer när den första visat sig
  fungera mot enhet. Resten av notistyperna är ögonblick som redan finns i datat, och dem
  schemalägger telefonen själv.
