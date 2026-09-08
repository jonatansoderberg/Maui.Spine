# Issue #195 — MauiSpinePushSampleApp: deklarerad widget utan provider

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/195
**Branch:** issue/195-deklarerad-widget-utan-provider
**Status:** Completed

## Plan

Samplet deklarerade `<SpineWidget Include="sample" …>` men hade ingen klass med `[Widget]`. Widgeten
gick att lägga på hemskärmen och visade `—`, eftersom ingenting byggde innehåll till den.

En provider som visar något som **syns ändras** vid varje ombyggnad, så en widget-push går att
observera i stället för att bara svara `sent 1`.

## Changes

- `Widgets/SampleWidget.cs`: `[Widget("sample")]` som visar tid för senaste ombyggnad och ett
  löpnummer. Båda ändras vid varje bygge.
- Räknaren ligger i `Preferences` och överlever processen — en widget kan byggas om långt efter den
  start som schemalade det, och en nollställd räknare hade fått en riktig refresh att se ut som den
  första.
- Providern skriver också en rad i `PushLog`, så ombyggnaden syns på **Logg**-sidan.
- `Refresh(15 min)` så widgeten inte fryser när ingen push kommer.

### Innehåll från Skicka-sidan

- `Services/WidgetContent.cs`: vad widgeten visar, satt av den senaste tysta pushen och sparat i
  `Preferences`.
- `SamplePushHandler` fångar en tyst push som bär `widget.title`, lagrar innehållet och ber
  `IWidgetService` bygga om widgeten.
- Providern renderar det, med en tydlig text när inget skickats än i stället för ett tomt kort.
- Skicka-sidan skickar innehållet som `data` när sorten är `silent`.

### Fälten följer sorten

Varje sort använder olika delar av formuläret, och att visa allt inbjöd till att fylla i en kanal för
en widget-refresh och undra varför den inte gjorde något.

| Sort | Fält | Rad som förklarar |
|---|---|---|
| alert | titel, text, route, kanal, prioritet | visas inte i förgrunden |
| silent | titel, text | innehållet hamnar i widgeten |
| liveactivity | titel, text, prioritet | kräver en körande aktivitet |
| widget | inga | bara "bygg om" |

## Decisions

- **Loggraden är inte dekoration.** När widget-pushen provades första gången svarade servern `sent 1`
  utan att något gick att observera: en widget-refresh hanteras internt av Spine och når aldrig
  appens `IPushHandler`, så Logg-sidan var tom och widgeten visade `—`. Med raden i `PushLog` går
  vägen att verifiera även av den som inte har widgeten på hemskärmen — vilket är det normala när
  man utvecklar.
- **Tid och löpnummer, inte bara tid.** Två ombyggnader inom samma sekund hade sett identiska ut.
- **Innehållet kommer med en tyst push, inte med widget-pushen.** Spine ger en widget-push direkt till
  `IWidgetService` — den betyder "bygg om" och bär inget innehåll, och `WidgetContext` har bara
  `Kind`. Det är rätt form: en widget ska rita det appen vet, inte det som råkade ligga i den senaste
  payloaden, eftersom pushar kan tappas, slås ihop eller komma i oordning. Så innehållet reser som en
  tyst push, som når appens handler, och widget-pushen förblir det den är. Alternativet — att låta
  widget-meddelanden bära data in i providern — hade varit en ramverksändring som gör widgets
  beroende av leveransgarantier de inte har.

## Verifiering

- **Templaten renderar.** På en fysisk iPhone 16 Pro visar widgeten på hemskärmen nu ombyggnadstext
  i stället för `—`. Den fylls vid varje bakgrundning, oberoende av push.
- **Fälten följer sorten**, verifierat i simulatorn: `alert` visar alla fält med raden om förgrunden;
  `widget` visar bara mål och fördröjning, med "Bara 'bygg om'. Widgeten ritar det en tyst push
  senast la där."
- **Push-utlöst ombyggnad är inte verifierad.** Telefonen fick inte upp någon registrering mot
  sample-servern vid försöket, så varken `silent` med innehåll eller `kind: "widget"` kunde skickas
  till den. Simulatorn duger inte heller: den levererar inte tysta pushar alls.
