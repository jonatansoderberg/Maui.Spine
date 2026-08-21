# Arenabilder — Python-prototypen

Referensimplementationen av tävlingsbildsrenderingen. Den är **facit** för C#-porten som
beskrivs i [`docs/arenabilder-till-csharp.md`](../../docs/arenabilder-till-csharp.md), och
ska stå kvar tills porten är klar och mätt mot den.

## Kedjan

```
Eventor          arena, tävlingsområde, datum, gren
   ↓
Lantmäteriet     markhöjdmodell 1 m (COG) + ortofoto 0,25 m (WMS)
   ↓
render.py        voxel-strålmarsch -> naken terräng, mur, arenaljus
   ↓
gpt-image-2      images.edit, 1920x1088
   ↓
tavlingsbild.py  vimpel och gräns i bildplanet, efter AI-passet
```

## Kom igång

```bash
python3 -m venv venv && ./venv/bin/pip install -r requirements.txt
./venv/bin/python tavlingsbild.py 59691          # bara rendering
./venv/bin/python forbattra.py 59691             # hela kedjan
```

Inloggningar läses ur filer du äger och som koden aldrig skriver:

```
~/.config/lantmateriet.env    LM_USER=, LM_PASS=    Geotorget, behörighet till
                                                    "Markhöjdmodell Nedladdning"
~/.config/openai.env          OPENAI_API_KEY=
```

## Det som är lätt att göra fel

**`dl1.lantmateriet.se` svarar sporadiskt 403** på fullt giltiga anrop — lastbalansering
där inte alla noder känner sessionen. Allt som hämtar därifrån måste återförsöka.
GDAL:s `/vsicurl` ger upp direkt och utan insyn, vilket är varför rutorna hämtas för hand.

**Markhöjdmodellen har husen bortfiltrerade.** Över bebyggt område är 0,000 % av ytorna
brantare än 45°. Byggnadshöjder finns inte i datat; de kommer från bildmodellen.

**Tävlingsområdet saknas i knappt hälften av tävlingarna**, och arenakoordinaten saknas
helt för indoor. Ingen gräns får ritas när polygonen fattas — att gissa fram en och rita
den är att hitta på arrangörens gränsdragning.

**Bilderna bär ingen text.** Ortofoto och höjddata är CC BY 4.0, så attributionen måste
följa med och visas bredvid bilden i appen.

**AI-passet lägger sig före överlagringarna.** Diffusion mosar tunna linjer och bokstäver,
så vimpel och gräns ritas efter, i bildplanet, med ockluderingstest mot djupbufferten.

## Facit

`referens/checkpoints.json` och `referens/trimtex-24aug-naken.png` skapas av
`gor_referens.py`. Toleranserna står i filen. AI-passet ingår inte — det är inte
reproducerbart, och det är därför murkontrollen finns i stället.
