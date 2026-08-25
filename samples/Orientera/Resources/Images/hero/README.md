# Hjältebilden på Hem

Bakgrunden bakom hälsningen (beslut **D12** i
[redesign-04-hem.md](../../../docs/design/redesign-04-hem.md)).

Den här bilden är appens **enda** undantag från P7 — den bär stämning, inte plats. Undantaget är
räknebart precis som `SignalUrgent`: exakt en bundlad icke-terrängbild, på exakt en yta. Överallt
annars gäller P7 oförändrad, och `HeroImage` slår upp terräng på disciplin med kartrutan som
fallback.

Bilden påstår aldrig att den är någonstans. Den har ingen text, ingen arena och ingen löpare som
går att känna igen.

## Filen

```
hero_home.jpg
```

Namnet är fast och slås inte upp — det finns en hjälte på Hem och den byter inte med något.

## Provisorisk

`hero_home.jpg` är i dag **inte ett fotografi**. Den är stiliserade lager genererade av
[`generate-placeholder.py`](generate-placeholder.py) — deterministiskt, så samma kommando ger
samma fil:

```
python3 generate-placeholder.py
```

Att den uppenbart inte är ett fotografi är en fördel så länge den ligger kvar: ingen kan missta
den för en plats. D12 gäller fortfarande — den ska bytas mot en kurerad bild.

## Kontrastkravet

Hälsningen står i vitt ovanpå bilden. Det är **gradienten** som gör texten läsbar, inte bilden:
uppmätt mot bildens ljusaste pixel under textytan ger bilden ensam 2.25:1, och med `HeroScrim`
över sig 6.4:1.

Kravet gäller bilden som byter in också. Klarar en bild inte 4.5:1 mot vitt under texten, med
gradienten inräknad, byts **bilden** — aldrig texten.

## Licens

Se [`hero-licenses.txt`](hero-licenses.txt). En bild utan rad i den filen hör inte hemma i appen,
samma regel som för terrängbilderna.
