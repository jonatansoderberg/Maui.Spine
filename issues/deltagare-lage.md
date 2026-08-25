# Deltagarlistan: ett fält, fyra lägen

**GitHub:** _issue ej skapad än_
**Branch:** issue/deltagare-lage
**Status:** Completed

## Plan

Omläggningen följer [redesign-03-deltagare.md](../samples/Orientera/docs/design/redesign-03-deltagare.md)
i sex etapper. Kärnan: *anmälda · startlista · live · resultat* är fyra lägen hos **samma** lista
under **samma** tävling, inte fyra platser i appen. Flikraden krymper från fem till tre
(Hem · Tävlingar · Jag), Live och Resultat upphör som sektioner, och min säsong blir en undersida
till Jag.

1. **Läget som domän** — enum, tillgänglighet och förval som ren, testbar kod.
2. **Källorna** — en radanatomi för alla fyra lägen, och resultat per klass i backend.
3. **Deltagarsidan** — egen pushad sida med lägesväxlare, ett läge i taget.
4. **Tävlingssidan blir nav** — startfältssektionen blir ett deltagarkort, CTA:n dirigeras om.
5. **Flikarna faller** — Live och Resultat tas bort, Hem kompenserar för grupp-över-tävlingar.
6. **Följderna** — kravdokument, designsystemsida, notiser.

## Changes

### Etapp 1 — Läget som domän ✅

- **`Orientera.Domain/Domain/ParticipantMode.cs`** — `ParticipantMode` (fyra lägen i
  livscykelordning), `Sighting` (vad en källa svarat: `Unknown` / `Absent` / `Present`),
  `ParticipantSightings` (ackumulatorn, monoton), `ParticipantInput`, `ParticipantModeOffer` och
  `ParticipantDecision`. Speglar `ContextState.cs`, som håller sin egen uppsättning på samma sätt.
- **`Orientera/Services/Context/ParticipantModeEngine.cs`** — ren `Decide(input) → decision`, som
  `ContextEngine`. Avgör vilka chip som går att trycka på, vad de otillgängliga säger, och vilket
  läge sidan öppnar på.
- **`Orientera.Tests/ParticipantModeTests.cs`** — 23 tester: hela livscykeln genom växlaren,
  livets långa svans efter sista målgång, båda riktningarna av "svaret slår kalendern", och
  offline-fallen. Hela sviten grön (464).
- **`docs/krav/02-context-engine.md`** — states-tabellen har en lägeskolumn, och ett avsnitt om
  vad som avgör tillgänglighet.

### Etapp 2 — Källorna ✅

- **`competitions/{id}/results?class=`** i backend. Hela listan hämtas, normaliseras och cachas
  **en gång per tävling**; varje klass serveras ur den kopian. Sträcktider utelämnas som
  förval — det är där vikten sitter — och en tävling vars arena stängt får sex timmars livslängd
  i stället för den minut ett pågående lopp behöver.
- **`IParticipationSource.GetClassResultsAsync`** genom alla tre källor (`FakeDataSource`,
  `BackendSource`, `UnreliableSource`).
- **Sex tester** i `EventorSourceTests`: klassfiltret, en klass ingen sprang, en tom klassträng
  som inte frågar Eventor alls, `includeSplitTimes=false`, att andra klassen inte kostar någon
  hämtning, och att ett flerloppsevenemang behåller båda sina etapper.

### Etapp 3 — Deltagarsidan ✅

- **`Features/Events/Participants/`** — `ParticipantsPage` med lägesväxlare (`SegmentBar`),
  skoprad, klassväljare och två listytor: en vanlig lista för anmälda/startlista/resultat och
  splittabellen för live.
- **`ParticipantRow`/`ParticipantCell`/`ParticipantClassGroup`** — en radanatomi för alla fyra
  lägen, byggd på `ListRow`s `[identitet] [primär/sekundär] [värde] [→]`.
- **Fyra laddare**, en per läge, som var och en skriver ned vad källan svarade.
- **Startlistans sorteringsväxel**: starttid eller Sverigelistan, i listans huvud.
- **Livelox mot klassen** — `LiveloxLink.Classes` har burit en url per klass hela tiden och
  användes av ingen.
- **Offline**: startlisteläget läser genom `OfflinePackageService` och visar de sparade
  starttiderna för dig och din grupp, sagt att vara partiellt. Live gör det inte och säger varför.

### Etapp 4 — Tävlingssidan blir nav ✅

- Startfältssektionen är ett **deltagarkort**: läget som badge, antalet, fem rader, och en väg in.
- `PrimaryAction` går till deltagarsidan i rätt läge; `Analyse`/`ShowRouteChoice` till löparsidan.
- Snabbhandlingarna Live och Resultat blev en: **Deltagare**.
- **Fyndet lagat** (D6): `FavouriteGlyph`, `FavouriteDescription` och `ToggleFavouriteCommand`
  fanns inte på vy-modellen. Stjärnan gjorde ingenting. Bindningskontrollen (MAUIG2045) visade
  alla tre; de heter `Interest*`.

### Etapp 5 — Flikarna faller ✅

- **`MyResultsPage`** under Jag; en rad öppnar deltagarsidan i resultatläget, i radens klass.
- **`RunnerResultPage`** ersätter `ResultsDetailPage`: ett lopp, en löpare. Fältlistan är borta —
  den bor i deltagarlistan — och sidan tar `RunnerResultTarget(tävling, klass, löpare?)`.
- **Hem** ger ett Live nu-block *per* pågående tävling du har någon i, upp till tre.
- **Borttaget**: `Features/Live/` (inklusive `ChooseCompetitionSheet`), `LiveSelection`,
  `tab_live.svg`, `tab_results.svg`. Flikraden är Hem · Tävlingar · Jag.
- Tab-badgen sitter på **Tävlingar**.

### Etapp 6 — Följderna ✅

- `01-vision-och-navigation.md` beskriver tre flikar och tre nivåer. `05-live-och-min-grupp.md`
  och `06-resultat-winsplits.md` inleds med vad omläggningen gjorde med dem.
- `DesignSystemPage`s `SegmentBar`-exemplar visar de fyra lägena, med det sista släckt.
- `utfall-m0.md` säger vad omläggningen gjorde med testkörningens fynd, och lägger till tre nya.
- Notiser: `NotificationPlanner` pekar fortfarande inte på någon sida. Planen tar uttryckligen
  inte det arbetet — punkten står där för att det inte ska glömmas bort i tron att det finns.

### Skarp körning i simulatorn ✅

Appen byggd för iOS-simulatorn och körd mot den lokala backenden. Tre flikar, deltagarkortet,
lägesväxlaren, urvalet per läge och resultatraderna verifierade på skärm. Två fel föll ut av att
köra den — inget av dem syntes i bygget eller i testerna:

1. **`SegmentBar` ritade en tom rad.** Två fel i samma kontroll. Den byggde bara om sig när
   `ItemsSource` *byttes*, inte när samlingen fylldes på — nog för en fast uppsättning segment,
   vilket var allt kontrollen haft. Och en horisontell `ScrollView` tar sin innehållsstorlek när
   innehållet *sätts*: segment som lades i en layout den redan mätt till noll stannade på noll.
   Nu lyssnar den på `INotifyCollectionChanged` och lämnar över en ny rad i stället för att fylla
   på den gamla.
2. **Växlaren fylldes aldrig när sidan öppnades med ett utpekat läge.** `Decide()` fyller
   `Modes` som sidoeffekt, och den anropades bara i grenen där kalendern fick välja — vilket
   "Deltagare"-knappen aldrig går igenom. Nu körs den efter varje laddning, och ritar bara om
   raden när tillgängligheten faktiskt ändrats så att en live-pollning inte får chipsen att
   blinka var femtonde sekund.

**Ett fynd till, ur en andra genomgång mot planen.** Deltagarsidan sa "Ingen anslutning" om en
tävling utanför kalenderfönstret, på en fungerande uppkoppling — samma regression
`ResultsDetailPage` en gång fått lagad, återinförd på den nya sidan. Orsaken var att
`OfflinePackageService.GetAsync` gav `DataOrigin.Unavailable` på två helt olika svar: "källan
säger att tävlingen inte finns" och "källan svarar inte". `DataOrigin` har nu ett `Missing`
emellan, och de två meningarna kan inte längre bli samma. Två tester i `OfflinePackageTests`
håller isär dem. Tävlingssidan fick samma uppdelning.

**Kvar att verifiera:** `?class=` mot en omstartad backend. Den lokala Functions-värden kör sedan
före ändringen och returnerar hela listan (512 rader, 37 klasser) för `?class=H21`, vilket är
exakt vad den gjorde innan. Verifierat med curl; enhetstesterna täcker filtret.

## Decisions

**Resultatläget öppnar vid `Live`, inte vid `Finished`.** Planens §3.2 säger att `Finished` är
det tillstånd som *förvalt* ger Resultat, och det står kvar. Men *tillgängligheten* måste börja
tidigare: LiveResults fyller på den preliminära listan medan löpare kommer i mål, och att spärra
resultatchipet tills arenan stänger vore att dölja målgångar för dem som står och tittar på dem.
Det är också vad D11 redan säger — preliminärt och officiellt är samma lista. Fyndet kom ur ett
rött test, inte ur läsning.

**Förvalet är "det mest framskridna läget som har något bakom sig", inte kalenderns gissning.**
Första utkastet lät `ContextState` välja och föll sedan tillbaka nedåt i stegen. Det gav fel svar
när verkligheten låg före kalendern: en klass vars startlista finns fastän kalendern säger
"Anmäld" ska öppna på startlistan. Eftersom tillgängligheten redan bär kalenderns åsikt behövs
den inte en gång till. Kalendern används nu bara som sista utväg, för en tävling där inget läge
alls har något bakom sig.

Undantaget är loppet självt: medan någon är ute slår Live över Resultat, för de fåtal rader som
kommit in är ännu inte svaret på "hur gick det". `IsRunningNow` är ett mätt faktum och betyder
ingenting förrän live-källan svarat — en sida som beslutar före sin första hämtning beslutar om
efter den.

**`Sighting` är monoton.** Offline är `Unknown`, och `Unknown` får aldrig skriva över `Present`:
annars gråas startlistan ut för löparen som står på arenan och läser den. Ett `Absent` överlever
också en senare utebliven hämtning, men viker för rader — en klass kan dyka upp i livelistan sent.

**Iakttagelserna hör till en klass.** `ParticipantSightings` nollställs när klassen byts. Att bära
H21:s startlista över till D14 vore en sida som ljuger om en lista den aldrig läst.

**Sträcktider blev en parameter, inte ett andra anrop.** Deltagarlistan vill ha en placering och
en tid; analysen bakom en löparrad behöver hela klassens sträckor, för en sträcka är bara bra
eller dålig jämfört med dem som sprang den. `GetClassResultsAsync(..., splits)` täcker båda med
var sin kopia i cachen. Bieffekten är den större: `ResultsDetailPage` läste hela tävlingens lista
*med* sträcktider — 86 MB för O-Ringen — och `RunnerResultPage` läser en klass.

**`HasLiveSourceAsync` togs bort utan att #89 tappades.** Kalendern vet att loppet pågår; den vet
inte att LiveResults har det. Garden är nu deltagarkortets egen hämtning: en tom klass är ett lopp
utan livelista bakom sig, och "Följ live" försvinner i stället för att landa löparen i någon
annans tävling.

**Löparsidan kräver att löparen finns i listan.** Förut visade `ResultsDetailPage` fältet även när
läsaren inte var med i det. Nu är fältet ett steg bakåt — deltagarlistan — och en sida om ett lopp
som ingen sprang har inget att rita. Den säger det i stället.

**`ChooseCompetitionSheet` ströks.** Den fanns bara för att Live-fliken behövde fråga *vilken*
tävling. Under D7 kom man dit från tävlingen, och frågan finns inte.

**Deltagarkortet gör ett anrop, inte fyra.** Hela sidan observerar alla källor och avgör läget på
fakta (D10); ett kort som gjorde samma sak skulle göra fyra hämtningar för att rita fem namn.
Kortet följer kalendern och säger vad det hittade — ett tomt svar blir lägets egen villkorstext i
stället för en tom yta.
