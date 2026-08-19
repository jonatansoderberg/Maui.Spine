# Issue #148 — Ett ark som öppnas ur ett ark lämnar ett tomt ark kvar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/148
**Branch:** issue/148-sheet-from-sheet
**Status:** Completed

## Vad som hände

Logga in via appens egna fält och skicka. Inloggningen går igenom — sessionen skrivs, Jag visar
namn och Sverigelistan — men appen blir stående med ett tomt ark som inte går att stänga.

`NavigateToWithResultCoreAsync` presenterade alltid ett nytt bottenark när målet var ett ark.
`NavigateToAsync` har haft en gren för att ett ark redan står uppe; den med resultat hade det inte.

Båda arken hämtar sitt innehåll ur samma `NavigationRegion`. Det andra arkets presentation flyttade
därför vyn ur det första, som blev tomt. När det andra stängde sig hade koordinatorn redan satt
`IsSheetActive = false`, så `ReturnAsync` läste tabbens region i stället för arkets och stängde
aldrig det som stod kvar.

Felet är äldre än #142. Det som ändrades var att #142 gjorde ark-i-ark till huvudvägen in:
`AppLoginSheet` öppnar `EventorLoginSheet` för att lämna över lösenordet till Eventors eget
formulär.

## Changes

- **`NavigationService.NavigateToWithResultCoreAsync`** — ett ark som öppnas medan ett ark står uppe
  läggs på det som redan finns, precis som utan resultat. Stacken blir `[AppLoginSheet,
  EventorLoginSheet]`, `ReturnAsync` går tillbaka i stället för att stänga, och sessionen når den
  som väntar.

**Verifierat på iOS-simulatorn**, hela vägen: appens fält → överlämning → Eventors sida → sessionen
tillbaka → arket stänger sig självt och Hem läser om. Före rättningen blev ett tomt ark stående.
Välkomstflödet, som öppnar ett ark direkt efter att ett annat stängts, kontrollerades särskilt —
det öppnar fortfarande sitt eget ark och påverkas inte. Build grön, `dotnet test` 394 gröna.

## Decisions

**Ingen egen bevakning av avvisandet i den nya grenen.** Den presenterande vägen hänger en
fortsättning på arkets uppgift för att avbryta den väntande om arket försvinner utan `ReturnAsync`.
Inuti regionen behövs det inte: `BackAsync` avbryter den poppade sidans väntan och `CloseAsync` den
aktuella sidans. Att lägga till en till hade varit en andra väg att avbryta samma sak.
