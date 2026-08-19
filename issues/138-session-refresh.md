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

## Andra halvan: ett läge i stället för fyra halvsanningar

Förnyelsen tar bort de flesta fallen innan någon ser dem. Det här är för när den inte kan — när
lösenordet är borta, eller när det inte längre fungerar.

- **`EventorMessage`** — en rubrik och en förklaring per läge (`NoSession`, `Expired`,
  `Unreachable`), på ett ställe. Sidorna hittar inte längre på var sin mening om samma faktum.
- **Resultatlistan** säger "Inloggningen har gått ut" i stället för "Inga resultat ännu" när det är
  inloggningen som fattas. Frågan ställs bara när sidan är tom — en full lista behöver ingen
  förklaring, och frågan kostar ett anrop.
- **Startfältet** sa "0 av 36 finns på listan" både när ingen var rankad och när ingen kunde läsas.
  Är det inloggningen som fattas står det, med samma ord som resten av appen.
- **Resultatsidan** sa "Ingen anslutning" om en tävling som ligger utanför kalenderns fönster —
  över samma nätverk som listan bakom just laddats med. Den säger nu vad som faktiskt gäller: att
  tävlingen är äldre än kalendern appen läser, och att raden i listan har tid och placering.
- **Tävlingar och Live** ber nu också om förnyelsen.

**Verifierat:** build grön, `dotnet test` 393 gröna.

## Kvar

- **Inte kört skarpt.** Sessionen i simulatorn är giltig, så `Expired`-vägen har inte utlösts på
  riktigt. Den behöver provas med en död session — enklast genom att tömma sessionsfilen och
  behålla lösenordet.
