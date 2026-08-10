# 6. Resultat och WinSplits++

Eventors dokumenterade resultat-endpoints kan inkludera sträcktider (`includeSplitTimes`) [K1]. Därmed kan Orientera bygga **egen analys ovanpå rådata** utan att vara beroende av WinSplits som primär renderingsyta.

## Resultatflikar

| Översikt | Sträckor | Analys |
|----------|----------|--------|
| Placering/tid | Sträcktid | Största tapp |
| Efter vinnaren | Sträckplacering | Sannolika bommar |
| Prediction vs utfall | Ackumulerad placering | Bomtid / teoretisk tid |
| Ranking-effekt | Efter sträckbästa | Jämför löpare |
| Snabb CTA | Färgkodad förlust | Karta och vägval |

## WinSplits++ — målbild

- Automatisk detektion av sannolika tidsförluster.
- Bästa/sämsta sträckor och stabilitetsindex.
- Jämför mot vinnaren som default eller valfri löpare.
- Jämför mot klubbkompis/grannklubb/distriktslöpare.
- Teoretisk sluttid utan de största avvikelserna – **tydligt märkt som uppskattning**.
- Placering efter varje kontroll och hur loppet utvecklades.
- Head-to-head mellan två valfria löpare.
- Koppling till karta/GPS när data finns.

## Förklarbarhet

Analysen ska skilja mellan **observerad data** (tid, placering, distans) och **modellerad data** (sannolik tidsförlust, bomindikering, alternativt utfall). Beräknade värden ska presenteras som uppskattningar.
