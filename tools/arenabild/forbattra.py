#!/usr/bin/env python3
"""Skickar en tävlingsbild genom OpenAI:s bildredigering och lägger tillbaka överlagringarna.

    ./venv/bin/python forbattra.py 59691 [--when 2027-03-03T13:00]

Nyckeln läses ur OPENAI_API_KEY eller ur ~/.config/openai.env — en fil du äger och
som koden läser men aldrig skriver:

    printf 'OPENAI_API_KEY=sk-...\\n' > ~/.config/openai.env && chmod 600 ~/.config/openai.env

Ordningen är avsiktlig. Terrängen förbättras naken, utan gräns, vimpel eller text —
diffusion mosar tunna linjer och bokstäver, så överlagringarna läggs på efteråt.
"""
import base64, datetime as dt, io, os, sys, time
import numpy as np
from PIL import Image
import eventor, prompt, sol, render as R, tavlingsbild as TB

MODEL = os.environ.get("OPENAI_IMAGE_MODEL", "gpt-image-2")
# gpt-image-2 tar valfri upplösning, men sidorna måste vara multiplar av 16.
# 1920x1080 är det inte (1080 = 67,5 * 16); 1088 är närmaste giltiga och ger 16:9
# med 0,7 procents fel.
SIZE = (1920, 1088)


def api_key():
    key = os.environ.get("OPENAI_API_KEY")
    if key:
        return key
    path = os.path.expanduser(os.environ.get("OPENAI_CREDS", "~/.config/openai.env"))
    if os.path.exists(path):
        for line in open(path):
            if line.startswith("OPENAI_API_KEY="):
                return line.split("=", 1)[1].strip()
    sys.exit("ingen nyckel — sätt OPENAI_API_KEY eller skapa ~/.config/openai.env")


def enhance(png_bytes, text, key):
    from openai import OpenAI
    client = OpenAI(api_key=key)
    buf = io.BytesIO(png_bytes)
    buf.name = "render.png"
    kwargs = dict(model=MODEL, image=buf, prompt=text,
                  size=f"{SIZE[0]}x{SIZE[1]}", input_fidelity="high")
    try:
        result = client.images.edit(**kwargs)
    except Exception as e:
        # gpt-image-2 avvisar input_fidelity med 400, inte med TypeError. Utan den
        # blir terrängen lösare, och det ska synas i loggen i stället för att
        # upptäckas i bilden långt senare.
        if "input_fidelity" not in str(e):
            raise
        print("  input_fidelity stöds inte av modellen — kör utan")
        kwargs.pop("input_fidelity")
        result = client.images.edit(**kwargs)
    return base64.b64decode(result.data[0].b64_json)


def main(event_id, when_override=None):
    t0 = time.time()
    key = api_key()

    # 1. Naken render i det format modellen accepterar.
    TB.W, TB.H = SIZE
    bare = f"ai-{event_id}-ren.png"
    TB.main(event_id, when_override=when_override, tag="ren-ai", bare=True)
    os.replace(next(f for f in os.listdir(".") if f.startswith(f"tavlingsbild-{event_id}-ren-ai")), bare)

    # 2. Prompten ur tävlingens eget ljus.
    ev = eventor.fetch(event_id)
    when = when_override or ev["when_dt"]
    alt, az = sol.solar_position(*ev["arena"], when)
    light = R.lighting(alt, az)
    lamp = R.lit_arena(light)
    text = prompt.enhancement(ev, sol.season(when), light, sol.format_when(when), lamp=lamp)
    print(f"  modell {MODEL} · {SIZE[0]}x{SIZE[1]} · {light['label']}"
          + ("  · arenaljus i prompten" if lamp else ""))

    # 3. Förbättra.
    out = f"ai-{event_id}-{when:%d%b-%H%M}".lower() + ".png"
    png = enhance(open(bare, "rb").read(), text, key)
    open(out, "wb").write(png)
    print(f"  AI-svar mottaget ({time.time()-t0:.1f} s)")

    # 4. Överlagringarna ovanpå, i bildplanet.
    TB.main(event_id, enhanced=out, when_override=when_override, tag=f"ai-{when:%d%b}".lower())
    print(f"-> klart ({time.time()-t0:.1f} s)")


if __name__ == "__main__":
    a = sys.argv[1:]
    w = next((dt.datetime.fromisoformat(x.split("=", 1)[1])
              for x in a if x.startswith("--when=")), None)
    main(int(a[0]) if a and a[0].isdigit() else 59691, w)
