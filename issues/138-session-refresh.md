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

## Kört skarpt

Sessionens tre inloggningskakor (`ple`, `pld`, `ASP.NET_SessionId`) fick döda värden i
`eventor-session.json`, så filen fanns kvar men Eventor kände inte igen den — vilket är `Expired`
och inte `NoSession`, alltså precis det läge förnyelsen finns för.

Appen öppnades på **Resultat**, inte på Hem. Det är hela poängen: före den här ändringen hade
ingenting hänt förrän man råkade öppna Hem.

1. Resultatfliken utlöste förnyelsen direkt.
2. Arket "Loggar in dig igen på Eventor. Fyll i själv om sidan frågar." öppnades och fyllde
   Eventors egen sida.
3. Det stängde sig självt, och Jag visade "Inloggad som Jonatan Söderberg, Gävle OK" med
   Sverigelistan omläst — placeringen hade flyttat sig 1921 → 1926, så siffrorna var nya.

Ingenting skrevs för hand.

## Kvar: en kapplöpning vid återkomsten

Resultatlistan var tom direkt efter förnyelsen, med den generella texten. Arket sparar sessionen,
tömmer läsarens cache och läser *sedan* kontot, medan sidan laddar om så fort arket returnerar — så
omladdningen kan hinna före den sista sparningen. Nästa visning är korrekt, men den första är det
inte.

Rätt form är troligen att arket returnerar först när det är klart med allt, eller att sidan lyssnar
på att sessionen ändrats i stället för att ladda om på returen. Inte utrett.
