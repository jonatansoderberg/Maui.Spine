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
