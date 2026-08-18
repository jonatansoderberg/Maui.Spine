# Terrängbilder

Bildbanken bakom `HeroImage` (beslut **D2** i
[redesign-02-natur-och-energi.md](../../../docs/design/redesign-02-natur-och-energi.md)).

Bilderna är bundlade och fungerar offline. De påstår **aldrig** att de är arenan — de visar den
sortens terräng tävlingen går i. Kartrutan är den som bär sann geografi, och den är alltid
fallback när ingen bild matchar (P7).

## Namnregel

```
terrain_<disciplin>_<terräng>.jpg
```

`<disciplin>` är `Discipline`-värdet i gemener: `sprint`, `middle`, `long`, `ultralong`,
`night`, `relay`, `indoor`.

`<terräng>` är ett värde ur den slutna mängden: `urban`, `skog`, `moran`, `fjall`, `kust`,
plus `default` som disciplinens egen reserv.

MAUI plattar ut underkataloger och kräver gemener och understreck i resursnamnet — därav
prefixet `terrain_` i stället för en katalog i namnet.

## Uppslagsordning

`HeroImage` (etapp B) provar i tur och ordning:

1. `terrain_<disciplin>_<terräng>` — när tävlingens terrängtyp är känd
2. `terrain_<disciplin>_default`
3. kartrutan

## Provisoriska bilder

De elva filerna som ligger här nu är **inte fotografier**. De är stiliserade lager genererade av
[`generate-placeholders.py`](generate-placeholders.py) — deterministiskt, så samma kommando ger
samma filer:

```
python3 generate-placeholders.py
```

De finns för att `HeroImage` ska kunna byggas och granskas i etapp B utan att vänta på
bildvalet. Att de uppenbart inte är fotografier är en fördel så länge de ligger kvar: ingen kan
missta dem för arenan. D2 gäller fortfarande — de ska bytas mot kurerade terrängbilder.

## Vad som ska ligga här

Ett tiotal bilder som täcker uppslagets båda första steg:

| Fil | Täcker |
|---|---|
| `terrain_sprint_urban.jpg` | stadssprint |
| `terrain_sprint_default.jpg` | sprint i park och närskog |
| `terrain_middle_skog.jpg` | medel i barrskog |
| `terrain_middle_moran.jpg` | medel i detaljrik morän |
| `terrain_long_skog.jpg` | lång i skog |
| `terrain_long_moran.jpg` | lång i morän |
| `terrain_long_fjall.jpg` | lång i öppen fjällterräng |
| `terrain_ultralong_fjall.jpg` | ultralång |
| `terrain_night_skog.jpg` | natt |
| `terrain_relay_skog.jpg` | stafett |
| `terrain_indoor_default.jpg` | inomhus |

## Licens

Varje bild skrivs in i [`terrain-licenses.txt`](terrain-licenses.txt) med källa, upphovsperson
och licens innan den läggs till — samma ordning som `Resources/Fonts/Inter-OFL.txt`.
En bild utan rad i den filen hör inte hemma i appen.
