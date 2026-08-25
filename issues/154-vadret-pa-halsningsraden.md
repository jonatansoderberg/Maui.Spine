# Issue #154 — Vädret på hälsningsraden

**GitHub:** _issue ej skapad än_
**Branch:** issue/153-hem-ritas-om-hjalte-live-yta-och-sektionsrubriker
**Status:** In Progress

## Plan

Etapp 4 i [redesign-04-hem.md](../samples/Orientera/docs/design/redesign-04-hem.md). Ligger på
samma gren som etapp 3 — raden hör till hjälten och har ingen egen sida att stå på.

Position → prognos → cache → rad. Ingen rad alls hellre än en gissad.

## Changes

### `Services/Weather/` ✅

| Fil | Vad den är |
|---|---|
| `CurrentWeather.cs` | Svaret, plus `WeatherWords` som säger en symbolkod i tecken och i ord |
| `MetForecast.cs` | Ren parser av MET Norways punktprognos |
| `WeatherStore.cs` | Filcachen, med två åldrar |
| `WeatherService.cs` | Position, tillståndsfråga och orkestrering — plattformshalvan |

### Övrigt ✅

- **`MauiProgram.cs`** — klient mot `api.met.no` med den User-Agent deras villkor kräver.
- **`HomePage.ViewModel.cs`** — `WeatherText`, `WeatherDescription`, `HasWeather`; hämtningen
  ligger efter blocken.
- **`HomePage.View.xaml`** — raden under datumet, med sin egen uppläsning.
- **`ProfilePage.View.xaml`** — krediteringen av MET Norway, permanent.
- **`AndroidManifest.xml`** — `ACCESS_COARSE_LOCATION`. **iOS/MacCatalyst `Info.plist`** —
  `NSLocationWhenInUseUsageDescription`.
- **`Orientera.Tests`** — de tre MAUI-fria filerna länkas in; `WeatherService.cs` lämnas utanför,
  precis som `EventorPlatform.cs`.

### Verifiering ✅

- Testsviten grön (536, varav 10 nya för vädret).
- Kört på iPhone 17-simulator med simulerad position i Gävle, mot MET:s riktiga API: raden visar
  "☀️ 16° i Gästrikland" och `weather.json` skrivs.

## Decisions

- **Källan blev MET Norway, inte SMHI som D13 pekade ut.** SMHI:s
  `opendata-download-metfcst.smhi.se` svarar 404 på hela värden, API-roten inräknad — tjänsten
  ligger inte kvar på den adressen. MET:s är fri och nyckellös på samma sätt men kräver en
  identifierande User-Agent och en kreditering; båda är gjorda. §4 i riktningen är omskriven.

- **Krediteringen står på Jag och inte i utvecklingsläget.** En licensrad som bara syns i debug är
  ingen licensrad.

- **Symbolkoderna läses på delsträng, inte som en tabell.** MET har ett fyrtiotal, byggda av samma
  ord i kombination — "heavysleetshowersandthunder" är fyra av dem i rad. En tabell hade varit
  fyrtio rader att hålla i synk med någon annans lista. Ordningen är regeln: åska vinner över det
  den regnar med.

- **Åldern mäts som avstånd, inte differens.** Tidsmaskinen under Jag flyttar appens dygn, och ett
  väder stämplat med riktig tid ligger då i framtiden — en rå subtraktion hade gjort det evigt
  färskt. Vädret självt hämtas mot `DateTimeOffset.Now` och inte `IClock`: det är ett påstående om
  den verkliga världen.

- **Väderhämtningen fångar `SourceUnavailableException`.** Den behöver `me.Home` ur källorna, och
  med dem nere hade en utsmyckning tagit ned hela `OnAppearing`. Uppdagat när sidan kördes mot en
  backend som inte fanns.

- **Geokodarens fångst är bred, och avsiktligt.** Plattformens geokodare misslyckas på fler sätt
  än den dokumenterar — CLGeocoder med sitt eget NSError, Androids med en IOException — och
  ortnamnet är en bekvämlighet. Distriktet står redan redo som svar.

## Open Questions

- **Ortnamnet är inte verifierat på riktig enhet.** Simulatorns geokodare svarar inget användbart,
  så raden föll tillbaka på distriktet ("Gästrikland" i stället för "Gävle") vid varje körning.
  Fallbacken är rätt beteende, men att `Locality` faktiskt kommer fram behöver ses på en telefon.
