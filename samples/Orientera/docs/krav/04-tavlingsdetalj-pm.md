# 4. Tävlingsdetalj och PM Intelligence

## Tävlingsdetalj

Tävlingsdetaljen ska vara **vertikal, mobil och kontextstyrd**. Den ska inte spegla Eventors desktopsektioner en-till-en.

Sektionsordning:

1. **Hero:** namn, arrangör, datum, nivå, disciplin och intressemarkering.
2. **För dig:** anmälningsstatus, klass, min start, deadline, restid och prediction.
3. **Snabbhandlingar:** PM, Karta, Live, Resultat, Livelox.
4. **Tävlingsinfo:** arena, parkering, avgifter, stämpling, klasser, kontakt.
5. **Dokument:** PM, inbjudan, terräng-/kartbilder, boende/camping.
6. **Startlista, resultat, splits och serie i samma eventkontext.**

## PM Intelligence

PM och inbjudan ska behandlas som **data, inte bara PDF**. En AI/LLM-baserad extraktion skapar en strukturerad `CompetitionProfile` som används både för briefing och prediction.

### Fältgrupper

| Fältgrupp | Exempel |
|-----------|---------|
| Logistik | Arena, parkering, avgift, vägvisning, avstånd parkering–arena–start. |
| Tävling | Första start, kartskala, ekvidistans, stämplingssystem, vätska, toalett, dusch. |
| Terräng | Teknisk svårighet, kupering, framkomlighet, sikt, underlag. |
| Klasspecifikt | Ungdomsbanor stigrika, öppen klass, särskilda starter, särskilda regler. |
| Risk/viktigt | Förbjudna passager, trafik, specialsymboler, väderrelaterade instruktioner. |

### Källspårning

Varje AI-extraherad uppgift bör lagras med **Value, Confidence, SourceDocument och Page**. UI ska kunna visa exempelvis *"Måttligt kuperat – PM sida 2"*.

> **Princip:** LLM tolkar informationen. Domänmodellen lagrar strukturen. Prediction Engine räknar på den. Detta gör lösningen testbar, förklarbar och mindre beroende av fri AI-generering.
