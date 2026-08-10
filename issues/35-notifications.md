# Issue #35 — Orientera M2 — notisgrund: planering, opt-in per typ och lokal leverans

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/35
**Branch:** issue/35-notifications
**Status:** Completed

## Plan

M2:s andra halva. Notiser är opt-in per typ enligt kraven, och grunden är lokal: allt som
planeras är en tidpunkt som redan finns i datan — ett anmälningsstopp, en publicering, en
första start, en starttid. Push, för det bara en server kan veta, är M5.

1. `NotificationPlanner` — ren och klockdriven, som context engine.
2. Opt-in per typ, sparat lokalt.
3. `INotificationScheduler` med iOS/Mac Catalyst och Android bakom sig.
4. Omplanering vid start och vid återkomst till förgrunden.
5. Notisinställningar i Jag.

## Open Questions

Inga.

## Changes

- `Services/Notifications/Notifications.cs` — de åtta typerna ur kraven, deras texter, och
  `PlannedNotification` vars id härleds ur typ + tävling så att omplanering *ersätter* i stället
  för att stapla.
- `Services/Notifications/NotificationPlanner.cs` — reglerna: anmälningsstopp minus ett dygn
  (men inte för en tävling jag redan är anmäld till), PM och startlista för mina tävlingar,
  avresetid räknad bakåt från min start, första start och resultat även för Min grupp.
- `Services/Notifications/NotificationPreferences.cs` — opt-in per typ, av som utgångsläge,
  sparat i telefonen.
- `Services/Notifications/INotificationScheduler.cs` — `SyncAsync` gör enhetens schema lika med
  planen; det som inte längre gäller tas bort. Plus en implementation som säger att den inte
  kan i stället för att låtsas.
- `AppleNotificationScheduler` (`UNUserNotificationCenter`, förgrundsdelegat) och
  `AndroidNotificationScheduler` (kanal, `AlarmManager`, `POST_NOTIFICATIONS`, immutabla
  `PendingIntent`) — plattformskod bakom `#if`.
- `NotificationService` — bygger planen ur källorna och synkar; en källa som inte svarar kostar
  notiserna, inte appen.
- `App.CreateWindow` — omplanering vid `Created` och `Resumed`.
- `Features/Profile/NotificationSheet` — en rad per typ med på/av, och `ProfilePage` fick vägen dit.
- `Services/Travel/TravelEstimate` — avstånd, restid och avresetid på ett ställe i stället för i
  en formel på tävlingssidan; "dags att åka" och tävlingsdetaljen svarar nu likadant.
- `NotificationPlannerTests` — 12 nya, 198 totalt.

## Decisions

- **Bara typer som har data bakom sig visas.** Sverigelistan och prognos är M3; en reglage som
  inte går att slå på är ett löfte appen inte kan hålla. Typerna finns i modellen, inte i UI:t.
- **Behörighet begärs när första typen slås på**, inte vid start. En dialog innan appen har
  något att notifiera om är hur en app blir nekad för gott.
- **Planen ersätter, den kompletterar inte.** `SyncAsync` gör om enhetens schema helt. En
  tävling som flyttats eller slutat följas ska sluta notifiera, och en diff vore ett extra
  ställe för ett gammalt schema att överleva på.
- **Inexakta alarm på Android.** Exakta kräver `SCHEDULE_EXACT_ALARM`, som är till för väckarklockor
  och kalenderhändelser. Ingen av de här notiserna är värd att vara på minuten — de är värda att
  komma fram.
- **Min grupps notiser följer flaggan per person.** Att följa någon är inte samma sak som att
  vilja bli väckt av dem.

## Vad körningen avslöjade

- **`Switch` i en `DataTemplate` i en sheet tar inte emot tryck på iOS.** Inget hände alls:
  ingen visuell ändring, ingen `PropertyChanged`. Krysset i samma sheet och ett `Entry` i en
  annan svarade, och varken `IsEnabled`-bindningen eller `ScrollView` var orsaken. En `Button`
  med `Command` i samma mall fungerade direkt, så raderna använder det mönstret nu. Fyndet är
  eget ärende: [#36](https://github.com/jonatansoderberg/Maui.Spine/issues/36).
- **`Any` låg i den sparade filen.** Härledd egenskap, samma sak som `[JsonIgnore]`-städningen i
  M1 — den ska räknas fram, inte lagras.
- **`Context` betydde fel sak på Android.** Appen har en namnrymd `Orientera.Services.Context`,
  så `Context` inuti `Orientera.Services.Notifications` band till den i stället för till
  `Android.Content.Context`. Bara Android-bygget kunde säga det, vilket är skälet att bygga det.
- **Appikonen dög inte som notisikon.** Android ritar den lilla ikonen som en silhuett, så
  notiserna fick en egen vit vektor (starttriangeln) i stället för en vit klump.

## Verifiering

- **198 tester gröna**, varav 12 nya över planeringsreglerna.
- **iPhone 17 Pro-simulator (iOS 26.2):** notisinställningarna visar de sex typer som har data,
  alla av. Att slå på den första utlöser iOS behörighetsdialog; efter *Allow* står raden på och
  `notifications.json` innehåller `{"enabled":["EntryClosing"]}`. Appen startar om och
  återupptar utan problem med omplaneringen inkopplad.
- `dotnet build` grön för iOS, Mac Catalyst och Android.

## Kvar

Att se en notis *levereras* gick inte att verifiera i den här körningen: varje planerad tidpunkt
i seed-datat ligger dagar bort i enhetens tid, och simulatorn kan inte resa i tid. Leveransvägen
— kanal, trigger, behörighet — är byggd enligt plattformarnas API:er och behöver en körning där
en tidpunkt faktiskt passeras.

Android-sidan är byggd men inte kördverifierad. `AlarmManager` överlever inte omstart; en
`BOOT_COMPLETED`-mottagare som lägger om schemat hör till den körningen.
