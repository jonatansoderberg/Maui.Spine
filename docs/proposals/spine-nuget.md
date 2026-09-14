# Spine som NuGet-paket — paketkarta och implementationsplan (rev 2)

**Status:** Proposal — inget implementerat.
**Rev 2:** `Plugin.Maui.Spine.Push` döps om till `Plugin.Maui.Spine.PushNotifications` före första releasen (§2, steg 2). Nytt avsnitt §7 om vad som inte bryts ut.
**Fråga:** Orientera ska flytta till ett eget repo. Vilka paket ska Spine publicera, hur ska de heta och avgränsas så att ramverket kan användas modulärt, och hur bygger och publicerar vi releaser direkt från GitHub?
**Svar:** Åtta paket under det namn som redan är repots, `Plugin.Maui.Spine.*`, ett per befintligt `src/`-projekt, med gemensam version och en tagg-driven GitHub Actions-release till nuget.org. Inga projekt behöver delas eller slås ihop; ett byter namn (Push blir PushNotifications). Det som saknas är paketmetadata, tre byggfixar som avgör om `build/`-mappen faktiskt fungerar ur ett paket, och två workflows.

---

## 1. Nuläge

Repot har åtta bibliotek under `src/`, inget av dem har paketmetadata i dag och det finns varken workflows, taggar eller versionsnummer. Ingen `Plugin.Maui.Spine*`-id är tagen på nuget.org (kontrollerat 2026-09-11). Orientera konsumerar biblioteken med `ProjectReference` och importerar tre `.targets`-filer uttryckligen.

Vad Orientera faktiskt använder:

| Projekt | Använder |
|---|---|
| `samples/Orientera` (appen) | `Plugin.Maui.Spine`, `.Svg`, `.Widgets`, `.Push` (blir `.PushNotifications`), samt `.Common` transitivt. `.Controls.HeroCollectionView` refereras men används inte — bara ett globalt xmlns i `GlobalXmlns.cs` pekar på det, ingen sida använder kontrollen; referensen kan strykas vid flytten. |
| `samples/Orientera.Backend` (Functions) | `Plugin.Maui.Spine.Server`, `.Common` |
| `Orientera.Domain`, `Orientera.Tests`, `Orientera.AppHost` | inget från Spine |

`Plugin.Maui.Spine.Controls.AnimatedLabel` används bara av `MauiSpineSampleApp`.

---

## 2. Paketkartan

Paketen följer projekten ett till ett, och id, assemblynamn och rotnamespace är samma sak. Ett projekt byter namn: `Plugin.Maui.Spine.Push` blir **`Plugin.Maui.Spine.PushNotifications`**. Paketet är lika mycket lokala notiser (`ILocalNotificationService` med Android- och Apple-implementationer, notisstore, action-receivers, behörighetsflödet) som remote push, och namnet säger vad som faktiskt visas för användaren. Serverpaketet förblir push, för det gör inget annat: servern skickar push, klienten hanterar notiser.

Två alternativ som valts bort:

- **Prefixet `Spine.Maui.*`** i stället för `Plugin.Maui.Spine.*` — kräver namespace-byte i hela repot och i Orientera utan att ge något tillbaka.
- **Ett gruppsegment för tillvalen**, som `Plugin.Maui.Spine.Extensions.Widgets`. `Plugin.Maui.Spine.Extensions` är redan kärnans namespace (`UseSpine`, glass, knappar), Svg är ingen extension utan något kärnan beror på, och ett segment lönar sig när det samlar många små saker av samma slag — vilket `Controls.` gör, men Widgets och PushNotifications är två stora delsystem med egen byggpipeline, egen native kod och för push en serverhalva. De är jämbördiga med kärnan. Ekosystemet gör likadant: `CommunityToolkit.Maui.MediaElement`, `Microsoft.Maui.Controls.Maps`. Grupperingen görs i README:s pakettabell i stället: **Kärna** (`Plugin.Maui.Spine`, `.Svg`), **Utanför fönstret** (`.Widgets`, `.PushNotifications`), **Kontroller** (`.Controls.*`), **Server** (`.Common`, `.Server`).

| Paket | TFM | Beroenden (Spine) | Beroenden (externa) | Innehåll |
|---|---|---|---|---|
| **`Plugin.Maui.Spine`** | android, ios, maccatalyst, windows | `.Svg` | Microsoft.Maui.Controls, CommunityToolkit.Mvvm, AsyncAwaitBestPractices; WinUIEx och Microsoft.WindowsAppSDK (windows) | Navigering, regioner, sheets, tab host, header bar, glass, shortcuts, Windows-fönster. `build/`: entitlements-steget som Widgets och Push skriver nycklar genom. |
| **`Plugin.Maui.Spine.Svg`** | android, ios, maccatalyst, windows | — | SkiaSharp.Views.Maui.Controls, Svg.Skia | `SvgImageSource`, `SvgIcon`, `ISvgIconService`, `ResourceNameCache`; 164 inbäddade ikoner (se §6). |
| **`Plugin.Maui.Spine.Common`** | net10.0 | — | — | Kontrakten app och server delar: widgetträdet (`W`, `WidgetNode`, `LiveActivityLayout`), `PushInstallation`, `PushKeys`, `PushTagExpression`, JSON-serialiseringen. Ingen MAUI. |
| **`Plugin.Maui.Spine.Widgets`** | android, ios, maccatalyst, windows | `Plugin.Maui.Spine`, `.Common` | Microsoft.Maui.Controls | `IWidgetService`, `ILiveActivityService`, Android-receivers. `build/`: targets + `spine-widgets-build.sh`; `native/ios/`: fem Swift-filer som blir extensionen. |
| **`Plugin.Maui.Spine.PushNotifications`** (i dag `.Push`) | android, ios, maccatalyst, windows | `Plugin.Maui.Spine`, `.Common` | Microsoft.Maui.Controls; Xamarin.Firebase.Messaging + AndroidX-pinnarna (android) | `IPushNotificationService`, `IPushNotificationHandler`, `ILocalNotificationService`. `build/`: targets + `spine-push-notifications-build.sh`; `native/ios/`: Notification Service Extension. |
| **`Plugin.Maui.Spine.Server`** | net10.0 | `.Common` | Azure.Data.Tables, FirebaseAdmin; FrameworkReference Microsoft.AspNetCore.App | Enhetsregister, transporter mot APNs/FCM/WNS, `MapSpinePush`, `AddSpinePush`. |
| **`Plugin.Maui.Spine.Controls.HeroCollectionView`** | android, ios, maccatalyst, windows | `.Svg` | Microsoft.Maui.Controls, SkiaSharp | `CollectionView` med kollapsande sticky header, titelöverlägg och färgsamplande overlay; på Windows även dragyta för egen titelrad. |
| **`Plugin.Maui.Spine.Controls.AnimatedLabel`** | android, ios, maccatalyst, windows | — | Microsoft.Maui.Controls, SkiaSharp.Views.Maui.Controls | Fristående kontroll. Lägst prioritet, men gratis att ta med när pipelinen finns. |

Beroendegraf, så som den ser ut i dag:

```
Common ◄──────────────┬──────────── Server
  ▲                   │
  │                   │
Widgets ──► Spine ──► Svg ◄── HeroCollectionView
  ▲              ▲
  └── PushNotifications    AnimatedLabel (fristående)
```

Tre saker om grafen är värda att veta:

- **PushNotifications refererar `Plugin.Maui.Spine` men använder ingen kod därifrån.** Referensen finns för att dess targets kör `DependsOnTargets="_SpineWriteEntitlements"`, som ligger i kärnpaketets `build/`. En app som bara vill ha notiser drar därmed in kärnan, Svg, SkiaSharp och Svg.Skia. Det lämnas som det är i första releasen (§6, beslut B); att lossa beroendet senare är inte brytande för konsumenter.
- **Widgets beror på kärnan på riktigt** — `WidgetRegistry` läser `SpineOptions` för att hitta `[Widget]`-providers i de assemblies `UseSpine` fick.
- **Kärnan beror på Svg på riktigt** — `ResourceNameCache` injiceras i tab-hosten och tray-ikonerna på Windows/Mac Catalyst går genom `ISvgIconService`.

Så en konsument kan välja: bara navigering (kärna + Svg), navigering + widgets, navigering + notiser + server, eller bara server + Common i ett backend-projekt utan MAUI. Det är den modularitet som finns att hämta utan att dela projekt, och den räcker för Orientera.

---

## 3. Vad som inte fungerar ur ett paket i dag

Tre saker fungerar med `ProjectReference` men går sönder när samma filer kommer ur `~/.nuget/packages`. Alla tre måste rättas före första releasen.

**A. Kärnans targets-fil importeras aldrig.** NuGet importerar bara `build/<PackageId>.targets`, det vill säga `Plugin.Maui.Spine.targets`. Filen heter `Plugin.Maui.Spine.Entitlements.targets` och packas som den är, så `_SpineWriteEntitlements` skulle inte finnas i en konsuments build och både Widgets och PushNotifications skulle stanna på ett okänt target. Kommentaren i filen ("a PackageReference imports it on its own") stämmer alltså inte. Lösning: döp om filen till `Plugin.Maui.Spine.targets` och uppdatera de tre samplarnas `<Import>`.

**B. Skripten körs utan exekveringsbit.** Targets kör `"$(_SpineWidgetsScript)" --out …` direkt. I git har `.sh`-filerna läge 100755, men en nupkg är en zip utan Unix-rättigheter och NuGet sätter inga vid uppackning; ur ett paket är skriptet 0644 och `Exec` får *Permission denied*. Lösning: anropa `bash "$(_SpineWidgetsScript)" …` respektive `bash "$(_SpinePushNotificationsScript)" …` (två rader).

**C. Radslut.** `.gitattributes` har `* text=auto`. På en Windows-runner har git `core.autocrlf=true`, så `.sh`- och `.swift`-filerna checkas ut med CRLF och packas så — och `bash` på konsumentens Mac stannar på `\r`. Lösning: lägg till `*.sh text eol=lf` och `*.swift text eol=lf` i `.gitattributes` (och normalisera med `git add --renormalize`).

Resten av `build/`-upplägget håller: skript och `native/` lokaliseras med `MSBuildThisFileDirectory` och `..\native\ios`, vilket är samma relativa läge i paketet (`build/`, `buildTransitive/` och `native/` är syskon) som i källträdet. Android-tasken i Widgets-targets är en `RoslynCodeTaskFactory`-task i själva targets-filen och behöver ingen tools-assembly. De native stegen är villkorade på `IsOSPlatform('OSX')` och inre iOS-byggen, så en Windows-konsument påverkas inte.

---

## 4. Versionering och release-flöde

**En version för alla paket.** Paketen släpps i låst takt ur samma repo; separat versionering per paket ger inget förrän ramverket har externa konsumenter med olika uppgraderingstakt. Version är alltid `Major.Minor.Patch` med valfritt `-preview.N`.

**Taggen är versionen.** En release är `git tag v0.1.0 && git push --tags`. Workflown läser versionen ur taggnamnet och skickar den som `-p:Version=`. Lokalt utan tagg bygger allt som `0.0.0-local` från `VersionPrefix` i `src/Directory.Build.props`, så en lokal nupkg kan aldrig förväxlas med en publicerad. (Alternativet MinVer ger samma sak plus automatiska prerelease-nummer per commit; det är ett paket till i byggkedjan och behövs inte förrän vi vill ha en kontinuerlig prerelease-feed.)

**Prerelease går samma väg.** `v0.2.0-preview.1` publiceras som prerelease på nuget.org. GitHub Packages som NuGet-feed väljs bort: den kräver en PAT även för publika paket, vilket är precis fel friktion i en MAUI-app som ska byggas på flera maskiner.

**Startversion:** `0.1.0`. API:t rör sig fortfarande, och 0.x säger det.

**Runner: `windows-latest`.** Det finns bara en runner som kan ge alla fyra TFM i ett bygge: `net10.0-windows10.0.19041.0` kräver Windows, och iOS- och Mac Catalyst-*bibliotek* kompileras på Windows utan en parad Mac (referensassemblies räcker; en Mac behövs först vid app-paketering). `Directory.Build.props` lägger till Windows-TFM:et bara på Windows-värd, så ett bygge på macOS ger ett paket utan Windows-stöd — vilket är rätt lokalt men fel i en release. Risken som ska verifieras i första CI-körningen: att iOS-biblioteken faktiskt bygger på Windows-runnern (de gör det för vanliga MAUI-klassbibliotek; det här repot har inga native referenser i själva biblioteken).

**Publicering:** `dotnet nuget push` med en API-nyckel i secreten `NUGET_API_KEY`, scope:ad till `Plugin.Maui.Spine*`. nuget.org:s *Trusted Publishing* (OIDC från GitHub Actions, ingen nyckel) är ett bättre alternativ om det är tillgängligt för kontot när vi sätter upp det; det byts in i ett steg utan att workflown i övrigt ändras.

---

## 5. Implementationsplan

Steg 2 (namnbytet) är en egen PR först, eftersom det rör alla samplar, wikin och serverns API. Steg 1 och 3–6 kan göras i en PR (`feature/nuget-packaging`); steg 7 är kontosaker; steg 8 är första releasen; steg 9 är Orientera.

### Steg 1 — Paketmetadata i `src/Directory.Build.props`

Ny fil som importerar rotens props (`$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))`) och sätter det gemensamma:

```xml
<IsPackable>true</IsPackable>
<VersionPrefix>0.0.0</VersionPrefix>
<VersionSuffix>local</VersionSuffix>
<Authors>Jonatan Söderberg</Authors>
<Copyright>© Jonatan Söderberg</Copyright>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<PackageProjectUrl>https://github.com/jonatansoderberg/Maui.Spine</PackageProjectUrl>
<RepositoryUrl>https://github.com/jonatansoderberg/Maui.Spine</RepositoryUrl>
<RepositoryType>git</RepositoryType>
<PackageReadmeFile>README.md</PackageReadmeFile>
<PackageIcon>icon.png</PackageIcon>
<PackageTags>maui;dotnet-maui;navigation;spine</PackageTags>
<PackageReleaseNotes>https://github.com/jonatansoderberg/Maui.Spine/releases</PackageReleaseNotes>
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<PublishRepositoryUrl>true</PublishRepositoryUrl>
<EmbedUntrackedSources>true</EmbedUntrackedSources>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
<ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
<PackageOutputPath>$(MSBuildThisFileDirectory)../artifacts/packages/</PackageOutputPath>
```

plus `<None Include="README.md" Pack="true" PackagePath="/" />` och samma för `$(MSBuildThisFileDirectory)../assets/icon.png`. Source Link för GitHub ingår i SDK:t sedan .NET 8 och behöver inget paket.

Per projekt: `Description` och gärna projektspecifika `PackageTags` (t.ex. `push;notifications;apns;fcm` för PushNotifications, `widgets;widgetkit;live-activities` för Widgets). `Plugin.Maui.Spine.Common` och `.Server` får ingen MAUI-tagg.

`samples/Directory.Build.props` och `tests/Directory.Build.props`: importera roten och sätt `IsPackable=false`, så `dotnet pack Spine.slnx` bara packar `src/`.

Städning i samma steg: `GenerateDocumentationFile` saknas i HeroCollectionView och AnimatedLabel (kommer från props nu, men de behöver XML-doc på publika typer för att slippa CS1591-brus — eller `<NoWarn>CS1591</NoWarn>` tills dess); `<Compile Remove="AdaptiveOverlayBehavior.cs" />` i HeroCollectionView pekar på en fil som inte finns.

### Steg 2 — Push blir PushNotifications

Ett fullständigt byte, i en egen PR, innan något publiceras. Regeln är enkel: **varje namn som säger `Push` som ord för delsystemet säger `PushNotifications` efteråt** — paket, namespace, MSBuild-knoppar, skript, extension, wiki, samplar och serverns API. Typer som beskriver en enskild push-sak (`PushMessage`, `PushPermission`, `PushStatus`, `PushInstallation`, `PushRegistrationClient`) står kvar.

| Vad | I dag | Efter |
|---|---|---|
| Projekt, assembly, paket-id | `Plugin.Maui.Spine.Push` | `Plugin.Maui.Spine.PushNotifications` |
| Rotnamespace (28 filer) | `Plugin.Maui.Spine.Push`, `.Push.Extensions`, `.Push.Services` | `Plugin.Maui.Spine.PushNotifications`, `.PushNotifications.Extensions`, `.PushNotifications.Services` |
| Registrering | `UseSpinePush()` i `SpinePushExtensions` | `UseSpinePushNotifications()` i `SpinePushNotificationsExtensions` — registrerar som i dag både push och lokala notiser |
| Klientens klasser med `SpinePush`-prefix | `SpinePushOptions`, `SpinePushLifecycle`, `SpinePushMessagingService` (Android) | `SpinePushNotificationsOptions`, `SpinePushNotificationsLifecycle`, `SpinePushNotificationsMessagingService` |
| Push-gränssnitten | `IPushService`, `IPushHandler` | `IPushNotificationService`, `IPushNotificationHandler`; implementationen `PushService` blir `PushNotificationService`. Singular, som `ILocalNotificationService`. Refereras i 15 filer i projektet, 6 i wikin, 3 i Orientera och 5 i samplen. |
| MSBuild-knoppar | `SpinePushEnabled`, `SpinePushRemote`, `SpinePushImages`, `SpinePushImagesCodesignProvision`, `SpinePushEnvironment` | `SpinePushNotificationsEnabled`, `SpinePushNotificationsRemote`, `SpinePushNotificationsImages`, `SpinePushNotificationsImagesCodesignProvision`, `SpinePushNotificationsEnvironment`; de interna `_SpinePush*` följer med |
| Widgets-targets rad 62 | läser `SpinePushEnvironment` | läser `SpinePushNotificationsEnvironment` |
| Targets-fil | `build/Plugin.Maui.Spine.Push.targets` | `build/Plugin.Maui.Spine.PushNotifications.targets` (måste matcha paket-id) |
| Skript | `build/spine-push-build.sh` | `build/spine-push-notifications-build.sh` |
| Notification Service Extension | `native/ios/SpineNotificationService.swift`, appex-namnet `SpineNotificationService` | `SpinePushNotificationService.swift`, appex-namnet `SpinePushNotificationService` |
| Serverns API | `AddSpinePush`, `MapSpinePush`, `SpinePushOptions`, `SpinePushEndpoints` | `AddSpinePushNotifications`, `MapSpinePushNotifications`, `SpinePushNotificationsOptions`, `SpinePushNotificationsEndpoints`. Paketet heter fortfarande `Plugin.Maui.Spine.Server`. |
| Wiki | `push.md`, `push-server.md` | `push-notifications.md`, `push-notifications-server.md`; länkarna från `widgets.md` och README följer med |
| Samplar | `MauiSpinePushSampleApp`, `MauiSpinePushSampleApp.Server` | `MauiSpinePushNotificationsSampleApp`, `MauiSpinePushNotificationsSampleApp.Server`; kataloger, csproj, `RootNamespace`, `Spine.slnx` |

Options-klassen heter `SpinePushNotificationsOptions` på både klient och server, i olika namespaces — samma läge som i dag med `SpinePushOptions`, och de möts aldrig i ett och samma projekt.

**En konsekvens att känna till:** appex-namnet ingår i extensionens bundle-id, `$(ApplicationId).SpineNotificationService`, som blir `$(ApplicationId).SpinePushNotificationService`. Det är ett nytt App ID i utvecklarportalen för varje app som bygger med `SpinePushNotificationsImages=true` på enhet, med en ny provisioning profile. Push-samplens profil är redan ogiltig och ska göras om ändå; Orientera har inte bildextensionen påslagen. Simulatorbyggen berörs inte.

Utanför projektet: Orientera (`MauiProgram.cs`, `Platforms/iOS/Program.cs`, `Services/Push/*`, `SpinePushEnabled`-villkoret i csproj) och samplen får nya `using`-rader, typnamn och knoppar i samma PR. Issues och proposals som säger Spine.Push lämnas som historik.

### Steg 3 — Byggfixarna i §3

1. `git mv src/Plugin.Maui.Spine/build/Plugin.Maui.Spine.Entitlements.targets src/Plugin.Maui.Spine/build/Plugin.Maui.Spine.targets`; uppdatera `<Import>` i `MauiSpineSampleApp`, notis-samplen och `Orientera` samt kommentaren i filen.
2. `bash` framför skriptanropen i `Plugin.Maui.Spine.Widgets.targets:91` och `Plugin.Maui.Spine.PushNotifications.targets` (i dag `Plugin.Maui.Spine.Push.targets:130`).
3. `.gitattributes`: `*.sh text eol=lf`, `*.swift text eol=lf`; kör `git add --renormalize .` och kontrollera att diffen är tom.
4. Kosmetiskt: det avslutande semikolonet i `PackagePath="build\;buildTransitive\;"` i kärnan och PushNotifications kan tas bort (Widgets har det redan utan).
5. Repohygien i samma PR: `build_output.txt` (9 MB byggutskrift) och `MAC_TRAY_DEBUG.md` är spårade i git och ska bort; `docs/pages-guide.md` och `.github/copilot-instructions.md` talar fortfarande om `MauiBottomSheetPoc` och behöver antingen uppdateras eller tas bort innan repot får läsare via nuget.org.

### Steg 4 — README per paket och ikon

- `src/<Projekt>/README.md`: en skärm text — vad paketet gör, `dotnet add package`, minsta `MauiProgram`-snutt, länk till wikisidan med absolut GitHub-URL (nuget.org löser inte relativa länkar). Wikin har redan en sida per paket (`getting-started.md`, `svg.md`, `widgets.md`, `push-notifications.md`, `push-notifications-server.md`, `hero-collection-view.md`, `animated-label.md`) att korta ned från.
- `assets/icon.png`, 128×128, en ikon för alla paket.
- `LICENSE.txt` har fortfarande mallens `[year] [fullname]`; fyll i. Paketet använder `PackageLicenseExpression` och bär inte filen, men repot är publikt.
- Root-`README.md`: en pakettabell (som §2 ovan, kortare) och en uppdaterad plattformstabell — den säger "iOS in progress" medan Orientera kör på iOS.

### Steg 5 — `.github/workflows/ci.yml`

Kör på `pull_request` och `push` till `master`. Ett jobb på `windows-latest` med `defaults: run: shell: bash`:

```yaml
- uses: actions/checkout@v4
- uses: actions/setup-dotnet@v4
  with: { global-json-file: global.json }
- run: dotnet workload install maui
- run: dotnet restore Spine.Packages.slnf
- run: dotnet build Spine.Packages.slnf -c Release --no-restore
- run: dotnet test tests/Plugin.Maui.Spine.Server.Tests -c Release --no-build
- run: dotnet pack Spine.Packages.slnf -c Release --no-build
- uses: actions/upload-artifact@v4
  with: { name: packages, path: artifacts/packages/*.nupkg }
```

`Spine.Packages.slnf` är ett lösningsfilter över `src/` och `tests/`: samplarna är appar med signering och `google-services.json`-villkor och hör inte hemma i CI förrän det finns ett skäl. (Fungerar filtret inte mot `.slnx` i SDK:t på runnern är reserven att lista de nio projektsökvägarna i workflown.) `global.json` pekar på SDK och workload-set `10.0.201`, så `workload install` blir deterministiskt. Räkna med 10–15 minuter per körning, varav hälften är workload-installation; cache:a `~/.nuget/packages` med `actions/cache`.

### Steg 6 — `.github/workflows/release.yml`

Trigger `push: tags: ['v*']`. Samma jobb som CI plus:

```yaml
- name: Version from tag
  run: echo "VERSION=${GITHUB_REF_NAME#v}" >> $GITHUB_ENV
  shell: bash
- run: dotnet pack Spine.Packages.slnf -c Release --no-build -p:Version=${{ env.VERSION }}
- run: dotnet nuget push artifacts/packages/*.nupkg --source https://api.nuget.org/v3/index.json --api-key ${{ secrets.NUGET_API_KEY }} --skip-duplicate
- run: gh release create ${{ github.ref_name }} artifacts/packages/*.nupkg --generate-notes ${{ contains(github.ref_name, '-') && '--prerelease' || '' }}
  env: { GH_TOKEN: ${{ github.token }} }
```

Så att `-p:Version` slår igenom i `--no-build`-läget måste bygget före också få `-p:Version`; enklast är att sätta `VERSION` först och skicka den till både `build` och `pack`. `permissions: contents: write` för release-steget. `--skip-duplicate` gör att en omkörd workflow inte faller på redan publicerade paket. Release-noterna genereras från PR-titlar sedan förra taggen — det är den changelog vi får utan att skriva en.

Valfritt men billigt: ett steg som listar innehållet i varje nupkg (`unzip -l`) i loggen, så att `build/`, `buildTransitive/`, `native/` och alla fyra `lib/`-TFM syns i varje release.

### Steg 7 — Konto och secrets

- nuget.org-konto; API-nyckel med scope *Push new packages and package versions*, glob `Plugin.Maui.Spine*`, giltighet 365 dagar; lägg som repo-secret `NUGET_API_KEY`. (Eller Trusted Publishing, se §4.)
- Repo-inställning: Actions får skapa releases (`contents: write` i workflown räcker med `GITHUB_TOKEN`).

### Steg 8 — Verifiera lokalt och släpp `v0.1.0`

1. `dotnet pack Spine.Packages.slnf -c Release` lokalt (ger tre TFM på Mac, vilket räcker för att verifiera layouten): `unzip -l artifacts/packages/Plugin.Maui.Spine.Widgets.0.0.0-local.nupkg` ska visa `build/Plugin.Maui.Spine.Widgets.targets`, `build/spine-widgets-build.sh`, `buildTransitive/…`, `native/ios/*.swift`, `lib/net10.0-ios18.0/…` osv.
2. Konsumera paketen i ett scratch-projekt utanför repot (samma upplägg som tidigare iOS-spikar: kopiera `global.json`, lokal feed via `nuget.config` som pekar på `artifacts/packages`), med ett `<SpineWidget>` och `SpinePushNotificationsEnabled`, och bygg för simulatorn. Det är det enda testet som bevisar §3 A–C på riktigt: att entitlements skrivs, att skriptet körs och att extensionen hamnar i `.app`.
3. Merge:a PR:en, kör CI grönt på master, tagga `v0.1.0`, följ release-workflown. Kontrollera på nuget.org att alla åtta paket har fyra TFM (två för Common/Server).

### Steg 9 — Orientera till eget repo

Det som flyttar: `samples/Orientera*` (fem projekt), `tools/arenabild`, `docs/arenabilder-till-csharp.md`, de Orientera-specifika `issues/*.md`, `Orientera-*.docx`, och de Orientera-specifika raderna i `Directory.Packages.props` (Mapsui, Anthropic, Azure Functions, Aspire, OpenAI, ProjNET, LibTiff, xunit…).

Det nya repot behöver egna:

- `global.json` (samma SDK-pin), `Directory.Build.props` med `SpineMauiTargetFrameworks` (egenskapen är repo-lokal och Orientera använder den), nullable, `LangVersion preview`.
- `Directory.Packages.props` med sina paket plus `Plugin.Maui.Spine*` på samma version.
- `.gitignore` med `google-services.json`, `appsettings.local.json`.
- Ändringar i `Orientera.csproj`: sex `ProjectReference` → fem `PackageReference` (HeroCollectionView stryks, se §1), ta bort de tre `<Import>`-raderna (buildTransitive gör jobbet), behåll `SpineWidgetsExtensionName`, `SpinePushNotificationsEnabled`-villkoret, `SupportedOSPlatformVersion=23` (Firebase) och `CodesignEntitlements`.
- `Orientera.Backend.csproj`: två `ProjectReference` → `PackageReference` på `.Common` och `.Server`.

Arbetsflödet mellan repona efteråt: en Spine-ändring Orientera behöver → PR i Spine → tagg (prerelease duger) → bump i Orienteras `Directory.Packages.props`. För snabb lokal iteration räcker `dotnet pack` i Spine och en `nuget.config` i Orientera som lägger `../Maui.Spine/artifacts/packages` som källa. Haken är att NuGet cachar `0.0.0-local` i `~/.nuget/packages` och inte packar upp en ny nupkg med samma version, så varje ompackning följs av `rm -rf ~/.nuget/packages/plugin.maui.spine*` i Orientera. Det är samma pris som med vilken fast lokal version som helst; MinVer (§4) tar bort det genom att ge varje commit sin egen version.

---

## 6. Beslut att ta

**A. Ikonerna i `Plugin.Maui.Spine.Svg`.** Paketet bäddar in 164 SVG:er (672 KB) — Dishwasher, LawnMower, Weather1…50 — varav samplarna använder ett dussin och Spine självt ingen. Varje konsuments app bär dem. Rekommendation: flytta hela ikonuppsättningen till `MauiSpineSampleApp` innan första releasen, så att `.Svg` blir enbart pipelinen. `ResourceNameCache` scannar redan konsumentens assemblies, så samplen fungerar oförändrad med ikonerna hos sig. Kostnad: en `EmbeddedResource`-glob i samplen och en `None Remove`-lista mindre i `.Svg.csproj`. Görs det senare är det en brytande ändring för den som råkat bygga på dem.

**B. PushNotifications-paketets beroende på kärnan.** Beskrivet i §2. Rekommendation: lämna som det är i `0.1.0`. Om det ska lossas är den naturliga platsen för entitlements-steget `Plugin.Maui.Spine.Common` (båda konsumenterna beror redan på det; targets-filen är villkorad på iOS-TFM och stör inte Server), men det är en ändring i ett känsligt steg och vinner inget för Orientera.

**C. Flytande Windows-versioner.** `WinUIEx 2.9.*` och `Microsoft.WindowsAppSDK 1.8.*` löses till exakta versioner vid pack, så nuspec:en är alltid deterministisk för en given release men kan glida mellan releaser. Rekommendation: pinna exakt före första releasen; flyt var ett svar på en tidigare NU1011-situation som inte längre finns i ett paketerat läge.

**D. AnimatedLabel.** Publiceras eller inte? Rekommendation: ja, den kostar ingenting extra i pipelinen och har en wikisida. Om den inte ska underhållas: `IsPackable=false` i dess csproj, en rad.

**E. Windows-TFM i CI utan Windows-maskin lokalt.** Windows-TFM:et byggs inte på macOS, så första CI-körningen på `windows-latest` är också första gången Windows-koden i kärnan (WinUIEx, tray, fönsterhantering) verifieras i en pipeline; räkna med en fixrunda. Alternativet — släppa `0.1.0` utan Windows-TFM genom att inte lägga till det i `SpineMauiTargetFrameworks` på runnern — är möjligt men gör paketet oärligt mot README.

---

## 7. Vad som inte bryts ut eller flyttas

Frågan är ställd för varje projekt. Svaret är nej överallt utom för ikonerna (beslut A) och det som redan hör till Orientera (steg 9), av dessa skäl:

- **Widget-gränssnitten i `Common`.** `IWidgetService`, `ILiveActivityService`, `IWidgetProvider` och `SpineWidgetsOptions` ligger i `Common`, inte i `Widgets`, trots att servern inte använder dem. Poängen är att en provider kan skrivas och testas i ett projekt utan MAUI — Orientera.Tests kompilerar appens tjänster utan MAUI redan i dag. Att dela `Common` i `Widgets.Abstractions` och `PushNotifications.Abstractions` ger servern två beroenden i stället för ett och ingen konsument något nytt.
- **Azure Table-lagret i `Server`.** `AzureTablePushInstallationStore` (260 rader) drar in `Azure.Data.Tables` i varje serverkonsument, och storage-providers brukar vara egna paket. Men den enda konsumenten använder Azure, `InMemoryPushInstallationStore` finns redan för den som inte gör det, och beroendet är harmlöst i ett backend-projekt. Ett `Plugin.Maui.Spine.Server.AzureTables` kan brytas ut den dag en andra lagring skrivs; i 0.x är flytten inte dyrare då.
- **Lokala notiser och remote push i `PushNotifications`.** Firebase-paketen ligger i Android-TFM:et villkorslöst, så en app som bara vill ha lokala notiser bär Firebase och minSdk 23. Knappen `SpinePushNotificationsRemote=false` stänger av allt remote i bygget, men binärerna följer med. En delning i ett lokalt och ett remote-paket är tänkbar senare; för Orientera, som behöver båda, finns inget att vinna nu.
- **Windows-koden i kärnan.** Tray, fönsterposition, single-instance och titelraden drar in WinUIEx och WindowsAppSDK — men bara i Windows-TFM:et, så en mobilkonsument betalar inget. Ett eget paket skulle bara flytta villkoret.
- **`ViewModelBase` och CommunityToolkit.Mvvm.** Kärnan tvingar Mvvm-toolkitet på alla, men Spines egna vymodeller (regioner, sheets) bygger på det, så det är ett riktigt beroende och inte ett tillval.
- **Kärnans beroende på `Svg`.** `ResourceNameCache` är en obligatorisk tjänst i tab-hosten; att göra den valfri är en ombyggnad av tab-hosten för ett paket ingen bett om.

## 8. Leveranslista

Filer som skapas:

- [ ] `Spine.Packages.slnf` — lösningsfilter över `src/` och `tests/`
- [ ] `src/Directory.Build.props` — paketmetadata (steg 1)
- [ ] `samples/Directory.Build.props`, `tests/Directory.Build.props` — `IsPackable=false`
- [ ] `src/<åtta projekt>/README.md`
- [ ] `assets/icon.png`
- [ ] `.github/workflows/ci.yml`
- [ ] `.github/workflows/release.yml`
- [ ] `artifacts/` i `.gitignore`

Filer som ändras:

- [ ] Namnbytet Push → PushNotifications enligt steg 2: projekt, 28 källfiler, gränssnitt och `SpinePush*`-klasser, MSBuild-knoppar, Widgets-targets rad 62, targets-fil, skript, Swift-extension, serverns API, wiki, samplar, `Spine.slnx`, Orientera
- [ ] `src/Plugin.Maui.Spine/build/Plugin.Maui.Spine.Entitlements.targets` → `Plugin.Maui.Spine.targets` (+ tre `<Import>` i samplarna)
- [ ] `src/Plugin.Maui.Spine.Widgets/build/Plugin.Maui.Spine.Widgets.targets` — `bash` i `Exec`
- [ ] `src/Plugin.Maui.Spine.PushNotifications/build/Plugin.Maui.Spine.PushNotifications.targets` — `bash` i `Exec`
- [ ] `.gitattributes` — `eol=lf` för `.sh` och `.swift`
- [ ] Åtta `.csproj` — `Description`, `PackageTags`, README-item; HeroCollectionView: död `Compile Remove`
- [ ] `Directory.Packages.props` — pinna WinUIEx/WindowsAppSDK (beslut C)
- [ ] `src/Plugin.Maui.Spine.Svg/` och `samples/MauiSpineSampleApp/` — ikonflytten (beslut A)
- [ ] `LICENSE.txt` — namn och år
- [ ] Ta bort `build_output.txt` och `MAC_TRAY_DEBUG.md`; uppdatera eller ta bort `docs/pages-guide.md` och `.github/copilot-instructions.md`
- [ ] `README.md` — pakettabell, plattformstabell
- [ ] `docs/wiki/*.md` — `dotnet add package`-rad överst på varje paketsida

Utanför repot:

- [ ] nuget.org-konto och API-nyckel → secret `NUGET_API_KEY`
- [ ] Scratch-konsument som bygger mot lokala paket för simulatorn (steg 8.2)
- [ ] Tagg `v0.1.0`
- [ ] Nytt repo för Orientera (steg 9)
