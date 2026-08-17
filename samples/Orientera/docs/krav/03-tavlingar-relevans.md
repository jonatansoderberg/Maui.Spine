# 3. Tävlingskalender, relevans och gruppering

## Utgångsläge

Eventor har stor datatäckning men desktoporienterad informationsdensitet. Produktmöjligheten är inte att kopiera innehållet, utan att **prioritera, gruppera och göra samma information situationsmedveten**.

## Lista + karta

- Tävlingar ska kunna växla mellan **Lista** och **Karta**.
- Kartan används främst för discovery; faktisk navigation lämnas till native maps-app.

### Snabbfilter

- För dig
- Nära
- Gästrikland / valt distrikt
- Större
- Denna vecka
- Mina
- Intresserad

### Avancerade filter

- Datumintervall och helg/vecka.
- Distrikt och geografisk radie/restid.
- Tävlingstyp och disciplin.
- Tävlingsnivå / viktighet.
- Serie.
- Endast anmälningsbara.
- Endast där min klass finns.
- Visa/dölj träningar och återkommande motionsaktiviteter.

## Relevansmotor

Relevans ska vara en **egen komponent, inte en sortering inne i ViewModel**. Den väger ihop `ImportanceScore`, `PersonalScore`, `GeographicScore` och `TemporalScore`.

| Signal | Exempel på effekt |
|--------|-------------------|
| Tävlingsnivå | SM/DM/nationell får högre grundvikt än träning. |
| Jag är anmäld | Mycket stark personaliseringssignal. |
| Min grupp är anmäld | Hög relevans även om jag själv inte springer. |
| Avstånd/restid | Nära events prioriteras, men får inte alltid slå mästerskap. |
| Distrikt | Valt distrikt får boost men begränsar inte discovery. |
| Serie/följd arrangör | Följda serier, klubbar och arrangörer får boost. |
| Deadline | Anmälan som snart stänger flyttas upp. |
| Återkommande träning | Grupperas/komprimeras och kan döljas. |

## Event grouping

> **Exempel "Veckans bana":** Sex Eventor-rader 4–9 augusti ska normalt visas som ett kort: *"Veckans bana – Hemlingby, 4–9 aug, 6 tillfällen"*. Originaleventen finns kvar när gruppen öppnas.

- Gruppering kan initialt baseras på **normaliserad titel + arrangör + plats + klassificering + angränsande datum**.
- Heuristiken ska testas mot flera månaders verklig data (spike SP-09).
