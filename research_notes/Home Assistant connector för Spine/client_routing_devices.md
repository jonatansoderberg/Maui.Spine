# Client routing between a local home API and a cloud API (local-first, cloud fallback) for a .NET MAUI app, an Android TV launcher and an ESP32-S3 device — state of October 2026

Scope note for the report writer: several primary hosts were blocked from this research session (companion.home-assistant.io, community.home-assistant.io, developers.home-assistant.io, the home-assistant GitHub org, words.filippo.io, docs.espressif.com, learn.microsoft.com, tailscale.com, developers.meethue.com, androidauthority.com). For those, findings rest on search-engine extracts of the pages, which is lower confidence than a full read. Apple and Android developer docs, the ESP-IDF/esp-protocols sources, the dotnet/maui, dotnet/aspnetcore and IdentityModel sources, and NuGet registration data were read in full.

Version flag: the brief says "iOS 26". Apple's TN3179 was revised on 2026-10-06 "to account for changes in macOS 27.2", so the 2026 Apple releases are numbered 27 (iOS 27 / macOS 27). The report should say "iOS 26/27" or check which one the owner means — [TN3179](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy).

## 1. How do existing products choose between local and remote (HA Companion, Plex, Hue, Homey, Ubiquiti, Synology, Tailscale), and which patterns generalise?

### Takeaway
There are two families. In the first, the client decides from its network context: HA Companion matches the Wi-Fi SSID, which needs location permission and gets fooled by identical SSIDs. In the second, the client gets a list of candidate endpoints, tries the direct one first, falls back to a relay and keeps checking in the background (Plex, Synology, Tailscale). The patterns worth copying are: an authenticated local identity that doesn't depend on the SSID (Hue's CN = bridge-ID check, Plex's and Homey's per-device wildcard hostnames under a vendor domain that resolve to LAN IPs), an endpoint list supplied by the cloud, direct before relay, and background upgrades back to direct. HA's history also shows what goes wrong when plaintext local URLs are used off the home network.

### Cited Findings
**Home Assistant Companion (iOS/Android)**
- The user enters home Wi-Fi SSIDs under the internal URL setting, and the app uses the internal URL only on those networks. Reading the SSID needs location permission; without it the app can't tell which network it's on — [HA Companion docs, Connection security level](https://companion.home-assistant.io/docs/getting_started/connection-security-level/) (search extract).
- From version 2025.11 the user must pick a "connection security level" before the app will use an unencrypted URL. "Most secure" allows a URL such as `http://homeassistant.local:8123` only on the specified home network, and off it the app shows a blocking screen that explains the requirements. "Less secure" is for users who haven't granted location permission but still need an unencrypted URL. The page was labelled "iOS BETA 2025.11.0" — [HA Companion docs](https://companion.home-assistant.io/docs/getting_started/connection-security-level) (search extract).
- On Android, a VPN or Ethernet connection can also count as the "home network" since 2025.11. A December 2024 commit added Ethernet/VPN as extra reasons to treat a network as internal, alongside SSID matching rather than instead of it — [HA Companion networking docs](https://companion.home-assistant.io/en/troubleshooting/networking) (search extract), [Android repo mirror](https://git.sudo.is/home-assistant/android/src/branch/master/common).
- There was an Android PR "Add option to always try the internal URL first" (#1788) — [home-assistant/android#1788](https://github.com/home-assistant/android/pull/1788).
- Security bug CVE-2026-55844: before 2025.5.0 the iOS app ignored the SSID allowlist and fell back to the internal URL when it found no other URL, which could expose the user's token on an insecure network. Fixed in 2025.5.0 — [SOCRadar CVE-2026-55844](https://socradar.io/free-tools/cve-radar/CVE-2026-55844); related PR "Do not default to internal URL when external URL is not available" — [home-assistant/iOS#2767](https://github.com/home-assistant/iOS/pull/2767).
- VPN users on iOS have had to set the external URL equal to the internal URL to make their setup work — [home-assistant/iOS#2767](https://github.com/home-assistant/iOS/pull/2767) (search extract).
- Problems users report:
  - The internal URL fields stay greyed out until an SSID is added — [HA community](https://community.home-assistant.io/t/android-companion-app-internal-connection-url-setting-disabled/442181).
  - Newer Android versions need location permission granted in system Settings before the SSID lookup works.
  - One user saw the app keep using the external URL until location was set to "Allow all the time" — [HA community](https://community.home-assistant.io/t/companion-app-not-connecting-to-local-ip/413314).
  - The app treats another network with the same SSID as home — [HA community](https://community.home-assistant.io/t/companion-app-functioning-via-internet-even-on-identical-ssid-as-home/953291).
  - SSL errors when the internal URL uses https with a certificate issued for the external name — [home-assistant/iOS discussion #1727](https://www.github.com/home-assistant/iOS/discussions/1727).

**Plex**
- Plex runs a dynamic DNS space under `plex.direct`: a name with an IP encoded in it resolves back to that IP. Plex partnered with DigiCert to issue each user a wildcard certificate for `*.HASH.plex.direct`. Because the user/server hash binds the certificate to one server (whatever its IP), it is safe to hand the private key to the user — [Filippo Valsorda, "How Plex is doing HTTPS for all its users"](https://words.filippo.io/how-plex-is-doing-https-for-all-its-users/) (search extract), [notes.billmill.org summary](https://notes.billmill.org/programming/DNS/How_plex_is_doing_HTTPS_for_all_its_users.html).
- A `192-168-1-7.<hash>.plex.direct` name resolves to a private IP, so routers with DNS-rebinding protection break it, and users are told to whitelist plex.direct — [Plex forum](https://forums.plex.tv/t/tls-unknown-player-error-and-plex-direct/772754/4), [Plex forum](https://forums.plex.tv/t/enabling-remote-access/873557).
- Clients get the candidate connections (local, remote, and relay with `includeRelay=1`) from plex.tv's `/api/resources`. The server reports its local IP to plex.tv. Relay is used only when the server isn't directly reachable, it is bandwidth-limited (users report about 2 Mbps), and the dashboard labels relayed sessions "Indirect" — [Plex forum](https://forums.plex.tv/t/different-ways-to-connect-to-pms/574208), [Plex support: Network](https://support.plex.tv/articles/200430283-network/).
- I found no official Plex document on whether clients test candidates in parallel or in sequence (see Gaps).

**Philips Hue**
- The CLIP v2 API runs on the bridge on the LAN, with an `hue-application-key` header and an `/eventstream` for pushed updates. Remote control goes through the Cloud Remote API at `api.meethue.com`, which needs a Hue developer account, a registered remote app and an OAuth flow. The .NET library Q42.HueApi has separate `LocalHueApi`/`RemoteHueApi` classes — [Telegraf huebridge plugin README](https://ftp.mirror.yandex.ru/mirrors/github/influxdata/telegraf/main/plugins/inputs/huebridge/README.md), [Q42.HueApi docs index](https://docsearch.algolia.com/mcp/docs/repo/michielpost/q42.hueapi).
- Local TLS, as the Hue developer docs are quoted on a forum:
  - Validate the bridge certificate against Signify's private CA for Hue bridges.
  - Check that the certificate CN equals the bridge ID, either by using the bridge ID as hostname with a custom resolver to the IP, or with a custom hostname verifier.
  - Older bridges have self-signed certificates whose CN is also the bridge ID, so use trust-on-first-use pinning keyed to the bridge ID.
  - [Homey community quoting Hue docs](https://community.homey.app/t/philips-hue-dynamische-scenes-starten-met-logic-http-request/67913); node-hue-api compares the subject CN with the bridge ID — [node-hue-api LocalBootstrap.ts](https://app.unpkg.com/node-hue-api@5.0.0-beta.16/files/src/api/http/LocalBootstrap.ts).

**Homey (Athom)**
- Locally, Homey Pro has a local API key, sent as a Bearer token to the LAN address, and an mDNS name `homey-[ID].local`.
- For local HTTPS it uses a Plex-like name built from the IP with dashes, e.g. `https://192-168-0-123.homey.homeylocal.com`, and the Athom account API returns a `localUrlSecure` per Homey.
- Cloud access goes through `api.athom.com` with a delegation JWT (audience `homey`) that creates a session on the specific Homey.
- Local keys and cloud tokens aren't interchangeable. The official `node-homey-api` library hides local vs cloud from the app.
- Sources: [Homey Web API docs](https://api.developer.homey.app/llms-full.txt), [Homey HTTP spec](https://api.developer.homey.app/http-and-socket.io/http-specification), [Homey support](https://support.homey.app/hc/en-us/articles/8178797067292), [community CLI notes](https://backiee.wasmer.app/https_github_com/maxsumrall/homeycli) (search extracts; I couldn't confirm the certificate details).

**Ubiquiti UniFi**
- Remote management through Site Manager (`unifi.ui.com`) is on by default. It fails when the console's clock is wrong (blocked UDP 123) or UniFi OS is outdated — [Ubiquiti Help Center](https://help.ui.com/hc/en-us/articles/115012240067-UniFi-How-to-enable-remote-access-for-remote-management).
- Users report that the remote session is WebRTC (STUN on UDP 3478, plus TCP 80/443 and UDP 443) from an outbound console connection, with no port forwarding. This is forum-level evidence — [TrueNAS forum](https://www.truenas.com/community/threads/unifi-controller-non-plugin-no-remote-access.94163/post-727647).

**Synology QuickConnect**
- Three stages, tried in order (per a guide citing Synology's white paper):
  1. LAN/WAN direct connection to the addresses the NAS registered.
  2. Hole punching, which performs close to DDNS with port forwarding.
  3. Relay through Synology servers, which is slow and bandwidth-limited.
- [WunderTech guide](https://www.wundertech.net/synology-quickconnect-ultimate-setup-guide/); Synology's own page confirms the global cloud infrastructure and hole punching — [Synology network management](https://www.synology.com/de-de/dsm/feature/network_management).

**Tailscale**
- Tries a direct WireGuard path first. If that's impossible it uses peer relays in the tailnet, then DERP servers.
- DERP also carries the DISCO messages used to negotiate a direct path, and clients keep rechecking so that a relayed connection can upgrade to direct.
- The home DERP is chosen by latency, with failover to the next closest region. DERP forwards traffic that is already encrypted.
- `tailscale status` shows `direct` / `relay` / `peer-relay` per peer.
- [Tailscale docs: DERP servers](https://tailscale.com/kb/1232/derp-servers) (search extract).

### Inferences
- What generalises:
  - **(a)** Prove "I can reach the right home server" by an authenticated probe, not by "I'm on SSID X". HA's SSID model needs location permission, breaks on duplicate SSIDs, and once (CVE-2026-55844) leaked tokens over plaintext.
  - **(b)** Have the cloud hand out the candidate endpoints (Plex `/api/resources`, Homey `localUrlSecure`). The connector registers its LAN addresses and certificate fingerprint with the Azure backend, so clients never need manual URLs.
  - **(c)** Order candidates direct → relay and keep probing in the background so the client can upgrade back to direct (Tailscale, Synology).
  - **(d)** Make local TLS cheap and valid with a per-installation wildcard under an owned domain (Plex/Homey), or a private CA plus CN = device-ID check (Hue).
  - **(e)** One client library hides the path from the app (node-homey-api, Q42.HueApi).
- Spine's situation is close to Homey's: one owner, a local key/token and a cloud JWT. The difference is that the brief wants the same token on both paths, which Homey doesn't do (see Q5).
- Never let the client fall back to a plaintext local URL when it isn't sure it is at home. Choose between paths by asking "is this the connector I paired with?", never by a guess about the network.

### Gaps
- I couldn't read the full HA Companion docs or source (blocked), so the exact Android setting names (e.g. "Prioritize internal URL") and iOS fallback ordering come only from search extracts.
- I couldn't confirm whether Plex clients race connections in parallel. Community lore says they test all candidates simultaneously, but no primary source was found.
- I didn't read Ubiquiti's WebRTC signalling internals or Synology's white paper directly.

## 2. Local discovery from .NET MAUI: mDNS/DNS-SD libraries, native APIs, iOS local network privacy, Android local network permission, Mac Catalyst

### Takeaway
On Android 17 (API 37), local network access is a runtime permission (`ACCESS_LOCAL_NETWORK`) for apps that target SDK 37+. On Android 16 it is opt-in, and Play only requires target 36 by 31 Aug 2026, so a MAUI app can defer this but must plan for it. On iOS, Bonjour discovery needs `NSLocalNetworkUsageDescription` and `NSBonjourServices`. Raw-socket mDNS libraries need the restricted multicast entitlement, so use the system browser (NWBrowser/NSNetServiceBrowser, or Zeroconf's built-in NSNetServiceBrowser fallback). Of the .NET libraries, Zeroconf (3.7.16, Dec 2024, has `net8.0-ios18.0`/`maccatalyst` targets) is the most MAUI-aware. Tmds.MDns is the most actively released (0.10.1, Sep 2026). Makaretu forks are stale.

### Cited Findings
**Android local network protection (LNP)**
- **Android 16 (API 36):** opt-in only. During the opt-in phase access is gated by `NEARBY_WIFI_DEVICES`. Test with `adb shell am compat enable RESTRICT_LOCAL_NETWORK <pkg>` and a reboot. During opt-in, APIs that run outside the app process, such as NsdManager, aren't affected — [Android: Local network permission](https://developer.android.com/privacy-and-security/local-network-permission).
- **Android 17 (API 37):** enforced by default for apps targeting SDK 37+. The new permission `ACCESS_LOCAL_NETWORK` is in the `NEARBY_DEVICES` group; declare it and request it at runtime. Apps targeting 36 or lower get an implicit grant with INTERNET, and Google says not to add `ACCESS_LOCAL_NETWORK` yet — [same page](https://developer.android.com/privacy-and-security/local-network-permission).
- **What it blocks:**
  - Outgoing TCP, accepting incoming TCP, and sending or receiving UDP (unicast, multicast, broadcast) on the LAN.
  - Resolving `.local` names, NsdManager, and raw-socket mDNS/SSDP.
  - It applies to all stacks: platform sockets, managed code, Cronet, OkHttp.
- **Exemptions:** DNS to a LAN resolver on port 53, the Output Switcher, and WebViews (which inherit the app's state). Internet and mobile traffic are unaffected.
- **Failure modes:** TCP usually shows up as a timeout (confirm with NDK `android_getnetworkblockedreason()` → `ANDROID_NETWORK_BLOCKED_REASON_LNP`); UDP returns `EPERM` — [same page](https://developer.android.com/privacy-and-security/local-network-permission).
- **Paths without the broad permission:** `NsdManager.registerServiceInfoCallback` with `DiscoveryRequest.Builder("_http._tcp").setFlags(DiscoveryRequest.FLAG_SHOW_PICKER)` shows a system picker, and the returned `getHostAddresses()` can be connected to without `ACCESS_LOCAL_NETWORK`.
- **Persistent home automation/IoT access:** Google's guidance is Path B, the runtime permission. If the user already granted another NEARBY_DEVICES permission they aren't prompted again, and the platform resets the denial counter for `ACCESS_LOCAL_NETWORK` — [same page](https://developer.android.com/privacy-and-security/local-network-permission).
- **Play deadlines:** new apps and updates must target API 36 by 31 Aug 2026. Android TV apps have a separate, lower schedule; a third-party summary says API 34 — [Play Console Help: target API](https://support.google.com/googleplay/android-developer/answer/11926878), [Android dev: target SDK](https://developer.android.com/google/play/requirements/target-sdk), [third-party summary](https://dev.to/mr_manushukla/android-target-api-36-is-due-aug-31-2026-api-37-is-next-the-play-store-migration-checklist-2f9i).
- **Zeroconf on Android:** the caller must acquire a `WifiManager.MulticastLock` and declare `ChangeWifiMulticastState` — [Zeroconf README](https://github.com/novotnyllc/Zeroconf).

**Apple local network privacy (TN3179, revised 2026-10-06)**
- **Platforms:** iOS/iPadOS 14+, macOS 15+, visionOS. Not on tvOS or watchOS. MDM can't configure it — [TN3179](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy).
- **What counts as local:**
  - A local network is one on a broadcast-capable interface (Wi-Fi, Ethernet), **not cellular or VPN**. All multicast addresses and 255.255.255.255 also count.
  - **Needs access:** outgoing TCP, UDP unicast/multicast/broadcast sends, and connecting a UDP socket.
  - **No access needed:** listening for and accepting TCP.
  - The checks sit deep in the stack, so they apply to Network framework, BSD sockets, URLSession and everything built on them — [TN3179](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy).
- **Exceptions:** a DNS server or proxy on the LAN, and traffic from WKWebView, SFSafariViewController and Safari. Resolving `.local` names and every Bonjour operation (register, browse, resolve) need access. AirPlay, UIKit printing, DeviceDiscoveryUI and AccessorySetupKit don't.
- **Info.plist:** add `NSLocalNetworkUsageDescription`, and list Bonjour types in `NSBonjourServices`.
- **Multicast entitlement:** on iOS, sending or receiving multicast/broadcast, browsing arbitrary types, or browsing `_services._dns-sd._udp` needs `com.apple.developer.networking.multicast`. Apple must approve it, and it **isn't required on macOS** — [TN3179](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy), [multicast entitlement](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.networking.multicast).
- **Behaviour to design for:**
  - When the alert first appears, the system may deny the operation at once, before the user answers. Use Network framework or URLSession with `waitsForConnectivity`, or add retries.
  - An app in the **background** with an undetermined state is denied silently and without a prompt (the decision isn't recorded).
  - The simulator doesn't implement the feature; test on a device.
  - There's no API to query the state.
  - Detect denial with `NWBrowser` in `.waiting(.dns(kDNSServiceErr_PolicyDenied = -65570))`, or `NWConnection` in `.waiting` with `currentPath?.unsatisfiedReason == .localNetworkDenied`. The connection retries automatically once access is granted.
  - Workaround to trigger the alert: connect a UDP socket to a link-local IPv6 address, which sends no traffic.
  - Don't hard-code `en0`.
  - [TN3179](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy).
- **macOS specifics** (these apply to Mac Catalyst apps, which are macOS processes):
  - State is kept per user account.
  - It tracks identity by code signature, so sign with an Apple-issued identity, and the main executable needs a unique UUID.
  - Since macOS 15.5, admins can exempt networks with `defaults write com.apple.network.local-network AllowedWiFiLocalNetworkAddresses/AllowedEthernetLocalNetworkAddresses` (CIDR, needs a reboot).
  - Remove/reset buttons for the privilege arrived in macOS 27.2.
  - [TN3179](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy).

**.NET libraries (NuGet registration data read 2026-10-30)**
- **Zeroconf:**
  - Latest is 3.7.16 (published 2024-12-17), with targets net48, netstandard2.0, Xamarin.iOS, net6.0, net8.0, `net8.0-ios18.0` and `net8.0-maccatalyst18.0` — [NuGet Zeroconf](https://www.nuget.org/packages/Zeroconf).
  - On iOS 14.5+ it automatically falls back to `NSNetServiceBrowser`/`NSNetService` when the app lacks the multicast entitlement, so only the types listed in `NSBonjourServices` are visible.
  - With the entitlement, set `ZeroconfResolver.UseBSDSocketsZeroconfOniOS = true` — [Zeroconf README](https://github.com/novotnyllc/Zeroconf).
- **Tmds.MDns:** 0.10.1, published 2026-09-28 (0.9.1 Aug 2026, 0.10.0 Sep 2026), targets netstandard2.0/net8.0. It is a `ServiceBrowser` with `ServiceAdded/Removed/Changed` events on raw sockets, and its author says it isn't a full RFC 6762 querier/cache — [NuGet Tmds.MDns](https://www.nuget.org/packages/Tmds.MDns), [README](https://github.com/tmds/Tmds.MDns).
- **Makaretu forks:** Makaretu.Dns.Multicast stopped at 0.27.0 (Nov 2019). The fork Makaretu.Dns.Multicast.New is at 0.38.0 (Nov 2024, net8.0/net9.0) — [NuGet Makaretu.Dns.Multicast](https://www.nuget.org/packages/Makaretu.Dns.Multicast), [NuGet Makaretu.Dns.Multicast.New](https://www.nuget.org/packages/Makaretu.Dns.Multicast.New).
- **Plugin.Maui.NearbyConnectivity** (preview, `net10.0-ios26.0`) covers nearby device discovery and data exchange; it isn't a general mDNS browser — [NuGet](https://www.nuget.org/packages/Plugin.Maui.NearbyConnectivity).

**MAUI Connectivity internals (dotnet/maui main)**
- **iOS/macOS:** uses `NWPathMonitor` plus `CTCellularData`, with a 100 ms delay/poll after path changes before raising `ConnectivityChanged` — [Connectivity.ios.tvos.macos.reachability.cs](https://github.com/dotnet/maui/blob/main/src/Essentials/src/Connectivity/Connectivity.ios.tvos.macos.reachability.cs).
- **Android:** registers a `ConnectivityManager.NetworkCallback` (plus a legacy broadcast receiver). It maps `NET_CAPABILITY_VALIDATED` to `NetworkAccess.Internet`, and INTERNET without VALIDATED (e.g. a captive portal) to `NetworkAccess.ConstrainedInternet` — [Connectivity.android.cs](https://github.com/dotnet/maui/blob/main/src/Essentials/src/Connectivity/Connectivity.android.cs).

### Inferences
- **Discovery is an optimisation, not the base mechanism.** The cloud already knows the connector's LAN IPs and certificate fingerprint (registered by the connector). The client can connect directly to the cached LAN endpoint, which on Android 17 still needs `ACCESS_LOCAL_NETWORK` and on iOS still triggers the local network alert. Use mDNS only to refresh a changed IP. That avoids the multicast entitlement, and on Android 16 it avoids multicast locks.
- **iOS / Mac Catalyst setup:** add `NSLocalNetworkUsageDescription`, `NSBonjourServices = [_spine-connector._tcp]` and `NSAllowsLocalNetworking` (see Q3). Do discovery in the foreground, after explaining it to the user (onboarding step "Find your home connector"), so the first prompt happens in context and not in the background, where it is silently denied.
- **Mac Catalyst entitlements:** the sandbox needs the outgoing-connections entitlement (`com.apple.security.network.client`). That is from my own knowledge, not verified this session. The multicast entitlement isn't needed on macOS.
- **Android MAUI app:**
  - Today (target 36): no permission needed.
  - When moving to target 37: declare `ACCESS_LOCAL_NETWORK`, request it in onboarding, and treat denial as "cloud only" (still functional).
  - Detect LNP blocking as TCP timeouts, which feed into the router as "local unhealthy". No special case is needed beyond surfacing a hint in settings.
- **Which library:** use Zeroconf when one cross-platform .NET library must run on iOS without the entitlement. Otherwise call the native NWBrowser/NsdManager directly through MAUI platform code. The Android 17 `FLAG_SHOW_PICKER` flow is a native API that a .NET library is unlikely to wrap yet.

### Gaps
- I couldn't read Microsoft Learn (blocked), so the public `Connectivity` API surface (e.g. `ConnectionProfiles`) was inferred from source and a training module.
- I didn't verify whether Android 17 shipped broadly on phones and TV devices by Oct 2026, or the exact Play deadline for API 37 (one third-party source expects Aug 2027 — [dev.to](https://dev.to/mr_manushukla/android-target-api-36-is-due-aug-31-2026-api-37-is-next-the-play-store-migration-checklist-2f9i)).
- I didn't check whether .NET MAUI bindings expose `DiscoveryRequest`/`FLAG_SHOW_PICKER` (API 37) yet.

## 3. Local TLS options and trade-offs (pinning, private CA, real domain + DNS-01 / wildcard like plex.direct, plain HTTP + app-level signing); iOS ATS, Android network security config, ESP32 cost

### Takeaway
The strongest option for all three clients is a real hostname under an owned domain, with a publicly trusted certificate obtained by the connector through DNS-01, ideally Plex/Homey style (`<ip-dashed>.<installId>.home.<domain>` resolving to the LAN IP, or split-horizon). It passes iOS ATS, the Android 17 Certificate Transparency default, and the ESP-IDF certificate bundle without app-specific trust code. The fallback is a self-signed or private-CA cert pinned per installation (Hue model), with the fingerprint distributed through the cloud at pairing. Plain HTTP on the LAN is a regression on every platform: Android blocks cleartext by default since API 28, ATS needs exceptions, and HA's CVE shows the risk. On the ESP32-S3, one mbedTLS session costs about 42 KB of heap by default and about 22 KB with dynamic buffers. Put mbedTLS in PSRAM, since internal RAM (~75 KB free, 36 KB largest block) can't safely hold two sessions.

### Cited Findings
**iOS ATS**
- `NSAllowsLocalNetworking` lets the app reach unqualified names, `.local` names and IP addresses.
- In iOS 10–16 those were allowed by default.
- **In iOS 17 / macOS 14, ATS "no longer allows connections to IP addresses by default"**; add individual IPs or CIDR ranges under `NSExceptionDomains`.
- Apple recommends setting `NSAllowsLocalNetworking = YES` as a statement of intent. Combined with `NSAllowsArbitraryLoads` it covers older OS versions without a global bypass on newer ones.
- [Apple: NSAllowsLocalNetworking](https://developer.apple.com/documentation/bundleresources/information-property-list/nsapptransportsecurity/nsallowslocalnetworking).

**Android network security config**
- Cleartext has been off by default since API 28. Enable it per domain with `<domain-config cleartextTrafficPermitted="true"><domain includeSubdomains="true">…</domain>`. The page doesn't document IP or CIDR matching in `<domain>` — [Android: Network security config](https://developer.android.com/privacy-and-security/security-config).
- **Custom CA:** `<trust-anchors><certificates src="@raw/my_ca"/></trust-anchors>` per domain. Since API 24, user-installed CAs aren't trusted unless `<certificates src="user"/>` is listed.
- **Pinning:** `<pin-set expiration="yyyy-MM-dd"><pin digest="SHA-256">…</pin>` holds SPKI SHA-256 pins. Always include a backup pin. After the expiration date pinning is skipped.
- **New in Android 16/17:**
  - Certificate Transparency is opt-in on 16 and **on by default on 17 (API 37)**. It isn't checked for custom trust anchors.
  - Encrypted Client Hello is on by default for targetSdk 37 (`<domainEncryption>`).
  - Localhost gets an implicit cleartext-allowed config on API 37+.
  - [Android: Network security config](https://developer.android.com/privacy-and-security/security-config).

**Wildcard-to-LAN-IP model**
- The Plex model: `*.HASH.plex.direct` wildcard, IP encoded in the name, per-server hash, so the key can be handed to the user — [Valsorda](https://words.filippo.io/how-plex-is-doing-https-for-all-its-users/) (search extract). Homey uses the same pattern with `*.homey.homeylocal.com` — [Homey docs](https://api.developer.homey.app/llms-full.txt) (search extract).
- **Known failure mode:** router DNS-rebinding protection drops public names that resolve to RFC 1918 addresses — [Plex forum](https://forums.plex.tv/t/tls-unknown-player-error-and-plex-direct/772754/4).

**Pinned-device model (Hue)**
- Use the device ID as hostname with a custom resolver to the IP, check the CN against the ID, and use TOFU pinning for self-signed certificates — [Homey community quoting Hue docs](https://community.homey.app/t/philips-hue-dynamische-scenes-starten-met-logic-http-request/67913).

**ESP32 (ESP-IDF master docs; the v5.5 Kconfig has the same dynamic-buffer constraints)**
- Heap for the `https_request` example with server validation:
  - Default: **42,196 B**
  - `CONFIG_MBEDTLS_SSL_VARIABLE_BUFFER_LENGTH`: 42,120 B
  - Without `CONFIG_MBEDTLS_SSL_KEEP_PEER_CERTIFICATE`: 38,533 B
  - With `CONFIG_MBEDTLS_DYNAMIC_BUFFER` + `CONFIG_MBEDTLS_DYNAMIC_FREE_CONFIG_DATA` + `CONFIG_MBEDTLS_DYNAMIC_FREE_CA_CERT`: **22,013 B**
  - Cross-signed bundle verification adds about 1 KB peak during the handshake.
  - [ESP-IDF mbedtls.rst](https://github.com/espressif/esp-idf/blob/master/docs/en/api-reference/protocols/mbedtls.rst).
- **Content lengths:** `CONFIG_MBEDTLS_ASYMMETRIC_CONTENT_LEN` (default y) saves about 12 KB per connection. Defaults are `MBEDTLS_SSL_IN_CONTENT_LEN = 16384` and `MBEDTLS_SSL_OUT_CONTENT_LEN = 4096`.
- **Dynamic buffer constraints:** `MBEDTLS_DYNAMIC_BUFFER` can't be combined with DTLS or `SSL_VARIABLE_BUFFER_LENGTH`.
- **Allocation:** `CONFIG_MBEDTLS_MEM_ALLOC_MODE` offers internal (default), **external SPIRAM**, default malloc, custom or IRAM. Kconfig notes that on ESP32-S2/S3, SPIRAM is "also a safe choice from security perspective" when flash encryption is on, because the hardware encrypts external SPIRAM.
- Sources: [ESP-IDF mbedTLS Kconfig (master)](https://github.com/espressif/esp-idf/blob/master/components/mbedtls/Kconfig), [v5.5 Kconfig](https://github.com/espressif/esp-idf/blob/release/v5.5/components/mbedtls/Kconfig).
- **Peak heap:** Espressif says dynamic buffers greatly cut peak heap for TLS at a small performance cost — [Espressif memory case study](https://developer.espressif.com/blog/esp32-memory-analysis-case-study/).
- **Measuring free memory:** `esp_get_free_heap_size()` includes PSRAM, so measure internal RAM separately — [ESP FAQ mbedTLS](https://docs.espressif.com/projects/espressif-esp-faq/en/latest/software-framework/protocols/mbedtls.html) (search extract).
- **Certificate bundle:** the full Mozilla bundle has more than 130 roots (master bundle updated 20 Oct 2026). A "common" subset of about 35 roots covers about 99% of market share. Selected with `CONFIG_MBEDTLS_DEFAULT_CERTIFICATE_BUNDLE`, attached via `crt_bundle_attach = esp_crt_bundle_attach` — [esp_crt_bundle.rst](https://github.com/espressif/esp-idf/blob/master/docs/en/api-reference/protocols/esp_crt_bundle.rst).
- **ESPHome's answer for LAN device↔HA:** no TLS at all. The native API uses the **Noise** framework (`Noise_NNpsk0_25519_ChaChaPoly_SHA256`) with a 32-byte base64 PSK, and the legacy password option is being removed — [HA ESPHome integration docs](https://www.home-assistant.io/components/fan.esphome/), [esphome-client Noise impl](https://cdn.jsdelivr.net/npm/esphome-client@1.3.1/dist/crypto-noise.d.ts), [Ava-Pro wiki](https://github.com/knoop7/Ava-Pro/wiki/ESPHome-Encryption).

### Inferences
- **Recommended for the connector (Pi 5 / NAS):**
  - It runs an ACME DNS-01 client against a zone the owner controls (Azure DNS works naturally with the Azure backend) and gets a wildcard `*.<installId>.home.<domain>`.
  - It publishes A/AAAA records for `<ip-dashed>.<installId>.home.<domain>` (or one `connector.<installId>.home.<domain>` → current LAN IP).
  - Clients then use normal TLS validation, pass ATS without IP exceptions, and pass Android 17 CT (Let's Encrypt certificates carry SCTs).
  - Let's Encrypt wildcards require DNS-01. That is general knowledge, not verified this session.
- **DNS-rebinding fallback:** the .NET client can bypass router DNS by connecting to the known LAN IP while still validating the hostname. Use `SocketsHttpHandler.ConnectCallback` to dial the IP and keep the URI host for SNI and certificate checks; that is the .NET equivalent of Hue's custom-resolver advice (my knowledge of the .NET API, not verified this session). On iOS, MAUI's default `NSUrlSessionHandler` doesn't expose a connect callback, so pick the handler per platform deliberately.
- **Fallback for installations without a domain:** a connector-generated ECDSA P-256 self-signed cert, with its SPKI hash registered to the cloud at pairing. Clients pin it with a custom `ServerCertificateCustomValidationCallback` (.NET), `pin-set` (Android native TV app), or by passing `cert_pem` to `esp_websocket_client`. Rotation: publish the new pin through the cloud before switching (backup pin).
- **Avoid plain HTTP.** Android needs a cleartext exception, which can't easily be scoped to "any LAN IP". iOS 17+ needs IP exceptions. The HA CVE shows the token-leak risk.
- **ESP32 budget:** with about 75 KB free internal RAM and a 36 KB largest block, a default session (~42 KB, including a contiguous 16 KB IN buffer) is at the edge.
  - Set `CONFIG_MBEDTLS_EXTERNAL_MEM_ALLOC=y`, which is safe on S3 with flash encryption, plus `CONFIG_MBEDTLS_DYNAMIC_BUFFER=y`.
  - Keep **one** long-lived TLS WebSocket rather than parallel HTTPS calls.
  - Use the "common" certificate bundle, or pin only the Azure front-door/Let's Encrypt roots with `cert_pem`.
  - Use ECDSA certificates on the connector to keep handshake cost down.
- **ESP32 on the LAN:** either TLS to the connector's Let's Encrypt name (same code path as cloud, only the host differs) or ESPHome-style Noise-PSK over plain TCP/WS (cheaper, and HA-ecosystem-proven). The first keeps one code path and is preferred if PSRAM allocation works.

### Gaps
- I found no measured figure for esp_websocket_client + mbedTLS over WSS on ESP32-S3 specifically. The 42 KB/22 KB figures are for the HTTPS example.
- `MBEDTLS_SSL_MAX_FRAGMENT_LENGTH` negotiation (to shrink the 16 KB IN buffer) depends on server support. I didn't verify whether Azure Front Door, Azure Functions or Kestrel honour it.
- I couldn't fetch Let's Encrypt docs (rate limits and wildcard rules), so those statements rest on general knowledge.
- The ATS page's wording, that iOS 17 doesn't allow IP addresses by default, sits alongside a note that ATS doesn't block local loads by default on newer OSes. The two read as inconsistent and should be verified with a device test.

## 4. Routing algorithm: racing, health probes, reachability events, sticky choice, hysteresis, handover, captive portals, VPN, in-flight safety, resync after a switch

### Takeaway
Use an event-driven, happy-eyeballs-style race with a local head start. Network-change events from NWPathMonitor or the Android NetworkCallback (both behind MAUI `Connectivity`) only trigger re-evaluation; they never decide the path. Each candidate gets an authenticated health probe with a short timeout. Prefer local when it answers within a small grace window. Keep the choice sticky and leave local only on failure. Re-probe local in the background while on cloud, and need N consecutive successes before switching back. Commands carry an `Idempotency-Key` so they can be retried on the other path. Live state is handed over make-before-break: open the new hub, then fetch a snapshot or diff since a sequence number, then drop the old one.

### Cited Findings
- **Happy eyeballs (RFC 8305):** the recommended "Connection Attempt Delay" is 250 ms, with a floor of 10 ms. Squid, Privoxy and IO::Socket::HappyEyeballs all default to 250 ms — [Squid docs](https://www.squid-cache.org/Doc/config/happy_eyeballs_connect_timeout), [IO::Socket::HappyEyeballs](https://metacpan.org/pod/IO::Socket::HappyEyeballs). These are secondary sources; I didn't read the RFC itself.
- **Android signals:**
  - `registerDefaultNetworkCallback` gives `onAvailable`, `onCapabilitiesChanged`, `onLinkPropertiesChanged` and `onLost`. Don't call synchronous getters inside `onAvailable`; wait for `onCapabilitiesChanged`/`onLinkPropertiesChanged`, which follow immediately on API 26+.
  - `NET_CAPABILITY_VALIDATED` means the network was probed and reaches the internet. Behind a captive portal the network has INTERNET + `CAPTIVE_PORTAL` but not VALIDATED.
  - `NET_CAPABILITY_NOT_VPN` and `TRANSPORT_VPN` are available, and a VPN network can carry several transports at once.
  - Google warns that transport is a weak proxy for quality.
  - [Android: Reading network state](https://developer.android.com/develop/connectivity/network-ops/reading-network-state).
- **MAUI `Connectivity`:** wraps NWPathMonitor on Apple (with a 100 ms settle delay) and NetworkCallback on Android. Captive portals map to `NetworkAccess.ConstrainedInternet` — [dotnet/maui Connectivity sources](https://github.com/dotnet/maui/tree/main/src/Essentials/src/Connectivity).
- **Apple:** VPN and cellular interfaces aren't "local network" for privacy purposes, so traffic to home over a VPN tunnel doesn't need the local network privilege. Packet-tunnel/app-proxy extensions always have local network access — [TN3179](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy).
- **Idempotency:** the IETF "Idempotency-Key" HTTP header draft (httpapi WG) makes POST/PATCH fault-tolerant. draft-07 was dated 2025-10-15, a later revision was hinted at, and I found no RFC by Oct 2026 — [IETF Datatracker](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/).
- **SignalR .NET client:** `WithAutomaticReconnect()` defaults to retry delays of 0, 2, 10 and 30 s, then stops — [aspnetcore DefaultRetryPolicy.cs](https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/clients/csharp/Client.Core/src/Internal/DefaultRetryPolicy.cs).
- **Stateful reconnect (.NET 8+):** `WithStatefulReconnect()` lets a client resume a briefly dropped connection without losing messages — [Microsoft Learn API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.signalr.client.hubconnectionbuilderhttpextensions.withstatefulreconnect).
- **Azure SignalR Service:** supports stateful reconnect with a 30 s window for missed messages. The server option `ServiceOptions.AllowStatefulReconnects` is off by default. A full app restart gets a new connection ID — [Azure SignalR client disconnections](https://docs.azure.cn/en-us/azure-signalr/signalr-concept-client-disconnections), [azure-signalr ServiceOptions.cs](https://github.com/Azure/azure-signalr/blob/dev/src/Microsoft.Azure.SignalR/ServiceOptions.cs).
- **How others upgrade back to direct:** Tailscale keeps probing in the background and upgrades relayed connections to direct when possible — [Tailscale DERP docs](https://tailscale.com/kb/1232/derp-servers). Synology tries direct, then hole-punching, then relay, in order — [WunderTech](https://www.wundertech.net/synology-quickconnect-ultimate-setup-guide/).

### Inferences
Recommended algorithm. This is my design, built from the patterns above:

1. **Candidates:** `Local` (cached `https://connector.<installId>.home.<domain>:8443`, falling back to the last LAN IP via a connect-callback, refreshed by mDNS `_spine-connector._tcp`) and `Cloud` (`https://api.<domain>`). Optionally `Vpn` when a tunnel interface exists; that is the same local URL, but note that the LAN IP is reachable via VPN without the LNP prompt on iOS.
2. **Triggers:**
   - App start or resume.
   - A `Connectivity.ConnectivityChanged` / `ConnectionProfiles` change. Debounce about 500 ms on top of MAUI's 100 ms, because Wi-Fi↔cellular handover fires several events.
   - A request failure on the current path.
   - A background timer: every 60 s while on Cloud and the device has a Wi-Fi/Ethernet profile, and never on cellular-only.
3. **Probe:** `GET /v1/hello?nonce=…`. The connector answers `{installId, connectorVersion, serverTime, sig}`, where sig is a signature with the connector key (pinned at pairing) or the TLS identity is enough. This verifies the right home, never just a 200 from any host on the same SSID. Timeouts: local connect 700 ms and total 1.5 s; cloud 5 s.
4. **Race:** start the local probe at t=0 and the cloud probe at t=250 ms (RFC 8305-style head start). If cloud wins first, wait a further grace of about 300 ms for local before committing. Commit to the best healthy path by priority Local > Vpn > Cloud.
5. **Sticky and hysteresis:**
   - Stay on the chosen path until a hard failure: connect refused, TLS failure, two consecutive timeouts, or the WebSocket closing without reconnecting within 2 s.
   - Local → Cloud is immediate on hard failure.
   - Cloud → Local needs 2 consecutive successful background probes at least 10 s apart, then a make-before-break hub switch.
   - Never flip more than once per 30 s unless the current path is down.
6. **Captive portal:** if `NetworkAccess == ConstrainedInternet` (no VALIDATED), still probe Local, because a hotel network won't have the connector and a home network can be "unvalidated" when the WAN is down. Don't probe Cloud until the network validates, and show "network needs sign-in" rather than an error.
7. **Home internet down:** the Local probe succeeds and Cloud fails. Local still works only if auth works offline (see Q5); this is the main reason for offline JWT validation plus a long-lived local credential.
8. **In-flight commands:**
   - Every mutating call (`POST /v1/scenes/{id}:activate`, `POST /v1/services/{domain}/{service}`) carries `Idempotency-Key: <uuid>` and an optional `X-Request-Deadline`.
   - The connector keeps a dedupe table (key → result, TTL about 10 min; the commands are short-lived home actions), and the cloud relay forwards the header unchanged.
   - On failure, the client retries the **same key** once on the alternate path. A completed command returns its cached result, a running one returns 409/202 with status, and an unknown one executes.
   - GET calls are retried freely. Non-idempotent streaming (voice) isn't retried; it is restarted.
9. **Live state resync:**
   - Every hub event carries a monotonically increasing `seq` per connector (the connector is the single source, so local and cloud relay see the same `seq`).
   - After switching, the client opens the new hub, sends `Subscribe(lastSeq)`, and gets either the missed events from a connector ring buffer (e.g. the last 1,000 events) or `Snapshot{seq, entities}` when it fell too far behind.
   - Then it closes the old hub and drops duplicates by `seq`.
   - Stateful reconnect covers brief drops on the same path, but it doesn't help across paths because the server is different.
10. **SignalR reconnect policy:** use a custom `IRetryPolicy` that, instead of stopping after 30 s, defers to the router. The router may pick the other path and build a new connection.

### Gaps
- I didn't read RFC 8305 directly; the 250 ms value is from implementations that cite it.
- I don't know whether Azure SignalR **serverless** mode (used with Azure Functions bindings) supports stateful reconnect. The docs found refer to the Service SDK/default mode.
- I didn't verify how iOS reports Wi-Fi→cellular handovers for an established WebSocket (e.g. URLSession's `NSURLErrorNetworkConnectionLost` timing).
- I found no published, field-tested numbers for probe timeouts or hysteresis in HA, Plex or Hue. The values above are design proposals.

## 5. One contract over two transports, one token valid on both, and a .NET client library that hides the path

### Takeaway
Make the cloud a transparent relay of the connector's own API: identical REST paths under `/v1`, an identical hub protocol (same method names, payloads and `seq`), and the same error model. The connector validates cloud-issued JWTs offline with cached signing keys, which needs a long "last known good" key lifetime because Microsoft.IdentityModel defaults to 12 h refresh and 1 h LKG. A device-bound local credential minted at pairing covers the case where home internet is down for longer than the access-token lifetime. The client library exposes a single `ISpineHome`-style interface over an `HttpMessageHandler` that rewrites the base address per request, plus a hub wrapper. App code never sees the path.

### Cited Findings
- **Microsoft.IdentityModel `BaseConfigurationManager` defaults:** `DefaultAutomaticRefreshInterval` = 12 h, `DefaultRefreshInterval` = 5 min, `DefaultLastKnownGoodConfigurationLifetime` = 1 h. Minimums are 5 min for automatic refresh and 1 s between `RequestRefresh` calls — [BaseConfigurationManager.cs](https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/BaseConfigurationManager.cs).
- **Homey keeps the two token types separate:** local API keys don't work as cloud tokens, and cloud access uses a delegation JWT with audience `homey` to open a session on a specific device — [Homey Web API docs](https://api.developer.homey.app/llms-full.txt), [homeycli notes](https://backiee.wasmer.app/https_github_com/maxsumrall/homeycli).
- **Hue does the same:** a local application key against the bridge versus OAuth tokens for the remote API — [Telegraf huebridge README](https://ftp.mirror.yandex.ru/mirrors/github/influxdata/telegraf/main/plugins/inputs/huebridge/README.md).
- **Client libraries that hide local vs cloud:** node-homey-api ("authenticating & connecting to a customer's Homey Pro or Homey Cloud") — [Homey Web API docs](https://api.developer.homey.app/llms-full.txt); Q42.HueApi has `LocalHueApi`/`RemoteHueApi` behind shared models — [Q42.HueApi](https://docsearch.algolia.com/mcp/docs/repo/michielpost/q42.hueapi).
- **Hub transport:** stateful reconnect is client opt-in (`WithStatefulReconnect`) and server opt-in (`AllowStatefulReconnects`) — [Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.signalr.client.hubconnectionbuilderhttpextensions.withstatefulreconnect), [azure-signalr ServiceOptions.cs](https://github.com/Azure/azure-signalr/blob/dev/src/Microsoft.Azure.SignalR/ServiceOptions.cs).

### Inferences
**Contract**
- OpenAPI for `/v1/*` lives in a shared project. The Azure Functions relay forwards method, path, query, body, `Authorization` and `Idempotency-Key` untouched to the connector over the connector's outbound WebSocket/SignalR tunnel, and returns the connector's status and body as they are. Cloud-only additions such as push registration and account management live under `/cloud/*`, never under `/v1`.
- **Hub:** use one protocol (e.g. a JSON SignalR hub `HomeHub` with `Subscribe(lastSeq)`, `Event{seq,…}` and `Snapshot{…}`). Locally the connector hosts it with ASP.NET Core SignalR. In the cloud, Azure SignalR either fans out events the connector pushes up, or proxies the hub. Either way the payloads and `seq` come from the connector.
- **Prefer plain SignalR on both ends over raw WebSocket** so the client code is identical. If the ESP32 can't afford SignalR framing, expose an additional raw WebSocket endpoint `/v1/ws` with the same message schema; SignalR's JSON protocol over WebSockets is just a handshake plus `\x1e`-delimited frames, so the ESP32 could speak it directly.

**Tokens**
- **(1) Cloud JWT:** issued by the Azure identity provider, with audience `spine-home` and `home_id`/`install_id` claims. The connector validates it with JwtBearer against cached JWKS. Configure the `ConfigurationManager` with a long `LastKnownGoodLifetime` (e.g. 30 days) and persist JWKS to disk so a connector restart while offline still validates.
- **(2) Device-bound local credential:** created at pairing over the cloud. The connector issues a refresh-style token, stored in iOS Keychain, Android Keystore-backed storage or ESP32 NVS (encrypted). The client exchanges it locally at `POST /v1/auth/local-token` for a short-lived local JWT signed by the connector. This keeps home control working when the home internet has been down for days and the cloud access token has expired.
- The connector accepts either issuer. App code just calls `GetTokenAsync()`, and the token provider picks a cloud or local token to match the chosen path.

**.NET client shape (Spine style)**
- `services.AddSpineHome(o => { o.CloudBaseAddress = …; o.InstallId = …; })` registers:
  - `IHomeRouter`, a singleton that owns the probe/race/hysteresis state machine and exposes `CurrentPath` and `PathChanged` for diagnostics only.
  - A `RoutingHandler : DelegatingHandler` that rewrites `request.RequestUri` to the current base, adds the token and `Idempotency-Key`, and on a transport failure calls `router.ReportFailure()` and retries once on the next path.
  - A typed client `IHomeApi` (Refit-like or hand-written) over `HttpClient`.
  - `IHomeEvents` (an `IAsyncEnumerable<HomeEvent>` / observable) that survives path switches by doing make-before-break hub replacement inside.
- The app (pages, view models, widgets) depends only on `IHomeApi`/`IHomeEvents`. A settings page shows "Connected via home network / cloud" from `PathChanged`, which follows the settings-page conventions in `docs/wiki/settings-pages.md`.

### Gaps
- I didn't verify which Azure identity product (Entra External ID vs a custom token service in Functions) the system uses, or its token lifetimes.
- I didn't confirm whether Azure Functions SignalR bindings (serverless) can relay client→connector hub invocations, as opposed to only server→client broadcast. This decides whether the cloud hub can be "the same hub". If it can't, the cloud relay may need a small ASP.NET Core app (Container Apps) or Azure Web PubSub instead.

## 6. Android TV / Google TV launcher app in 2026: home-launcher rights, remote-mic voice input, .NET MAUI vs native Kotlin/Compose for TV

### Takeaway
On Google TV, a third-party app can't reliably become the default home screen. The Home button is hard-mapped to Google's launcher, and a 2025/2026 Google TV update reportedly broke the accessibility-service override that apps like Projectivy used. Build the app as a normal leanback app with an optional "replace home" mode only on devices that allow it (AOSP-style Android TV or operator builds; behaviour on the Shield and onn was unverified). The remote's mic and Assistant button belong to the system. A third-party app should start voice from its own UI (a focused mic button or D-pad action) and capture audio itself with RECORD_AUDIO, then stream it to the realtime AI. That remote-mic capture path must be tested per device. MAUI has no first-class TV/D-pad support, so for a 10-foot launcher UI, native Kotlin with Compose for TV is the lower-risk choice. The shared routing contract can be re-implemented, or shared as a .NET for Android library if the app stays .NET.

### Cited Findings
**Launcher / home rights**
- Users report that the Home button on Android TV/Google TV remotes is hard-mapped to Google's launcher and that alternative launchers can't be set as the default — [XDA](https://www.xda-developers.com/google-tv-bloated-replaced-custom-launcher-actually-works/), [Pocket-lint](https://www.pocket-lint.com/third-party-launcher-apps-on-android-tv/).
- A Google TV update appears to break Projectivy Launcher's ability to override the stock launcher; the app still opens manually — [Android Authority](https://www.androidauthority.com/google-tv-update-blocks-projectivy-override-3649445/) (search extract; date not confirmed), [Gadget Hacks](https://android.gadgethacks.com/news/google-tv-update-blocks-third-party-launcher-workaround/).
- Users describe the Projectivy override as "Override current launcher" plus enabling the app's **accessibility service** in Settings > System > Accessibility. Results vary by device, and some users still get the stock launcher on Home — [Android Police](https://www.androidpolice.com/remove-google-tv-ads/) and related reports (search extracts).
- The other workarounds are remapping a remote button or disabling the stock launcher over ADB (a legacy method) — [XDA forums](https://xdaforums.com/t/how-to-disable-google-tv-launcher-install-3rd-party-launcher.4601397/).
- **Platform rules:** devices that let third-party apps replace the home screen must declare `android.software.home_screen`, and TV devices must provide Home and Back functions — [Android 11 CDD](https://source.android.com/docs/compatibility/11/android-11-cdd).
- **TV app basics:** a TV app needs an activity with a `CATEGORY_LEANBACK_LAUNCHER` intent filter, `touchscreen required="false"`, a 320×180 banner and a 160×160 icon — [Android: Create and run a TV app](https://developer.android.com/training/tv/get-started/create), [TV app quality](https://developer.android.com/docs/quality-guidelines/tv-app-quality). From 1 Aug 2026, Play TV apps must support 32- and 64-bit and 16 KB page sizes — [Play multi-platform quality guidelines](https://support.google.com/googleplay/android-developer/answer/18058043?hl=en).

**Voice**
- On TV, `KEYCODE_ASSIST` opens the system voice UI. An AOSP change made `KEYCODE_SEARCH` open a text keyboard for mic-less remotes — [AOSP commit](https://android.googlesource.com/platform/frameworks/base/+/15cac2637e10).
- With the ATVV remote voice protocol, the mic button only sends an event to the TV. The Android host decides to send "mic open" and stream audio. A forum post adds that Android requires OEMs to let the mic be opened from the TV regardless of the button — [Nordic DevZone](https://devzone.nordicsemi.com/f/nordic-q-a/40938/audio-issue-with-atvv-service) (secondhand).
- On Fire TV, the mic/search button is a system event that apps can't intercept — [Amazon Fire TV remote input](https://developer.amazon.com/docs/fire-tv/remote-input.html).
- Platform `SpeechRecognizer` on TV needs RECORD_AUDIO (runtime since API 23). Leanback apps can supply a `SpeechRecognitionCallback` to run their own recognition flow — [Android Developers Blog 2015](https://android-developers.googleblog.com/2015/09/new-permissions-requirements-for.html) (old but still the documented mechanism).

**MAUI vs native**
- `Xamarin.AndroidX.Leanback` 1.2.0.4 binds androidx.leanback for net9.0/net10.0-android — [NuGet](https://www.nuget.org/packages/Xamarin.AndroidX.Leanback).
- Android's TV guidance: every interaction is by D-pad, every element needs a clear focus state, and focus order is set with Compose `FocusRequester` or View `nextFocus*` attributes — [Build adaptive apps for TV](https://developer.android.com/develop/adaptive-apps/guides/tv/build-adaptive-apps-for-tv).
- I found no MAUI-specific Android TV guidance. Uno Platform documents Android TV and remote focus support — [Uno: Android TV](https://platform.uno/docs/articles/features/android-tv.html).

**Local network on TV**
- LNP applies on Android 17+ for targetSdk 37+ — [Android LNP](https://developer.android.com/privacy-and-security/local-network-permission). Play's TV target requirement is lower than phones' (a third-party summary says API 34) — [dev.to summary](https://dev.to/mr_manushukla/android-target-api-36-is-due-aug-31-2026-api-37-is-next-the-play-store-migration-checklist-2f9i).

### Inferences
- **Product decision:** ship a leanback app (banner, `LEANBACK_LAUNCHER`), not a Home replacement.
  - On devices that allow it, add `<category android:name="android.intent.category.HOME"/>` and let the user pick it as home.
  - On Google TV, rely on "open on boot" behaviour (e.g. the `BOOT_COMPLETED` receiver and a launch-on-wake setting) and a remapped button. Don't promise "launcher" behaviour.
  - An accessibility-service override is fragile and may conflict with Play policy on accessibility API use (Play policy not verified this session).
- **Voice UX:** a focused on-screen "Talk" button. Long-press of D-pad centre starts push-to-talk; the app opens `AudioRecord` (VOICE_RECOGNITION source, 16 kHz mono) and streams PCM/Opus to the realtime voice backend.
  - Whether `AudioRecord` gets audio from the remote's mic without the user pressing the remote's mic button is device-dependent. ATVV suggests the host can open the mic, but this must be tested on Google TV Streamer, Chromecast with Google TV, Shield and onn.
  - If it fails, fall back to the system `SpeechRecognizer`/Assistant for text, or to a phone (the MAUI app) as the microphone.
- **Routing on TV:** a TV is almost always at home on Ethernet/Wi-Fi, so the router will practically always pick Local. Cloud matters only when the connector is down, or for the AI voice session if that runs in Azure.
- **Tech choice:** a launcher-grade 10-foot UI with focus rings, rows, D-pad and voice is much easier in Kotlin + Compose for TV. MAUI would need custom focus handling in handlers.
  - Keep the client contract (OpenAPI + hub schema) language-neutral so a Kotlin client can be generated.
  - If reuse of the .NET routing library matters more, use .NET for Android with native views (not MAUI) or MAUI plus platform focus code; budget extra time either way.

### Gaps
- I found no official Google statement or date on the Google TV launcher restriction change. Android Authority and Gadget Hacks are secondary sources and couldn't be read in full.
- Unverified: whether Nvidia Shield (Android TV, not Google TV UI) and onn devices still honour a user-chosen HOME app in 2026.
- No primary source on whether a foreground third-party app's `AudioRecord` captures the remote mic on Google TV devices. This needs a device test.
- I didn't verify the current Compose for TV (`androidx.tv`) release status, or whether Leanback is deprecated, this session.

## 7. ESP32-S3 device client: esp_http_client / esp_websocket_client, mDNS query, local vs cloud choice, NVS token storage, reconnects, realtime audio path and where tool calls should flow

### Takeaway
ESP-IDF has everything needed:
- `esp_websocket_client` has auto-reconnect (`reconnect_timeout_ms`, 10 s default), `network_timeout_ms`, ping/pong timeouts, `headers` for the token, and `cert_pem`/`crt_bundle_attach` for trust.
- `mdns_query_ptr` / `mdns_query_srv` / `mdns_query_a` (plus async variants) find the connector.

Internal RAM is the binding constraint. The recommended design is: the device keeps **one** WebSocket, preferably to the **local connector**; the connector holds the TLS session to the realtime AI and runs tool calls against Home Assistant next door. The cloud path (Azure, same protocol) is a fallback when the connector is unreachable. Proven ESP32-S3 realtime-voice projects use a WebSocket to a relay server rather than WebRTC on the device, because WebRTC on the device needs PSRAM, and OpenAI's own embedded WebRTC SDK was discontinued in April 2025.

### Cited Findings
**esp_websocket_client config fields (esp-protocols master)**
- Connection: `uri`, `host`, `port`, `path`, `transport`, `subprotocol`, `headers`, `user_agent`.
- Reconnects: `disable_auto_reconnect`, `enable_close_reconnect`, `reconnect_timeout_ms` (default 10 s), `network_timeout_ms` (default 10 s).
- Liveness: `ping_interval_sec` (default 10 s), `pingpong_timeout_sec`, `disable_pingpong_discon`, `keep_alive_enable/idle/interval/count`.
- Task and buffers: `buffer_size`, `task_stack`, `task_prio`, `task_core_id`.
- Trust: `cert_pem`/`cert_len`, `client_cert`/`client_key` (mTLS), `use_global_ca_store`, `crt_bundle_attach`, `cert_common_name`, `skip_cert_common_name_check`, and `if_name` to bind an interface.
- Error events report `esp_tls_last_esp_err`, `esp_tls_stack_err`, `esp_tls_cert_verify_flags`, `esp_ws_handshake_status_code` and `esp_transport_sock_errno`. Data events carry `payload_len`/`payload_offset` (a payload larger than the buffer arrives in several events) and the close status code.
- [esp_websocket_client.h](https://github.com/espressif/esp-protocols/blob/master/components/esp_websocket_client/include/esp_websocket_client.h).

**mDNS (esp-protocols)**
- `mdns_query_ptr(service_type, proto, timeout_ms, max_results, &results)`, `mdns_query_srv`, `mdns_query_txt`, `mdns_query_a(host, timeout, &esp_ip4_addr_t)`, `mdns_query_aaaa`, plus a non-blocking `mdns_query_async_new` / `mdns_query_async_get_results` / `mdns_query_async_delete`. Errors include `ESP_ERR_NO_MEM` and `ESP_ERR_INVALID_STATE` (mDNS not running) — [mdns.h](https://github.com/espressif/esp-protocols/blob/master/components/mdns/include/mdns.h).
- ESP-IDF's RAM guide points to the mDNS "Minimizing RAM Usage" section — [ram-usage.rst](https://github.com/espressif/esp-idf/blob/master/docs/en/api-guides/performance/ram-usage.rst).

**TLS cost:** see Q3 — about 42 KB default and about 22 KB with dynamic buffers, with an external SPIRAM allocation mode that is safe on S3 with flash encryption — [mbedtls.rst](https://github.com/espressif/esp-idf/blob/master/docs/en/api-reference/protocols/mbedtls.rst), [Kconfig](https://github.com/espressif/esp-idf/blob/master/components/mbedtls/Kconfig).

**Realtime voice on ESP32**
- OpenAI's ESP-IDF realtime embedded SDK (WebRTC via libpeer) was announced as ended on 2025-04-01 and was closer to a sample than an SDK — [zenn.dev write-up](https://zenn.dev/goroman/articles/3d6d51106179ce).
- ElatoAI runs speech-to-speech on an ESP32-S3 over **WebSockets** through an edge (Deno) relay server, with "No PSRAM required" — [OpenAI Cookbook: ElatoAI on ESP32](https://developers.openai.com/cookbook/examples/voice_solutions/running_realtime_api_speech_on_esp32_arduino_edge_runtime_elatoai).
- Espressif's ESP WebRTC solution lists PSRAM as a requirement — [elektroda.pl](https://www.elektroda.pl/rtvforum/topic4121950.html).
- A hobbyist ran LiveKit WebRTC on an ESP32-S3 (512 KB SRAM + PSRAM), with a server-side agent talking to OpenAI Realtime — [LiveKit community](https://community.livekit.io/t/i-put-livekit-on-an-esp32-s3-respeaker-xvf3800-a-full-duplex-smart-speaker-that-listens-while-it-talks/1560).

**HA-ecosystem precedent:** ESPHome voice devices talk to HA locally over the Noise-encrypted native API, with no TLS — [HA ESPHome docs](https://www.home-assistant.io/components/fan.esphome/), [esphome-client](https://cdn.jsdelivr.net/npm/esphome-client@1.3.1/dist/crypto-noise.d.ts).

### Inferences
**Path choice on the device (simple, deterministic)**
- At boot or Wi-Fi up:
  - **(1)** Try the NVS-cached local endpoint (host/IP + port + pin).
  - **(2)** If that fails within 1.5 s, run `mdns_query_ptr("_spine-connector", "_tcp", 1500, 4, &r)` and match the TXT `id=<installId>`.
  - **(3)** Otherwise use the cloud URL.
- While on cloud, re-try local every 60–120 s with a cheap TCP connect + hello. Switch back only between conversations, never mid-utterance.
- Use `disable_auto_reconnect=false` with `reconnect_timeout_ms` around 3,000 for the active path. After 3 failed reconnects, hand over to the selector.

**Credentials in NVS**
- Store `install_id`, the device refresh credential (from pairing via the MAUI app, BLE or SoftAP provisioning), the connector cert pin and the cloud URL.
- Enable NVS encryption together with flash encryption. Flash encryption also makes mbedTLS-in-PSRAM acceptable per Kconfig.
- Exchange the refresh credential for a short-lived access token at connect time (`Authorization` via the `headers` field), and keep the long-lived secret off the wire.

**Memory plan**
- `CONFIG_MBEDTLS_EXTERNAL_MEM_ALLOC=y` and `CONFIG_MBEDTLS_DYNAMIC_BUFFER=y`.
- One WSS connection with `buffer_size` 4–8 KB, with the audio frames and the task stack allocated in PSRAM where the APIs allow it.
- Avoid concurrent `esp_http_client` HTTPS calls during a voice session; send control calls (scene activation) as messages over the same WebSocket.
- If TLS to the connector proves too costly, use Noise-PSK over plain WS on the LAN, the ESPHome precedent, as the local-only variant.

**Where the AI runs and where tool calls flow (recommended)**
- **Device → connector (local WebSocket, PCM/Opus frames) → realtime AI provider** (the connector holds the provider WebSocket/WebRTC session and the API key) **→ tool calls are executed by the connector against HA locally → results go back to the model.**
  - This keeps API keys off the device and needs only one session on the device.
  - The lowest-latency path for HA actions (no cloud round trip) is also the one that keeps working with the home LAN only, while the model itself still needs internet.
  - The same `/v1/ws` voice protocol is exposed by the Azure relay. If the connector is unreachable (e.g. the device is used away from home or the Pi is down), the cloud side terminates the AI session itself and routes tool calls through the connector tunnel, or fails gracefully.
- **Rejected alternative:** device → cloud AI → connector. It is simpler when the AI orchestration lives in Azure, but every HA action makes a cloud round trip, and nothing works locally when Azure or the internet is down.
- **Rejected alternative:** device → connector directly for tools while the device talks to the AI cloud directly. That needs two sessions on the device (two TLS contexts), which the internal RAM budget argues against.
- **Android TV and MAUI voice:** they should use the same server-side voice endpoint (connector first, cloud fallback). The AI tool-calling logic then exists once, in the connector, with a thin copy in the cloud relay for the fallback.

### Gaps
- I found no measured RAM numbers for esp_websocket_client + WSS + Opus on ESP32-S3 with ~75 KB internal free, and no confirmation of which of its internal allocations can be steered to PSRAM.
- I didn't verify the current (late-2026) state of Espressif's WebRTC solution or of any successor to OpenAI's embedded SDK.
- I didn't check whether the chosen realtime AI provider allows a server-side relay holding the session (standard for WebSocket APIs, but provider terms and latency weren't checked).
