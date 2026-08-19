# Issue #142 — Logga in med appens egna fält ska vara vägen in, och ligga i Eventor-kortet

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/142
**Branch:** issue/142-app-login-default
**Status:** Completed

## Plan

`AppLoginSheet` byggdes för att vägas mot `EventorLoginSheet` på en riktig telefon (#123) och låg
därför under Utvecklingsläge — ett försök, inte ett val användaren skulle ställas inför. Valet är
gjort. Fälten blir vägen in, och de hör hemma i Eventor-kortet på Jag.

## Changes

- **`ProfilePage`** — Eventor-kortets knapp öppnar `AppLoginSheet`. De två kommandona blev ett:
  `OpenAppLogin` är borta och `OpenEventorLogin` öppnar fälten. Knappen under Utvecklingsläge är
  borttagen — två inloggningsknappar bredvid varandra är förvirrande, vilket var hela skälet till
  att den låg där den låg.
- **Välkomsten** — första körningens inloggning gick fortfarande till Eventors sida. Den första
  inloggningen en användare möter ska inte vara den vi valt bort, så `HomePage.ScheduleWelcome`
  öppnar samma ark som Jag.
- **Två meningar som slutade stämma** — Sverigelistans tomma text och välkomstens förklaring sa
  båda "du loggar in på Eventors egen sida". Nu säger de vad som gäller: uppgifterna stannar på
  telefonen och skickas bara till Eventors egen inloggning.
- **`AppLoginSheetViewModel`** — resonemanget skrevs om från "ska utvärderas, inte ersätta" till
  vad som nu är beslutat, med priset kvar i klartext.

**Verifierat:** build grön för maccatalyst och iossimulator, `dotnet test` 393 gröna. Kört på
simulatorn: knappen i Eventor-kortet öppnar "Logga in med Eventor-konto" med användarnamn och
lösenord.

## Decisions

**Priset står kvar nedskrivet.** Argumentet mot fälten var aldrig att de inte fungerar, utan att en
löpare som lärt sig skriva sitt Eventor-lösenord i en app som inte är Eventor har lärt sig den vana
nätfiske lever på — och att adressfältet inte går att kontrollera när sidan inte syns. Det gäller
fortfarande. Att välja bort ett argument är inte att motbevisa det, så det står kvar i
`AppLoginSheetViewModel` som en känd kostnad i stället för att strykas.

**Utmaningen offras inte.** Fälten loggar inte in. De skriver ned lösenordet och lämnar över till
`EventorLoginSheet`, som fyller i Eventors eget formulär och skickar det. Går det inte igenom står
förbundets egen sida redan framme — vilket är det som gör att en andra faktor fortfarande kan visas.

**En knapp, inte två.** Webbvägen har ingen egen knapp längre. Den är inte borta — den ligger i
flödet, direkt efter fälten — men att erbjuda båda sida vid sida vore att be användaren välja mellan
två saker som gör samma sak.
