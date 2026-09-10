# Issue #230 — Spine.Widgets: iOS 26 widget-push (WidgetPushHandler, apns-push-type: widgets)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/230
**Branch:** issue/230-widget-push
**Status:** Completed

## Plan

I dag skickar `RefreshWidgetsAsync` en tyst push som väcker appen, som bygger om widgetarna — best effort: tysta pushar stryps, levereras inte efter force-quit och aldrig i simulatorn. iOS 26 har en egen väg där WidgetKit laddar om utan att appen körs.

Förarbetet fanns redan: `PushInstallation.WidgetToken` i kontraktet, i Table Storage-registret och i `PushService` fingeravtryck; `ApplePushPlatform.WidgetToken` svarade `null`.

1. **Servern** väljer väg per installation: widget-token → `widgets`-push till den tokenen; ingen → den tysta pushen som förut.
2. **Klienten**: en `WidgetPushHandler` i extensionet, tokenen till appen och upp i installationen.

## Open Questions

Inga. Klientdelen: **opt-in som gör extensionet till iOS 26** (Jonatans val), se Blockerare nedan.

## Changes

- `ApnsPayload.ContentChanged` → `"content-changed": true` i `aps`.
- `PushPayloads.ApnsWidgetPush(bundleId)`: push-typ `widgets`, ämne `<bundle id>.push-type.widgets`, prioritet 10.
- `PushSender.RefreshWidgetsAsync` delar Apple-installationerna: med widget-token får widget-push adresserad till tokenen, utan får den tysta pushen. Leveranser till en widget-token raderar aldrig installationen.
- Tester: payloaden, båda vägarna, att en död widget-token inte raderar och att en död enhets-token på den tysta vägen fortfarande gör det.
- `SpineWidgetsPush` (standard `false`) i Widgets-targets. Skickar `--push` och push-miljön (från `SpinePushEnvironment`, annars development i Debug) till byggskriptet, och säger vid varje bygge att extensionet blir iOS 26.
- `spine-widgets-build.sh --push true`: varje widget får `.pushHandler(SpineWidgetPushHandler.self)`, extensionet kompileras och får `MinimumOSVersion` 26.0 (bryggan behåller `SpineWidgetsMinimumOSVersion`), och extensionets entitlements får `aps-environment`.
- Simulatorn: extensionets signatur har bara App Group, och hela uppsättningen (med `aps-environment`) länkas in i sektionerna `__entitlements` och `__ents_der` — som .NET SDK gör för appen.
- `native/ios/SpineWidgetPush.swift`: hanteraren. Postar Darwin-notisen `<app group>.spine-widgets.push-token` när tokenen byts.
- Bryggan: `refreshWidgetPushToken()` hämtar `WidgetCenter.shared.currentPushInfo` bakom `#available(iOS 26)` och postar samma notis när tokenen ändrats; `widgetPushToken()` läser den senaste.
- `IWidgetService.PushToken` och `PushTokenChanged`. iOS lyssnar på Darwin-notisen från start, hämtar tokenen på nytt och höjer händelsen.
- `PushService` läser widget-tokenen från `IWidgetService` och registrerar om vid `PushTokenChanged`. `IPushPlatform.WidgetToken` borttagen — tokenen tillhör Widgets.
- Push-samplet slår på `SpineWidgetsPush` för simulatorbyggen; sample-serverns `/installations` visar widget-tokenen.
- Wikin: `widgets.md` får avsnittet *Reloading by push* och egenskapen i tabellen; `push-server.md` beskriver de två vägarna; raderna under *Not in v1* borta.

## Verifiering (iOS 26.2-simulator, riktig APNs sandbox via sample-servern på 5100)

- Bygget: extensionet `arm64-apple-ios26.0-simulator` med `MinimumOSVersion` 26.0, bryggan kvar på 17.0, appen på 15.0. Signaturen har bara App Group; `aps-environment` ligger i `__entitlements`.
- Med `aps-environment` i signaturen dog extensionet vid start (*Launch failed*, chronod: *unable to obtain widget extension session*) — därav sektionen.
- chronod läste in widgetarna med `supportsPush = YES`, men begärde ingen token förrän en widget låg på hemskärmen.
- Widgeten "Spine push" tillagd på hemskärmen → apsd gav en token för `com.companyname.mauispinepushsampleapp.push-type.widgets`, chronod skickade den till extensionet, och hanteraren loggade *widget push token changed, covering 1 widget(s)*.
- Appen startad → `widgetPushToken` i registreringen (`80f4d809…dcd8760f`, samma som apsd:s).
- **Appen stängd**, `POST /send` med `kind: widget` → `Sent`. chronod tog emot `{"aps":{"content-changed":1}}` på widgets-ämnet och laddade om `sample` en sekund senare; appen startades inte.
- Android och Mac Catalyst bygger; 175 servertester gröna.

## Decisions

- **En död widget-token raderar inte installationen.** `RemoveInvalidAsync` tar bort hela installationen när APNs underkänner en token — rätt för enhetens egen token, fel för en widget-token, som inte säger något om den. Appen skickar en ny vid nästa registrering.
- **Widget-pushen kan inte rikta sig mot en typ.** Tokenen täcker alla widgetar med push-hanteraren, och en push laddar om dem alla; `kind` används bara på den tysta vägen. Dokumenteras vid API:t.
- **Opt-in som gör extensionet till iOS 26** (Jonatans val). Swift låter inte en widget ha push-hanterare från iOS 26 och sakna den före (se nedan), så valet står mellan push och widgetar på äldre iOS — och det valet är appens.
- **Bara extensionet blir iOS 26, inte bryggan.** Bryggan länkas in i appen, som fortsatt kör på äldre iOS; den når tokenen bakom `#available`.
- **Tokenen ägs av Widgets, inte Push.** Den hör till extensionet, och Widgets vet om det byggts med push. Push läser den genom `IWidgetService` i Common, som den redan gör med Live Activity-tokens.
- **`aps-environment` i sektionen, inte i signaturen, i simulatorn.** Simulatorn vägrar starta ett ad hoc-signerat extension vars signatur har nyckeln (processen dör direkt, chronod: *Launch failed*). Utan nyckeln alls hittar chronod ingen push-miljö. .NET SDK signerar appen utan rättigheter och lägger dem i binären; extensionet gör nu likadant.
- **Tokenen kommer först när en widget ligger på hemskärmen.** chronod prenumererar bara för extension med placerade widgetar som stöder push. Dokumenterat i wikin, så att ingen letar efter ett fel som inte finns.
- **Bryggan hämtar tokenen, extensionet säger bara till.** Hanteraren körs i extensionets process och kan inte nå appens minne; `currentPushInfo` kan anropas från appen. Darwin-notisen är samma mönster som knapparna och aktiviteterna.

## Blockerare: iOS-versionen

`WidgetPushHandler`, `WidgetPushInfo` och `WidgetConfiguration.pushHandler(_:)` finns från iOS 26 (SDK 26.2 på maskinen). Extensionet byggs för iOS 17 (`SpineWidgetsMinimumOSVersion`), och Swift låter inte en widget välja variant efter version:

1. `if #available(iOS 26) { MedPush() }` + `if #unavailable(iOS 26) { Utan() }` i bunten → kompilatorkrasch (*failed to produce diagnostic*).
2. `if #available { config.pushHandler(...) } else { config }` i `Widget.body` → *branches have mismatching types* — `body` har ingen resultatbyggare och SwiftUI ingen typradering för konfigurationer.
3. `if #available { MedPush() } else { Utan() }` i bunten → *control flow cannot be used with WidgetBundleBuilder*.
