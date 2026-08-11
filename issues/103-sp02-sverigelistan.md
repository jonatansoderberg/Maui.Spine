# SP-02 — Sverigelistan

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/103
**Branch:** spike/sp-02-sverigelistan
**Status:** Completed — **kvalificerat negativt**

## Sammanfattning

Sverigelistan finns **inte** i Eventors API. Den finns som webbsidor, uppdaterade dagligen, och
går tekniskt att läsa — men utan person-id, utan historik och utan något löfte om formatet.

Det finns en väg som är liten nog att försvara: **klubbsidan**. En sida per klubb och dygn ger
varje medlems poäng och rikslistplacering. Det är ett anrop, inte en svepning, och matchningen
sker inom en klubb i stället för över hela landet.

Men det är fortfarande skrapning av en sida ingen lovat oss. **Rekommendationen är att fråga
Svensk Orientering om ett API eller ett skriftligt ja innan något byggs.**

## De fyra frågorna

### 1. Maskinläsbar källa — nej

Eventors API dokumenterar **37 endpoints**. Ingen av dem rör ranking:

```
activities, activity, authenticatePerson, competitor, competitors, competitorcount,
entries, entryfees, event, eventclasses, events, events/documents, externalLoginUrl,
organisation, organisations, persons/organisations, results/event, results/organisation,
results/person, starts/event, starts/organisation, starts/person, …
```

Gissade endpoints (`/api/ranking`, `/api/rankinglist`, `/api/competitorranking` med flera) svarar
404. Sverigelistan finns bara som HTML under `/Ranking/ol/…`.

### 2. Historik — nej

Sidorna är ögonblicksbilder, märkta *"Uppdaterad 2026-08-11"*. Varken API:et eller sidorna
erbjuder en serie bakåt. Vill appen visa utveckling får den spara sina egna dagliga avläsningar
och bygga historiken själv — vilket också betyder att den börjar tom.

### 3. Rate limits — odokumenterade

Inget står i API-dokumentationen. För webbsidorna finns inget uttalat alls. `robots.txt` säger
`User-agent: *` → `Allow: /`, alltså inget förbud mot `/Ranking` för vanliga agenter, men listar
ett tiotal AI-crawlers som `Disallow: /` och sätter `Content-Signal: search=yes, ai-train=no,
use=reference`.

Att det är tillåtet att hämta är inte samma sak som att det är tänkt att byggas på. Frånvaron av
en gräns är inte ett löfte om att det inte finns någon.

### 4. Personmatchning — sämre än SP-04

Rankingsidorna innehåller **noll persondjuplänkar**. Ingen `personId`, ingen `/Athlete/`-länk,
ingenting. En rad är:

| # | Namn | Klass | Klubb | Poäng |
|---|---|---|---|---|
| 1 | Gustav Bergman | H35 | OK Ravinen | 0,00 |

Alltså exakt det underlag `RunnerIdentity` redan brottas med (SP-04) — namn och klubb — fast över
hela landet i stället för inom en tävling. Två löpare med samma namn i samma klubb är ovanligt;
över alla klasser och klubbar i Sverige är namnkollisioner en säkerhet, inte en risk.

## Klubbsidan — den enda vägen som är liten nog

`/Ranking/ol/Club/Index/{organisationId}` ger en klubbs hela lista:

| # | Namn | Klass | Rikslista | Poäng |
|---|---|---|---|---|
| 1 | Isa Envall | D21 | 5 | 3,30 |
| 2 | Annika Simonsen | D21 | 14 | 5,66 |

76 kB, 36 löpare, ett anrop. Det är attraktivt av tre skäl:

- **Ett anrop per klubb och dygn** — ingen bulkhämtning, vilket repots principer förbjuder.
- **Både poäng och rikslistplacering** står på raden.
- **Matchningen sker inom en klubb**, där namn + klass räcker långt. Appen vet redan användarens
  klubb-id genom `OrganisationDirectory`.

Kvar står ändå: det är HTML utan kontrakt. En layoutändring i Eventor tar sönder det tyst, och
felet dyker upp som att en löpares poäng plötsligt är borta.

## Rekommendation

**Bygg inte nu.** Fråga i stället, i den här ordningen:

1. Finns eller planeras ett API för Sverigelistan? Det är den fråga som gör allt annat onödigt.
2. Om inte — är det okej att en app hämtar **klubbsidan för användarens egen klubb**, en gång per
   dygn, med attribution?
3. Kan raderna få ett person-id? Utan det är varje koppling en gissning, och det är samma vägg
   som identiteten och personsökningen går in i (M5, #63).

Fram till dess står `GetRankingAsync` kvar på `null`, och Jag-fliken visar demodata i demoläget
och ingenting mot skarpt — vilket är repots princip och inte ett provisorium.

## Vad som *inte* undersöktes, med flit

Att skrapa riks- eller distriktslistorna. Det är bulkhämtning av andras data i den mening repot
förbjuder, och det hade dessutom gjort personmatchningen svårare, inte lättare.

## Nästa spike detta rör

Prediction (SP-11) landade negativt bland annat för att formunderlaget var tunt. Sverigelistan
skulle vara en input där. Så länge SP-02 står obesvarad står den delen av SP-11 kvar också.
