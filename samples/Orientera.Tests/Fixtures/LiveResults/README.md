# LiveResults-fixturer

**Inspelade från skarpa svar** — LiveResults publika API kräver ingen autentisering, så till
skillnad från Eventor-fixturerna är det här riktiga responser, hämtade 2026-08-10 från
`https://liveresultat.orientering.se/api.php` och nedkortade till några klasser och löpare.

Tävlingen är Norrlandsmästerskapen medel (Gävle OK, 2026-08-09) — samma helg som fake-datat är
modellerat på.

Rör dem inte i onödan. De bär tre saker som normaliseringen finns för:

- `competitions.json` innehåller en **rå tabb inuti ett strängvärde**. Payloaden är därmed inte
  giltig JSON, och varje strikt parser vägrar den. Filen ska behålla tabben.
- Samma fält är ibland tal, ibland sträng, ibland tom sträng (`result`, `timeplus`, `splits`).
- Tider är hundradels sekund, och starttid är hundradelar sedan midnatt — utan datum.
- `classresults-vit20.json` (hämtad 2026-08-11, hel klass) bär **starttid som tom sträng** för två
  löpare som aldrig startade. Det är skillnaden mellan "har inte startat än" och "startade
  aldrig", och den finns bara i det här svaret (#65).
