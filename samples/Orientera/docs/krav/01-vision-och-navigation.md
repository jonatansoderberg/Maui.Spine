# 1. Vision, principer och navigation

## Vision

Orientera ska vara den ultimata personliga orienteringsappen för Sverige. Den samlar det som idag är utspritt över flera tjänster, men framför allt **tolkar, prioriterar och presenterar** informationen så att användaren slipper navigera i systemens interna struktur.

**Produktlöfte:** Orientera hjälper dig före, under och efter tävlingen – och blir smartare ju mer den lär känna vad som är relevant för dig.

**Positionering:** Orientera är inte en mobil rendering av Eventor. Externa system är källor och destinationspunkter – Orientera äger sammanhanget och användarupplevelsen.

## Produktmål

- Phone-first, byggd för iPhone + Android från dag ett.
- Riktigt användbar även utan konto; identitet/login låser upp djup personalisering.
- Samla tävlingar, PM, starter, live, resultat, splits, serier, Sverigelistan, karta och analys i ett sammanhållet flöde.
- Minska Eventor-brus genom relevans, gruppering och möjlighet att dölja lågprioriterade träningsaktiviteter.
- Göra efteranalys betydligt bättre än dagens split-tabeller: bommar, vägval, GPS, karta, jämförelser och långsiktig utveckling.
- Vara en verklig produkt **och** samtidigt ett avancerat real-world sample för Maui.Spine.

## Grundprinciper

| Princip | Tillämpning |
|---------|-------------|
| Relevans före mängd | Viktiga tävlingar, mina tävlingar och live-situationer prioriteras. Resten finns kvar under "Visa allt". |
| Fungerar utan konto | Publik data, lokala favoriter och centrala flöden ska fungera utan login. |
| Personligt när identiteten är känd | Login/identifiering används för mina starter, resultat, klass, distrikt, ranking och rekommendationer. |
| Phone-first | Primärt en enhandsapp med tydliga huvudhandlingar, bottom sheets och korta beslutspunkter. |
| Offline när det spelar roll | PM, starttid, arena och annan kritisk pre-race-information ska finnas lokalt. |
| Öppen integrationsarkitektur | Eventor, LiveResults, Omaps och Livelox behandlas som adapters bakom en egen domänmodell. |
| Förklarbar intelligence | AI får tolka text; beräkningar/predictions ska vara testbara och kunna förklaras. |

## Målgrupp

- Aktiva svenska orienterare – från ungdom och motionär till elit.
- Föräldrar som följer barn och vill samla starttider, live och resultat.
- Klubbmedlemmar som följer klubbkompisar, distriktslöpare eller egna favoriter.
- Användare som vill analysera sin utveckling över säsongen, inte bara se sluttiden.

## Användarlägen

| Läge | Kapabilitet |
|------|-------------|
| Anonym/lokal | Tävlingar, karta, PM, startlistor, publik live, resultat, splits, lokala favoriter, offline-cache. |
| Identifierad Eventor-person | Mina anmälningar, min klass, mina starter/resultat, distrikt, klubb, Sverigelistan och personalisering. |
| Min grupp | Barn, familj, vänner, klubbkompisar och valfria favoriter som kan följas i start/live/resultat. |
| Avancerad analys | Historik, predictions, jämförelser, karta/GPS och vägvalsanalys. |

## Huvudnavigation — fem flikar

| Flik | Primärt jobb | Innehåll |
|------|--------------|----------|
| **Hem** | Vad behöver jag veta eller göra just nu? | Kontextstyrt: Nästa för mig, Live nu, Senaste resultat |
| **Tävlingar** | Hitta rätt tävling – lista, karta, filter, relevans. | Lista + karta, För dig/Nära, Distrikt/Större, avancerade filter |
| **Live** | Följ mig, Min grupp, min klass eller valfria löpare. | Pågår nu, Min grupp, Min klass, Favoriter |
| **Resultat** | Resultat, splits och WinSplits++-analys. | Översikt + analys, Splits, Vägval, Jämför |
| **Jag** | Profil, Sverigelistan, serier, favoriter och utveckling. | Profil, Sverigelistan, Serier, Utveckling |
