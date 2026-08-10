# Issue #29 — Orientera M1 (app-sidan): offline-paket och felfallback

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/29
**Branch:** issue/29-offline-package
**Status:** Completed

## Plan

Bygga den del av M1 som inte är blockerad av SP-01: offline-paketet och felfallbacken, båda
ur M1:s DoD. Eventor-adaptern kräver kontakt med SOFT/Eventor och ingår inte.

## Changes

- `Domain/CompetitionPackage` — tävlingspaketet enligt krav 09: grunddata, PM/profil, min start
  och Min grupps starter, min anmälan, senast kända resultat och prognos.
- `Services/Offline/IOfflineStore` + `FileOfflineStore` — ett JSON-dokument per tävling under
  appens datakatalog, skrivet via temporärfil och flytt.
- `Services/Offline/OfflinePackageService` — läser live och sparar samtidigt, faller tillbaka
  på paketet när källan är nere, och `RefreshRelevantAsync` som uppdaterar det jag är anmäld
  till, det Min grupp är anmäld till och det jag favoritmarkerat.
- `Services/Offline/ConnectivitySwitch` + `UnreliableSource` — nätverksgränsen, med en
  dev-strömbrytare i tidsmaskinen så offline- och felvägarna går att köra.
- `Presentation/OrienteraViewModel` — gemensam bas som fångar `SourceUnavailableException` och
  gör den till `IsOffline` för vyn.
- Offline-lägen i UI:t: banner med tidsstämpel på tävlingsdetaljen, sparade tävlingar som lista
  i Tävlingar, designade lägen på Hem, Live och Resultat, och Jag som behåller det lokala.
- `OfflinePackageTests` — 11 tester över fallback, persistens och lokal data.

## Decisions

- **Nätverksgränsen är en egen dekorator.** `UnreliableSource` ligger där BFF:en kommer att
  ligga och kastar när strömbrytaren är av. Utan den hade offline-vägarna inte gått att köra
  förrän det fanns en riktig integration att förlora — och de hade då byggts oprövade.
- **Lokalt är lokalt.** Vem jag är, vilka jag följer och vad jag favoritmarkerat går aldrig
  genom nätverksgränsen. Appen ska fungera utan konto, och då ska den fungera utan täckning.
- **Ett dokument per tävling, inte ett index.** Ett paket läses och skrivs alltid helt, och en
  avbruten skrivning kan då bara skada den tävling den gäller. Skrivning sker till temporärfil
  och flyttas på plats.
- **Ett paket som inte går att läsa slängs.** Alternativet är att varje läsning av just den
  tävlingen misslyckas för all framtid.
- **Context Engine matas från paketet offline.** Paketet bär därför `MyEntryRegisteredAt` och
  `GroupEntryRegisteredAt` — utan dem kan CTA:n inte beräknas, och CTA:n är det mest användbara
  på sidan.
- **`SourceUnavailableException` fångas bara där den betyder något.** `LoadAsync` fångar den
  och ingenting annat: ett verkligt fel ska fortsätta krascha högljutt i stället för att gömma
  sig bakom ett offline-meddelande.
- **Filbaserad lagring, inte SQLite.** M1 lagrar en handfull paket som alltid läses hela.
  SQLite blir rätt när datamängden — en hel säsongs resultat och sträcktider — kräver frågor
  snarare än inläsning. Interfacet är samma dag det byts.

## Vad körningen avslöjade

- **Appen kraschade** med ett ohanterat `SourceUnavailableException` första gången
  strömbrytaren slogs av. Det är precis DoD-punkten *"integrationsfel ger tydlig fallback utan
  krasch"*, och den var inte uppfylld. Dev-strömbrytaren betalade för sig direkt.
- **De sparade paketen gick inte att öppna offline.** De fanns, men både Hem och Tävlingar
  krävde den live-hämtade kalendern för att visa något — så feltexterna pekade på varandra i en
  cirkel. Tävlingar listar nu de sparade paketen när det inte finns anslutning.
- **Jag tappade mer än den behövde.** Ett kast mitt i inläsningen tömde även Min grupp, som är
  lokal data. Lokalt laddas nu först och oskyddat; bara Sverigelistan och serien är beroende av
  nätverket.

## Verifiering

- 102 tester gröna, varav 11 nya över offline-vägarna.
- iPhone 17 Pro-simulator (iOS 26.2): tävlingsdetaljen offline visar "Offline — sparat 11:50"
  med min starttid, klass, prognos, PM-briefing och rätt CTA ur paketet; Tävlingar listar de
  sparade tävlingarna; Hem, Live och Resultat visar designade lägen; Jag behåller profil och
  Min grupp. Ingen krasch i något läge.
- `dotnet build -f net10.0-android`: OK.

## Kvar

Eventor-adaptern och BFF:en väntar på SP-01. Notiser är M2, kartdata M4.
