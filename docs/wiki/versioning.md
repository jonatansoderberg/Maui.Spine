# Versioning

How an app built on Spine is versioned, and how the Spine packages are. The recommendation is one scheme for both, set by the build so nobody types a version:

```
major.yyMMdd.revision (build id)
0.261031.412 (437245)
```

| Part | What it is | Who sets it |
|---|---|---|
| `major` | `VersionMajor`, the one part bumped by hand. `0` until the first real release; from `1` it says the app or the API changed so much that the number should say so. | You, in `Directory.Build.props` |
| `yyMMdd` | The day of the build (local time for an app, UTC for a package release). The minor part, so the version always increases without anyone choosing a number. | The build |
| `revision` | The commit count of `HEAD`, `git rev-list --count HEAD`. It names the commit a build came from: on `main` the count is unique and only grows. | The build, from git |
| `build id` | Minutes since 1 January 2026 UTC. New for every build, so two builds of the same commit on the same day still differ, and it gives the build time back. | The build |

On the platforms:

| Property | Value | Becomes |
|---|---|---|
| `ApplicationDisplayVersion` | `0.261031.412` | `CFBundleShortVersionString`, Android's `versionName` |
| `ApplicationVersion` | `437245` | `CFBundleVersion`, Android's `versionCode` |
| `Version`, `InformationalVersion` | `0.261031.412` | The assembly's product version (the SDK adds `+<commit>`) |
| `AssemblyVersion` | `0.0.0.0` | Kept at `major.0.0.0`: an assembly version part stops at 65535 (CS7034), and a stable one never breaks binding |
| `FileVersion` | `0.26.1031.412` | The date split in two so every part stays below 65535 |

Why this scheme:

- **No version is ever typed.** The date and the commit count increase by themselves, so the stores' rule that a new upload has a higher version than the last always holds, and a version can't be forgotten or reused.
- **A version says when and what.** `0.261031.412` is a build from 31 October 2026 of commit 412; `git rev-list --reverse HEAD | sed -n 412p` finds it.
- **The build id is what the stores count.** Android's `versionCode` and the App Store's `CFBundleVersion` must grow with every upload, including two uploads of the same version; minutes since 2026 do, and stay far below Android's limit (2 100 000 000) for thousands of years.
- **One scheme for apps and packages**, so a Spine package version and an app version read the same way.

## Setting it up in an app

Copy [`Version.props`](../../Version.props) from the root of this repository unchanged to the root of the app's repository, next to `Directory.Build.props`, and import it at the end of that file:

```xml
	<!-- The version: major.yyMMdd.revision, with the build id (minutes since 2026-01-01 UTC) as the build number.
	     Version.props has the parts; Maui.Spine's docs/wiki/versioning.md the reasoning. Bump the major here. -->
	<PropertyGroup>
		<VersionMajor>0</VersionMajor>
	</PropertyGroup>
	<Import Project="$(MSBuildThisFileDirectory)Version.props" />

</Project>
```

Then take `ApplicationDisplayVersion`, `ApplicationVersion`, `AssemblyVersion`, `FileVersion`, `InformationalVersion` and any build-number property out of the app's csproj. A value the project still sets wins over `Version.props`, which is how one project can opt out, and also how an old line left behind quietly keeps the old version.

`Version.props` sets everything it can while MSBuild evaluates the project. The revision needs git, which evaluation cannot run, so its `SpineVersionFromGit` target runs `git rev-list --count HEAD` once per project and puts the revision in before Android (`_GetAndroidPackageName`), iOS and Mac Catalyst (`_CompileAppManifest`), the assembly attributes and a pack read the versions. Without git (a source archive, a machine without git) the revision is `0` and the build still works.

Spine's widget and notification extensions carry the same version as the app, which App Store Connect expects. A universal Mac Catalyst build (Release, arm64 and x64) writes the app's `Info.plist` in its outer build and builds the extensions once per architecture, later, so the outer build hands its date and build id to them in `obj/spine-version-<framework>.txt`.

The canonical copy is the one in this repository; change it here first and copy it to the apps. Spine's own samples import it from `samples/Directory.Build.props`.

### Pinning a part

Every part can be given on the command line, which a release pipeline or a rebuild of an old version needs:

```bash
dotnet publish src/MyApp -f net10.0-ios -c Release -p:BuildId=437245
dotnet build src/MyApp -p:VersionRevision=412 -p:VersionDate=261031
```

### Bumping the major

Change `VersionMajor` in `Directory.Build.props`. Nothing else: the date and the revision go on as before, so `0.270315.640` is followed by `1.270316.641`.

### Builds in CI

The revision is the commit count, so a CI checkout needs the whole history; a shallow clone counts 1. With `actions/checkout`:

```yaml
- uses: actions/checkout@v4
  with:
    fetch-depth: 0
```

## Showing it in the app

The settings page's Version row shows the version with the build id, and the day of the build as its detail ([Settings pages](settings-pages.md#version-and-build-number)):

```csharp
public string Version => $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

// Abbreviated months end in a dot in some languages (Swedish "okt."), left out.
public string BuildDate => int.TryParse(AppInfo.Current.BuildString, out var minutes)
    ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes).ToLocalTime().ToString("d MMM yyyy", culture).Replace(".", "")
    : "";
```

The day (and, where there is room, the time) comes back from the build id, so nothing else is stamped into the build. Feedback mails and logs should carry the same string, so a report names its build.

## The Spine packages

The packages use the same scheme, `VersionMajor.yyMMdd.revision`, without a build id (nuget.org has no build number):

- **A release is started by hand** and gets its version from the commit: Actions → Release → Run workflow on `master`, or `gh workflow run release.yml`. The workflow computes `0.<today, UTC>.<commit count>`, packs, publishes and tags the commit `v<version>` itself. See [Releasing](releasing.md).
- `VersionMajor` is in `src/Directory.Build.props`.
- `src/Directory.Build.targets` keeps the assembly version at `major.0.0.0` and splits the date in the file version (`0.26.1031.812`) for any version of that shape.
- A local pack is still `0.0.0-local`, so it can never be mistaken for a published package. A package to try in an app before a release is packed with a prerelease of the next version: `-p:Version=0.261031.812-dev.1`.

A version in the new scheme is higher than every `0.1.x`, so apps move from `0.1.37` to `0.261031.812` with an ordinary update.

## Gotchas

| Symptom | Cause |
|---|---|
| The revision is `1` on CI | A shallow checkout; fetch the whole history. |
| The revision is `0` | git was not found or the folder is not a repository. The build goes on. |
| An app still shows its old version | A version property left in the csproj, which wins over `Version.props`. |
| CS7034: the version is in an incorrect format | A project derives `AssemblyVersion` from a version whose date part is above 65535; let `Version.props` (apps) or `src/Directory.Build.targets` (packages) set it. |
| App Store Connect refuses the upload: version must be higher | The app was already released with a higher major, `1.0`, and now starts at `0`. Bump `VersionMajor` to that major first. |
| Windows: the app shows an old version and build time | MAUI writes the versions into `obj/.../resizetizer/m/Package.appxmanifest`, where `AppInfo` reads them on Windows, and rewrote it only when its input files changed. `Version.props` removes MAUI's stamp when the version differs from the last build's; a copy older than 2026-11-13 lacks that, so copy it again. On Windows `AppInfo.VersionString` is that manifest version, with the build id as a fourth part (`0.261031.412.437245`). |
| Windows MSIX packaging | An MSIX version is four parts below 65535 and does not take `0.261031.412`. The apps here are unpackaged (`WindowsPackageType` `None`); a packaged Windows app would set its MSIX version from `FileVersion`. |
| The widget or notification extension shows `1.0 (1)` | The app sets no `ApplicationDisplayVersion` / `ApplicationVersion`. `Plugin.Maui.Spine.Widgets` and `.PushNotifications` write the app's into their extensions' `Info.plist` after the extension is built, so a new build id costs no Swift build. |
