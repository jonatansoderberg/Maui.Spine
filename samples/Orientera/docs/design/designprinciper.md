# Orientera — UI/UX-designprinciper (FÖRSLAG)

> **Status: FÖRSLAG — ska stämmas av innan någon skarp implementation.**
> Baserat på spec v1.0 avsnitt 15 (UX/design och mockups) + Nordic Light/Dark-mockuperna (sid 21–24).
> Beslut som behövs markeras ⬜.

## 1. Rekommenderad visuell riktning

Spec:en visar tre riktningar — **A. Nordic** (luftigt, lugnt, premium), **B. Map** (tydligare orienteringsidentitet), **C. Performance** (sport- och analysfokus) — alla i Light + Dark.

**Rekommendation (ur spec, som jag ställer mig bakom):**

> **Nordic som bas + subtil Map-identitet + Performance-språk i Resultat/Analys.**
> Målet är en konsumentapp som känns enkel på ytan men kraftfull när användaren borrar sig ned.

Konkret:

- **Hem, Tävlingar, Jag** → Nordic: stora luftiga kort, få block, mycket whitespace.
- **Tävlingsdetalj-hero + kart-vyer** → subtila Map-inslag: svaga höjdkurve-mönster/kartdetaljer som bakgrundstextur, aldrig som brus bakom text.
- **Live, Resultat, Analys** → Performance: högre datatäthet, tabulära siffror, färgkodad förlust/vinst.

⬜ **Beslut 1:** Godkänn riktningen Nordic + Map + Performance, eller välj en renodlad variant.

## 2. Designprinciper (ur spec, normativa)

1. **Få tydliga kort hellre än många widgets.** Hem har max 3–4 stora block.
2. **Orange används som orienteringsaccent och primär action — inte överallt.** En primär CTA per vy.
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

⬜ **Beslut 2:** Godkänn tokenuppsättningen (namn + palettinriktning). Exakta nyanser justeras vid kontrastverifiering (WCAG AA mot respektive yta).

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

| # | Beslut | Alternativ |
|---|--------|-----------|
| 1 | Visuell riktning | Nordic+Map+Performance (rek.) / renodlad Nordic / renodlad Performance |
| 2 | Färgtokens & accent | Orange enligt förslag / justerad palett |
| 3 | Typsnitt | Inter (rek.) / Manrope / systemfont |
| 4 | Tabbikonografi | Punktmarkörer som i mockups / klassiska ikoner + text |
| 5 | Namn/brand i M0 | "Orientera" + kompasslogga enligt PDF / placeholder tills SP-13 name clearance |

När besluten är tagna kodifieras detta som design tokens i `Resources/Styles/` (Light/Dark-resurslexikon) och en `docs/design/design-system.md` med de låsta värdena.
