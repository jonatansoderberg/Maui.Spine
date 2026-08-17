# Orientera — UI/UX-designprinciper (FÖRSLAG)

> **Status: AVSTÄMD 2026-08-10.** Principerna nedan är normativa. Besluten 1–5 är tagna —
> se sammanfattningen i §9 och de låsta värdena i [design-system.md](design-system.md).
> Baserat på spec v1.0 avsnitt 15 (UX/design och mockups) + Nordic Light/Dark-mockuperna (sid 21–24).

## 1. Rekommenderad visuell riktning

Spec:en visar tre riktningar — **A. Nordic** (luftigt, lugnt, premium), **B. Map** (tydligare orienteringsidentitet), **C. Performance** (sport- och analysfokus) — alla i Light + Dark.

**Rekommendation (ur spec, som jag ställer mig bakom):**

> **Nordic som bas + subtil Map-identitet + Performance-språk i Resultat/Analys.**
> Målet är en konsumentapp som känns enkel på ytan men kraftfull när användaren borrar sig ned.

Konkret:

- **Hem, Tävlingar, Jag** → Nordic: stora luftiga kort, få block, mycket whitespace.
- **Tävlingsdetalj-hero + kart-vyer** → subtila Map-inslag: svaga höjdkurve-mönster/kartdetaljer som bakgrundstextur, aldrig som brus bakom text.
- **Live, Resultat, Analys** → Performance: högre datatäthet, tabulära siffror, färgkodad förlust/vinst.

✅ **Beslut 1:** Riktningen Nordic + Map + Performance godkänd.

## 2. Designprinciper (ur spec, normativa)

1. **Få tydliga kort hellre än många widgets.** Hem har max 3–4 stora block.
2. **Grönt bär primär handling och aktivt läge.** En primär CTA per vy. Orange är inte varumärket utan en signal, och reserveras för tre saker: live pågår, deadline inom ett dygn, och tapp mot vinnaren. *(Omskriven genom beslut 6; löd tidigare "Orange används som orienteringsaccent och primär action".)*
3. **Live/resultat får ha högre datatäthet än Hem/Tävlingar.** Densitet är ett medvetet lägesval, inte en glidande skala.
4. **Kartan får vara visuellt dominant när användaren analyserar vägval.** UI kliver undan; overlay-kort är kompakta.
5. **Status och osäkerhet uttrycks tydligt** — prediction visas som intervall ("8–15"), AI-extraherad information med källa ("Måttligt kuperat — PM sida 2") och uppskattningar märks som uppskattningar.
6. **Touch targets och text ska fungera under tävlingsförhållanden** — frusna fingrar, solljus, stress. Minst 44 pt targets, generösa tap-ytor på kärnhandlingar.

## 3. Färgtokens (förslag)

Systemtema som default; alla färger som tokens i Light + Dark (samma nyckelset, jfr PWOS-modellen). Aldrig literaler i XAML.

| Token | Light | Dark | Användning |
|-------|-------|------|------------|
| `SurfacePage` | `#F7F7F5` varm off-white | `#111315` nära-svart | Sidbakgrund |
| `SurfaceCard` | `#FFFFFF` | `#1B1E21` | Kort |
| `SurfaceRaised` | `#FFFFFF` + skugga | `#22262A` | Sheets, hero |
| `TextPrimary` | `#1A1D21` | `#F2F3F4` | Rubriker, värden |
| `TextSecondary` | `#5B6167` | `#9AA1A7` | Metadata, etiketter |
| `AccentAction` | `#E8590C` orange | `#FF7A33` orange (ljusare för kontrast) | Primär CTA, aktiv flik, live-markör |
| `AccentSubtle` | `#FBE8DD` | `#3A2A20` | Fill bakom accent-chip/badge |
| `MapInk` | `#8A7B5C` beige/terräng | `#4A5240` mörk skogsgrön | Map-identitetsdetaljer |
| `PositiveDelta` | `#2F9E44` | `#51CF66` | Vinst/över prognos |
| `NegativeDelta` | `#E03131` | `#FF6B6B` | Tapp/bom |
| `EstimateInk` | `#9C36B5` | `#DA77F2` | Modellerade/uppskattade värden |

Nyckelidé: **modellerad data får en egen färg** (`EstimateInk`) så att observerat vs uppskattat aldrig förväxlas — det operationaliserar förklarbarhetsprincipen.

✅ **Beslut 2:** Tokenuppsättningen godkänd. Fyra nyanser justerades vid kontrastverifieringen (`AccentAction`, `AccentSubtle`, `PositiveDelta`, `NegativeDelta` i light) — se [design-system.md](design-system.md#justeringar-mot-förslaget-wcag-aa).

## 4. Typografi

- **En humanistisk sans** (förslag: Inter eller Manrope; mockuperna använder Inter-liknande snitt) i 3–4 vikter.
- **Tabulära siffror** (tnum) i alla tider, placeringar och splits — kolumner får inte hoppa när live uppdaterar.
- Skala (phone-first): Display 28 (Hem-hälsning, stora resultat), H1 22, H2 17, Body 15, Caption 13, Micro 11 (uppercase-etiketter som "LIVE NU", "NÄSTA FÖR MIG").
- Stora nyckeltal (placering "3:a", "14 / 67", poäng) sätts i Display-storlek med efterföljande delta i Caption — mönstret från mockuperna.

## 5. Komponentprinciper

- **Kort är den bärande ytan.** Radius 16, låg skugga i light, hairline-outline i dark (samma knep som PWOS: skugg-/stroke-tokens per tema).
- **Sektionsetiketter** i Micro/uppercase/TextSecondary ("LIVE NU", "SENASTE RESULTAT") — ger struktur utan rubrikvikt.
- **Chips** för snabbfilter (För dig, Nära, distrikt) — en rad, horisontellt scrollbar, vald chip = AccentAction-fill.
- **Bottom sheets för alla sekundära beslut** (filter, klassval, följ löpare, jämför) — Spine `NavigableSheet` med detents; huvudflödet lämnas aldrig i onödan.
- **Statusbadges:** Anmäld (grön fill), PM publicerat (grön text), Live (orange puls), Grupp (neutral chip) — alltid text + färg, aldrig enbart färg (tillgänglighet).
- **Live-listor:** rad-highlight för "jag" (orange ton) och ★ för favoriter/Min grupp; uppdateringsstatus synlig ("Uppdaterad för 12 sek sedan").
- **Tomma/fel/offline-lägen är designade lägen**, inte spinners: offline visar cachet tävlingspaket med tidsstämpel; saknad integration degraderar till deep-link-kort (NFR Fallback).

## 6. Navigation och flöden

- **5 bottenflikar:** Hem, Tävlingar, Live, Resultat, Jag (fast ordning enligt spec).
- Kontextstyrd Hem — kortens ordning styrs av Context Engine, aldrig av användarkonfiguration i v1.
- Djupflöden (Tävling → PM → Karta → Live → Resultat → Analys) körs som Spine-regions med typed params; tillbaka-svep ska alltid fungera.
- Externa destinationer (Livelox-viewer, native maps, Eventor-webb) öppnas explicit med extern-länk-ikonografi — användaren ska aldrig vara osäker på om hen lämnar appen.

## 7. Rörelse

- Sparsam animation: sheet-transitioner (Spine), live-puls på LIVE-badge, mjuk höjning av kort vid touch. Ingen dekorativ animation i listor.
- Live-uppdateringar animerar **värdebyte** (fade/rulla siffror), aldrig layoutskiften.

## 8. Tillgänglighet (normativ, ur NFR)

- Dynamisk textstorlek där rimligt; layouten tål +2 steg utan klippning på kärnflöden.
- Kontrast WCAG AA på alla token-par (verifieras per tema innan lås).
- VoiceOver/TalkBack på kärnflöden: Hem, tävlingslista, detalj, live, resultat.
- Touch targets ≥ 44 pt.

## 9. Avstämningspunkter innan skarp implementation

Beslut 1–5 togs 2026-08-10, inför M0. Beslut 6–11 togs 2026-08-17 och kommer ur
[redesign-02-natur-och-energi.md](redesign-02-natur-och-energi.md) §3.

| # | Beslut | Utfall |
|---|--------|--------|
| 1 | Visuell riktning | **Nordic + Map + Performance** |
| 2 | Färgtokens & accent | **Förslaget godkänt**, nyanser justerade endast där WCAG AA fallerade |
| 3 | Typsnitt | **Inter**, med tabulära siffror i alla tider, placeringar och splits |
| 4 | Tabbikonografi | **Klassiska ikoner + text** |
| 5 | Namn/brand i M0 | **"Orientera" internt**, ingen store-facing branding (SP-13 kvarstår) |
| 6 | Accentfärg (D1) | **Grön bas, orange för det som brinner.** Orange bryts ut som `SignalUrgent` med tre tillåtna användningar: live pågår, deadline inom ett dygn, tapp mot vinnaren. Princip 2 i §2 omskriven |
| 7 | Bildkälla (D2) | **Kurerade terrängbilder i appen**, valda på disciplin och terrängtyp. Fungerar offline, påstår aldrig att de är arenan; kartrutan är fallback |
| 8 | Profilbild och social graf (D3) | **Avatarplatsen byggs nu, innehållet stannar lokalt.** `IdentityView` känner inte sin källa, så konto och följargraf blir ett eget M5-beslut som inte rör designen igen |
| 9 | Flikstruktur (D4) | **`Hem · Tävlingar · Live · Profil · Mer`.** Resultat flyttar in under Mer; §6 och [krav/01-vision-och-navigation.md](../krav/01-vision-och-navigation.md) skrivs om |
| 10 | Anmälan (D5) | Mellanlandning ja; formuläret **kvar i appens webbvy** — Safari har egen kakburk och extern öppning loggar ut användaren |
| 11 | Arbetsordning (D6) | Fynden från testkörningen lagas inuti varje sida när den byggs om, inte i en egen omgång |

Kodifierat som design tokens i `Resources/Styles/` (Light/Dark-resurslexikon) och låsta värden i [design-system.md](design-system.md).
