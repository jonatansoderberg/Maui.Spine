"""Generates Nova, the floating robot companion from the concept sheet, for the Spine Avatar Lab (#524).

Usage: python nova.py <out-dir> [poster-light.png poster-dark.png]
Writes <out-dir>/nova.spineavatar. Needs numpy and Pillow; shares geometry helpers with plush_avatars.py.

Glossy white shell, a black glass visor inset into it, and a face of light on the visor: eyes and
mouth are emissive bands projected onto the visor whose morph targets give the expressions (ovals,
happy arches, wide, closed, squint) and the visemes. Ear pods with glowing rings slide out with the
microphone level. A small floating body below. Connecting spins the whole robot once a loop inside
rings of light; interrupted raises a red mark; muted closes the eyes and shows a mic-off badge.
"""
import hashlib, io, json, math, os, sys, zipfile
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from plush_avatars import (ellipsoid, smin, smax, gradient, ray_surface, front_point, frame, uv_sphere,
                           ellipsoid_mesh, srgb_to_linear, Glb, quat, wave, tiny_png)
from face_styles import torus_mesh

FEET = -0.78
HEAD_C = np.array([0, 0.18, 0])
BODY_C = np.array([0, -0.55, 0])
EAR_X, EAR_Y = 0.64, 0.13
VISOR_C, VISOR_HALF, VISOR_N = (0.0, 0.15), (0.47, 0.31), 2.6

# ---------------------------------------------------------------- shapes

def superellipsoid(p, c, r, n):
    q = np.abs(p - np.asarray(c)) / np.asarray(r)
    f = (q[..., 0] ** n + q[..., 1] ** n + q[..., 2] ** n) ** (1 / n)
    return (f - 1) * min(r) * 0.9

def head(p):
    # A wide, slightly flattened egg, as on the concept sheet.
    return superellipsoid(p, HEAD_C, (0.66, 0.5, 0.52), 2.25)

def ear(p, side):
    q = p - np.array([side * EAR_X, EAR_Y, 0])
    radial = np.hypot(q[..., 1], q[..., 2]) - 0.16
    along = np.abs(q[..., 0]) - 0.055
    outside = np.hypot(np.maximum(radial, 0), np.maximum(along, 0))
    return outside + np.minimum(np.maximum(radial, along), 0) - 0.025

def body(p):
    # A small bell: rounded shoulders, a waist and a short point below, with stubby arms out to the sides.
    d = smin(superellipsoid(p, (0, -0.5, 0), (0.17, 0.1, 0.14), 2.4), ellipsoid(p, (0, -0.59, 0), (0.12, 0.11, 0.1)), 0.06)
    d = smin(d, ellipsoid(p, (0, -0.68, 0), (0.05, 0.045, 0.05)), 0.05)
    for s in (-1, 1):
        q = p - np.array([s * 0.19, -0.53, 0.0])
        c, sn = math.cos(s * 1.0), math.sin(s * 1.0)
        q = np.stack([c * q[..., 0] - sn * q[..., 1], sn * q[..., 0] + c * q[..., 1], q[..., 2]], axis=-1)
        d = smin(d, ellipsoid(q, (0, 0, 0), (0.04, 0.075, 0.04)), 0.03)
    return d

# ---------------------------------------------------------------- meshes

def sampled(sdf, centre, rings=96, segments=144):
    d, uv, idx = uv_sphere(rings, segments)
    t = ray_surface(sdf, np.asarray(centre), d, far=1.4)
    p = np.asarray(centre) + d * t[:, None]
    return dict(pos=p, nrm=gradient(sdf, p), uv=uv, idx=idx)

def split(mesh, classify):
    tri = mesh["idx"].reshape(-1, 3)
    materials = classify(mesh["pos"][tri].mean(axis=1))
    return [dict(pos=mesh["pos"], nrm=mesh["nrm"], uv=mesh["uv"], idx=tri[materials == m].reshape(-1).astype(np.uint32), material=int(m))
            for m in np.unique(materials)]

COLS, ROWS = 28, 7

def outline(w, cu=0.0, cl=0.0, top=0.0, bottom=0.0, line=0.0, shift=(0.0, 0.0)):
    """A filled band between two curves: corners at ±w, each curve dips by cu/cl at the centre,
    top/bottom open it in an oval, line is the least half-thickness."""
    s = -np.cos(np.linspace(0, math.pi, COLS))
    oval = np.sqrt(np.clip(1 - s * s, 0, 1))
    thin = np.clip(1 - s * s, 0, 1) ** 0.3
    upper = -cu * (1 - s * s) + top * oval + line * thin
    lower = -cl * (1 - s * s) - bottom * oval - line * thin
    rows = np.linspace(0, 1, ROWS)
    y = upper[None, :] * (1 - rows[:, None]) + lower[None, :] * rows[:, None] + shift[1]
    x = np.broadcast_to(w * s + shift[0], y.shape)
    return np.stack([x, y], axis=-1).reshape(-1, 2)

def band_indices():
    idx = []
    for r in range(ROWS - 1):
        for c in range(COLS - 1):
            a, b = r * COLS + c, r * COLS + c + 1
            idx += [a, a + COLS, b, b, a + COLS, b + COLS]
    return np.array(idx, np.uint32)

def on_visor(xy, lift=0.02):
    p, n = front_point(head, xy[:, 0], xy[:, 1])
    return p + n * lift, n

EYE_X, EYE_Y = 0.18, 0.17
EYES = {
    # Tall pills at rest, as on the concept sheet.
    "rest": dict(w=0.058, top=0.1, bottom=0.1),
    "blink": dict(w=0.07, line=0.014),
    "happy": dict(w=0.1, cu=-0.075, cl=-0.075, line=0.032, shift=(0, -0.015)),
    "wide": dict(w=0.088, top=0.09, bottom=0.09),
    "closed": dict(w=0.09, cu=0.04, cl=0.04, line=0.026, shift=(0, -0.025)),
    "squint": dict(w=0.064, top=0.05, bottom=0.06, shift=(0, -0.005)),
    # Thinking, one eye per side: the viewer's left eye an arch, the right a small ring looking up.
    "think": ({"w": 0.085, "cu": -0.05, "cl": -0.05, "line": 0.018, "shift": (0, 0.01)}, {"w": 0.062, "top": 0.066, "bottom": 0.066, "shift": (0.01, 0.035)}),
}
LOOK = {"look_left": (-0.04, 0), "look_right": (0.04, 0), "look_up": (0, 0.035), "look_down": (0, -0.03)}

def grown(shape, k):
    """The same eye shape grown by k, for a glow layer behind it."""
    q = dict(shape)
    q["w"] = q["w"] * (1 + (k - 1) * 0.6)
    for key in ("top", "bottom"):
        if key in q: q[key] = q[key] * k
    q["line"] = q.get("line", 0.0) * k + 0.012 * (k - 1) / 0.4
    return q

# Glow: the face shapes again, larger and fainter, behind the core.
# Solid shapes of light; bloom gives the glow (glow layers read as translucent, ghosted eyes).
EYE_LAYERS = [(1.0, 2, 0.02)]

def eyes_mesh():
    names = [k for k in EYES if k != "rest"] + list(LOOK)
    band = band_indices()
    prims = []
    for k_scale, material, lift in EYE_LAYERS:
        pos, nrm, idx, deltas = [], [], [], [[] for _ in names]
        for side in (-1, 1):
            def place(shape, dx=0.0, dy=0.0):
                xy = outline(**grown(shape, k_scale)) + np.array([side * EYE_X + dx, EYE_Y + dy])
                return on_visor(xy, lift)
            rest, n = place(EYES["rest"])
            offset = sum(len(x) for x in pos)
            pos.append(rest); nrm.append(n); idx.append(band + offset)
            for k, name in enumerate(names):
                if name in LOOK:
                    moved, _ = place(EYES["rest"], *LOOK[name])
                elif isinstance(EYES[name], tuple):
                    moved, _ = place(EYES[name][0 if side < 0 else 1])
                else:
                    moved, _ = place(EYES[name])
                deltas[k].append(moved - rest)
        prims.append(dict(pos=np.concatenate(pos), nrm=np.concatenate(nrm), idx=np.concatenate(idx),
                          targets=[np.concatenate(d) for d in deltas], material=material))
    return prims, names

MOUTH_Y = 0.0
# The rest mouth is a point: the mouth appears only as a pose or a viseme opens it.
MOUTHS = {
    "rest": dict(w=0.001, line=0.0005),
    "smile": dict(w=0.12, cu=0.05, cl=0.05, line=0.02),
    "side_o": dict(w=0.024, top=0.024, bottom=0.026, shift=(-0.06, 0.0)),
    "frown": dict(w=0.06, cu=-0.022, cl=-0.022, line=0.01),
    "small": dict(w=0.03, line=0.009),
    "open": dict(w=0.058, cu=0.012, cl=0.03, top=0.012, bottom=0.04, line=0.006),
    "round": dict(w=0.032, top=0.03, bottom=0.032),
    "wide": dict(w=0.075, cu=0.018, cl=0.026, top=0.006, bottom=0.018, line=0.008),
    "press": dict(w=0.05, cu=0.006, cl=0.006, line=0.006),
}

def mouth_mesh():
    names = [k for k in MOUTHS if k != "rest"]
    def place(shape):
        return on_visor(outline(**shape) + np.array([0, MOUTH_Y]))
    rest, n = place(MOUTHS["rest"])
    return dict(pos=rest, nrm=n, idx=band_indices(), targets=[place(MOUTHS[k])[0] - rest for k in names], material=2), names

def visor_edge(segments, scale=1.0):
    a = np.linspace(0, 2 * math.pi, segments, endpoint=False)
    c, sn = np.cos(a), np.sin(a)
    k = 2 / VISOR_N
    return np.stack([np.sign(c) * np.abs(c) ** k * VISOR_HALF[0] * scale, np.sign(sn) * np.abs(sn) ** k * VISOR_HALF[1] * scale], axis=1)

def visor_rim(segments=160, width=0.012, lift=0.008):
    """A thin band of light just inside the visor's edge."""
    inner = visor_edge(segments, 1 - width / VISOR_HALF[1]) + np.array(VISOR_C)
    outer = visor_edge(segments) + np.array(VISOR_C)
    p, n = front_point(head, np.concatenate([inner[:, 0], outer[:, 0]]), np.concatenate([inner[:, 1], outer[:, 1]]))
    idx = []
    for j in range(segments):
        a, b = j, (j + 1) % segments
        idx += [a, segments + a, b, b, segments + a, segments + b]
    return dict(pos=p + n * lift, nrm=n, uv=np.zeros((len(p), 2)), idx=np.array(idx, np.uint32), material=5)

def visor_mesh(rings=24, segments=128, lift=0.006):
    """The glass: a grid over the visor's oval laid just above the shell, so its edge is smooth
    (splitting the shell's triangles by material gave a jagged edge)."""
    edge = visor_edge(segments)
    pts = [np.array([[0.0, 0.0]])] + [edge * (r / rings) for r in range(1, rings + 1)]
    xy = np.concatenate(pts) + np.array(VISOR_C)
    p, n = front_point(head, xy[:, 0], xy[:, 1])
    # A slight dome, so the glass catches highlights.
    radial = np.linalg.norm((xy - np.array(VISOR_C)) / np.array(VISOR_HALF), axis=1)
    p = p + n * (0.012 * (1 - np.clip(radial, 0, 1) ** 2))[:, None]
    idx = [k for j in range(segments) for k in (0, 1 + j, 1 + (j + 1) % segments)]
    for r in range(rings - 1):
        for j in range(segments):
            a0, b0 = 1 + r * segments + j, 1 + r * segments + (j + 1) % segments
            idx += [a0, a0 + segments, b0, b0, a0 + segments, b0 + segments]
    return dict(pos=p + n * lift, nrm=n, uv=np.zeros((len(p), 2)), idx=np.array(idx, np.uint32), material=1)

def disc(radius, segments=48):
    a = np.linspace(0, 2 * math.pi, segments, endpoint=False)
    pos = np.vstack([[0, 0, 0], np.stack([np.cos(a) * radius, np.sin(a) * radius, np.zeros_like(a)], axis=1)])
    idx = [k for j in range(segments) for k in (0, 1 + j, 1 + (j + 1) % segments)]
    return dict(pos=pos, nrm=np.tile([0, 0, 1.0], (len(pos), 1)), idx=np.array(idx, np.uint32))

def turned(mesh, rotation, offset):
    r = np.asarray(rotation)
    return dict(pos=mesh["pos"] @ r.T + np.asarray(offset), nrm=mesh["nrm"] @ r.T, idx=mesh["idx"])

def rot_y(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])

def rot_x(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])

def rot_z(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])

def arc_mesh(R, r, start, end, segments=72, sides=10):
    """Part of a torus in the xy plane, tapering at both ends: a trail of light."""
    pos, nrm, idx = [], [], []
    for i in range(segments + 1):
        t = i / segments
        a = start + (end - start) * t
        taper = math.sin(math.pi * t) ** 0.6
        for j in range(sides):
            b = 2 * math.pi * j / sides
            cx, cy = math.cos(a), math.sin(a)
            rr = r * taper + 1e-4
            pos.append((cx * (R + rr * math.cos(b)), cy * (R + rr * math.cos(b)), rr * math.sin(b)))
            nrm.append((cx * math.cos(b), cy * math.cos(b), math.sin(b)))
    for i in range(segments):
        for j in range(sides):
            a, b = i * sides + j, i * sides + (j + 1) % sides
            idx += [a, a + sides, b, b, a + sides, b + sides]
    return dict(pos=np.array(pos), nrm=np.array(nrm), idx=np.array(idx, np.uint32))

# ---------------------------------------------------------------- materials

def mat(name, color, rough, metal=0.0, emissive=None, strength=1.0, alpha=1.0, clearcoat=False):
    m = {"name": name, "pbrMetallicRoughness": {"baseColorFactor": srgb_to_linear(color) + [alpha], "metallicFactor": metal, "roughnessFactor": rough}}
    ext = {}
    if emissive:
        m["emissiveFactor"] = srgb_to_linear(emissive)
        if strength != 1:
            ext["KHR_materials_emissive_strength"] = {"emissiveStrength": strength}
    if clearcoat:
        ext["KHR_materials_clearcoat"] = {"clearcoatFactor": 1.0, "clearcoatRoughnessFactor": 0.06}
    if ext:
        m["extensions"] = ext
    if alpha < 1:
        m["alphaMode"] = "BLEND"
    return m

GLOW = "#2F7CFF"

# ---------------------------------------------------------------- build

def build():
    g = Glb()
    del g.gltf["samplers"], g.gltf["textures"], g.gltf["images"]
    g.gltf["materials"] = [
        mat("shell", "#E4E9F0", 0.24, clearcoat=True),                       # 0
        mat("visor", "#06080F", 0.06, clearcoat=True),                        # 1
        # Deep blue: brighter emission tone-maps toward cyan-white.
        mat("face", "#2F6BFF", 0.25, emissive="#1F55FF", strength=3.0),      # 2
        mat("ring", "#6CC4FF", 0.3, emissive=GLOW, strength=2.6),             # 3
        mat("earcap", "#DDE6F0", 0.22, clearcoat=True),                      # 4
        mat("halo", "#6CC4FF", 0.4, emissive=GLOW, strength=2.0, alpha=0.75), # 5
        mat("alert", "#FF4B3A", 0.4, emissive="#FF4B3A", strength=2.2),       # 6
        mat("badge", "#1E2430", 0.35),                                        # 7
        mat("seam", "#C9D2DE", 0.3),                                          # 8
        mat("earglow", "#5C9BFF", 0.4, emissive="#2F6BFF", strength=3.0, alpha=0.7),   # 9
        mat("glow1", "#3F7BFF", 0.4, emissive="#2A5CFF", strength=1.4, alpha=0.2),     # 10
        mat("glow2", "#3F7BFF", 0.4, emissive="#2A5CFF", strength=1.2, alpha=0.12),    # 11
    ]
    g.gltf["extensionsUsed"] = ["KHR_materials_emissive_strength", "KHR_materials_clearcoat"]

    shell_mesh = sampled(head, HEAD_C); shell_mesh["material"] = 0
    head_mesh = [shell_mesh, visor_mesh(), visor_rim()]
    eyes, eye_targets = eyes_mesh()
    mouth, mouth_targets = mouth_mesh()
    body_mesh = sampled(body, BODY_C, 56, 84); body_mesh["material"] = 0
    # The neck joint: a grey collar between head and body.
    collar = turned(torus_mesh(0.065, 0.022, 32, 10), rot_x(math.pi / 2), (0, -0.37, 0)); collar["material"] = 8
    chest = turned(disc(0.022, 24), np.eye(3), (0, -0.5, 0.135)); chest["material"] = 3

    m_head = g.mesh("Head", head_mesh)
    m_eyes = g.mesh("Eyes", eyes, eye_targets)
    m_mouth = g.mesh("Mouth", [mouth], mouth_targets)
    m_body = g.mesh("Body", [body_mesh, collar, chest])

    # 0 Root, 1 Body (pivot at the bottom), 2 Head, 3 Face, 4 Eyes, 5 Mouth, 6 Torso, 7 EarL, 8 EarR, then extras.
    g.node("Root", children=[1])
    g.node("Body", t=(0, FEET, 0), children=[2, 3, 6])
    g.node("Head", m_head, t=(0, -FEET, 0))
    g.node("Face", t=(0, -FEET, 0), children=[4, 5])
    g.node("Eyes", m_eyes)
    g.node("Mouth", m_mouth)
    g.node("Torso", m_body, t=(0, -FEET, 0))

    # Ears: a white pod with a pale cap and a ring of light on the outside, each its own node so the
    # microphone level can slide it out.
    ears, glows = [], []
    for side in (-1, 1):
        pod = sampled(lambda p: ear(p + np.array([side * EAR_X, EAR_Y, 0]), side), (0, 0, 0), 32, 48); pod["material"] = 0
        face_x = side * 0.083
        to_side = rot_y(side * math.pi / 2)
        cap = turned(disc(0.12), to_side, (face_x, 0, 0)); cap["material"] = 4
        ring = turned(torus_mesh(0.13, 0.018), to_side, (face_x + side * 0.004, 0, 0)); ring["material"] = 3
        inner = turned(torus_mesh(0.05, 0.008), to_side, (face_x + side * 0.003, 0, 0)); inner["material"] = 3
        m_ear = g.mesh(f"Ear{'L' if side < 0 else 'R'}", [pod, cap, ring, inner])
        i = g.node(f"Ear{'L' if side < 0 else 'R'}", m_ear, t=(side * EAR_X, EAR_Y - FEET, 0))
        g.gltf["nodes"][1]["children"].append(i)
        ears.append(i)
        # The light between ear and head: a ring of light at the joint, a little wider than the pod
        # so it shows round it from the front, grown from nothing by a morph target that the
        # microphone level drives. It stays on the head while the ear slides out.
        ring_light = turned(torus_mesh(0.19, 0.032, 64, 12), to_side, (0, 0, 0))
        light_prims = []
        for m in (ring_light,):
            light_prims.append(dict(pos=m["pos"] * 0.01, nrm=m["nrm"], idx=m["idx"], targets=[m["pos"] * 0.99], material=9))
        name = f"EarLight{'L' if side < 0 else 'R'}"
        # At the joint, where the head's surface meets the pod.
        li = g.node(name, g.mesh(name, light_prims, ["glow"]), t=(side * (EAR_X + 0.012), EAR_Y - FEET, 0))
        g.gltf["nodes"][1]["children"].append(li)
        glows.append((li, g.gltf["nodes"][li]["mesh"]))

    # Connecting: rings of light under the body and trails around the head; shown by scaling the node.
    rings = [turned(torus_mesh(0.42, 0.012, 96, 8), rot_x(math.pi / 2), (0, 0, 0)),
             turned(torus_mesh(0.56, 0.007, 96, 8), rot_x(math.pi / 2), (0, 0.02, 0)),
             turned(arc_mesh(0.86, 0.016, 0.3, 2.9), rot_x(math.pi / 2 - 0.35), (0, 0.95, 0)),
             turned(arc_mesh(0.78, 0.01, 3.6, 5.6), rot_x(math.pi / 2 - 0.2), (0, 1.07, 0))]
    for r in rings:
        r["material"] = 5
    connect = g.node("Connect", g.mesh("Connect", rings), t=(0, 0.06, 0))
    g.gltf["nodes"][connect]["scale"] = [0.01, 0.01, 0.01]
    g.gltf["nodes"][1]["children"].append(connect)

    # Interrupted: a red mark above the right of the head.
    bar = turned(ellipsoid_mesh((0.026, 0.085, 0.026), 10, 14), rot_z(-0.35), (0.0, 0.05, 0)); bar["material"] = 6
    dot = turned(ellipsoid_mesh((0.027, 0.027, 0.027), 10, 14), np.eye(3), (-0.045, -0.07, 0)); dot["material"] = 6
    bar2 = turned(ellipsoid_mesh((0.018, 0.06, 0.018), 10, 14), rot_z(-0.7), (0.085, 0.0, 0)); bar2["material"] = 6
    alert = g.node("Alert", g.mesh("Alert", [bar, dot, bar2]), t=(0.62, 0.8 - FEET, 0.2))
    g.gltf["nodes"][alert]["scale"] = [0.022, 0.022, 0.022]
    g.gltf["nodes"][1]["children"].append(alert)

    # Muted: a mic-off badge beside the body, grown by a morph target from a point.
    mic = turned(ellipsoid_mesh((0.035, 0.06, 0.02), 10, 14), np.eye(3), (0, 0.03, 0))
    stand = turned(arc_mesh(0.055, 0.008, math.pi * 1.1, math.pi * 1.9, 24, 6), np.eye(3), (0, 0.02, 0))
    slash = turned(ellipsoid_mesh((0.011, 0.1, 0.011), 8, 10), rot_z(0.8), (0, 0.01, 0.02))
    parts = [(mic, 7), (stand, 7), (slash, 6)]
    badge_prims = []
    for m, material in parts:
        full = m["pos"]
        badge_prims.append(dict(pos=full * 0.01, nrm=m["nrm"], idx=m["idx"], targets=[full - full * 0.01], material=material))
    m_badge = g.mesh("MicOff", badge_prims, ["show"])
    badge = g.node("MicOff", m_badge, t=(0.6, -0.56 - FEET, 0.35))
    g.gltf["nodes"][badge]["scale"] = [3.2, 3.2, 3.2]
    g.gltf["nodes"][1]["children"].append(badge)

    clips(g, ears, (EAR_Y - FEET))
    return g.bytes(), dict(eyes=4, mouth=5, m_eyes=m_eyes, m_mouth=m_mouth, eye_targets=eye_targets, mouth_targets=mouth_targets,
                           ears=ears, glows=glows, connect=connect, alert=alert, badge=badge, m_badge=m_badge, badge_prims=len(badge_prims))

def clips(g, ears, ear_y):
    def rot(axis, angle): return quat(axis, angle)
    # Listening: the ears pulse outward in a steady beat (the microphone level adds on top).
    t = np.linspace(0, 0.9, 19)
    beat = np.sin(np.pi * t / 0.9) ** 2
    g.animation("listen", [(node, "translation", t, [[s * (EAR_X + 0.025 * v), ear_y, 0] for v in beat]) for node, s in zip(ears, (-1, 1))])
    t, e = wave(3.6, 2)
    bob = [[0, FEET + 0.025 * v, 0] for v in e]
    g.animation("idle_a", [(1, "translation", t, bob)])
    t, e = wave(5.0, 2)
    g.animation("idle_b", [(1, "translation", t, [[0, FEET + 0.02 * v, 0] for v in e]),
                           (1, "rotation", t, [rot((0, 0, 1), 0.035 * v) for v in np.sin(np.linspace(0, 2 * math.pi, len(t)))])])
    # Connecting: one full turn per loop, with a gentle bob; keys every 22.5° so the turn interpolates.
    t = np.linspace(0, 1.8, 17)
    turn = [rot((0, 1, 0), 2 * math.pi * k / 16) for k in range(17)]
    g.animation("connect", [(1, "rotation", t, turn), (1, "translation", t, [[0, FEET + 0.03 * math.sin(math.pi * k / 8), 0] for k in range(17)])])
    t, e = wave(3.0, 1)
    g.animation("think", [(1, "rotation", t, [rot((0, 0, 1), -0.07 * v) for v in e])])
    t, e = wave(0.8, 1)
    g.animation("listen_enter", [(1, "rotation", t, [rot((1, 0, 0), 0.08 * v) for v in e])])
    t, e = wave(0.9, 4)
    g.animation("nod", [(1, "rotation", t, [rot((1, 0, 0), 0.14 * v) for v in e])])
    g.animation("shake", [(1, "rotation", t, [rot((0, 1, 0), 0.25 * v) for v in e])])
    t, e = wave(1.0, 1)
    g.animation("lean_in", [(1, "rotation", t, [rot((1, 0, 0), 0.12 * v) for v in e])])
    t = np.linspace(0, 0.8, 33)
    air = np.where((t > 0.1) & (t < 0.7), np.sin(np.pi * np.clip((t - 0.1) / 0.6, 0, 1)), 0)
    g.animation("hop", [(1, "translation", t, [[0, FEET + 0.14 * v, 0] for v in air])])
    t, e = wave(0.45, 1)
    g.animation("interrupt", [(1, "translation", t, [[0, FEET - 0.03 * v, -0.04 * v] for v in e])])
    t, e = wave(1.0, 4)
    g.animation("wiggle", [(1, "rotation", t, [rot((0, 0, 1), 0.12 * v) for v in e])])

# ---------------------------------------------------------------- package

VISEMES = ["sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"]
VISEME_MOUTHS = {
    "sil": {}, "PP": {"press": 1}, "FF": {"small": 1}, "TH": {"small": 0.7, "open": 0.3}, "DD": {"open": 0.5},
    "kk": {"open": 0.45}, "CH": {"wide": 0.6}, "SS": {"wide": 0.5}, "nn": {"open": 0.35}, "RR": {"round": 0.5},
    "aa": {"open": 1}, "E": {"wide": 1}, "I": {"wide": 0.8}, "O": {"round": 1}, "U": {"round": 0.6},
}

def package(glb, ids, posters):
    def eye(name, v): return {"node": ids["eyes"], "mesh": ids["m_eyes"], "primitives": [0], "targetIndex": ids["eye_targets"].index(name), "value": v}
    def mouth(name, v): return {"node": ids["mouth"], "mesh": ids["m_mouth"], "primitives": [0], "targetIndex": ids["mouth_targets"].index(name), "value": v}
    def tilt(axis, angle): return {"node": 1, "property": "rotation", "value": [round(x, 6) for x in quat(axis, angle)]}
    def show(node, v=1.0): return {"node": node, "property": "scale", "value": [v, v, v]}

    poses = {
        # Standby: relaxed, slightly lowered eyes.
        "idle": [eye("squint", 0.18)],
        "connecting": [show(ids["connect"])],
        "listening": [eye("wide", 0.35), tilt((1, 0, 0), 0.04)],
        "thinking": [eye("think", 1), mouth("side_o", 1), tilt((0, 0, 1), -0.06)],
        "speaking": [eye("happy", 1), mouth("smile", 0.55)],
        "interrupted": [eye("wide", 1), mouth("round", 0.8), show(ids["alert"], 2.2), tilt((1, 0, 0), -0.06)],
        "muted": [eye("closed", 1), tilt((1, 0, 0), 0.06)],
        "reduced_motion": [],
        "expr_neutral": [],
        "expr_happy": [eye("happy", 1), mouth("smile", 1)],
        "expr_curious": [eye("wide", 0.4), eye("look_up", 0.3), mouth("small", 0.5), tilt((0, 0, 1), 0.12)],
        "expr_thinking": [eye("think", 1), mouth("side_o", 0.8)],
        "expr_concerned": [eye("squint", 0.4), eye("look_down", 0.3), mouth("frown", 0.9)],
        "expr_surprised": [eye("wide", 1), mouth("round", 1)],
        "expr_apologetic": [eye("closed", 0.6), mouth("frown", 0.5), tilt((1, 0, 0), 0.1)],
        "expr_confident": [eye("happy", 0.6), mouth("smile", 1)],
    }
    for v in VISEMES:
        poses["viseme_" + v] = [mouth(name, w) for name, w in VISEME_MOUTHS[v].items()]

    ear_params = [{"node": node, "property": "translation", "min": [s * EAR_X, EAR_Y - FEET, 0], "max": [s * (EAR_X + 0.035), EAR_Y - FEET, 0]}
                  for node, s in zip(ids["ears"], (-1, 1))]
    # Faint while listening in silence, bright at full input.
    ear_lights = [{"node": node, "mesh": mesh, "primitives": [0], "targetIndex": 0, "min": 0.18, "max": 1.0} for node, mesh in ids["glows"]]
    bindings = {
        "schemaVersion": "1.0", "renderer": "native3d",
        "animations": {n: n for n in ("idle_a", "idle_b", "connect", "think", "listen", "listen_enter", "nod", "shake", "lean_in", "hop", "interrupt", "wiggle")},
        "poses": poses,
        "parameters": {
            # The ears slide out with the microphone; speech pulses them a little too.
            "inputLevel": ear_params + ear_lights,
            "outputLevel": [{**p, "max": [p["min"][0] * 1.06, p["min"][1], 0]} for p in ear_params],
        },
        "channelMasks": {"expression": ["face", "body"], "speech": ["mouthShape"], "idle": ["body"], "reflex": ["face", "body"]},
        # A little from above and to the side, as the concept sheet shows it.
        "framing": {"cameraPosition": [0, 0.45, 3.7], "lookAt": [0, -0.04, 0], "verticalFov": 30, "safeInset": 0.06, "fit": "contain"},
        # SceneKit's threshold is on luminance: blue light needs a high intensity to pass it, and the
        # white shell must stay under it.
        "bloom": {"intensity": 1.0, "threshold": 1.4, "radius": 12},
        "gaze": {"node": ids["eyes"], "morphs": {"mesh": ids["m_eyes"], "primitives": [0], **{k: ids["eye_targets"].index("look_" + k) for k in ("left", "right", "up", "down")}},
                 "turn": {"node": 1, "factor": 0.6}},
        "blink": {"node": ids["eyes"], "mesh": ids["m_eyes"], "targetIndex": ids["eye_targets"].index("blink"),
                  "suppressExpressionTargets": [ids["eye_targets"].index(n) for n in ("happy", "wide", "closed", "squint", "think")], "expressionScaleRule": "oneMinusBlink"},
        "muteBadge": {"node": ids["badge"], "mesh": ids["m_badge"], "targetIndex": 0},
        "stateAnimations": {"connecting": "connect", "thinking": "think", "listening": "listen"},
        "headNode": 1, "eyesNode": ids["eyes"], "mouthNode": ids["mouth"],
        "speechMouthMesh": ids["m_mouth"],
        "expressionMouthTargets": [ids["mouth_targets"].index(n) for n in ("smile", "frown", "small")],
    }
    channels = ["face", "body"]
    manifest = {
        "schemaVersion": "1.0", "id": "nova", "displayName": "Nova", "assetVersion": "1.0.0",
        "minRuntimeVersion": "1.0.0", "profile": "character",
        "representations": [{
            "id": "native3d-primary", "renderer": "native3d", "format": "glb",
            "model": "models/nova.glb", "bindings": "bindings/native3d.json",
            "capabilities": ["ambient", "expressions", "audioReactive", "gestures", "reducedMotion", "speechArticulation", "gaze", "blink", "themeSlots"],
            "platforms": ["android", "ios", "maccatalyst", "windows"],
            "idleOwner": "scheduler", "blinkOwner": "scheduler", "gazeOwner": "scheduler",
        }],
        "states": {s: {"pose": s, "transitionMs": 90 if s == "interrupted" else 220, "inputReactive": s == "listening",
                       "outputReactive": s == "speaking", "gaze": "processing" if s == "thinking" else "engaged"}
                   for s in ("idle", "connecting", "listening", "thinking", "speaking", "interrupted", "muted")},
        "expressions": {e: {"pose": "expr_" + e, "channels": channels, "transitionMs": 100 if e == "surprised" else 220}
                        for e in ("neutral", "happy", "curious", "thinking", "concerned", "surprised", "apologetic", "confident")},
        "speech": {"mode": "canonicalVisemes", "canonicalProfile": "oculus15-v1",
                   "mapping": {str(i): "viseme_" + v for i, v in enumerate(VISEMES)}, "expressionMouthScale": 0.45, "releaseMs": 80},
        "motions": {"idleVariants": ["idle_a", "idle_b"],
                    "gestures": {"nod": "nod", "shake": "shake", "lean": "lean_in", "hop": "hop", "wiggle": "wiggle", "interrupt": "interrupt"},
                    "seedable": True, "blinkIntervalSeconds": [3.0, 6.5], "breathPeriodSeconds": [3.6, 5.0]},
        # The mic-off badge is dark on light backgrounds and light on dark ones.
        "themes": {"slots": {
            "badge": {"light": "#1E2430", "dark": "#E8ECF2", "bindings": ["material:7"]},
            # A touch darker on dark backgrounds, so the white shell does not glare.
            "shell": {"light": "#E4E9F0", "dark": "#C4CEDC", "bindings": ["material:0"]},
        }},
        "reducedMotion": {"pose": "reduced_motion", "retainSpeech": True, "transitionMs": 180},
        "posters": {"light": "previews/poster-light.png", "dark": "previews/poster-dark.png"},
        "license": "LICENSE.txt", "provenance": "provenance.json",
    }
    files = {
        "models/nova.glb": glb,
        "bindings/native3d.json": json.dumps(bindings, indent=1).encode(),
        "previews/poster-light.png": open(posters[0], 'rb').read() if posters else tiny_png(),
        "previews/poster-dark.png": open(posters[1], 'rb').read() if posters else tiny_png(),
        "LICENSE.txt": b"Generated for the Spine Avatar Lab by source/nova.py after the Nova concept sheet. Same licence as the Maui.Spine repository.\n",
        "provenance.json": json.dumps({"model": "procedural (source/nova.py)", "posters": "captured from the lab" if posters else "placeholder 1x1, not a render"}, indent=1).encode(),
        "source/nova.py": open(__file__, 'rb').read(),
    }
    manifest["files"] = [{"path": p, "sha256": hashlib.sha256(b).hexdigest(), "bytes": len(b)} for p, b in sorted(files.items())]
    out = io.BytesIO()
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr("avatar.json", json.dumps(manifest, indent=1))
        for p, b in sorted(files.items()):
            z.writestr(p, b)
    return out.getvalue()

if __name__ == "__main__":
    out_dir, posters = sys.argv[1], sys.argv[2:4]
    glb, ids = build()
    data = package(glb, ids, posters)
    path = os.path.join(out_dir, "nova.spineavatar")
    open(path, 'wb').write(data)
    print(path, len(data), "bytes")
