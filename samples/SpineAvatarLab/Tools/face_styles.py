"""Generates face-style avatars (head-and-shoulders busts) for the Spine Avatar Lab (#524), to choose a
style from. Built on plush_avatars.py: the same smooth-union bodies, band mouth, morph-target eyes,
clips and packaging, with hair, ears, noses, brows, glasses, beards and several surface materials.

Usage: python face_styles.py <out-dir> [name ...] [--posters <dir>]
Writes <out-dir>/face-<name>.spineavatar. Needs numpy and Pillow.
"""
import math, os, sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import plush_avatars as P
from plush_avatars import (ellipsoid, smin, smax, round_box, gradient, ray_surface, front_point, frame, uv_sphere,
                           ellipsoid_mesh, cylinder_mesh, srgb_to_linear, Glb, add_clips, package, fur_textures,
                           mouth_outline, MOUTHS, MOUTH_REST, COLS, ROWS, tufts)

HEAD = np.array([0, 0.02, 0])
BUST = np.array([0, -0.78, 0])
FEET = -0.98

# ---------------------------------------------------------------- shapes

def head_sdf(spec):
    h = spec["head"]
    w, ht, jaw = h.get("w", 1.0), h.get("h", 1.0), h.get("jaw", 1.0)
    kind = h.get("kind", "human")

    def sdf(p):
        if kind == "box":
            d = round_box(p, (0, 0.0, 0), (0.36 * w, 0.4 * ht, 0.34), 0.08)
        elif kind == "cylinder":
            q = p - np.array([0, 0.0, 0])
            radial = np.hypot(q[..., 0], q[..., 2]) - 0.4 * w
            d = smax(radial, np.abs(q[..., 1]) - 0.42 * ht, 0.12)
        elif kind == "round":
            d = ellipsoid(p, (0, 0.0, 0), (0.48 * w, 0.48 * ht, 0.46))
        else:
            d = smin(ellipsoid(p, (0, 0.06, 0), (0.42 * w, 0.5 * ht, 0.44)),
                     ellipsoid(p, (0, -0.16 * ht, 0.04), (0.33 * w * jaw, 0.3 * ht, 0.36)), 0.18)
        ears = h.get("ears")
        for s in (-1, 1):
            if ears == "human":
                d = smin(d, ellipsoid(p, (s * 0.41 * w, 0.0, -0.02), (0.06, 0.1, 0.05)), 0.04)
            elif ears == "bear":
                d = smin(d, ellipsoid(p, (s * 0.32 * w, 0.42 * ht, -0.04), (0.13, 0.13, 0.08)), 0.06)
            elif ears == "cat":
                # A cone-ish ear: an ellipsoid stretched up and leaned out.
                q = p - np.array([s * 0.27 * w, 0.46 * ht, -0.02])
                c, sn = math.cos(s * -0.35), math.sin(s * -0.35)
                q = np.stack([c * q[..., 0] - sn * q[..., 1], sn * q[..., 0] + c * q[..., 1], q[..., 2]], axis=-1)
                taper = 1 - np.clip(q[..., 1] / 0.2, -1, 1) * 0.6
                ear = np.hypot(q[..., 0] / (0.11 * taper), q[..., 2] / (0.05 * taper)) - 1
                d = smin(d, smax(ear * 0.05, np.abs(q[..., 1] + 0.02) - 0.18, 0.02), 0.05)
        if h.get("muzzle"):
            d = smin(d, ellipsoid(p, (0, -0.16 * ht, 0.33), (0.2, 0.14, 0.14)), 0.06)
        if kind == "ghost":
            d = smin(d, ellipsoid(p, (0, -0.55, -0.05), (0.38, 0.4, 0.36)), 0.2)
        return d
    return sdf

def hair_sdf(spec, head):
    hair = spec.get("hair")
    if not hair:
        return None
    th, y0, back, bangs = hair.get("thickness", 0.05), hair.get("front", 0.32), hair.get("back", 0.9), hair.get("bangs", 0.0)
    part = hair.get("part", 0.0)

    def sdf(p):
        x, y, z = p[..., 0], p[..., 1], p[..., 2]
        line = y0 - bangs * np.exp(-((x - part) / 0.28) ** 2) + 0.18 * (x - part) * hair.get("sweep", 0.0)
        region = (line - y) - back * np.maximum(-z, 0) + 0.25 * np.maximum(np.abs(x) - 0.3, 0)
        d = smax(head(p) - th, region * 0.6, 0.03)
        for b in hair.get("buns", []):
            d = smin(d, ellipsoid(p, b["at"], b["r"]), 0.05)
        return d
    return sdf

def beard_sdf(spec, head):
    beard = spec.get("beard")
    if not beard:
        return None
    my = spec["mouth"]["y"]

    def sdf(p):
        x, y, z = p[..., 0], p[..., 1], p[..., 2]
        region = smax(y - (my + 0.06), 0.12 - z, 0.03)
        hole = 0.075 - np.hypot((x) / 1.4, y - my)
        return smax(smax(head(p) - beard.get("thickness", 0.045), region, 0.03), hole, 0.02)
    return sdf

def bust_sdf(p):
    d = smin(ellipsoid(p, (0, -0.52, -0.03), (0.15, 0.32, 0.14)), ellipsoid(p, (0, -0.88, -0.03), (0.62, 0.24, 0.32)), 0.14)
    return smax(d, -(p[..., 1] - FEET), 0.06)

# ---------------------------------------------------------------- meshes

def sampled(sdf, centre, rings, segments, flat=False, fur=True, face_clear=True):
    d, uv, idx = uv_sphere(rings, segments)
    t = ray_surface(sdf, centre, d, far=1.8)
    if fur:
        t *= 1 + 0.006 * tufts(d) * (np.clip((0.6 - d[:, 2]) / 0.3, 0, 1) if face_clear else 1)
    p = centre + d * t[:, None]
    n = gradient(sdf, p)
    mesh = dict(pos=p, nrm=n, uv=uv * np.array([7.0, 3.5]), idx=idx)
    return flatten(mesh) if flat else mesh

def flatten(mesh):
    """Low-poly: every triangle its own three vertices with the face normal."""
    tri = mesh["idx"].reshape(-1, 3)
    pos = mesh["pos"][tri].reshape(-1, 3)
    uv = mesh["uv"][tri].reshape(-1, 2)
    a, b, c = mesh["pos"][tri[:, 0]], mesh["pos"][tri[:, 1]], mesh["pos"][tri[:, 2]]
    n = np.cross(b - a, c - a)
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
    return dict(pos=pos, nrm=np.repeat(n, 3, axis=0), uv=uv, idx=np.arange(len(pos), dtype=np.uint32))

def split(mesh, classify):
    """One primitive per material: classify(centroids) gives a material per triangle."""
    tri = mesh["idx"].reshape(-1, 3)
    centroids = mesh["pos"][tri].mean(axis=1)
    materials = classify(centroids)
    out = []
    for m in np.unique(materials):
        out.append(dict(pos=mesh["pos"], nrm=mesh["nrm"], uv=mesh["uv"], idx=tri[materials == m].reshape(-1).astype(np.uint32), material=int(m)))
    return out

def placed(sdf, x, y, local, lift=0.0):
    """A local mesh set on the surface at (x, y), facing out along the normal."""
    at, n = front_point(sdf, x, y)
    r = frame(n)
    return dict(pos=local["pos"] @ r.T + at + n * lift, nrm=local["nrm"] @ r.T, uv=local.get("uv", np.zeros((len(local["pos"]), 2))), idx=local["idx"])

def torus_mesh(R, r, segments=40, sides=10):
    pos, nrm, idx = [], [], []
    for i in range(segments):
        a = 2 * math.pi * i / segments
        for j in range(sides):
            b = 2 * math.pi * j / sides
            cx, cy = math.cos(a), math.sin(a)
            n = (cx * math.cos(b), cy * math.cos(b), math.sin(b))
            pos.append((cx * (R + r * math.cos(b)), cy * (R + r * math.cos(b)), r * math.sin(b))); nrm.append(n)
    for i in range(segments):
        for j in range(sides):
            a, b = i * sides + j, i * sides + (j + 1) % sides
            c, d = ((i + 1) % segments) * sides + j, ((i + 1) % segments) * sides + (j + 1) % sides
            idx += [a, c, b, b, c, d]
    return dict(pos=np.array(pos), nrm=np.array(nrm), idx=np.array(idx, np.uint32))

def tube_on_surface(sdf, points, radius, flat=0.45, lift=0.006):
    """A tube along points (x, y) on the face, flattened against the surface: brows, whiskers."""
    p, n = front_point(sdf, points[:, 0], points[:, 1])
    p = p + n * lift
    tangent = np.gradient(p, axis=0)
    tangent /= np.maximum(np.linalg.norm(tangent, axis=1, keepdims=True), 1e-9)
    side = np.cross(n, tangent)
    sides = 8
    pos, nrm, idx = [], [], []
    taper = np.sin(np.linspace(0.15, math.pi - 0.15, len(p))) ** 0.5
    for i in range(len(p)):
        for j in range(sides):
            b = 2 * math.pi * j / sides
            offset = side[i] * math.cos(b) * radius * taper[i] + n[i] * math.sin(b) * radius * flat * taper[i]
            pos.append(p[i] + offset); nrm.append(side[i] * math.cos(b) + n[i] * math.sin(b))
    for i in range(len(p) - 1):
        for j in range(sides):
            a, b = i * sides + j, i * sides + (j + 1) % sides
            idx += [a, a + sides, b, b, a + sides, b + sides]
    return dict(pos=np.array(pos), nrm=np.array(nrm), idx=np.array(idx, np.uint32))

# Eyes: parts in a local frame (z out of the face), all sharing blink, happy, wide, squint and look.
def eye_parts(style):
    if style == "cartoon":
        return 0.032, 0.45, [(ellipsoid_mesh((0.07, 0.082, 0.032)), 7),
                             (ellipsoid_mesh((0.042, 0.048, 0.012), centre=(0, -0.006, 0.026)), 8),
                             (ellipsoid_mesh((0.022, 0.026, 0.008), centre=(0, -0.006, 0.034)), 1),
                             (ellipsoid_mesh((0.012, 0.013, 0.005), 8, 10, centre=(0.016, 0.016, 0.037)), 2)]
    if style == "anime":
        return 0.028, 0.5, [(ellipsoid_mesh((0.068, 0.098, 0.028)), 7),
                            (ellipsoid_mesh((0.056, 0.082, 0.012), centre=(0, -0.012, 0.02)), 8),
                            (ellipsoid_mesh((0.026, 0.046, 0.008), centre=(0, -0.018, 0.028)), 1),
                            (ellipsoid_mesh((0.016, 0.02, 0.005), 8, 10, centre=(0.02, 0.028, 0.032)), 2),
                            (ellipsoid_mesh((0.008, 0.008, 0.004), 8, 10, centre=(-0.02, -0.05, 0.03)), 2)]
    if style == "button":
        return 0.012, 0.2, [(ellipsoid_mesh((0.052, 0.052, 0.012)), 1),
                            (ellipsoid_mesh((0.006, 0.006, 0.004), 6, 8, centre=(0.016, 0.016, 0.012)), 2),
                            (ellipsoid_mesh((0.006, 0.006, 0.004), 6, 8, centre=(-0.016, -0.016, 0.012)), 2)]
    if style == "led":
        return 0.02, 0.3, [(ellipsoid_mesh((0.075, 0.032, 0.02)), 9)]
    if style == "bead":
        return 0.022, 0.6, [(ellipsoid_mesh((0.036, 0.042, 0.022)), 1),
                            (ellipsoid_mesh((0.011, 0.012, 0.005), 8, 10, centre=(0.012, 0.016, 0.02)), 2)]
    if style == "sleepy":
        return 0.024, 0.7, [(ellipsoid_mesh((0.056, 0.036, 0.024), centre=(0, -0.016, 0)), 1),
                            (ellipsoid_mesh((0.012, 0.01, 0.005), 8, 10, centre=(0.016, -0.004, 0.022)), 2)]
    return 0.024, 0.7, [(ellipsoid_mesh((0.056, 0.072, 0.024)), 1),
                        (ellipsoid_mesh((0.017, 0.019, 0.008), 8, 10, centre=(0.018, 0.027, 0.02)), 2)]

def eyes_mesh(sdf, spec):
    e = spec["eyes"]
    ex, ey = e["x"], e["y"]
    rz, inset, parts = eye_parts(e["style"])
    rx = max(np.abs(m["pos"][:, 0]).max() for m, _ in parts)
    ry = max(np.abs(m["pos"][:, 1]).max() for m, _ in parts)
    def blink(p): return p * np.array([1.05, 0.08, 0.5]) + np.array([0, -0.012, 0])
    def happy(p):
        x = np.clip(p[:, 0] / rx, -1, 1)
        return np.stack([p[:, 0] * 1.1, p[:, 1] * 0.16 + ry * 0.75 * (1 - x * x) - ry * 0.3, p[:, 2] * 0.6], axis=1)
    def wide(p): return p * 1.2
    def squint(p): return p * np.array([1.04, 0.55, 0.8])
    names = ["blink", "happy", "wide", "squint", "look_left", "look_right", "look_up", "look_down"]
    shifts = {"look_left": (-0.028, 0), "look_right": (0.028, 0), "look_up": (0, 0.022), "look_down": (0, -0.02)}
    prims = []
    for local, material in parts:
        pos, nrm, idx, deltas = [], [], [], [[] for _ in names]
        for side in (-1, 1):
            lp = local["pos"]
            def placer(dx=0.0, dy=0.0):
                at, n = front_point(sdf, side * ex + dx, ey + dy)
                r = frame(n)
                origin = at - n * rz * inset
                return lambda q: q @ r.T + origin, r
            place, r = placer()
            offset = sum(len(x) for x in pos)
            rest = place(lp)
            pos.append(rest); nrm.append(local["nrm"] @ r.T); idx.append(local["idx"] + offset)
            for k, f in enumerate([blink, happy, wide, squint]):
                deltas[k].append(place(f(lp)) - rest)
            for name, (dx, dy) in shifts.items():
                moved, _ = placer(dx, dy)
                deltas[names.index(name)].append(moved(lp) - rest)
        prims.append(dict(pos=np.concatenate(pos), nrm=np.concatenate(nrm), idx=np.concatenate(idx),
                          targets=[np.concatenate(d) for d in deltas], material=material))
    return prims, names

def mouth_mesh(sdf, spec):
    m = spec["mouth"]
    scale, my = m.get("scale", 1.0), m["y"]
    def shape(params):
        q = dict(params)
        for k in ("w", "cu", "cl", "top", "bottom", "shift"):
            if k in q: q[k] *= scale
        xy = mouth_outline(**q)
        p, n = front_point(sdf, xy[:, 0], xy[:, 1] + my)
        return p + n * 0.003, n
    rest, normals = shape(MOUTH_REST)
    idx = []
    for r in range(ROWS - 1):
        for c in range(COLS - 1):
            a, b = r * COLS + c, r * COLS + c + 1
            idx += [a, a + COLS, b, b, a + COLS, b + COLS]
    names = list(MOUTHS)
    deltas = [shape(MOUTHS[t])[0] - rest for t in names]
    return dict(pos=rest, nrm=normals, idx=np.array(idx, np.uint32), targets=deltas, material=3), names

def cheeks_mesh(sdf, spec, rings=6, segments=28):
    bx, by = spec.get("blushAt", (spec["eyes"]["x"] + 0.11, spec["mouth"]["y"] + 0.06))
    radius = np.concatenate([[0], np.repeat(np.linspace(1 / rings, 1, rings), segments)])
    angle = np.concatenate([[0], np.tile(np.linspace(0, 2 * math.pi, segments, endpoint=False), rings)])
    unit = np.stack([np.cos(angle) * radius * 0.07, np.sin(angle) * radius * 0.042], axis=1)
    disc = []
    for r in range(rings):
        for j in range(segments):
            k = (j + 1) % segments
            if r == 0:
                disc += [0, 1 + j, 1 + k]
            else:
                a, b = 1 + (r - 1) * segments + j, 1 + (r - 1) * segments + k
                disc += [a, a + segments, b, b, a + segments, b + segments]
    def place(scale):
        xy = np.concatenate([unit * scale + np.array([side * bx, by]) for side in (-1, 1)])
        p, n = front_point(sdf, xy[:, 0], xy[:, 1])
        return p + n * 0.0035, n
    rest, normals = place(1.0)
    grown, _ = place(1.35)
    idx = np.concatenate([np.array(disc), np.array(disc) + len(unit)]).astype(np.uint32)
    return dict(pos=rest, nrm=normals, idx=idx, targets=[grown - rest], material=4), ["blush"]

def features(sdf, spec):
    """Static parts on the face: nose, brows, glasses, freckles, whiskers."""
    out = []
    ex, ey = spec["eyes"]["x"], spec["eyes"]["y"]
    if nose := spec.get("nose"):
        sx, sy, sz = nose.get("size", (0.035, 0.03, 0.03))
        local = ellipsoid_mesh((sx, sy, sz), 12, 16)
        out.append(dict(**placed(sdf, 0, nose["y"], local, lift=-sz * 0.35), material=10))
    if brows := spec.get("brows"):
        for side in (-1, 1):
            t = np.linspace(-1, 1, 14)
            x = side * (ex + t * 0.055 * brows.get("length", 1.0))
            y = brows["y"] + brows.get("arch", 0.012) * (1 - t * t) + side * t * brows.get("tilt", 0.0)
            out.append(dict(**tube_on_surface(sdf, np.stack([x, y], axis=1), brows.get("thickness", 0.011)), material=11))
    if glasses := spec.get("glasses"):
        R = glasses.get("radius", 0.088)
        ring = torus_mesh(R, 0.008)
        inner = []
        for side in (-1, 1):
            at, n = front_point(sdf, side * ex, ey)
            r = frame(n)
            centre = at + n * 0.05
            out.append(dict(pos=ring["pos"] @ r.T + centre, nrm=ring["nrm"] @ r.T, idx=ring["idx"], material=12))
            inner.append(centre + r[:, 0] * -side * R)
        a, b = inner
        bridge = cylinder_mesh(0.007, float(np.linalg.norm(b - a)))
        axis = (b - a) / np.linalg.norm(b - a)
        # The cylinder runs along +y; turn it onto the bridge axis.
        y = np.array([0, 1.0, 0]); v = np.cross(y, axis); c = float(np.dot(y, axis))
        vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
        rot = np.eye(3) + vx + vx @ vx / (1 + c)
        out.append(dict(pos=bridge["pos"] @ rot.T + a + np.array([0, 0.012, 0]), nrm=bridge["nrm"] @ rot.T, idx=bridge["idx"], material=12))
    if spec.get("freckles"):
        rng = np.random.default_rng(5)
        dot = ellipsoid_mesh((0.008, 0.007, 0.003), 6, 8)
        for side in (-1, 1):
            for _ in range(7):
                x = side * (ex + rng.uniform(-0.03, 0.09)); y = ey - 0.09 + rng.uniform(-0.03, 0.03)
                out.append(dict(**placed(sdf, x, y, dot, lift=-0.001), material=13))
    if spec.get("whiskers"):
        for side in (-1, 1):
            for k, tilt in enumerate((-0.05, 0.0, 0.05)):
                t = np.linspace(0, 1, 10)
                x = side * (0.12 + 0.2 * t); y = spec["mouth"]["y"] + 0.04 + tilt * t * 1.5 + k * 0.0
                out.append(dict(**tube_on_surface(sdf, np.stack([x, y], axis=1), 0.004, flat=1.0, lift=0.012), material=12))
    return out

# ---------------------------------------------------------------- materials

def material(name, kind, color, fur_ok):
    base = srgb_to_linear(color)
    m = {"name": name, "pbrMetallicRoughness": {"baseColorFactor": base + [1], "metallicFactor": 0, "roughnessFactor": 0.6}}
    pbr = m["pbrMetallicRoughness"]
    if kind in ("fur", "felt") and fur_ok:
        pbr["baseColorTexture"] = {"index": 0}
        pbr["roughnessFactor"] = 0.95
        m["normalTexture"] = {"index": 1, "scale": 0.8 if kind == "fur" else 0.35}
        m["extensions"] = {"KHR_materials_sheen": {"sheenColorFactor": [min(1, c * 1.4 + 0.15) for c in base], "sheenRoughnessFactor": 0.45 if kind == "fur" else 0.8}}
    elif kind == "clay":
        pbr["roughnessFactor"] = 0.82
    elif kind == "vinyl":
        pbr["roughnessFactor"] = 0.28
    elif kind == "skin":
        pbr["roughnessFactor"] = 0.62
    elif kind == "metal":
        pbr["metallicFactor"] = 0.75
        pbr["roughnessFactor"] = 0.32
    elif kind == "ghost":
        pbr["baseColorFactor"] = base + [0.86]
        pbr["roughnessFactor"] = 0.5
        m["alphaMode"] = "BLEND"
        m["emissiveFactor"] = [c * 0.35 for c in base]
    return m

def flat_material(name, color, rough=0.5, emissive=None, alpha=1.0):
    m = {"name": name, "pbrMetallicRoughness": {"baseColorFactor": srgb_to_linear(color) + [alpha], "metallicFactor": 0, "roughnessFactor": rough}}
    if emissive: m["emissiveFactor"] = srgb_to_linear(emissive)
    if alpha < 1: m["alphaMode"] = "BLEND"
    if name == "mouth": m["doubleSided"] = True
    return m

# ---------------------------------------------------------------- one avatar

def build(key, spec, fur):
    head = head_sdf(spec)
    hair = hair_sdf(spec, head)
    beard = beard_sdf(spec, head)
    parts = [s for s in (hair, beard) if s is not None]
    def combined(p):
        d = head(p)
        for s in parts:
            d = smin(d, s(p), 0.012)
        return d

    skin_kind, skin = spec["skin"]
    hair_kind = spec["hair"].get("kind", skin_kind) if spec.get("hair") else skin_kind
    bust_spec = spec.get("bust", {"kind": skin_kind})
    kinds = {skin_kind, hair_kind, (bust_spec or {}).get("kind", skin_kind)}
    uses_fur = bool(kinds & {"fur", "felt"})
    g = Glb()
    if uses_fur:
        g.gltf["samplers"].append({"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497})
        grain = g.image(fur[0], "fur-grain"); normal = g.image(fur[1], "fur-normal")
        g.gltf["textures"] += [{"sampler": 0, "source": grain}, {"sampler": 0, "source": normal}]
    else:
        del g.gltf["samplers"], g.gltf["textures"], g.gltf["images"]
    hair_color = (spec.get("hair") or spec.get("beard") or {"color": spec.get("accent", skin)})["color"]
    blush = spec.get("blush", True)
    e = spec["eyes"]
    g.gltf["materials"] = [
        material("skin", skin_kind, skin, uses_fur),                                       # 0
        flat_material("eye", "#15131A", 0.14),                                              # 1
        flat_material("shine", "#FFFFFF", 0.4, emissive="#FFFFFF"),                         # 2
        flat_material("mouth", spec["mouth"].get("color", "#3B1F26"), 0.55,
                      emissive=spec["mouth"].get("glow")),                                  # 3
        flat_material("blush", blush if isinstance(blush, str) else "#FF8FA3", 0.9, alpha=0.42 if blush else 0.0),  # 4
        material("hair", hair_kind, hair_color, uses_fur),                                  # 5
        material("shirt", (bust_spec or {}).get("kind", skin_kind), (bust_spec or {}).get("color", "#5B7DB1"), uses_fur),  # 6
        flat_material("sclera", "#FBFBFD", 0.25),                                           # 7
        flat_material("iris", e.get("iris", "#4A7BD0"), 0.2),                               # 8
        flat_material("led", e.get("iris", "#4FE3FF"), 0.3, emissive=e.get("iris", "#4FE3FF")),  # 9
        material("nose", spec.get("nose", {}).get("kind", skin_kind), spec.get("nose", {}).get("color", skin), uses_fur),  # 10
        flat_material("brow", spec.get("brows", {}).get("color", hair_color), 0.7),        # 11
        flat_material("frame", spec.get("glasses", {}).get("color", "#2A2A33"), 0.35),     # 12
        flat_material("freckle", "#B5704F", 0.8),                                           # 13
        material("muzzle", skin_kind, spec.get("head", {}).get("muzzleColor", "#FFF3E2"), uses_fur),  # 14
    ]
    if uses_fur:
        g.gltf["extensionsUsed"] = ["KHR_materials_sheen"]

    lowpoly = spec.get("lowpoly", False)
    rings, segments = (10, 14) if lowpoly else (84, 128)
    furry = skin_kind in ("fur", "felt") and not lowpoly
    surface = sampled(combined, HEAD, rings, segments, flat=lowpoly, fur=furry)
    muzzle = spec.get("head", {}).get("muzzle")
    def classify(c):
        m = np.zeros(len(c), int)
        if parts:
            m[head(c) > 0.01] = 5
        if muzzle:
            mz = ellipsoid(c, (0, -0.16 * spec["head"].get("h", 1.0), 0.33), (0.21, 0.15, 0.15))
            m[(m == 0) & (mz < 0.012)] = 14
        return m
    head_prims = split(surface, classify)
    for prim in head_prims:
        prim.setdefault("material", 0)

    eyes, eye_targets = eyes_mesh(combined, spec)
    mouth, mouth_targets = mouth_mesh(combined, spec)
    cheeks, cheek_targets = cheeks_mesh(combined, spec)
    feats = features(combined, spec)

    m_head = g.mesh("Head", head_prims)
    m_eyes = g.mesh("Eyes", eyes, eye_targets)
    m_mouth = g.mesh("Mouth", [mouth], mouth_targets)
    m_cheeks = g.mesh("Cheeks", [cheeks], cheek_targets)

    # 0 Root, 1 Body (pivot at the bottom), 2 Head, 3 Face, 4 Eyes, 5 Mouth, 6 Cheeks, then features and bust.
    g.node("Root", children=[1])
    g.node("Body", t=(0, FEET, 0), children=[2, 3])
    g.node("Head", m_head, t=(0, -FEET, 0))
    g.node("Face", t=(0, -FEET, 0), children=[4, 5, 6])
    g.node("Eyes", m_eyes)
    g.node("Mouth", m_mouth)
    g.node("Cheeks", m_cheeks)
    if feats:
        i = g.node("Features", g.mesh("Features", feats), t=(0, -FEET, 0))
        g.gltf["nodes"][1]["children"].append(i)
    if bust_spec is not None:
        bust = sampled(bust_sdf, BUST, 40, 64, flat=lowpoly, fur=bust_spec.get("kind", skin_kind) in ("fur", "felt") and not lowpoly, face_clear=False)
        prims = split(bust, lambda c: np.where(c[:, 1] > -0.66, 0, 6))
        i = g.node("Bust", g.mesh("Bust", prims), t=(0, -FEET, 0))
        g.gltf["nodes"][1]["children"].append(i)

    add_clips(g, FEET, [])
    return g.bytes(), dict(body=1, eyes=4, mouth=5, cheeks=6, m_eyes=m_eyes, m_mouth=m_mouth, m_cheeks=m_cheeks,
                           eye_targets=eye_targets, mouth_targets=mouth_targets, window=None, eye_prims=len(eyes))

# ---------------------------------------------------------------- the styles

HUMAN_EYES = dict(x=0.15, y=0.04)
STYLES = {
    "plush": dict(name="1 Plush", skin=("fur", "#F2B49A"), hair=dict(kind="fur", color="#8A5A3C", front=0.3), head=dict(ears="human"),
                  eyes=dict(style="dot", **HUMAN_EYES), mouth=dict(y=-0.17), bust=dict(kind="fur", color="#6FA8DC")),
    "felt": dict(name="2 Felt doll", skin=("felt", "#F4C9B0"), hair=dict(kind="fur", color="#C2462E", front=0.3, bangs=0.12), head=dict(ears="human"),
                 eyes=dict(style="button", **HUMAN_EYES), mouth=dict(y=-0.17, color="#8E2F3C"), bust=dict(kind="felt", color="#7A9E5A")),
    "clay": dict(name="3 Clay", skin=("clay", "#E9A884"), hair=dict(kind="clay", color="#3E2A20", front=0.3, sweep=0.6, part=0.1), head=dict(ears="human", jaw=1.1),
                 eyes=dict(style="cartoon", x=0.15, y=0.05, iris="#5B8C3A"), nose=dict(y=-0.06, size=(0.05, 0.04, 0.05)),
                 brows=dict(y=0.17, thickness=0.015, color="#3E2A20"), mouth=dict(y=-0.19), bust=dict(kind="clay", color="#E0B040")),
    "vinyl": dict(name="4 Vinyl toy", skin=("vinyl", "#F7E3D3"), hair=dict(kind="vinyl", color="#1F1F2A", front=0.26, bangs=0.18), head=dict(kind="round", w=1.12, ears=None),
                  eyes=dict(style="bead", x=0.2, y=-0.02), mouth=dict(y=-0.15, scale=0.6), blush="#FF9AA8", bust=dict(kind="vinyl", color="#FF6B5A")),
    "cartoon": dict(name="5 Animated film", skin=("skin", "#F1C3A2"), hair=dict(kind="skin", color="#7B4A2A", front=0.34, sweep=-0.8, part=-0.12, thickness=0.07), head=dict(ears="human"),
                    eyes=dict(style="cartoon", x=0.15, y=0.04, iris="#3E7CC9"), nose=dict(y=-0.07, size=(0.036, 0.03, 0.04)),
                    brows=dict(y=0.16, thickness=0.012), mouth=dict(y=-0.19), bust=dict(kind="skin", color="#3A6EA5")),
    "anime": dict(name="6 Anime", skin=("skin", "#FBE2D2"), hair=dict(kind="skin", color="#2C2F4A", front=0.26, bangs=0.1, thickness=0.07), head=dict(ears="human", jaw=0.85),
                  eyes=dict(style="anime", x=0.15, y=0.0, iris="#6A5ACD"), nose=dict(y=-0.09, size=(0.012, 0.012, 0.012)),
                  brows=dict(y=0.14, thickness=0.006, length=0.8), mouth=dict(y=-0.2, scale=0.55), blush="#FF9DB0", bust=dict(kind="skin", color="#22263A")),
    "chibi": dict(name="7 Chibi", skin=("vinyl", "#FFE0CC"), hair=dict(kind="vinyl", color="#E8A23A", front=0.22, bangs=0.16, buns=[dict(at=(-0.36, 0.38, -0.05), r=(0.13, 0.13, 0.13)), dict(at=(0.36, 0.38, -0.05), r=(0.13, 0.13, 0.13))]),
                  head=dict(kind="round", w=1.2, h=1.12, ears=None), eyes=dict(style="anime", x=0.17, y=-0.05, iris="#2E9C8A"), mouth=dict(y=-0.24, scale=0.6), blush="#FF8FA3",
                  bust=dict(kind="vinyl", color="#FF8FB8")),
    "mii": dict(name="8 Simple avatar", skin=("skin", "#F5D0B5"), hair=dict(kind="skin", color="#4A3426", front=0.3, thickness=0.06), head=dict(kind="round", w=0.95, h=1.05, ears="human"),
                eyes=dict(style="bead", x=0.13, y=0.03), brows=dict(y=0.12, thickness=0.008), nose=dict(y=-0.05, size=(0.022, 0.03, 0.03)), mouth=dict(y=-0.16, scale=0.8),
                blush=False, bust=dict(kind="skin", color="#D9483B")),
    "block": dict(name="9 Block figure", skin=("vinyl", "#F6CD2E"), head=dict(kind="cylinder", w=1.0, ears=None), eyes=dict(style="bead", x=0.12, y=0.05),
                  brows=dict(y=0.14, thickness=0.006, color="#1A1A1A"), mouth=dict(y=-0.1, color="#1A1A1A"), blush=False, bust=dict(kind="vinyl", color="#2F6FD0")),
    "robot": dict(name="10 Robot", skin=("metal", "#B8C2CC"), head=dict(kind="box", ears=None), eyes=dict(style="led", x=0.14, y=0.06, iris="#4FE3FF"),
                  mouth=dict(y=-0.14, color="#1C2A33", glow="#4FE3FF"), blush=False, bust=dict(kind="metal", color="#7D8B99")),
    "lowpoly": dict(name="11 Low poly", lowpoly=True, skin=("clay", "#E8B494"), hair=dict(kind="clay", color="#5C3A28", front=0.3), head=dict(ears="human"),
                    eyes=dict(style="dot", **HUMAN_EYES), mouth=dict(y=-0.17), blush=False, bust=dict(kind="clay", color="#4F9D8A")),
    "sleepy": dict(name="12 Sleepy", skin=("fur", "#C9B6E8"), head=dict(kind="round", ears="bear"), eyes=dict(style="sleepy", x=0.15, y=0.0),
                   mouth=dict(y=-0.15, scale=0.7), bust=dict(kind="fur", color="#9C86C9")),
    "glasses": dict(name="13 Glasses", skin=("skin", "#EBC0A0"), hair=dict(kind="skin", color="#2B211B", front=0.32, sweep=0.9, part=0.14, thickness=0.06), head=dict(ears="human"),
                    eyes=dict(style="cartoon", x=0.15, y=0.04, iris="#6B4A2E"), glasses=dict(color="#1E1E28"), nose=dict(y=-0.07, size=(0.03, 0.03, 0.035)),
                    brows=dict(y=0.17, thickness=0.01), mouth=dict(y=-0.2), blush=False, bust=dict(kind="skin", color="#6B7A8F")),
    "beard": dict(name="14 Beard", skin=("clay", "#E2A57E"), hair=dict(kind="fur", color="#6B3E22", front=0.36), beard=dict(color="#6B3E22", thickness=0.05),
                  head=dict(ears="human", jaw=1.08), eyes=dict(style="dot", x=0.15, y=0.06), nose=dict(y=-0.04, size=(0.045, 0.035, 0.045)),
                  brows=dict(y=0.16, thickness=0.016, tilt=-0.01), mouth=dict(y=-0.17, color="#5A2A2A"), blush=False, bust=dict(kind="felt", color="#3F5E3A")),
    "elder": dict(name="15 Grandparent", skin=("clay", "#EDBFA2"), hair=dict(kind="fur", color="#D9D9DE", front=0.36, buns=[dict(at=(0, 0.5, -0.32), r=(0.16, 0.14, 0.14))]), head=dict(ears="human"),
                  eyes=dict(style="bead", x=0.14, y=0.04), glasses=dict(color="#B08A3E", radius=0.07), nose=dict(y=-0.06, size=(0.035, 0.035, 0.04)),
                  brows=dict(y=0.14, thickness=0.009, color="#C9C9CF"), mouth=dict(y=-0.18), bust=dict(kind="felt", color="#A5526A")),
    "kid": dict(name="16 Kid", skin=("skin", "#F3C6A6"), hair=dict(kind="fur", color="#E07A2E", front=0.3, bangs=0.1, buns=[dict(at=(-0.44, -0.02, -0.06), r=(0.1, 0.16, 0.1)), dict(at=(0.44, -0.02, -0.06), r=(0.1, 0.16, 0.1))]),
                head=dict(kind="round", ears=None), eyes=dict(style="cartoon", x=0.15, y=0.02, iris="#3D8B5A"), freckles=True, nose=dict(y=-0.07, size=(0.025, 0.022, 0.025)),
                mouth=dict(y=-0.18), blush="#FF9E8F", bust=dict(kind="skin", color="#F2C14E")),
    "bear": dict(name="17 Bear mascot", skin=("fur", "#B07A4F"), head=dict(kind="round", ears="bear", muzzle=True, muzzleColor="#EBD3B5"),
                 eyes=dict(style="dot", x=0.16, y=0.08), nose=dict(y=-0.08, size=(0.05, 0.035, 0.035), color="#3A2618", kind="vinyl"),
                 mouth=dict(y=-0.2, scale=0.7), bust=dict(kind="fur", color="#B07A4F")),
    "cat": dict(name="18 Cat mascot", skin=("fur", "#9AA3AE"), head=dict(kind="round", ears="cat", muzzle=True, muzzleColor="#F2F2F4"), eyes=dict(style="cartoon", x=0.16, y=0.06, iris="#7FBF3F"),
                nose=dict(y=-0.08, size=(0.028, 0.02, 0.02), color="#F28CA0", kind="vinyl"), whiskers=True, mouth=dict(y=-0.2, scale=0.6), bust=dict(kind="fur", color="#9AA3AE")),
    "ghost": dict(name="19 Spirit", skin=("ghost", "#EEF2FF"), head=dict(kind="ghost", ears=None), eyes=dict(style="dot", x=0.14, y=0.05),
                  mouth=dict(y=-0.14, scale=0.8), blush="#B9A8FF", bust=None),
}

if __name__ == "__main__":
    args = sys.argv[1:]
    posters = None
    if "--posters" in args:
        i = args.index("--posters"); posters = args[i + 1]; del args[i:i + 2]
    out_dir, names = args[0], args[1:] or list(STYLES)
    fur = fur_textures()
    for key in names:
        spec = dict(STYLES[key])
        spec.setdefault("framing", {"cameraPosition": [0, -0.12, 4.5], "lookAt": [0, -0.2, 0]})
        spec["color"] = spec["skin"][1]; spec["dark"] = spec["skin"][1]
        spec["accent"] = (spec.get("hair") or spec.get("beard") or {"color": spec["skin"][1]})["color"]
        glb, ids = build(key, spec, fur)
        data = package(key, spec, glb, ids, posters, prefix="face", source=os.path.abspath(__file__))
        path = os.path.join(out_dir, f"face-{key}.spineavatar")
        open(path, 'wb').write(data)
        print(path, len(data), "bytes")
