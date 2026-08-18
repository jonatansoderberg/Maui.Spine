# Issue #136 — Etapp C steg 3: resultat och analys

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/136
**Branch:** issue/136-results-analysis
**Status:** In Progress

## Changes

1. **Tomt läge visas aldrig under en hämtning.** `IsIdle` sätts när en hämtning är klar och
   ingenting kom, i stället för att härledas ur `HasResult` — som är falskt under hela de fyra
   sekunder hämtningen tar. Resultatlistan får skelettkort i listans egen form i stället för
   snurran, så sidan fylls i i stället för att byta utseende.
2. **Rött betyder något igen.** Tappet mot vinnaren är neutralt tills det är materiellt, och då
   `SignalUrgent` — dess tredje tillåtna användning (D1). Vinnarens marginal ned till tvåan är
   fortsatt grön. Samma regel på båda sidorna, så resultatlistan och resultatsidan inte kan säga
   olika saker om samma lopp.
3. **Kolumnrubrikerna skrivs ut** — Sträcka och Totalt i stället för STR och TOT.
4. **Jämförelsetabellen får en rubrikrad** med de två löparnas förnamn över sina kolumner, och
   Diff över differensen.

**Verifierat:** build grön för maccatalyst och ios. Tomt läge kontrollerat i appen — det ritas utan
snurra ovanpå. **Inte sett med data:** kontot i den här simulatorn har inga resultat, så
radfärgerna, skelettet, sträcktabellens rubriker och jämförelsens rubrikrad är byggverifierade men
inte granskade mot riktiga siffror. De behöver ett svep med ett konto som har resultat innan de kan
kallas klara.

## Decisions

- **Gränsen för ett materiellt tapp är en tiondel av vinnarens tid.** Den skalar, vilket ett
  absolut tal inte gör — en minut är ett nederlag på en sprint och ett bra lopp på en långdistans.
  Tio procent är en vald linje och ingen uppmätt, och den ligger på ett ställe per sida om den ska
  flyttas.
- **Tre färdigstylade etiketter i stället för en trigger** för differensen, av samma skäl som
  `ChipView` dokumenterar: en trigger minns färgen den ersatte och lämnar tillbaka fel temas färg
  efter ett temabyte.

5. **Motstridiga placeringssiffror lagade.** Två källor för en och samma sak:
   `RaceStoryFacts` räknade klassens storlek ur startfältet medan Översikt läser
   `CompetitionResult.Starters` — därav "33:e plats av 38 startande" under en rubrik som sa 33/34.
   Sammanfattningen läser nu samma tal som sidan visar, och `field`-parametern föll bort med det.
   Och "gled ner till 34:e plats i mål" var fel ord: `PositionAfter` är placeringen *vid en
   kontroll*, bland dem som har sträcktider där, med upploppet kvar. Den säger nu "vid sista
   kontrollen", vilket är det den vet.

**Testerna:** `dotnet test` — 393 gröna. Tio föll när jag körde sviten första gången, alla från
datumformatet i steg 1 (#131): `FormatTests` pinnade ordningstalsformen som fyndet bad oss ta bort.
Jag körde aldrig testerna i det steget, och de gick mergade till `master` röda. Testerna beskriver
nu regeln som gäller.

## Kvar

- **Fynd 5, motstridiga placeringssiffror.** Översikt säger 33/34, analystexten "33:e plats av 38
  startande" och avslutar med "34:e plats i mål" — tre fältstorlekar och två placeringar för samma
  lopp. Det kräver att analystextens siffror spåras till samma källa som Översikt läser, och är
  inte gjort här.
- **Årsrubriker och säsongssammanfattning** i resultatlistan (skärm 26), och normaliseringen av
  klasskolumnen som blandar klasser och banor (skärm 27). Båda hör till samma sida men är egna
  ändringar.

## Testat på resultatsidan

Öppnad från listan med riktiga data:

- **Tomt läge kommer först efter laddningen.** Snurran går ensam, och först när hämtningen är klar
  står svaret. Det var fyndet, och det är lagat.
- **Nytt fynd:** när hämtningen misslyckas står "Inget resultat ännu — Ingen anslutning. Resultat
  och sträcktider behöver nätverk." Appen har uppenbart nätverk; listan bakom laddades nyss. Det är
  `LoadAsync` som tolkar varje misslyckad källa som en utebliven anslutning, och sidan upprepar det
  som en förklaring den inte har täckning för. Att en tävling saknar resultatdata hos källan är
  något annat än att telefonen är offline, och de ska inte säga samma sak.
- **Fortfarande osett:** sträcktabellens utskrivna rubriker och jämförelsens rubrikrad. De
  tävlingar jag kunde öppna lämnar inga sträcktider från den här källan, så tabellerna ritas aldrig.
  De är bygg- och testverifierade men inte sedda.

## Det som såg ut som fel tävling — och inte var det

Jag skrev att två rader öppnade samma tävling. **Det stämde inte.** Rubriken sätts i `BuildAsync`
efter att tävlingen hämtats, så en hämtning som faller tidigt lämnar den förra sidans rubrik kvar.
Det var det jag såg.

Den verkliga orsaken: backendens kalender börjar 2026-04-20 och innehåller inte Leksandstrippeln
(april), Älgsprinten eller Veteranträffen (maj). Resultatlistan kommer från Eventors egen historik
och når längre bak än kalendern gör. `GetCompetitionAsync` ger då null, `BuildAsync` returnerar
direkt, och sidan visar gammal rubrik plus "Ingen anslutning".

**Två riktiga fynd faller ur det:**
- En sida som inte kunde hämta sin tävling behåller föregående sidas rubrik.
- "Ingen anslutning" sägs om något som inte är ett nätverksfel. Att en tävling ligger utanför
  kalenderns fönster är något annat än att telefonen är offline.

Båda hör hemma i en egen omgång; ingen av dem kommer ur den här ändringen.

## Testat med data på resultatsidan

Valbos nationella (16 aug, finns i kalendern) laddar helt:

- **Siffrorna stämmer överens.** Analystexten säger "du gick i mål som 33:a av 34 på tiden 1:15:50,
  +50:28 efter vinnaren" — samma tal som Översikt visar. Testkörningens motsägelse (33 av 38, och
  34:e i mål) är borta.
- **"Efter vinnaren +50:28" står i orange**, som det materiella tapp det är.
- **Sträcktabellen:** de utskrivna rubrikerna radbröts mitt i ordet — "STRÄC KA", "TOTAL T" —
  eftersom kolumnerna är 44 punkter breda. Förkortningarna är tillbaka och förklaras i stället en
  gång ovanför tabellen, vilket är fyndets egen andra väg: "skriv ut rubrikerna **eller** lägg en
  förklaring som säger dem en gång".

**Kvar osett:** jämförelsens rubrikrad, som ligger längre ned i Analys-fliken.
**Kvar olagat:** "Stabilitet 0,36" är fortfarande ett tal utan skala.
