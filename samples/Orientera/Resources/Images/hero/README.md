# Hjältebilden på Hem

Bakgrunden bakom hälsningen (beslut **D12** i
[redesign-04-hem.md](../../../docs/design/redesign-04-hem.md)).

Den här bilden är appens **enda** undantag från P7 — den bär stämning, inte plats. Undantaget är
räknebart precis som `SignalUrgent`: exakt en bundlad icke-terrängbild, på exakt en yta. Överallt
annars gäller P7 oförändrad, och `HeroImage` slår upp terräng på disciplin med kartrutan som
fallback.

Bilden påstår aldrig att den är någonstans. Den har ingen arena och ingen löpare som går att känna
igen — ryggtavla i motljus, inget ansikte.

## Filen

```
hero_home.jpg
```

Namnet är fast och slås inte upp — det finns en hjälte på Hem och den byter inte med något.

## Kontrastkravet

Hälsningen står i vitt ovanpå bilden. Det är **gradienten** som gör texten läsbar, inte bilden.

Mätningen görs mot den ljusaste pixeln **inom varje textrads egen bredd** — inte inom en generös
ruta runt den, vilket mäter sådant texten aldrig ligger på. Med den här bilden ger en rak toning
3.84:1 på väderraden, som är den svagaste; ett mjukt andra stopp vid 40 % (`HeroScrimSoft`) lyfter
den till 5.69:1. Att i stället hålla `HeroScrim` genom hela textbandet hade gett 9.38:1 och en bild
man inte ser.

Kravet gäller bilden som byter in också. Klarar en bild inte 4.5:1 mot vitt under texten, med
gradienten inräknad, byts **bilden** — aldrig texten.

## Uttoningen i underkanten

Originalet kom med en uttoning mot vitt, så att bilden skulle lösas upp i sidans yta i stället för
att kapas. Rätt tanke, fel plats: en **vit** uttoning är rätt i exakt ett av två teman, och i mörkt
läge hade den lyst som ett band längs kanten.

Den bakade uttoningen är därför bortbeskuren — bilden är klippt till 1254×990, ovanför den — och
uttoningen ritas i stället i `HomeHero`, mot `SurfacePage`. Då går den mot det som faktiskt ligger
under bilden, vilket tema den än råkar visas i.

En bild som byter in behöver alltså ingen egen uttoning. Har den en bör den beskäras bort.

## Licens

Se [`hero-licenses.txt`](hero-licenses.txt). En bild utan rad i den filen hör inte hemma i appen,
samma regel som för terrängbilderna.
