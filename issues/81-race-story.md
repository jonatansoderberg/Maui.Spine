# Issue #81 — Loppberättelse på Analys

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/81
**Branch:** issue/81-race-story
**Status:** Completed

## Plan

Analys visar siffror: bomtid, tid utan bommar, stabilitet, största tapp. Det som saknas är
meningen — "du började snabbt, bommade sexan, men var bland de snabbaste 7–10". En tränare som
sett loppet hade sagt det på tre rader.

Uppdelningen är bestämd i förväg och går bara åt ett håll:

1. **Appen bestämmer vad som är sant.** `RaceStoryFacts` väljer ut punkterna ur `LegAnalysis`
   deterministiskt: start, sammanhängande stark sträcka, de två dyraste bommarna,
   placeringsutveckling, jämnhet, målgång.
2. **Backend bestämmer hur det låter.** Fakta postas till `POST /api/stories/race`, som ber
   Claude formulera om dem till 3–5 meningar. Modellen får aldrig lägga till något.

Utan konfigurerad nyckel svarar backend 404 och appen visar inget kort alls.

## Changes

- `Orientera.Domain/Sources/RaceStory.cs` — `RaceStoryRequest` (klass + färdiga påståenden),
  `RaceStory` (texten) och `IRaceStorySource`.
- `Orientera/Services/Analysis/RaceStoryFacts.cs` — faktavalet. Konstanterna som styr vad som
  räknas som en stark sträcka och hur många bommar som får nämnas ligger namngivna högst upp.
- `Orientera/Services/Analysis/RaceStorySources.cs` — `BackendRaceStorySource` (postar och
  minns svaret per lopp) och `NoRaceStorySource` (tomt utan backend).
- `Orientera.Backend/Story/RaceStoryWriter.cs` — anropet mot Claude (`claude-sonnet-5`, officiella
  `Anthropic`-paketet), med systemprompten som förbjuder tillägg och en cache på faktanas hash.
- `Orientera.Backend/Functions/StoryFunctions.cs` — `POST /api/stories/race`.
- `Orientera.Backend/Configuration/StoryOptions.cs` + `local.settings.example.json` —
  `Story__ApiKey`, `Story__Model`.
- `ResultsDetailPage` — `StoryText`/`HasStory`/`IsWritingStory`, kortet "Ditt lopp" och en rad
  som säger att texten är AI-sammanfattad.
- `Orientera.Tests/RaceStoryFactsTests.cs` — sju tester på faktalagret.

## Decisions

- **Fakta först, formulering sen.** En modell som både får hitta och formulera berättelsen
  hittar gärna en stark sträcka som inte fanns — och en peppande ton är precis det tryck som gör
  det. Allt som kan bli fel är därför beräknat innan modellen ser något, och prompten säger på
  tre ställen att inget får läggas till. Testerna är skrivna som skyddsräcke: ett påstående som
  inte finns i `Lines` är ett påstående modellen hittat på.
- **"Bland de snabbaste" får aldrig omfatta halva klassen.** Första utfallet hade ett golv på tre
  placeringar, vilket i en klass med fyra startande gjorde meningen sann om nästan alla. Tröskeln
  är nu en fjärdedel av klassen, men aldrig fler än halva fältet — i en klass med två gäller bara
  segraren. Det är testat i båda riktningarna.
- **Skrivs när Analys öppnas, inte när sidan laddas.** Det är det enda på sidan som kostar pengar
  per läsning. Den som bara ville se sin tid betalar aldrig för det.
- **Laddas asynkront och cachas i två lager.** Vymodellen väntar inte in texten — resten av
  fliken går att läsa medan den skrivs, med en rad som säger att den skrivs. Backend cachar på
  faktanas hash i ett dygn (ett avslutat lopp ändrar sig inte), appen minns svaret per lopp under
  sessionen. Ett uteblivet svar cachas inte: det ska frågas om igen, inte kommas ihåg som "det
  finns ingen".
- **Ingen nyckel, inget kort.** Backend svarar 404 och appen visar ingenting — samma princip som
  gäller resten av det ointegrerade. Alternativet, att appen sätter ihop meningarna själv, hade
  gett en text som låter skriven utan att vara det.
- **Sonnet 5, inte Opus 5.** Uppgiften är att formulera om sex färdiga påståenden — en
  språkuppgift, inte en resonemangsuppgift. Opus 5 var fel tier från början: fem gånger dyrare
  och långsammare utan att texten blir bättre. `Story__Model` går att sätta om utan omkompilering
  om utfallet säger något annat.
- **Ett misslyckat anrop loggas.** Ett uteblivet svar ser i appen likadant ut som "ingen nyckel
  konfigurerad" — inget kort alls. `AnthropicException` fångas därför och loggas, annars finns
  inget sätt att i efterhand skilja ett trasigt anrop från en avstängd funktion.
- **Ingen längd som är längre än fakta.** Första prompten bad om 3–5 meningar. Det kravet blev
  ett golv: en löpare med två sträckor gav två faktarader, och modellen fyllde ut till fem
  meningar med "ett stabilt lopp där du höll dig i toppen genom hela banan" — ingenting av det
  stod i listan. Regeln är nu 2–4 meningar *men aldrig fler än listan har punkter*, plus ett
  förbud mot att beskriva hur det låg till mellan punkterna. Det är samma sorts fel som
  faktalagret finns för att förhindra, men det uppstod i formuleringssteget och gick bara att
  hitta genom att köra skarpt.
- **Bara fakta över nätet.** Requesten innehåller klass och färdiga påståenden — inget namn,
  ingen klubb, inget person-id. En loppberättelse ska inte bli stället där identiteten läcker.

## Verifiering

`dotnet test`: 221 gröna (214 + 7 nya).

**Faktalagret mot Eventor-fixturen** (utskrift under arbetet, inte incheckad): för segraren i H21
blev raderna "Snabbast i klassen på första sträckan", "Sträcka 1–3: snabbaste sträcktid i klassen
på var och en", "Jämn fart genom hela loppet", "I mål: 1:a av 4 på 1:02:33" — och för tvåan bara
start och målgång, ingen påhittad stark sträcka. Den utskriften avslöjade också formuleringen
"i snitt 1:a sträcktid" när samtliga sträckor var snabbast, som nu har en egen mening.

**iPhone 17 Pro-simulator (iOS 26.2) mot BFF-stubben, utan nyckel:** Analys-fliken postar till
`/api/stories/race`, får 404, och visar varken kort eller spinner som hänger. Fliken i övrigt
oförändrad.

**Skarpt anrop mot Claude, med nyckel** (2026-08-11, `claude-sonnet-5`): fungerar. Första
anropet tar 7–10 sekunder, cacheträffen 0,04 s. Segrarens berättelse i H21 blev korrekt återgiven
in i varje siffra.

Körningen avslöjade också ett verkligt fel: tvåan i samma klass har bara två sträckor och därmed
två faktarader, och modellen fyllde ut till fem meningar med påståenden om jämnhet och position
som inte fanns i datat. Prompten är omskriven (se besluten ovan) och båda fallen körda om — två
fakta ger nu två meningar, sex fakta ger fyra, och inget som inte står i listan.

**Inte verifierat:** hur texten står sig över många olika lopp. Fem skarpa anrop är inte ett
stickprov som säger något om svansen.
