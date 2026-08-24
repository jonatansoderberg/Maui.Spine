# Arenabilder till C#: kedjan som en kötriggad Function

**GitHub:** _issue ej skapad än_
**Branch:** issue/arenabild-cache
**Status:** Completed

## Plan

Porten följer [docs/arenabilder-till-csharp.md](../docs/arenabilder-till-csharp.md) i sex steg,
med Python-prototypen i `tools/arenabild/` som facit. Varje steg mäts mot
`tools/arenabild/referens/checkpoints.json` och lämnar systemet körbart.

1. **Terränghämtning** — STAC-sökning, nedladdning med omförsök (dl1 svarar sporadiskt 403),
   COG-läsning med LibTiff, mosaik till grid med bilinjär omsampling. Ortofoto och
   terrängskuggning via WMS med `HttpClient`.
2. **Sol och årstid** — NOAA:s algoritm, årstid ur månaden, nattdetektering.
3. **Renderaren** — höjdfält, skuggning, voxel-strålmarsch med suffixminimering, dis, gradering.
4. **Överlagringar** — muren i markplanet med ockluderingstest, vimpeln i bildplanet, arenaljuset.
5. **Bildmodellen** — promptkomposition, `images.edit` mot `gpt-image-2`, murkontroll.
6. **Kötriggad Function** — läser `arenabilder-att-gora`, skriver blobben.

Två kända fällor, båda ur provkörningarna: allt mot `dl1.lantmateriet.se` måste återförsöka,
och varje talparsning måste ta `CultureInfo.InvariantCulture` — `double.Parse` kastar annars
på svenskt locale.

## Changes

- **Steg 1, terränghämtningen.** `SwedishProjection` (ProjNET, byggd programmatiskt utan
  WKT-parsning), `ElevationTile` (LibTiff-läsare för Lantmäteriets COG:er med pyramidval),
  `ScalarGrid`/`ColorGrid` (numpy-arrayernas motsvarighet med bilinjär sampling),
  `GeotorgetCredentials`, `LantmaterietClient` (STAC, nedladdning med 8 omförsök mot dl1:s
  sporadiska 403, WMS för ortofoto och terrängskuggning, diskcache med prototypens
  namngivning) och `TerrainSource` (mosaik + omsampling). Facittester i `ArenaTerrainTests`:
  projektionen inom 1 m mot pyproj, höjdmosaikens min/max/medel/std inom 0,01 m mot rasterio.

- **Steg 2, sol och årstid.** `Sun` med NOAA-algoritmen och sommartidsregeln portade rakt av.
  Facittester i `ArenaSunTests`: solhöjd och azimut inom 0,05° mot prototypen, årstiden via
  befintliga `ArenaImageKey.SeasonOf`.

- **Steg 3, renderaren.** `Lighting` (tre ljusregimer), `TerrainTexture` (hillshade,
  vinterstilisering, texturbygge), `GridMath` (percentil, boxbaserad gaussisk utjämning),
  `TerrainRenderer` (voxel-strålmarsch med suffixminimering, dis, djupbuffert, tilt-shift-
  kamera) och `ImageGrade` (lokalkontrast, S-kurva, delad toning, mättnad, vegetationslyft).
- **Steg 4, överlagringarna.** `Rasterizer` (skanlinjefyllnad utan kantutjämning, som PIL),
  `Overlays` (muren med ockluderingstest och nattglöd, gränsen i bildplanet, arenaljuset),
  `Flag` (den böjda vimpelduken med ImageSharp.Drawings canvas för text). `ArenaComposer`
  binder ihop kedjan till den nakna bilden. Facittest i `ArenaImageFacitTests`:
  kantkorrelation **0,990** mot referensbilden (krav > 0,98), ram och grid mot checkpoints.
  Eventor-sidan för referenstävlingen sparad som fixture så testet är nätfritt.

- **Steg 5, bildmodellen.** `EnhancementPrompt` och `IndoorPrompt` (prototypens texter
  ordagrant, mätta strängexakt mot snapshotade fixturer i `ArenaPromptTests`), `ImageModel`
  (OpenAI-SDK:ns `images.edit` med `input_fidelity=high` och 400-fallback; ingen
  `response_format` — gpt-image avvisar den, upptäckt i provkörning), `WallCheck`
  (murkontrollen mäter orange täckning på murens egna kvadrar ur renderingen).
- **Steg 6, kötriggade Function.** `EventorArenaPage` (arenan och polygonen ur publika
  sidan, testad mot sparad HTML), `ArenaImageWorker` (läser `arenabilder-att-gora`,
  renderar, ljussätter, murkontrollerar, lägger vimpeln, skriver blobben; inomhus går via
  ren generering ur `IndoorPrompt`). DI i Program.cs; `ArenaImage`-nycklar i
  local.settings.example.json.
- **Provkörd mot riktiga tjänster.** Hela kedjan för Trimtex Cup #4: naken render 2 s,
  gpt-image-2 42 s, murkontroll 100 % godkänd, vimpel pålagd. gpt-image-2 avvisade
  `input_fidelity` och kedjan föll korrekt tillbaka till att köra utan.

- **Vimpeln som bildfil.** `Arena/Assets/vimpel.png` (inbäddad resurs) — en levererad
  produktbild av TC-beachflaggan, beskuren och lagd enligt konventionen: transparent
  bakgrund, mastfoten i bildens vågräta mitt med nederkanten vid foten, och filens hela
  höjd skalas till den begärda vimpelhöjden. Filen är utbytbar utan kodändring; `Flag`
  skalar (egen Lanczos) och komponerar bara. Den procedurella dukritningen är borta ur
  porten.
- **Muren 50 % genomskinlig.** Ändrad i både prototyp och port, prompten omskriven till
  halvgenomskinligt material, facit regenererat (`gor_referens.py`). På vägen hittades en
  bugg i prototypen: PIL:s `ImageDraw.Draw(img, "RGBA")` blandar bara mot RGB-bilder, så
  murens alfa hade aldrig haft effekt — nu ritas den på ett eget lager som alfakomponeras,
  i båda implementationerna. Kantkorrelation efter ändringen: **0,995**.

- **Appen visar bilden i tävlingssidans hjälte.** `ArenaImage`/`ArenaSeason` och
  `IArenaImageSource` flyttade till domänens källkontrakt (`Orientera.Domain/Sources/
  ArenaImages.cs`) så app och backend delar exakt form. `BackendSource` frågar
  `competitions/{id}/arenabild`; finns bilden får `HeroImage` dess url (enhetscachad 180
  dagar — bloburlen bär versionen, innehållet bakom den ändras aldrig) med CC BY-krediteringen
  i nederkanten; annars står den medföljande terrängbilden kvar som platshållare, ingenting
  cachas av ett nej, och själva uppslaget var beställningen — nästa besök hittar bilden.

- **Skarp test på fem tävlingar** (Veteran-SM sprint, Golden Weekend, Nässjömedeln,
  Jarlkut'n, Ungdoms-SM sprint — fem distrikt, stad och skog, med och utan område).
  Alla fem igenom på 50–61 s styck, murkontroll 100 % där mur finns. Två fynd åtgärdade:
  **WMS-taket** — Jarlkut'ns grid var 4 600 px högt och minkarta vägrar över 4096, så
  ortofoto och skuggning hämtas nu i block som sys ihop på målgridets pixelgränser — och
  **LibTiff-varningsspam** — vissa regioners höjdrutor har osorterade taggkataloger som ger
  hundratals varningsrader per ruta trots korrekt läsning (korsmätt mot rasterio), så
  varningskanalen är tystad medan fel fortfarande blir undantag.

- **Områdespassning i kameran.** När området är arrangörens eget krymps brännvidden tills
  varje polygonhörn ryms i bild med marginal (sidled, nederkant, överkant med murtopp), och
  närgränsen dras in till frustumets nederkant — annars smetades bildens nederkant ut i
  kolumnränder vid vida vyer. På för `HasOutline`, av annars; referensområdet påverkas inte
  (ingen begränsning biter där), så facit står orört.
- **Testat genom appen, hela vägen.** Azurite + func-värd lokalt, iOS-simulator: första
  besöket på Trimtex-sidan visade terrängplatshållaren och lade själv beställningen; 63 s
  senare fanns bilden och nästa besök visade den, med CC BY-raden i hjältens nederkant.
  Fynd på vägen: **containern skapades privat** och varje blobhämtning svarade 403 — nu
  skapas den med `PublicAccessType.Blob` (lagringskontot måste också tillåta publik
  åtkomst). Lokalt kräver Azurite `--skipApiVersionCheck` mot nya SDK:n.

- **Tävlingar utan klockslag renderas mitt på dagen.** Eventor lämnar ofta starttiden tom;
  `FirstStart` står då vid midnatt, solen under horisonten, och en dagtävling blev en
  nattbild (hittad i apptestet med DM stafett). `ArenaImageKey.RenderTimeOf` antar 12:00 —
  utom för nattävlingar som får 21:00 och behåller mörkret, samma regel som prototypens
  `parse_when`. Testad i `ArenaImageKeyTests`.
- **Hjälten i appen putsad.** 224 pt (arenabildens egna 16:9 på skärmbredden, ingen synlig
  beskärning), skymningsgradienten borttagen helt (dess märken-ovanpå-syfte används inte
  längre någonstans), krediteringen 8 pt vit med egen skuggkant.

- **Bloburlen bär ändringstiden.** `ArenaImageStore` läser blobbens egenskaper i stället för
  bara existens och lägger `?v=<LastModified>` på urlen: enhetens 180-dagarscache är bara
  sund så länge en omgjord blob under samma namn också blir en ny url. Hittad live — en
  regenererad bild serverades som sin gamla version ur telefonens cache.
- **Arenan kan flytta sig efter generering.** Eventors arena för Trimtex flyttade ~120 m
  samma dag som PM:et uppdaterades; bilden från morgonen bar gamla läget och en ny render
  det nya (verifierat mot Eventors arena-KML, `/Events/ShowEventCenterPosition` — samma
  värde som sidans kartcentrum, vilket bekräftar prototypens heuristik). Känd begränsning:
  en redan gjord bild följer inte med när arrangören flyttar arenan; mekanismen för att
  göra om allt är versionshöjningen.

- **Ren dag eller ren natt, aldrig skymningen emellan.** Kvällstävlingar — de flesta
  närtävlingar startar 18:30 — fick solen några grader över horisonten, och gyllene timme
  på riktigt blev en dunkel bild där terrängen försvann i långa skuggor och orange grus.
  `Lighting.For` väljer nu ljuset: dagbilder ljussätts vid `DayFloor` 35° med
  tävlingstidens azimut kvar, så skuggriktningen varierar mellan tävlingar men höjden
  lyfts; en nattävling är natt även när solen bara står ett par grader under horisonten,
  som en juninatt klockan tio. Arenabelysningen vid låg sol (`LampBelow`, `LitArena`) är
  borta — den kunde aldrig tändas längre — och natt är det enda som tänder arenan.
  Speglat i prototypen (`render.light_for`, `forbattra` väljer ljuset först och skickar
  ner det till den nakna renderingen som `light_override`). `ArenaImage:Version` höjd till
  2, så alla bilder görs om.
- **Prompten säger datum, inte klockslag.** Solhöjden väljs nu av policyn och inte av
  starttiden, och "18:30" intill "sun 35 degrees above the horizon" vore en motsägelse
  mitt i prompten. `FormatWhen` → `FormatDate`; datumet bär årstiden, som är det enda av
  tidpunkten som syns i bilden. Fixturerna omgenererade i båda ändar.
- **Vimpeln står på Eventors arenakoordinat — även när den är fel.** Uppmätt för Trimtex
  Cup #4: projektionen sätter vimpeln på exakt sidans `centerLatitude/Longitude`, samma
  värde som `/Events/ShowEventCenterPosition`. Mot ortofotot ligger den punkten i
  skogsremsan vid tillfartsvägen i områdets nordvästra hörn, medan PM:et anger sandtaget
  i Rörberg — arrangören har släppt nålen bredvid TC. Ingen åtgärd i kedjan: vi ritar
  arrangörens egen punkt, och att gissa fram en bättre vore att hitta på var arenan
  ligger.
- **Provkörd med det nya ljuset.** Trimtex Cup #4 hela vägen: naken render med mur 1,7 s,
  gpt-image-2 49 s, vimpel pålagd. Dunklet är borta — terrängen är läsbar över hela bilden.
  Ett fynd på vägen: `input_fidelity=high` avvisas fortfarande av modellen, och utan den
  driver den. Grovt mätt (andel mark där rött överväger grönt innanför gränsen) krympte den
  öppna marken från 23,6 % till 17,0 % — modellen planterade skog i sandtaget trots att
  prompten säger att öppen mark ska förbli öppen. Det är samma sorts avdrift som
  murkontrollen finns för, men på vegetation, och det är inte åtgärdat.
- **Den procedurella vimpeln borta även ur prototypen.** `_flag_flat`, `flag_tile` och
  `_bezier` är strukna ur `render.py` — med dem hela den ritade duken, masten, foten och
  texten. `draw_flag` skalar och komponerar nu `samples/Orientera.Backend/Arena/Assets/
  vimpel.png`, samma fil porten bär som inbäddad resurs, efter samma konvention. Ingen
  kopia i `tools/`: två filer hade betytt två utseenden. Provkörningen ovan visade
  prototypens ritade "TC"-duk och inte den levererade bilden — det var vad som gjorde
  skillnaden synlig.
- **Python-prototypen borttagen.** Porten är implementationen; två renderare av samma bild
  är en för mycket, och den ena hade redan hunnit visa fel vimpel i en provkörning.
  `tools/arenabild/` bär nu bara `referens/` (facit) och `cache/` (nedladdad terräng).
  `ArenaImageOptions.CacheDirectory` är ny så att porten kan fylla den cache facittesterna
  läser — utan den fanns det ingen väg dit när prototypen försvann. Tom betyder temp, som
  förut, och det är vad som gäller i drift.

## Decisions

- **Mosaiken samplar i pixelcentra med prototypens upplösningskonvention.** rasterios `merge`
  får upplösningen härledd ur ramens bredd och låter den gälla båda axlarna; porten gör
  likadant, annars glider samplingspositionerna och statistiken mot facit.
- **Nodata blir NaN före omsamplingen, inte efter.** Prototypen tröskar bort värden under
  −1000 efter bilinjär omsampling och kan i princip missa utsmetade halvvärden; porten
  markerar dem i källan så att interpolation mot nodata blir NaN och fylls med medianen.
  Referensrutorna saknar nodata, så facit påverkas inte.
- **Terrarium-reserven portas inte.** Prototypen faller tillbaka på globala ~25 m-rutor när
  Geotorget-inloggning saknas; i backend är inloggningen konfiguration, och saknas den ska
  beställningen misslyckas hörbart i stället för att tyst ge en grövre bild.
- **Produktionskedjan ritar muren, inte gränslinjen.** Den nakna bilden får muren
  (`wall=True` som i facit), bildmodellen får den i prompten och murkontrollen mäter att den
  överlevde; efter AI-passet läggs bara vimpeln (och nattskenet). Prototypens
  `draw_outline`-väg finns kvar i `Overlays` för felsökning.
- **SkiaSharp i stället för ImageSharp, med egen Lanczos.** Planen pekade på ImageSharp
  4.1.1, men v4 kräver licensnyckel vid bygge och **stoppar Release-byggen** utan den.
  Efter avstämning valdes SkiaSharp (MIT). Skia saknar Lanczos-omsampling — med Catmull-Rom
  föll kantkorrelationen till 0,9795, strax under kravet — så `Lanczos` implementerar PIL:s
  kärna för hand. Slutmätning: **0,9903**. Skia används för avkodning, PNG och vimpelns
  text; `SkiaSharp.NativeAssets.Linux.NoDependencies` följer med för Function-appen.
- **Murkontrollens trösklar.** Kvadratcentroider provas i 7×7-fönster mot en generös
  orange-definition; 60 % av kvadrarna måste bära orange. Prototypens kontroll larmade
  falskt tre gånger, så täckningen loggas och gränsen ligger medvetet lågt. Underkänd bild
  kastar tillbaka beställningen på kön — en ny AI-dragning är hela poängen med omförsöket.
- **Ljuspolicyn ligger ovanför `Lighting.At`, inte i den.** `At` är den mätta
  avbildningen solhöjd → ljus och är facit mot prototypen; `For` är valet av vilken solhöjd
  bilden ska ha. Hade golvet lagts i `At` hade facittesterna mätt policyn i stället för
  renderaren, och kantkorrelationen mot referensbilden hade tappat sin betydelse.
