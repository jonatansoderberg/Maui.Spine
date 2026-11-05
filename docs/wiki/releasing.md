# Releasing

Releases are built and published by GitHub Actions. Nothing is packed or pushed by hand, and no version is typed: the workflow takes it from the commit ([Versioning](versioning.md)).

## Making a release

Run the `Release` workflow on `master`: Actions → Release → Run workflow, or

```bash
gh workflow run release.yml --ref master
```

The version is `VersionMajor.yyMMdd.revision`: the major from `src/Directory.Build.props`, the day of the run in UTC and the commit count of `master`, `0.261031.812`. The workflow tags the commit `v0.261031.812` when it creates the GitHub release.

A pushed tag still releases the version it names, which is how a prerelease is made: a tag with a suffix, `v0.261031.812-preview.1`, is published to nuget.org as a prerelease and marked as one on GitHub. Keep to the scheme in a tag, too, so the versions keep increasing.

The `Release` workflow (`.github/workflows/release.yml`) then:

1. Checks out the whole history (the revision is the commit count) and works out the version.
2. Builds every package project and the tests on `windows-latest` with the SDK and workload set pinned in `global.json`.
3. Runs the tests.
4. Packs with `-p:Version=<version>` into `artifacts/packages/`, symbols as `.snupkg`.
5. Prints the `lib/`, `build/`, `buildTransitive/` and `native/` entries of every package, so the log shows that each one carries all four target frameworks and its build assets.
6. Signs in to nuget.org with Trusted Publishing (the `NuGet/login` action trades the job's OpenID Connect token for a short-lived API key) and pushes every `.nupkg` (and its symbols) with `--skip-duplicate`, so a rerun does not fail on packages already published.
7. Creates the GitHub release and its tag, `v<version>`, with auto-generated notes from the merged pull requests and the packages attached.

The `CI` workflow runs the same build, test and pack on every pull request and on pushes to `master`, and uploads the packages as a workflow artifact without publishing them.

## Why a Windows runner

It is the one runner that produces every target framework in one build. `net10.0-windows` needs Windows, while the iOS and Mac Catalyst *class libraries* compile on Windows without a paired Mac; only app bundling needs one. A pack on macOS gives a package without the Windows framework, which is right for a local check and wrong for a release.

## Accounts and trust

There is no stored key. nuget.org's **Trusted Publishing** ties the nuget.org account to this repository's release workflow: the workflow presents its OpenID Connect token, nuget.org checks it against the registered policy and answers with an API key that lives for the job. The workflow therefore asks for `id-token: write`.

| What | Where |
|---|---|
| Trusted publisher policy | nuget.org → the account's *Trusted Publishing* page. Owner `jonatansoderberg`, repository `Maui.Spine`, workflow file `release.yml`. The nuget.org user the policy belongs to is named in the workflow's `NuGet/login` step (`user: CosmoMedia`). |
| Package ownership | The first push of a package id makes that account its owner; later pushes must come from an owner. |
| GitHub release | Created with the workflow's own `GITHUB_TOKEN`; the workflow asks for `contents: write`. |

An API key from nuget.org still works for a push from the command line, but nuget.org discourages keys for automated publishing and the workflow does not use one.

## Versioning rules

- One version for all packages, `VersionMajor.yyMMdd.revision` from the release run (or the tag). `src/Directory.Build.props` sets `0.0.0-local` for builds without one, so a local package can never be mistaken for a published one.
- `0.x` while the API is moving; any release may break. From `1`, a breaking change bumps `VersionMajor` in `src/Directory.Build.props`.
- `src/Directory.Build.targets` keeps `AssemblyVersion` at `major.0.0.0` and splits the date in `FileVersion` (`0.26.1031.812`): an assembly version part stops at 65535.
- A package to try in an app before a release is a prerelease of the next version, `-p:Version=0.261031.812-dev.1`.
- Release notes are the merged pull request titles since the previous tag. Write PR titles as the line you want in the notes.

## Checking a package locally

```bash
dotnet pack Spine.Packages.slnf -c Release
python3 .github/workflows/list-packages.py artifacts/packages
```

On macOS this gives the Android, iOS and Mac Catalyst frameworks (no Windows), which is enough to check the layout: `build/`, `buildTransitive/` and `native/` next to `lib/`.

To try the packages in an app before a release, point a `nuget.config` in that app at the folder:

```xml
<configuration>
  <packageSources>
    <add key="spine-local" value="../Maui.Spine/artifacts/packages" />
  </packageSources>
</configuration>
```

and reference version `0.0.0-local`. NuGet caches that version in `~/.nuget/packages`, so after every repack clear it:

```bash
rm -rf ~/.nuget/packages/plugin.maui.spine*
```

## What must hold for the packages to work

Three things differ between a `ProjectReference` and a package, and each is covered in the repository:

- NuGet imports only `build/<PackageId>.targets`, so each targets file is named after its package.
- A `.nupkg` carries no Unix permissions, so the targets run the build scripts through `bash` rather than executing them directly.
- `.gitattributes` forces LF on `.sh` and `.swift` files; a CRLF checkout on the Windows runner would otherwise be packed and break `bash` on the consumer's Mac.

Each package's icon is a 256×256 PNG in `assets/icons/`, named in the project's `SpineIcon` property and packed by `src/Directory.Build.targets`; `assets/spine-logo.png` is the project logo for the README.

## Troubleshooting

| Symptom | Cause |
|---|---|
| Release workflow fails at `NuGet login` | No trusted publisher policy on nuget.org matches this repository and workflow file, or the nuget.org user in the step is wrong. |
| Release workflow fails at `dotnet nuget push` with 403 | The package id is owned by another nuget.org account. |
| A package on nuget.org lacks `net10.0-windows` | It was packed on a Mac. Only the workflow publishes. |
| An app cannot restore the new version for a while after the release (`NU1102`, nearest version the previous one) | nuget.org validates and indexes each package on its own: in 0.1.40 most were listed within minutes of the push, `Plugin.Maui.Spine.Widgets` and `.Controls.Rows` about 20 minutes later. Wait until `https://api.nuget.org/v3-flatcontainer/<id>/index.json` lists the version for every package the app uses, then `dotnet restore --no-http-cache`, since NuGet keeps the old version list in its HTTP cache for a while. |
| The consumer's iOS build says `_SpineWriteEntitlements` does not exist | The app references `Plugin.Maui.Spine.Widgets` or `.PushNotifications` through a `ProjectReference` without importing `Plugin.Maui.Spine.Common.targets`; a `PackageReference` imports it. |
| `Permission denied` on `spine-widgets-build.sh` | A targets file that runs the script directly instead of through `bash`. |
