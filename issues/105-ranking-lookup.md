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

**Mot skarp Eventor via BFF-stubben**, Gävle OK (124): 35 löpare, med id, klass,
rikslistplacering och poäng — `16695 Isa Envall D21 riks 5 3,30`. Andra anropet svarar på
0,0008 s ur cachen.

## Kvar

- **Poäng per disciplin** kräver löparsidan, alltså rankingavgiften. Frågan till förbundet blir
  därmed en affärsfråga: finns ett API, och vad kostar åtkomst till det som redan är en betaltjänst?
- **Historik** finns inte här heller. Klubbsidan är ett nuläge.
- `GetRankingAsync` i appen är fortfarande inte kopplad — endpointen finns, men kopplingen kräver
  att användaren pekar ut sin egen rad en gång, och det är ett eget ärende.
