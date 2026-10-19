"""Packages Spine's built-in bars and ring, ambient spine2d avatars that show a voice's sound in and out (#630).

Usage: python voice_visuals.py <out-dir>
Writes bars.spineavatar and ring.spineavatar with placeholder posters; ship them through the spine-avatar tool
(`pack <file> -o <out> --strip`), which renders the posters with the Skia renderer.

Each band of the spectrum is its own node, scaled by a per-band parameter (inputBand0-23, outputBand0-23): the answer's
bands in the `output` slot's colour, the microphone's in `accent`'s, which follows the app's accent; AvatarView.SetThemeColor("output", …)
and AccentColor change them. Under Reduce Motion the bands give way to one level per side.
"""
import hashlib, io, json, math, os, struct, sys, zipfile, zlib

BANDS = 24
STATES = ("idle", "connecting", "listening", "thinking", "speaking", "interrupted", "muted")
EXPRESSIONS = ("neutral", "happy", "curious", "thinking", "concerned", "surprised", "apologetic", "confident")
LIVE = ("listening", "thinking", "speaking", "interrupted")
INPUT = {"light": "#1E88E5", "dark": "#64B5F6"}      # the app's accent replaces it
OUTPUT = {"light": "#FF7A45", "dark": "#FF8A5C"}     # warm, for the answer
SLIVER = 0.04                                         # a band at silence still reads as a bar


def node(id, type="group", parent=None, x=0.0, y=0.0, sx=1.0, sy=1.0, rotation=0.0, opacity=1.0, slot=None, w=0.0, h=0.0, path=None, stroke=None):
    n = {"id": id, "type": type, "channel": "ambientPose",
         "transform": {"x": x, "y": y, "scaleX": sx, "scaleY": sy, "rotation": rotation}, "opacity": opacity}
    if parent:
        n["parent"] = parent
    if slot:
        n["fill"] = {"slot": slot}
    if stroke:
        n["stroke"] = {"width": stroke[0], "slot": stroke[1], "cap": "round"}
    if type == "path":
        n["geometry"] = {"path": path}
    elif type != "group":
        n["geometry"] = {"width": w, "height": h}
    return n


def capsule(length, thickness, across=0.0):
    """A bar from x 0 to length, centred at y = across: a scale on its node grows it from x 0, its inner end."""
    h, r = thickness / 2, min(thickness / 2, length / 2)
    f = lambda v: f"{v:.2f}"
    a, b = across - h, across + h
    return (f"M {f(r)},{f(a)} L {f(length - r)},{f(a)} Q {f(length)},{f(a)} {f(length)},{f(across)} Q {f(length)},{f(b)} {f(length - r)},{f(b)} "
            f"L {f(r)},{f(b)} Q 0,{f(b)} 0,{f(across)} Q 0,{f(a)} {f(r)},{f(a)} Z")


def still():
    # The scheduler's springs (squash, tilt, lift) move the first root node; an empty one keeps the spectrum level.
    return node("still")


UP, DOWN = -math.pi / 2, math.pi / 2


def bars():
    width, height, half = 300.0, 200.0, 90.0
    gap = 3.0
    bar = (width - gap * (BANDS - 1)) / BANDS
    xs = [-width / 2 + b * (bar + gap) + bar / 2 for b in range(BANDS)]
    nodes = [still(), node("root", x=width / 2, y=height / 2)]
    nodes.append(node("midline", "path", "root", slot="accent", opacity=0.2, path=capsule(width, 1.0, 0), x=-width / 2))
    parameters = {}
    for b, x in enumerate(xs):
        # A bar turned to point up (the answer) or down (the microphone) from the midline, and scaled along it.
        nodes.append(node(f"out{b}", "path", "root", x=x, rotation=UP, sx=SLIVER, slot="output", path=capsule(half, bar)))
        nodes.append(node(f"in{b}", "path", "root", x=x, rotation=DOWN, sx=SLIVER, slot="accent", path=capsule(half, bar)))
        parameters[f"outputBand{b}"] = [{"node": f"out{b}", "property": "scaleX", "min": SLIVER, "max": 1}]
        parameters[f"inputBand{b}"] = [{"node": f"in{b}", "property": "scaleX", "min": SLIVER, "max": 1}]
    # Reduce Motion: the same row of bars in one still bell shape, rising with the level instead of the spectrum.
    # Turned up, a bar's screen x is its path's y; turned down, minus it.
    bell = [0.35 + 0.65 * math.exp(-((b - (BANDS - 1) / 2) / (BANDS / 4)) ** 2) for b in range(BANDS)]
    up = " ".join(capsule(half * k, bar, x) for k, x in zip(bell, xs))
    down = " ".join(capsule(half * k, bar, -x) for k, x in zip(bell, xs))
    nodes.append(node("outLevel", "path", "root", rotation=UP, sx=0.2, opacity=0, slot="output", path=up))
    nodes.append(node("inLevel", "path", "root", rotation=DOWN, sx=0.2, opacity=0, slot="accent", path=down))
    parameters["outputLevel"] = [{"node": "outLevel", "property": "scaleX", "min": 0.2, "max": 1}]
    parameters["inputLevel"] = [{"node": "inLevel", "property": "scaleX", "min": 0.2, "max": 1}]

    bands = [f"out{b}" for b in range(BANDS)] + [f"in{b}" for b in range(BANDS)]
    reduced = [{"node": n, "property": "opacity", "value": 0} for n in bands]
    reduced += [{"node": n, "property": "opacity", "value": 1} for n in ("outLevel", "inLevel")]
    muted = [{"node": f"in{b}", "property": "opacity", "value": 0.3} for b in range(BANDS)]
    return "bars", "Bars", width, height, nodes, parameters, reduced, muted


def ring():
    size = 300.0
    radius, reach, gap, thickness = size * 0.33, size * 0.14, 3.0, 3.0
    ticks = BANDS * 2
    nodes = [still(), node("root", x=size / 2, y=size / 2)]
    # The circle the ticks stand on; the middle stays empty for what the ring surrounds.
    nodes.append(node("circle", "ellipse", "root", opacity=0.28, w=2 * radius, h=2 * radius, stroke=(1.5, "accent")))
    parameters = {}
    for t in range(ticks):
        # The bands run up one side and down the other, so the ring is symmetric and low notes meet at the top.
        band = t if t < BANDS else ticks - 1 - t
        angle = -math.pi / 2 + t * 2 * math.pi / ticks
        cos, sin = math.cos(angle), math.sin(angle)
        # Each tick starts at its inner end, turned to point outward (the microphone's inward) and scaled along it.
        nodes.append(node(f"out{t}", "path", "root", x=cos * (radius + gap), y=sin * (radius + gap), rotation=angle, sx=SLIVER, slot="output", path=capsule(reach, thickness)))
        nodes.append(node(f"in{t}", "path", "root", x=cos * (radius - gap), y=sin * (radius - gap), rotation=angle + math.pi, sx=SLIVER, slot="accent", path=capsule(reach * 0.7, thickness)))
        parameters.setdefault(f"outputBand{band}", []).append({"node": f"out{t}", "property": "scaleX", "min": SLIVER, "max": 1})
        parameters.setdefault(f"inputBand{band}", []).append({"node": f"in{t}", "property": "scaleX", "min": SLIVER, "max": 1})
    # Reduce Motion: a ring of the answer's colour outside the circle and the microphone's inside, each widening with its level.
    nodes.append(node("outLevel", "ellipse", "root", opacity=0, w=2 * radius + 6, h=2 * radius + 6, stroke=(3, "output")))
    nodes.append(node("inLevel", "ellipse", "root", opacity=0, w=2 * radius - 6, h=2 * radius - 6, stroke=(3, "accent")))
    parameters["outputLevel"] = [{"node": "outLevel", "property": "scaleX", "min": 1, "max": 1.1},
                                 {"node": "outLevel", "property": "scaleY", "min": 1, "max": 1.1}]
    parameters["inputLevel"] = [{"node": "inLevel", "property": "scaleX", "min": 1, "max": 0.9},
                                {"node": "inLevel", "property": "scaleY", "min": 1, "max": 0.9}]

    bands = [f"out{t}" for t in range(ticks)] + [f"in{t}" for t in range(ticks)]
    reduced = [{"node": n, "property": "opacity", "value": 0} for n in bands]
    reduced += [{"node": n, "property": "opacity", "value": 1} for n in ("outLevel", "inLevel")]
    muted = [{"node": f"in{t}", "property": "opacity", "value": 0.3} for t in range(ticks)] + [{"node": "inLevel", "property": "opacity", "value": 0.3}]
    return "ring", "Ring", size, size, nodes, parameters, reduced, muted


def png_1x1():
    chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b'\x00\x00\x00\x00\x00')) + chunk(b'IEND', b'')


def package(out_dir, id, name, width, height, nodes, parameters, reduced, muted):
    poses = {s: [] for s in STATES}
    poses["muted"] = muted
    poses["reduced_motion"] = reduced
    # A spectrum has no face: every expression looks the same.
    poses.update({"expr_" + e: [] for e in EXPRESSIONS})
    bindings = {
        "schemaVersion": "1.0", "renderer": "skia", "poses": poses, "parameters": parameters,
        "channelMasks": {}, "framing": {"safeInset": 0.02, "fit": "contain"},
    }
    model = {"schemaVersion": "1.1", "bounds": {"width": width, "height": height}, "nodes": nodes,
             "pathPoses": {}, "animations": {}, "defaultPose": []}
    def bound(slot):
        return sorted([f"{n['id']}.fill" for n in nodes if n.get("fill", {}).get("slot") == slot]
                      + [f"{n['id']}.stroke" for n in nodes if n.get("stroke", {}).get("slot") == slot])
    # The microphone's side is the accent slot, so it follows the app's accent (AvatarView.AccentColor) like the rest of Spine.
    slots = {"accent": {**INPUT, "bindings": bound("accent")}, "output": {**OUTPUT, "bindings": bound("output")}}
    features = ["strokes"] if any("stroke" in n for n in nodes) else []
    manifest = {
        "schemaVersion": "1.0", "id": id, "displayName": name, "assetVersion": "1.0.0", "minRuntimeVersion": "1.0.0",
        "profile": "ambient",
        "representations": [{
            "id": "skia-primary", "renderer": "skia", "format": "spine2d",
            "model": f"models/{id}.avatar2d.json", "bindings": "bindings/skia.json", "requiredFeatures": features,
            "blinkOwner": "none", "gazeOwner": "none",
        }],
        # Duplex: both sides react whenever the conversation is live, so talking over the answer shows both at once.
        "states": {s: {"pose": s, "transitionMs": 180, "gaze": "none", "inputReactive": s in LIVE, "outputReactive": s in LIVE} for s in STATES},
        "speech": {"releaseMs": 90},
        "themes": {"slots": slots},
        "reducedMotion": {"pose": "reduced_motion", "retainSpeech": False, "transitionMs": 180},
        "posters": {"light": "previews/poster-light.png", "dark": "previews/poster-dark.png"},
        "license": "LICENSE.txt", "provenance": "provenance.json",
    }
    files = {
        f"models/{id}.avatar2d.json": json.dumps(model, indent=1).encode(),
        "bindings/skia.json": json.dumps(bindings, indent=1).encode(),
        "previews/poster-light.png": png_1x1(),
        "previews/poster-dark.png": png_1x1(),
        "LICENSE.txt": b"Built into Spine. Same licence as the Maui.Spine repository.\n",
        "provenance.json": json.dumps({"created": "2026-10-19", "generator": "samples/SpineAvatarLab/Tools/voice_visuals.py",
                                       "source": "Procedural, after Spine's former Skia bars and ring (#630)", "externalAssets": []}, indent=1).encode(),
        "source/voice_visuals.py": open(__file__, 'rb').read(),
    }
    manifest["files"] = [{"path": p, "sha256": hashlib.sha256(b).hexdigest(), "bytes": len(b)} for p, b in sorted(files.items())]
    out = io.BytesIO()
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr("avatar.json", json.dumps(manifest, indent=1))
        for p, b in sorted(files.items()):
            z.writestr(p, b)
    path = os.path.join(out_dir, f"{id}.spineavatar")
    open(path, 'wb').write(out.getvalue())
    print(path, len(out.getvalue()), "bytes")


if __name__ == "__main__":
    for build in (bars, ring):
        package(sys.argv[1], *build())
