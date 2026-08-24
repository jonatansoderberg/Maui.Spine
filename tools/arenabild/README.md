# Arenabilder — facit och terrängcache

Här bor inte längre någon implementation. Renderingen är C#, i
[`samples/Orientera.Backend/Arena`](../../samples/Orientera.Backend/Arena), och körs av
`ArenaImageWorker` på kön `arenabilder-att-gora`. Python-prototypen som porten mättes mot
är borttagen — den fyllde sin uppgift och två implementationer av samma bild är en för
mycket.

## `referens/`

Facit som `ArenaImageFacitTests` och `ArenaTerrainTests` mäter mot: `checkpoints.json`
(projektion, sol, ram, höjdmodellens statistik, med toleranserna i filen) och
`trimtex-24aug-naken.png` (den nakna renderingen av Trimtex Cup #4, kravet är
kantkorrelation > 0,98).

Måtten kom ur prototypen. Nu är de regressionsskydd: faller korrelationen har renderaren
ändrat sig, och det ska märkas innan bilderna gör det. Ska facit medvetet flyttas — som när
muren gjordes genomskinlig — skrivs den nya referensbilden av porten själv;
`ArenaImageFacitTests` sparar sin egen render till temp och sökvägen står i felmeddelandet.

## `cache/`

Nedladdade höjdrutor (`cache/hojd/*.tif`) och ortofoton (`cache/orto_*.img`). Ignorerad av
git: den väger tiotals megabyte och är Lantmäteriets data snarare än vår.

Facittesterna läser cachen och hoppas över sig själva när den är tom. Fyll den genom att
köra kedjan en gång med cachen pekad hit:

```
ArenaImage__CacheDirectory=<repo>/tools/arenabild/cache
```

Det kräver Geotorget-inloggning med behörighet till *Markhöjdmodell Nedladdning*, ur
`LM_USER`/`LM_PASS` eller `~/.config/lantmateriet.env`.

Allt mot `dl1.lantmateriet.se` måste återförsöka — den svarar sporadiskt 403 på fullt
giltiga anrop, och `LantmaterietClient` gör åtta försök av just det skälet.
