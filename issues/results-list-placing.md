# Resultatlistan: snabbare fram, och placeringen som en placering

**GitHub:** _issue ej skapad än_
**Branch:** issue/results-list-placing
**Status:** Completed

## Plan

Fyra saker på Resultat: sidan tar lång tid innan den visar något, placeringen bär ett ordningstal
den inte behöver, den saknar fältets storlek, och placeringssiffran har ingen egen bredd så
tävlingsnamnet flyttar sig i sidled rad för rad. Podieplatser ska dessutom synas som podieplatser.

Väntetiden var strukturell. `BuildAsync` hämtade en tävling i taget, i tur och ordning, för varje
resultat som låg utanför kalenderfönstret — och kalendern är några månader bred, så en hel säsong
blev ett trettiotal HTTP-anrop efter varandra innan den första raden ritades. Det enda de anropen
egentligen bidrog med var distansmärket; namnet och datumet bär resultatet redan själv, eftersom
Eventors egen resultatsida namnger varje etapp för sig.

## Changes

- `ResultsPage.ViewModel.cs` — `MyResultRow` är en `ObservableObject` i stället för en `record`, och
  listan ritas färdig av det resultatet självt bär. Distansen fylls i efteråt, fyra tävlingar åt
  gången, in i rader som redan står på skärmen. Bara de resultat som varken kan namnge eller datera
  sig väntas in före ritningen — de kan inte bli en rad utan — och även de hämtas parallellt.
- `Format.cs` — `PlaceNumber` (siffran utan ordningstal), `Medal` (guld, silver, brons för
  placering 1–3) och `OutOf` ("av 91", tomt när fältets storlek är okänd).
- `ResultsPage.View.xaml` — placeringen är en egen kolumn med fast bredd: medalj överst, siffran i
  mitten, "av x" under i `CaptionLabel`. En pallplats visar bara medaljen — den säger redan vilken
  av de tre det var — men behåller "av x".
- `Components.xaml` — `PlaceColumnWidth`, 56, brett nog för tre siffror i `SizeDisplay`.
- `Typography.xaml` — `MedalLabel`, 24 punkter. Egen storlek och inte ett steg på typskalan: en
  emoji ritas större än en siffra i samma punktstorlek, och medaljen ersätter siffran i stället
  för att kommentera den.
- `FormatTests.cs` — de tre nya formaterarna.

### "Du är inte med i den här resultatlistan" — fast man var det

LiU Indoor sa så, med 285 resultat totalt, medan `1:a Söderberg Jonatan, Gävle OK` stod i klassen
GUBBAR längre ner på samma sida. Listan skriver **efternamnet först, utan kommatecken**, och
`RunnerIdentity.Reorder` vänder bara på namn som har ett. "söderberg jonatan" och "jonatan
söderberg" blev därför två personer.

- `RunnerIdentity.Matches` jämför namndelarna i sorterad ordning i stället för som skriven sträng.
  `Name` och `Key` är orörda, så lokalt sparade id:n (`me:namn|klubb`) betyder fortfarande samma sak.

Det slår igenom överallt jämförelsen används: resultatsidan, live-listan, startfältet — och
"av x" på Resultat, som hämtar sin siffra ur samma matchning.

### Att öppna ett resultat tog timeout

"Ingen anslutning. Resultat och sträcktider behöver nätverk." på en tävling som svarar på en
kvarts sekund. Backendloggen visade vad som faktiskt tog tid: inte resultatlistan utan
`GetCompetition` — 20 016 ms, det vill säga precis över appens timeout. En kall tävlingshämtning
är fem anrop till Eventor efter varandra (eventet, dess dokument, dess klasser, schemat och första
start); varm tar samma anrop en sekund.

Fyra ändringar, i den ordning de spelar roll:

- `ResultsDetailPage.BuildAsync` — resultatlistan och tävlingen hämtas **samtidigt** i stället för
  efter varandra. Sidan väntar på den längsta i stället för på summan.
- `ResultsDetailPage.OnAppearingAsync` — **två försök** innan det kallas en störning. Backenden
  överger inte en hämtning för att den som frågade lade på, så andra frågan hittar den ofta klar.
- `ResultsPageViewModel` — bakgrundsifyllningen **avbryts när man lämnar sidan**, och när listan
  byggs om. Verifierat i loggen: anropen bryts efter 1–4 sekunder i stället för att ligga kvar.
- `Politely` — **två samtidiga i stället för fyra**. Fyra gjorde Eventor långsamt nog att ett tryck
  som kom mitt i ifyllningen fick vänta ut den.

Valbos nationella, som förut sa "Ingen anslutning", öppnar nu med "33:e 33/34" och hela H21-listan.

### Sträckor och Analys gick inte att trycka på

De var avstängda, och såg inte avstängda ut. `IsEnabled="{Binding HasSplits}"` stoppar trycket i
plattformsvyn, men `ChipView` ritade sig likadant ändå — så ett ord som inte gjorde något när man
tryckte såg exakt ut som det bredvid som fungerade. LiU Indoors resultatlista bär inga sträcktider
alls (0 av 285 rader), så där finns ingenting att visa.

- `ChipView` — opacitet 0,4 när chipet är avstängt, och "Inte tillgängligt" som uppläst ledtråd.
  Opacitet och inte en färg: chipets färger byts mellan två färdiga `Border`ar just för att ingen
  trigger ska behöva minnas en.

Gäller alla chip — filtren på Tävlingar och lägesväljaren på Live med.

### "Logga in igen under Jag" — från en app som kan logga in själv

`EventorSessionResume.EnsureAsync` var `_attempt ??= AttemptAsync(...)`, alltså ett försök per
appkörning. Två sätt att fastna i det, och båda inträffar i vanlig användning:

1. **Första frågan kom medan sessionen levde.** Hem frågar inom en sekund från start, `AccessAsync`
   svarar att allt är bra, och `_attempt` står kvar som en färdig tom uppgift. När sessionen dör
   en halvtimme senare returnerar `??=` den gamla — inget försök görs, någonsin mer den körningen.
2. **Sessionen dog två gånger.** Eventor släpper sessioner efter en och en halv timme; en app som
   är öppen längre än så ser det mer än en gång.

- `EventorSessionResume` — ett färdigt försök är inte ett pågående, så det nollställs och ett nytt
  görs varje gång sessionen har gått ut. Uppgifterna är sparade; då ska appen logga in, inte be
  någon annan göra det.
- Det enda som inte görs om är att spela upp uppgifter som redan blivit nekade. De prövas en gång,
  rutan lämnas stående på Eventors egen sida — det enda som kan lösa det, och det som fortsätter
  fungera den dag förbundet lägger till en andra faktor — och prövas inte igen förrän det sparade
  har ändrats. Jämförelsen görs mot ett fingeravtryck och inte mot uppgifterna själva: frågan är
  bara "är det samma som föll", och ett lösenord är inte värt att ligga i ett fält appen ut.

## Decisions

- **Raden fyller i sig själv i stället för att ritas om.** Därför `ObservableObject`: grupperingen
  i `ResultSeason` är en vanlig `List`, så ett utbyte av ett element hade inte synts. Nu ändras
  raden på plats och bindningen gör resten.
- **Kalendern får bara tala för det lopp den beskriver.** Samma regel som förut, nu även i
  ifyllningen: en behållare som kallar fyra medeldistanser "Lång" är värre än inget märke alls.
- **"av 0" skrivs inte ut.** En placering utan känt fält är fortfarande hela det faktum raden
  finns för; en påhittad nämnare vore sämre än tystnad.
- **Ordningstalet hör hemma i en mening, inte i en kolumn.** `Format.Place` står kvar orörd — Hem,
  resultatdetaljen och Jag skriver placeringen mitt i en text och behöver "3:e" där.

### Klasserna låg i ingen ordning

Resultatsidan sorterade klasserna alfabetiskt, vilket är vad en dator gör med klassnamn och vad en
löpare läser som slump: "Blå 3,0" ovanför D10, D2 ingenstans i närheten av D21, de öppna banorna
utspridda mellan åldersklasserna.

- `ClassOrder` (ny) — tre grupper i den ordning en resultatlista läses: **huvudklasser, ungdom,
  öppna banor**. En åldersklass är en bokstav och en ålder (D21, H45, HD12); tjugo är sista
  ungdomsåret. Allt annat är en bana vem som helst får springa — "Öppen 5", "Blå 3,0", "Gubbar" —
  och de kommer sist.
- Inuti en grupp gäller arrangörens egen ordning. Den finns redan i `Competition.Classes`, hämtad
  ur `eventclasses`, och är den ordning löparen har läst på anmälningsblanketten och på Eventor —
  den är också det som håller D21 bredvid H21 i stället för elva D-klasser följda av elva H. Det
  arrangörslistan inte namnger sorteras på vad ett klassnamn är gjort av: bokstäverna först, sedan
  talet som ett tal, så att D21 hamnar mellan D18 och D35 i stället för mellan D2 och D3. En
  etappklass ("H45, Etapp 3") rankas som sin klass.
- `ResultsDetailPage.BuildField` — egen klass först som förut, därefter den ordningen.
- `ClassOrderTests` — sex fall.

## Fältets storlek: fråga Eventor om sig själv

Mätt: `competitions/50594/results` är **86 081 042 byte och 97 sekunder** när man låter den gå
klart. Backendens Eventor-klient ger upp efter 20 s, och därför gick O-Ringen varken att få en
siffra till eller att öppna. Att höja timeouten hade bytt "Ingen anslutning" mot 86 MB över
mobildata.

Eventors API har svaret ([dokumentationen](https://eventor.orienteering.sport/api/documentation)):
`results/person` tar en person och en lista av tävlingar och svarar med just den personens rader.
O-Ringens fem etapper: **1 723 byte på 0,38 sekunder**, med sträcktider 7 933 byte på 0,66.

- `EventorSource.GetPersonResultsAsync` + `EventorNormalizer.PersonResults` — `results/person`,
  normaliserat per tävling ur den `ResultListList` den svarar med.
- `CompetitionFunctions.GetPersonResults` — `results/person?person=&events=&splits=`.
- `IParticipationSource.GetOwnResultsAsync` — egna rader i givna tävlingar. Skild från
  `GetResultsForPersonAsync`, som svarar på *vilka* tävlingar som var mina och läses från Eventors
  egna sidor.
- `ResultsPageViewModel.FillFieldAsync` — **ett** anrop för hela säsongen i stället för ett per
  tävling. Raderna matchas på tävling och placering, eftersom en flerdagars är en tävling och fem
  lopp.
- `ResultsDetailPage` — hela resultatlistan först, som förut, och där den inte går att hämta de
  egna raderna i stället, med en rad som säger varför. Sträckor och Analys hålls avstängda i det
  läget: sträcktider mäts mot klassen, och med bara den egna raden blir varje sträcka "bäst i
  klassen" — en siffra som ser ut som en analys utan att vara en.

**Vägen som inte fanns:** `wrsresults/event?classId=` står i dokumentationen som "results for a
class of an event" och hade varit det exakta svaret för resultatsidan. Den svarar 404 för allt vi
prövade — den hör till världsrankingen. `top` på `results/person` gav noll rader i stället för de
utlovade "besides the specified person, this number of competitors from the top". Ingen av dem
finns kvar i koden.

### "av 935" i en klass med ett par hundra

Klassens eget antal räknar **hela tävlingen, inte ett lopp**. Karlstad Indoors Herrar säger 91 för
två lopp om 44 och 47; O-Ringens H45 säger 935 för fem om ungefär 187. Normaliseraren använde det
talet som fältstorlek så fort loppets eget saknades, och fyra av fem etapper fick en nämnare fem
gånger för stor — på en rad vars hela poäng är placeringen.

- `EventorNormalizer.Results` — för en flerdagars används bara loppets eget antal
  (`ClassRaceInfo/@noOfStarts`). Saknas det står raden utan siffra, för ett tal från fel lopp säger
  mindre än ingen nämnare alls.
- Samma metod tar nu `partial`, satt av `PersonResults`: en lista som bara innehåller den man
  frågade om kan inte räknas — den hade svarat "av 1".
- `MultiRaceResultTests` — två nya fall, och en fixtur där andra loppet inte säger hur många som
  startade.

Utfallet på O-Ringen: etapp 4 säger 180 och har sin siffra; etapp 1, 2, 3 och 5 säger ingenting om
sitt eget fält, och då står det ingenting.

### Varför siffran uteblev för de flesta

Första körningen gav "av x" på två rader av trettionio. Backendloggen sa varför: av 23 anrop
avbröts sju efter **exakt 20 000 ms** med `TaskCanceledException` i `ResponseCache.GetOrAddAsync` —
appens egen HTTP-timeout mot backenden. En tävling Eventor inte fått en fråga om på ett tag tar
längre än så att hämta och normalisera.

`ResponseCache` startar hämtningen med `CancellationToken.None` och avbryter bara *väntan*, så
arbetet fortsätter och resultatet ligger kvar i cachen. Alltså räcker det att fråga en gång till:
andra frågan hittar en hämtning som redan är på väg i stället för att starta om den. Två försök är
fyrtio sekunders tålamod för en siffra som fylls i bakom en lista man redan kan läsa.

Efter ändringen, kall backend: 29 anrop, tre avbrutna. Siffran står på i stort sett varje rad.

## Verifiering

`dotnet test`: 397 gröna.

**iPhone 17 Pro-simulator (iOS 26.2), mot den lokala backenden:**

| Test | Utfall |
|---|---|
| Öppna Resultat | listan står färdig direkt, med distansmärken |
| Placeringen | "33", "91", "109" — inget ordningstal |
| Placeringskolumnen | fast bredd, tävlingsnamnen börjar på samma x rad efter rad |
| "av x" under siffran | "33 av 34" på Valbos nationella, "14 av 18" på Faxeträffen |
| Medalj för placering 1–3 | 🥉 på DM sprint, 🥈 på Älgsprinten — kolumnen håller sin bredd |

**Klassfiltret, mot den lokala backenden:** `competitions/53725/results` 363 kB / 278 rader / 32
klasser, `?class=H45` 4,5 kB / 3 rader / en klass, `starters` satt.

**LiU Indoor, som sa att jag inte var med:** listraden visar 🥇 och "av 41", och resultatsidan
öppnar på GUBBAR med "1:a 1/41" och min rad markerad. Backendens egen data säger `place 1`,
`starters 41` — samma siffror.

**Kall backend, andra körningen:** i stort sett varje rad fick sin siffra — "av 3", "av 5",
"av 13", "av 16", "av 18", "av 43". Valbos nationella fick sin på nästa besök på fliken, vilket är
precis vad cachen är till för: första frågan betalade för hämtningen, andra hittade den färdig.

**Chipen:** på LiU Indoor, som saknar sträcktider, står Sträckor och Analys nedtonade. På Valbos
nationella, som har dem, är de fulla och "Sträckor" öppnar sträcktidstabellen.

**Att öppna ett resultat:** Valbos nationella öppnar med översikt och klasslista där den förut sa
"Ingen anslutning". O-Ringen gör det fortfarande inte — se nedan.

**Där siffran uteblir:** O-Ringens fem rader. `competitions/50594/results` svarar 502 efter 20 s —
backendens egen Eventor-klient ger upp, så ingen mängd tålamod i appen hjälper. Det felet fanns
före den här ändringen och hör hemma i backendens timeout, inte här. Faxeträffen dag 2 saknar
också sin: klassens resultatlista har ingen rad för mig på den placering Eventors egen sida uppger
för den etappen, och då står siffran hellre tom än gissad.
