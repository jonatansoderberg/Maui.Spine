"""Generates the Minis: one sksl avatar, mini.spineavatar, whose options pick one of seven hand-drawn shapes
(ball, cube, drop, bowl, bean, inverted-drop, marshmallow) and round or tall eyes, and whose presets are the
nine figures of v1 (brown, red, orange, amber, green, teal, blue, purple, pink). The body colour is the theme
slot `body`, so an app can give any colour (AvatarView.BodyColor).

Usage: python mini_avatars.py <out-dir>
Writes <out-dir>/mini.spineavatar with placeholder posters. Pack it for shipping (the posters are then
rendered from the shader, with the default options):
    spine-avatar pack <out-dir>/mini.spineavatar -o samples/Avatars/mini.spineavatar --strip --size 256

The shader is mini_avatars.sksl beside this script; the outlines and faces below fill it. Each face's eye
reach and foot are measured here from the Python outline (mini_eyereach.py), so they follow any change.
Needs numpy.
"""
import hashlib, io, json, os, struct, sys, zipfile, zlib

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
import mini_eyereach as m

template = open(os.path.join(here, "mini_avatars.sksl"), encoding="utf-8").read()

# The option values, in order: the shader receives the index.
SHAPES = ["ball", "cube", "drop", "bowl", "bean", "inverted-drop", "marshmallow"]
EYES = ["round", "tall"]

# Each outline in body space (x right, y up; the drawing spans -1..1 and the body stays inside about 0.8 of it,
# so squash, stretch and the jump stay in the frame): the SkSL lines that compute `d`, and the same in Python.
OUTLINES = {
    "ball": ("float d = length(q) - 0.72;",
             lambda x, y: m.length(x, y) - 0.72),
    # The cube's corners differ top from bottom and a little side from side, never diagonally: big corners at
    # opposite ends of a diagonal read as a turned box.
    "cube": ("float d = roundBox4(q, float2(0.66, 0.62), float4(0.34, 0.27, 0.31, 0.29));",
             lambda x, y: m.round_box4(x, y, 0.66, 0.62, (0.34, 0.27, 0.31, 0.29))),
    "drop": ("float k = 1.0 - 0.18 * clamp(q.y / 0.78, -1.0, 1.0);\n        float d = ellipse(q, float2(0.6 * k, 0.78));",
             lambda x, y: m.ellipse(x, y, 0.6 * (1 - 0.18 * m.np.clip(y / 0.78, -1, 1)), 0.78)),
    "bowl": ("float d = smax(ellipse(q - float2(0.0, -0.5), float2(0.76, 1.06)), -0.42 - q.y, 0.12);",
             lambda x, y: m.smax(m.ellipse(x, y + 0.5, 0.76, 1.06), -0.42 - y, 0.12)),
    "bean": ("float d = roundBox(q, float2(0.8, 0.5), 0.42);",
             lambda x, y: m.round_box(x, y, 0.8, 0.5, 0.42)),
    "inverted-drop": ("float k = 1.0 + 0.16 * clamp(q.y / 0.78, -1.0, 1.0);\n        float d = ellipse(q, float2(0.58 * k, 0.78));",
                      lambda x, y: m.ellipse(x, y, 0.58 * (1 + 0.16 * m.np.clip(y / 0.78, -1, 1)), 0.78)),
    "marshmallow": ("float t = clamp(q.x / 0.68, -1.0, 1.0);\n        float d = roundBox(float2(q.x * (1.0 + 0.06 * q.y), q.y - (q.y > 0.0 ? 0.1 : 0.04) * (1.0 - t * t)), float2(0.66, 0.6), 0.14);",
                    lambda x, y: (lambda t: m.round_box(x * (1 + 0.06 * y), y - m.np.where(y > 0, 0.1, 0.04) * (1 - t * t), 0.66, 0.6, 0.14))(m.np.clip(x / 0.68, -1, 1))),
}

# The hand-drawn deviation of each outline: amplitudes and phases of harmonics 2, 3, 4 and 5 of the angle, and
# how much fuller one side is (positive: the right). Every shape its own, all small (about 2-3 % of the size),
# static and smooth; the cube's fourth harmonic bows its sides. Harmonics 2-4 are mirror-symmetric (2 and 4 at
# phase ±π/2, 3 at 0 or π): at any other phase they turn the outline into a leaning oval or move its top to one
# side, and the figure looks tilted although it stands straight. The irregularity is in the small fifth harmonic
# and the fuller side.
DRAWN = {
    "ball": dict(amp=(0.008, 0.005, 0.0, 0.003), phase=(1.5708, 3.1416, 0.0, 4.0), fuller=0.008),
    "cube": dict(amp=(0.004, 0.003, 0.009, 0.002), phase=(-1.5708, 0.0, 1.5708, 2.6), fuller=-0.006),
    "drop": dict(amp=(0.007, 0.004, 0.0, 0.003), phase=(1.5708, 3.1416, 0.0, 5.1), fuller=0.006),
    "bowl": dict(amp=(0.005, 0.004, 0.003, 0.002), phase=(1.5708, 0.0, -1.5708, 1.2), fuller=-0.006),
    "bean": dict(amp=(0.008, 0.004, 0.0, 0.003), phase=(-1.5708, 3.1416, 0.0, 3.3), fuller=0.006),
    "inverted-drop": dict(amp=(0.006, 0.005, 0.0, 0.003), phase=(1.5708, 0.0, 0.0, 0.9), fuller=-0.006),
    "marshmallow": dict(amp=(0.004, 0.004, 0.006, 0.003), phase=(-1.5708, 3.1416, 1.5708, 1.6), fuller=0.007),
}

# Each shape's face, from the v1 figure it comes from: the resting look (lon, lat in radians; the shader uses
# 0.8 of it), half the angle between the eyes, how far round the body the eyes travel, the face's height, the
# body's centre, and the eyes' half width and height, round and tall.
FACES = {
    "ball": dict(home=(-0.75, -0.25), sep=0.42, mapX=0.64, eyeY=0.0, bodyY=0.0, round=(0.15, 0.135), tall=(0.08, 0.145)),
    "cube": dict(home=(0.6, -0.35), sep=0.30, mapX=0.62, eyeY=0.0, bodyY=0.0, round=(0.13, 0.125), tall=(0.08, 0.15)),
    "drop": dict(home=(0.35, 0.2), sep=0.34, mapX=0.56, eyeY=0.04, bodyY=0.0, round=(0.13, 0.13), tall=(0.085, 0.16)),
    "bowl": dict(home=(0.6, -0.35), sep=0.32, mapX=0.62, eyeY=0.0, bodyY=-0.02, round=(0.12, 0.115), tall=(0.072, 0.128)),
    "bean": dict(home=(-0.8, -0.05), sep=0.34, mapX=0.76, eyeY=0.0, bodyY=0.0, round=(0.14, 0.14), tall=(0.08, 0.145)),
    "inverted-drop": dict(home=(-0.65, -0.3), sep=0.40, mapX=0.56, eyeY=-0.04, bodyY=0.0, round=(0.13, 0.13), tall=(0.075, 0.14)),
    "marshmallow": dict(home=(-0.75, -0.2), sep=0.38, mapX=0.66, eyeY=-0.04, bodyY=0.0, round=(0.13, 0.13), tall=(0.08, 0.145)),
}

# The nine figures of v1. One colour serves both modes; the shader lifts it a little in dark.
PRESETS = [
    dict(id="brown", name="Mini Brown", color="#9A6334", shape="ball", eyes="round"),
    dict(id="red", name="Mini Red", color="#F1273A", shape="cube", eyes="tall"),
    dict(id="orange", name="Mini Orange", color="#F8640A", shape="drop", eyes="tall"),
    dict(id="amber", name="Mini Amber", color="#F1920B", shape="bowl", eyes="tall"),
    dict(id="green", name="Mini Green", color="#0AC16D", shape="bean", eyes="round"),
    dict(id="teal", name="Mini Teal", color="#04AF9B", shape="inverted-drop", eyes="round"),
    dict(id="blue", name="Mini Blue", color="#1C7FF5", shape="marshmallow", eyes="round"),
    dict(id="purple", name="Mini Purple", color="#8955F2", shape="ball", eyes="tall"),
    dict(id="pink", name="Mini Pink", color="#F73096", shape="ball", eyes="round"),
]


def sdf(name):
    """The outline as drawn: the geometric shape minus its hand-drawn deviation, as outline() in the shader."""
    geometric, d = OUTLINES[name][1], DRAWN[name]
    return lambda x, y: geometric(x, y) - m.drawn(x, y, d)


def measure():
    """Per shape: eye reach (normal, 1.3x) for round and tall eyes, foot and the drawn deviation."""
    rows, data = [], {}
    for name in SHAPES:
        f, face = sdf(name), FACES[name]
        r = {eyes: (m.reach(f, face, face[eyes], 1.0), m.reach(f, face, face[eyes], 1.3)) for eyes in EYES}
        # The shader eases the look to reach - sep; with no room past the eyes' own spread the eyes could not turn.
        for eyes, (normal, enlarged) in r.items():
            if min(normal, enlarged) < face["sep"] + 0.1:
                raise SystemExit(f"{name} with {eyes} eyes: reach {normal}/{enlarged} leaves no room past sep {face['sep']}; make the eyes smaller or move them")
        data[name] = dict(reach=r, foot=m.foot(f), deviation=m.deviation(DRAWN[name]))
        rows.append(f"{name:14} round {r['round']}  tall {r['tall']}  foot {data[name]['foot']:.3f}  drawn ±{data[name]['deviation']:.3f}")
    measure.data = data
    return rows


def f(x):
    return f"{x:.4f}"


def v(xs):
    return ", ".join(f(x) for x in xs)


def shader():
    measure()
    data = measure.data
    outlines, faces = [], []
    for i, name in enumerate(SHAPES):
        d = DRAWN[name]
        cond = f"s < {i + 0.5:.1f}" if i < len(SHAPES) - 1 else None
        body = (f"        {OUTLINES[name][0]}\n"
                f"        return d - drawn(q, float4({v(d['amp'])}), float4({v(d['phase'])}), {f(d['fuller'])});")
        outlines.append(f"    // {name}\n" + (f"    if ({cond}) {{\n{body}\n    }}" if cond else "    {\n" + body + "\n    }"))
        face, r = FACES[name], data[name]["reach"]
        assign = (f"        home = float2({v(face['home'])}); sep = {f(face['sep'])}; mapX = {f(face['mapX'])};\n"
                  f"        eyeY = {f(face['eyeY'])}; bodyY = {f(face['bodyY'])}; foot = {f(data[name]['foot'])}; phase = {f(i * 1.37)};\n"
                  f"        dotEye = float2({v(face['round'])}); pillEye = float2({v(face['tall'])});\n"
                  f"        reachRound = float2({v(r['round'])}); reachTall = float2({v(r['tall'])});")
        faces.append(("    if" if i == 0 else "    } else if") + f" (shape < {i + 0.5:.1f}) {{  // {name}\n{assign}" if cond
                     else f"    }} else {{  // {name}\n{assign}\n    }}")
    return template.replace("{{OUTLINES}}", "\n".join(outlines)).replace("{{FACES}}", "\n".join(faces)).encode()


def png_1x1():
    chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b'\x00\x00\x00\x00\x00')) + chunk(b'IEND', b'')


def package(out_dir):
    model = "models/mini.sksl"
    default = PRESETS[0]
    manifest = {
        # Format 1.1 for options and presets; runtime 1.1.0 for them and for the gesture uniforms.
        "schemaVersion": "1.1", "id": "mini", "displayName": "Mini", "assetVersion": "2.0.0",
        "minRuntimeVersion": "1.1.0", "profile": "character",
        "representations": [{"id": "skia-shader", "renderer": "skia", "format": "sksl", "model": model}],
        "speech": {"canonicalProfile": "oculus15-v1", "expressionMouthScale": 1},
        "themes": {"slots": {
            "body": {"light": default["color"], "dark": default["color"], "bindings": ["uniform:body"]},
            "eye": {"light": "#FFFFFF", "dark": "#F4F5F7", "bindings": ["uniform:eye"]},
        }},
        "options": {
            "shape": {"displayName": "Shape", "values": SHAPES, "default": default["shape"], "bindings": ["uniform:shape"]},
            "eyes": {"displayName": "Eyes", "values": EYES, "default": default["eyes"], "bindings": ["uniform:eyeStyle"]},
        },
        "presets": {p["id"]: {"displayName": p["name"], "options": {"shape": p["shape"], "eyes": p["eyes"]},
                              "themes": {"body": {"light": p["color"], "dark": p["color"]}}} for p in PRESETS},
        "posters": {"light": "previews/poster-light.png", "dark": "previews/poster-dark.png"},
        "license": "LICENSE.txt", "provenance": "provenance.json",
    }
    files = {
        model: shader(),
        "previews/poster-light.png": png_1x1(),
        "previews/poster-dark.png": png_1x1(),
        "LICENSE.txt": b"Written for Maui.Spine. Same licence as the Maui.Spine repository.\n",
        "provenance.json": json.dumps({"model": "SkSL shader generated by samples/SpineAvatarLab/Tools/mini_avatars.py from mini_avatars.sksl",
                                       "concept": "the owner's row of nine coloured blobs with white eyes (2026-10-27), and the owner's review of v1 (2026-10-28)"}, indent=1).encode(),
        "source/mini_avatars.py": open(__file__, 'rb').read(),
        "source/mini_eyereach.py": open(os.path.join(here, "mini_eyereach.py"), 'rb').read(),
        "source/mini_avatars.sksl": template.encode(),
    }
    manifest["files"] = [{"path": p, "sha256": hashlib.sha256(b).hexdigest(), "bytes": len(b)} for p, b in sorted(files.items())]
    out = io.BytesIO()
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr("avatar.json", json.dumps(manifest, indent=1))
        for p, b in sorted(files.items()):
            z.writestr(p, b)
    path = os.path.join(out_dir, "mini.spineavatar")
    open(path, 'wb').write(out.getvalue())
    print(path, len(out.getvalue()), "bytes")


if __name__ == "__main__":
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    for row in measure():
        print(row)
    package(out_dir)
