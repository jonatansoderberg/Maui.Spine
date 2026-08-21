# Plan: flytta arenabildsrenderingen till C#

Prototypen är skriven i Python (numpy, rasterio, PIL). Frågan är om hela kedjan kan bo i
`Orientera.Backend` i stället, så att det blir en deploy, ett språk och en CI.

Svaret är ja. Den här planen bygger på mätning, inte antagande — de två riskabla
beroendena är provkörda mot riktig data innan planen skrevs.

## Vad som är verifierat

| Python idag | C#-ersättning | Status |
|---|---|---|
| rasterio/GDAL, läsa COG | `BitMiracle.LibTiff.NET` 2.4.660 | **Provkörd** mot Lantmäteriets ruta: 2500×2500 float32, DEFLATE, kakel 512×512. Kakel avläst, georeferens tolkad ur tagg 33550/33922. |
| pyproj, WGS84 → SWEREF99 TM | `ProjNET` 2.1.0 | **Provkörd** mot pyproj: 0 m avvikelse i Malmö, Valbo och på centralmeridianen, 1 m i Kiruna. |
| PIL, bildritning | `SixLabors.ImageSharp` 4.1.1 | Finns, moget, plattformsoberoende. |
| OpenAI-klient | `OpenAI` 2.13.0 | Officiell .NET-SDK. |
| laspy, punktmoln | — | Behövs inte. Byggnadshöjder kom från bildmodellen i stället. |

## Det som ser svårast ut är det lättaste

**numpy är inte problemet.** Voxel-strålmarschen är loopar över arrayer, och i C# med
`Span<T>` blir den snabbare än numpy, inte långsammare — numpys fördel är uttrycksfullhet,
inte hastighet. Renderaren är dessutom redan skriven och förstådd; det är en översättning,
inte en konstruktion.

Det verkligt fiffliga är i stället:

1. **Mosaik av flera COG-rutor till ett grid** med bilinjär omsampling. GDAL:s `merge` gör
   det gratis; i C# skrivs det för hand. Väldefinierat, ~150 rader.
2. **Överbildsnivåer.** Rutorna bär pyramider (2, 4, 8). Vid utzoomad vy ska rätt nivå
   läsas i stället för full upplösning, annars hämtas tio gånger för mycket data.
3. **Kulturberoende talparsning.** `double.Parse("15.0")` kastar på en svensk maskin.
   Varje parsning av WKT, geometri och API-svar måste ta `CultureInfo.InvariantCulture`.
   Den här buggen slog till redan i provkörningen.

## Ordning

Varje steg lämnar systemet körbart, och Python-versionen står kvar som facit tills det
sista steget är klart.

| # | Steg | Innehåll |
|---|---|---|
| 1 | **Terränghämtning** | STAC-sökning, nedladdning med omförsök (dl1 svarar sporadiskt 403), COG-läsning, mosaik till grid. Ortofoto och terrängskuggning via WMS med `HttpClient`. |
| 2 | **Sol och årstid** | Solhöjd och azimut ur NOAA:s algoritm, årstid ur månaden, nattdetektering ur `Discipline.Night`. Ren matematik, ~60 rader. |
| 3 | **Renderaren** | Höjdfält, skuggning, voxel-strålmarsch med suffixminimering, dis, gradering. Den största enskilda biten. |
| 4 | **Överlagringar** | Muren i markplanet med ockluderingstest mot djupbufferten, vimpeln i bildplanet, arenaljuset. |
| 5 | **Bildmodellen** | Promptkomposition, `images.edit` mot `gpt-image-2` i 1920×1088, murkontroll före cachning. |
| 6 | **Kötriggad Function** | Läser beställningen `ArenaImageStore` redan lägger, skriver blobben. Samma Function App som resten. |

## Hur porten hålls ärlig

Python-versionen är facit. Varje steg får ett test som jämför C#-utdata mot sparad
Python-utdata för Trimtex Cup #4:

- höjdgrid: max absolut avvikelse < 0,01 m
- projektion: < 1 m
- färdig bild: kantkorrelation > 0,98 mot referensbilden

Det är samma sorts mätning som avslöjade att gpt-image-2 bevarar struktur bättre än
gpt-image-1.5, och att min egen murkontroll larmade falskt tre gånger. Utan den hade
porten kunnat vara subtilt fel utan att någon märkte det.

## Vad det ger

En deploy i stället för två. Ingen Container App, ingen Docker-avbildning med GDAL,
ingen kö mellan två språk — genereringen blir en kötriggad Function i samma app.

## Vad det kostar

Uppskattat 8–11 dagars arbete. Renderaren och terränghämtningen är merparten.

Alternativet — Python-arbetaren i en Azure Container Apps Job — kan vara igång på en dag
och kostar ören att köra. Det är ingen dålig lösning; den kostar bara ett andra språk och
en andra deploy i underhåll för all framtid.
