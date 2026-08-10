# 8. Kartor, Omaps, GPS och vägvalsanalys

## Omaps

Omaps är en svensk orienteringskartdatabas. Kartägare kan ladda upp georefererade kartor och välja vilka externa tjänster som får hämta kartinformationen. Omaps anger uttryckligen att riktiga kartor kan delas till externa tjänster/appar via API, medan upphovsrätten ligger kvar hos kartägaren [K4].

> **Målbild:** Omaps är förstahandskandidat för den riktiga orienteringskartan när rättigheter finns. Detta är mycket bättre än att försöka hämta kartbilder från Livelox.

## Livelox

Livelox offentliga API kan ge eventinformation och kursdata i IOF XML, inklusive kontrollkoordinater när informationen är publik. Däremot är **kartor och GPS-rutter uttryckligen inte tillgängliga** genom det publika API:t [K3]. Deep-link till Livelox för den fulla viewer-upplevelsen.

## Vägvalsanalys

- Visa tävlingskarta när den är lagligt/tillåtet tillgänglig.
- Rita bana/kontroller från kursdata.
- Rita användarens GPS-spår.
- Per sträcka: fågelväg, sprungen distans, extra distans, tid, placering och tapp.
- Detektera stopp, större riktningsändringar och avvikelse från direktlinje.
- Kombinera med splits för sannolik bom/tidsförlust.
- Jämför alternativ endast när datakällan tillåter det.
- Deep-link till Livelox för den fulla viewer-upplevelsen.

## GPS

Initial fallback: **import av GPX/FIT**. Målbild: automatisk koppling till den rutt användaren redan har via träningsklocka/Livelox eller annan godkänd integration. Detta kräver separat partner-/API-utredning (spike SP-08).
