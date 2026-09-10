# Issue #205 — MauiSpinePushSampleApp: demonstrera RemoteSource — den enda widget-vägen som inte kräver att appen väcks

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/205
**Branch:** issue/205-remotesource-i-push-samplet
**Status:** Completed

## Plan

Samplet visar två sätt att uppdatera en widget, och båda kräver att iOS väcker appen. `RemoteSource` gör det inte: WidgetKit laddar om, extensionet hämtar.

1. **Servern:** `GET /widget/remote` svarar med en `WidgetTimeline` byggd med `W` och `.ToJson()` — "Från servern" och serverns klockslag, så att det syns att svaret är färskt och varifrån det kom.
2. **En andra widget, `remote`**, med en egen `RemoteWidgetProvider` som sätter `RemoteSource` mot adressen och bygger en reserv som säger "Från appen".
3. **Adressen** i `appsettings.json` (`WidgetSource`), registrerad i `MauiProgram` som `RemoteWidgetSource`.
4. **Wikin** pekar på samplet från avsnittet om `RemoteSource`, och säger att appen måste ha byggt widgeten en gång — adressen står i dokumentet appen skriver.

## Open Questions

Inga.

## Changes

- Sample-servern: `GET /widget/remote` — en `WidgetTimeline` byggd med `W`, "Från servern" och serverns klockslag, `Refresh` 15 min, serialiserad med `ToJson()`.
- `Widgets/RemoteWidget.cs`: `RemoteWidgetProvider` (`[Widget("remote")]`) med `RemoteSource` och en reserv som säger "Från appen"; `RemoteWidgetSource` bär adressen.
- Samplet: `<SpineWidget Include="remote">` i csproj, `WidgetSource` i `appsettings.json`, registrering i `MauiProgram`.
- `docs/wiki/widgets.md`: tre saker att veta om `RemoteSource` — appen måste ha byggt widgeten en gång, reserven bör säga varifrån den kommer (och var felet loggas), och vem som hämtar på respektive plattform — plus en hänvisning till samplet.

## Verifiering (iOS-simulator)

- Servern svarar på `/widget/remote` med ett giltigt dokument (en post, `refreshAfterSeconds` 900).
- Appen skrev `spine-widgets/remote.json` i App Group med adressen `http://localhost:5100/widget/remote`.
- **Serverns väg:** widgeten "Spine remote" på hemskärmen visar "Från servern" — sett av Jonatan.
- **Reserven:** servern stoppad, appen omstartad så att widgetarna laddades om. Extensionet loggade *[SpineWidgets] remote source for remote failed: Could not connect to the server.* och widgeten visade "Från appen — Reserv · byggd 15:56:05"; `sample` bredvid oförändrad. Servern startad igen.
- Android: bygget går igenom; emulatorerna var stängda, och hämtningen där är befintlig ramverkskod.

## Decisions

- **En andra widget i stället för `RemoteSource` på `sample`** (Jonatans val). `sample` visar det de tysta pusharna bär och har Kvittera-knappen; med en fjärrkälla skulle serverns svar täcka båda så fort servern svarar, vilket den i samplet alltid gör. Två widgetar visar båda vägarna sida vid sida.
- **Reserven säger "Från appen".** Utan det ser de två källorna likadana ut, och en widget som tyst fallit tillbaka ser ut att fungera.
- **Adressen är en egen inställning**, inte härledd ur `SendEndpoint`. På en fysisk enhet måste extensionet nå servern över LAN eller en tunnel, och det ska gå att peka om widgeten utan att röra resten.
- **Sample-servern på 5100 startas om med den nya koden** (Jonatans ja). Registreringarna ligger i minnet och kommer tillbaka när appen kommer fram.
