# Sessionsförnyelsen: ett tyst ark i stället för en helskärmsläsare

**GitHub:** _issue ej skapad än_
**Branch:** issue/okand-arena (staplad)
**Status:** Completed

## Uppdraget

När Eventor-sessionen förnyas ska det ske i ett litet ark med bara förloppsindikator och text.
Webbvyn göms, och visas först om inloggningen misslyckas.

## Vad som hände förut

`EventorSessionResume` öppnade `EventorLoginSheet` — helskärm, dimmad bakgrund, Eventors egen
inloggningssida — och fyllde i det sparade lösenordet åt läsaren. Sidan visades alltså för ett
formulär ingen skulle röra, ovanpå det de höll på att läsa. Eventor släpper sessionen efter ungefär
en och en halv timme, så en app som är öppen längre än så får se den två gånger.

## Vad som gjordes

Två ark, för att de två halvorna inte är samma sak att titta på:

- **`EventorResumeSheet`** — kvartshög, förloppsindikator, "Loggar in dig på Eventor igen" och
  varför. Webbvyn ligger kvar och driver Eventors formulär, men med `Opacity="0"` och
  `InputTransparent`.
- **`EventorLoginSheet`** — oförändrad. Öppnas av `EventorSessionResume` *efteråt*, och bara om
  det tysta försöket inte gick igenom.

`OnPageAsync` svarar nu om den lämnat tillbaka en session, vilket är vad det tysta arket behöver
för att veta att det inte längre ska döma om något.

**När ger det upp?**

1. **Direkt** när Eventor svarat på POST:en och inloggningsformuläret är tillbaka utan hälsning —
   det är lösenordet som nekats, och att vänta ut klockan hade bara fått ett fel lösenord att se
   ut som ett långsamt nät.
2. **Efter 20 sekunder** annars. Två navigeringar över vilket nät en arena nu har, plus en
   samtyckesruta som injiceras efter att sidan sagt sig vara laddad.

## Decisions

**`Opacity` och inte `IsVisible`.** En hopfälld vy mäts till noll, och skripten som söker upp
formuläret, avböjer samtycket och rullar rätt arbetar på en layout. Sidan ska ha samma yta att
lägga ut sig i som den skulle haft synlig — den ska bara inte ritas.

**Det synliga arket öppnas utan det sparade lösenordet.** Appen har just skickat det och blivit
avvisad; att skicka det igen automatiskt kostar bara ännu ett försök mot ett konto som kan låsas.
Läsaren får sidan med Eventors eget felmeddelande på.

**Nedräkningen är vaktad av `_settled`.** Timern går på sin egen klocka, och att stänga ett ark
som redan stängts poppar det som ligger bakom.

## Verifierat

Kört skarpt i simulatorn genom att skriva ett dött `ASP.NET_SessionId` i appens sparade session.
Arket kom upp som avsett, det tysta försöket gick igenom, och sessionen skrevs om — två gånger,
13:46:34 och 13:48:20. Ingen helskärmsläsare någon gång.

**Ej verifierat:** felvägen. Den kräver ett lösenord Eventor nekar, och det finns inget sätt att
framkalla utan att skriva ett falskt i nyckelringen.
