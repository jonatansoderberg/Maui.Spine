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
b["parameters"]["outputLevel"] += [{"node": "swirlA", "property": "opacity", "min": 0.5, "max": 1}]

json.dump(scene, open(f'{d}/models/aurora.avatar2d.json', 'w'), indent=1)
json.dump(b, open(f'{d}/bindings/skia.json', 'w'), indent=1)
print('nodes', len(nodes))
