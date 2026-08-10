# 7. Sverigelistan, serier, utveckling och Prediction Engine

## Sverigelistan

Sverigelistan ska vara en **förstaklassfunktion i Jag** och samtidigt en viktig input till prediction. Eventor beskriver huvudpoängen som medelvärdet av de **sex bästa resultaten under exakt ett år**; disciplinlistor använder färre resultat. Åtkomst till en robust maskinläsbar datakälla behöver utredas (spike SP-02) [K5].

- Aktuell poäng och placering.
- Total + medel/lång/sprint/natt där data finns.
- Resultat som räknas i snittet.
- Resultat som snart faller ur.
- Historisk trend och förändring.
- Används som **en signal – inte enda sanningen** – i Prediction Engine.

## Serier

- Visa totalställning, deltävlingar, poäng och strukna resultat.
- Föreslå serier användaren själv deltar i.
- Tillåt manuell följning av serie.
- Visa nästa deltävling på Hem när relevant.
- Dataåtkomst för Eventor Series/Standings behandlas som teknisk spike (SP-03).

## Utveckling över säsong

- Placering i percentil / relativ placering.
- Sverigelistan och disciplinutveckling.
- Uppskattad bomtid och stabilitet.
- Prediction vs faktiskt resultat.
- Prestation i olika terrängprofiler.
- Starkaste distans/terrängtyp och trend över tid.

## Prediction Engine

> **Standardpresentation:** "Förväntad placering: 8–15". Prediction ska uttrycka ett **rimligt intervall, inte falsk precision**.

### Input

| Dimension | Signaler |
|-----------|----------|
| Startfält | Sverigelistan, disciplinpoäng, historik, recent form, head-to-head. |
| Tävlingskontext | Distans, nivå, klass, anmälda, banlängd när tillgänglig. |
| PM/inbjudan | Terräng, kupering, framkomlighet, sikt, teknisk svårighet, klasspecifika kommentarer. |
| Personprofil | Historiska prestationer i liknande terräng/distans och stabilitet. |
| Osäkerhet | Saknad data, extremt teknisk terräng, små startfält eller låg datamängd ska bredda intervallet. |

### AI:s roll

AI används för att tolka PM/inbjudan till strukturerade egenskaper. Själva placeringsprognosen bör i första hand beräknas av en **deterministisk/statistisk modell** så att den kan testas på historiska tävlingar (backtest, spike SP-11).

### Klasspecifik terräng

`CompetitionProfile` måste kunna skilja mellan exempelvis vuxenbanor och ungdomsbanor. Formuleringen "ungdomsbanorna går i stigrikt område" ska påverka prediction för relevanta ungdomsklasser men inte automatiskt för H45.

### Sluttidsprognos

Kan utredas men ska **inte** vara standard i första versionen. Terräng, kupering, kartläsning och banläggning gör absolut tid betydligt svårare att estimera än relativ placering.
