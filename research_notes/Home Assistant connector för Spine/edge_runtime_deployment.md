# Edge runtime for the connector: building, packaging, running and operating a .NET 10 home service on a Raspberry Pi 5, then a NAS (as of October 2026)

> Research method note: this sandbox could not reach learn.microsoft.com, devblogs.microsoft.com, developers.home-assistant.io, raspberrypi.com or aspire.dev. Where possible, the **GitHub source files of those docs sites** were read instead (dotnet/docs, dotnet/AspNetCore.Docs, dotnet/dotnet-docker, dotnet/core, home-assistant/developers.home-assistant, home-assistant/home-assistant.io, raspberrypi/documentation, microsoft/aspire.dev, Azure/azure-sdk-for-net, docker/docs). They are cited by their github.com path, and each one is the source of the matching published page. Items taken only from web-search summaries (not read in full) are marked "(search summary)".

## 1. .NET 10 on the Raspberry Pi 5: support status, footprint, GC settings, startup, Native AOT and trimming

### Takeaway
.NET 10 (LTS, patch 10.0.14 as of 2026-10-25, supported to 2028-11-14) runs on linux-arm64 and officially supports Debian 13, which Raspberry Pi OS "Trixie" is based on. Debian 12 (Bookworm) is now listed as *out of support*. ASP.NET Core Minimal APIs, SignalR client and server, WebSockets, health checks and System.Text.Json source generation all work with Native AOT, with a few SignalR limits (JSON protocol only, no strongly typed `Hub<T>`). EF Core under AOT is still experimental, and MVC, Session and "other authentication" don't support AOT at all. On a 4–16 GB Pi 5, memory isn't the constraint. The defaults (Server GC with DATAS) are fine, and a heap limit is a safety net, not a necessity.

### Cited Findings
**Support status / versions**
- .NET release index (2026-10-25): 10.0 → latest 10.0.14, "active", LTS, EOL 2028-11-14; 11.0 → 11.0.0-rc.3, "go-live", STS; 9.0 and 8.0 both reach EOL **2026-11-10** — [dotnet/core releases-index.json](https://github.com/dotnet/core/blob/main/release-notes/releases-index.json)
- .NET 11 is STS, supported "for two years, from November 10, 2026, to November 9, 2028". RC1 2026-09-08, RC2 2026-10-13, RC3 2026-10-25 — [dotnet/core 11.0 README](https://github.com/dotnet/core/blob/main/release-notes/11.0/README.md)
- .NET 10 supported Linux list: Debian **13** (Arm32, Arm64, x64); Ubuntu 26.04/24.04/22.04; Alpine 3.21–3.24; Azure Linux 3.0, among others. Under "Out of support OS versions", **Debian 12** is listed (end-of-life column shows 2028-06-30, i.e. Debian LTS). "OS versions that are out of support by the OS publisher are not tested or supported by .NET." — [dotnet/core 10.0 supported-os.md](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- Same file: "The QEMU emulator is not supported to run .NET apps. QEMU is used, for example, to emulate Arm64 containers on x64" — [supported-os.md](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- Raspberry Pi OS Trixie (Debian 13) was announced 2 October 2025 and is the default in Raspberry Pi Imager. Bookworm remains as "legacy". Both work on the Pi 5 — [Raspberry Pi news: Trixie](https://www.raspberrypi.com/news/trixie-the-new-version-of-raspberry-pi-os/) (search summary)
- A 16 GB Pi 5 launched in January 2025 at $120. Later list prices went up (one listing cited $305), probably because of the late-2025 memory price rises (unconfirmed) — [linuxiac](https://linuxiac.com/raspberry-pi-5-introduces-16gb-model/), [raspberrypi.com/5](https://raspberrypi.com/5) (search summary)
- Microsoft's SBC guidance: deployment to single-board computers "is identical to that of any other platform", framework-dependent (install via `dotnet-install.sh --channel LTS`, set `DOTNET_ROOT`) or self-contained — [dotnet/docs iot/deployment.md](https://github.com/dotnet/docs/blob/main/docs/iot/deployment.md)

**Footprint and startup (indicative numbers; none measured on a Pi 5 with SignalR)**
- Microsoft's .NET 8 GA post: default template 473 ms startup / 98.27 MB app / 193 MB working set vs Native AOT 115 ms / 21.55 MB / 121 MB; a separate Microsoft table shows 156 ms / 92.6 MB / 96 MB vs AOT 48 ms / 10.0 MB / 41 MB — [Announcing ASP.NET Core in .NET 8](https://devblogs.microsoft.com/dotnet/announcing-asp-net-core-in-dotnet-8) (search summary)
- Thinktecture (independent, .NET 8) on a **Raspberry Pi 4**: Native AOT startup just under 90 ms vs about 530 ms for the JIT build; AOT working set about 17–18 MB on ARM right after Kestrel starts — [Thinktecture: Native AOT with ASP.NET Core](https://www.thinktecture.com/en/?p=20991) (search summary)
- A SignalR + `webapiaot` sample compiles to a 10.9 MB Linux executable (.NET 9) — [AspNetCore.Docs: SignalR trimming and Native AOT](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/release-notes/aspnetcore-9/includes/signalr-trimming-aot.md)

**GC settings**
- Server GC is "the default GC for ASP.NET Core apps". "When multiple containerized apps run on the same machine, Workstation GC might be more performant than Server GC" — [AspNetCore.Docs performance/memory.md](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/performance/memory.md)
- DATAS (dynamic adaptation to application sizes) is "enabled by default starting in .NET 9". Toggle with `DOTNET_GCDynamicAdaptationMode` (0/1), the MSBuild property `GarbageCollectionAdaptationMode`, or runtimeconfig `System.GC.DynamicAdaptationMode` — [dotnet/docs runtime-config/garbage-collector.md](https://github.com/dotnet/docs/blob/main/docs/core/runtime-config/garbage-collector.md)
- DATAS "starts with one heap" and grows or shrinks the heap count as needed. It is "a hybrid between the existing GC modes". In TechEmpower runs it cut working set sharply for a 2–3% loss in maximum RPS — [dotnet/docs garbage-collection/datas.md](https://github.com/dotnet/docs/blob/main/docs/standard/garbage-collection/datas.md)
- `DOTNET_GCHeapHardLimit` (a **hexadecimal** value; runtimeconfig `System.GC.HeapHardLimit` takes decimal) is the maximum commit size for the GC heap plus bookkeeping. In a memory-limited container with no value set, the default is "either 20 MB or the heap hard limit percent of the container limit, whichever is larger". `GCHeapHardLimitPercent` defaults to 75% of the container limit. `DOTNET_GCConserveMemory` takes 0–9 (higher means a smaller heap and more GCs) — [garbage-collector.md](https://github.com/dotnet/docs/blob/main/docs/core/runtime-config/garbage-collector.md)

**Native AOT / trimming compatibility (ASP.NET Core doc dated 06/02/2026)**
- Supported: CORS, gRPC, HealthChecks, HttpLogging, JWT authentication, Localization, OutputCaching, RateLimiting, RequestDecompression, ResponseCaching, ResponseCompression, Rewrite, StaticFiles, **WebSockets**. Partial: **Minimal APIs**, **SignalR**. Not supported: Blazor Server, **MVC**, OData, **"Other Authentication"**, **Session**, Spa — [AspNetCore.Docs fundamentals/native-aot.md](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/fundamentals/native-aot.md)
- `CreateSlimBuilder` leaves out HTTPS endpoints and HTTP/3 by default (add them back with `UseKestrelHttpsConfiguration` / `UseQuic`), and also regex and alpha route constraints. System.Text.Json must use the source generator (`TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default)`), because reflection isn't supported in Native AOT — [native-aot.md](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/fundamentals/native-aot.md)
- SignalR AOT (client **and** server, since .NET 9) limits: only the JSON protocol, with the STJ source generator; on the server, hub parameters of `IAsyncEnumerable<T>`/`ChannelReader<T>` where T is a struct are not supported; **strongly typed hubs (`Hub<T>`) are not supported with PublishAot** (they are with trimming only); async returns must be Task/ValueTask(T) — [SignalR trimming and Native AOT](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/release-notes/aspnetcore-9/includes/signalr-trimming-aot.md)
- EF Core NativeAOT and precompiled queries are "highly experimental … not yet suited for production use". They need C# interceptors and `Microsoft.EntityFrameworkCore.Tasks`, still emit AOT and trim warnings, and dynamic queries aren't supported — [EF Core docs: NativeAOT and precompiled queries](https://github.com/dotnet/EntityFramework.Docs/blob/main/entity-framework/core/performance/nativeaot-and-precompiled-queries.md)
- The Azure Monitor exporter supports AOT for traces, metrics and logs, but "automatic configuration binding from appsettings.json … is not supported in AOT-compiled applications", so configure it in code — [Azure.Monitor.OpenTelemetry.Exporter README](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.Exporter/README.md)

### Inferences
- **Host OS:** use Raspberry Pi OS **Trixie 64-bit** (or Ubuntu 24.04/26.04). Bookworm still runs .NET, but it is outside .NET 10's tested matrix. Inside a container the host distro matters much less, because the image brings its own userland (Ubuntu 24.04 for `aspnet:10.0`).
- **AOT verdict for the connector:** feasible if the code stays within the limits: Minimal APIs, SignalR server hub(s) as untyped `Hub` with `SendAsync("name", …)`, `ClientWebSocket` for HA, STJ source generation for every DTO, and Microsoft.Data.Sqlite or Dapper-style SQL instead of EF Core. The MCP server SDK, Azure Web PubSub/Relay SDKs and other third-party packages must be checked one by one for AOT warnings (not researched here). **Recommendation: start JIT/framework-dependent and keep the code AOT-clean (`<IsAotCompatible>true</IsAotCompatible>`, analyzers on).** On a Pi 5 the gain from AOT (about 0.5 s startup, tens of MB of RAM) is small next to HA's own footprint. Use trimming or AOT later if image size or NAS density matters.
- **GC:** on a Pi shared with HA, keep Server GC + DATAS (the default) and set a container `mem_limit` (e.g. 256–512 MB). The GC then caps itself at 75% of that limit. Switch to Workstation GC (`<ServerGarbageCollection>false</ServerGarbageCollection>`) only if measurements show idle heap growth. `DOTNET_GCHeapHardLimit` is mainly useful for a bare systemd service with no cgroup limit (or use systemd `MemoryMax=`, which the GC sees as a cgroup limit, same as in a container).
- A connector with a few WebSocket connections should sit around tens of MB of working set (JIT: roughly 60–150 MB; AOT: roughly 20–60 MB). These are extrapolations from the figures above, not measurements.

### Gaps
- No primary benchmark for ASP.NET Core + SignalR (server + client) memory/CPU on a Pi 5 with .NET 10. Measure it (`dotnet-counters`, `docker stats`).
- Didn't check whether the MCP C# SDK (ModelContextProtocol.AspNetCore), Microsoft.Azure.SignalR, Azure.Messaging.WebPubSub.Client or Microsoft.Azure.Relay are AOT/trim-clean in their current versions.
- No .NET 11 Pi-specific changes researched. .NET 11 goes GA on 2026-11-10 as STS; staying on .NET 10 LTS is the safe choice for an edge box.

## 2. Container images: aspnet:10.0 arm64, chiseled/distroless/-extra, sizes, SDK container publishing from a Mac, multi-arch in GitHub Actions, GHCR

### Takeaway
All `mcr.microsoft.com/dotnet/aspnet:10.0` tags are multi-arch (amd64 + arm64v8) and Ubuntu-based: `10.0` = Noble 24.04, and Resolute 26.04 tags now exist too. A framework-dependent minimal API is about 92 MB compressed on the full image, 53 MB on `noble-chiseled` and 68 MB on `noble-chiseled-extra` (which adds ICU and tzdata). Native AOT on `runtime-deps:10.0-noble-chiseled` gets down to about 12 MB. The .NET SDK builds OCI images, including multi-arch indexes, without Docker or a Dockerfile, using `dotnet publish /t:PublishContainer` with `ContainerRuntimeIdentifiers="linux-x64;linux-arm64"`, and can push straight to GHCR. That works from an Apple Silicon Mac or a GitHub Actions runner. Native arm64 runners are free for public repos and, since January 2026, standard (2 vCPU) in private repos.

### Cited Findings
- Tag list (2026-10): `10.0.14-noble-arm64v8, 10.0-noble, 10.0, latest`; `10.0-noble-chiseled`, `10.0-noble-chiseled-extra`, `-composite` and `-composite-extra` variants; `10.0-resolute-chiseled(-extra)` (Ubuntu 26.04); `10.0-alpine3.24(-extra)`; `10.0-azurelinux3.0-distroless(-extra)`. All exist for amd64 and arm64v8 — [dotnet-docker README.aspnet.md](https://github.com/dotnet/dotnet-docker/blob/main/README.aspnet.md)
- Chiseled = distroless Ubuntu: "Minimal set of packages … Non-root user by default; No package manager; No shell". Adding packages with Chisel slices "requires the presence of the Chisel manifest, which is currently only available in .NET 10+ images" — [dotnet-docker ubuntu-chiseled.md](https://github.com/dotnet/dotnet-docker/blob/main/documentation/ubuntu-chiseled.md)
- Distroless: no shell, so use the exec form of `ENTRYPOINT`/`CMD`; the non-root UID is available as the `APP_UID` env var — [dotnet-docker distroless.md](https://github.com/dotnet/dotnet-docker/blob/main/documentation/distroless.md)
- Compressed sizes for a minimal web API (November 2025 snapshot): `aspnet:10.0` FDD 92.48 MB; `aspnet:10.0-noble-chiseled` 52.81 MB; `-noble-chiseled-extra` 67.68 MB; `runtime-deps:10.0-noble-chiseled` self-contained+trimmed 21.86 MB (36.82 MB `-extra`); Native AOT on `runtime-deps:10.0-noble-chiseled` 11.60 MB (26.56 MB `-extra`); `aspnet:10.0-alpine` 51.93 MB; AOT on `runtime-deps:10.0-alpine` 10.69 MB. "`extra` images add `icu` and `tzdata`" — [dotnet-docker sample-image-size-report.md](https://github.com/dotnet/dotnet-docker/blob/main/documentation/sample-image-size-report.md)
- SDK container publishing: "The .NET SDK creates container images without Docker". `dotnet publish --os linux --arch x64 /t:PublishContainer` pushes to the local daemon by default (Docker or Podman). `-p ContainerArchiveOutputPath=./images/container-image.tar.gz` writes a tarball without a daemon. `-p ContainerRegistry=ghcr.io` pushes directly — [dotnet/docs containers/sdk-publish.md](https://github.com/dotnet/docs/blob/main/docs/core/containers/sdk-publish.md)
- Base image inference: self-contained → `runtime-deps`; ASP.NET Core → `aspnet`; tag = TFM version. `ContainerFamily` (e.g. `noble-chiseled`, `alpine`) appends a variant. With `InvariantGlobalization=false` the `-extra` variant is used. Multi-arch: "If no single RuntimeIdentifier is specified but multiple RuntimeIdentifiers or ContainerRuntimeIdentifiers are set, the SDK publishes the app for each specified RID and combines the resulting images into an OCI Image Index" (since SDK 8.0.405 / 9.0.102); multi-arch output is always OCI format. `ContainerUser` defaults to the rootless `app` user (by UID) for .NET 8+ on Microsoft images — [dotnet/docs containers/publish-configuration.md](https://github.com/dotnet/docs/blob/main/docs/core/containers/publish-configuration.md)
- GitHub arm64 hosted runners: GA for public repositories in August 2025 (4 vCPU); "arm64 standard runners are now available in private repositories" on 2026-01-29 (2 vCPU, counts against plan minutes; label `ubuntu-24.04-arm`) — [GitHub changelog 2025-08-07](https://github.blog/changelog/2025-08-07-arm64-hosted-runners-for-public-repositories-are-now-generally-available/), [GitHub changelog 2026-01-29](https://github.blog/changelog/2026-01-29-arm64-standard-runners-are-now-available-in-private-repositories) (search summary)
- Home Assistant's own app build moved to "Docker BuildKit, which is natively supported on GitHub Actions runners and has built-in multi-arch support with QEMU emulation if needed" — [HA dev blog 2026-04-02: builder migration](https://github.com/home-assistant/developers.home-assistant/blob/master/blog/2026-04-02-builder-migration.md)

### Inferences
- **Suggested image:** `aspnet:10.0-noble-chiseled-extra`, or `noble-chiseled` with `<InvariantGlobalization>true</InvariantGlobalization>` if the connector never formats dates or numbers per locale. The HA connector does deal with time zones, so `-extra` (tzdata) is the safer default. Later, use AOT + `runtime-deps:10.0-noble-chiseled-extra` (about 27 MB).
- **Build path without a Dockerfile** (works identically on the Mac, since cross-RID publish of IL needs no emulation):
  ```xml
  <ContainerRepository>owner/spine-connector</ContainerRepository>
  <ContainerRegistry>ghcr.io</ContainerRegistry>
  <ContainerFamily>noble-chiseled-extra</ContainerFamily>
  <RuntimeIdentifiers>linux-x64;linux-arm64</RuntimeIdentifiers>
  <ContainerRuntimeIdentifiers>linux-x64;linux-arm64</ContainerRuntimeIdentifiers>
  <ContainerImageTags>1.4.2;latest</ContainerImageTags>
  ```
  `dotnet publish -c Release /t:PublishContainer` then pushes one OCI index to GHCR (authenticate with `docker login ghcr.io` or a `GITHUB_TOKEN` in CI). For **Native AOT**, each RID must be compiled on its own architecture (cross-OS/arch AOT isn't generally supported), so use a matrix of `ubuntu-24.04` + `ubuntu-24.04-arm` runners and merge the manifests (`docker buildx imagetools create`), or use a Dockerfile with buildx on native runners. QEMU-emulated .NET builds are unsupported (see the supported-os note above).
- A Dockerfile is still worth keeping for the HA app path (HA labels, `/data` conventions) and for anything that needs files outside the publish output. Labels can also be set from MSBuild (`<ContainerLabel Include="io.hass.type" Value="app" />`), so one SDK-built image can carry them.
- Health checks in a chiseled image can't `curl`. Either ship a tiny `--healthcheck` mode in the connector binary (exec form `HEALTHCHECK CMD ["/app/Connector","--healthcheck"]`), or rely on the HA Supervisor `watchdog:` URL or an external monitor.

### Gaps
- No measured compressed size of the arm64 variants specifically (the report is for one architecture). Arm64 sizes are typically similar.
- Didn't confirm the exact `ContainerFamily` string accepted for the Resolute (26.04) variant, or whether the SDK now infers `noble-chiseled` automatically for .NET 10 (the doc text found still mentions `jammy-chiseled-aot` for musl/AOT).

## 3. Deployment options compared: Compose on Raspberry Pi OS, systemd service, Home Assistant app (formerly add-on), NAS platforms; can one OCI image serve all?

### Takeaway
Home Assistant renamed **add-ons to "apps" in 2026.2 (February 2026)**. Apps are still Docker images defined by a `config.yaml`. As of Supervisor 2026.04 they are built with plain BuildKit, reference a single multi-arch image name, and support only `aarch64` and `amd64`. One OCI image *can* serve Compose on a Pi, Synology, QNAP, Unraid, TrueNAS and an HA app, if it carries the `io.hass.*` labels, reads its options from `/data/options.json` when present, keeps all state under one volume path, and takes HA connection details from config (or `SUPERVISOR_TOKEN` + `ws://supervisor/core/websocket` inside HA OS). Every NAS platform listed runs standard Docker images. Compose support differs: Synology (Projects) and QNAP (Applications) take compose YAML natively; TrueNAS 25.10 takes compose YAML via "Install via YAML"; Unraid still needs a plugin or an XML template.

### Cited Findings
**HA apps (formerly add-ons)**
- "Add-ons are now called Apps!" in release 2026.2 (published 4 February 2026). Mostly a rename, but URLs/menus changed (Settings → Apps) — [HA 2026.2 release notes](https://www.home-assistant.io/blog/2026/02/04/release-20262/), [frenck.dev: Renaming add-ons to apps](https://frenck.dev/renaming-home-assistant-add-ons-to-apps/) (search summary)
- "Under the hood, apps are container images published to a container registry like GitHub container registry". The example repo is `home-assistant/apps-example` — [developers.home-assistant docs/apps.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps.md)
- `config.yaml` essentials: `arch` (allowed values: `aarch64`, `amd64`); `image: ghcr.io/my-org/my-app` (the generic multi-arch name is preferred; `{arch}` is a fallback) with `version` matching the tag; `ingress`, `ingress_port` (default 8099), `ingress_stream`; `homeassistant_api` (REST proxy `http://supervisor/core/api`); `hassio_api` and `hassio_role`; `map` (`homeassistant_config`, `app_config`, `share`, `ssl`, `backup`, `media`, `data` …; `/data` is always mapped and writable); `watchdog` (an HTTP URL or `tcp://[HOST]:[PORT:x]` health monitor); `backup: hot|cold`, `backup_exclude`, `backup_pre/post`; `init` (default true, meaning Docker's init; set it to false for s6-overlay v3); `timeout` (default 10 s stop timeout); `stage`; `options`/`schema` (user options are delivered as `/data/options.json`). Labels: `io.hass.version`, `io.hass.type="app"`, `io.hass.arch="aarch64|amd64"`; `legacy: true` covers images without hass labels. "Since Supervisor 2026.04.0, the BUILD_FROM argument is no longer provided by default", and `build.yaml` "is no longer used" — [developers.home-assistant docs/apps/configuration.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/configuration.md)
- Communication: Core REST via `http://supervisor/core/api/` with `Authorization: Bearer ${SUPERVISOR_TOKEN}` (needs `homeassistant_api: true`); the WebSocket API proxy is at `ws://supervisor/core/websocket`, with `SUPERVISOR_TOKEN` as the access token. Apps can reach `/core/api`, `/core/websocket`, `/addons/self/*`, `/services*`, `/discovery*`, `/info` without `hassio_api`. Services API offers mqtt and mysql — [docs/apps/communication.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/communication.md)
- Ingress: the server runs on 8099 (or `ingress_port`); "Only connections from 172.30.32.2 must be allowed"; the base path arrives in the `X-Ingress-Path` header; the gateway supports WebSockets; ingress gives +2 on the app security rating — [docs/apps/presentation.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/presentation.md)
- Publishing: pre-built registry images are "the preferred method". Locally built apps add "more wear and tear to users' SD cards". The `build-image` action pushes per-arch images (`ghcr.io/my-org/aarch64-my-app:1.0.0`) and `publish-multi-arch-manifest` pushes the generic `ghcr.io/my-org/my-app:1.0.0` — [docs/apps/publishing.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/publishing.md)
- The legacy `home-assistant/builder` container and action were retired (April 2026). Use composite actions plus BuildKit. Base images are multi-platform manifests. The new actions don't infer `io.hass.*` labels, so add them explicitly — [HA dev blog: builder migration](https://github.com/home-assistant/developers.home-assistant/blob/master/blog/2026-04-02-builder-migration.md)
- Local testing: the VS Code devcontainer `home-assistant/devcontainer` bootstraps Supervisor + HA at `http://localhost:7123/`, and apps in the repo root show up as Local Apps — [docs/apps/testing.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/testing.md)
- HA Container installs "do not have access to apps" — [home-assistant.io _includes/installation/container.md](https://github.com/home-assistant/home-assistant.io/blob/current/source/_includes/installation/container.md)

**systemd**
- `Microsoft.Extensions.Hosting.Systemd` (`UseSystemd()`) sends only `READY=1` and `STOPPING=1` via sd_notify — [dotnet/runtime SystemdLifetime.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Hosting.Systemd/src/SystemdLifetime.cs), [ServiceState.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Hosting.Systemd/src/ServiceState.cs)

**NAS platforms**
- **Synology:** Container Manager 24.0.2-1543 (2025-06-24) requires DSM 7.2.1+ and adds the `docker compose` CLI; Docker daemon 24.0.2. Container Manager 20.10.23 added ARMv8 models DS420j and DS223 — [Synology Container Manager release notes](https://www.synology.com/releaseNote/ContainerManager) (search summary). A third-party guide says the package name changes back toward "Docker" on DSM 7.3 — [xaweho.de](https://xaweho.de/en/wissen/synology-hosting/container-manager-docker) (search summary, unverified)
- **QNAP:** Container Station 3.0+ on QTS 5.1+ uses Compose V2 (`docker compose`, no hyphen) — [QNAP FAQ](https://www.qnap.com/en/how-to/faq/con_show.php?cid=3009); compose "Applications" are created in the Container Station UI — [QNAP: How to use Container Station 3](https://www.qnap.com/en/how-to/tutorial/con_show.php?cid=510) (search summary)
- **Unraid:** "Unraid does not natively support Docker Compose" (official docs); the usual route is the Docker Compose Manager / Compose Manager Plus plugins or Community Applications XML templates. Native compose is rumoured for Unraid 8 (unconfirmed) — [Unraid docs (FR)](https://docs.unraid.net/fr/unraid-os/using-unraid-to/run-docker-containers/overview/), [Compose Manager Plus thread](https://forums.unraid.net/topic/197334-plugin-compose-manager-plus), [StaXX thread](https://forums.unraid.net/topic/200462-staxx-new-docker-compose-management-plugin/) (search summary)
- **TrueNAS (25.10 "Goldeye"):** apps are Docker-based; "Install via YAML" opens an advanced YAML editor for Docker Compose. From 25.10, YAML that only `include:`s an external compose file must also have `services: {}` — [TrueNAS 25.10 docs: Install Custom App screens](https://www.truenas.com/docs/scale/25.10/scaleuireference/apps/installcustomappscreens/), [apps.truenas.com: Installing custom apps](https://apps.truenas.com/managing-apps/installing-custom-apps/), [TechnoTim](https://technotim.com/posts/truenas-docker-pro/), [TrueNAS forum](https://forums.truenas.com/t/25-10-rc1-custom-app-yaml-input-bug/55300) (search summary)

### Inferences
**Comparison (Pi 5 phase)**

| Option | Pros | Cons |
|---|---|---|
| Docker Compose on Raspberry Pi OS (HA elsewhere, or HA Container next to it) | Same artifact as the NAS later; easy rollback by tag; isolation; restart policies; Diun/Komodo work | Docker on the Pi; no Supervisor integration; HA long-lived token needed |
| systemd + framework-dependent/self-contained publish | Lowest overhead, `MemoryMax=`, `WatchdogSec=`, journald logs, no Docker | Different artifact than the NAS; you own updates, rollback, permissions and the runtime install; Trixie-only support for FDD |
| HA app on HA OS (same Pi) | One box; Supervisor starts, updates, backs up (`/data`) and watchdogs it; `SUPERVISOR_TOKEN` gives HA access with no user token; ingress UI inside HA with HA auth | Ties the connector to HA OS; the update UX is HA's; only reachable via ingress/mapped ports; must handle 172.30.32.2 allow-listing and `X-Ingress-Path` base paths; per-app resource limits aren't configurable in config.yaml (none found) |

- **One image for all targets:** yes. Design rules:
  1. Config precedence: env vars → `/data/options.json` (only present under the Supervisor) → appsettings.
  2. All state lives in `/data` (HA's convention, and simple to map on any NAS: `-v /volume1/docker/connector:/data`).
  3. If `SUPERVISOR_TOKEN` is set, default HA URL = `ws://supervisor/core/websocket`; otherwise a user-provided URL plus a long-lived token.
  4. Listen on one HTTP port; under ingress, honor `X-Ingress-Path` (`UsePathBase`) and allow only 172.30.32.2 on the ingress port.
  5. Carry the `io.hass.type/arch/version` labels in every build (they're harmless elsewhere).
  6. Run as non-root but tolerate the HA/NAS UID mapping. Chiseled runs as UID 1654 (`app`); NAS bind mounts often need `user:` overrides or PUID/PGID-style handling, so document a `user:` line.
- **s6-overlay is not needed:** with `init: true` (the default), Docker's tini init is enough for a single .NET process. Only HA's own base images need `init: false`.
- **Distribution:** one GitHub repo can host both the app repository (`repository.yaml` + `connector/config.yaml`) and the GHCR image. NAS users get a `compose.yaml`; Unraid users get a CA XML template pointing at the same image.
- Synology ARM models (DS223/DS124/DS223j) need the arm64 image, the same as the Pi. Low-end x86 Synology/QNAP models need amd64. Hence the multi-arch image.

### Gaps
- Couldn't confirm the DSM 7.3 Container Manager version, its bundled Compose version, or the ARM model list from Synology's own DSM 7.3 pages.
- Didn't check whether TrueNAS 26 (if released) changed the app system again, or the current state of the TrueNAS "Apps catalog" submission process.
- Didn't find whether HA apps can set CPU/memory limits. The config options read had none.

## 4. Updates and rollback: Watchtower in 2026 and alternatives, self-update, HA app update channel, health checks, automatic rollback

### Takeaway
**containrrr/Watchtower is unmaintained: the repo shows "This project is no longer maintained" and was archived in December 2025 (last release 1.7.1).** For a home appliance, notify-then-apply (Diun or WUD notifications, or Komodo/Portainer stacks) is safer than silent auto-updates. Docker itself never restarts an *unhealthy* container and has no automatic rollback. Rollback means pinning to version tags and keeping the previous one. Under HA OS, the Supervisor's app store handles updates. The image tag is driven by `version` in `config.yaml`, and HA backups can snapshot an app before an update.

### Cited Findings
- Watchtower README: "⚠️ This project is no longer maintained — See https://github.com/containrrr/watchtower/discussions/2135" — [containrrr/watchtower README](https://github.com/containrrr/watchtower)
- Archived (read-only) on 17 December 2025; last release 1.7.1, no security patches; forks exist (e.g. openserbia/watchtower, nickfedor/watchtower), but their status wasn't verified — [linuxiac](https://linuxiac.com/docker-update-tool-watchtower-reaches-end-of-maintenance/) (search summary). A search for the nickfedor fork returned nothing usable.
- Diun: "watches container images and tells you when an update is available. It checks registries for new tags or digest changes" — notification only; runs as a binary or Docker image — [crazy-max/diun README](https://github.com/crazy-max/diun)
- What's Up Docker (WUD): "Keep your containers up-to-date across any orchestrator"; supports Docker, Swarm, Kubernetes, Nomad, Docker Compose, with "triggers" (notify or update) — [getwud/wud README](https://github.com/getwud/wud)
- Komodo: "A tool to build and deploy software across many servers"; GPL-3; Stacks/Compose, Sync, Update screens; uses a "periphery" agent per server — [moghtech/komodo README](https://github.com/moghtech/komodo)
- Docker restart policies (`no`, `on-failure[:max-retries]`, `always`, `unless-stopped`) act on container exit, and `always`/`unless-stopped` also bring containers back after a daemon restart (e.g. after a reboot) — [docker/docs: Start containers automatically](https://github.com/docker/docs/blob/main/content/manuals/engine/containers/start-containers-automatically.md)
- Docker marks containers unhealthy but "intentionally does not restart" them. Workarounds are an autoheal sidecar (Docker socket + label) or making the process exit; `--exit-on-unhealthy` (moby #22719) is still not implemented — [docker/compose #4826](https://github.com/docker/compose/issues/4826), [VirtualizationHowto 2026-06](https://www.virtualizationhowto.com/2026/06/i-thought-docker-would-restart-unhealthy-containers-i-was-missing-one-tool/) (search summary)
- HA apps: `version` "needs to match the tag of the image" when `image` is used; `watchdog` is the Supervisor's health URL; `stage: stable|experimental|deprecated` — [docs/apps/configuration.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/configuration.md)

### Inferences
- **Pi/NAS (Compose):** publish semver tags plus a moving `:1` and `:latest`. Users pin `:1` (or exact). Run **Diun** or **WUD** (notify mode) to post to HA (WUD has an MQTT/HA trigger; not verified in detail) or to the Spine app via the connector. Apply with `docker compose pull && up -d`, or with a Komodo/Portainer "redeploy". Rollback = edit the tag back.
- **Self-update from the connector:** possible but risky. The connector would need the Docker socket, which is effectively root on the host. Better: the connector reads GitHub Releases or GHCR tags, shows "update available" in Spine, and leaves applying the update to the host tool or HA.
- **Do-it-yourself rollback:** the connector writes a "boot ok" marker after N minutes of healthy operation. A small host script or Komodo procedure redeploys the previous tag if the new container exits repeatedly (`on-failure:5`) or never gets the marker. Docker has no native equivalent.
- **Database migrations** must be forward-compatible for at least one version (expand/contract), or rolling back the image breaks on a newer schema. Snapshot `/data` before migrating (see section 6).
- **HA app channel:** a stable repo plus a separate "edge/beta" app slug (`stage: experimental`) is the usual pattern. HA's update entity and "backup before update" toggle cover rollback for apps.

### Gaps
- Didn't verify whether WUD's current release still auto-updates Compose stacks safely, or its HA/MQTT trigger details.
- Didn't verify Portainer CE's 2026 licensing or arm64 status.
- Didn't read Watchtower discussion #2135 itself (github.com returned 403 to the fetcher).

## 5. Running HA on the same Pi 5 or separately: HA OS on Pi 5, NVMe boot, resource sharing, SD card wear

### Takeaway
HA's install docs still describe HA OS on a Pi 5 with an SD card (any Pi 4/5 with at least 2 GB RAM). The Raspberry Pi docs support NVMe boot on the Pi 5 through the M.2 HAT+ (PCIe 2.0 x1, about 500 MB/s, 2230/2242) and the EEPROM `BOOT_ORDER` (NVMe = mode 6). For a box that also runs a connector with SQLite, an audit log and telemetry buffers, **boot from NVMe (or a USB SSD), not an SD card**. Running HA OS with the connector as an HA app on one 8 GB Pi 5 is reasonable. If HA already runs on other hardware, run the connector in Compose on Raspberry Pi OS and point it at HA over the LAN.

### Cited Findings
- HA OS on Raspberry Pi: hardware list "Raspberry Pi 5 or Raspberry Pi 4 … (make sure to choose a model with at least 2 GB of RAM)", a microSD card, and Ethernet "highly recommended"; Home Assistant Container on a Pi is the alternative — [home-assistant.io installation/raspberrypi.markdown](https://github.com/home-assistant/home-assistant.io/blob/current/source/installation/raspberrypi.markdown)
- HA OS install notes: "some NVMe SSDs and hard drives use native 4K sectors (4Kn) only. Home Assistant OS cannot boot from these, so avoid them for your boot medium" — [home-assistant.io _includes/installation/operating_system.md](https://github.com/home-assistant/home-assistant.io/blob/current/source/_includes/installation/operating_system.md)
- M.2 HAT+: "Single-lane PCIe 2.0 interface (500 MB/s peak transfer rate)"; HAT+ supports 2230/2242, HAT+ Compact only 2230 (fits the official case; the Active Cooler can't fit under the Compact) — [raspberrypi/documentation m2-hat-plus/about.adoc](https://github.com/raspberrypi/documentation/blob/master/documentation/asciidoc/accessories/m2-hat-plus/about.adoc)
- NVMe boot: check `/dev/nvme0n1`, then `raspi-config` → Advanced Options → Boot Order (include NVMe), or set `BOOT_ORDER` with NVMe boot mode `6` — [raspberrypi/documentation boot-nvme.adoc](https://github.com/raspberrypi/documentation/blob/master/documentation/asciidoc/computers/raspberry-pi/boot-nvme.adoc)
- PCIe Gen 3 is not certified on the Pi 5 and may be unstable; Gen 2 is the default. Community reports of HA OS NVMe boot problems exist (Pimoroni forum, simon42) — [Raspberry Pi news: using M.2 HAT with Pi 5](https://www.raspberrypi.com/news/using-m-2-hat-with-raspberry-pi-5/), [Pimoroni forum](https://forums.pimoroni.com/t/m-2-hat-and-pi-5-worked-last-night-now-wont/26697), [simon42 community](https://community.simon42.com/t/booten-von-nvme/38649) (search summary)
- The HA developer docs note that locally built apps add "wear and tear to users' SD cards/hard drives" — [docs/apps/publishing.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/publishing.md)
- HA Core can need longer than Docker's default 10 s stop timeout "to close its database", and the HA Container docs recommend raising `stop_grace_period` — [home-assistant.io container.md](https://github.com/home-assistant/home-assistant.io/blob/current/source/_includes/installation/container.md)

### Inferences
- **SD card wear:** SD cards have no proper wear levelling or power-loss protection compared with SSDs. HA's recorder (SQLite) plus the connector's SQLite WAL, audit log and the Azure exporter's offline telemetry store all write small amounts constantly, which is the worst case for SD cards. This is general knowledge, not from a primary source read here. Use NVMe (avoid 4Kn drives for HA OS) and a 27 W official PSU.
- **Same Pi:** HA OS plus a .NET connector (about 100 MB) fits easily in 8 GB; 4 GB is fine, 16 GB is unnecessary. Add an `ingress` panel so the connector's local admin UI sits inside HA. Risk: one box is a single point of failure, and HA OS updates and reboots take the connector down too (acceptable at home).
- **Separate:** if HA runs on a Yellow, Green or NUC, or as HA Container, the Pi runs Raspberry Pi OS Trixie with Docker Compose, which is the same model as the later NAS. **Recommendation: build for Compose first (it's the general case), and add the HA app wrapper as a thin `config.yaml` over the same image.**

### Gaps
- No primary HA document read on Pi 5 + NVMe boot under HA OS specifically (supported HA OS version, known issues). Only Raspberry Pi docs and community reports.
- No primary source found on SD card wear numbers.

## 6. Local storage: SQLite vs LiteDB vs files; WAL; backups to Azure Blob; relation to HA backups

### Takeaway
Use **SQLite via Microsoft.Data.Sqlite** (with or without EF Core; EF Core only if staying JIT). Turn on WAL, and keep dashboards and config as JSON documents in tables or plain JSON files if they're small. LiteDB's README still advertises "v5" and targets netstandard, so it looks low-activity (see Gaps). Use SQLite's online backup API (`SqliteConnection.BackupDatabase`) to make a consistent copy and upload it to Azure Blob. Under HA OS, `/data` goes into HA backups automatically (`backup: hot|cold`, `backup_exclude`). HA backups are encrypted by default since 2025.1.

### Cited Findings
- "SQLite doesn't support asynchronous I/O. Async ADO.NET methods will execute synchronously in Microsoft.Data.Sqlite." "Instead, use write-ahead logging." "Write-ahead logging is enabled by default on databases created using Entity Framework Core" — [dotnet/docs sqlite/async.md](https://github.com/dotnet/docs/blob/main/docs/standard/data/sqlite/async.md)
- Busy/locked: Microsoft.Data.Sqlite "will automatically retry until it succeeds or the command timeout is reached". Default `CommandTimeout` is 30 s; `DefaultTimeout` covers implicit commands — [dotnet/docs sqlite/database-errors.md](https://github.com/dotnet/docs/blob/main/docs/standard/data/sqlite/database-errors.md)
- Online backup: `SqliteConnection.BackupDatabase` backs up a live database "as quickly as possible and blocks other connections from writing" — [dotnet/docs sqlite/backup.md](https://github.com/dotnet/docs/blob/main/docs/standard/data/sqlite/backup.md)
- EF Core + Native AOT is experimental ("not yet suited for production use") — [EF Core docs](https://github.com/dotnet/EntityFramework.Docs/blob/main/entity-framework/core/performance/nativeaot-and-precompiled-queries.md)
- LiteDB README: "100% C# code for .NET 4.5 / NETStandard 1.3/2.0 in a single DLL"; "New v5"; MIT; prerelease builds are published from CI — [litedb-org/LiteDB README](https://github.com/litedb-org/LiteDB)
- HA app backups: `backup: hot` (default) or `cold` (Supervisor stops the app first); `backup_pre`/`backup_post` commands; `backup_exclude` globs; `/data` is always mapped — [docs/apps/configuration.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/configuration.md)
- HA 2025.1: automatic backups with mandatory encryption (key from setup or the settings "emergency kit"), locations include network storage and HA Cloud (encrypted only, no key stored) — [HA blog: 3-2-1 Backup](https://www.home-assistant.io/blog/2025/01/03/3-2-1-backup/), [Nabu Casa cloud backups](https://nabucasa.com/config/backups) (search summary)

### Inferences
- **Layout in `/data`:** `connector.db` (SQLite: settings, dashboard documents as JSON columns, audit log, state cache) + `keys/` (Data Protection) + `otel/` (exporter offline storage) + `backups/`. Keep the audit log in its own table with a retention job (e.g. 90 days) so it stays bounded on small disks. A state cache that can be rebuilt from HA on reconnect doesn't need durability (`PRAGMA synchronous=NORMAL` is fine with WAL).
- **Pragmas:** `journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout`, `foreign_keys=ON`, periodic `wal_checkpoint(TRUNCATE)`. One writer, via a single `Channel<T>`-based write queue or a `SemaphoreSlim`, because async calls are synchronous anyway.
- **Backups:** nightly `BackupDatabase` to `/data/backups/connector-YYYYMMDD.db`, then upload to Azure Blob (managed through the cloud link with a SAS from the backend, so no storage key lives on the edge) and keep N local copies. When HA app backups run with `backup: hot`, add `backup_pre` to trigger a consistent SQLite snapshot (or exclude `*.db-wal`/`*.db-shm` and back up only the snapshot) so HA doesn't copy a torn WAL. Restore = stop, swap the file, start.
- **Use plain files** only for user-exportable artifacts (dashboard JSON export/import). Atomic write = write temp, `fsync`, rename.
- LiteDB adds nothing here that SQLite plus JSON columns doesn't cover, and SQLite has much broader tooling.

### Gaps
- Didn't verify LiteDB's latest release date or v6 status (the GitHub release page wasn't fetchable).
- Didn't verify whether the HA backup format (tar per app) handles open SQLite WAL files safely without `backup_pre`.

## 7. Secrets on the edge: HA token, cloud credentials, Data Protection key persistence, file permissions, Docker secrets, no TPM, rotation

### Takeaway
On Linux, ASP.NET Core Data Protection **does not encrypt keys at rest unless you configure it** (`ProtectKeysWith*`). Keys must sit on a persistent volume, or encrypted tokens become unreadable after a container recreate. Keys rotate every 90 days by default. The Pi 5 has no TPM, so on-device secret encryption mostly protects against casual disk theft unless the wrapping key lives elsewhere. Practical model: keep the HA token and cloud credential in `/data` (mode 0600, non-root UID), optionally encrypted with Data Protection whose key ring is wrapped by a certificate or key delivered from the cloud. Prefer short-lived, rotatable cloud credentials (device enrollment → token refresh) over long-lived keys. Under HA OS, `SUPERVISOR_TOKEN` removes the need for a user HA token.

### Cited Findings
- "If you specify an explicit key persistence location, the data protection system deregisters the default key encryption at rest mechanism, so keys are no longer encrypted at rest" — [AspNetCore.Docs key-storage-providers.md](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/security/data-protection/implementation/key-storage-providers.md)
- "When hosting in a Docker container, keys should be persisted in a folder that's a Docker volume … or in an external provider, such as Azure Key Vault or Redis". "Keys have a 90-day lifetime by default. When a key expires, the app automatically generates a new key"; retired keys can still decrypt — [AspNetCore.Docs default-settings.md](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/security/data-protection/configuration/default-settings.md)
- The key ring "should be protected using appropriate permissions. You can choose to encrypt keys at rest, but this doesn't prevent cyberattackers from creating new keys". In Docker the app path is constant, so the application discriminator is stable; set `SetApplicationName` if the app runs in several environments. `ProtectKeysWithCertificate` plus `UnprotectKeysWithAnyCertificate` support certificate rotation; Azure options are `PersistKeysToAzureBlobStorage` and `ProtectKeysWithAzureKeyVault` — [AspNetCore.Docs configuration/overview.md](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/security/data-protection/configuration/overview.md)
- Compose secrets: "Secrets are mounted as a file in /run/secrets/<secret_name>" from `file:` sources; images commonly read `*_FILE` env vars; Linux containers only — [docker/docs compose use-secrets.md](https://github.com/docker/docs/blob/main/content/manuals/compose/how-tos/use-secrets.md)
- HA apps get `SUPERVISOR_TOKEN` in the environment for the Core API/WebSocket proxy — [docs/apps/communication.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/communication.md)
- The chiseled images run as non-root by default (`APP_UID`) — [dotnet-docker distroless.md](https://github.com/dotnet/dotnet-docker/blob/main/documentation/distroless.md)

### Inferences
- **Threat model at home:** the main risks are a stolen SD card or SSD, a NAS share exposing `/data`, or backups leaking (HA backups are encrypted; Azure Blob backups should be too). Without a TPM, encrypting with a key stored next to the data only stops the casual cases. Options to do better:
  1. Wrap the Data Protection key ring with a certificate whose private key is fetched from the cloud at startup, after device authentication, and kept only in memory. The edge then can't decrypt offline, but HA keeps working while the cloud is down if the decrypted token is cached in memory.
  2. Accept file permissions as the boundary (0600, dedicated UID, volume not shared over SMB).
  
  Option 2 plus encrypted off-box backups is proportionate for a home connector. Option 1 is a hardening step for later.
- **HA token:** under HA OS use `SUPERVISOR_TOKEN`, so nothing is stored. Elsewhere the user pastes a long-lived access token during pairing, stored Data-Protection-encrypted in SQLite. Rotation = the user creates a new token in the HA profile, the connector validates it with `auth/current_user` over WebSocket, then the user revokes the old one (HA long-lived tokens last 10 years by default; not verified here).
- **Cloud credential:** device enrollment (one-time pairing code → backend issues a device identity), then short-lived access tokens for Web PubSub/SignalR/Relay negotiated from the backend. Never ship Azure connection strings to the edge. Rotation comes from expiry. Revocation sits server-side.
- **Docker secrets** suit NAS installs where the admin supplies the HA token up front (`HA_TOKEN_FILE=/run/secrets/ha_token`). Support both the env var and `*_FILE`.
- Set `SetApplicationName("spine-connector")` so the key ring survives path or image changes, and persist keys under `/data/keys`.

### Gaps
- Didn't look into Pi 5 alternatives such as an add-on TPM HAT (e.g. Infineon SLB9670-based boards) or the RP1/BCM2712 OTP secure-boot features. No primary source read.
- Didn't verify HA long-lived token lifetime or rotation APIs.

## 8. Observability from the edge: OpenTelemetry → Azure Monitor, sampling and cost, health endpoints, remote logs, Aspire dashboard standalone

### Takeaway
Use the **Azure Monitor OpenTelemetry Distro** (`Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0, July 2026) or the bare exporter for AOT. It samples at a **rate-limited 5 traces/s by default**, keeps telemetry in an **offline disk store** (50 MiB cap) when the link is down, and persists pending telemetry at shutdown. On a home box, turn off Live Metrics, keep traces rate-limited (≤1/s), send logs at Warning+ and metrics every 60 s, so cost stays at a few cents a month (not verified). For local viewing, the Aspire dashboard ships as a standalone container (`mcr.microsoft.com/aspire/dashboard`) and since 13.6 can persist telemetry to SQLite. It's a dev tool, not production monitoring.

### Cited Findings
- Distro versions: 1.6.0 (2026-07-27), 1.5.0 (2026-04-30), 1.4.0 (2025-11-14); 1.7.0-beta.1 unreleased — [Azure.Monitor.OpenTelemetry.AspNetCore CHANGELOG](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.AspNetCore/CHANGELOG.md)
- "The Azure Monitor Distro uses rate-limited sampling by default, collecting up to 5.0 traces per second". Configure with `options.TracesPerSecond`, or `SamplingRatio` (set `TracesPerSecond = null`), or env `OTEL_TRACES_SAMPLER=microsoft.rate_limited` / `microsoft.fixed_percentage` with `OTEL_TRACES_SAMPLER_ARG`. `TracesPerSecond` wins if both are set. Live Metrics is on by default and is disabled with `EnableLiveMetrics = false`. Metric views can drop instruments (e.g. `http.client.request.duration`) — [Distro README](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.AspNetCore/README.md)
- Exporter offline storage is on unless `DisableOfflineStorage`. The offline storage cap (50 MiB) "is not configurable". `Shutdown()` writes pending telemetry to disk and uploads it in the background or on a later run. `Retry.NetworkTimeout` defaults to 100 s. Several apps on one machine should each set `StorageDirectory`. AOT is supported, but `IConfiguration` binding isn't, so configure in code — [Exporter README](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.Exporter/README.md)
- Health checks are AOT-compatible — [native-aot.md](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/fundamentals/native-aot.md). The HA Supervisor polls an app's `watchdog` URL — [docs/apps/configuration.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/configuration.md)
- Aspire dashboard standalone: UI on 18888, OTLP/gRPC on 18889 (mapped to 4317), OTLP/HTTP on 18890 (mapped to 4318); image `mcr.microsoft.com/aspire/dashboard:latest`; secured with a browser token by default (`--allow-anonymous` for dev). "Telemetry is automatically removed if telemetry limits are exceeded". "The standalone dashboard defaults to no persistence". The persistence modes (`ASPIRE_DASHBOARD_PERSISTENCE_MODE=run`, `Resume`) need a mounted volume. It "is intended for development history … not long-term production observability" — [aspire.dev dashboard/standalone.mdx](https://github.com/microsoft/aspire.dev/blob/main/src/frontend/src/content/docs/dashboard/standalone.mdx)
- Aspire 13.6 (2026-09-29) added "SQLite-backed telemetry and run history" in the dashboard — [aspire.dev whats-new/aspire-13-6.mdx](https://github.com/microsoft/aspire.dev/blob/main/src/frontend/src/content/docs/whats-new/aspire-13-6.mdx)

### Inferences
- **Edge config:**
  ```csharp
  builder.Services.AddOpenTelemetry().UseAzureMonitor(o => {
      o.ConnectionString = cloudProvidedConnString;   // fetched via device enrollment
      o.TracesPerSecond = 0.5;
      o.EnableLiveMetrics = false;
      o.StorageDirectory = "/data/otel";
  });
  ```
  plus `builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("", LogLevel.Warning)`. Add a second OTLP exporter only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (local Aspire dashboard).
- **Is a connection string a secret?** Application Insights connection strings (instrumentation key) are ingestion-only identifiers, but anyone holding one can send telemetry. Have the backend issue it at enrollment, and/or route edge telemetry through the backend. Alternatively, use Entra ID-authenticated ingestion with a token from the backend (not researched).
- **Endpoints:** `/health/live` (process alive) and `/health/ready` (HA WebSocket connected, cloud link connected, DB writable). The `watchdog:` URL and the Docker `HEALTHCHECK` use `live`. The Spine app UI shows `ready` details.
- **Remote logs:** instead of SSH, the connector keeps a ring buffer of recent logs (e.g. the last 2,000 lines in memory or SQLite), and an authenticated cloud command asks for "send last N log lines / turn on debug for 15 min". This lets the owner debug through Spine without opening ports. Under HA OS, app stdout shows in the HA app log panel.
- **Local viewing:** `docker run mcr.microsoft.com/aspire/dashboard` on the Pi or the Mac, pointing the connector's OTLP exporter at it. Fine for debugging sessions; don't keep it running on the Pi permanently (it uses memory and keeps telemetry in RAM unless persistence is on).

### Gaps
- No current Azure Monitor pricing for small ingestion volumes was researched, so the cost estimate is unverified.
- Didn't verify whether the Aspire dashboard image is published for arm64 (very likely, but not confirmed from the docs read).

## 9. Dev workflow: Aspire AppHost with the connector, local HA container + demo integration, Azure Functions and emulators; remote debugging on the Pi; hot reload limits

### Takeaway
Aspire's current version is **13.6 (2026-09-29)**, after 13.5 in August 2026. An AppHost on the Mac can run the connector, a `ghcr.io/home-assistant/home-assistant:stable` container (via the generic `AddContainer`, since no first-party HA integration was found) seeded with `demo:`, and Azure Functions/emulators. The same AppHost can **publish Docker Compose files** (`AddDockerComposeEnvironment` + `aspire publish`/`aspire deploy`), which fits the Pi and NAS target well. Remote debugging on the Pi uses `vsdbg` over SSH from VS Code (64-bit OS only). For containerized deploys, debugging locally against the demo HA is usually enough.

### Cited Findings
- Aspire 13.6: dashboard run history with SQLite-backed telemetry, AppHost-owned terminals, first-party Java and Rust hosting, preview Azure Connector Namespace, experimental Azure Container Apps Sandboxes, portable volume paths, updated emulator images (Azure App Configuration 1.2.0, Linux vNext Cosmos DB emulator as default); upgrade with `aspire update --self` then `aspire update`. In run mode, `DATA_PATH` points to a workload-scoped dir, while Docker Compose/Kubernetes/ACA "receive /data" — [aspire.dev aspire-13-6.mdx](https://github.com/microsoft/aspire.dev/blob/main/src/frontend/src/content/docs/whats-new/aspire-13-6.mdx)
- Aspire 13.5 (DevBlogs 2026-08-18): dashboard refresh, Interaction Service, cross-scope Azure references, live terminals — [Aspire DevBlogs Aug 2026](https://devblogs.microsoft.com/aspire/2026/08/) (search summary)
- Docker Compose as an Aspire deployment target: `builder.AddDockerComposeEnvironment("env")`; `aspire publish` generates `docker-compose.yaml` and a `.env` with parameters; `aspire do prepare-{env}` builds images and fills `.env.{environment}`; `aspire deploy` does everything; Podman via `ASPIRE_CONTAINER_RUNTIME=podman` — [aspire.dev deployment/docker-compose.mdx](https://github.com/microsoft/aspire.dev/blob/main/src/frontend/src/content/docs/deployment/docker-compose.mdx)
- HA Demo integration: "allows you to use integrations which are providing a demo of their implementation". It covers light, switch, sensor, climate, cover, lock, media_player, camera, vacuum, update and more — useful for a deterministic dev HA — [home-assistant.io _integrations/demo.markdown](https://github.com/home-assistant/home-assistant.io/blob/current/source/_integrations/demo.markdown)
- No Aspire hosting integration for Home Assistant turned up in search — [search: Aspire Home Assistant integration](https://aspire.dev/integrations/frameworks/java/java-get-started/) (search summary; negative result)
- Remote debugging: install the debugger on the Pi with `curl -sSL https://aka.ms/getvsdbgsh | /bin/sh /dev/stdin -v latest -l ~/vsdbg`, use passwordless SSH, and a VS Code `launch.json` with `pipeTransport`. The C# extension isn't compatible with 32-bit ARM OSes, and Microsoft recommends developing on a PC and deploying to the device (doc dated 2026-03-07) — [dotnet/docs iot/debugging.md](https://github.com/dotnet/docs/blob/main/docs/iot/debugging.md)
- HA app development uses the `home-assistant/devcontainer` with Supervisor at `localhost:7123` — [docs/apps/testing.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/testing.md)

### Inferences
- **AppHost sketch:**
  ```csharp
  var ha = builder.AddContainer("homeassistant", "ghcr.io/home-assistant/home-assistant", "stable")
      .WithBindMount("./ha-dev-config", "/config")      // configuration.yaml with `demo:` and a pre-onboarded auth store
      .WithHttpEndpoint(port: 8123, targetPort: 8123)
      .WithEnvironment("TZ", "Europe/Stockholm");
  var connector = builder.AddProject<Projects.Connector>("connector")
      .WithEnvironment("HA__Url", ha.GetEndpoint("http"))
      .WithReference(/* functions, signalr emulator, etc. */);
  builder.AddDockerComposeEnvironment("edge");           // `aspire publish` → compose for the Pi
  ```
  Onboarding HA in a throwaway container needs a pre-created user and long-lived token. Committing a seeded `.storage/auth` is fragile, so a startup script that onboards through `/api/onboarding` is more robust (not verified).
- Apple Silicon runs the arm64 HA image natively. Behaviour on the Mac then matches the Pi's architecture.
- Inner loop: `dotnet watch` against the local HA and the AppHost on the Mac. Hot reload over a remote device isn't a supported workflow (no source found). Deploy builds to the Pi via GHCR (CI) or `dotnet publish /t:PublishContainer -p ContainerRegistry=<pi-local-registry>` for fast iteration.
- To debug inside a running container on the Pi: `docker exec` into the image (not possible with chiseled: no shell). Use a debug image variant (`aspnet:10.0` non-chiseled) for the dev tag only, or VS Code attach via `pipeTransport` with `docker exec`.

### Gaps
- Didn't read the Aspire docs for Azure Functions, Azure SignalR emulator and Web PubSub (paths not found in the aspire.dev repo at the guessed locations). Check which Azure services in the cloud design have Aspire emulators.
- Hot reload limits for .NET 10/11 not researched from a primary source.

## 10. Resilience on a small box: reconnect loops, watchdogs, power loss, clock sync

### Takeaway
Use `Microsoft.Extensions.Resilience` (Polly v8 pipelines; `Microsoft.Extensions.Http.Polly` is deprecated) for the HA WebSocket and cloud reconnect loops: exponential backoff with jitter, capped around 60 s, plus a circuit breaker. Docker `restart: unless-stopped` covers crashes and reboots, but Docker doesn't act on *unhealthy*. For hangs, add a self-watchdog that exits the process, or use systemd `WatchdogSec` with your own `WATCHDOG=1` pings (the built-in `UseSystemd()` only sends READY/STOPPING). The Pi 5 has an RTC (battery via the J5 connector), but no battery is included, so after a power cut with no network, time may be wrong until NTP syncs. Validate the clock before trusting token expiry or audit timestamps.

### Cited Findings
- `Microsoft.Extensions.Resilience` and `Microsoft.Extensions.Http.Resilience` are built on Polly; "The Microsoft.Extensions.Http.Polly NuGet package is deprecated"; pipelines are registered per key with `AddResiliencePipeline` and run strategies in configuration order — [dotnet/docs core/resilience/index.md](https://github.com/dotnet/docs/blob/main/docs/core/resilience/index.md)
- `UseSystemd()` notifies only `READY=1` / `STOPPING=1` — [dotnet/runtime ServiceState.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Hosting.Systemd/src/ServiceState.cs)
- Docker restart policies act on exit and after daemon restart; `unless-stopped` doesn't restart a container the user stopped — [docker/docs start-containers-automatically.md](https://github.com/docker/docs/blob/main/content/manuals/engine/containers/start-containers-automatically.md)
- Unhealthy containers aren't restarted by Docker; use autoheal or exit on failure — [docker/compose #4826](https://github.com/docker/compose/issues/4826) (search summary)
- Pi 5: "includes an RTC module. This can be battery powered through the J5 (BAT) connector"; wake alarm via `/sys/class/rtc/rtc0/wakealarm` — [raspberrypi/documentation rtc.adoc](https://github.com/raspberrypi/documentation/blob/master/documentation/asciidoc/computers/raspberry-pi/rtc.adoc)
- Raspberry Pi OS Trixie addresses the Year 2038 problem — [Neowin/Raspberry Pi news](https://www.neowin.net/news/raspberry-pi-os-trixie-unveils-new-control-center-and-future-proof-design/) (search summary)
- The Azure Monitor exporter persists telemetry to disk on shutdown and retries later — [Exporter README](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.Exporter/README.md)
- SQLite busy/locked retry up to `CommandTimeout` — [database-errors.md](https://github.com/dotnet/docs/blob/main/docs/standard/data/sqlite/database-errors.md)

### Inferences
- **Reconnect loop pattern** (HA WebSocket and cloud link, each a `BackgroundService`): connect → authenticate → resubscribe (`subscribe_events`/`subscribe_entities`) → full state resync (`get_states`) → mark ready. On drop: Polly retry with `BackoffType.Exponential`, `UseJitter=true`, `MaxDelay=60s`, infinite attempts (`MaxRetryAttempts=int.MaxValue`); circuit breaker only on auth failures (stop retrying a revoked token and surface "re-pair needed"). Use HA's ping/pong (or `ClientWebSocket.KeepAliveInterval` plus an app-level ping every 30 s with a timeout) to catch half-open TCP connections. Expect HA restarts during updates: the WebSocket closes cleanly and the connector must not treat that as an alarm.
- **Hang detection:** a `HeartbeatMonitor` checks that the main loops tick. If not, log and `Environment.Exit(1)`, so the restart policy, systemd `Restart=on-failure` or the HA Supervisor watchdog takes over. Under systemd, add a `WATCHDOG=1` sender (P/Invoke `sd_notify` or write to `$NOTIFY_SOCKET`) and `WatchdogSec=30`.
- **Power loss:** SQLite WAL with `synchronous=NORMAL` can lose the last transactions but won't corrupt. Use FULL for audit rows if needed. Use an NVMe drive with power-loss protection if possible. Write config files atomically. Stop gracefully: HA Container docs show 10 s isn't always enough for HA, so give the connector `stop_grace_period: 30s` and the HA app `timeout: 30`.
- **Clock:** systemd-timesyncd on Raspberry Pi OS syncs at boot. HA OS handles its own NTP. The connector should check `timedatectl`-style sync status, or simply sanity-check that the time is after the build date, before issuing or validating tokens, and should tag audit entries with a monotonic sequence number as well as wall-clock time. Optionally fit a Pi 5 RTC battery.
- **Resource caps:** in Compose, `mem_limit: 512m`, `cpus: 1.0`, and `logging: driver: local` with `max-size` so json logs don't fill the disk. systemd equivalents are `MemoryMax=` and `CPUQuota=`.

### Gaps
- No primary source read for systemd-timesyncd defaults on Trixie, or HA OS NTP behaviour.
- Didn't verify HA WebSocket API ping/heartbeat semantics in this research (likely covered by the HA API researcher).

## 11. Recommendation: Pi 5 phase and NAS phase

### Takeaway
Build **one multi-arch (amd64 + arm64) OCI image** of the connector on `aspnet:10.0-noble-chiseled-extra`, published to GHCR with semver tags by GitHub Actions. Ship it in three wrappers: a `compose.yaml` (Pi with Raspberry Pi OS, and every NAS), an HA app `config.yaml` (HA OS on the same Pi), and an Unraid CA template. The connector keeps all state in `/data` (SQLite WAL + Data Protection keys + OTel buffer) and reads config from env → `/data/options.json` → appsettings. Updates are notify-then-apply. Health comes from `/health/live` plus a self-watchdog. Telemetry goes to Azure Monitor with rate-limited sampling. Develop on the Mac with an Aspire 13.6 AppHost (connector + HA demo container + cloud emulators) that can also emit the Compose file.

### Cited Findings
- Building blocks that support this design: SDK multi-arch container publishing to GHCR — [publish-configuration.md](https://github.com/dotnet/docs/blob/main/docs/core/containers/publish-configuration.md); chiseled-extra sizes — [sample-image-size-report.md](https://github.com/dotnet/dotnet-docker/blob/main/documentation/sample-image-size-report.md); HA apps referencing one generic multi-arch image with `/data` and `SUPERVISOR_TOKEN` — [configuration.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/configuration.md), [communication.md](https://github.com/home-assistant/developers.home-assistant/blob/master/docs/apps/communication.md); Aspire Docker Compose publishing — [docker-compose.mdx](https://github.com/microsoft/aspire.dev/blob/main/src/frontend/src/content/docs/deployment/docker-compose.mdx); NAS compose support — [TrueNAS](https://www.truenas.com/docs/scale/25.10/scaleuireference/apps/installcustomappscreens/), [QNAP](https://www.qnap.com/en/how-to/faq/con_show.php?cid=3009), [Synology](https://www.synology.com/releaseNote/ContainerManager), [Unraid](https://docs.unraid.net/fr/unraid-os/using-unraid-to/run-docker-containers/overview/)

### Inferences
**Pi 5 phase (recommended):**
1. Hardware: Pi 5, 8 GB (4 GB is enough), M.2 HAT+ with a 2230/2242 NVMe drive (not 4Kn), official 27 W PSU, Active Cooler, Ethernet. Boot from NVMe.
2. Pick one of two setups:
   - **(A) HA elsewhere / HA Container:** Raspberry Pi OS Trixie Lite 64-bit + Docker Engine + `compose.yaml` (connector with `restart: unless-stopped`, `mem_limit`, `/data` volume, log rotation, `stop_grace_period`).
   - **(B) HA OS on the Pi:** connector as an HA app from your own GitHub app repository (`image: ghcr.io/<owner>/spine-connector`, `arch: [aarch64, amd64]`, `homeassistant_api: true`, `ingress: true` for the local admin UI, `watchdog: http://[HOST]:[PORT:8099]/health/live`, `backup: hot` with `backup_pre` snapshot, `timeout: 30`).
   
   Prefer (B) if the owner's HA lives on that Pi anyway: it removes token handling, adds backups and the update UI, and Supervisor watchdogs the app. Otherwise use (A).
3. Runtime: framework-dependent JIT, Server GC + DATAS, container memory limit 512 MB; code kept AOT-clean for later.
4. Updates: Diun/WUD notify (A) or the HA app store (B); pinned major tag; roll back by retagging.
5. Avoid systemd bare-metal unless Docker is unwanted. It's the leanest option, but it creates a second artifact and you own updates and rollback.

**NAS phase:**
- The same image and `compose.yaml`: Synology Container Manager **Project**, QNAP Container Station **Application**, TrueNAS 25.10 **Install via YAML** (`services:` required), Unraid **CA template** or the Compose Manager plugin. Map `/data` to a dataset or share that the NAS backs up (snapshots on TrueNAS/Btrfs). Set `user:` to match the share owner, or make `/data` writable for UID 1654.
- Check the NAS CPU architecture (ARMv8 Synology models need arm64, the rest amd64). The multi-arch index covers both.
- On a NAS with more RAM and several containers, keep the defaults (DATAS adapts), or set Workstation GC if density matters.
- Consider Native AOT + `runtime-deps:10.0-noble-chiseled-extra` (about 27 MB image, ~0.1 s start) once dependencies are AOT-clean. It mainly helps NAS boxes with slow disks and many containers.

### Gaps
- The recommendation assumes the cloud link (Web PubSub/SignalR/Relay) and MCP SDKs are container-friendly and, for the AOT path, AOT-clean. Not verified here.
- No hands-on test of one image across all four NAS UIs. Label and `user:` handling on Unraid/TrueNAS in particular should be validated in a test install.
