# Issue #75 — Demoläget läser aldrig den identitet man sparar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/75
**Branch:** issue/75-demo-identity
**Status:** Completed

## Plan

Identitetsarket sparade, men profilen visade den seedade löparen. Samma ark, samma knapp, två
olika utfall beroende på datakälla — och demoläget är det man designar och demonstrerar i.

Ärendet skisserade två utgångar: låta identiteten gälla (och acceptera att det seedade materialet
följer med), eller säga i arket att den inte används. Jag valde en tredje som ger det första utan
dess baksida.

## Changes

- `FakeDataSource` tar `LocalIdentityStore` och byter **namn, klubb och klass på den seedade
  löparen** — id:t behålls.
- `Runs(competition)` returnerar sina körningar med den omdöpta personen, vilket räcker för att
  resultat och livelistor ska följa med.
- `MauiProgram` kopplar in store:n.

## Decisions

- **Byt namn på henne, skapa inte någon ny bredvid.** `LocalIdentityStore.AsPerson` bygger ett
  nytt person-id (`me:...`). Använt rakt av i demoläget hade det ställt användaren utanför varje
  resultatlista i seeden — man hade fått sitt namn i profilen och "du är inte med i den här
  resultatlistan" överallt annars. Seeden är en säsong byggd kring **en** löpare; att behålla
  hennes id och byta vad hon heter gör demot till användarens eget.
- **Bara ett ställe.** Resultat- och liverader byggs båda ur `PlannedRun.Person`, och matchas
  tillbaka på namn och klubb (SP-04). Omdöpningen i `Runs` räcker därför för hela appen.
- **Klassen i ett resultat är ett faktum, inte en preferens.** Identitetens klass sätter
  `DefaultClass`, men ett löpt resultat behåller den klass det sprangs i. H45 i profilen och D21 i
  ett resultat från i somras är inte en motsägelse.

## Verifiering

`dotnet test`: 246 gröna.

**iPhone 17 Pro-simulator (iOS 26.2), demoläge, identitet Jonatan Söderberg / Gävle OK / H45:**

- Profilen visar namnet och klubben, med den seedade Sverigelistan under.
- Resultat listar de seedade loppen som mina.
- Hemlingbyloppets fält markerar **rad 4, Jonatan Söderberg, Gävle OK** som min.

Före ändringen visade allt detta Elin Norberg medan arket påstod sig ha sparat.
