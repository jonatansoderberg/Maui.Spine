# Issue #55 — Restiden visas som mätt fast den är fågelvägen delat med 70

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/55
**Branch:** issue/55-travel-estimate
**Status:** Completed

## Plan

Tävlingssidan visade **Resa · 3,4 km hemifrån · ca 3 min** i samma form som starttiden bredvid,
alltså som en uppmätt uppgift. Den är rak linje delat med 70 km/h. Två fel följde: modellerat såg
uppmätt ut, och talet blev orimligt på korta resor.

Riktig restid kräver ruttning och PM:ets parkering (M3). Det här handlar bara om att inte påstå
mer än appen vet.

## Changes

- `TravelEstimate.SpeedKmh` — medelhastigheten beror på avståndet i stället för att vara 70 km/h
  rakt av: 25 km/h på de första kilometrarna, stigande till 80 km/h vid sex mil.
- `EventDetailsPage` — tiden bär `EstimateInk` och prefixet `~`; avståndet flyttas till en egen
  rad i `CaptionLabel` och säger "ca 21 km fågelvägen". Uppläst form: "uppskattat 21 km
  fågelvägen, ungefär 28 minuter".
- `TravelEstimateTests` — nytt.

## Decisions

- **Kurva, inte trappsteg.** Första utfallet delade in avstånden i fyra intervall med var sin
  fasta hastighet. Det gjorde att en längre resa kunde ta *kortare* tid — sex kilometer kom fram
  före fyra, eftersom sexan hamnade i ett snabbare intervall. Mitt eget monotonitetstest fångade
  det. Nu stiger hastigheten jämnt med avståndet, vilket håller svaret växande hela vägen.
- **Färgen bär bara tiden.** Avståndet fågelvägen *är* uppmätt — det är hastigheten som är gissad.
  Att måla båda som uppskattade hade sagt fel sak om det som faktiskt är räknat.
- **"Fågelvägen" gör jobbet.** Ett enda ord säger att detta är sträckan appen kan räkna ut, och
  varje bilist vet att vägen är längre. Det är ärligare än en asterisk och kortare än en förklaring.
- **Notisen "dags att åka" ändras med.** `NotificationPlanner` använder samma `TravelEstimate`, så
  avresetiden följer automatiskt. Det var poängen med att ha en regel i stället för två.

## Verifiering

`dotnet test`: 230 gröna (221 + 9 nya). Det tidigare testet på avresetid gick från 60 till 52
minuter för samma sträcka på knappa sju mil — närmare landsvägsfart, som avsett.

**iPhone 17 Pro-simulator (iOS 26.2):** Norrlandsmästerskapen Lång visar "~28 min" i
`EstimateInk` med "ca 21 km fågelvägen" under. Gamla regeln hade sagt 18 minuter för samma resa.
