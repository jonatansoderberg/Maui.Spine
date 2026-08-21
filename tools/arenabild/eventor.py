"""Läser tävlingsuppgifter ur Eventors publika sida.

Arenans position finns alltid. Tävlingsområdets polygon ligger i sidan som
"förbjudet område" — men den är frivillig för arrangören, och saknas i knappt
hälften av tävlingarna. Anropare måste klara `area=None`.
"""
import html as _html
import re
import urllib.request

import sol

BASE = "https://eventor.orientering.se/Events/Show/"
FIELDS = ["Tävling", "Arrangörsorganisation", "Distrikt", "Datum",
          "Tävlingsdistans", "Tävlingstidpunkt"]


def _text(raw):
    t = re.sub(r"<(script|style).*?</\1>", " ", raw, flags=re.S | re.I)
    t = re.sub(r"<[^>]+>", "\n", t)
    return [x for x in (l.strip() for l in _html.unescape(t).split("\n")) if x]


def _is_night(vals):
    """Nattävling? Namnet och tävlingstidpunkten avgör.

    Eventor lämnar ofta starttiden tom, och då säger klockan ingenting. En tävling
    som heter "DM, natt" springs i mörker oavsett vad datumfältet utelämnar.
    """
    text = " ".join(vals.get(k, "") for k in ("Tävling", "Tävlingstidpunkt")).lower()
    return "natt" in text


def fetch(event_id):
    req = urllib.request.Request(f"{BASE}{event_id}",
                                 headers={"User-Agent": "orientera-prototype/0.1"})
    with urllib.request.urlopen(req, timeout=45) as r:
        raw = r.read().decode("utf-8", "replace")

    lines = _text(raw)
    vals = {}
    for label in FIELDS:
        for i, l in enumerate(lines):
            if l == label and i + 1 < len(lines):
                vals[label] = lines[i + 1]
                break

    # Arenan: sidans kartcentrum. Citattecknen runt värdet skiljer den från
    # polygonens hörn, som ligger som rena tal.
    m = re.search(r'centerLatitude&quot;:&quot;([-\d.]+)', raw)
    m2 = re.search(r'centerLongitude&quot;:&quot;([-\d.]+)', raw)
    arena = (float(m.group(1)), float(m2.group(1))) if m and m2 else None

    pts = re.findall(r'Longitude&quot;:([-\d.]+),&quot;Latitude&quot;:([-\d.]+)', raw)
    area = [(float(la), float(lo)) for lo, la in pts]
    if len(area) > 2 and area[0] == area[-1]:
        area = area[:-1]                     # sluten ring: sista hörnet upprepar det första

    return {
        "id": event_id,
        "name": vals.get("Tävling", f"Tävling {event_id}"),
        "club": vals.get("Arrangörsorganisation", ""),
        "district": vals.get("Distrikt", ""),
        "when": vals.get("Datum", "").replace(" klockan ", ", "),
        "distance": vals.get("Tävlingsdistans", ""),
        "night_race": _is_night(vals),
        "url": f"{BASE}{event_id}",
        "when_dt": sol.parse_when(vals.get("Datum", ""),
                                  night=_is_night(vals)),
        "arena": arena,
        "area": area if len(area) >= 3 else None,
    }
