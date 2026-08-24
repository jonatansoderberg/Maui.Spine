# Orientera — designriktning 03 "Deltagare"

**Status:** genomförd · **Datum:** 2026-08-22 · **Changelog:** [deltagare-lage.md](../../../../issues/deltagare-lage.md) · **Föregås av:**
[redesign-02-natur-och-energi.md](redesign-02-natur-och-energi.md)

Ett enda listjobb — *vilka är med, och hur går det för dem* — ligger i dag utspritt på tre
flikar och fyra sidor. Den här riktningen samlar det under tävlingen det handlar om, och låter
flikraden krympa till de tre frågor användaren faktiskt ställer appen.

---

## 1. Vad som ändras

| | I dag | Efter |
|---|---|---|
| Flikar | Hem · Tävlingar · Live · Resultat · Jag | **Hem · Tävlingar · Jag** |
| Anmälda | Utfällbar sektion på tävlingssidan | Läge i deltagarlistan |
| Startfält | Sektion på tävlingssidan | Läge i deltagarlistan |
| Live | Egen flik, väljer tävling själv | Läge i deltagarlistan |
| Resultatlista | `ResultsDetailPage`, egen flikgren | Läge i deltagarlistan |
| Min säsong | Egen flik (`ResultsPage`) | Undersida till **Jag** |
| Sträckor/analys | Flikar inne på `ResultsDetailPage` | Egen sida bakom en löparrad |
| Livelox | Länk till tävlingen | Länk till **klassen**, i resultatläget |

Kärnan: **listan är en, läget skiftar.** Man går inte till en annan del av appen för att se
samma fält i ett annat skede — man byter läge på samma sida.

---

## 2. Beslut

D1–D6 står kvar. D4 skrivs om.

| # | Beslut | Utfall | Följd |
|---|---|---|---|
| **D7** | Flikstruktur (ersätter D4) | **Hem · Tävlingar · Jag** | Live och Resultat är inte platser i appen utan lägen hos en tävling. Verktygslådan "Mer" skjuts till M4, när det finns verktyg att lägga i den |
| **D8** | Deltagarlistans hemvist | **Egen pushad sida under tävlingen** | Tävlingssidan är redan 821 rader vy-modell; en klass på 400 löpare inuti dess `ScrollView` blir en sida ingen scrollar till botten av. Live-pollning på en egen sida stannar också när sidan lämnas, vilket är mönstret `LivePage` redan har |
| **D9** | Sträckor och analys | **Egen sida bakom en löparrad** | Lägesväxlaren handlar om *fältet*; sträckor och analys handlar om *en löpare*. Att blanda dem i samma rad gör raden till två frågor |
| **D10** | Läget härleds, tillgängligheten mäts | **Context Engine väljer förvalt läge, källorna avgör vilka lägen som går att välja** | Kalendern vet vad som *borde* finnas. Bara ett svar vet vad som *finns*. #89 är samma lärdom en gång till: att inte veta är inte att veta att det inte finns |
| **D11** | Preliminärt och officiellt är samma läge | **Resultatläget är ett, med källan utskriven på sidan** | LiveResults *är* den preliminära resultatlistan. Två lägen för samma fråga vore en växlare som frågar läsaren vilken sanning de vill ha |

---

## 3. Deltagarläget — modellen

### 3.1 Stegen

```csharp
// Orientera.Domain/Domain/ParticipantMode.cs
public enum ParticipantMode { Entries, StartList, Live, Results }
```

Deklarerad i livscykelordning, precis som `ContextState` — "mest framskridna tillgängliga läge"
blir då en `Max`, inte en `switch`.

| Läge | Etikett | Vad raden säger | Källa |
|---|---|---|---|
| `Entries` | Anmälda | Namn, klubb | `IStartFieldSource.GetEntryListAsync` |
| `StartList` | Startlista | Starttid, namn, klubb, Sverigelistan | `IStartFieldSource.GetStartFieldAsync` |
| `Live` | Live | Radiokontroller som kolumner, placering vid varje | `ILiveSource.GetSnapshotAsync` |
| `Results` | Resultat | Placering, tid, efter vinnaren | `ILiveSource` (preliminärt) → `IParticipationSource` (officiellt) |

Alla fyra frågorna är **redan per klass** i källorna. Det är den enskilt viktigaste anledningen
till att den här sidan går att bygga: ingen av dem kräver att hela tävlingen hämtas.

Undantaget är den officiella resultatlistan — se [§4.2](#42-resultat-per-klass-backend).

### 3.2 Förval

`ContextState` väljer:

| Tillstånd | Förvalt läge |
|---|---|
| `Discovered` … `PmPublished` | Anmälda |
| `StartListPublished`, `RaceDay` | Startlista |
| `Live` | Live |
| `Finished` | Resultat *(preliminärt)* |
| `ResultsPublished` … `MapAndAnalysisAvailable` | Resultat *(officiellt)* |

Förvalet är en gissning ur kalendern och får bara bestämma **var växlaren står när sidan
öppnas**. Vilka chip som går att trycka på avgörs av vad källorna svarade — ett läge utan rader
bakom sig är släckt med en rad som säger varför ("finns när startlistan lottats"), aldrig ett
chip som leder till en tom sida. Det är samma regel som `LiveConditionText` redan följer på
tävlingssidan.

Ett läge som en gång svarat får stå kvar tillgängligt även om nästa hämtning fallerar: offline
är inte samma sak som "finns inte".

### 3.3 Live och Resultat är samma fält

Medan någon är ute i skogen ritar sidan splittabellen. När ingen är ute *är*
live-ögonblicksbilden den preliminära resultatlistan — `LiveEntry` bär redan `FinishTime`,
`FinalPlace` och `FinishBehind`. Resultatläget läser därför:

1. den officiella listan när den publicerats (bär sträcktider och är den som gäller), annars
2. live-källans klassvy, märkt **Preliminärt** — samma ord som `ContextState.Finished` redan
   använder.

`IsRunningNow` (finns i `LivePageViewModel` i dag) är det som skiljer Live-läget från
Resultat-läget, och det är ett mätt faktum, inte en klocka.

### 3.4 Urval inuti läget

Live-flikens skopor flyttar med, oförändrade i innebörd:

- **Min grupp** — spänner över klasser, den enda skopan som gör det
- **Min klass** — den klass anmälan/valet/starten säger
- **⟨vald klass⟩** — `ChooseClassSheet`, som i dag

Klassen löses i samma ordning som tävlingssidan redan gör: anmälan → `CompetitionClassStore` →
`MyStart.Class` → `me.DefaultClass`. Ett val som görs här sparas på samma ställe, så
tävlingssidan och deltagarsidan aldrig kan visa olika klasser för samma tävling.

I lägena Anmälda och Startlista finns ingen "Min grupp" hos källan — entrylistan bär varken
person-id eller starttider. Skopan är därför bara tillgänglig i Live och Resultat, och släckt
med sin egen mening i de två andra.

---

## 4. Etapper

Varje etapp ska kunna gå in i master för sig och lämna appen i ett användbart läge.

### Etapp 1 — Läget som domän *(ingen UI)*

**Nytt**
- `Orientera.Domain/Domain/ParticipantMode.cs` — enum enligt §3.1
- `Orientera.Domain/Domain/ParticipantAvailability.cs` — `record` med fyra `bool` och en
  `ParticipantMode Default`, plus en anledningstext per otillgängligt läge
- `Orientera/Services/Context/ParticipantModes.cs` — ren funktion:
  `ContextState + observationer → ParticipantAvailability`

**Test** — `Orientera.Tests/ParticipantModeTests.cs`: hela livscykeln en gång genom
tidsmaskinen; att ett läge utan rader är släckt; att ett läge som svarat en gång inte släcks av
en efterföljande `SourceUnavailableException`; att Live viker för Resultat när ingen är ute.

**Klart när** testerna är gröna och `ContextEngine`-tabellen i
[02-context-engine.md](../krav/02-context-engine.md) har lägeskolumnen.

### Etapp 2 — Källorna

#### 4.1 En rad, fyra ursprung

**Nytt** — `Orientera/Features/Events/Participants/ParticipantRow.cs`. En radanatomi för alla
fyra lägen (P9): identitetsplats (namn + klubb + klubbmärke), värdeplats (starttid / placering /
tid), och en detaljrad. `IsMe` och `IsInMyGroup` markerar som i dag. Splittabellens celler
(`LiveCell`) hänger på raden och ritas bara i Live-läget.

**Ändrat** — `Services/Sources/Sources.cs`: `IParticipationSource` får

```csharp
Task<IReadOnlyList<CompetitionResult>> GetClassResultsAsync(
    CompetitionId competition, string className, CancellationToken cancellationToken = default);
```

Implementeras i `FakeDataSource`, `BackendSource` och `UnreliableSource`.

#### 4.2 Resultat per klass (backend)

Eventor erbjuder ingenting smalare än hela tävlingens resultatlista — `results/event` tar ingen
klass, och `wrsresults/event`, som gör det, svarar 404 för allt utanför världsrankingen. Det är
mätt vad det kostar: **O-Ringens lista är 86 MB och 97 sekunder**. Appen får därför aldrig
hämta den.

**Ändrat** — `Orientera.Backend/Functions/CompetitionFunctions.cs`: `competitions/{id}/results`
tar `?class=`. Backend hämtar hela listan **en gång**, lägger den i `ResponseCache` per tävling
och serverar klassen ur den. Livslängden är lång — en publicerad resultatlista ändras inte —
och en tävling utan cache-träff betalar hämtningen en gång för alla klasser tillsammans.

**Klart när** en klass ur O-Ringen kommer tillbaka under en sekund vid varm cache, och
`Orientera.Tests` täcker att `?class=` filtrerar utan att ändra ordningen.

### Etapp 3 — Deltagarsidan

**Nytt** — `Orientera/Features/Events/Participants/`
- `ParticipantsPage.cs` — `[NavigableRegion(Title = "Deltagare")]`,
  `INavigableWithParameter<ParticipantsTarget>` där `ParticipantsTarget` bär
  `CompetitionId` + valfri `string? Class` + valfritt `ParticipantMode? Mode`
- `ParticipantsPage.View.xaml` — `SegmentBar` för läget överst, skoprad under, listan därunder
- `ParticipantsPage.ViewModel.cs`

**Flyttas hit** ur `LivePage.ViewModel.cs`, i stort sett oförändrat: `LiveRow`, `LiveCell`,
`LiveClassGroup`, `Merge`/`Regroup`/`Measure`/`Fit`, pollningsslingan och
`FrozenWidth`/`MinColumnWidth`. Det är den mest värdefulla koden i den gamla fliken och den ska
inte skrivas om, bara byta hem.

**Ur `EventDetailsPage.ViewModel.cs`** flyttar `LoadStartFieldAsync`, `StartFieldRow`,
`Entrants`/`ToggleEntrants` och `EventorMessage`-förklaringen av en tom Sverigelistan.

**Livelox** — `LiveloxLink.Classes` bär redan en url per klass och används inte av någon i dag.
Resultatläget avslutas med ett `HandoffCard` mot **klassens** Livelox när den finns, annars mot
tävlingens.

**Ordning inom etappen** — ett läge i taget, med sidan användbar mellan varje: Startlista →
Anmälda → Live → Resultat. Startlistan först därför att den är den enda som måste fungera
offline, och därför den som ställer de svåra frågorna om paketet.

**Offline** — `CompetitionSnapshot` bär redan `MyStart`, `GroupStarts` och `Results`.
Startlisteläget läser genom `OfflinePackageService` som tävlingssidan gör, med samma
cache-etikett. Live-läget gör det inte och säger varför — det är den enda vyn en sparad kopia
inte kan stå för.

### Etapp 4 — Tävlingssidan blir nav

**Ändrat** — `EventDetailsPage`

- Startfältssektionen ersätts av ett **Deltagare-kort**: läget som badge, de tre–fem rader som
  angår läsaren (jag, min grupp, ledaren), och en väg in till hela listan.
- `PrimaryAction` dirigeras om: `ShowMyStart`, `FollowLive`, `ShowPreliminary` och
  `ShowMyResult` går alla till `ParticipantsPage` med rätt läge. `Analyse` och `ShowRouteChoice`
  går till löparsidan (etapp 5).
- `OpenLive`/`OpenResults`-snabbhandlingarna blir en: **Deltagare**.
- `HasLiveSourceAsync` blir överflödig — tillgängligheten kommer nu från `ParticipantModes`
  (etapp 1) i stället för en egen fråga.

**Passa på** (D6: fynd lagas inuti sidan när den byggs om): `EventDetailsPage.View.xaml` binder
`FavouriteGlyph` och `ToggleFavouriteCommand`, som inte finns på vy-modellen — den heter
`InterestGlyph`/`ToggleInterestCommand`. Stjärnan på tävlingssidan gör alltså ingenting i dag.

### Etapp 5 — Flikarna faller

**Nytt**
- `Features/Profile/MyResultsPage.cs` — `[NavigableRegion(Title = "Mina resultat")]`. Innehållet
  är `ResultsPage.ViewModel.cs` oförändrat; bara attributet och namnet byts. En rad öppnar
  `ParticipantsPage` i resultatläget, med löparens klass, i stället för `ResultsDetailPage`.
- `Features/Results/RunnerResultPage.*` — dagens `ResultsDetailPage` beskuren till *en löpare*:
  Översikt / Sträckor / Analys, jämförelse och loppberättelse. Fältlistan lyfts ur den; den bor
  i deltagarlistan nu. Tar `ResultId` eller `(CompetitionId, PersonId, Class)`.

**Ändrat**
- `ProfilePage` får raden **Mina resultat** överst i sin egen sektion.
- `HomePage.ViewModel.cs`: `OpenLive` → `ParticipantsPage` i live-läget för blockets tävling.
  `OpenResult` → `ParticipantsPage` i resultatläget. `_tabBadges.SetBadge<LivePage>` utgår
  (se §5).
- `MauiProgram.cs`: inget att göra — sidorna registreras via attributskanning.

**Tas bort**
- `Features/Live/LivePage.*`, `Features/Live/ChooseCompetitionSheet.*`
- `Services/Context/LiveSelection.cs` — den fanns bara för att `SwitchToTabAsync` inte bär
  parameter. `ParticipantsPage` tar en parameter, och behovet försvinner med fliken.
- `Features/Results/ResultsPage.*`, `Features/Results/ResultsDetailPage.*` (ersatta ovan)
- Ikonerna `tab_live.svg`, `tab_results.svg`

**Klart när** appen har tre flikar, ingen kvarvarande `SwitchToTabAsync<LivePage>`, och varje
väg som förr gick till Live eller Resultat landar på rätt läge i rätt klass.

### Etapp 6 — Följderna

- **Kravdokumentet.** [01-vision-och-navigation.md](../krav/01-vision-och-navigation.md) §
  "Huvudnavigation — fem flikar" skrivs om.
  [05-live-och-min-grupp.md](../krav/05-live-och-min-grupp.md) och
  [06-resultat-winsplits.md](../krav/06-resultat-winsplits.md) beskriver lägen, inte flikar.
- **Notiser.** `NotificationPlanner` planerar `LiveStarted` och `ResultsPublished` utan att peka
  på någon sida. Om de ska bli djuplänkar är det ett eget arbete — nämns här bara så att det
  inte glöms bort i tron att det redan finns.
- **Designsystemsidan** (`DesignSystemPage`) får `SegmentBar` i lägesform.
- **`utfall-m0.md`** kompletteras med vad omläggningen gjorde med testkörningens fynd.

---

## 5. Konsekvenser som måste hanteras

**Att följa Min grupp i två samtidiga tävlingar tappar sin vy.** Det var Live-flikens ena
riktiga jobb: en förälder med barn i två lopp, en mästerskapshelg. Under D7 finns ingen sida som
spänner över tävlingar. Kompensationen är Hems `LiveNowBlock` — den måste bli **flera block, ett
per tävling där någon i gruppen är ute**, i stället för dagens `FirstOrDefault`. Det står i
etapp 5 men är den enda punkten i planen som *lägger till* funktion i stället för att flytta den,
och den bör byggas i samma vända som fliken tas bort. Räcker det inte i skarp körning är svaret
en "Följer"-flik, och då är det ett nytt D7.

**Tab-badgen har ingen flik kvar att sitta på.** Punkten som säger "något händer live" satt på
Live-fliken. Den flyttar rimligen till **Tävlingar**; att badga fliken man redan står på (Hem)
säger ingenting.

**Djupet i Jag-fliken.** Jag → Mina resultat → Deltagare → Löparen är fyra nivåer i en stack.
Det är en nivå mer än i dag och accepteras medvetet: alternativet är att resultatet öppnas
utanför sin tävling, vilket är precis den uppdelning omläggningen tar bort.

**Två chip-rader ovanpå listan** (läge + urval) är mycket krom för en telefonskärm. Byggs som
skisserat, men mäts i etapp 3 — visar det sig tungt är klasschipet det som flyttar in i
sidhuvudet.

**Resultatlistans storlek** är den enda hårda tekniska risken, och den är avgränsad till §4.2.
Räcker inte cachen får resultatläget läsa live-källan även efter publicering, och den officiella
listan hämtas bara för den enskilda löparens sida.

---

## 6. Vad genomförandet ändrade i planen

Fyra saker såg annorlunda ut när de byggdes. Alla står med sina skäl i
[changeloggen](../../../../issues/deltagare-lage.md); i korthet:

1. **Resultatläget öppnar vid `Live`, inte vid `Finished`.** Den preliminära listan fylls på medan
   löpare kommer i mål. §3.2 gäller fortfarande som *förval*; det var *tillgängligheten* som satt
   för sent.
2. **Förvalet är "det mest framskridna läget som har något bakom sig".** Kalendern används bara
   som sista utväg. Att låta den välja och sedan falla tillbaka gav fel svar när verkligheten låg
   före den.
3. **`GetClassResultsAsync` fick en `splits`-parameter** (§4.2 nämnde den inte). Deltagarlistan
   vill inte ha sträckor; löparsidans analys behöver hela klassens. Bieffekten är att även
   löparsidan slutade hämta hela tävlingen.
4. **Startlistan fick en sorteringsväxel** — starttid eller Sverigelistan — i stället för att
   lägets namn tyst avgjorde vad som hände med #119.

`ParticipantRow` byggdes i etapp 3 tillsammans med sidan i stället för i etapp 2, så att formen
drevs av verklig användning i stället för av gissning.

## 7. Arbetsordning

1. Etapp 1 — läget som domän *(litet, rent, testbart)*
2. Etapp 2 — källorna, backend först
3. Etapp 3 — deltagarsidan, ett läge i taget
4. Etapp 4 — tävlingssidan blir nav
5. Etapp 5 — flikarna faller, Hem kompenserar
6. Etapp 6 — dokument och följdfynd

Etapp 1–2 kan gå parallellt med att etapp 3 skissas. Etapp 5 får inte gå in före etapp 3 är klar
i alla fyra lägen — mellanläget "fliken borta men listan halv" är det enda tillstånd användaren
inte ska se.
