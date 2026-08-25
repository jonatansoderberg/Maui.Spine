# Issue #154 — Hem ritas i fyra lägen

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/154
**Branch:** issue/154-hem-ritas-i-fyra-lagen
**Status:** In Progress

## Plan

Etapp 5 i [redesign-04-hem.md](../samples/Orientera/docs/design/redesign-04-hem.md), och den
sista. Grenad ur [#153](153-hem-ritas-om-hjalte-live-yta-och-sektionsrubriker.md).

Hem hade två av P10:s fyra lägen, och det ena var just det principen förbjuder: en
`ActivityIndicator` ovanpå innehållet. `StateView` finns sedan riktning 02 och gör redan hela
jobbet — det som saknades var att sidan använde den.

## Changes

### `Features/Home/HomePage.ViewModel.cs` ✅

- `State` — ett värde härlett ur `IsLoading`, `IsOffline` och `HasContent`, i den ordningen.
  Ingenting är tomt medan svaret är okänt, och ingenting är offline medan en hämtning pågår.
- `ReloadCommand` — knappen i offline-läget.
- Aliaset `using ViewState = Orientera.Controls.ViewState;`, eftersom MAUI har ett eget.

### `Features/Home/HomePage.View.xaml` ✅

`StateView` bär sidans nedre hälft, med fyra innehåll:

| Läge | Vad som ritas |
|---|---|
| Laddar | Skelett i blockens form: ett högt kort, sedan två med var sin rubrikstapel ovanför |
| Har data | `CollectionView` med blocken |
| Tomt | En mening om varför, och en knapp till kalendern |
| Offline | "Ingen anslutning", vad som ändå fungerar, och "Försök igen" |

`ActivityIndicator`-en är borta, och den fristående offline-stapeln vid sidan av listan med den.

### Verifiering ✅

- Build grön, testsviten grön (536).
- Kört på iPhone 17-simulator i ljust och mörkt läge: **Har data** mot demodatat, **Offline** mot
  en backend som inte svarar, och **Laddar** mot en oroutbar adress så att skelettet står kvar
  länge nog att granskas.

### Efterjustering — hjälten växer och kortet lägger sig över ✅

Bilden ska gå ned till knappt halva skärmen och första kortet ska överlappa den.

- **`Features/Home/HomeHero.cs`** — hjälten bruten ur sidans XAML till en egen vy, eftersom den
  numera behövs på fyra ställen: som listans huvud i innehållsläget, och överst i vart och ett av
  de tre andra lägena.
- **`HomePage.ViewModel.cs`** — `HeroHeight` (46 % av skärmen, ur `DeviceDisplay`) och
  `HeroOverlap` (negativ underkant på huvudet).
- **`HomePage.View.xaml`** — hjälten in i `CollectionView.Header`; listan blev helbleed och de sex
  mallarna bär sin egen sidmarginal i stället.
- **`HomePage.cs`** — flikattributets `SafeAreaEdges` tillbaka till standard, se nedan.

### Efterjustering — riktiga bilder ✅

- **`Resources/Images/hero/hero_home.jpg`** — den genererade platshållaren ersatt av projektägarens
  bild. Generatorn borttagen; README och licensfil omskrivna.
- **`Resources/Images/surfaces/surface_live.jpg`** — live-kortets yta är en höjdkurvebild i stället
  för en enfärgad platta. Egen katalog med README och licensfil, som terräng och hjälte.
- **`HeroScrimSoft`** — nytt token i båda temana, gradientens andra stopp.
- **`Features/Home/HomeHero.cs`** — uttoning mot `SurfacePage` i underkanten.
- **`Resources/Styles/Components.xaml`**, **`Controls/IdentityView.cs`**, **`Controls/AvatarStack.cs`**
  — badgetexten och initialerna i rubrikfonten, centrerade.

### Efterjustering — optisk centrering och parallax ✅

- **`Resources/Styles/Components.xaml`** — `Badge` och `ChipCompact` fick osymmetrisk padding, med
  orsaken förklarad en gång ovanför stilarna.
- **`Controls/IdentityView.cs`**, **`Controls/AvatarStack.cs`** — samma rättelse som marginal, för
  där finns ingen padding att flytta.
- **`Features/Home/HomePage.cs`** + **`.View.xaml`** — hjälten följer halva skrollsträckan.

### Efterjustering — kanten mot statusfältet ✅

- **`Controls/EdgeBlur.cs`** — Apples scroll edge-effekt: `UIVisualEffectView` med systemets
  ultratunna material, maskad med en gradient så underkanten tonar ut. Där ingen oskärpa finns att
  låna ritas ett band i sidans färg med samma uttoning.
- **`Features/Home/HomePage.View.xaml` + `.cs`** — bandet och en hopfälld, centrerad rubrik tonas
  in tillsammans när den stora hälsningen är på väg under statusfältet.
- **`HomePage.ViewModel.cs`** — `TopBlurHeight`, `TopTitleHeight`, `TopTitleMargin`.

## Decisions

- **Offline är sidans fel-läge, inte ett femte.** P10:s fel-läge kräver vad som gick fel, vad som
  ändå fungerar, och en väg att försöka igen — vilket är exakt vad offline-texten redan sa, minus
  knappen. Ett femte läge hade varit samma tre delar med ett annat namn.

- **Tomt läge fick en knapp och inte bara en mening.** `StateView.EmptyHint` är text; P10 kräver
  en väg vidare. Slotten `EmptyView` finns för precis det, så komponenten behövde inte ändras.

- **Kortavståndet i listan gick från 12 till 16.** Sektionsrubrikerna sitter numera ovanför korten
  och inte inuti dem, och med tolv punkter mellan blocken låg föregående korts underkant lika nära
  rubriken som rubriken låg sitt eget kort.

- **Hjälten flyttade in i listans huvud, och skrollar därmed bort med korten.** Det är överlappet
  som kräver det. En hjälte som står still tvingar korten att antingen klippas mot dess underkant
  på väg upp — mitt på ett fotografi, vilket läser som trasigt snarare än som djup — eller att
  täcka hälsningen. Fällan som stod dokumenterad i koden (en header som mätts som tom växer inte)
  undviks av att höjden är satt och känd innan något bundits.

- **Bilden går under statusfältet, och överlappet är halva hjälten.** Att hjälten skrollar betyder
  att korten passerar under statusfältet, där klockan hamnar ovanpå ett kort — det är hur en
  helbleed-sida beter sig på iOS, och det är valt framför att låta bilden börja under fältet.
  Alternativet, en permanent mörk remsa bakom statusfältet (Apples scroll edge), kostar ett mörkt
  band över vita kort i ljust läge och är inte byggt.

- **Överlappet är en andel och inte ett punktmått**, av samma skäl som höjden: en tredjedel är en
  tredjedel på varje skärm. Två tredjedelar av bilden står fria, den nedersta ligger bakom kortet.

- **`CollectionView` byttes mot en `ScrollView` med en stapel.** Huvudcellen beskär sitt innehåll
  till den höjd marginalen lämnar, så hjälten kapades exakt vid första kortets överkant: bilden
  slutade där kortet började och kortet stod på sidans yta i stället för på fotot. Överlappet var
  alltså bara ett högre startläge. En stapel beskär inte sina barn, och den negativa marginalen
  sitter numera på blockstapelns överkant i stället för på hjältens underkant. Hem visar högst
  fyra block, så virtualiseringen kostar ingenting att ge upp — sidan är per sin egen definition
  få stora block och aldrig en tät instrumentpanel.

- **Höjden är 46 % av skärmen och inte ett punktmått.** "Knappt halva skärmen" är ett förhållande:
  fyrahundra punkter är nästan hela en iPhone SE och en tredjedel av en iPad.

- **Bilden mäts mot textens egen bredd, inte mot en ruta runt den.** Första mätningen tog en ruta
  dubbelt så bred som texten, fick träff på solen i bilden och sa 1.0:1 — vilket hade motiverat en
  nedtoning som gjort fotot till en yta. Mot radernas verkliga bredd är svaret 3.84:1 på den
  svagaste raden, och ett mjukt andra stopp i gradienten räcker: 5.69:1.

- **Uttoningen i underkanten ritas, den är inte inbakad.** Originalbilden kom med en uttoning mot
  vitt, vilket är rätt i exakt ett av två teman. Den är bortbeskuren, och `HomeHero` tonar mot
  `SurfacePage` i stället — mot det som faktiskt ligger under bilden.

- **Ytbilden får inte bestämma kortets storlek.** Mätt på sin egen storlek — elvahundra punkter —
  växte live-kortet till en skärmhög grön platta. `HeightRequest="1"` med `VerticalOptions="Fill"`
  gör att den bidrar med ingenting i mätningen och fyller ytan i arrangemanget.

- **`CourseMark` togs bort från live-kortet.** Höjdkurvorna i ytbilden säger samma sak, och två
  kartmotiv på ett kort är ett för mycket. Komponenten finns kvar och visas på designsystemsidan,
  men har därmed ingen sida som använder den.

- **Texten i piller och plattor satt genomgående för lågt, och orsaken är en.** En etikett
  centreras på sin radhöjd, som går från typsnittets översta ascender till dess understa
  descender. Versaler når varken den ena eller den andra: ovanför dem ligger luften som prickarna
  över Ä och Ö behöver, under dem hela descenderdjupet som inget tecken använder. Uppmätt på iOS
  5 pt för lågt i badgen och lika mycket i det kompakta chippet — alltså samma fel oavsett typsnitt
  och storlek, vilket är varför det inte gick att lösa genom att byta font. Rättelsen är halva
  skillnaden bort från överkanten och lika mycket till underkanten: texten flyttas upp, höjden står
  kvar. Det höga chippet rörs inte; dess padding vilar på en egen uppmätning som står i koden.

- **Parallaxen är en översättning nedåt och inte en egen skrollvy.** Hjälten ligger i skrollytan och
  flyttas alltså redan uppåt med hela sträckan; hälften tas tillbaka. Att översättningen aldrig
  överstiger sträckan är vad som gör att bildens överkant inte kan hamna nedanför skärmkanten och
  lämna en glipa.

- **I studsen ovanför toppen tas sträckan tillbaka helt, inte till hälften.** Först klämdes den
  bara vid noll, och då följde hjälten med studsen nedåt och blottade sidans tomma yta ovanför
  sig. Med hela sträckan återtagen står den stilla i överkanten medan korten dras ned — vilket
  dessutom ger mer av fotografiet, som är vad gesten borde ge.

  *Verifierad genom resonemang och inte på skärm:* studsen går inte att fånga i en skärmdump med
  simulatorns gestverktyg, eftersom bilden tas efter att gesten släppts.

- **Oskärpan är plattformens egen, inte en målad platta.** MAUI exponerar ingen oskärpa, så
  `EdgeBlur` lägger en `UIVisualEffectView` i den plattformsvy MAUI redan skapat i stället för att
  byta ut hela handlern. Materialet är det **ultratunna**: det tunna lägger till en ljus ton som
  gör bandet till en platta, och hela poängen är att se vad som passerar under. Där ingen oskärpa
  finns — Android, Windows — ritas sidans färg med samma uttoning.

- **Underkanten tonas ut med en gradientmask.** Utan den slutar oskärpan i en rak linje tvärs över
  innehållet, och en skarp kant mitt på ett fotografi läses som ett fel snarare än som en yta.
  Masken är ett lager och följer därför inte med autoresizing som vyer gör; dess ram och stopp
  sätts om när bandet byter storlek, utan implicita animationer så den inte glider efter layouten.

- **Den stora hälsningen fälls ihop till en liten centrerad rubrik.** Samma skärning som Tävlingar
  bär, på den plats ögat letar när den stora är borta. Den tonas in tillsammans med bandet — de är
  en och samma sak för ögat.

- **Badgetexten och initialerna bär rubrikfonten.** Två versaler i en cirkel och ett ord i ett
  piller är märken, inte text — vilket är vad Brandon Grotesque är ritad för. Textjusteringen står
  bredvid layoutjusteringen: den ena centrerar etiketten i plattan, den andra raden inuti
  etiketten, och utan båda hamnar bokstäverna för högt.

- **Skelettet ritar två block, inte fyra.** Hem visar högst fyra, men med hjälten på halva skärmen
  ligger redan det tredje under kanten — ett skelett för något som ändå inte syns är en rad kod som
  aldrig läses.
