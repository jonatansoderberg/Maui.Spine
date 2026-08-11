# Issue #107 — Klubb- och klassplacering bredvid riksplaceringen

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/107
**Branch:** issue/107-club-and-class-place
**Status:** Completed

## Plan

Jag-fliken säger idag bara *1914:e i Sverige*. Två placeringar till ligger redan i sidor vi
hämtar; ingen av dem kräver en ny källa.

**Klassplaceringen är gratis.** Löparsidans listtabell har under varje lista en rad för löparens
egen klass med samma poäng och en annan placering:

```
Sverigelistan   1914   62,98   1,54
H45              203   62,98   1,54
```

`RunnerRankingParser.Lists` läser redan förbi den raden och kastar den — kommentaren i koden säger
att klassraderna "faller bort på namnuppslaget". Nu tas den under Sverigelistan tillvara.

**Klubbplaceringen är en sammanfogning.** Löparsidan länkar själv till löparens klubb:

```html
<h3 class="runnerClubLink"><a href="/Ranking/ol/Club/Index/115">Gävle OK</a></h3>
```

Så både id och namn kommer ur sidan vi redan har. Med id:t slås klubbsidan upp via befintliga
`RankingScraper.ForClubAsync` (cachad tolv timmar), och raden med samma `RunnerId` bär numret.

**Klubbsidan är delad per kön.** Uppmätt i fixturen: sidan har två tabeller under rubrikerna
*Damer* (12 löpare) och *Herrar* (23), och båda numrerar från 1. `RankingPageParser` slår idag
ihop dem och tappar därmed vilken tabell en rad kom ur. En klubbplacering utan det är tvetydig —
det finns två 17:e i varje klubb. Parsern får läsa rubriken.

## Öppna frågor — besvarade före kod

- **Gren:** #105 mergas först, denna grenas från master. *(Svar: merga först.)*
- **Ordval:** raden säger `17:e i Gävle OK, herrar` — vad siffran faktiskt betyder, inte bara vad
  Eventor skriver ut. *(Svar: alternativ 2.)*
- **"Herrar-listan":** löparsidan har ingen Herrar-rad; den har en klassrad (H45). Klassplaceringen
  är alltså det som fanns, och den görs i samma ärende som klubbplaceringen — samma rad, samma
  kort, samma verifiering. *(Svar: samma issue.)*

## Changes

- `Domain/Ranking.cs` — `ClassStanding`, `ClubStanding` och `RankingSection`, samt `Class` och
  `Club` på `RankingSnapshot`. Båda frivilliga: saknas de säger appen ingenting i stället för
  något påhittat.
- `Ranking/RankingPageParser.cs` — läser rubrikerna Damer/Herrar och märker varje rad med sin
  sektion. Rubriker och rader matchas i dokumentordning i stället för bara rader.
- `Ranking/RankingRow.cs` — `Section`.
- `Ranking/RunnerRankingParser.cs` — klassraden under Sverigelistan tas tillvara, och `Club(html)`
  läser klubbens id och namn ur `runnerClubLink`.
- `Ranking/RunnerRankingSource.cs` — hämtar klubbsidan i löparens egen session och plockar raden
  med löparens id. Adresserna byggs nu ur `RankingOptions.BaseAddress` i stället för en hårdkodad
  sträng.
- `Presentation/Format.cs` — `Section` → "damer" / "herrar".
- `Features/Profile/ProfilePage.ViewModel.cs` — `NationalPlaceText` blir `PlacesText`: de tre
  placeringarna sammanfogade, där en som saknas utelämnas.
- `FakeData/FakeDataset.cs` — demodatat får klass- och klubbplacering.
- Fyra nya tester.

## Decisions

- **Klubbsidan läses genom löparens session, inte anonymt.** Detta var planens enda felaktiga
  antagande, och skarp data avslöjade det direkt: `GET /api/ranking/clubs/115` gav **en** löpare.
  Sidan säger själv varför — *"du är inte inloggad"* eller *"din klubb har inte betalt avgiften"*.
  Samma sida genom sessionen som redan mintas för löparsidan: **188 löpare**. Klubb 124, som
  fixturen kommer från, ger 35 anonymt — den klubben har betalat, och det var därför spiken aldrig
  såg gränsen.

  Det gör också åtkomsten enklare att försvara: det är löparens egen klubbsida, hämtad med
  löparens egen session, och bara löparens egen rad används.
- **`RankingScraper` och `/api/ranking/clubs/{id}` blir kvar.** De fungerar för klubbar som betalat
  och delar parser med det här. Att ta bort dem hade varit att kasta en fungerande publik väg.
- **Sektionen är nullbar hela vägen.** Slutar Eventor sätta rubriker faller ordet "herrar" bort men
  placeringen står kvar — samma hållning som resten av parsern: tappa det som gick sönder, inte
  allt.
- **Klassraden tas bara under Sverigelistan.** Samma rad finns under varje disciplinlista och säger
  samma sak om ett smalare snitt; kortet frågar efter en placering, inte fem.
- **En felaktig rad i `issues/105-ranking-lookup.md` rättad:** klubb 124 är IKHP Huskvarna
  Idrottsklubb, inte Gävle OK. Gävle OK är 115, vilket löparsidans egen klubblänk visar.

## Verifiering

`dotnet test`: **266 gröna** (262 + 4 nya).

**Mot skarp Eventor via BFF-stubben** (`EVENTOR_LIVE=1`, `/api/ranking/me`):

```
nationalPlace 1914   points 62.98
class  { class: H45,       place: 203 }
club   { club: Gävle OK,   place: 17,  section: Men }
```

Hela kedjan — engångslänk, session, löparsida, klubbsida — tog 1,8 s.

**I simulatorn (iPhone 17 Pro), mot den skarpa stubben:**
`1914:e i Sverige · 203:e i H45 · 17:e i Gävle OK, herrar` — ryms på en rad på 402 pt utan brytning.

**I demoläget** (`Backend:BaseAddress` tom): `187:e i Sverige · 24:e i D21 · 2:a i OK Gästrike, damer`
— och `Format.Place` ger riktigt "2:a", inte "2:e".

### Vad körningen avslöjade

1. **Klubbsidan är betald, inte publik** — se beslutet ovan. Planen hade fel och stubben visade det
   på första anropet.
2. **Klubbplaceringen räknas per kön**, uppmätt i fixturen: 12 damer och 23 herrar, båda numrerade
   från 1. Utan sektionen hade "17:e i klubben" varit två olika löpare. Skarpt är Gävle OK 188
   löpare och raden `17 · Jonatan Söderberg · H45 · 1914 · 62,98` under rubriken Herrar.
3. **Demoläget kan nu säga emot identiteten.** Kortet namnger klass och klubb för första gången,
   och demodatats löpare är Elin Norberg (D21, OK Gästrike) medan identiteten är lokal och min
   telefon minns "Jonatan Söderberg, Gävle OK, H45" från de skarpa körningarna. Vid en ren
   installation stämmer de; på en enhet som kört skarpt gör de det inte. Ingen kod är fel, men
   motsägelsen syntes inte förrän kortet började nämna klass och klubb.
4. **En kosmetisk bugg utanför det här ärendet:** varningsraden blir "faller ur 19 sep.." — det
   svenska datumformatet `d MMM` har redan en punkt, och meningen lägger till en till. Flaggad
   separat i stället för att smygas in här.
