# Issue #192 — Spine.Widgets: en app med widgets går inte att installera på enhet — extensionen får ingen profil, och felet säger inte det

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/192
**Branch:** issue/192-spine-widgets-en-app-med-widgets-gar-inte-att
**Status:** In Progress

## Plan

### Vad undersökningen visade

Issuet föreslog att targeten skulle fela tidigt med ett begripligt meddelande. Efter att ha läst
SDK:ns targets är det inte tillräckligt — den riktiga bristen ligger ett steg längre ner.

`Xamarin.Shared.targets` i `Microsoft.iOS.Sdk.net10.0_26.2` bäddar in en profil på exakt ett ställe:

```
2067: <_EmbeddedProvisionProfilePath …>$(_AppBundlePath)embedded.mobileprovision</…>
2094: <Target Name="_EmbedProvisionProfile" Condition="'$(_ProvisioningProfile)' != ''" …>
```

Det är **appbundlen**. `_ExtendAppExtensionReferences` (rad 2615) tar `AdditionalAppExtensions` och
lägger dem i `_CodesignBundle` med entitlements och signeringsnyckel — men det finns ingen metadata
för en profil och ingenting som kopierar in `embedded.mobileprovision` i `.appex`.

Slutsatsen: **att bara skapa profilen i portalen räcker inte.** Den måste också hamna inne i
extensionen, och det är ingen annan än Spine som kan göra det, eftersom det är Spine som genererar
bundlen. Ett bygge som bara felar med "skapa en profil" hade skickat nästa person till en åtgärd som
inte hjälper.

### Steg 1 — Hitta rätt profil

Ny property `SpineWidgetsCodesignProvision`, tom som default, samma roll som `CodesignProvision` har
för appen. Är den satt används den profilen. Är den tom söks
`~/Library/MobileDevice/Provisioning Profiles/` igenom efter en profil vars
`Entitlements.application-identifier` matchar `<team>.$(ApplicationId).$(SpineWidgetsExtensionName)`,
med exakt träff före wildcard och senaste utgångsdatum först.

Sökningen görs i `spine-widgets-build.sh` snarare än i MSBuild — `security cms -D` och plist-läsning
hör hemma i skalet, och skriptet finns redan.

### Steg 2 — Bädda in den

Profilen kopieras till `<appex>/embedded.mobileprovision` innan SDK:n signerar. Det ska ske i
`_SpineWidgetsRegister`, före `_DetectSigningIdentity`, så filen finns när `_CodesignBundle`
bearbetas.

### Steg 3 — Fela begripligt när ingen finns

Device-bygge (`_SpineWidgetsSdk == 'iphoneos'`) med signering påslagen och ingen matchande profil →
`<Error>` som namnger `$(ApplicationId).$(SpineWidgetsExtensionName)` och säger att det behöver ett
eget App ID och en development-profil, i samma stil som `Plugin.Maui.Spine.Push.targets` gör för
`google-services.json` och minSdk 23. Simulatorbyggen berörs inte — de signerar ad hoc.

### Steg 4 — Dokumentation

`docs/wiki/widgets.md` säger i dag bara att App Group behöver finnas i profilen för device-byggen
(rad 93). Den behöver ett stycke om att extensionen har ett eget bundle-id och därför kräver ett eget
App ID och en egen profil, plus felkoden `0xe8008015` i felsökningstabellen så den går att söka på.

### Verifiering

- **Felvägen** går att prova direkt: bygg samplet för `ios-arm64` utan profil för extensionen och se
  att felet kommer vid bygget i stället för vid installationen.
- **Lyckavägen** kräver ett App ID `com.companyname.mauispinepushsampleapp.SpineWidgets` och en
  development-profil i portalen. Det behöver din inloggade session igen.

## Open Questions

1. **Fela eller varna när ingen profil hittas?** Jag föreslår `<Error>`. Ett device-bygge utan profil
   för extensionen ger en `.app` som garanterat inte går att installera, så ett bygge som lyckas är
   ett falskt kvitto. Men det är en hårdare hållning än paketet har i dag, och den kan slå mot ett
   flöde jag inte känner till — säg om du hellre vill ha en varning.

2. **App Group.** `group.$(ApplicationId)` måste troligen vara registrerad och påslagen på båda
   App ID:na för att signeringen ska gå igenom. Det hann aldrig verifieras, eftersom widgets stängdes
   av under #190. Om det stämmer bör kontrollen i steg 3 säga det också — men jag vill se det hända
   innan jag skriver en text som påstår det.

## Changes

- `spine-widgets-build.sh` letar upp extensionens profil och kopierar in den som
  `<appex>/embedded.mobileprovision` före signeringen. Matchning på
  `Entitlements.application-identifier` utan team-prefixet, senaste utgångsdatum vinner.
- Ny property `SpineWidgetsCodesignProvision`, tom som default. Är den satt måste profilen ha både
  det namnet och rätt App ID — annars felar bygget hellre än signerar med fel profil.
- Device-bygge med signering och ingen matchande profil felar nu, med App ID, App Group och
  vilken sorts profil som behövs utskrivet, plus `SpineWidgetsEnabled=false` som utväg.
- Båda värdena ligger i `inputs.txt`, så en ändrad property bygger om extensionen.

## Verifierat

- **Felvägen, skarpt.** `dotnet build -f net10.0-ios -p:RuntimeIdentifier=ios-arm64` på
  `MauiSpinePushSampleApp` utan profil för extensionen stannar nu vid bygget med meddelandet ovan,
  i stället för att producera en `.app` som faller vid installationen med `0xe8008015`.
- **Lyckavägen är inte verifierad än.** Den kräver App ID och profil för
  `com.companyname.mauispinepushsampleapp.SpineWidgets`. App Group
  `group.com.companyname.mauispinepushsampleapp` är skapad och App Groups ikryssad på appens App ID,
  men tilldelningsdialogen i portalen tog inte emot syntetiska klick på Continue.

## Decisions

- **Bädda in profilen, inte bara fela.** Issuet föreslog en tidig kontroll. Att bara kontrollera hade
  skickat nästa person till en åtgärd som inte räcker: SDK:n bäddar in en profil enbart i appbundlen
  (`_EmbedProvisionProfile`, `Xamarin.Shared.targets` rad 2094), och `_ExtendAppExtensionReferences`
  (rad 2615) signerar extensionen utan att ge den någon. Profilen måste alltså in i `.appex`, och det
  är Spine som genererar den bundlen.
- **Bara exakt matchning på App ID, inga wildcards.** Extensionen bär en App Group-entitlement och ett
  wildcard-App-ID kan inte ha App Groups, så en wildcard-profil som "matchar" hade fallit i
  signeringen ändå — senare och med ett sämre felmeddelande.
- **Sökningen ligger i skalskriptet, inte i MSBuild.** `security cms -D` och plist-läsning hör hemma
  där, och skriptet finns redan som enda ställe som rör extensionens bundle.
- **Fela, inte varna** — efter avstämning. Ett signerat device-bygge utan profil i extensionen ger en
  app som garanterat inte går att installera, så ett lyckat bygge vore ett falskt kvitto.
