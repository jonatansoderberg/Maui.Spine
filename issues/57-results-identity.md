# Issue #57 — Resultat: hittar aldrig mig i skarpa data, och visar då ingenting alls

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/57
**Branch:** issue/57-results-identity
**Status:** Completed

## Plan

Resultatsidan letade upp mig med `r.Person == _me.Id`. Min identitet är lokal (`me:<namn|klubb>`)
medan Eventors resultat bär Eventors person-id, så jämförelsen kunde aldrig bli sann. Hittades jag
inte visade sidan ingenting — trots att hela resultatlistan låg hämtad i minnet — och skickade mig
till tidsmaskinen, som bara finns i demoläget.

Tre saker:

- Matcha mig på namn och klubb med `RunnerIdentity`, samma väg som livelistan går (SP-04).
- Visa fältet även när jag inte är med. Att öppna en tävling man inte sprang är normalfallet.
- Säg något sant i det tomma läget: "inte publicerat" och "du är inte med" är olika saker.

## Changes

- `ResultsDetailPage.ViewModel` — `_mine` slås upp med `RunnerIdentity.Of(namn, klubb)` i stället
  för person-id. `HasResult` betyder nu att tävlingen har en publicerad resultatlista; `HasMine`
  att jag står i den.
- `BuildField` bygger hela fältet klassvis, med min klass först, min rad accenttonad och
  klubbmärket bredvid klubbnamnet. Placering, tid och tid efter vinnaren per rad; en felstämpling
  visar sin status i stället för en tid.
- `NotInFieldText` säger det en gång, ovanför listan, när jag inte är med.
- Tomma läget säger "Resultatet är inte publicerat ännu." utan hänvisning till tidsmaskinen.
- `ResultsDetailPage.View.xaml` — översiktskortet och prognoskortet visas bara med `HasMine`;
  resultatlistan ligger under dem på Översikt.

## Decisions

- **Namn och klubb, inte person-id.** Det är samma slutsats som SP-04 drog för livelistan, och
  `RunnerIdentity` fanns redan i domänen. Att jämföra id:n över systemgränsen är inte en bugg som
  kan lagas med ett id till — de två systemen har olika uppfattning om vem en person är.
- **Hela fältet, klassvis, med min klass först.** En mästerskapstävling har fyrtio klasser och 240
  resultat. Sorteringen gör att den klass man kom för att se ligger överst; resten finns för den
  som letar.
- **Sträckor och Analys kräver fortfarande mig.** De är analys av *mitt* lopp; utan min rad finns
  inget att analysera, och de flikarna förblir avstängda.
- **Inga nya tester.** Vymodellerna ligger i `Features/` och kompileras inte in i testprojektet
  (bara `Services/`, `Domain` och backend gör det). `RunnerIdentity.Matches` — det som faktiskt
  gör jobbet — är redan täckt av `RunnerIdentityTests`. Verifieringen är därför körningen nedan.

## Verifiering

`dotnet test`: 206 gröna. Grenen står på master, inte på live-stapeln — de åtta tester som kommit
till där hör till #43, #53 och #65.

**iPhone 17 Pro-simulator (iOS 26.2) mot skarp Eventor-data** via BFF-stubben,
Norrlandsmästerskapen sprint (240 resultat i 30 klasser):

- **Utan mig i fältet** (identitet Jonatan Söderberg / Gävle OK): sidan säger "Du är inte med i den
  här resultatlistan. 240 resultat totalt." och visar H45 först — Olov Vikström 13:28, Göran Tronde
  +1:52, och Ola Tuvesson som "Felstämplat" utan placering — följt av övriga klasser. Före
  ändringen stod hela sidan tom med "Inget resultat ännu".
- **Med mig i fältet** (identitet satt till Olov Vikström / Sundsvalls OK): översiktskortet visar
  "1:a, 1 / 7, 13:28, +0:00, Godkänd" och hans rad är accenttonad i listan. Sträckor fylls med
  riktiga sträcktider från Eventor — fjorton kontroller med sträckplacering och tapp, +0:03 på
  sträcka 6 och +0:17 på sträcka 11.

Kvarstår utanför den här issuen: **Resultat-fliken listar fortfarande inget**, eftersom
`BackendSource.GetResultsForPersonAsync` svarar tomt. Att hitta mina resultat över hela kalendern
kräver antingen en källa per person eller att hämta varje tävlings resultatlista, och det senare är
just den bulkhämtning appen inte ska göra. Det är ett eget ärende.
