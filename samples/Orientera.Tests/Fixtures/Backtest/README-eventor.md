# Backtest-underlag, Eventor

**Inspelat från skarpa svar** — 244 individuella tävlingar 2026, klassificering 1–2, hämtade från
Eventors `/api/results/event` 2026-08-12, plus Sverigelistan-historik för de 2 209 löpare som
startade i augustifönstret.

Det här ligger vid sidan av `swedish-2026.json` i stället för att ersätta den. Den fixturen är
LiveResults och identifierar löpare på namn och klubb; den här är Eventors egen och har
`personId` på varje rad. Rankingen hänger på det id:t, så kopplingen blir exakt och ingen
namnmatchning brusar in i mätningen (#113).

## Format

```
competitions : [{id, date, name}]
results      : [personId, tävlings-id, klass, placering, startande, kvot]
rankings     : { personId: [[datum, poäng], …] }
```

`kvot` är löparens tid delad med vinnartiden i klassen — samma mått som den andra fixturen, och det
enda som är jämförbart mellan banor och terränger.

`rankings` är löparens **alla** rankingresultat med datum, inte ett nuläge. Det är det som gör
backtesten läckagefri: snittet räknas om som det stod dagen före varje lopp. Bara de tolv månader
ett snitt kan bestå av är sparade — löparsidan bär fler år, men de läses aldrig.

Bara fullföljda lopp (status OK) och standardklasserna H/D 16–70 ingår, och bara klasser med minst
fem i mål.

## Vad som saknas

87 av 2 209 löpare har ingen rankingsida ens genom en session — de har inte betalat avgiften. De
finns kvar i resultaten och räknas i fältet, men får ingen prior. Det är verkligheten modellen
möter, och därför är det inte städat bort.
