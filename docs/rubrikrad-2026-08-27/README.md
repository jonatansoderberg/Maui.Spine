# Rubrikraden, kartläggning 27 augusti 2026

Skärmdumpar från kartläggningen av hur Spine placerar sidans titel och rubrikradens
åtgärdsknappar, och av de tre fel den hittade. Kört mot `MauiSpineSampleApp` och `Orientera` på
iPhone 17 Pro, iPad Pro 11", Pixel 10 Pro och Pixel Tablet.

Rättningarna ligger i [#163](https://github.com/jonatansoderberg/Maui.Spine/pull/163).

## Vad kartläggningen visade

Rubrikraden är två kontroller i skilda vyträd som råkar hamna över varandra: titeln är en etikett
i rad 0 av `PagePresenter`, inne i sidans innehåll, medan knapparna ligger i `HeaderBar` som ett
överlägg i `NavigationRegion`. Det finns ingen `DeviceIdiom`-förgrening i rubrikkoden — telefon
och platta får identisk rad, och allt som skiljer dem kommer från plattformens arkbehållare.

## Filerna

| Prefix | Läge |
|--------|------|
| `ios-*`, `and-*` | Före alla rättningar |
| `fix-*` | Efter arkets toppmarginal och titelns reservation |
| `title-after*` | Mellanläge: reservationen räknad på konstanter i stället för uppmätta knappbredder |
| `final-*` | Efter alla tre rättningarna — det som ligger på master |
| `debug-ramar-ios-phone.png` | Titelns ram röd, åtgärdens blå: identiska, 186–281 px |

`*-main` är startsidan, `*-region` en regionrubrik, `*-sheet-simple|fullscreen|small` de tre
detenterna. `and-phone-landscape-region.png` visar ett fel som **inte** är rättat: i liggande läge
lägger sig rubrikraden i statusfältet på Android. Pixel Tablet startar liggande och är därför
alltid drabbad; stående läge är korrekt. Det förtjänar ett eget ärende.
