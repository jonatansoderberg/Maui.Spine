# SP-11b — duger Sverigelistan som prior för prognosen?

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/111
**Branch:** spike/sp-11b-ranking-prior
**Status:** Completed — positivt utfall, se Verdikt

## Frågan

SP-11 (#40) landade negativt och pekade ut fyra saker som skulle flytta modellen. Först på listan:
Sverigelistan som prior, "en löpares ranking finns för hela fältet, även för dem som saknar
historik hos oss". Nu finns Sverigelistan (#105). Innan modellkod skrivs: **duger priorn?**

Två frågor, båda mätbara utan att skriva en rad modellkod:

1. **Täckning** — hur stor del av ett riktigt startfält går att få en ranking för?
2. **Signal** — hur väl förutsäger rankingen *före* loppet den faktiska placeringen?

## Metod

**Loppet:** Tjällmoträffen 2026-08-11 (Eventor 53683), sprunget dagen före mätningen. Fyra klasser
med riktiga fält: H21 (44), D21 (30), H50 (31), H45 (17) — 122 löpare.

**Facit:** Eventors egna resultat (`/api/results/event?eventId=53683`), som ger `personId` och
placering. Ingen namnmatchning mot LiveResults behövs, alltså inget matchningsbrus i mätningen.

**Priorn, läckagefritt:** dagens ranking innehåller loppets eget resultat och duger inte. Men
löparsidan listar varje resultat med datum och poäng, så snittet rekonstrueras som det såg ut
dagen före: de sex bästa av resultaten i de tolv månaderna **före** 2026-08-11. Loppet självt och
allt efter det utesluts.

**Måttet:** Spearmans rangkorrelation mellan rekonstruerad ranking och faktisk målgång, per klass.
Att slå ihop klasser vore meningslöst — de sprang olika banor mot olika folk.

## Resultat

| Klass | Fält | Sida läsbar | Sex resultat bakåt | Par | ρ | Median rangfel |
|---|---|---|---|---|---|---|
| H21 | 44 | 43 | 40 | 39 | **0,815** | 4,0 |
| D21 | 30 | 30 | 20 | 19 | **0,539** | 5,0 |
| H45 | 17 | 17 | 14 | 12 | **0,797** | 1,5 |
| H50 | 31 | 31 | 25 | 25 | **0,892** | 2,0 |

**Täckning:** 121 av 122 sidor läsbara genom sessionen — **99 %**. Med sex resultat bakåt, alltså
en användbar ranking: **95 av 122 = 78 %**. De resterande 22 % har en sida men för lite historik.

## Verdikt

**Priorn duger, och den är stark.** ρ mellan 0,54 och 0,89, i tre av fyra klasser omkring 0,8.
Medianfelet är 1,5–5 placeringar i fält på 12–39. Det är precis den signal SP-11 saknade, och den
finns för fyra av fem i ett startfält.

Det gör också modellens form tydlig: ρ ≈ 0,8 är **stark men inte avgörande**. Den tionde bästa i
H21 kom 31:a; den tredje bästa kom 10:a. En prognos byggd på det här måste fortfarande vara ett
intervall, inte ett tal — men intervallet kan bli smalare än de 57 % av fältet SP-11 behövde.

Rekommendationen är alltså att ta om modellen, som ett eget arbete, med:

- Sverigelistan som prior för de 78 % som har en, och LiveResults-formen som i dag för de övriga.
- **Priorn måste rekonstrueras per datum, inte hämtas som nuläge.** Backtesten mot SP-11:s 1 132
  prognoser kräver rankingen som den var före varje lopp, och löparsidan har det som behövs.
- Kostnaden för backtesten är ~2 400 löparsidor genom sessionen. Det är ett eget beslut att fatta
  innan den körs, inte något att glida in i.

## Två fel i mätningen, båda funna och rättade

Båda hör hemma bland fällorna, för båda gav *trovärdiga men falska* siffror.

1. **Kakan låg på en `#HttpOnly_`-rad.** Cookiejar-filen från curl markerar sessionskakan så, och
   parsern hoppade över rader som börjar med `#`. Hämtningarna var alltså **anonyma** — och gav
   ändå 29 av 44 H21-sidor, eftersom elitlöpares sidor är publika. Första mätningen sa därför
   "34 % täckning" när det rätta var 99 %. En anonym hämtning som delvis lyckas är farligare än en
   som misslyckas helt.
2. **En död session ser exakt ut som en betalvägg.** Sessionen tar slut efter några minuter, och
   sidan svarar då med avgiftssidan — samma sidstorlek varje gång, 30 674 byte. H45 gav 0 av 17 och
   H50 1 av 31, vilket lästes som "de klasserna är inte tillgängliga". Det var sessionen som dog
   mitt i D21. Kontrollen som avslöjade det: hämta samma person anonymt och med session och jämföra
   — 8 av 8 gav avgiftssida anonymt och läsbar sida med session.

Praktiskt: **förnya sessionen var tjugonde anrop**, och behandla avgiftssidan som "vet inte" i
stället för som "finns inte".

## Inte gjort

- **Ingen modellkod.** Spiken svarar på om priorn duger, inte hur den vägs in. `GetPredictionAsync`
  svarar fortfarande tomt.
- **Ett lopp, fyra klasser.** ρ per klass vilar på 12–39 par. Riktningen är entydig, storleken är
  det inte.
- **D21 sticker ut nedåt** (0,539 på 19 par). Kan vara brus, kan vara att fältet var tätare. Värt
  att titta på när backtesten körs på fler lopp.
