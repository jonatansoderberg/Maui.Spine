# Issue #195 — MauiSpinePushSampleApp: deklarerad widget utan provider

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/195
**Branch:** issue/195-deklarerad-widget-utan-provider
**Status:** Completed

## Plan

Samplet deklarerade `<SpineWidget Include="sample" …>` men hade ingen klass med `[Widget]`. Widgeten
gick att lägga på hemskärmen och visade `—`, eftersom ingenting byggde innehåll till den.

En provider som visar något som **syns ändras** vid varje ombyggnad, så en widget-push går att
observera i stället för att bara svara `sent 1`.

## Changes

- `Widgets/SampleWidget.cs`: `[Widget("sample")]` som visar tid för senaste ombyggnad och ett
  löpnummer. Båda ändras vid varje bygge.
- Räknaren ligger i `Preferences` och överlever processen — en widget kan byggas om långt efter den
  start som schemalade det, och en nollställd räknare hade fått en riktig refresh att se ut som den
  första.
- Providern skriver också en rad i `PushLog`, så ombyggnaden syns på **Logg**-sidan.
- `Refresh(15 min)` så widgeten inte fryser när ingen push kommer.

## Decisions

- **Loggraden är inte dekoration.** När widget-pushen provades första gången svarade servern `sent 1`
  utan att något gick att observera: en widget-refresh hanteras internt av Spine och når aldrig
  appens `IPushHandler`, så Logg-sidan var tom och widgeten visade `—`. Med raden i `PushLog` går
  vägen att verifiera även av den som inte har widgeten på hemskärmen — vilket är det normala när
  man utvecklar.
- **Tid och löpnummer, inte bara tid.** Två ombyggnader inom samma sekund hade sett identiska ut.

## Verifiering

- **Templaten renderar.** På en fysisk iPhone 16 Pro visar widgeten på hemskärmen nu ombyggnadstext
  i stället för `—`. Den fylls vid varje bakgrundning, oberoende av push.
- **Push-utlöst ombyggnad är inte verifierad än.** Telefonen fick inte upp någon registrering mot
  sample-servern vid försöket, så `kind: "widget"` kunde inte skickas till den. Vägen är oförändrad
  sedan tidigare test, där utskicket i sig svarade `sent 1`.
