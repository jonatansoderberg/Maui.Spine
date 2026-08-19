# Issue #146 — Avbryt i inloggningsarket kraschar appen

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/146
**Branch:** issue/146-cancel-crashes
**Status:** Completed

## Vad som hände

```
Unhandled Exception:
System.ArgumentNullException: Value cannot be null. (Parameter 'result')
```

`INavigationService.ReturnAsync(object result)` lämnar ett resultat till den som väntar, och kastar
på null. Tre ställen anropade den med `null!` för att säga "inget resultat" — vilket är att gå runt
ett kontrakt med en tystad varning i stället för att följa det. Kompilatorn sa det rakt ut:
`AppLoginSheet.ViewModel.cs(66,39): warning CS8604`.

Kastet sker efter att arket redan animerats bort, så avbrytandet ser ut att lyckas. Sedan är appen
borta. Att anropet ligger i ett `[RelayCommand]` gör kastet till en okontrollerad krasch i stället
för ett fel någon fångar.

## Changes

- **`AppLoginSheetViewModel`** — `Cancel` stänger arket med `BackAsync`. `Submit` lämnar tillbaka
  sessionen när det finns en, och stänger annars.
- **`EventorLoginSheetViewModel`** — `Cancel` likaså.

**Verifierat på iOS-simulatorn:** Avbryt i inloggningsarket stänger det och appen står kvar på Hem.
Före rättningen dog den, med kraschrapport. Build grön för maccatalyst och iossimulator,
`dotnet test` 393 gröna.

## Decisions

**Avbrytvägen fanns redan.** `BackAsync` stänger ett ark med en sida och avbryter den väntande, som
får `NavigationResult.Canceled()` — precis vad ett avbrutet ark ska ge. Det behövdes ingen ny
mekanik, bara att sluta be `ReturnAsync` om något den sagt att den inte gör.

**`null!` var symptomet, inte orsaken.** Tre ställen skrev samma utropstecken för att tysta samma
varning om samma sak. En tystad varning som återkommer på tre ställen säger att anroparen letar
efter ett verb som inte fanns där den letade — och det är värt att minnas nästa gång ett `null!`
dyker upp i en signatur som inte tar null.
