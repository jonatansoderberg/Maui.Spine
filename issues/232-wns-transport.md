# Issue #232 — Spine.Server: WNS-transport för Windows

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/232
**Branch:** issue/232-wns-transport
**Status:** Completed

## Plan

Serversidan kan nå APNs och FCM men inte WNS. Designen står i `docs/proposals/spine-push.md` §7.3 och §9.3: toasten ritas av WNS (`wns/toast` med XML som servern bygger), tysta meddelanden går som `wns/raw`, inloggning med Entra (client credentials), och kanal-URI:n är handle. Klienten är #233.

1. **`WindowsPushOptions`** (`TenantId`, `ClientId`, `ClientSecret`) och `SpinePushOptions.Windows(...)`, validerade som Apple och Android.
2. **`PushPayloads.Wns(notification, now)`**: toast-XML — `ToastGeneric` med titel, text och bild — där Spine-nycklarna ligger i `launch` i Windows App SDK:s argumentformat (`key=value;…`, med `%`, `;` och `=` kodade), så att klienten läser dem med `AppNotificationActivatedEventArgs.Arguments`. `PushPayloads.WnsSilent(data)`: raw med datan som JSON. Båda spärrade vid WNS gräns på 5000 byte. `PushEnvelope.WnsType` bär `wns/toast` eller `wns/raw`.
3. **`WnsTransport`**:
   - Token från `login.microsoftonline.com/<tenant>/oauth2/v2.0/token`, scope `https://wns.windows.com/.default`, cachad till strax före utgång. `401` från WNS → ny token och ett nytt försök.
   - POST till kanal-URI:n med `Authorization: Bearer`, `X-WNS-Type`, `Content-Type` (`text/xml` eller `application/octet-stream`) och `X-WNS-TTL` när meddelandet har en livslängd.
   - **Bara `https://*.notify.windows.com`.** Kanal-URI:n kommer från klientens registrering; skickades bearer-tokenen till vilken adress som helst kunde en falsk registrering fånga den. En annan adress blir `Invalid` utan att något skickas.
   - Svaren: `404`/`410` → `Invalid`, `406`/`503` → `Throttled`, resten `Failed` med `X-WNS-Error-Description` eller `X-WNS-Status`. Misslyckas tokenen blir varje leverans `Failed` med Entras förklaring.
4. **`PushSender`**: Windows får toast och raw. Live Activities, widgetar och broadcast finns inte på Windows och hoppas över.
5. **DI**: `WnsTransport` registreras när `Windows(...)` är konfigurerat.
6. **Tester** i samma stil som `ApnsTransportTests` och `FcmTransportTests`: tokenförfrågan och cache, huvudena för toast och raw, 401-försöket, svarskoderna, adresskontrollen, XML:en och argumenten, spärren, avsändaren och valideringen.
7. **Wikin**: `push-server.md` får Windows i uppsättningen; *Not in v1* säger att klienten är #233.

**Verifiering:** tester och bygge. Det finns ingen Windows-maskin eller Entra-app att prova mot.

## Open Questions

Inga — §7.3 avgör toast kontra raw.

## Changes

- `WindowsPushOptions` (`TenantId`, `ClientId`, `ClientSecret`) och `SpinePushOptions.Windows(...)`, validerade med namnet på det som saknas; `WnsTransport` registreras i DI när Windows är konfigurerat.
- `PushPayloads.Wns(notification, now)`: `ToastGeneric`-toast med titel, text och hero-bild, Spine-nycklarna i `launch` (`key=value;`, `%`/`;`/`=` kodade), `Expiration` från `TimeToLive`. `PushPayloads.WnsSilent(data)`: raw-JSON med `spine.kind=silent`. Båda spärrade vid 5000 byte. `PushNotification.Windows` justerar toast-elementet; `PushEnvelope.WnsType` bär typen.
- `WnsTransport`: Entra-token (client credentials, scope `https://wns.windows.com/.default`) cachad till fem minuter före utgång, `expires_in` läst som tal eller sträng; 401 → ny token och ett försök till, och en skur av 401 hämtar bara en; `X-WNS-Type`, `Content-Type`, `X-WNS-TTL`, ingen `Expect: 100-continue`; bara `https://*.notify.windows.com` på standardporten; 404/410 → `Invalid`, 406/503/429 → `Throttled`, annars `Failed` med `X-WNS-Error-Description`/`X-WNS-Status` och `X-WNS-Msg-ID`; en nekad token gör varje leverans `Failed` med Entras förklaring, utan hemligheten.
- `PushSender`: Windows får toast och raw; Live Activities, widgetar och broadcast hoppar över Windows.
- Tester: 28 nya — transporten (token, cache, 401, huvuden, TTL, svarskoder, adresskontroll, nekad token), payloads (XML, escaping, launch-argument, raw, spärren), avsändaren och valideringen. 217 servertester gröna.
- Sample-servern läser `Push:Wns:*` ur user-secrets när de finns.
- Wikin: `push-server.md` får `Windows(...)` i uppsättningen och avsnittet *Windows*; *Not in v1* säger att appdelen är #233.

## Verifiering

Tester och bygge. Ingen Windows-maskin eller Entra-app finns att prova mot; tokenflödet och WNS-anropen följer Microsofts dokumentation (*Push notification service request and response headers*, *Quickstart: Push notifications in the Windows App SDK*).

## Decisions

- **Bara `*.notify.windows.com` får tokenen.** Kanal-URI:n kommer från klientens registrering och bearer-tokenen följer med dit den pekar; utan kontrollen kunde vem som helst med registreringsrätt fånga serverns WNS-token. En annan adress blir `Invalid`, så registreringen städas bort.
- **En nekad token blir leveransfel, inte ett undantag.** Samma form som APNs och FCM: resultatet säger per installation vad som hände, med Entras förklaring.
- **Knappar ritas inte från kategorin.** Kategorins knappar deklareras i appen; servern känner dem inte. `PushNotification.Windows` lägger till dem i XML:en när det behövs.
- **Toast ritas av WNS, inte av appen** (§7.3). Den visas även när appen inte kör, vilket en opaketerad app — Orienteras — annars inte klarar: den får bara förgrundsleverans.
