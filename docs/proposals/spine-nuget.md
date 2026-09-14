# Spine as NuGet packages — package map and implementation plan (rev 3)

**Status:** Implemented. The package map is in [docs/wiki/packages.md](../wiki/packages.md) and the release procedure in [docs/wiki/releasing.md](../wiki/releasing.md). Kept as design history.
**Question:** Orientera is moving to its own repository. Which packages should Spine publish, how should they be named and cut so that the framework can be used modularly, and how do we build and publish releases straight from GitHub?
**Answer:** Nine packages under the name the repository already uses, `Plugin.Maui.Spine.*`, one per project under `src/`, with one shared version and a tag-driven GitHub Actions release to nuget.org. Two projects changed shape on the way: the push project was renamed to say what it does, and the built-in icons became a package of their own.

**Rev 2:** `Plugin.Maui.Spine.Push` renamed to `Plugin.Maui.Spine.PushNotifications`.
**Rev 3:** decisions taken (§6): the icons are `Plugin.Maui.Spine.Svg.Icons`, the entitlements step lives in `Plugin.Maui.Spine.Common`, the Windows packages are pinned, AnimatedLabel is published, and the pipelines run.

---

## 1. Starting point

The repository had eight libraries under `src/`, none with package metadata, and no workflows, tags or version numbers. No `Plugin.Maui.Spine*` id was taken on nuget.org (checked 2026-09-11). Orientera consumed the libraries through `ProjectReference` and imported three `.targets` files explicitly.

What Orientera uses:

| Project | Uses |
|---|---|
| `samples/Orientera` (the app) | `Plugin.Maui.Spine`, `.Svg`, `.Widgets`, `.PushNotifications`, and `.Common` transitively. `.Controls.HeroCollectionView` was referenced but not used — only a global xmlns pointed at it; the reference goes at the move. |
| `samples/Orientera.Backend` (Functions) | `Plugin.Maui.Spine.Server`, `.Common` |
| `Orientera.Domain`, `Orientera.Tests`, `Orientera.AppHost` | nothing from Spine |

---

## 2. The package map

The packages follow the projects one to one, and id, assembly name and root namespace are the same thing. Two alternatives were rejected:

- **The prefix `Spine.Maui.*`** instead of `Plugin.Maui.Spine.*` — a namespace change across the repository and Orientera for nothing in return.
- **A grouping segment for the optional packages**, such as `Plugin.Maui.Spine.Extensions.Widgets`. `Plugin.Maui.Spine.Extensions` is already the core's namespace (`UseSpine`, glass, buttons); Svg is not an extension but something the core depends on; and a segment earns its place when it gathers many small things of one kind — which `Controls.` does, while Widgets and PushNotifications are two large subsystems with their own build pipelines, native code and (for push) a server half. They are peers of the core. The ecosystem does the same: `CommunityToolkit.Maui.MediaElement`, `Microsoft.Maui.Controls.Maps`. The grouping is done in the README's package table instead: **Core**, **Outside the window**, **Controls**, **Server**.

Two renames were made instead:

- **`Plugin.Maui.Spine.Push` became `Plugin.Maui.Spine.PushNotifications`.** The package is as much local notifications (`ILocalNotificationService` with Android and Apple implementations, the notification store, action receivers, the permission flow) as remote push. The rule was that every name using *Push* as the word for the subsystem says *PushNotifications* afterwards: package, namespace, `UseSpinePushNotifications`, `SpinePushNotificationsOptions`, `IPushNotificationService`, `IPushNotificationHandler`, the MSBuild properties `SpinePushNotifications*`, the script, the appex name, the server's `AddSpinePushNotifications` and `MapSpinePushNotifications`, the wiki pages and the sample. Types that describe one push thing (`PushMessage`, `PushInstallation`, `PushRegistrationClient`) and the Azure table prefix `"SpinePush"` (stored data) stayed. The server package is still `Plugin.Maui.Spine.Server`: it does nothing but push.
- **The 164 icons left `Plugin.Maui.Spine.Svg` for `Plugin.Maui.Spine.Svg.Icons`.** Spine itself used none of them, and every consumer carried 672 KB of rooms, appliances and weather symbols. The icons package is `net10.0` and resource-only; `ResourceNameCache` loads it by assembly name at startup, so referencing it is all a consumer does — the same as before, one package further away. Its targets root the assembly for trimmed builds.

The final map, dependencies and target frameworks are in [packages.md](../wiki/packages.md).

Three facts about the dependency graph:

- **PushNotifications does not depend on the core.** It used to, only because its targets ran `DependsOnTargets="_SpineWriteEntitlements"` from the core's `build/`. That step now lives in `Plugin.Maui.Spine.Common`, which both Widgets and PushNotifications already depend on, so an app that wants notifications without Spine navigation no longer pulls in the core, Svg, SkiaSharp and Svg.Skia.
- **Widgets depends on the core for real** — `WidgetRegistry` reads `SpineOptions` to find `[Widget]` providers in the assemblies `UseSpine` was given.
- **The core depends on Svg for real** — `ResourceNameCache` is injected into the tab host, and the tray icons on Windows and Mac Catalyst go through `ISvgIconService`.

---

## 3. What did not work from a package

Three things worked with a `ProjectReference` and broke when the same files came from `~/.nuget/packages`. All three are fixed.

**A. The core's targets file was never imported.** NuGet imports only `build/<PackageId>.targets`. The file was called `Plugin.Maui.Spine.Entitlements.targets`, so `_SpineWriteEntitlements` would not have existed in a consumer's build and both Widgets and PushNotifications would have stopped on an unknown target. The file is now `Plugin.Maui.Spine.Common/build/Plugin.Maui.Spine.Common.targets`.

**B. The scripts ran without an execute bit.** The targets executed `"$(_SpineWidgetsScript)" --out …` directly. A `.nupkg` is a zip without Unix permissions and NuGet sets none on extraction, so from a package the script is 0644 and `Exec` gets *Permission denied*. The targets now run `bash "$(script)" …`.

**C. Line endings.** `.gitattributes` had `* text=auto`; on a Windows runner git has `core.autocrlf=true`, so `.sh` and `.swift` files would be checked out with CRLF, packed that way, and stop `bash` on the consumer's Mac. `.gitattributes` now forces LF for both.

The rest of the `build/` layout held: scripts and `native/` are located with `MSBuildThisFileDirectory` and `..\native\ios`, the same relative position in the package (`build/`, `buildTransitive/` and `native/` are siblings) as in the source tree. The Android task in the Widgets targets is a `RoslynCodeTaskFactory` task inside the targets file and needs no tools assembly. The native steps are conditioned on `IsOSPlatform('OSX')` and inner iOS builds, so a Windows consumer is untouched.

---

## 4. Versioning and release flow

**One version for all packages.** They are released in lockstep from one repository; per-package versioning buys nothing until the framework has external consumers with different upgrade cadences. A version is `Major.Minor.Patch` with an optional `-preview.N`.

**The tag is the version.** A release is `git tag v0.1.0 && git push origin v0.1.0`. The workflow reads the version from the tag and passes it as `-p:Version=`. Locally without a tag everything builds as `0.0.0-local` from `src/Directory.Build.props`, so a local package can never be mistaken for a published one. (MinVer would give the same plus automatic prerelease numbers per commit; it is one more moving part and is not needed until a continuous prerelease feed is wanted.)

**Prereleases go the same way.** `v0.2.0-preview.1` is published to nuget.org as a prerelease. GitHub Packages as a NuGet feed was rejected: it requires a PAT even for public packages, which is exactly the wrong friction in a MAUI app built on several machines.

**Starting version:** `0.1.0`. The API is still moving, and 0.x says so.

**Runner: `windows-latest`.** It is the one runner that can produce all four target frameworks in one build: `net10.0-windows10.0.19041.0` requires Windows, and iOS and Mac Catalyst *libraries* compile on Windows without a paired Mac. `Directory.Build.props` adds the Windows framework only on a Windows host, so a build on macOS gives a package without Windows support — right locally, wrong in a release.

**Publishing:** nuget.org's Trusted Publishing — the `NuGet/login` action trades the job's OIDC token for a short-lived key under a policy registered for this repository and `release.yml`. No stored secret; nuget.org itself discourages API keys for automated publishing.

---

## 5. What was done

1. **Rename** — Push became PushNotifications (PR #257).
2. **Package metadata** — `src/Directory.Build.props` with `IsPackable`, version fallback, authors, licence, repository, README, icon, tags, symbols, Source Link and the output folder; `samples/` and `tests/` opt out with their own `Directory.Build.props`. Every project has a `Description`, package tags and a `README.md` that is packed.
3. **Build fixes** — §3 A, B and C.
4. **Structure** — `Plugin.Maui.Spine.Svg.Icons` split out; the entitlements step moved to Common; PushNotifications dropped its core reference (and gained the `AsyncAwaitBestPractices` reference it had been getting through it).
5. **Pinned** — `WinUIEx 2.9.3` and `Microsoft.WindowsAppSDK 1.8.260804001`, which is what the floating `2.9.*` and `1.8.*` resolved to; central package management no longer needs floating versions.
6. **Hygiene** — `build_output.txt` (9 MB of build output), `MAC_TRAY_DEBUG.md` and the stale `docs/pages-guide.md` removed; `LICENSE.txt` filled in; `.github/copilot-instructions.md` and the README's platform table brought up to date.
7. **Workflows** — `ci.yml` (pull requests and `master`: build, test, pack, upload) and `release.yml` (tags: the same, then push to nuget.org and a GitHub release), both on `windows-latest` over `Spine.Packages.slnf`.
8. **Documentation** — [packages.md](../wiki/packages.md), [releasing.md](../wiki/releasing.md), install lines on every package page, the icon set in [svg.md](../wiki/svg.md), and a package table in the README.

Outside the repository: a trusted publisher policy on nuget.org for `jonatansoderberg/Maui.Spine` and `release.yml`, then the tag `v0.1.0`.

---

## 6. Decisions

**A. The icons in `Plugin.Maui.Spine.Svg`.** *Decided:* a package of their own, `Plugin.Maui.Spine.Svg.Icons`, usable as directly as before.

**B. PushNotifications' dependency on the core.** *Decided:* the entitlements step moved to `Plugin.Maui.Spine.Common` and the dependency is gone.

**C. Floating Windows versions.** *Decided:* pinned to the versions they resolved to.

**D. AnimatedLabel.** *Decided:* published.

**E. The Windows framework without a Windows machine locally.** *Decided:* the pipeline runs on `windows-latest`; its first run is also the first time the Windows code is verified in a pipeline.

---

## 7. What is not broken out or moved

- **The widget interfaces in `Common`.** `IWidgetService`, `ILiveActivityService`, `IWidgetProvider` and `SpineWidgetsOptions` live in `Common`, not in `Widgets`, although the server does not use them. The point is that a provider can be written and tested in a project without MAUI — Orientera.Tests compiles the app's services without MAUI already. Splitting `Common` into `Widgets.Abstractions` and `PushNotifications.Abstractions` gives the server two dependencies instead of one and no consumer anything new.
- **The Azure Table store in `Server`.** `AzureTablePushInstallationStore` (260 lines) pulls `Azure.Data.Tables` into every server consumer, and storage providers are usually packages of their own. But the only consumer uses Azure, `InMemoryPushInstallationStore` exists for those who do not, and the dependency is harmless in a backend project. A `Plugin.Maui.Spine.Server.AzureTables` can be split out the day a second store is written; in 0.x the move costs no more then.
- **Local notifications and remote push in `PushNotifications`.** The Firebase packages are in the Android framework unconditionally, so an app that wants only local notifications carries Firebase and minSdk 23. `SpinePushNotificationsRemote=false` switches everything remote off in the build, but the binaries come along. A split into a local and a remote package is conceivable later; for Orientera, which needs both, there is nothing to gain now.
- **The Windows code in the core.** Tray, window position, single instance and the title bar pull in WinUIEx and WindowsAppSDK — but only in the Windows framework, so a mobile consumer pays nothing. A separate package would only move the condition.
- **`ViewModelBase` and CommunityToolkit.Mvvm.** The core makes the toolkit a dependency for everyone, but Spine's own view models (regions, sheets) are built on it, so it is a real dependency rather than an option.
- **The core's dependency on `Svg`.** `ResourceNameCache` is a required service in the tab host; making it optional is a rebuild of the tab host for a package nobody has asked for.

---

## 8. Orientera to its own repository

What moves: `samples/Orientera*` (five projects), `tools/arenabild`, `docs/arenabilder-till-csharp.md`, the Orientera-specific `issues/*.md`, `Orientera-*.docx`, and the Orientera-specific lines in `Directory.Packages.props` (Mapsui, Anthropic, Azure Functions, Aspire, OpenAI, ProjNET, LibTiff, xunit).

The new repository needs its own:

- `global.json` (the same SDK pin), `Directory.Build.props` with `SpineMauiTargetFrameworks` (the property is repository-local and Orientera uses it), nullable, `LangVersion preview`.
- `Directory.Packages.props` with its packages plus `Plugin.Maui.Spine*` at one version.
- `.gitignore` with `google-services.json`, `appsettings.local.json`.
- Changes in `Orientera.csproj`: six `ProjectReference` → five `PackageReference` (HeroCollectionView is dropped, see §1), the three `<Import>` lines removed (`buildTransitive` does the job), `SpineWidgetsExtensionName`, the `SpinePushNotificationsEnabled` condition, `SupportedOSPlatformVersion=23` (Firebase) and `CodesignEntitlements` kept.
- `Orientera.Backend.csproj`: two `ProjectReference` → `PackageReference` on `.Common` and `.Server`.

The workflow between the repositories afterwards: a Spine change Orientera needs → PR in Spine → tag (a prerelease will do) → bump in Orientera's `Directory.Packages.props`. For quick local iteration, `dotnet pack` in Spine and a `nuget.config` in Orientera pointing at `../Maui.Spine/artifacts/packages` is enough; see [releasing.md](../wiki/releasing.md) for the cache caveat.
