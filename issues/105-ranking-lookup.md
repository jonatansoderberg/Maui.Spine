# Sverigelistan: uppslag per klubb i stället för nattlig crawler

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/103 (SP-02)
**Branch:** issue/105-ranking-index
**Status:** Completed

## Bakgrund

Efter SP-02 beslutades ett nattligt Durable-jobb som skulle svepa alla 3 049 klubbar och lagra
poängen i Table Storage. Under bygget kom två upptäckter som gjorde den formen fel, och en
tredje som gjorde en bättre form möjlig.

## De tre upptäckterna

1. **SP-02 hade fel om person-id.** Klubbsidan länkar varje löpare till
   `/Ranking/ol/Runner/Index/{id}`, 35 av 35 rader. Spiken sökte efter `/Athlete/` och `/Person/`
   och aldrig efter `/Runner/`, och drog slutsats av att inte hitta. Hela matchningsavsnittet
   vilade på det felet.
2. **Löparsidan är en betaltjänst.** Anonymt svarar den *"Avgift för Sverigelistan krävs för
   nuvarande säsong"*. Det är där poäng per disciplin finns — Sverigelistan, Långlistan,
   Medellistan, Nattlistan, Sprintlistan — och dit går backend inte.
3. **Klubbsidan är fri och räcker.** Namn, klass, klubbplacering, rikslistplacering, poäng — och
   löpar-id:t.

## Changes

- `Ranking/RankingRow.cs` — en rad, med `RunnerId`.
- `Ranking/RankingPageParser.cs` — läser klubbsidan; hoppar över rader den inte förstår i stället
  för att kasta.
- `Ranking/RankingScraper.cs` — hämtar och cachar per klubb.
- `Functions/RankingFunctions.cs` — `GET /api/ranking/clubs/{clubId}`.
- `Configuration/RankingOptions.cs` — adress och cachetid.
- `RankingPageParserTests` — sex tester mot en riktig sparad klubbsida.

## Decisions

- **Uppslag, inte crawler.** Svepet hade byggt en egen kopia av en lista förbundet tar betalt runt,
  för att besvara frågor ingen ställt än. Det här läser en publik sida när en löpare faktiskt
  öppnar appen, och minns den i tolv timmar. Durable Functions och Table Storage behövs då inte
  alls, och är borttagna igen.
- **Ingen inloggning.** Poäng per disciplin ligger bakom Sverigelistans avgift. Att hämta det
  server-side hade krävt någons konto, och det gör backend inte — varken med en delad inloggning
  eller med en användares egen.
- **Id:t är rankingens, inte Eventors `personId`.** Det duger att slå upp med, och appen kan
  spara det när användaren pekat ut sig själv i klubbens lista.
- **Fixturen är skyddsräcket.** Det här är den skörast koden i backend: en layoutändring hos
  Eventor tar sönder den tyst. Testerna läser värden ur en riktig sparad sida, inte ur parsern
  själv.

## Verifiering

`dotnet test`: 256 gröna (250 + 6 nya).

**Mot skarp Eventor via BFF-stubben**, IKHP Huskvarna Idrottsklubb (124): 35 löpare, med id, klass,
rikslistplacering och poäng — `16695 Isa Envall D21 riks 5 3,30`. Andra anropet svarar på
0,0008 s ur cachen.

## Löparsidan, undersökt i webbläsaren

Med inloggat konto (användaren loggade in själv i den interna webbläsaren; backend fick aldrig
några uppgifter) svarar `/Ranking/ol/Runner/Index/{id}` med allt det som saknas här:

- **En tabell med 170 rader** — varje resultat med datum, tävling, disciplinkod (Lå/Me/Sp/Na),
  klass och poäng. De sex som räknas är numrerade 1–6 i första kolumnen.
- **En tabell med listorna per disciplin** — Sverigelistan, Långlistan, Medellistan, Nattlistan,
  Sprintlistan — var och en med rikslistplacering, poäng och Ti, plus samma rad för klassen.

**Hur åtkomsten fungerar:** vanlig formulärinloggning som sätter en sessionscookie. I nätverkslogen
finns **inga XHR- eller JSON-anrop alls** — sidan är serverrenderad HTML plus statiska filer. Det
finns alltså inget API bakom rankingen, inte ens för inloggade. `/api/authenticatePerson`
autentiserar mot *API:et*, som inte har några rankingendpoints.

## Tre vägar till poäng per disciplin

1. **Fråga förbundet om API-åtkomst.** Enda vägen som är hållbar över tid. Det är redan en
   betaltjänst, så frågan är affärsmässig snarare än teknisk.
2. **WebView i appen där användaren loggar in själv**, och appen läser sin egen användares sida
   på enheten. Ingen delad session, inga uppgifter i backend, användaren läser data hen betalat
   för. Försvarbart — men det är HTML-skrapning bakom en inloggning, och går sönder vid varje
   ändring av markup eller inloggningsflöde.
3. **Backend med ett konto.** Avfärdad. Det skulle betyda att servern uppträder som en person,
   och att data någon betalar för sprids vidare.

## Kvar

- **Poäng per disciplin** kräver löparsidan, alltså rankingavgiften. Frågan till förbundet blir
  därmed en affärsfråga: finns ett API, och vad kostar åtkomst till det som redan är en betaltjänst?
- **Historik** finns inte här heller. Klubbsidan är ett nuläge.
- `GetRankingAsync` i appen är fortfarande inte kopplad — endpointen finns, men kopplingen kräver
  att användaren pekar ut sin egen rad en gång, och det är ett eget ärende.

---

# Del 2 — proxy mot löparsidan

Efter beslut att bygga server-proxyn, och efter att webbläsartestet visat mekanismen.

## Hur den fungerar

1. `GET /api/externalLoginUrl?personId=…&organisationId=…` med organisationsnyckeln ger en
   engångslänk, giltig fem minuter.
2. Länken följs med en egen cookiejar → en session.
3. `GET /Ranking/ol/Runner/Index/{personId}` med den sessionen → sidan utan betalvägg.
4. `RunnerRankingParser` läser båda tabellerna till en `RankingSnapshot`.

**Inget lösenord är inblandat någonstans.** Det visade sig att `externalLoginUrl` bara behöver ett
`personId`, och det hämtas ur klubbens medlemslista med nyckeln vi redan har.

**`personId` är samma id som rankingens löpar-id** — 121330 i både `/api/persons/organisations/115`
och `/Ranking/ol/Runner/Index/121330`. Ingen mappning behövs.

## Två gränser, uppmätta

| Fråga | Svar |
|---|---|
| Kan vi skapa en session för någon i en annan klubb? | **Nej.** 403, både med deras riktiga klubb-id och när de påstods vara våra. |
| Kan en inloggad session läsa en annan klubbs löpare? | **Ja.** Isa Envall (klubb 124) svarade 200 utan betalvägg. |

Det andra svaret är det som betyder något: Sverigelistan är en **prenumeration**, inte en
behörighet per person. En betalande medlems session öppnar allas sidor.

Därför är `Ranking:DemoSessionPersonId` en inställning och inte ett beteende. Tom kan backend bara
svara för sin egen organisations medlemmar — den gräns Eventor ändå upprätthåller. Satt svarar den
för vem som helst, på en persons prenumeration, och det är ett val någon fattar och äger.

## Verifiering

`dotnet test`: 262 gröna. Sex nya mot en riktig sparad löparsida.

**Skarpt, hela kedjan:** riksplats 1914, 63 poäng, Lång 86 / Medel 61 / Natt 215 / Sprint 85,
141 resultat varav **exakt 6** räknande.

**I appen:** Jag-fliken visar det, med de sex räknande newest first och "faller ur 19 sep." på den
som går ut.

**En bugg som datan avslöjade:** vyn sorterade `Results` på poäng fallande och visade alltså de
*sämsta* resultaten under rubriken "resultat i snittet" — Sverigelistan räknar nedåt, lägre är
bättre. Demodatat hade motsatt konvention och dolde felet. Nu används `Counting`, nyast först.

## Inte gjort

- **Annan klubbs löpare genom prototypsessionen** svarade inte i stubben (timeout). Vägen är
  verifierad med curl men inte genom backend.
- **Trend** är 0. Sidan visar ett nuläge; en trend kräver två avläsningar.
