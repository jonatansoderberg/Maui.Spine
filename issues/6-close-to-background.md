# Issue #6 — macOS: CloseToBackground fungerar inte

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/6
**Branch:** issue/6-close-to-background
**Status:** Completed

## Plan

Att stänga fönstret avslutade appen även med `CloseToBackground = true`. Ärendet listade fem
försök som alla dött på samma vägg: det gick inte att nå den underliggande `NSWindow` från Mac
Catalyst. `NSApplication.windows` är tom, `keyWindow` och `mainWindow` är nil, och varken
`UIWindow.nsWindow` eller `UIWindowScene._nsWindowScene` svarar på sin selektor — UIKit äger
fönstren och AppKit vet inte om dem.

## Changes

- `SpineApplication.MacCatalyst.cs` — fångar `NSWindow` ur den notifikation Spine redan lyssnar
  på, tar över stängknappens target/action, och lägger `makeKeyAndOrderFront:` till
  `FocusMacWindow` så menyradsikonen blir vägen tillbaka.
- `SpineWindowDelegate` borttagen. Den fanns bevarad "för när problemet löses"; lösningen blev en
  annan, och en oanvänd delegat som ser ut att vara mekanismen är sämre än ingen.

## Decisions

- **Fönstret fanns redan i huset.** `NSWindowDidBecomeKeyNotification` är en helt vanlig
  AppKit-notifikation, och dess `object` **är** fönstret. Spine observerade den redan för att slå
  på `fullSizeContentView`. Alla fem försöken i ärendet letade efter en bro från UIKit till
  AppKit; den behövdes aldrig, för AppKit skickar fönstret självt i ett meddelande.
- **Stängknappen, inte fönstrets delegat.** På Catalyst är `NSWindow.delegate` ett UIKit-objekt
  som kör scenlivscykeln. Att byta ut det hade krävt att varje selektor det implementerar
  vidarebefordras rätt — eller gått sönder på ställen långt från den här filen. Stängknappen
  tillhör ingen annan.
- **Bara en gång.** Notifikationen fyras vid varje aktivering; att peka om knappen varje gång
  hade läckt ett target per fokusering.
- **Menyradsikonen ordnar fram fönstret, inte bara appen.** Utan `makeKeyAndOrderFront:` hade den
  aktiverat en app utan något på skärmen.

## Verifiering

**Mac Catalyst, sample-appen, körd skarpt.** Fönsterräkning och process lästes med AppleScript
mellan varje steg:

| Steg | Utfall |
|---|---|
| Start | 1 fönster, processen lever |
| Röda knappen | **0 fönster, processen lever** |
| Menyradsikonen → Settings | 1 fönster, processen lever |
| ⌘W | 0 fönster, processen lever |
| Menyradsikonen → Exit | processen avslutas |

**Baslinje:** med `CloseToBackground = false` och samma bygge avslutas appen av röda knappen. Det
är alltså den här ändringen som gör skillnaden, inte något annat i miljön.

`dotnet build` på plugin-projektet grönt för samtliga målplattformar.
