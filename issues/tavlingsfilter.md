# Tävlingsfiltret: minst lika kapabelt som nya Eventor

**GitHub:** _issue ej skapad än_
**Branch:** issue/tavlingsfilter
**Status:** In Progress — arket och listan klara, Start/Live/Resultat kvar

## Uppdraget

Förbättra filtret på Tävlingar med nya Eventor som måttstock. Det får inte vara sämre än deras
på någon punkt — och "Mitt distrikt" nämndes som exempel.

## Vad nya Eventor har (mätt 2026-08-24, inloggad, eventor.se → Arrangemangskalender → Filter)

Ett högerdockat ark med sex fält och `Rensa filter` / `Spara` i foten:

| Fält | Form | Innehåll |
|---|---|---|
| Sök | fritext | |
| Datumintervall | från–till, riktiga datum | förvalt 17 aug → 23 sep |
| Distrikt | **flerval**, kryssrutor | **"Mitt Distrikt" överst**, därefter Blekinge…Östergötland |
| Tävlingstyp | **flerval** | Mästerskaps-, Nationell, Distrikts-, När-, Klubb-, Internationell, Veckans Bana |
| Grenar | flerval | OL, Inomhus-OL, MTBO, PreO, OL-skytte, SkidO |
| Distanser | **flerval** | Sprint, Medel, Lång, Ultralång, Natt, Stafett, PreO, PreO Sprint, TempO |

Dessutom, **ovanför kalendern och utanför arket**: aktiva filter som borttagbara chips
("Gästrikland ✕") plus `Rensa filter`. Man ser vad som är satt och kan ta bort ett i taget utan
att öppna arket. Och listan bakom arket uppdateras medan man väljer.

## Var vi är sämre i dag

1. **Tävlingsnivå är en stege, inte en mängd.** `MinimumLevel` betyder "den här nivån och uppåt".
   Det går inte att be om *bara* närtävlingar, och listan erbjuder fyra av sju steg — Internationell,
   Närtävling, Träning och Motion går inte att välja alls.
2. **Disciplin är enkelval och stympat.** Fem alternativ av åtta: Stafett och Ultralång går inte
   att välja. Stafett är dessutom den disciplin man oftast vill välja bort.
3. **Inget "Mitt distrikt".** Distriktschippen ligger i A–Ö i en vågrätt rullande rad. Gästrikland
   ligger fyra chips in; för Östergötland krävs det halva alfabetet.
4. **Fyra `Picker`.** Tidsintervall, nivå, disciplin och avstånd öppnar iOS hjul. Det är tre
   interaktioner för ett val, och det bryter mot princip 6 (frusna fingrar) och mot chipmönstret
   som resten av vyn använder.
5. **Man ser inte vad som är satt.** "Filter (3)" säger antalet, inte vilka, och inget går att ta
   bort utan att öppna arket.
6. **Man ser inte vad filtret ger förrän man stängt arket.** Eventor har listan bakom sig.
7. En kvarglömd bildtext lovar att "Datumintervall, distrikt, restid och serie kommer" — distrikten
   finns sedan länge.

## Var vi är bättre, och som inte får tappas

Eventor vet inte var du bor och inte vem du är:

- **Avstånd hemifrån** (inom 25/50/100 km).
- **Endast där min klass finns.**
- **Endast anmälningsbara.**
- **Distriktslistan är de distrikt det finns tävlingar i**, inte alla 24 i Sverige.
- Snabbfilterchippen på sidan (För dig, Nära, Mitt distrikt, Större, …) — presets ovanpå filtret.

## Plan

**`EventFilter`**

- `MinimumLevel: CompetitionLevel?` → `Levels: IReadOnlySet<CompetitionLevel>`, tom = alla.
- `Discipline: Discipline?` → `Disciplines: IReadOnlySet<Discipline>`, tom = alla.
- Ny `Includes(Competition, Person, DateTimeOffset)` — hela `PassesAdvanced` flyttar hit från
  `EventsPageViewModel`. Reglerna hör till filtret, och först här går de att testa: `EventFilter.cs`
  länkas in i testprojektet, vy-modellen kan inte.
- Ny `Facets` — de satta valen, ett i taget, där varje fasett bär **filtret utan sig själv**.
  Det är vad chippraden behöver för att kunna ta bort ett val, och det håller borttagningslogiken
  på ett ställe.

**Arket** — fyra `Picker` blir fem grupper. Nivå och disciplin blir flerval och kompletta.
Distrikt får **"Mitt distrikt" först**, brutet ur A–Ö-raden, och bildtexten säger vilket det är.
Primärknappen räknar: "Visa 23 tävlingar", och blir "Inget matchar filtret" när den skulle ge noll.

**Formen på grupperna** (efter genomgång mitt i arbetet — vågrätt rullande chiprader dög inte):
hopfällbara sektioner med summering i huvudet, och radbrutna kompakta chips inuti. Ingen sidled
att svepa i, allt på skärmen samtidigt.

**Sidan** — en rad med aktiva filter som borttagbara chips, med "Rensa" sist. Ritas bara när ett
avancerat filter är satt, så förvalsläget kostar ingen höjd.

## Changes

**Filtret som modell**

- `EventFilter.MinimumLevel` → `Levels` (mängd), `Discipline` → `Disciplines` (mängd). Tom = alla.
- `EventFilter.Includes(Competition, Person, DateTimeOffset)` — hela `PassesAdvanced` flyttade hit
  från `EventsPageViewModel`, tillsammans med `IsRegisterable`.
- `EventFilter.Facets` — de satta valen, ett i taget, där varje fasett bär filtret utan sig själv.
- `IsActive` och `ActiveCount` borta: deras enda läsare var rubrikens "Filter (n)", som är ersatt.

**Arket**

- Fem hopfällbara `FilterSection` i stället för en chiprad och fyra `Picker`. Var sektion visar i
  sitt huvud vad som är valt — "Mitt distrikt", "Mästerskap +2", "Alla nivåer".
- Chippen radbryts i en `WrapLayout` och är kompakta. Alla tjugofyra distrikten syns samtidigt.
- Nivå: sju val, flerval. Disciplin: åtta val, flerval. Båda var förut enkelval med fyra
  respektive fem av alternativen nåbara.
- Distrikt: **"Mitt distrikt" först**, brutet ur A–Ö. Bildtexten säger vilket det är.
- Avstånd fick 200 km som fjärde steg.
- Primärknappen räknar mot vad snabbfiltret lämnar: "Visa 233 tävlingar" / "Inget matchar filtret".

**Sidan**

- Rad med aktiva filter som borttagbara chips, plus "Rensa". Ritas bara när något är satt.

**Nya kontroller**

- `Controls/WrapLayout.cs` — egen `ILayoutManager` som radbryter. `FlexLayout` med `Wrap` ritar
  rätt men tar inte emot tryck i en lodrät `ScrollView`; det är därför två chiprader i appen var
  byggda som vågräta rullningar.
- `Controls/FilterSection.cs` — rubrik, summering, chevron, hopfällbart innehåll.
- `ChipView.IsCompact` + `ChipCompact`-stilarna.

**Tester** — nio nya i `EventFilterTests` för `Includes` och `Facets`.

## Decisions

**Nivå och disciplin blev mängder.** En stege ("den här nivån och uppåt") kan inte säga *bara*
närtävlingar, och det är precis vad någon som letar efter något litet och nära en tisdag frågar
efter. Eventors motsvarighet är flerval; vår var dessutom stympad till fyra av sju steg.

**Reglerna flyttade till `EventFilter`.** De låg i vy-modellen och gick därför inte att testa —
testprojektet länkar in `EventFilter.cs` men kan inte referera MAUI. Samma flytt är vad som gör
att arket kan räkna: knappens antal använder exakt de regler listan använder, inte en kopia.

**Antalet på knappen räknas mot snabbfiltrets urval, inte hela kalendern.** "Mina" eller "Denna
vecka" tar bort rader efteråt, och ett antal som räknat hela kalendern hade lovat rader som
försvinner i samma andetag.

**Knappen är kvar aktiv vid noll.** Den säger "Inget matchar filtret" men går att trycka på: det
tomma läget är ett designat läge som förklarar sig självt, och en spärrad primärknapp på arkets
enda väg ut är den återvändsgränd `ChipView` redan dokumenterar.

**"Filter (n)" i rubriken togs bort.** Den sa hur många val som var satta men aldrig vilka, och
den uppdaterades bara när man gick in i fliken på nytt — rubriken läser `PageActions` när sidan
visas och får ingen signal när samlingen ändras. Fasettchippen säger vilka, när det händer.

**Sektionerna är hopfällda från början.** Fem grupper utfällda är ett ark man inte ser botten på,
och fyra av fem är nästan alltid orörda. Summeringen i huvudet är vad som gör det ofarligt: en
stängd grupp döljer chippen, aldrig valet.

**Kompakta chips är ~35 pt höga, inte 44.** Designprincip 6 sätter 44 pt för kärnhandlingar under
tävlingsförhållanden. Ett valrutnät inne i ett ark är inte det, och 44 pt gånger tjugofyra
distrikt är vad som gjorde raderna omöjliga att visa utan sidledsrullning. Undantaget är
medvetet och gäller bara `ChipCompact` — snabbfiltret, resultatflikarna och live-växeln behåller
sina 44.

**Egen `WrapLayout` i stället för `FlexLayout`.** Två ställen i koden hade redan gett upp och
byggt vågräta rullningar för att `FlexLayout` med `Wrap` ritar chippen utanför sin mätta yta och
plattformen träffytetestar mätningen. En egen layout mäter och placerar i samma pass, så höjden
som rapporteras är höjden som används.

## Kvar

`val likt bild 3 — bör implementeras på start/live/resultat också som filter/sortering/val`:
`WrapLayout`, `FilterSection` och `ChipCompact` är byggda för att återanvändas, men Start, Live
och Resultat är inte omgjorda än. Det som saknas där är en hopfälld **filterrad** ovanför
innehållet — summeringschips plus chevron, som Eventors resultatsida — som fälls ut till samma
sektioner.
