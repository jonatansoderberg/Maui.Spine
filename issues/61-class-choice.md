# Issue #61 — Tävlingssidan: att välja klass gör nästan ingenting

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/61
**Branch:** issue/61-class-choice
**Status:** Completed

## Plan

Arket **Välj klass** lovade "Klassen styr banan, startlistan och prediction". Valet satte en
egenskap på vymodellen som filtrerade PM-punkterna — och skrevs över nästa gång sidan laddades.
Två saker att göra: låta valet överleva, och låta texten säga det valet faktiskt gör.

Klasslistan var också hårdkodad till sju damklasser. Det rättades redan i #58-arbetet;
`ChooseClassSheet` tar nu tävlingens riktiga klasser via `ClassChoice`.

## Changes

- `LiveClassStore` → `CompetitionClassStore`. Namnet sa "klassen i livelistan"; innehållet är
  "min klass i den här tävlingen", vilket är samma fråga som tävlingssidan ställer.
- `EventDetailsPage` — läser och skriver klassen genom den store:n, och bygger om PM-punkterna
  direkt när valet görs i stället för att vänta på nästa besök.
- Arkets text: "Klassen avgör vilka PM-punkter som visas, och vilken klass Live öppnar i."

## Decisions

- **Ett valt klassval vinner över allt appen räknat ut själv.** Första utfallet lät anmälan och
  starttid gå före, vilket lät rimligt — de är fakta — men innebar att väljaren tyst slutade
  fungera vid nästa sidladdning, alltså precis det ärendet handlade om. Nu vinner det användaren
  sagt. Starttiden strax under säger fortfarande vilken klass löparen faktiskt springer.
- **En store, inte två.** Att välja klass på tävlingssidan är att välja den klass Live öppnar i.
  Två separata minnen för samma fråga hade betytt att appen kan ha två olika svar på vilken
  klass som är min.
- **Filnamnet är kvar som `live-classes.json`.** Namnet är där det började; att döpa om det hade
  kastat bort de val som redan ligger sparade på folks telefoner.
- **Texten lovar bara det som händer.** Startlistan och prediction påverkas inte — prediction är
  dessutom inte inkopplad (SP-11). Ett löfte om något som inte sker är sämre än inget löfte.

## Verifiering

`dotnet test`: 230 gröna (ren beteendeändring i vymodellen).

**iPhone 17 Pro-simulator (iOS 26.2):** Norrlandsmästerskapen Lång öppnar i D21, arket listar
tävlingens riktiga klasser med den nya texten, D45 väljs, märket byter till D45 — och står kvar
efter att sidan lämnats och öppnats igen.
