# Ytor

Bilder som *är* en yta, inte innehåll på den. De bär ingen mening — tas de bort blir kortet en
enfärgad platta och ingenting går förlorat utom stämningen.

## Filerna

| Fil | Yta |
|---|---|
| `surface_live.jpg` | Live-kortet på Hem |

## Regler

- **Färgtoken ligger kvar under bilden.** `SurfaceLive` är ytans färg medan bilden laddas, och den
  färg avatarernas ring lyser med. Bildens medelfärg är `#07553C`, alltså i praktiken samma nyans
  som token — går bilden förlorad märks det inte.
- **Bilden får aldrig bestämma kortets storlek.** Den ritas med `Aspect="AspectFill"` och
  `HeightRequest="1"` plus `VerticalOptions="Fill"`: mätt bidrar den med ingenting, arrangerad
  fyller den ytan. Utan det mäts den på sin egen storlek och kortet växer till en skärmhög platta.
- **Ingen text i bilden**, och inget som måste läsas. Den ligger bakom innehåll och beskärs olika
  på olika skärmar.

## Licens

Se [`surface-licenses.txt`](surface-licenses.txt). En bild utan rad i den filen hör inte hemma i
appen, samma regel som för terräng- och hjältebilderna.
