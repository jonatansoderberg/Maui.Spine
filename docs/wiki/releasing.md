# Releasing

Releases are built and published by GitHub Actions. A release is a tag; nothing is packed or pushed by hand.

## Making a release

```bash
git checkout master && git pull
git tag v0.1.0
git push origin v0.1.0
```

The tag name is the version with a `v` in front. A prerelease is a tag with a suffix, `v0.2.0-preview.1`, and is published to nuget.org as a prerelease and marked as one on GitHub.

The `Release` workflow (`.github/workflows/release.yml`) then:

1. Builds every package project and the tests on `windows-latest` with the SDK and workload set pinned in `global.json`.
2. Runs the tests.
3. Packs with `-p:Version=<tag without v>` into `artifacts/packages/`, symbols as `.snupkg`.
4. Prints the `lib/`, `build/`, `buildTransitive/` and `native/` entries of every package, so the log shows that each one carries all four target frameworks and its build assets.
5. Pushes every `.nupkg` (and its symbols) to nuget.org with `--skip-duplicate`, so a rerun does not fail on packages already published.
6. Creates a GitHub release for the tag with auto-generated notes from the merged pull requests and the packages attached.

The `CI` workflow runs the same build, test and pack on every pull request and on pushes to `master`, and uploads the packages as a workflow artifact without publishing them.

## Why a Windows runner

It is the one runner that produces every target framework in one build. `net10.0-windows` needs Windows, while the iOS and Mac Catalyst *class libraries* compile on Windows without a paired Mac; only app bundling needs one. A pack on macOS gives a package without the Windows framework, which is right for a local check and wrong for a release.

## Secrets and accounts

| What | Where |
|---|---|
| `NUGET_API_KEY` | Repository secret. An API key from nuget.org scoped to *Push new packages and package versions* with the glob `Plugin.Maui.Spine*`. Keys expire after at most a year; the workflow fails with a 403 when it has. |
| GitHub release | Created with the workflow's own `GITHUB_TOKEN`; the workflow asks for `contents: write`. |

nuget.org's Trusted Publishing (OpenID Connect from GitHub Actions, no stored key) can replace the API key: configure the repository and workflow on nuget.org and swap the push step for the `NuGet/login` action.

## Versioning rules

- One version for all packages, taken from the tag. `src/Directory.Build.props` sets `0.0.0-local` for builds without one, so a local package can never be mistaken for a published one.
- `0.x` while the API is moving; a minor version may break. From `1.0`, breaking changes bump the major version.
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

## Troubleshooting

| Symptom | Cause |
|---|---|
| Release workflow fails at `dotnet nuget push` with 403 | The API key expired or does not cover the package id. |
| A package on nuget.org lacks `net10.0-windows` | It was packed on a Mac. Only the workflow publishes. |
| The consumer's iOS build says `_SpineWriteEntitlements` does not exist | The app references `Plugin.Maui.Spine.Widgets` or `.PushNotifications` through a `ProjectReference` without importing `Plugin.Maui.Spine.Common.targets`; a `PackageReference` imports it. |
| `Permission denied` on `spine-widgets-build.sh` | A targets file that runs the script directly instead of through `bash`. |
