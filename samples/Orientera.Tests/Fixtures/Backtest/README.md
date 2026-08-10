# Backtest-underlag

**Inspelat från skarpa svar** — 60 svenska tävlingar mellan januari och augusti 2026, hämtade
från LiveResults publika API 2026-08-10. 5 556 resultatrader, 2 402 löpare, varav 725 med minst
tre lopp. Det är underlaget prognosmodellen mäts mot (SP-11).

## Vad "svensk tävling" betyder här

LiveResults kalender har inget land, och `timediff` skiljer bara ut andra tidszoner — danska,
norska och österrikiska tävlingar ligger kvar. Urvalet är i stället gjort så här:

1. Klubbarna i startfälten på två svenska distriktsmästerskap (Norrlandsmästerskapen medel,
   Gävle OK, och GM publik medel, OK Nackhe) — 186 klubbar, svenska genom att de sprang ett
   svenskt mästerskap.
2. Tävlingar 2026 arrangerade av någon av de klubbarna.

Arrangörslistan i `competitions` är genomgången för hand: samtliga 60 är svenska.

## Format

`results` är rader, inte objekt, för att hålla filen liten:

```
[namn, klubb, tävlings-id, klass, placering, startande, kvot]
```

`kvot` är löparens tid delad med vinnartiden i den klassen — måttet modellen bygger form på,
och det enda som är jämförbart mellan olika banor och terränger.

Bara fullföljda lopp (status OK) ingår. Klasserna är standardklasserna H/D 16–65.
