# Issue #168 — Spine.Widgets v2: Android (RemoteViews + Live Updates)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/168
**Branch:** issue/168-spine-widgets-v2-android-remoteviews-live-updates
**Status:** Completed

## Plan

Android-implementationen av `Plugin.Maui.Spine.Widgets` enligt förstudiens §4.1 (Android-kolumnen), §5 och §6
(v2). Det publika API:t i [docs/wiki/widgets.md](../docs/wiki/widgets.md) och modellen i `Core/*.cs` är
fastspikade av iOS-releasen (#165) och ändras inte. Acceptanskravet är att `samples/MauiSpineSampleApp/Widgets/
SampleWidget.cs` och `samples/Orientera/Widgets/*.cs` renderar på Android utan en enda ändring.

Verifierade förutsättningar (Mono.Android 36.1.53, .NET Android SDK 36.1.53, MAUI 10.0.50):

- `AndroidManifestOverlay`-items finns i SDK:n, så targeten kan generera ett manifestfragment som manifest-mergern
  slår ihop med appens — receivers, intent-filter och `uses-permission` utan att appen rör sitt `AndroidManifest.xml`.
- `Notification.ProgressStyle` (segment, punkter, `SetStyledByProgress`), `Notification.Builder.SetShortCriticalText`,
  `NotificationManager.CanPostPromotedNotifications` och `Notification.FlagPromotedOngoing` är bundna.
  **`Notification.Builder.setRequestPromotedOngoing(boolean)` är inte bundet** — anropas via `JNIEnv` (en rad).
- `RemoteViews.SetChronometerCountDown`, `SetViewLayoutMargin`/`Height`/`Width` (API 31) är bundna.
- Det finns ingen `setRelativeTime` i `RemoteViews`; se Decisions.
- En `BroadcastReceiver` i .NET Android kör i appens process, och `MauiApplication.OnCreate` bygger `MauiApp` innan
  någon komponent körs — så receivern har tillgång till DI-containern (`IPlatformApplication.Current.Services`)
  även utan aktivitet. Det gör att `Refresh(after)` faktiskt kan köra providern på Android, till skillnad från iOS.

I leveransordning:

1. **Plattformslagret** `Platforms/Android/WidgetPlatform.Android.cs` (`IWidgetPlatform`):
   - Lagring i `Context.FilesDir/spine-widgets/<kind>.json` och `assets/<id>`, atomiskt via tmp + move som på iOS.
     Ingen App Group: appen och receivern är samma paket och process.
   - `Reload(kind)`: hämta widget-id:n för kindens `ComponentName` via `AppWidgetManager.GetAppWidgetIds` och
     rendera direkt (`UpdateAppWidget`), i stället för att skicka en broadcast till sig själv. `ReloadAll` loopar.
   - `Platforms/Android/SpineWidgetsExtensions.Android.cs`: registrerar `WidgetPlatform`, kopplar
     `AndroidLifecycle.OnCreate`/`OnNewIntent` → `TryHandleLink(intent.Data)`, och `OnStop` → `RefreshAllInBackground`
     när `RefreshOnBackground` gäller. `Default.cs` får `#if !IOS && !ANDROID`.
2. **Renderaren** `Platforms/Android/RemoteViewsRenderer.cs`: `WidgetNode` → `RemoteViews` med stub-layouts i
   `Platforms/Android/Resources/layout/` (`spine_widget_vstack`, `_hstack`, `_zstack`, `_text`, `_timer`, `_progress`,
   `_icon`, `_image`, `_spacer`, `_divider`, `_root`) och `AddView` för nästling. Läser samma JSON som Swift-renderaren
   (`Serialization/WidgetJson.cs`-dokumentet) via en intern deserialisering av `type`-diskriminatorn — inte via
   `WidgetNode` (som bara serialiseras), så modellen förblir orörd.
   - `Text` → `TextView` med `setTextSize` per `TextRole` (title 22sp, headline 16sp bold, body 14sp, caption 12sp),
     `setTextColor` (semantiska färger → `?android:attr/textColorPrimary`/`Secondary` genom temat i stub-layouten,
     `green/red/orange/yellow/blue` → egna `color`-resurser med dark-variant, `accent` → `?android:attr/colorAccent`,
     hex → `Color.ParseColor`). Bold via `setTypeface`-fri väg: två stub-layouter (`_text`/`_text_bold`) eftersom
     `RemoteViews` inte kan sätta typsnitt.
   - `Timer` → `Chronometer` med `setBase(elapsedRealtime + (until − now))`, `setChronometerCountDown(true)`, started.
     Passerat `until` → `Chronometer` från 0 nedåt visar negativ tid; klampas till `now` som Swift gör
     (`max(end, now)`).
   - `Relative` → `Chronometer` som räknar upp från `date` (systemritad, tickar utan appen). Se Decisions.
   - `Progress` → `ProgressBar` (horizontal, max 1000) med `setProgressTintList` via `setColorStateList` (API 31).
   - `Icon` → `ImageView`; se Open Questions 1. `Image` → `ImageView` med `setImageViewBitmap` från `assets/`,
     nedskalad till `height`.
   - `Spacer` → `View` med `layout_weight=1` i stub-layouten (fungerar som barn i `LinearLayout` via `AddView`;
     det är förstudiens kända hål — verifieras i emulatorn). `Divider` → `View` 1dp, `?android:attr/listDivider`.
   - Familjer: `Small`/`Medium`/`Large` → en `RemoteViews` per familj, kombinerade med `RemoteViews(Map<SizeF,
     RemoteViews>)` på API 31+ så launchern väljer efter faktisk storlek; under 31 väljs efter
     `AppWidgetOptions.MinWidth`. `default` används när familjen saknas. Accessory-familjerna har ingen motsvarighet
     och ignoreras.
   - Timeline: posten som gäller *nu* renderas; nästa posts datum och `refreshAfterSeconds` schemaläggs med
     `AlarmManager.SetAndAllowWhileIdle(RtcWakeup, …)` som sänder `APPWIDGET_UPDATE` till receivern (entry-byte,
     gratis) respektive kör providern igen (refresh). Ingen exakt alarm-behörighet krävs.
3. **Receivern** `Platforms/Android/SpineAppWidget.cs`: en `AppWidgetProvider`-bas som läser sin kind ur
   `appwidget-provider`-metadata (`<meta-data android:name="spine.kind">` på receivern) och renderar i `OnUpdate`/
   `OnAppWidgetOptionsChanged`. Per kind behövs en egen `ComponentName`; se Open Questions 2.
4. **Bygget** i `build/Plugin.Maui.Spine.Widgets.targets`, ny Android-gren med samma villkor-stil som iOS-grenen
   (`GetTargetPlatformIdentifier == 'android'`, `@(SpineWidget) != ''`, inte design-time), plattformsoberoende
   (ingen shell; `WriteLinesToFile`/inline-XML) så Windows-värdar bygger Android:
   - `obj/spinewidgets/android/AndroidManifest.xml` → `AndroidManifestOverlay`: en `<receiver>` per kind med
     `android:label="DisplayName"`, `<intent-filter>` för `APPWIDGET_UPDATE`, `<meta-data android.appwidget.provider>`
     → `@xml/spine_widget_<n>` och `<meta-data spine.kind>`; `<uses-permission POST_NOTIFICATIONS>` +
     `POST_PROMOTED_NOTIFICATIONS` när `SpineWidgetsLiveActivities`; intent-filter med `android:scheme="$(ApplicationId)"`
     på en plugin-ägd trampolinaktivitet (Open Questions 2 / steg 5).
   - `res/xml/spine_widget_<n>.xml` (`appwidget-provider`: `minWidth`/`minHeight` ur minsta familjen,
     `targetCellWidth/Height` API 31, `resizeMode`, `description="@string/spine_widget_<n>_description"`,
     `widgetCategory=home_screen`, `updatePeriodMillis=0`) och `res/values/spine_widgets.xml` (strängar) →
     `AndroidResource` med `LogicalName`, hakade före `_ComputeAndroidResourcePaths`. Mappning
     `Small`→2×2 (110×110dp), `Medium`→4×2 (250×110), `Large`→4×4 (250×250), `ExtraLarge`→5×4.
   - `SpineWidgetsLiveActivities`/`SpineWidgetsEnabled` återanvänds. iOS-grenens targets rörs inte.
5. **Deep link**: `PendingIntent.GetActivity` mot en plugin-ägd `SpineWidgetLinkActivity` (genomskinlig, `NoDisplay`-
   tema, `exported` med `<data android:scheme="$(ApplicationId)" android:host="widget"/>`) med URL:en som `Data`.
   Den vidarebefordrar till appens launcher-aktivitet (`PackageManager.GetLaunchIntentForPackage`) med samma `Data`
   och `FLAG_ACTIVITY_SINGLE_TOP`, så `OnCreate` (kallstart) eller `OnNewIntent` i MAUI:s lifecycle får intentet och
   `TryHandleLink` gör resten. Samma URL som iOS: `<ApplicationId>://widget/<kind>?…`. Schemat är därmed registrerat
   i manifestet (adb `am start -d …` fungerar), utan att targeten behöver veta `MainActivity`:s Java-namn.
6. **Live Updates** `Platforms/Android/LiveUpdateNotifications.cs`: `LiveActivityLayout` → notis i kanalen
   `spine_live_updates` (importance default, ljudlös):
   - `LockScreen`-trädet → `setCustomContentView`/`setCustomBigContentView` via samma renderare (`DecoratedCustom-
     ViewStyle`), så timer/progress ritas av systemet i notisen.
   - API 36: `ProgressStyle` med ett segment (första `Progress`-noden i trädet, annars indeterminate av) +
     `setRequestPromotedOngoing(true)` (JNI), `CompactTrailing`-trädets första text/timer → `SetShortCriticalText`
     (timer → `setWhen(until)` + `setUsesChronometer`/`setChronometerCountDown` så statusfältet tickar).
   - Golv på äldre Android: se Open Questions 3.
   - Id: tag = `spine:<kind>`, `LiveActivity.Id` = tagen; `kind` läggs i `Notification.Extras`.
     `ActiveActivities()` = `NotificationManager.GetActiveNotifications()` filtrerat på tag-prefixet → överlever appstart
     utan egen lagring. `EndActivity` = `Cancel(tag, id)`. `staleAt` → alarm som postar om notisen dimmad (alpha 0.5
     på roten), samma mekanism som timeline-alarmen.
   - `AreActivitiesEnabled` = `NotificationManagerCompat.AreNotificationsEnabled()` (+ `CanPostPromotedNotifications`
     på 36 bara som logg, inte krav — en opromoterad ongoing-notis är fortfarande en Live Update i degraderad form).
7. **Drivarna**: bara projektfiler. `MauiSpineSampleApp`: inget alls om targeten gör jobbet. `Orientera`: har redan
   `POST_NOTIFICATIONS`. Sampleappens "Start live activity" behöver `Permissions.PostNotifications` på API 33+ — det
   är app-kod utanför `Widgets/`, tillåtet.
8. **Verifiering i emulatorn** (`emulator-5554`, API 36) med båda drivarna, skärmdumpar `v2-*` i
   `docs/proposals/spine-widgets/bilder/`: väljaren (namn, beskrivning, storlekar), tickande timer, `RefreshAllAsync`
   från knappen och vid `OnStop`, tryck → rätt sida (sample: Inställningar, Orientera: startlistan), Live Update i
   notisen (kompakt i statusfältet, expanderad), kvar efter appdöd + `Active` adopterar den.
9. **Dokumentation**: plattformstabellen + Android-sektion i `docs/wiki/widgets.md` (bygg, storlekar, ikoner, Live
   Updates-golvet, kända hål: `layout_weight` i djupt nästlade stackar, typsnitt, `Relative`-format), rad i
   förstudiens §5, README om plattformsraden ändras.

## Open Questions

Inga — de tre frågorna avgjordes av användaren 2026-09-07, se Decisions.

<details><summary>Frågorna som ställdes</summary>

1. **Ikoner.** Träden bär SF Symbol-namn (`figure.run`). Alternativ: (a) en liten inbyggd mappning SF-namn →
   vector drawables i pluginen, med app-överstyrning genom en drawable med samma namn i snake_case (`figure_run`)
   i appens resurser — ingen modelländring, inget nytt beroende; (b) en SVG per ikon via `Plugin.Maui.SvgIcon`/
   `SvgImage` renderad till PNG när trädet skrivs — drar in SkiaSharp i widgets-pluginen; (c) utöka `W.Icon` med ett
   plattformsalternativ — den enda punkten där modellen växer, och drivarna måste ändras.
2. **Receiver per kind.** Android kräver en `ComponentName` per widget-kind. Alternativ: (a) fasta
   `SpineAppWidget0..8` i pluginen som targeten hakar in i manifest-overlayen i deklarationsordning; (b) appen
   skriver en receiver-subklass per kind själv (bryter "inga ändringar i appen").
3. **Live Updates-golv.** `ProgressStyle` + `setRequestPromotedOngoing` kräver API 36. Alternativ: (a) samma
   `LockScreen`-notis som vanlig ongoing notification på API 26–35, promoted bara på 36 — `AreActivitiesEnabled`
   är sant så fort notiser är tillåtna; (b) `AreActivitiesEnabled = false` och `StartAsync → null` under 36.

</details>

## Changes

### `src/Plugin.Maui.Spine.Widgets` — Android ✅

- **`Platforms/Android/WidgetPlatform.Android.cs`**: `IWidgetPlatform` mot `files/spine-widgets/<kind>.json` och
  `assets/` (atomisk skrivning som på iOS), `Reload` ritar direkt via `SpineAppWidget.Update`, Live Activities via
  `LiveUpdateNotifications` bara på API 36+. `IsSupported` = strängarrayen `spine_widget_kinds` finns i appens
  resurser, dvs. targeten körde med minst ett `<SpineWidget>`.
- **`Platforms/Android/SpineWidgetsExtensions.Android.cs`**: `OnCreate` (kallstart → `RefreshAll`, länk i
  `Intent.Data`), `OnNewIntent` (varm länk), `OnStop` → `RefreshAll` när `RefreshOnBackground`. Länkar markeras som
  hanterade på intentet så en omskapad aktivitet inte navigerar igen. `Default.cs` gäller nu `!IOS && !ANDROID`.
- **`Platforms/Android/RemoteViewsRenderer.cs`**: JSON-trädet → `RemoteViews` med en stub-layout per nodtyp
  (`Resources/layout/spine_widget_*.xml`) och `AddView` för nästling. Text/timer/relative med textstorlek per roll och
  fet variant som egen layout; färger via `setColorAttr`/`setColorInt`/`setColorStateList` på API 31+ så semantiska
  färger följer launcherns ljus/mörk, hex via `Color.ParseColor` (alfa-varianten hanteras av plattformen — inte av
  egen parsning, vilket var iOS-felet). `WidgetPalette` bär iOS-systemfärgerna i ljus och mörk variant.
- **`Platforms/Android/WidgetIcons.cs`**: SF-namn → inbäddad SVG (`figure_run.svg`) → vit bitmap via Svg.Skia, tintad
  av vyn. Cache per namn och pixelstorlek.
- **`Platforms/Android/SpineAppWidget.cs`**: bas-`AppWidgetProvider` + `SpineAppWidget0..8` med fasta Java-namn
  (`[Register]`, ingen manifest-emission från attribut). `Update` väljer timeline-posten som gäller nu, bygger en
  `RemoteViews` per familj (`RemoteViews(Map<SizeF,…>)` på API 31+, annars efter widget-options) och schemalägger
  två inexakta alarm per kind: RENDER vid nästa posts datum och REFRESH (`refreshAfterSeconds` efter sista posten)
  som kör providern via `IWidgetService` i receivern (`GoAsync`). Placerad widget utan dokument visar "—" och
  begär en REFRESH.
- **`Platforms/Android/SpineWidgetLinkActivity.cs`**: trampolin med `Theme.NoDisplay` som tar emot
  `<ApplicationId>://widget/…` och startar appens launcher-aktivitet med samma `Data` (`NEW_TASK | SINGLE_TOP`).
- **`Platforms/Android/LiveUpdateNotifications.cs`** (`[SupportedOSPlatform("android36.0")]`): layouten → notis i
  kanalen `spine_live_updates`: titel/text ur `LockScreen`-trädets texter, `Timer` → `setWhen` + chronometer
  (nedräkning), `Progress` → `ProgressStyle` med ett segment i nodens färg (annars `BigTextStyle`), `CompactTrailing`-
  text → `setShortCriticalText`, första ikonen → liten ikon + accentfärg, `Link` → content intent via trampolinen,
  `setRequestPromotedOngoing(true)` via JNI. Tag `spine-widgets:<kind>` + fast id; `Active()` läser
  `GetActiveNotifications()` så listan överlever appstart.
- **`build/Plugin.Maui.Spine.Widgets.targets`**: inline-tasken `SpineWidgetsAndroidGenerate` (RoslynCodeTaskFactory)
  skriver `AndroidManifest.xml`-overlay (receivers, trampolinens intent-filter med `$(ApplicationId)`-schemat,
  `POST_NOTIFICATIONS` + `POST_PROMOTED_NOTIFICATIONS` när `SpineWidgetsLiveActivities`), `res/xml/spine_widget_N.xml`
  och `res/values/spine_widgets.xml` (kinds-array, label, beskrivning) till `obj/spinewidgets/android/`, hakade in
  som `AndroidManifestOverlay` och `AndroidResource` med `LogicalName` före `_ComputeAndroidResourcePaths`/
  `_ManifestMerger`. Skriver bara när innehållet ändrats.
- **`Services/IWidgetPlatform.cs`**: `StartActivity` → `StartActivityAsync`, eftersom Android måste be om
  `POST_NOTIFICATIONS` innan en Live Update kan postas (iOS-implementationen wrappar sitt synkrona anrop).

### Drivarna

- `samples/MauiSpineSampleApp`: `Resources/Svg/figure_run.svg` + `<EmbeddedResource>` för `Resources\Svg\*.svg`.
  `Widgets/SampleWidget.cs` orörd.
- `samples/Orientera`: `Resources/Svg/figure_run.svg` (globben fanns redan). `Widgets/*` orörda.

### Verifierat i emulatorn ✅

`emulator-5554` (Pixel Tablet) delades med en annan session, så allt utom `v2-01` kördes på `Pixel_10_Pro`
(`emulator-5556`, API 37-preview, dvs. Android 16-funktionerna finns). Skärmdumpar i
[docs/proposals/spine-widgets/bilder](../docs/proposals/spine-widgets/bilder/) med prefix `v2-`:

| Steg | Resultat | Bild |
| --- | --- | --- |
| Väljaren | "Spine sample" under appen med `Description` och 2×2 ur `<SpineWidget>` | `v2-01` |
| Timern | `W.Timer` räknar ned utan appen (40:26 → 40:19 på sju sekunder), `W.Relative` räknar upp | `v2-02-a/b` |
| Familjer | Storleksändring till 4×2 byter till medium-trädet via `RemoteViews(Map<SizeF,…>)` | `v2-03` |
| Reload vid bakgrund | `OnStop` → `RefreshAllAsync`: "Built by the app at" 15:09:59 → 15:12:02 utan knapptryck | `v2-04` |
| Deep link | Tryck öppnar Inställningar via `IWidgetLinkHandler`, både varm (`OnNewIntent`) och kall (`OnCreate` efter `am kill`) | `v2-05` |
| Live Update | Chip i statusfältet med tickande nedräkning (systemritad), panel med ProgressStyle-segment i nodens färg, titel och text ur `LockScreen`-trädet; `dumpsys notification` visar `PROMOTED_ONGOING` | `v2-06`, `v2-07` |
| `Active` | "End live activity" direkt efter start och efter `am kill` + omstart via widgeten; "End" tar bort notisen | `v2-08`, `v2-09` |
| Orientera | Demoläge med tidsmaskinen dagen före start: "Följ på låsskärmen" → behörighetsfråga → `PROMOTED_ONGOING`-notis "Norrlandsmästerskapen Lång / Din start 11:04", Hem visar "Sluta följa". Widgeten "Nästa start" renderar med BrandTint `#2E8B57` (hex-alfa-fallet som föll på iOS) | `v2-10`, `v2-11`, `v2-12` |

### Dokumentation ✅

- **`docs/wiki/widgets.md`**: plattformstabellen, Android-noter i uppsättning/ikoner/länkar/Live Activities, en
  Android-sektion (mappningstabell, ikoner, Live Updates-mappning, kända hål) och nya rader i felsökningen.
- **Förstudien**: status med #168, Android-raden i §5 markerad implementerad, v2-punkten i §6 markerad levererad.
- **README**: sampleappens demotabell säger iOS och Android.

### Kvar

- Fysisk Android-enhet är inte testad (bara emulatorer, API 36 tablet och API 37 telefon).
- Under API 31 är färger fasta och familjen väljs efter widget-options; inte verifierat i emulator.
- Push-to-start, fjärrkälla och knappar (`AppIntent`/broadcast) är fortfarande v2-punkter i förstudien.

**Fel som bara emulatorn hittade** (alla rättade, se Decisions): `android.view.View` som stub, ikon-SVG:n som
kulturresurs, `AddView` utan `RemoveAllViews` vid storleksändring, trampolinen i appens task (skapade en andra
`MainActivity` som kraschade i Sharpnado), och ett `Refresh(after)` i det förflutna som skulle ha gett ett alarm
per minut på en fejkad klocka.

## Decisions

- **Ikoner via SvgIcon-pipelinen** (avgjort av användaren): en `IconNode` med `systemImage: "figure.run"` renderas
  från en inbäddad SVG som heter `figure.run.svg` (eller `figure_run.svg`) i någon av assemblierna `UseSpine` fick,
  via `ResourceNameCache` som Spine redan registrerar, till en bitmap när trädet renderas. Tinten sätts på
  `ImageView` (`setColorFilter`), så samma bitmap fungerar i ljust och mörkt. Modellen och drivarna är orörda;
  drivar-apparna får en SVG-resurs. Skalan är SkiaSharp som `Plugin.Maui.Spine` redan drar in transitivt.
- **Fasta `SpineAppWidget0..8` i pluginen** (avgjort av användaren): nio `AppWidgetProvider`-subklasser med fasta
  Java-namn, targeten skriver manifest-overlay + `appwidget-provider`-XML + strängar per deklarerad kind i
  deklarationsordning. Samma tak som iOS (nio), ingen source generator, appens manifest orört.
- **Live Updates bara på API 36+** (avgjort av användaren): under 36 är `AreActivitiesEnabled = false` och
  `StartAsync` ger `null`; widgetar fungerar oavsett. `IsSupported` förblir gemensam för widget och aktivitet,
  som på iOS.
- **Generatorn på Android är en inline `RoslynCodeTaskFactory`-task i targeten**, inte ett skript: Android bygger
  även på Windows-värdar, och XML-escaping av `DisplayName`/`Description` görs rätt med `XmlWriter` i stället för
  sed-kedjor. iOS-skriptet rörs inte.

- **`Relative` → `Chronometer` som räknar upp**, inte `TextView` med relativ tid: `RemoteViews` har ingen
  systemritad "3 min ago"-text (ingen `setRelativeTime` finns; `DateUtils.getRelativeTimeSpanString` är en
  engångsberäkning som står stilla till nästa rendering). En uppräknande `Chronometer` tickar utan appen, vilket är
  nodens kontrakt; formatet blir `mm:ss`/`h:mm:ss` i stället för "3 min ago", dokumenterat i wikin.
- **Deep link via plugin-ägd trampolinaktivitet** i stället för intent-filter på `MainActivity`: targeten känner
  inte `MainActivity`:s genererade Java-namn, och en manifest-overlay som försöker slå ihop med den är skör.
  Trampolinen är en `<activity>` pluginen äger, schemat sätts av targeten, och `MainActivity` förblir orörd.
- **Ikon-SVG:n heter `figure_run.svg`, inte `figure.run.svg`.** .NET SDK:ns `AssignCulture` läser `figure.run.svg`
  som en kulturresurs — "run" är ISO 639-3 för kirundi — och lägger den i en satellit-assembly, så
  `ResourceNameCache` hittar den aldrig. Renderaren provar underscore-namnet först och punktnamnet sist (för appar
  som sätter `WithCulture="false"`), och dokumentationen anger bara underscore-formen.
- **`android.view.View` får inte inflateras av en RemoteViews-värd** (launchern loggar "Class not allowed to be
  inflated"), så `Spacer` och `Divider` är `FrameLayout`-stubbar.
- **`RemoveAllViews` före varje `AddView`.** När launchern ändrar widgetens storlek återapplicerar den
  `RemoteViews`-åtgärderna på den befintliga vyn i stället för att inflatera om, och `AddView` lade då medium-trädet
  under small-trädet i samma kort (syntes direkt vid första storleksändringen på Pixel 10 Pro). Det kanoniska
  mönstret är att tömma containern i samma åtgärdslista.
- **Trampolinaktiviteten kör i egen task (`TaskAffinity=""`) och startar appen med `CLEAR_TOP | SINGLE_TOP`.**
  I appens task låg den ovanpå `MainActivity`, så `SINGLE_TOP` skapade en andra `MainActivity` i stället för att
  leverera intentet — och MAUI-appen kraschade i en handler. Med egen task och `CLEAR_TOP` blir det `onNewIntent`
  på den befintliga instansen (varm) eller en ny task (kall).
- **Ett `Refresh(after)` som redan passerat skjuts 15 minuter fram**, inte en minut: en provider på en fejkad
  klocka (Orienteras tidsmaskin) eller ett gammalt dokument skulle annars ge ett alarm i minuten som kör
  providern i bakgrunden. Femton minuter är också WorkManagers golv och iOS praktiska.
- **En passerad `W.Timer` i en Live Update visar ingen chronometer** i stället för negativ tid — samma klampning
  som widgeten och Swift-renderaren (`max(end, now)`).
- **`setRequestPromotedOngoing` via JNI**: inte bundet i Mono.Android 36.1.53; en `JNIEnv.CallObjectMethod`-rad
  med kommentar, i stället för att vänta på nästa bindning.
