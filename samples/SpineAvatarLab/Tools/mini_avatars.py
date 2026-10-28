"""Generates the Minis: nine small sksl avatars, flat coloured bodies with two white eyes and no mouth.

Usage: python mini_avatars.py <out-dir>
Writes mini-<colour>.spineavatar for every figure into <out-dir>, with placeholder posters. Pack each one
for shipping (the posters are then rendered from the shader):
    spine-avatar pack <out-dir>/mini-red.spineavatar -o samples/Avatars/mini-red.spineavatar --strip

The shader is mini_avatars.sksl beside this script; each figure fills its outline and constants. The
figures are drawn after a concept row of nine coloured blobs (round, rounded square, egg, dome, pill,
oval, tin, plump, round) whose eyes sit off-centre, as if each blob were turned to one side.
"""
import hashlib, io, json, os, struct, sys, zipfile, zlib

here = os.path.dirname(os.path.abspath(__file__))
template = open(os.path.join(here, "mini_avatars.sksl"), encoding="utf-8").read()

# Outlines in body space: x right, y up, the drawing spans -1..1 and the body stays inside about 0.8 of it
# so squash, stretch and bounce never reach the edge.
SHAPES = {
    "round": "    return length(q) - 0.72;",
    "squircle": "    return roundBox(q, float2(0.66, 0.62), 0.32);",
    "egg": "    float k = 1.0 - 0.18 * clamp(q.y / 0.78, -1.0, 1.0);\n    return ellipse(q, float2(0.6 * k, 0.78));",
    "dome": "    float d = ellipse(q - float2(0.0, -0.5), float2(0.76, 1.06));\n    return smax(d, -0.42 - q.y, 0.12);",
    "pill": "    return roundBox(q, float2(0.8, 0.5), 0.42);",
    "oval": "    float k = 1.0 + 0.1 * clamp(q.y / 0.78, -1.0, 1.0);\n    return ellipse(q, float2(0.6 * k, 0.78));",
    "tin": "    float t = clamp(q.x / 0.68, -1.0, 1.0);\n    float2 r = float2(q.x * (1.0 + 0.06 * q.y), q.y - (q.y > 0.0 ? 0.1 : 0.04) * (1.0 - t * t));\n    return roundBox(r, float2(0.66, 0.6), 0.14);",
    "plump": "    float k = 1.0 - 0.06 * clamp(q.y / 0.68, -1.0, 1.0);\n    return ellipse(q, float2(0.76 * k, 0.68));",
}

# Each outline's lowest point below its centre (mini_eyereach.py prints it): a crouch sinks the figure by
# as much as it shortens it there, so the foot stays on the ground.
FEET = {"round": 0.72, "squircle": 0.619, "egg": 0.78, "dome": 0.42, "pill": 0.499, "oval": 0.78, "tin": 0.585, "plump": 0.68}

# body: light and dark colour; shape; tilt (radians, positive turns counter-clockwise); home: the resting
# look (lon, lat in radians; the shader uses half of it); eyes: dot (round) or pill (tall capsule), half
# width and height; sep: half the angle between the eyes; mapX: how far round the body the eyes travel;
# eyeY: the face's height; bodyY: the body's centre; reach: the furthest the outer eye may turn round the
# body (radians) with both eyes whole inside the outline, at normal and at 1.3x eye size, measured by
# mini_eyereach.py (run it again after changing a shape, the eyes or the face).
FIGURES = [
    dict(id="brown", name="Mini Brown", light="#9A6334", dark="#B07644", shape="round", tilt=0.0,
         home=(-0.75, -0.25), eyes="dot", w=0.16, h=0.135, sep=0.42, mapX=0.64, eyeY=0.0, bodyY=0.0, reach=(0.94, 0.80)),
    dict(id="red", name="Mini Red", light="#F1273A", dark="#FF4454", shape="squircle", tilt=-0.14,
         home=(0.6, -0.35), eyes="pill", w=0.08, h=0.15, sep=0.3, mapX=0.62, eyeY=0.0, bodyY=0.0, reach=(1.11, 1.04)),
    dict(id="orange", name="Mini Orange", light="#F8640A", dark="#FF7A2A", shape="egg", tilt=-0.22,
         home=(0.35, 0.2), eyes="pill", w=0.085, h=0.16, sep=0.34, mapX=0.56, eyeY=0.04, bodyY=0.0, reach=(0.73, 0.60)),
    dict(id="amber", name="Mini Amber", light="#F1920B", dark="#FFA62E", shape="dome", tilt=-0.06,
         home=(0.6, -0.35), eyes="pill", w=0.075, h=0.14, sep=0.32, mapX=0.62, eyeY=-0.02, bodyY=-0.02, reach=(0.64, 0.48)),
    dict(id="green", name="Mini Green", light="#0AC16D", dark="#2AD884", shape="pill", tilt=-0.04,
         home=(-0.8, -0.05), eyes="dot", w=0.14, h=0.14, sep=0.34, mapX=0.76, eyeY=0.0, bodyY=0.0, reach=(0.91, 0.73)),
    dict(id="teal", name="Mini Teal", light="#04AF9B", dark="#20C9B3", shape="oval", tilt=0.12,
         home=(-0.65, -0.3), eyes="dot", w=0.13, h=0.13, sep=0.4, mapX=0.56, eyeY=-0.04, bodyY=0.0, reach=(0.77, 0.67)),
    dict(id="blue", name="Mini Blue", light="#1C7FF5", dark="#4196FF", shape="tin", tilt=-0.06,
         home=(-0.75, -0.2), eyes="dot", w=0.13, h=0.13, sep=0.38, mapX=0.66, eyeY=-0.04, bodyY=0.0, reach=(0.86, 0.78)),
    dict(id="purple", name="Mini Purple", light="#8955F2", dark="#9D70FF", shape="plump", tilt=0.0,
         home=(0.85, -0.6), eyes="pill", w=0.08, h=0.14, sep=0.32, mapX=0.72, eyeY=-0.04, bodyY=0.0, reach=(0.94, 0.82)),
    dict(id="pink", name="Mini Pink", light="#F73096", dark="#FF4FA8", shape="round", tilt=0.0,
         home=(-0.7, -0.05), eyes="dot", w=0.14, h=0.14, sep=0.42, mapX=0.64, eyeY=0.0, bodyY=0.0, reach=(0.98, 0.83)),
]


def f(x):
    return f"{x:.4f}"


def shader(fig, index):
    consts = "\n".join([
        f"const float EYE_DOT = {f(1.0 if fig['eyes'] == 'dot' else 0.0)};",
        f"const float EYE_W = {f(fig['w'])};",
        f"const float EYE_H = {f(fig['h'])};",
        f"const float SEP = {f(fig['sep'])};",
        f"const float MAP_X = {f(fig['mapX'])};",
        f"const float MAP_Y = 0.3;",
        f"const float EYE_Y = {f(fig['eyeY'])};",
        f"const float BODY_Y = {f(fig['bodyY'])};",
        f"const float TILT = {f(fig['tilt'])};",
        f"const float2 HOME = float2({f(fig['home'][0])}, {f(fig['home'][1])});",
        f"const float2 REACH = float2({f(fig['reach'][0])}, {f(fig['reach'][1])});",
        f"const float WOBBLE = 0.006;",
        f"const float BODY_SCALE = 1.1;",
        f"const float FOOT = {f(FEET[fig['shape']])};",
        f"const float PHASE = {f(index * 1.37)};",
    ])
    return template.replace("{{CONSTS}}", consts).replace("{{SHAPE}}", SHAPES[fig["shape"]]).encode()


def png_1x1():
    chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b'\x00\x00\x00\x00\x00')) + chunk(b'IEND', b'')


def package(fig, index, out_dir):
    avatar_id = "mini-" + fig["id"]
    model = f"models/{avatar_id}.sksl"
    manifest = {
        # Runtime 1.1.0: the shader declares gesture uniforms (#696).
        "schemaVersion": "1.0", "id": avatar_id, "displayName": fig["name"], "assetVersion": "1.0.0",
        "minRuntimeVersion": "1.1.0", "profile": "character",
        "representations": [{"id": "skia-shader", "renderer": "skia", "format": "sksl", "model": model}],
        "speech": {"canonicalProfile": "oculus15-v1", "expressionMouthScale": 1},
        "themes": {"slots": {
            "body": {"light": fig["light"], "dark": fig["dark"], "bindings": ["uniform:body"]},
            "eye": {"light": "#FFFFFF", "dark": "#F4F5F7", "bindings": ["uniform:eye"]},
        }},
        "posters": {"light": "previews/poster-light.png", "dark": "previews/poster-dark.png"},
        "license": "LICENSE.txt", "provenance": "provenance.json",
    }
    files = {
        model: shader(fig, index),
        "previews/poster-light.png": png_1x1(),
        "previews/poster-dark.png": png_1x1(),
        "LICENSE.txt": b"Written for Maui.Spine. Same licence as the Maui.Spine repository.\n",
        "provenance.json": json.dumps({"model": "SkSL shader generated by samples/SpineAvatarLab/Tools/mini_avatars.py from mini_avatars.sksl",
                                       "concept": "the owner's row of nine coloured blobs with white eyes (2026-10-27)"}, indent=1).encode(),
        "source/mini_avatars.py": open(__file__, 'rb').read(),
        "source/mini_avatars.sksl": template.encode(),
    }
    manifest["files"] = [{"path": p, "sha256": hashlib.sha256(b).hexdigest(), "bytes": len(b)} for p, b in sorted(files.items())]
    out = io.BytesIO()
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr("avatar.json", json.dumps(manifest, indent=1))
        for p, b in sorted(files.items()):
            z.writestr(p, b)
    path = os.path.join(out_dir, avatar_id + ".spineavatar")
    open(path, 'wb').write(out.getvalue())
    print(path, len(out.getvalue()), "bytes")


if __name__ == "__main__":
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    for i, fig in enumerate(FIGURES):
        package(fig, i, out_dir)
