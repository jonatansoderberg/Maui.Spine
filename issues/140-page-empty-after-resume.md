# Issue #140 — Sidan som utlöste återinloggningen står tom en visning efteråt

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/140
**Branch:** issue/140-empty-page-after-resume
**Status:** In Progress

## Mätt först

Issuen bad om att kandidat 1 skulle mätas före allt annat. Det gjordes, på maccatalyst, med
spårutskrifter i `HomePageViewModel.OnAppearingAsync`, `ResultsPageViewModel.OnAppearingAsync` och
`EventorSessionResume.TryResumeAsync`, och med ett tillfälligt kodstycke som öppnade och stängde ett
ark åt sig självt (skärmstyrning var inte tillgänglig). Tre körningar, spåren nedan är från den sista.

**Kandidat 1 stämmer inte i den form issuen beskriver.** Att arket avvisas kör *inte*
`OnAppearingAsync` på sidan under det. Arket är en `UIViewController` i `.pageSheet`, presenterad
direkt mot UIKit — den presenterande vyn försvinner aldrig, så den visas heller aldrig igen:

```
PROBE opening sheet 08:35:21.020
PROBE closing sheet 08:35:23.038
PROBE switching to Results 08:35:26.041     ← ingenting däremellan
```

**Men två omgångar finns — vid start, på första fliken.** `OnAppearingAsync` körs två gånger
samtidigt på Hem: en gång ur `NavigationRegionViewModel.ResetAsync` när fliken förverkligas, och en
gång ur `SpineTabbedHostPage.OnAppearing`:

```
Home appearing IN ae75 dir=NavigateTo 08:35:17.210
Home appearing IN 5818 dir=None       08:35:17.244
```

Flikbyten ger en omgång, inte två. Så den dubbla laddningen är verklig men träffar bara appens
första flik vid start — den kan inte vara det som tömmer Resultat.

**Det som tömmer Resultat är kandidat 2, och den är strukturell snarare än en tidsslump.** Hem
förverkligas alltid först och når `TryResumeAsync` under den första sekunden. `_tried` sätts där och
då — före `AccessAsync`, alltså även när det inte fanns något att förnya. Alla senare frågor får
`false`:

```
Resume asked tried=False 08:35:17.935      ← Hem, vid start
Resume asked tried=True  08:35:26.259      ← Resultat, vid flikbytet
Results resume=false b1b7
Results appearing OUT b1b7 count=0 empty=True
```

Med en död session är det Hem som öppnar arket och Hem som får `true`. Resultat — sidan användaren
tittar på — får `false`, hoppar över sin omladdning och blir stående med listan den läste med den
döda sessionen. Och den generella texten följer av samma sak: `ExplainEmptinessAsync` frågar
`AccessAsync` *efter* att arket loggat in, får `Available`, och säger därför ingenting alls. Tom
lista, "Inga resultat ännu". Nästa visning laddar om och blir rätt.

Svaret på förnyelsen går alltså till en enda anropare, och det är sällan den som syns.

## Plan

### 1. `EventorSessionResume` bär utfallet i stället för en bock

`_tried` blir det pågående försöket, delat av alla:

- `EnsureAsync(INavigationService)` — startar försöket en gång per körning; den som frågar medan
  arket står öppet väntar in *samma* försök i stället för att få nej.
- `Generation` — räknas upp när en förnyelse faktiskt gav en ny session.

### 2. Sidorna frågar "gäller det jag läste med fortfarande?"

I stället för att lita på returvärdet från sitt eget anrop:

```csharp
var seen = _resume.Generation;

await LoadAsync(BuildAsync);
await _resume.EnsureAsync(_navigation);

if (_resume.Generation != seen)
    await LoadAsync(BuildAsync);
```

Rätt oavsett vem som utlöste arket, och en omladdning bara när sessionen verkligen bytts. Gäller
`HomePage`, `ResultsPage`, `ProfilePage`, `EventsPage` och `LivePage`.

### 3. `PageCache` får inte skriva över en tömd cache med det som lästes före tömningen

`Clear()` tömmer, men en läsning som redan var i luften skriver in sitt gamla svar efteråt. Två
flikar läser `Home/Index` samtidigt vid start, så en utloggad startsida kan hamna i cachen *efter*
inloggningen och stå kvar i fem minuter — varje sida tom under tiden. `Clear()` räknar upp en
generation, och `GetOrAddAsync` sparar inte ett svar som lästes i en tidigare.

## Changes

- **`Services/Eventor/EventorSessionResume.cs`** — `TryResumeAsync` bytt mot `EnsureAsync` och
  `Generation`. Försöket är ett delat `Task`: den som frågar medan arket står öppet väntar in samma
  försök i stället för att få nej. `Generation` räknas upp när arket faktiskt lämnat tillbaka en
  session — en avbruten inloggning, eller en som står kvar öppen för att lösenordet inte längre
  fungerar, ändrar ingenting och utlöser ingen omläsning.
- **`HomePage`, `ResultsPage`, `ProfilePage`, `EventsPage`, `LivePage`** — läser om när `Generation`
  skiljer sig från vad de började med, i stället för när deras eget anrop svarade ja. Rätt svar
  oavsett vem som utlöste arket.
- **`LivePage`** — laddningen låg inbakad i `OnAppearingAsync` och behövde ett namn för att kunna
  köras två gånger: `ReloadFieldAsync`. Anropar också förnyelsen *efter* sin första laddning i
  stället för före, så fliken visar det den har medan arket står öppet. `_me` nollas vid en ny
  session — inloggningen kan säga att appen läser som någon annan.
- **`ResultsPage`** — `ShowSkeleton = false` flyttad till efter första laddningen. Nu när varje sida
  väntar in förnyelsen skulle skelettet annars stå kvar över en färdig lista så länge arket är öppet.
- **`EventorReader.PageCache`** — `Clear()` räknar upp en generation, och `GetOrAddAsync` sparar
  inte ett svar som lästes i en tidigare. Utan det skriver en hämtning som redan var i luften in sin
  utloggade startsida i cachen inloggningen just tömt, och den står i sina fem minuter.
- **`Plugin.Maui.Spine/ViewModelBase`** — en visning är en annonsering. `SendAppearingAsync` och
  `SendDisappearingAsync` bär vakten, så alla vägar in går genom samma ställe: både regionens
  annonsering och den `NavigationService` gör direkt på ett ark före presentationen. `ResetAsync`
  säger till sidan den ersätter att den inte längre visas — den får aldrig veta det annars, och
  skulle annars bära ett inaktuellt "visas redan" resten av körningen.

**Verifierat:** `dotnet test` 394 gröna (den nya täcker cachen och faller utan rättningen), build grön
för maccatalyst. Kört skarpt på maccatalyst med samma spårutskrifter som mätningen: Hem annonseras en
gång vid start i stället för två, ett ark en gång per öppning i stället för två, och varje flikbyte
ger fortfarande exakt en omgång.

## Decisions

**Ett generationsnummer, inte ett returvärde.** `EnsureAsync` kunde ha gett `true` till alla som
väntar in försöket. Men den som frågar långt efteråt — ett flikbyte tio minuter senare — hade då
också fått `true` och laddat om i onödan, varje gång, resten av körningen. Frågan en sida behöver
svar på är inte "loggade någon in?" utan "gäller det jag läste med fortfarande?", och den kan varje
sida ställa själv.

**Sidorna väntar in förnyelsen efter sin första laddning, inte före.** Står arket kvar öppet för att
lösenordet inte längre fungerar väntar `EnsureAsync` så länge användaren gör det. Det är uthärdligt
bara om sidan redan visat det den har.

**Vakten sitter på sidan, inte på regionen.** Första försöket lade den i
`NavigationRegionViewModel`, vilket räckte för fliken vid start men inte för ark: `NavigationService`
annonserar arkets `OnAppearingAsync` direkt innan det presenteras — awaitad, så innehållet finns när
arket animeras in — och regionens `ResetAsync` annonserade sedan om. Sju ark i Orientera körde sin
`OnAppearingAsync` två gånger av det. Frågan "har den här sidan redan fått veta att den visas?" är
sidans egen, och då finns det ett ställe att svara på den i stället för ett per väg in.
