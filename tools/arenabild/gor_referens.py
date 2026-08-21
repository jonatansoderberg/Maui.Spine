#!/usr/bin/env python3
"""Skapar facit som C#-porten mäts mot.

Bara de deterministiska stegen. AI-passet är inte reproducerbart och kan därför inte vara
facit — det är just därför murkontrollen finns i stället.
"""
import datetime as dt, json, pathlib
from PIL import Image
import eventor, sol, terrain as T, tavlingsbild as TB

HIT = pathlib.Path(__file__).parent / "referens"
EID, WHEN = 59691, dt.datetime(2026, 8, 24, 18, 30)

ev = eventor.fetch(EID)
area = [T.sweref(la, lo) for la, lo in ev["area"]]
arena = T.sweref(*ev["arena"])
alt, az = sol.solar_position(*ev["arena"], WHEN)

b = TB.frame_bounds(area, TB.CAM["azimuth"], TB.CAM["pitch"], TB.CAM["fill"],
                    TB.CAM["reach"], TB.CAM["back"], 1920, 1088)
w, h = int((b[2] - b[0]) / TB.RES), int((b[3] - b[1]) / TB.RES)
dem = T.elevation_lm(b, w, h)

TB.W, TB.H = 1920, 1088
TB.main(EID, when_override=WHEN, tag="referens", bare=True, wall=True)
src = pathlib.Path(f"tavlingsbild-{EID}-referens.png")
# Nedskalad: kantkorrelationen mäts ändå på 960 px, och repot ska inte bära 4 MB.
Image.open(src).resize((960, 544), Image.LANCZOS).save(HIT / "trimtex-24aug-naken.png")
src.unlink()

(HIT / "checkpoints.json").write_text(json.dumps({
    "kommentar": "Facit för C#-porten. Se docs/arenabilder-till-csharp.md.",
    "tavling": {"id": EID, "namn": ev["name"], "tid": WHEN.isoformat()},
    "projektion": {
        "arena_wgs84": list(ev["arena"]),
        "arena_sweref99tm": [round(arena[0], 3), round(arena[1], 3)],
        "tolerans_m": 1.0,
    },
    "sol": {"hojd_grader": round(alt, 4), "azimut_grader": round(az, 4),
            "tolerans_grader": 0.05, "arstid": sol.season(WHEN)},
    "ram_sweref99tm": [round(v, 2) for v in b],
    "hojdmodell": {
        "grid": [h, w], "upplosning_m": TB.RES,
        "min_m": round(float(dem.min()), 3), "max_m": round(float(dem.max()), 3),
        "medel_m": round(float(dem.mean()), 3), "std_m": round(float(dem.std()), 3),
        "tolerans_m": 0.01,
    },
    "bild": {"fil": "trimtex-24aug-naken.png",
             "matt": "kantkorrelation mot porten, krav > 0.98"},
}, ensure_ascii=False, indent=2), encoding="utf-8")
print((HIT / "checkpoints.json").read_text())
