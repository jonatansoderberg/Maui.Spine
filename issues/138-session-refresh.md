# Issue #138 — Behåll inloggningen: förnya Eventor-sessionen automatiskt

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/138
**Branch:** issue/138-session-refresh
**Status:** In Progress

## Vad som faktiskt saknades

Planen antog att den tysta återinloggningen behövde byggas. Den fanns redan — men **bara på Hem**.
`HomePageViewModel.ResumeEventorAsync` läste `AccessAsync`, hittade `Expired`, läste lösenordet ur
`SecureStorage` och öppnade `EventorLoginSheet` med `UseSavedPassword: true`, som fyller Eventors
egen sida åt användaren.

Följden: en session som dog medan man läste resultat förblev död tills man råkade öppna Hem. Det är
precis vad som hände under testkörningen av #136 — resultatlistan tömdes, Sverigelistan försvann,
anmälan sa "du behöver vara inloggad" och startfältet tappade sin ranking. Fyra sidor med var sin
halvsanning om ett och samma faktum, och ingen av dem sa "utloggad".

Ingen ny mekanik behövdes alltså. Det som behövdes var att flytta den ut ur en sida.

## Changes

- **`Services/Eventor/EventorSessionResume.cs`** — Hems logik, flyttad och gjord till en singleton.
  "En gång per körning" är nu tjänstens löfte i stället för varje sidas egen bool, så två flikar
  som visas efter varandra inte kan försöka två gånger.
- **`HomePage`** anropar tjänsten i stället för att äga den, och tappar två beroenden.
- **`ResultsPage`** ber om samma sak. Det är sidan som töms av en död session och där felet syntes.
- **`ProfilePage`** likaså — sidan som säger vem Eventor tror att du är borde inte vara den enda
  som inte försöker väcka en somnad session.

**Verifierat:** build grön för maccatalyst, `dotnet test` 393 gröna.

## Kvar

- **Tävlingar och Live** ber inte om förnyelsen. De läser inte Eventor direkt i dag, men startfältet
  på tävlingsdetaljsidan gör det. Ett anrop till på var sida.
- **Inte kört i appen.** Sessionen i simulatorn är giltig just nu, så `Expired`-vägen har inte
  utlösts skarpt. Den behöver provas med en död session — enklast genom att låta den ligga tills
  Eventor släpper den, eller genom att tömma sessionsfilen och behålla lösenordet.
- **Ett läge i stället för fyra halvsanningar.** När förnyelsen inte lyckas säger sidorna
  fortfarande var sin sak ("Ingen anslutning", "Inga resultat ännu"). Det är den andra halvan av
  issuet och rör ordval på fyra sidor.
