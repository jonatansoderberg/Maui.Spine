"""Aurora Motion: the round-2 Aurora with what the concept's Pulse Bloom has and it lacks —
continuous swirl, a speaking pulse, a colour per state and glyphs. Lab demo for #524."""
import json, math, sys
d = sys.argv[1]
scene = json.load(open(f'{d}/models/aurora.avatar2d.json'))
b = json.load(open(f'{d}/bindings/skia.json'))

def ellipse(id, w, h, fill, opacity=0.0, blend=None, blur=0, x=0, y=0):
    n = {"id": id, "type": "ellipse", "channel": "ambientPose", "parent": "root",
         "transform": {"x": x, "y": y, "scaleX": 1, "scaleY": 1, "rotation": 0}, "opacity": opacity,
         "fill": fill, "geometry": {"width": w, "height": h}}
    if blend: n["blend"] = blend
    if blur: n["blur"] = blur
    return n

def stop(offset, color=None, slot=None, opacity=1):
    s = {"offset": offset, "opacity": opacity}
    if slot: s["slot"] = slot
    else: s["color"] = color
    return s

# Sweep gradients converge at their centre; soft, wide stops and a strong blur keep that from reading as a pie slice.
swirl_a = ellipse("swirlA", 250, 250, {"sweep": {"cx": 0, "cy": 0, "stops": [
    stop(0, slot="cyan", opacity=0), stop(0.22, slot="cyan", opacity=0.6), stop(0.4, slot="violet", opacity=0.35),
    stop(0.58, slot="blue", opacity=0.05), stop(0.78, slot="blue", opacity=0.5), stop(1, slot="cyan", opacity=0)]}}, opacity=0.5, blend="screen", blur=12)
swirl_b = ellipse("swirlB", 186, 186, {"sweep": {"cx": 0, "cy": 0, "stops": [
    stop(0, slot="violet", opacity=0), stop(0.3, slot="violet", opacity=0.5), stop(0.55, slot="pink", opacity=0.1),
    stop(0.8, slot="cyan", opacity=0.4), stop(1, slot="violet", opacity=0)]}}, opacity=0.4, blend="screen", blur=8)

def tint(id, color, blend="screen"):
    return ellipse(id, 300, 300, {"radial": {"cx": 0, "cy": 0, "r": 150, "stops": [
        stop(0, color, opacity=0.75), stop(0.55, color, opacity=0.45), stop(1, color, opacity=0)]}}, blend=blend)

tints = [tint("tintCyan", "#4DE6FF"), tint("tintViolet", "#9B6BFF"), tint("tintMagenta", "#FF4FD2"), tint("tintGrey", "#9AA3AE", blend=None)]
bars = [{"id": f"pause{i}", "type": "roundedRect", "channel": "ambientPose", "parent": "root",
         "transform": {"x": x, "y": 0, "scaleX": 1, "scaleY": 1, "rotation": 0}, "opacity": 0,
         "fill": {"slot": "white"}, "geometry": {"width": 11, "height": 42, "radius": 4}} for i, x in ((0, -11), (1, 11))]

ids = [n["id"] for n in scene["nodes"]]
nodes = scene["nodes"]
at = ids.index("ribbonCyan")
nodes[at:at] = [swirl_a, swirl_b]
mute = [n["id"] for n in nodes].index("muteSlash")
nodes[mute:mute] = tints + bars

def track(node, prop, keys):
    return {"node": node, "property": prop, "keyframes": [{"seconds": t, "value": v, "easing": e} for t, v, e in keys]}

def spin(node, duration, turns):
    return track(node, "rotation", [(0, 0, "linear"), (duration, 2 * math.pi * turns, "linear")])

A = scene["animations"]
A["idle_a"]["tracks"] += [spin("swirlA", 5.2, 1), spin("swirlB", 5.2, -1)]
A["idle_b"]["tracks"] += [spin("swirlA", 6.4, 1), spin("swirlB", 6.4, -1)]
A["think"]["tracks"] += [spin("swirlA", 5, 3), spin("swirlB", 5, -2)]
A["connect"]["tracks"] += [spin("swirlB", 2.4, 2), spin("swirlA", 2.4, -1)]
A["speak"] = {"durationSeconds": 0.9, "loop": True, "tracks": [
    track("core", "scaleX", [(0, 1, "easeOut"), (0.3, 1.08, "easeInOut"), (0.6, 0.97, "easeInOut"), (0.9, 1, "linear")]),
    track("core", "scaleY", [(0, 1, "easeOut"), (0.3, 1.08, "easeInOut"), (0.6, 0.97, "easeInOut"), (0.9, 1, "linear")]),
    track("innerGlow", "opacity", [(0, 1, "easeOut"), (0.3, 1.45, "easeInOut"), (0.9, 1, "linear")]),
    spin("swirlA", 0.9, 1)]}

# Moving parts. Every loop carries them, because a state loop replaces the idle one: without them the
# arcs would stop whenever Aurora listens or speaks. Rotations are whole turns so loops stay seamless.
A["speak"]["durationSeconds"] = 1.8
for t in A["speak"]["tracks"]:
    if t["property"] == "rotation":
        t["keyframes"][-1]["seconds"] = 1.8
    else:
        # Two pulses per loop instead of one.
        keys = t["keyframes"]
        t["keyframes"] = [dict(k, seconds=round(k["seconds"], 4)) for k in keys] + [dict(k, seconds=round(k["seconds"] + 0.9, 4)) for k in keys[1:]]
A["listen"] = {"durationSeconds": 2.0, "loop": True, "tracks": []}
b["animations"]["listen"] = "listen"
b["stateAnimations"]["listening"] = "listen"

def orbit_offsets(node, duration, turns, keys=12):
    # Flies the node around the orb's centre: offsets from its rest position along a circle.
    rest = next(n for n in nodes if n["id"] == node)["transform"]
    x, y = rest["x"], rest["y"]
    xs, ys = [], []
    for i in range(keys + 1):
        a = 2 * math.pi * turns * i / keys
        xs.append((i * duration / keys, round(x * math.cos(a) - y * math.sin(a) - x, 3), "linear"))
        ys.append((i * duration / keys, round(x * math.sin(a) + y * math.cos(a) - y, 3), "linear"))
    return [track(node, "x", xs), track(node, "y", ys)]

def wobble(node, duration, radius, phase):
    # A small elliptical drift, so the arcs float rather than spin on a pin.
    keys = 8
    xs = [(i * duration / keys, round(radius * math.cos(2 * math.pi * i / keys + phase), 3), "easeInOut") for i in range(keys + 1)]
    ys = [(i * duration / keys, round(radius * 0.7 * math.sin(2 * math.pi * i / keys + phase), 3), "easeInOut") for i in range(keys + 1)]
    xs[-1] = (duration, xs[0][1], "easeInOut"); ys[-1] = (duration, ys[0][1], "easeInOut")
    return [track(node, "x", xs), track(node, "y", ys)]

def breathe(node, duration, amount):
    return [track(node, p, [(0, 1, "easeInOut"), (duration / 2, 1 + amount, "easeInOut"), (duration, 1, "easeInOut")]) for p in ("scaleX", "scaleY")]

# turns per loop: (orbitInput, orbitOutput, ribbons, sparks)
speeds = {"idle_a": (1, -1, 1, 1), "idle_b": (1, -1, 1, 1), "think": (2, -2, 1, 1), "connect": (2, -1, 1, 1),
          "speak": (1, -2, 1, 1), "listen": (2, -1, 1, 1)}
for name, (inp, out, ribbon, spark) in speeds.items():
    dur = A[name]["durationSeconds"]
    A[name]["tracks"] += [spin("orbitInput", dur, inp), spin("orbitOutput", dur, out),
                          spin("ribbonCyan", dur, ribbon), spin("ribbonViolet", dur, -ribbon), spin("innerRibbon", dur, 2 * ribbon)]
    A[name]["tracks"] += wobble("orbitInput", dur, 9, 0) + wobble("orbitOutput", dur, 11, math.pi)
    A[name]["tracks"] += breathe("orbitOutput", dur, 0.06) + breathe("orbitInput", dur, 0.05)
    for i in range(4):
        A[name]["tracks"] += orbit_offsets(f"spark{i}", dur, spark if i % 2 == 0 else -spark)
A["listen"]["tracks"] += breathe("aura", 2.0, 0.08)

P = b["poses"]
P["listening"] += [{"node": "tintCyan", "property": "opacity", "value": 0.6}]
P["thinking"] += [{"node": "tintViolet", "property": "opacity", "value": 0.65},
                  {"node": "root", "property": "scaleX", "value": 0.86}, {"node": "root", "property": "scaleY", "value": 0.86}]
P["speaking"] += [{"node": "root", "property": "scaleX", "value": 1.08}, {"node": "root", "property": "scaleY", "value": 1.08},
                  {"node": "swirlA", "property": "opacity", "value": 0.8}]
P["connecting"] += [{"node": "tintViolet", "property": "opacity", "value": 0.4}, {"node": "swirlB", "property": "opacity", "value": 0.9}]
P["interrupted"] += [{"node": "tintMagenta", "property": "opacity", "value": 0.85},
                     {"node": "pause0", "property": "opacity", "value": 1}, {"node": "pause1", "property": "opacity", "value": 1}]
P["muted"] += [{"node": "tintGrey", "property": "opacity", "value": 0.8},
               {"node": "swirlA", "property": "opacity", "value": 0.08}, {"node": "swirlB", "property": "opacity", "value": 0.08}]
b["animations"]["speak"] = "speak"
b["stateAnimations"]["speaking"] = "speak"
b["parameters"]["outputLevel"] += [{"node": "swirlA", "property": "opacity", "min": 0.5, "max": 1},
                                   {"node": "orbitOutput", "property": "scaleX", "min": 1, "max": 1.25},
                                   {"node": "orbitOutput", "property": "scaleY", "min": 1, "max": 1.25}]
b["parameters"]["inputLevel"] += [{"node": "orbitInput", "property": "scaleX", "min": 1, "max": 1.2},
                                  {"node": "orbitInput", "property": "scaleY", "min": 1, "max": 1.2}]
for i in range(4):
    b["parameters"].setdefault("outputHigh", []).append({"node": f"spark{i}", "property": "opacity", "min": 0.4, "max": 1})

json.dump(scene, open(f'{d}/models/aurora.avatar2d.json', 'w'), indent=1)
json.dump(b, open(f'{d}/bindings/skia.json', 'w'), indent=1)
print('nodes', len(nodes))
