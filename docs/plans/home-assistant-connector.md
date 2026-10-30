# Plan: HA:Dispatcher – a Home Assistant connector

**Started:** 2026-10-30
**Last updated:** 2026-10-30 (named HA:Dispatcher, Q12; the TV launcher is TV:Dash. Earlier the same day the owner answered Q1, Q2, Q5 and Q6: HA runs on an HA Green, Azure with a Web App is fine, the domain is cosmomedia.se at Loopia, the TV launcher is a Spine app on Chromecast with Google TV; D2, D10 and D12 decided, D7 adapted, D13 added, S5 and S7 ready)

A .NET 10 service at home, **HA:Dispatcher** (called *the connector* below), is the only thing that talks to Home Assistant (HA), which runs on an HA Green. The connector runs on a Raspberry Pi 5 of its own first and on a NAS later. It owns the entity model, name resolution and the safety policy, serves a local `/v1` API with a live hub, and dials out to Azure over a single outbound connection. Through Azure, a Spine app, an ESP32-S3 voice display, TV:Dash (the owner's TV launcher, a Spine app on Chromecast with Google TV) and the Spine AI server reach the same `/v1` contract from anywhere. The app picks the local or the cloud path by itself and switches without the user noticing. Dashboards are built from HA entities and loaded by every client. The Spine AI server gets smart-home functions (list, search, read state, lights, blinds, climate, scenes, scripts) for text chat, realtime voice, the ESP display and the TV.

Name: **HA:Dispatcher**, in the owner's family of AI:Dash and TV:Dash; no project or image of that name was found. In code the namespace is `HADispatcher`, and no type is called `Dispatcher` or `IDispatcher`: those are MAUI's UI-thread dispatcher, used throughout Spine and the apps. In HA, "dispatcher" is also the core's internal signal helper (`helpers.dispatcher`), so the docs say *the connector* for the role. HA:Bridge was dropped because `ha-bridge` is an established Hue-emulation project published as Docker images and NAS apps. The HA-specific code lives in its own repository; what any Spine app could use becomes Maui.Spine issues (see [Spine gaps](#spine-gaps)). The full research behind this plan (HA's APIs, the Azure link, routing, Pi/NAS operation, AI tools and dashboards, and an inventory of the owner's private repositories) is kept outside this public repository.

Status values: **Proposal** (written, not reviewed), **Waiting** (on another step or an answer), **Ready**, **In progress**, **In review** (PR open), **Done**, **Parked**, **Dropped**.

---

## How to continue from a new session

1. Read this file. Check [Questions for the owner](#questions-for-the-owner): a step whose decision depends on an unanswered question is **Waiting**.
2. Spikes come before phases. Pick the first spike or step whose status is **Ready**.
3. Spine gaps are worked as Maui.Spine issues with `/spine-issue <id>`; connector and gateway work happens in the HA:Dispatcher repository, the TV work in TV:Dash's repository.
4. In the same PR that moves a step: set its status and PR here, add any decision to [Decisions](#decisions), and update **Last updated**.
5. Add what you could not verify to the [Verification backlog](#verification-backlog), and remove what you did verify.

---

## Principles

- **Only the connector touches Home Assistant.** Apps, the TV, the ESP display, the AI server and the cloud all speak the connector's `/v1` contract, directly on the LAN or through the gateway.
- **One contract, two transports.** The same REST paths, hub messages and sequence numbers locally and in the cloud. The cloud relays `/v1` unchanged; what only the cloud can do lives under `/cloud/*`.
- **The HA token never leaves home.** The cloud sends abstract commands (`commandId`, target, action, data); the connector maps them to HA with its own token and its own allow-list. A compromised cloud can never call `shell_command`, `hassio` or anything outside the list.
- **The path is chosen by an authenticated probe, never by the Wi-Fi name.**
- **Commands are absolute and idempotent** (`brightness_pct: 60`, not `toggle`), carry an `Idempotency-Key`, and are never queued while the home is offline: stale intent is dangerous for physical actions.
- **Spine stays a smorgasbord.** Generic parts (routing, a line chart, configurable controls) become Spine packages or issues; Microsoft.Extensions.AI remains the AI contract; no provider keys in apps.
- **Documentation is in English.** Conversation with the owner is in Swedish.

---

## System map

```mermaid
flowchart LR
  subgraph HOME["Home LAN"]
    HA["Home Assistant OS<br/>on HA Green"]
    subgraph CON["HA:Dispatcher (Pi 5, later NAS)"]
      HAC["HA client<br/>WebSocket + REST"]
      CORE["Home Core<br/>state cache, registries,<br/>name resolution, policy, audit"]
      LAPI["Local API<br/>/v1 REST + HomeHub"]
      DB[("/data: SQLite")]
      LINK["Home Link client"]
      LAI["Local Server.AI<br/>optional, later"]
    end
    ESP["ESP32-S3 voice display"]
    TV["TV:Dash<br/>Spine app on Chromecast"]
    APPH["Spine app at home"]
  end

  subgraph AZ["Azure (Sweden Central)"]
    subgraph GW["HA:Dispatcher Gateway (ASP.NET Core Web App, always on)"]
      HUB["Link hub + HomeHub"]
      RELAY["/v1 relay<br/>projection, presence"]
      AIS["Server.AI<br/>features, realtime relay,<br/>HADispatcher.AI functions"]
      AUTH["Server.Authentication<br/>JWT, JWKS, pairing"]
      PUSH["Server: push"]
    end
    FN["Functions Flex<br/>timers, queues, backups"]
    TAB[("Table Storage")]
    DNS["Azure DNS zone<br/>home.cosmomedia.se<br/>LAN names, DNS-01"]
  end

  LOOPIA["Loopia DNS<br/>cosmomedia.se"]

  LLM["Realtime / chat models"]
  APPB["Spine app away"]

  HAC -- "subscribe_entities, call_service, registries" --> HA
  HAC --> CORE --> LAPI
  CORE --> DB
  CORE --> LINK
  LAI -.-> CORE
  APPH -- "HTTPS + WSS /v1" --> LAPI
  TV -- "scenes, state /v1" --> LAPI
  LINK == "one outbound WSS" ==> HUB
  APPB -- "HTTPS /v1, WSS HomeHub" --> RELAY
  APPB -- "/ai, /auth" --> AIS
  APPH -- "/ai, /auth" --> AIS
  ESP == "one WSS, PCM16" ==> AIS
  TV -- "realtime voice" --> AIS
  RELAY --> HUB
  AIS -- "tool calls via IHomeApi" --> HUB
  AIS == "realtime" ==> LLM
  FN --> PUSH
  RELAY --> TAB
  RELAY -- "DNS-01 + A record for the connector" --> DNS
  LOOPIA -. "NS delegation" .-> DNS
```

Thick arrows are long-lived single sockets; dotted ones are optional.

---

## Subsystems

| Subsystem | Responsibility | Technology | Runs on | Reuses |
|---|---|---|---|---|
| HA client (`HADispatcher.HomeAssistant`) | One multiplexed WebSocket to HA: auth, `supported_features` with coalescing, `subscribe_entities`, registries (incl. aliases via `get_entries`), `call_service` with `return_response`, history, statistics; REST for camera stills and health | `ClientWebSocket`, System.Text.Json source generators, Microsoft.Extensions.Resilience | Connector | Nothing ready-made; NetDaemon's source as reference |
| Home Core | State cache with `seq` and a ring buffer, registries and name resolution (name, alias, area, floor), exposure per caller class, protected entities, idempotent commands, audit | .NET 10, Microsoft.Data.Sqlite (WAL) | Connector | HA's intent design as the model |
| Local API | `/v1` REST, `HomeHub`, signed `GET /v1/hello`, local token exchange, `/health/live` and `/health/ready`, mDNS `_spine-connector._tcp` | ASP.NET Core Minimal API, SignalR (untyped hub, JSON), Kestrel with a Let's Encrypt certificate | Connector | – |
| Home Link | The one outbound connection: commands with results, state deltas, snapshots, heartbeat, the union of cloud subscriptions | SignalR client → gateway hub, behind `IHomeLink` | Both | – |
| HA:Dispatcher Gateway | Relays `/v1` unchanged, HomeHub for clients away, last-known-state projection and presence, pairing, DNS-01 for the connector, backup SAS | ASP.NET Core `net10.0`, Table Storage, Key Vault, Azure DNS (`home.cosmomedia.se`, delegated from Loopia) | Azure Web App on App Service B1 Linux (Always On, WebSockets on) | Spine server hosting as in the Showcase |
| AI | Features (home chat, home voice, desk agent, TV agent), budgets per caller, realtime relay, the smart-home functions | `Plugin.Maui.Spine.Server.AI`, `HADispatcher.AI` with server-side `SpineAIFunction`s over `IHomeApi` | Gateway; optionally the connector | Server.AI unchanged |
| Identity and pairing | User sign-in, JWT/JWKS, device keys for the ESP display and the TV, connector enrolment with a one-time code | `Plugin.Maui.Spine.Server.Authentication`, private-key JWT for the connector | Gateway | Server.Authentication |
| Dashboards | Versioned schema, generator from areas/floors/labels/domains, storage with `rev` and ETag, cloud mirror | JSON in SQLite, C# generator | Connector (truth), gateway (mirror) | – |
| Client SDK (`HADispatcher.Client`) | `IHomeApi`, `IHomeEvents`, `IHomeRouter`, a routing `DelegatingHandler`, token choice | HttpClient, SignalR client, MAUI `Connectivity` | Phone/tablet/Mac app and TV:Dash | Spine's authentication handler; candidate for a Spine package |
| Spine app | Dashboards, controls, chat, voice, widgets and controls, push, settings | .NET MAUI with Spine packages | iOS, Android, Mac Catalyst | MasonryLayout, Reorder, Charts, ChatView, Voice, Widgets, PushNotifications, Rows |
| ESP32-S3 display | Wake word, echo cancellation, standing realtime session; later home context cards | ESP-IDF, ESP-SR | Desk | Existing desk-display firmware, unchanged in phase 3 |
| TV:Dash (TV launcher) | Scene rows, state, a voice button | The owner's Spine (.NET MAUI) Android app, in progress | Chromecast with Google TV | Client SDK, Spine packages |
| Background | Scheduled jobs, queue handlers, backups, retention | Functions Flex .NET 10 isolated, Spine Messaging | Azure | Spine Server, Messaging |
| Delivery and operation | One multi-arch OCI image, a compose file, health, telemetry, update notices, backup | `aspnet:10.0-noble-chiseled-extra`, SDK container publish, GHCR, Azure Monitor OpenTelemetry, Diun or WUD | Pi 5 (Docker Compose on Raspberry Pi OS Trixie, NVMe boot), then NAS | Spine's Aspire sample |

---

## Key flows

- **App command at home.** `IHomeApi.LightSetAsync(...)` → router on `Local` (a signed hello probe confirmed the paired connector) → HTTPS to `<installId>.home.cosmomedia.se:8443` (an A record to the Pi's LAN IP) with the user's JWT (validated offline against cached JWKS) or a local token → scope, allow-list, protection and idempotency checks → `call_service` on the connector's HA socket → `200`; the state change follows on HomeHub with a new `seq`.
- **App command away.** Same app code; router on `Cloud` → `api.<domain>/v1/...` → gateway authenticates, rate-limits, checks presence (`503 HomeOffline` with `lastSeen` if the home is offline) → `InvokeAsync` on the connector's link with a deadline → the connector runs the same code path → the response is returned unchanged. A Wi-Fi → cellular switch retries once on the other path with the same `Idempotency-Key`; live state switches make-before-break with `Subscribe(lastSeq)`.
- **Live state.** One `subscribe_entities` to HA, a monotonic `seq` per change and a ring buffer of ~1,000 events. Local clients subscribe with a filter; the connector forwards only the union of what active cloud clients watch, coalesced per entity (~250 ms for lights and covers, seconds with a dead band for sensors). After an HA restart the connector resubscribes and diffs the new snapshot against the old one.
- **ESP voice command.** The display's standing session over one WSS to the gateway's relay → the model calls `home_turn_on` → the relay runs the server function with the caller's context → `IHomeApi` over the link → the connector resolves the name (preferring the display's area), checks policy and calls HA → result back to the model → spoken answer. No firmware change: the relay already runs server functions for the device.
- **TV scene.** The launcher is a Spine app, so it uses the same client SDK as the phone: `POST /v1/scenes/{id}:activate` locally with the TV's device key; voice starts from the app's own button (the remote's assistant button belongs to the system) into a TV agent feature with a "no locks" scope and the living room as preferred area.
- **Dashboard load.** Render the cached version first → `GET /v1/dashboards/{id}` with `If-None-Match: <rev>` → `304` or new JSON → read the state of the dashboard's entities and subscribe from that `seq`. Cards whose entity disappeared are marked missing, never deleted.

---

## Decisions

**Decided** means the owner's answer settles it; the rest are **Proposal** until the owner has reviewed them.

| # | Decision | Recommendation | Alternatives | Switch when | Status |
|---|---|---|---|---|---|
| D1 | Home ↔ cloud transport | One outbound WSS to a self-hosted ASP.NET Core SignalR hub in the gateway: client results (`InvokeAsync<T>`) for commands, streamed deltas for state, behind `IHomeLink` | Web PubSub with Functions; Relay Hybrid Connections; Event Grid MQTT; Tailscale/Cloudflare Tunnel; IoT Hub | Many homes must be carried (→ Web PubSub) | Proposal; its precondition holds since Q2 allows a Web App |
| D2 | Cloud compute | An Azure Web App (App Service B1 Linux, Always On, WebSockets on) for everything with sockets or tight latency; Functions Flex for background work | Container Apps with min one replica; everything in Functions + Web PubSub | – (Functions caps HTTP at 230 s and cannot host Spine's realtime relay) | Decided (Q2) |
| D3 | Where the AI relay and tools run | Server.AI in the gateway; tools as server `SpineAIFunction`s in `HADispatcher.AI` calling `IHomeApi`, with all logic in the connector | Server.AI in the connector; both | S2 shows p95 > 300 ms per tool call, or voice must survive an Azure outage (→ also host Server.AI in the connector) | Proposal |
| D4 | MCP or in-process functions | In-process `SpineAIFunction`s for Spine's own sessions; an MCP facade over the same command API in phase 6 | MCP as the inner protocol; proxy HA's `/api/mcp` | Other agents (Claude, HA's MCP client) need the tools | Proposal |
| D5 | HA client | Own thin client over `ClientWebSocket` with source-generated JSON | NetDaemon.Client (marked internal, no `subscribe_entities`); stale HADotNet/HassClient | – | Proposal |
| D6 | Routing | `IHomeRouter`: authenticated hello probe, local at 0 ms and cloud at 250 ms, sticky with hysteresis, idempotency keys, make-before-break with `seq` | SSID matching; always cloud; VPN | – | Proposal |
| D7 | Local TLS and names | Delegate a subdomain, `home.cosmomedia.se`, from Loopia to an Azure DNS zone with NS records, and leave the rest of `cosmomedia.se` at Loopia. The gateway writes the connector's A record (its LAN IP) and the DNS-01 TXT records there, so no DNS credentials live on the edge. Let's Encrypt certificate for `<installId>.home.cosmomedia.se`; fallback: a pinned self-signed key distributed via the cloud | Loopia's API for DNS-01 straight from the gateway (its credentials reach the whole domain); private CA; plain HTTP on the LAN | Loopia does not allow NS records on a subdomain (→ Loopia API), or S5 fails on the owner's router (DNS rebinding protection) | Proposal (Q5 answered) |
| D8 | Authentication | Spine's JWT on both paths, validated offline with JWKS persisted on disk (~30 days last-known-good); a device-bound local credential from pairing; device keys for ESP and TV; HA token only in the connector | Separate local keys per client; HA tokens in clients | – | Proposal |
| D9 | Dashboards | Own versioned schema; the connector is the truth (`rev`, ETag), the gateway keeps a readable mirror, clients cache; the default dashboard is generated from areas, floors and labels | Cloud as truth; render HA Lovelace directly | Several homes, or editing must work while the home is offline (→ cloud as truth) | Proposal |
| D10 | Packaging | One multi-arch OCI image on GHCR, run with Docker Compose on Raspberry Pi OS Trixie (64-bit, NVMe boot) and later with the same compose file on the NAS. No HA app wrapper: HA runs on the HA Green, and a connector on hardware of its own keeps working while HA updates or restarts | Run it as an HA app on the HA Green; systemd | The Pi goes away before the NAS arrives (→ an HA app on the Green) | Decided (Q1) |
| D11 | Events into HA | `call_service` on scripts and automations with a non-admin HA user (`fire_event` and `subscribe_trigger` need admin) | Admin user; a companion integration | A need for custom events that scripts cannot cover | Proposal |
| D12 | TV technology | TV:Dash, the owner's TV launcher, a Spine (.NET MAUI) Android app already in progress on Chromecast with Google TV. It takes the same `HADispatcher.Client` as the phone app; the D-pad focus and leanback gaps it meets become Spine issues | Kotlin + Compose for TV (the research's pick, because MAUI has no TV focus support) | – | Decided (Q6) |
| D13 | Connector ↔ HA | The connector reaches the HA Green over the LAN (`ws://<ha-green>:8123/api/websocket`) with a dedicated non-admin user whose long-lived token is `local_only`; both boxes get DHCP reservations, so neither depends on `.local` resolving | HTTPS on HA (only if it is turned on for other reasons); an admin token | HA gets TLS anyway, or the connector moves off the LAN segment | Proposal |

---

## Spikes

| # | Spike | Question | Done when | Status |
|---|---|---|---|---|
| S1 | HA client | Does an own client handle `subscribe_entities`, registries with aliases, `call_service` with `return_response` and HA restarts, running on the Pi 5 against the HA Green? | 24 h against the HA Green and a demo container, incl. an HA update/restart, with no lost state; CPU and RAM on the Pi measured | Ready |
| S2 | Link latency | What does a tool call gateway → connector → HA cost? | p50/p95 for `get_state` and `call_service` from Sweden Central measured against 150 ms / 300 ms | Waiting (S1) |
| S3 | HA tool on the ESP display | Does an HA tool work as a server function without a firmware change? | `scene_activate` and `home_get_state` listed in the desk agent feature on a gateway copy of the display's backend; function-to-speech time measured | Waiting (S2) |
| S4 | Swedish name resolution | Own tools vs HA's Assist MCP vs `conversation/process`; full vs mini realtime model | 50–100 household utterances with accuracy and latency per variant | Waiting (S1) |
| S5 | Local TLS | Can `home.cosmomedia.se` be delegated from Loopia to Azure DNS, and do the owner's router, iPhone, Android (target 36 and 37), the Chromecast, Mac Catalyst and the ESP display accept a Let's Encrypt name pointing to a LAN IP? | NS delegation resolving; a green matrix, incl. the fallback when DNS rebinding protection blocks it | Ready |
| S6 | Routing | Can the client switch path mid-session without duplicate commands? | Wi-Fi → cellular with a command in flight and a live stream, no duplicates or gaps in `seq` | Waiting (S1) |
| S7 | TV microphone | On Chromecast with Google TV: does the launcher (a foreground Spine app) get audio from the remote's microphone, can it be the HOME app, and how much memory does it use? | Tested in the launcher on the owner's Chromecast; the voice path chosen (remote microphone, or the phone app as microphone) | Ready |

The dev loop runs on the Mac with an Aspire AppHost: the connector, an HA container (`ghcr.io/home-assistant/home-assistant:stable`) with the `demo` integration, the gateway and Azurite. The same AppHost generates the Pi's compose file (`AddDockerComposeEnvironment`).

---

## Phases

| # | Phase | Content | Done when | Status |
|---|---|---|---|---|
| 1 | At home on the Pi 5 | `HADispatcher.Contracts` (DTOs, envelopes, OpenAPI, JSON contexts), HA client, Home Core, local API with a Let's Encrypt certificate, SQLite in `/data`, OCI image with Docker Compose on the Pi, non-admin HA user on the HA Green; in the app a generated dashboard, controls and a settings page | The family controls the home from the app on the home Wi-Fi | Waiting (S1, S5) |
| 2 | Away | Gateway with sign-in, pairing, Home Link, `/v1` relay, HomeHub, projection and presence; the router in the client SDK; HA alerts to push via `IPushSender` | The same app works away with no settings, and an offline home shows "last seen" | Waiting (1, S2, S6) |
| 3 | AI | `HADispatcher.AI` in features for text chat (ChatView), app voice, the desk agent and the TV agent; protected entities with confirmation; budgets per caller | The ESP display and the app control the home by Swedish voice and text | Waiting (2, S3, S4) |
| 4 | Dashboards and surfaces | Editor with MasonryLayout and Reorder, cloud mirror, widgets and controls through generic slots, Live Activities, a line chart | A dashboard the user builds shows on every device | Waiting (2) |
| 5 | TV | HA:Dispatcher in TV:Dash: `HADispatcher.Client`, scene rows, state and a voice button | Scenes and a voice chat from the sofa | Waiting (1, S7); scenes can start after phase 1 since the TV is at home |
| 6 | NAS and hardening | Move to the NAS with the same image and a restored `/data`, backup to Blob via SAS, Native AOT, signed commands, MCP facade, optional local relay, optional companion integration | Runs without the Pi, and external agents reach the same tools | Waiting (1–3) |

### The AI tool set (phase 3)

Twelve typed tools, modelled on HA's intent tools rather than a generic `call_service`: `home_list_devices`, `home_search`, `home_get_state`, `home_turn_on`, `home_turn_off`, `light_set` (brightness, colour name, kelvin), `cover_set_position`, `cover_stop`, `climate_set_temperature`, `scene_activate`, `script_run`, `media_control`.

- A target is `{name, area, floor, domain, device_class}`; a hidden `preferred_area` comes from the caller (the TV's room, the display's room).
- Results are `{ok, targets, failed: [{name, reason, candidates}], speech}`. Ambiguity returns candidates, never a guess.
- Name resolution in layers: exact case-folded name and alias, then Swedish normalisation (definite forms), then fuzzy matching.
- Locks, alarm panels, garage doors, valves and chosen scripts are **protected**: the first call returns `confirmation_required` with a short-lived token, and only a second call with it acts.
- Reads (temperature and the like) are answered from the connector's cache, so a voice tool call stays within the latency budget.
- A generic `call_service` belongs only in a separate admin API that is never given to the display or the TV.

---

## Spine gaps

To be filed as Maui.Spine issues when the phase that needs them starts.

| Gap | Needed in | Notes |
|---|---|---|
| A routing / connectivity service the packages' backend URIs can ask | 2 | Today each client package has a fixed `Backend` URI; v1 of the client SDK routes only `/v1` and leaves `/ai` and `/auth` on the cloud |
| `LineChart` and a widget sparkline | 4 | Charts has ring, bar and column only |
| Configurable widgets and controls (generic slots resolved at runtime) | 4 | An app has at most nine static controls and nine widget kinds |
| App Intents / Siri | later | Parked idea in Spine |
| TV support: D-pad focus, leanback layouts | 5 | TV:Dash is a Spine app (D12), so what it has to work around becomes Spine issues |
| A server-side MCP package | 6 | Only if the MCP facade turns out to be generic |

---

## Questions for the owner

| # | Question | Affects | Answer |
|---|---|---|---|
| Q1 | Where does HA run today: HA OS on the same Pi 5, other hardware, or HA Container? | D10 (HA app or compose) | HA Green (2026-10-30) |
| Q2 | Is "Azure Function backend" a hard requirement, or is an always-on ASP.NET Core gateway (≈ B1) fine? | D1, D2 | Azure, and a Web App is fine (2026-10-30) |
| Q3 | Only this home, or several homes / a product later? | Tenancy, D1, D9 | Open |
| Q4 | Must voice or control work when the internet is down? | D3; a local-only voice path would mean an ESPHome variant against HA Assist | Open |
| Q5 | Is there an own domain that can be moved to Azure DNS? | D7, S5 | Not in Azure: cosmomedia.se at Loopia, so a delegated subdomain (2026-10-30) |
| Q6 | Does the TV launcher exist already: which device and which technology? | D12, S7 | In progress: a Spine (.NET MAUI) Android app on Chromecast with Google TV (2026-10-30) |
| Q7 | Which entities are sensitive, and may the AI ever unlock? | Protected entities, biometric step-up | Open |
| Q8 | Dashboards per user or per home; is import from Lovelace needed? | D9, the schema, the editor | Open |
| Q9 | Which realtime provider and region (Azure OpenAI Sweden Central, OpenAI, Gemini Live)? | Cost, data residency, latency | Open |
| Q10 | Should the ESP display's backend move into the gateway, or get the Home Link itself? | Phase 3 | Open |
| Q11 | Should HA scenes appear in Control Center / Quick Settings? | Spine gap: configurable controls | Open |
| Q12 | What are the connector and its repository called, and is it private? | Where phase 1 starts | Named HA:Dispatcher (2026-10-30); repository name and visibility open (`ha-dispatcher`?) |
| Q13 | Where does TV:Dash's code live, so its Spine gaps can be read? | Phase 5, Spine gaps | Open |

---

## Risks

| Risk | Counter-measure |
|---|---|
| HA changes undocumented WebSocket commands (`subscribe_entities`, registries, lovelace) | Integration tests against an HA container for every monthly release |
| HA's entity naming changes from 2026.11 (`friendly_name` deprecated) | Build display names from the registries and aliases |
| The gateway cold-starts | B1 with Always On, never F1; Functions only off the hot path |
| Tool latency over the WAN pushes voice answers past 1.5 s at p95 | S2, reads from cache, a persistent link, the local relay as retreat |
| The ESP display's internal RAM allows one TLS socket | Keep one socket; tools run server-side |
| The router blocks DNS rebinding | Connect to the cached IP while keeping the host name for TLS, pinning as fallback |
| iOS denies local network silently in the background; Android 17 needs `ACCESS_LOCAL_NETWORK` | Ask in the foreground during onboarding; a "no" means cloud only |
| Google TV does not reliably let a third-party app be the home screen, and Google TV updates have broken the usual workarounds | S7 on the owner's Chromecast; the launcher still works as an ordinary app opened from the home screen |
| A compromised cloud or AI operates locks | Connector allow-list, protected entities, confirmation tokens |
| SD card wear | Boot the Pi from NVMe from the start |

---

## Verification backlog

- Official Azure prices for Sweden Central (the research could only read secondary sources).
- Whether Azure SignalR Service supports client results across several hub servers (only matters when scaling out).
- Whether the Web PubSub, Relay, Azure SignalR and MCP SDKs are AOT-clean.
- .NET 10 + SignalR footprint on a Pi 5 (S1 measures it).
- Whether HA's `/api/mcp` accepts long-lived tokens, and whether `lovelace/dashboards/list` needs admin.
- Remote-microphone capture and HOME mode on the owner's TV device (S7).
- Swedish quality of the realtime models (S4).
