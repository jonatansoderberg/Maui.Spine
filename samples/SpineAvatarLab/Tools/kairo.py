"""Generates Kairo, the stylized human head from the concept sheet, for the Spine Avatar Lab (#524).

Usage: python kairo.py <out-dir> [poster-light.png poster-dark.png]
Writes <out-dir>/kairo.spineavatar. Needs numpy and Pillow; shares helpers with plush_avatars.py.

A sculpted head (smooth union of cranium, face, cheeks, chin and nose, with shallow eye sockets),
eyeballs with iris, pupil and highlights whose look targets turn the iris about the eyeball's
centre, eyelids as sphere patches round the eyeball whose targets close, widen, squint and smile,
brows that raise and frown, lips, mouth and teeth as bands on the face sharing the mouth targets
(smiles, the visemes), a purple bob with swept bangs and strand grooves, and a turtleneck bust.
The head turns on the neck; the bust stays.
"""
import hashlib, io, json, math, os, sys, zipfile
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from plush_avatars import (ellipsoid, smin, smax, gradient, ray_surface, front_point, frame, uv_sphere,
                           ellipsoid_mesh, srgb_to_linear, Glb, quat, wave, tiny_png, fur_textures)
from face_styles import torus_mesh, split

HEAD_C = np.array([0, 0.08, 0.02])
NECK = np.array([0, -0.4, -0.02])     # the head turns about this point
EYE_R = 0.112
EYES_AT = [np.array([s * 0.162, 0.02, 0.252]) for s in (-1, 1)]
MOUTH_Y = -0.18

# ---------------------------------------------------------------- shapes

def rot(axis, angle):
    x, y, z = axis
    c, s = math.cos(angle), math.sin(angle)
    return np.array([[c + x * x * (1 - c), x * y * (1 - c) - z * s, x * z * (1 - c) + y * s],
                     [y * x * (1 - c) + z * s, c + y * y * (1 - c), y * z * (1 - c) - x * s],
                     [z * x * (1 - c) - y * s, z * y * (1 - c) + x * s, c + z * z * (1 - c)]])

def head(p, sockets=True):
    # A round, wide-cheeked face over a small pointed chin, as stylised film characters have.
    d = smin(ellipsoid(p, (0, 0.16, -0.02), (0.38, 0.42, 0.42)), ellipsoid(p, (0, -0.02, 0.07), (0.33, 0.34, 0.35)), 0.14)
    for s in (-1, 1):
        # Cheekbones high and to the side; the jaw narrows below them.
        d = smin(d, ellipsoid(p, (s * 0.19, -0.04, 0.18), (0.13, 0.11, 0.13)), 0.09)
        d = smin(d, ellipsoid(p, (s * 0.12, -0.2, 0.13), (0.11, 0.12, 0.13)), 0.08)
    d = smin(d, ellipsoid(p, (0, -0.31, 0.15), (0.08, 0.075, 0.1)), 0.09)
    # Shallow sockets where the eyes sit.
    for c in EYES_AT if sockets else []:
        d = smax(d, -ellipsoid(p, c + np.array([0, 0.005, 0.07]), (0.11, 0.092, 0.072)), 0.04)
    # A small, soft nose.
    d = smin(d, ellipsoid(p, (0, -0.04, 0.39), (0.022, 0.06, 0.03)), 0.035)
    d = smin(d, ellipsoid(p, (0, -0.095, 0.415), (0.03, 0.022, 0.026)), 0.025)
    return d

def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)

def hair_thickness(p):
    """How far the hair stands off the head. Below zero the hair lies under the skin (the face), so
    the hairline is where this field crosses zero: a smooth curve, whatever the mesh."""
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    # The face: an oval on the front of the head with no hair.
    # A steep edge: a shallow one meets the skin at a grazing angle, and the crossing zigzags
    # between the two meshes' triangles.
    # Only the forward-facing part is face: the sides of the head get hair, which hangs beside the cheeks.
    face = smooth(1.0, 0.93, (x / 0.37) ** 2 + ((y + 0.12) / 0.38) ** 2) * smooth(0.14, 0.22, z)
    t = 0.065 * (1 - face)
    # The bob's sides stand away from the cheeks and flick out at the ends.
    # They grow out gradually from the temples, so the outline flows down without a shelf.
    sides = smooth(0.2, 0.3, np.abs(x)) * smooth(0.32, -0.1, y) * smooth(0.36, 0.2, z)
    t += 0.075 * sides + 0.04 * sides * smooth(-0.15, -0.3, y)
    # Volume at the parting on the viewer's left.
    t += 0.05 * np.exp(-(((x + 0.15) / 0.2) ** 2 + ((y - 0.38) / 0.14) ** 2))
    # Bangs: a band swept diagonally across the forehead from the parting, over the right brow.
    c, sn = math.cos(-0.5), math.sin(-0.5)
    u = (x - 0.04) * c + (y - 0.25) * sn
    v = -(x - 0.04) * sn + (y - 0.25) * c
    t += 0.075 * np.exp(-((u / 0.3) ** 4 + (v / 0.07) ** 2)) * smooth(0.0, 0.2, z)
    # The bob ends at the jaw. Where there is no hair the field is clearly negative, so the hair lies
    # well under the skin: near zero the two surfaces coincide and flicker in bands.
    return t * smooth(-0.37, -0.31, y) - 0.015

def hair_volume(p):
    # Offset from the head without its eye sockets: with them, the socket cut through the hair too and
    # left a hole beside the eye.
    return head(p, sockets=False) - hair_thickness(p)

def combined(p):
    return smin(head(p), hair_volume(p), 0.006)

def bust(p):
    # A high turtleneck: a soft fold round the neck up under the chin, and the shoulders.
    # The collar: a straight tube from under the chin, widening a little to the shoulders.
    q = p - np.array([0, -0.47, -0.03])
    widen = 1 + 0.3 * np.clip(-q[..., 1] / 0.12, 0, 1)
    tube = np.hypot(q[..., 0] / (0.18 * widen), q[..., 2] / (0.165 * widen)) - 1
    d = smax(tube * 0.17, np.abs(q[..., 1]) - 0.12, 0.03)
    d = smin(d, ellipsoid(p, (0, -0.8, -0.03), (0.55, 0.2, 0.3)), 0.16)
    return smax(d, -(p[..., 1] + 1.08), 0.05)

# ---------------------------------------------------------------- meshes

def outer_surface(sdf, centre, d, far=1.4, steps=160, fallback=None):
    """The outermost crossing along each ray, marching in from far away: a ray that passes several
    surfaces (the bangs over the face) finds the same one as its neighbours, so edges stay smooth.
    Rays that find nothing get `fallback` (a distance) or the centre."""
    centre = np.asarray(centre)
    ts = np.linspace(far, 0.0, steps)
    inside = np.stack([sdf(centre + d * t) < 0 for t in ts], axis=1)
    hit = inside.any(axis=1)
    first = np.argmax(inside, axis=1)
    lo = ts[np.maximum(first, 1) - 1]; hi = ts[first]       # lo outside, hi inside
    for _ in range(30):
        mid = (lo + hi) / 2
        ins = sdf(centre + d * mid[:, None]) < 0
        hi = np.where(ins, mid, hi); lo = np.where(ins, lo, mid)
    t = (lo + hi) / 2
    if fallback is not None:
        t = np.where(hit, t, fallback)
    return t, hit

def sampled(sdf, centre, rings, segments):
    d, uv, idx = uv_sphere(rings, segments)
    t, _ = outer_surface(sdf, centre, d)
    p = np.asarray(centre) + d * t[:, None]
    return d, t, uv, idx, p

def hairline_grid(cols=288, rows=150):
    """Directions for the hair mesh on a grid whose first row lies on the hairline. Around the face's
    axis (+z), each column finds where the hair begins on the head; the rows run from there over
    the head to the back. A regular sphere grid crossed the hairline diagonally and stair-stepped it."""
    theta = np.linspace(0, 2 * math.pi, cols + 1)
    def direction(a, th):
        return np.stack([np.sin(a) * np.cos(th), np.sin(a) * np.sin(th), np.cos(a)], axis=-1)
    # The first place hair begins, walking out from the face's centre (bisection alone could land on a
    # later crossing, past the bangs), then refined.
    def bare_at(a):
        dirs = direction(a, theta)
        t = ray_surface(head, HEAD_C, dirs, far=1.6)
        return hair_thickness(np.asarray(HEAD_C) + dirs * t[:, None]) < 0
    steps = np.linspace(0.1, 1.9, 90)
    first = np.full(cols + 1, len(steps) - 1)
    for i in range(len(steps) - 1, -1, -1):
        first = np.where(~bare_at(np.full(cols + 1, steps[i])), i, first)
    lo, hi = steps[np.maximum(first - 1, 0)], steps[first]
    for _ in range(20):
        mid = (lo + hi) / 2
        dirs = direction(mid, theta)
        t = ray_surface(head, HEAD_C, dirs, far=1.6)
        bare = hair_thickness(np.asarray(HEAD_C) + dirs * t[:, None]) < 0
        lo = np.where(bare, mid, lo); hi = np.where(bare, hi, mid)
    edge = (lo + hi) / 2
    k = (np.arange(rows + 1) / rows) ** 1.6
    alpha = edge[None, :] + (math.pi - 1e-3 - edge[None, :]) * k[:, None]
    d = direction(alpha, theta[None, :]).reshape(-1, 3)
    uv = np.stack([np.broadcast_to(theta / (2 * math.pi), alpha.shape), alpha / math.pi], axis=-1).reshape(-1, 2)
    idx = []
    w = cols + 1
    for r in range(rows):
        for c in range(cols):
            a, b = r * w + c, r * w + c + 1
            idx += [a, a + w, b, b, a + w, b + w]
    return d, uv, np.array(idx, np.uint32)

def head_and_hair():
    """The head and the hair as two meshes: where the hair meets the face the edge is where two
    smooth surfaces cross, which the depth test draws smoothly (splitting one surface by triangle
    gave a stair-stepped hairline)."""
    d, t, uv, idx, p = sampled(head, HEAD_C, 170, 256)
    skin = dict(pos=p, nrm=gradient(head, p), uv=uv, idx=idx, material=0)
    d, uv, idx = hairline_grid()
    # The outermost crossing, in 3 mm steps from just outside the hair: a ray can cross the hair
    # twice where its thickness dips, and bisection from inside then picked either and tore a slit.
    t, _ = outer_surface(hair_volume, HEAD_C, d, far=0.9, steps=300)
    p = np.asarray(HEAD_C) + d * t[:, None]
    # Soft locks rather than grooves: the texture carries the strands.
    az = np.arctan2(d[:, 0], d[:, 2])
    locks = np.sin(az * 13 + 1.7 * np.sin(p[:, 1] * 6)) * 0.6 + np.sin(az * 29 + 1.3) * 0.4
    t = t * (1 + 0.002 * locks * (hair_thickness(p) > 0.01))
    p = np.asarray(HEAD_C) + d * t[:, None]
    # Strands fall downward: u runs round the head, v down it.
    uv = np.stack([np.arctan2(p[:, 0], p[:, 2]) / (2 * math.pi) * 6, -p[:, 1] * 2.2], axis=1)
    hair = dict(pos=p, nrm=gradient(hair_volume, p), uv=uv, idx=idx, material=5)
    return [skin, hair, hair_locks()]

def hair_locks(count=85, seed=5):
    """Tapered locks lying on the hair: each follows the fall of the hair (down, or along the bangs'
    sweep), hugging the surface, and runs on past the bob's edge into a pointed tip that flicks out.
    They break the smooth shell into the messy bob of the sheet."""
    rng = np.random.default_rng(seed)
    d, _, _ = hairline_grid(cols=96, rows=40)
    t, _ = outer_surface(hair_volume, HEAD_C, d, far=0.9, steps=300)
    roots = np.asarray(HEAD_C) + d * t[:, None]
    # Start where the hair is thick enough and not under the bust or in the face.
    roots = roots[(hair_thickness(roots) > 0.03) & (roots[:, 1] > -0.3)]
    roots = roots[rng.choice(len(roots), size=min(count, len(roots)), replace=False)]
    eps = 1e-3
    def normal(p):
        g = np.array([hair_volume(p + e) - hair_volume(p - e) for e in np.eye(3) * eps])
        return g / max(np.linalg.norm(g), 1e-9)
    pos, nrm, uv, idx = [], [], [], []
    for root in roots:
        x, y, z = root
        # Bangs comb along their sweep toward the lower right; the rest falls.
        bang = math.exp(-((x - 0.04) ** 2 + (y - 0.25) ** 2) / 0.04) if z > 0.15 else 0.0
        fall = np.array([0.0, -1.0, 0.0]) * (1 - bang) + np.array([math.cos(-0.5), math.sin(-0.5), 0.0]) * bang
        length = rng.uniform(0.16, 0.3) * (0.8 if bang > 0.5 else 1.0)
        width = rng.uniform(0.045, 0.075)
        lift = rng.uniform(0.003, 0.008)
        steps = 10
        points, normals = [], []
        p = root.copy()
        stopped = False
        for k in range(steps + 1):
            n = normal(p)
            d_sdf = hair_volume(p)
            on_hair = hair_thickness(p) > 0.0 and p[1] > -0.34
            # A lock that leaves the hair toward the face ends there; only at the bob's lower edge
            # does it run on into a tip.
            if stopped or (not on_hair and p[1] > -0.3):
                stopped = True
                points.append(points[-1]); normals.append(normals[-1])
                continue
            if on_hair:
                p = p - n * d_sdf                       # back onto the surface
            points.append(p + n * lift); normals.append(n)
            tangent = fall - n * np.dot(fall, n)
            if not on_hair:
                # Past the edge the tip flicks outward from the head.
                out = np.array([p[0], 0.0, p[2]]); out /= max(np.linalg.norm(out), 1e-9)
                tangent = tangent * 0.8 + out * 0.25
            tangent /= max(np.linalg.norm(tangent), 1e-9)
            p = p + tangent * (length / steps)
        points, normals = np.array(points), np.array(normals)
        tang = np.gradient(points, axis=0)
        tang /= np.maximum(np.linalg.norm(tang, axis=1, keepdims=True), 1e-9)
        side = np.cross(normals, tang)
        side /= np.maximum(np.linalg.norm(side, axis=1, keepdims=True), 1e-9)
        taper = (1 - np.linspace(0, 1, steps + 1)) ** 0.8 * (0.6 + 0.4 * np.sin(np.linspace(0.3, 2.5, steps + 1)))
        base = len(pos) * 3 if False else sum(len(a) for a in pos)
        ring = []
        for k in range(steps + 1):
            # A slightly rounded cross-section: three vertices across.
            for j, sgn in enumerate((-1.0, 0.0, 1.0)):
                ring.append(points[k] + side[k] * sgn * width * taper[k] + normals[k] * (0.006 * (1 - abs(sgn)) - 0.002 * abs(sgn)))
        ring = np.array(ring)
        pos.append(ring)
        # Normals tilt with the rounded cross-section, so a lock shades softly instead of as a flat strip.
        rounded = []
        for k in range(steps + 1):
            for sgn in (-1.0, 0.0, 1.0):
                v = normals[k] + side[k] * sgn * 0.45
                rounded.append(v / np.linalg.norm(v))
        nrm.append(np.array(rounded))
        uv.append(np.stack([np.tile([0.0, 0.5, 1.0], steps + 1) * 0.2 + rng.uniform(0, 1), np.repeat(np.linspace(0, 1, steps + 1), 3) * 0.6], axis=1))
        for k in range(steps):
            for j in range(2):
                a = base + k * 3 + j
                idx += [a, a + 3, a + 1, a + 1, a + 3, a + 4]
    return dict(pos=np.concatenate(pos), nrm=np.concatenate(nrm), uv=np.concatenate(uv), idx=np.array(idx, np.uint32), material=16)

def bust_mesh():
    d, t, uv, idx, p = sampled(bust, (0, -0.85, -0.03), 48, 80)
    mesh = dict(pos=p, nrm=gradient(bust, p), uv=uv, idx=idx)
    # Neck skin only above the collar.
    return split(mesh, lambda c: np.where(c[:, 1] > -0.37, 0, 6))

def sphere_cap(radius, max_angle, rings=10, segments=40):
    """A cap of a sphere around +z, out to max_angle from the axis."""
    pos, nrm = [[0, 0, radius]], [[0, 0, 1]]
    for r in range(1, rings + 1):
        a = max_angle * r / rings
        for j in range(segments):
            b = 2 * math.pi * j / segments
            v = (math.sin(a) * math.cos(b), math.sin(a) * math.sin(b), math.cos(a))
            pos.append([radius * c for c in v]); nrm.append(v)
    idx = [k for j in range(segments) for k in (0, 1 + j, 1 + (j + 1) % segments)]
    for r in range(rings - 1):
        for j in range(segments):
            a0, b0 = 1 + r * segments + j, 1 + r * segments + (j + 1) % segments
            idx += [a0, a0 + segments, b0, b0, a0 + segments, b0 + segments]
    return dict(pos=np.array(pos), nrm=np.array(nrm), idx=np.array(idx, np.uint32))

# Small turns: the targets add linearly, and large turns pull the iris inside the eyeball.
LOOK = {"look_left": ((0, 1, 0), -0.2), "look_right": ((0, 1, 0), 0.2), "look_up": ((1, 0, 0), -0.16), "look_down": ((1, 0, 0), 0.15)}

def eyes_mesh():
    """Sclera, iris rings, pupil and highlights. Look targets turn the iris and pupil about the eyeball's
    centre; the sclera and highlights stay."""
    names = list(LOOK)
    parts = [
        (ellipsoid_mesh((EYE_R, EYE_R, EYE_R), 48, 64), 7, False),
        (sphere_cap(EYE_R * 1.012, 0.82), 8, True),       # iris, outer
        (sphere_cap(EYE_R * 1.018, 0.56), 12, True),      # iris, inner and lighter
        (sphere_cap(EYE_R * 1.024, 0.3), 1, True),        # pupil
    ]
    # Highlights sit on a sphere inside the lids' one, so a closed lid hides them.
    def on_eye(x, y):
        v = np.array([x, y, EYE_R]); return tuple(v / np.linalg.norm(v) * EYE_R * 1.02)
    shine = [ellipsoid_mesh((0.018, 0.021, 0.003), 8, 10, centre=on_eye(0.028, 0.03)),
             ellipsoid_mesh((0.009, 0.009, 0.0025), 8, 10, centre=on_eye(-0.022, -0.03))]
    for m in shine:
        parts.append((m, 2, False))
    prims = []
    for local, material, moves in parts:
        pos, nrm, idx, deltas = [], [], [], [[] for _ in names]
        for c in EYES_AT:
            offset = sum(len(x) for x in pos)
            # Eyes look a little outward and down, as stylised faces do at rest.
            base = rot((1, 0, 0), 0.05)
            pos.append(local["pos"] @ base.T + c); nrm.append(local["nrm"] @ base.T); idx.append(local["idx"] + offset)
            for k, name in enumerate(names):
                if moves:
                    axis, angle = LOOK[name]
                    r = rot(axis, angle) @ base
                    deltas[k].append(local["pos"] @ r.T + c - (local["pos"] @ base.T + c))
                else:
                    deltas[k].append(np.zeros_like(local["pos"]))
        prims.append(dict(pos=np.concatenate(pos), nrm=np.concatenate(nrm), idx=np.concatenate(idx),
                          targets=[np.concatenate(d) for d in deltas], material=material))
    return prims, names

# Eyelids: sphere patches round each eyeball. Pitch runs from the top (90°) down to the lid's edge;
# targets move the edge. Upper edge rests at 24°, lower at -32° (degrees from the eye's equator).
LID = {
    "rest": (18, -28),
    "blink": (-16, -18),
    "wide": (36, -36),
    "squint": (8, -18),
    "happy": (14, -10),
}
LID_R = EYE_R * 1.035

def lid_patch(upper, edge, yaw_span=100, rows=10, cols=40):
    """Positions of one lid for an edge pitch (degrees), on a sphere of LID_R round the origin."""
    pos = []
    for r in range(rows + 1):
        f = r / rows
        for c in range(cols + 1):
            yaw = math.radians(-yaw_span + 2 * yaw_span * c / cols)
            # The opening is almond-shaped: higher in the middle for the upper lid, lower for the lower.
            e = edge - (6 if upper else -6) * (yaw / math.radians(yaw_span)) ** 2
            start = 90 if upper else -90
            pitch = math.radians(start + (e - start) * f)
            pos.append([LID_R * math.cos(pitch) * math.sin(yaw), LID_R * math.sin(pitch), LID_R * math.cos(pitch) * math.cos(yaw)])
    idx = []
    for r in range(rows):
        for c in range(cols):
            a, b = r * (cols + 1) + c, r * (cols + 1) + c + 1
            ca, cb = a + cols + 1, b + cols + 1
            idx += ([a, ca, b, b, ca, cb] if upper else [a, b, ca, b, cb, ca])
    return np.array(pos), np.array(idx, np.uint32), rows, cols

def lash_strip(upper, edge, yaw_span=88, cols=36, thickness=0.012):
    """A dark line along the lid's edge, sitting just proud of it."""
    pos = []
    for k, (rr, de) in enumerate([(LID_R * 1.0, 0.0), (LID_R * 1.0 + thickness, 2.5 if upper else -1.5)]):
        for c in range(cols + 1):
            yaw = math.radians(-yaw_span + 2 * yaw_span * c / cols)
            e = edge - (6 if upper else -6) * (yaw / math.radians(yaw_span)) ** 2 + de
            pitch = math.radians(e)
            pos.append([rr * math.cos(pitch) * math.sin(yaw), rr * math.sin(pitch), rr * math.cos(pitch) * math.cos(yaw)])
    idx = []
    for c in range(cols):
        a, b = c, c + 1
        idx += [a, b, a + cols + 1, b, b + cols + 1, a + cols + 1]
    return np.array(pos), np.array(idx, np.uint32)

def lids_mesh():
    names = [k for k in LID if k != "rest"]
    prims = []
    specs = [("upper", True, 14), ("lower", False, 14), ("lash", True, 15), ("lashlow", False, 15)]
    for kind, upper, material in specs:
        pos, nrm, idx, deltas = [], [], [], [[] for _ in names]
        for c in EYES_AT:
            if kind in ("upper", "lower"):
                rest, tri, *_ = lid_patch(upper, LID["rest"][0 if upper else 1])
                shapes = [lid_patch(upper, LID[n][0 if upper else 1])[0] for n in names]
            else:
                th = 0.028 if kind == "lash" else 0.006
                rest, tri = lash_strip(upper, LID["rest"][0 if upper else 1], thickness=th)
                shapes = [lash_strip(upper, LID[n][0 if upper else 1], thickness=th)[0] for n in names]
            base = rot((1, 0, 0), 0.05)
            offset = sum(len(x) for x in pos)
            pos.append(rest @ base.T + c)
            nrm.append((rest / np.linalg.norm(rest, axis=1, keepdims=True)) @ base.T)
            idx.append(tri + offset)
            for k, shape in enumerate(shapes):
                deltas[k].append((shape - rest) @ base.T)
        prims.append(dict(pos=np.concatenate(pos), nrm=np.concatenate(nrm), idx=np.concatenate(idx),
                          targets=[np.concatenate(d) for d in deltas], material=material))
    return prims, names

def tube(points, normals, radius, flat=0.5, sides=8):
    tangent = np.gradient(points, axis=0)
    tangent /= np.maximum(np.linalg.norm(tangent, axis=1, keepdims=True), 1e-9)
    side = np.cross(normals, tangent)
    taper = np.sin(np.linspace(0.12, math.pi - 0.12, len(points))) ** 0.6
    pos = []
    for i in range(len(points)):
        for j in range(sides):
            b = 2 * math.pi * j / sides
            pos.append(points[i] + side[i] * math.cos(b) * radius * taper[i] + normals[i] * math.sin(b) * radius * flat * taper[i])
    idx = []
    for i in range(len(points) - 1):
        for j in range(sides):
            a, b = i * sides + j, i * sides + (j + 1) % sides
            idx += [a, a + sides, b, b, a + sides, b + sides]
    return np.array(pos), np.array(idx, np.uint32)

BROWS = {"rest": (0.0, 0.0, 0.0), "raise": (0.035, 0.03, 0.0), "frown": (-0.012, -0.03, 0.0), "inner_up": (0.0, 0.03, 0.0)}

def brows_mesh():
    """Brows above the eyes; targets raise both, frown, lift the inner ends, or raise just the
    viewer's right brow (thinking)."""
    names = [k for k in BROWS if k != "rest"] + ["one_up"]
    pos, idx, deltas = [], [], [[] for _ in names]
    def curve(side, lift, inner, extra=0.0):
        t = np.linspace(-1, 1, 18)
        ex = EYES_AT[0 if side < 0 else 1][0]
        x = side * (abs(ex) + 0.01 + t * 0.085)
        # Inner end is t = -1 (toward the nose).
        y = 0.155 + 0.024 * (1 - t * t) + lift + inner * (1 - (t + 1) / 2) * 0.8 + extra - 0.014 * t
        return np.stack([x, y], axis=1)
    def place(xy):
        p, n = front_point(head, xy[:, 0], xy[:, 1])
        return tube(p + n * 0.007, n, 0.0085, flat=0.45)
    for side in (-1, 1):
        rest, tri = place(curve(side, 0, 0))
        offset = sum(len(x) for x in pos)
        pos.append(rest); idx.append(tri + offset)
        for k, name in enumerate(names):
            if name == "one_up":
                shape = curve(side, 0.03 if side > 0 else -0.005, 0)
            else:
                lift, inner, _ = BROWS[name]
                shape = curve(side, lift, inner)
            deltas[k].append(place(shape)[0] - rest)
    p = np.concatenate(pos)
    return dict(pos=p, nrm=np.tile([0, 0, 1.0], (len(p), 1)), idx=np.concatenate(idx), targets=[np.concatenate(d) for d in deltas], material=11), names

# Mouth: corners at ±w; c is the corners' lift (a smile), cl/cr per side for a smirk; the opening's
# top and bottom; the lips' thickness.
MOUTH = {
    "rest": dict(w=0.062, c=0.008, top=0.0, bottom=0.0, upper=0.024, lower=0.034),
    "smile": dict(w=0.085, c=0.03, top=0.0, bottom=0.0, upper=0.011, lower=0.016),
    "open": dict(w=0.068, c=0.006, top=0.018, bottom=0.05, upper=0.012, lower=0.017),
    "round": dict(w=0.042, c=0.0, top=0.022, bottom=0.03, upper=0.016, lower=0.02),
    "wide": dict(w=0.082, c=0.012, top=0.008, bottom=0.024, upper=0.01, lower=0.015),
    "small_o": dict(w=0.04, c=0.0, top=0.012, bottom=0.018, upper=0.014, lower=0.018),
    "smirk": dict(w=0.072, c=0.0, cr=0.028, top=0.0, bottom=0.0, upper=0.012, lower=0.017),
    "frown": dict(w=0.064, c=-0.014, top=0.0, bottom=0.0, upper=0.012, lower=0.017),
    "press": dict(w=0.066, c=0.004, top=0.0, bottom=0.0, upper=0.007, lower=0.01),
    "teeth": dict(w=0.068, c=0.008, top=0.006, bottom=0.016, upper=0.012, lower=0.012),
}
COLS, ROWS = 30, 4

def mouth_curves(shape):
    s = -np.cos(np.linspace(0, math.pi, COLS))
    oval = np.sqrt(np.clip(1 - s * s, 0, 1))
    c_left, c_right = shape.get("cl", shape["c"]), shape.get("cr", shape["c"])
    lift = np.where(s < 0, c_left, c_right) * s * s
    bow = -0.004 * np.exp(-(s / 0.16) ** 2)
    inner_u = lift + shape["top"] * oval
    inner_l = lift - shape["bottom"] * oval
    outer_u = inner_u + shape["upper"] * oval ** 0.7 + bow * (shape["upper"] > 0.009)
    outer_l = inner_l - shape["lower"] * oval ** 0.8
    x = shape["w"] * s
    return x, inner_u, inner_l, outer_u, outer_l

def mouth_mesh():
    names = [k for k in MOUTH if k != "rest"]
    def band(x, y0, y1, lift0, lift1):
        rows = np.linspace(0, 1, ROWS)
        y = y0[None, :] * (1 - rows[:, None]) + y1[None, :] * rows[:, None]
        lifts = lift0 * (1 - rows[:, None]) + lift1 * rows[:, None]
        xy = np.stack([np.broadcast_to(x, y.shape), y], axis=-1).reshape(-1, 2)
        p, n = front_point(head, xy[:, 0], xy[:, 1] + MOUTH_Y)
        return p + n * np.broadcast_to(lifts, y.shape).reshape(-1, 1)
    def parts(shape):
        x, iu, il, ou, ol = mouth_curves(shape)
        gap = np.clip(iu - il, 0, None)
        # Inside and teeth sit just above the skin (below it they never showed), the lips above them.
        return [band(x, iu, ou, 0.006, 0.004),                  # upper lip
                band(x, il, ol, 0.007, 0.004),                  # lower lip
                band(x, iu, il, 0.002, 0.002),                  # inside
                band(x * 0.78, iu, iu - np.minimum(gap, 0.016), 0.003, 0.003)]  # upper teeth
    idx = []
    for r in range(ROWS - 1):
        for c in range(COLS - 1):
            a, b = r * COLS + c, r * COLS + c + 1
            idx += [a, a + COLS, b, b, a + COLS, b + COLS]
    idx = np.array(idx, np.uint32)
    rest = parts(MOUTH["rest"])
    shapes = [parts(MOUTH[n]) for n in names]
    prims = []
    for k, material in enumerate([3, 3, 4, 7]):
        prims.append(dict(pos=rest[k], nrm=gradient(head, rest[k]), idx=idx, targets=[s[k] - rest[k] for s in shapes], material=material))
    return prims, names

def blush_mesh():
    out = []
    for s in (-1, 1):
        a = np.linspace(0, 2 * math.pi, 40, endpoint=False)
        for scale in (1.0, 0.65):
            xy = np.vstack([[0, 0], np.stack([np.cos(a) * 0.06 * scale, np.sin(a) * 0.035 * scale], axis=1)]) + np.array([s * 0.2, -0.085])
            p, n = front_point(head, xy[:, 0], xy[:, 1])
            out.append(dict(pos=p + n * (0.003 + 0.001 * (scale < 1)), nrm=n, idx=np.array([k for j in range(40) for k in (0, 1 + j, 1 + (j + 1) % 40)], np.uint32), material=10))
    return out

def hair_textures(size=512, seed=11):
    """Tileable long strands: a grain texture with light and dark locks, and its normal map."""
    from PIL import Image
    rng = np.random.default_rng(seed)
    noise = rng.standard_normal((size, size))
    fy = np.fft.fftfreq(size)[:, None]; fx = np.fft.fftfreq(size)[None, :]
    spectrum = np.fft.fft2(noise)
    strands = np.real(np.fft.ifft2(spectrum * np.exp(-((fx / 0.25) ** 2 + (fy / 0.006) ** 2))))
    locks = np.real(np.fft.ifft2(spectrum * np.exp(-((fx / 0.03) ** 2 + (fy / 0.003) ** 2))))
    strands = (strands - strands.mean()) / strands.std()
    locks = (locks - locks.mean()) / locks.std()
    h = strands + 1.5 * locks
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) / 2
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) / 2
    n = np.stack([-dx * 0.3, dy * 0.3, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    # Lighter and darker locks, as in a painted bob.
    grain = np.clip(0.86 + 0.05 * strands + 0.11 * locks, 0, 1)
    def png(a):
        out = io.BytesIO(); Image.fromarray(a).save(out, 'PNG', optimize=True); return out.getvalue()
    return png((np.stack([grain] * 3, axis=-1) * 255).astype(np.uint8)), png(((n * 0.5 + 0.5) * 255).astype(np.uint8))

# ---------------------------------------------------------------- materials

def mat(name, color, rough, emissive=None, alpha=1.0, textured=False, sheen=None, normal_scale=1.0):
    m = {"name": name, "pbrMetallicRoughness": {"baseColorFactor": srgb_to_linear(color) + [alpha], "metallicFactor": 0, "roughnessFactor": rough}}
    if textured:
        m["pbrMetallicRoughness"]["baseColorTexture"] = {"index": 0}
        m["normalTexture"] = {"index": 1, "scale": normal_scale}
    if sheen:
        m["extensions"] = {"KHR_materials_sheen": {"sheenColorFactor": srgb_to_linear(sheen), "sheenRoughnessFactor": 0.4}}
    if emissive:
        m["emissiveFactor"] = srgb_to_linear(emissive)
    if alpha < 1:
        m["alphaMode"] = "BLEND"
    return m

# ---------------------------------------------------------------- build

def build():
    g = Glb()
    fur = hair_textures()
    g.gltf["samplers"].append({"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497})
    grain = g.image(fur[0], "strands-grain"); normal = g.image(fur[1], "strands-normal")
    g.gltf["textures"] += [{"sampler": 0, "source": grain}, {"sampler": 0, "source": normal}]
    g.gltf["materials"] = [
        mat("skin", "#F3C3AA", 0.55, sheen="#FFE0D2"),                       # 0
        mat("pupil", "#0E0B18", 0.12),                                       # 1
        mat("shine", "#FFFFFF", 0.3, emissive="#FFFFFF"),                    # 2
        mat("lips", "#E38A8E", 0.3),                                         # 3
        mat("inside", "#5C1F2C", 0.6),                                       # 4
        mat("hair", "#5558B4", 0.32, textured=True, sheen="#B1B4FF", normal_scale=1.0),  # 5
        mat("sweater", "#BEBCD4", 0.72),                                     # 6
        mat("white", "#EEF0F6", 0.2),                                        # 7 sclera, teeth
        mat("iris", "#2440A8", 0.16),                                        # 8
        mat("badge", "#1E2430", 0.35),                                       # 9
        mat("blush", "#FF8A98", 0.8, alpha=0.18),                            # 10
        mat("brow", "#3A2F86", 0.6),                                         # 11
        mat("iris_light", "#5C8AF0", 0.16),                                  # 12
        mat("alert", "#FF4B3A", 0.4, emissive="#FF4B3A"),                    # 13
        # The same skin as the face, so a closed lid does not read as a disc.
        {**mat("lid", "#F3C3AA", 0.55, sheen="#FFE0D2"), "doubleSided": True},  # 14
        {**mat("lash", "#241C46", 0.5), "doubleSided": True},                # 15
        {**mat("locks", "#6266C6", 0.3, textured=True, sheen="#B9BCFF", normal_scale=1.0), "doubleSided": True},  # 16
    ]
    g.gltf["extensionsUsed"] = ["KHR_materials_sheen"]

    head_prims = head_and_hair()
    eyes, eye_targets = eyes_mesh()
    lids, lid_targets = lids_mesh()
    brows, brow_targets = brows_mesh()
    mouth, mouth_targets = mouth_mesh()

    # 0 Root, 1 Bust, 2 HeadPivot (at the neck), 3 Head, 4 Eyes, 5 Lids, 6 Brows, 7 Mouth, 8 Blush, 9 MicOff.
    g.node("Root", children=[1, 2])
    g.node("Bust", g.mesh("Bust", bust_mesh()))
    g.node("HeadPivot", t=NECK, children=[3, 4, 5, 6, 7, 8])
    off = -NECK
    g.node("Head", g.mesh("Head", head_prims), t=off)
    m_eyes = g.mesh("Eyes", eyes, eye_targets); g.node("Eyes", m_eyes, t=off)
    m_lids = g.mesh("Lids", lids, lid_targets); g.node("Lids", m_lids, t=off)
    m_brows = g.mesh("Brows", [brows], brow_targets); g.node("Brows", m_brows, t=off)
    m_mouth = g.mesh("Mouth", mouth, mouth_targets); g.node("Mouth", m_mouth, t=off)
    g.node("Blush", g.mesh("Blush", blush_mesh()), t=off)

    # Mic-off badge, as on Nova: grown from a point by a morph target.
    mic = ellipsoid_mesh((0.035, 0.06, 0.02), 10, 14, centre=(0, 0.03, 0))
    slash = ellipsoid_mesh((0.011, 0.1, 0.011), 8, 10)
    slash = dict(pos=slash["pos"] @ rot((0, 0, 1), 0.8).T + np.array([0, 0.01, 0.02]), nrm=slash["nrm"] @ rot((0, 0, 1), 0.8).T, idx=slash["idx"])
    badge_prims = [dict(pos=m["pos"] * 0.01, nrm=m["nrm"], idx=m["idx"], targets=[m["pos"] * 0.99], material=mt) for m, mt in ((mic, 9), (slash, 9))]
    m_badge = g.mesh("MicOff", badge_prims, ["show"])
    badge = g.node("MicOff", m_badge, t=(0.42, -0.62, 0.35))
    g.gltf["nodes"][badge]["scale"] = [2.4, 2.4, 2.4]
    g.gltf["nodes"][0]["children"].append(badge)

    clips(g)
    return g.bytes(), dict(m_eyes=m_eyes, m_lids=m_lids, m_brows=m_brows, m_mouth=m_mouth, eye_targets=eye_targets, lid_targets=lid_targets,
                           brow_targets=brow_targets, mouth_targets=mouth_targets, badge=badge, m_badge=m_badge,
                           eye_prims=len(eyes), lid_prims=len(lids), mouth_prims=len(mouth))

def clips(g):
    pivot = 2
    def rot_clip(name, duration, axis, angle, peaks):
        t, e = wave(duration, peaks)
        g.animation(name, [(pivot, "rotation", t, [quat(axis, angle * v) for v in e])])
    t, e = wave(5.0, 2)
    g.animation("idle_a", [(pivot, "rotation", t, [quat((0, 0, 1), 0.025 * v) for v in e])])
    t, e = wave(6.0, 2)
    g.animation("idle_b", [(pivot, "rotation", t, [quat((0, 1, 0), 0.04 * v) for v in e])])
    rot_clip("nod", 0.9, (1, 0, 0), 0.12, 4)
    rot_clip("shake", 0.9, (0, 1, 0), 0.2, 4)
    rot_clip("lean_in", 1.0, (1, 0, 0), 0.08, 1)
    rot_clip("interrupt", 0.5, (1, 0, 0), -0.06, 1)
    rot_clip("think", 3.0, (0, 0, 1), -0.04, 1)

# ---------------------------------------------------------------- package

VISEMES = ["sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"]
VISEME_MOUTHS = {
    "sil": {}, "PP": {"press": 1}, "FF": {"teeth": 0.8}, "TH": {"teeth": 0.6, "open": 0.2}, "DD": {"open": 0.45},
    "kk": {"open": 0.4}, "CH": {"wide": 0.6, "teeth": 0.3}, "SS": {"teeth": 0.7, "wide": 0.3}, "nn": {"open": 0.3},
    "RR": {"round": 0.5}, "aa": {"open": 0.9}, "E": {"wide": 0.9}, "I": {"wide": 0.7}, "O": {"round": 0.9}, "U": {"round": 0.7, "small_o": 0.3},
}

def package(glb, ids, posters):
    def w(mesh, targets, prims, node):
        return lambda name, v: {"node": node, "mesh": ids[mesh], "primitives": list(range(prims)), "targetIndex": ids[targets].index(name), "value": v}
    eye = w("m_eyes", "eye_targets", ids["eye_prims"], 4)
    lid = w("m_lids", "lid_targets", ids["lid_prims"], 5)
    brow = w("m_brows", "brow_targets", 1, 6)
    mouth = w("m_mouth", "mouth_targets", ids["mouth_prims"], 7)
    def turn(yaw=0.0, pitch=0.0, roll=0.0):
        q = quat((0, 1, 0), yaw)
        for axis, a in (((1, 0, 0), pitch), ((0, 0, 1), roll)):
            r = quat(axis, a)
            x, y, z, ww = q; a2, b2, c2, d2 = r
            q = [ww * a2 + x * d2 + y * c2 - z * b2, ww * b2 - x * c2 + y * d2 + z * a2, ww * c2 + x * b2 - y * a2 + z * d2, ww * d2 - x * a2 - y * b2 - z * c2]
        return {"node": 2, "property": "rotation", "value": [round(v, 6) for v in q]}

    poses = {
        "idle": [mouth("smile", 0.25)],
        # Connecting: turned aside, eyes to the side, a small smile.
        "connecting": [turn(yaw=-0.42, roll=0.05), eye("look_left", 0.7), mouth("smile", 0.3)],
        "listening": [lid("wide", 0.35), brow("inner_up", 0.4), mouth("smile", 0.35), turn(roll=-0.05)],
        "thinking": [eye("look_up", 0.8), eye("look_right", 0.5), brow("one_up", 1), mouth("smirk", 0.9), turn(yaw=0.08, roll=0.06)],
        "speaking": [brow("raise", 0.25), lid("wide", 0.15)],
        "interrupted": [lid("wide", 0.9), brow("raise", 1), mouth("small_o", 0.9), turn(pitch=-0.05)],
        "muted": [lid("blink", 1), mouth("smile", 0.45), brow("inner_up", 0.2), turn(pitch=0.06, roll=0.05)],
        "reduced_motion": [],
        "expr_neutral": [],
        "expr_happy": [mouth("smile", 1), lid("happy", 0.7), brow("raise", 0.2)],
        "expr_curious": [brow("one_up", 0.8), lid("wide", 0.3), mouth("small_o", 0.4), turn(roll=0.08)],
        "expr_thinking": [eye("look_up", 0.7), eye("look_right", 0.5), brow("one_up", 0.8), mouth("smirk", 0.8)],
        "expr_concerned": [brow("inner_up", 1), brow("frown", 0.3), mouth("frown", 0.8)],
        "expr_surprised": [lid("wide", 1), brow("raise", 1), mouth("round", 0.8)],
        "expr_apologetic": [brow("inner_up", 0.8), lid("squint", 0.3), mouth("frown", 0.4), turn(pitch=0.06)],
        "expr_confident": [mouth("smirk", 0.6), mouth("smile", 0.5), lid("squint", 0.25)],
    }
    for v in VISEMES:
        poses["viseme_" + v] = [mouth(name, x) for name, x in VISEME_MOUTHS[v].items()]

    bindings = {
        "schemaVersion": "1.0", "renderer": "native3d",
        "animations": {n: n for n in ("idle_a", "idle_b", "nod", "shake", "lean_in", "interrupt", "think")},
        "poses": poses,
        "parameters": {},
        "channelMasks": {"expression": ["face"], "speech": ["mouthShape"], "idle": ["head"], "reflex": ["face"]},
        "framing": {"cameraPosition": [0, 0.0, 2.55], "lookAt": [0, -0.14, 0], "verticalFov": 30, "safeInset": 0.04, "fit": "contain"},
        "lighting": "portrait",
        "springs": False,
        "gaze": {"node": 4, "morphs": {"mesh": ids["m_eyes"], "primitives": list(range(ids["eye_prims"])), **{k: ids["eye_targets"].index("look_" + k) for k in ("left", "right", "up", "down")}},
                 "turn": {"node": 2, "factor": 0.4}},
        "blink": [{"node": 5, "mesh": ids["m_lids"], "targetIndex": ids["lid_targets"].index("blink"),
                   "suppressExpressionTargets": [ids["lid_targets"].index(n) for n in ("wide", "squint", "happy")]}],
        "muteBadge": {"node": ids["badge"], "mesh": ids["m_badge"], "targetIndex": 0},
        "stateAnimations": {"thinking": "think"},
        "headNode": 2,
        "speechMouthMesh": ids["m_mouth"],
        "expressionMouthTargets": [ids["mouth_targets"].index(n) for n in ("smile", "smirk", "frown", "small_o")],
    }
    manifest = {
        "schemaVersion": "1.0", "id": "kairo", "displayName": "Kairo", "assetVersion": "1.0.0",
        "minRuntimeVersion": "1.0.0", "profile": "character",
        "representations": [{
            "id": "native3d-primary", "renderer": "native3d", "format": "glb",
            "model": "models/kairo.glb", "bindings": "bindings/native3d.json",
            "capabilities": ["ambient", "expressions", "gestures", "reducedMotion", "speechArticulation", "gaze", "blink", "themeSlots"],
            "platforms": ["android", "ios", "maccatalyst", "windows"],
            "idleOwner": "scheduler", "blinkOwner": "scheduler", "gazeOwner": "scheduler",
        }],
        "states": {s: {"pose": s, "transitionMs": 90 if s == "interrupted" else 260, "inputReactive": s == "listening",
                       "outputReactive": s == "speaking", "gaze": "processing" if s == "thinking" else "engaged"}
                   for s in ("idle", "connecting", "listening", "thinking", "speaking", "interrupted", "muted")},
        "expressions": {e: {"pose": "expr_" + e, "channels": ["face"], "transitionMs": 120 if e == "surprised" else 260}
                        for e in ("neutral", "happy", "curious", "thinking", "concerned", "surprised", "apologetic", "confident")},
        "speech": {"mode": "canonicalVisemes", "canonicalProfile": "oculus15-v1",
                   "mapping": {str(i): "viseme_" + v for i, v in enumerate(VISEMES)}, "expressionMouthScale": 0.5, "releaseMs": 90},
        "motions": {"idleVariants": ["idle_a", "idle_b"],
                    "gestures": {"nod": "nod", "shake": "shake", "lean": "lean_in", "interrupt": "interrupt"},
                    "seedable": True, "blinkIntervalSeconds": [2.8, 6.0]},
        "themes": {"slots": {"badge": {"light": "#1E2430", "dark": "#E8ECF2", "bindings": ["material:9"]}}},
        "reducedMotion": {"pose": "reduced_motion", "retainSpeech": True, "transitionMs": 180},
        "posters": {"light": "previews/poster-light.png", "dark": "previews/poster-dark.png"},
        "license": "LICENSE.txt", "provenance": "provenance.json",
    }
    files = {
        "models/kairo.glb": glb,
        "bindings/native3d.json": json.dumps(bindings, indent=1).encode(),
        "previews/poster-light.png": open(posters[0], 'rb').read() if posters else tiny_png(),
        "previews/poster-dark.png": open(posters[1], 'rb').read() if posters else tiny_png(),
        "LICENSE.txt": b"Generated for the Spine Avatar Lab by source/kairo.py after the Kairo concept sheet. Same licence as the Maui.Spine repository.\n",
        "provenance.json": json.dumps({"model": "procedural (source/kairo.py)", "posters": "captured from the lab" if posters else "placeholder 1x1, not a render"}, indent=1).encode(),
        "source/kairo.py": open(__file__, 'rb').read(),
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
    path = os.path.join(out_dir, "kairo.spineavatar")
    open(path, 'wb').write(data)
    print(path, len(data), "bytes")
