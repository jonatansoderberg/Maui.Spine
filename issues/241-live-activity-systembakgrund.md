# Issue #241 — Spine.Widgets: Live Activity på låsskärmen — systemets bakgrund och färg på systemknappar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/241
**Branch:** issue/241-live-activity-systembakgrund
**Status:** Completed

## Plan

Uppföljning av #238. Utan `Background` ritar låsskärmen halvgenomskinlig svart (`.black.opacity(0.6)`, ett val från spiken); systemets eget material, som följer ljust och mörkt, går inte att be om, och systemets knappar har alltid systemets färg.

1. **`LiveActivityLayout.SystemBackground`** (Jonatans val: ett eget val på layouten, inte ett särskilt färgvärde): `true` → `activityBackgroundTint(nil)`, systemets material. Dagens svarta förblir standard. `Background` vinner om båda sätts, och renderaren loggar det.
2. **`LiveActivityLayout.ActionColor`** (`WidgetColor?`) → `activitySystemActionForegroundColor`. I layoutens JSON, så en push kan sätta det. Android ignorerar båda, som `Background`.
3. **Pröva en ogenomskinlig `Background` på iOS 26** i simulatorn — solid, eller fortfarande Liquid Glass? — och skriv resultatet i `docs/wiki/widgets.md`.
4. Tester: fälten överlever rundresan och skrivs inte när de saknas.
5. Wikin: systemets bakgrund kräver semantiska textfärger (`WidgetColor.Primary` m.fl.) — den blir ljus i ljust läge.

SDK:n (26.2) bekräftar `activityBackgroundTint(_ color: Color?)` och `activitySystemActionForegroundColor(_ color: Color?)`.

## Open Questions

Inga — Jonatans val: eget val på layouten.

## Changes

- `LiveActivityLayout.SystemBackground` (`bool?`) och `LiveActivityLayout.ActionColor` (`WidgetColor?`), i layoutens JSON som `systemBackground`/`actionColor` och utelämnade när de saknas.
- `SpineWidgetRenderer.swift`: `ActivityLayout` läser båda; `Palette.lockScreenTint` ger layoutens färg, `nil` (systemets material) när `systemBackground` är satt, och annars den halvgenomskinliga svarta som förut. Sätts båda vinner `background`, och extensionet loggar det. `activitySystemActionForegroundColor` tar `actionColor`.
- Test: fälten överlever rundresan och skrivs inte när de saknas.
- Push-samplet: knappen "Systemets bakgrund" på Live Activity-sidan startar en aktivitet med `SystemBackground`, semantiska textfärger och `ActionColor` grön — eller uppdaterar den som kör.
- Wikin: två punkter under *Backgrounds and boxes* och två rader i tabellen.

## Verifiering (iPhone 17-simulatorn, iOS 26.2)

- Servertester: 224 gröna (1 nytt). Push-samplet byggt för simulatorn i worktreen; Swift-renderaren kompilerar.
- **Ogenomskinlig färg på iOS 26:** aktiviteten startad med samplets gröna yta (`#1B5E3F`, #238) ritas **solid** på låsskärmen — jämn fyllning, bakgrundsbildens former syns inte igenom, bara en svag glaskant längs kanten. Inte Liquid Glass. Skrivet i wikin.
- **Systemets bakgrund, uppdatering:** den körande gröna aktiviteten uppdaterad med `SystemBackground` (utan `Background`) fick den nya texten med semantiska färger men **behöll den gröna tonen**. Det installerade extensionet har den nya koden (strängarna finns i binären, ingen varning om båda), så `activityBackgroundTint(nil)` returnerades — iOS behåller en ton som redan är satt.
- **Systemets bakgrund, från start** (knappen startar nu en aktivitet när ingen kör; simulatorn omstartad efter att skärmdumparna frusit): låsskärmen visar **systemets material** — mörkt glas med glaskant och ljusbrytning av bakgrundsbildens kant, varken grönt eller Spines platta svarta. Mörkt fast systemet är i ljust läge: låsskärmen följer den mörka bakgrundsbilden. Wikin säger det, och att valet görs vid start.
- **`ActionColor`:** satt i samplet, men iOS visade inga egna knappar på låsskärmspresentationen i simulatorn, så färgen är inte sedd.

## Decisions

- **Ingen omväg för en körande aktivitet.** Att byta från färg till systemets material under körning går inte att få iOS att göra — `nil` behåller den gamla tonen. Att avsluta och starta om aktiviteten i Spines ställe vore att dölja plattformens beteende; det dokumenteras i stället, och samplet startar på systemets bakgrund.
- **`SystemBackground` på layouten, inte `WidgetColor.System`** (Jonatans val). Inga ogiltiga lägen: en färg förblir en färg, och noder behöver inte avvisa något.
