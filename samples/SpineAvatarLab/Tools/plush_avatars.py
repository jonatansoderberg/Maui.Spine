"""Generates the plush avatars (soft, fuzzy blobs with dot eyes) for the Spine Avatar Lab (#524).

Usage: python plush_avatars.py <out-dir> [name ...] [--posters <dir>]
Writes <out-dir>/plush-<name>.spineavatar for mochi, sprig, bean and puff (or the names given).
With --posters, <dir>/plush-<name>-light.png and -dark.png become the posters.

Each body is a smooth union of ellipsoids, sampled along rays from its centre, so it must be
star-shaped. Fur is a tileable normal map and a light grain texture tinted by the material colour,
plus KHR_materials_sheen for renderers that have it. Eyes, mouth and cheeks are meshes that hug the
body; their morph targets give blink, happy and wide eyes, mouth expressions and the 15 visemes.
Needs numpy and Pillow.
"""
import hashlib, io, json, math, os, struct, sys, zipfile
import numpy as np
from PIL import Image

# ---------------------------------------------------------------- shapes

def ellipsoid(p, c, r):
    q = (p - np.asarray(c)) / np.asarray(r)
    return (np.linalg.norm(q, axis=-1) - 1) * min(r)

def smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0, 1)
    return b * (1 - h) + a * h - k * h * (1 - h)

def smax(a, b, k):
    return -smin(-a, -b, k)

def flat_bottom(d, p, y=-0.5):
    return smax(d, -(p[..., 1] - y), 0.08)

CHARACTERS = {
    # Peach dumpling with two small ear nubs.
    "mochi": dict(
        name="Mochi", color="#F49A84", dark="#F7A894", sheen="#FFD9CC", accent="#FF6F61",
        sdf=lambda p: flat_bottom(smin(smin(
            ellipsoid(p, (0, -0.04, 0), (0.62, 0.48, 0.52)),
            ellipsoid(p, (-0.36, 0.38, -0.02), (0.13, 0.12, 0.11)), 0.12),
            ellipsoid(p, (0.36, 0.38, -0.02), (0.13, 0.12, 0.11)), 0.12), p),
        eye=(0.17, 0.06), mouth=-0.08, blush=(0.31, -0.06), extras=[]),
    # Mint pear with a two-leaf sprout.
    "sprig": dict(
        name="Sprig", color="#62C9A0", dark="#6FD4AB", sheen="#C9F5E2", accent="#3E9B5F",
        sdf=lambda p: flat_bottom(smin(
            ellipsoid(p, (0, -0.16, 0), (0.56, 0.36, 0.5)),
            ellipsoid(p, (0, 0.14, 0), (0.37, 0.42, 0.36)), 0.28), p),
        eye=(0.15, 0.08), mouth=-0.06, blush=(0.28, -0.08), extras=["leaves"]),
    # Butter-yellow bean leaning a little, with a bobbing antenna.
    "bean": dict(
        name="Bean", color="#F5BC45", dark="#F7C55B", sheen="#FFF0C2", accent="#FF7A59",
        sdf=lambda p: flat_bottom(smin(
            ellipsoid(p, (0.02, 0.06, 0), (0.42, 0.56, 0.42)),
            ellipsoid(p, (-0.1, -0.26, 0), (0.44, 0.3, 0.42)), 0.22), p),
        eye=(0.14, 0.16), mouth=0.03, blush=(0.27, 0.02), extras=["antenna"]),
    # Lilac cloud of soft lobes.
    "puff": dict(
        name="Puff", color="#A98CF0", dark="#B49BF4", sheen="#E6DCFF", accent="#7C5CE0",
        sdf=lambda p: flat_bottom(smin(smin(smin(smin(
            ellipsoid(p, (0, -0.08, 0), (0.5, 0.4, 0.46)),
            ellipsoid(p, (-0.43, -0.12, 0), (0.28, 0.28, 0.3)), 0.14),
            ellipsoid(p, (0.43, -0.12, 0), (0.28, 0.28, 0.3)), 0.14),
            ellipsoid(p, (-0.2, 0.28, 0), (0.3, 0.28, 0.3)), 0.14),
            ellipsoid(p, (0.2, 0.28, 0), (0.3, 0.28, 0.3)), 0.14), p),
        eye=(0.16, 0.02), mouth=-0.11, blush=(0.31, -0.1), extras=[]),
}

CENTRE = np.array([0, -0.08, 0])

def gradient(sdf, p, e=1e-4):
    g = np.stack([sdf(p + np.array(d) * e) - sdf(p - np.array(d) * e) for d in np.eye(3)], axis=-1)
    return g / np.linalg.norm(g, axis=-1, keepdims=True)

def ray_surface(sdf, origin, direction, far=1.6, steps=40):
    """Distance along each ray to the surface, by bisection (the inside is negative)."""
    lo = np.zeros(direction.shape[:-1]); hi = np.full(direction.shape[:-1], far)
    for _ in range(steps):
        mid = (lo + hi) / 2
        inside = sdf(origin + direction * mid[..., None]) < 0
        lo = np.where(inside, mid, lo); hi = np.where(inside, hi, mid)
    return (lo + hi) / 2

def front_point(sdf, x, y):
    """The body surface seen from the front at (x, y), and its normal."""
    x, y = np.broadcast_arrays(np.asarray(x, float), np.asarray(y, float))
    origin = np.stack([x, y, np.zeros_like(x)], axis=-1)
    z = ray_surface(sdf, origin, np.array([0, 0, 1.0]), far=1.2)
    p = np.stack([x, y, z], axis=-1)
    return p, gradient(sdf, p)

def frame(n):
    """A rotation whose z axis is the normal n and whose y axis stays as upright as it can."""
    z = n / np.linalg.norm(n)
    x = np.cross([0, 1, 0], z); x /= np.linalg.norm(x)
    return np.stack([x, np.cross(z, x), z], axis=1)

# ---------------------------------------------------------------- meshes

def uv_sphere(rings, segments):
    """Directions, UVs and triangles of a UV sphere with a seam column (for the fur UVs)."""
    theta = np.linspace(0, math.pi, rings + 1)
    phi = np.linspace(0, 2 * math.pi, segments + 1)
    t, f = np.meshgrid(theta, phi, indexing='ij')
    d = np.stack([np.sin(t) * np.sin(f), np.cos(t), np.sin(t) * np.cos(f)], axis=-1).reshape(-1, 3)
    uv = np.stack([f / (2 * math.pi), t / math.pi], axis=-1).reshape(-1, 2)
    idx = []
    w = segments + 1
    for i in range(rings):
        for j in range(segments):
            a, b, c, e = i * w + j, i * w + j + 1, (i + 1) * w + j, (i + 1) * w + j + 1
            if i > 0: idx += [a, c, b]
            if i < rings - 1: idx += [b, c, e]
    return d, uv, np.array(idx, np.uint32)

def tufts(d, seed=3):
    """A smooth -1..1 field over directions: soft tufts so the outline is not a perfect curve."""
    rng = np.random.default_rng(seed)
    k = rng.standard_normal((40, 3)) * 9
    phase = rng.uniform(0, 2 * math.pi, 40)
    # numpy 2 on Apple's Accelerate reports spurious floating-point errors from matmul; the result is finite.
    with np.errstate(all='ignore'):
        v = np.sin(d @ k.T + phase).sum(axis=1)
    return v / np.abs(v).max()

def body_mesh(sdf):
    d, uv, idx = uv_sphere(72, 112)
    t = ray_surface(sdf, CENTRE, d)
    # Kept off the face, where eyes, mouth and cheeks sit on the smooth surface.
    t *= 1 + 0.009 * tufts(d) * np.clip((0.6 - d[:, 2]) / 0.3, 0, 1)
    p = CENTRE + d * t[:, None]
    # Fur runs downward: more repeats around than top to bottom.
    return dict(pos=p, nrm=gradient(sdf, p), uv=uv * np.array([7.0, 3.5]), idx=idx)

def leaf_mesh(length=0.26, width=0.11, n=14):
    """A curled leaf along +y in its own space, two-sided."""
    pos, nrm, uv, idx = [], [], [], []
    for i in range(n + 1):
        s = i / n
        half = width * math.sin(math.pi * s) ** 0.8
        y = length * s
        bend = 0.06 * s * s
        for k, x in enumerate((-half, 0.0, half)):
            pos.append((x, y, bend - 0.03 * (x / max(width, 1e-6)) ** 2 * 4)); nrm.append((0, 0, 1)); uv.append((k / 2, s))
    for i in range(n):
        for k in range(2):
            a, b = i * 3 + k, i * 3 + k + 1
            c, e = a + 3, b + 3
            idx += [a, b, c, b, e, c]
    return dict(pos=np.array(pos), nrm=np.array(nrm, float), uv=np.array(uv) * 2, idx=np.array(idx, np.uint32))

def ellipsoid_mesh(r, rings=14, segments=20, centre=(0, 0, 0)):
    d, uv, idx = uv_sphere(rings, segments)
    p = d * np.asarray(r) + np.asarray(centre)
    n = d / np.asarray(r); n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return dict(pos=p, nrm=n, uv=uv, idx=idx)

def cylinder_mesh(radius, height, segments=12):
    pos, nrm, idx = [], [], []
    for j in range(segments + 1):
        a = 2 * math.pi * j / segments
        for y in (0, height):
            pos.append((radius * math.sin(a), y, radius * math.cos(a))); nrm.append((math.sin(a), 0, math.cos(a)))
    for j in range(segments):
        a, b, c, e = 2 * j, 2 * j + 1, 2 * j + 2, 2 * j + 3
        idx += [a, c, b, b, c, e]
    return dict(pos=np.array(pos, float), nrm=np.array(nrm, float), uv=np.zeros((len(pos), 2)), idx=np.array(idx, np.uint32))

def eyes_mesh(sdf, eye):
    """Both eyes (glossy black) and their highlights, with blink, happy, wide and squint targets."""
    ex, ey = eye
    rx, ry, rz = 0.058, 0.076, 0.032
    base_eye = ellipsoid_mesh((rx, ry, rz))
    base_shine = ellipsoid_mesh((0.017, 0.019, 0.008), 8, 10, centre=(0.018, 0.027, rz * 0.82))

    def blink(p): return p * np.array([1.05, 0.08, 0.5]) + np.array([0, -0.012, 0])
    def happy(p):
        x = np.clip(p[:, 0] / rx, -1, 1)
        # A thin band bent into an arch: the closed, smiling eye (^).
        return np.stack([p[:, 0] * 1.1, p[:, 1] * 0.16 + ry * 0.75 * (1 - x * x) - ry * 0.3, p[:, 2] * 0.6], axis=1)
    def wide(p): return p * 1.24
    def squint(p): return p * np.array([1.04, 0.55, 0.8])
    def shine_off(p): return p * 0.05 + np.array([0.018, 0.0, rz * 0.4]) * 0.95
    targets = ["blink", "happy", "wide", "squint"]
    eye_f = [blink, happy, wide, squint]
    shine_f = [shine_off, shine_off, wide, squint]

    prims = []
    for part, (local, fs) in enumerate(((base_eye, eye_f), (base_shine, shine_f))):
        pos, nrm, idx, deltas = [], [], [], [[] for _ in targets]
        for side in (-1, 1):
            centre, n = front_point(sdf, side * ex, ey)
            r = frame(n)
            at = centre - n * rz * 0.45
            place = lambda q: q @ r.T + at
            offset = sum(len(x) for x in pos)
            pos.append(place(local['pos'])); nrm.append(local['nrm'] @ r.T); idx.append(local['idx'] + offset)
            for k, f in enumerate(fs):
                deltas[k].append(place(f(local['pos'])) - place(local['pos']))
        prims.append(dict(pos=np.concatenate(pos), nrm=np.concatenate(nrm), idx=np.concatenate(idx),
                          targets=[np.concatenate(d) for d in deltas], material=1 + part))
    return prims, targets

# A mouth is the band between an upper and a lower curve: s runs corner to corner; each curve
# dips by cu/cl at the centre; top/bottom open it with an oval envelope; w is the half-width.
MOUTH_REST = dict(w=0.056, cu=0.022, cl=0.022)
MOUTHS = {
    "smile": dict(w=0.07, cu=0.034, cl=0.036),
    "open_smile": dict(w=0.064, cu=0.01, cl=0.012, bottom=0.042),
    "frown": dict(w=0.048, cu=-0.018, cl=-0.018),
    "small": dict(w=0.026, cu=0.006, cl=0.006),
    "side": dict(w=0.036, cu=0.01, cl=0.01, shift=0.024),
    "open": dict(w=0.042, top=0.02, bottom=0.05),
    "wide": dict(w=0.064, cu=0.006, cl=0.006, top=0.006, bottom=0.02),
    "round": dict(w=0.03, top=0.026, bottom=0.03),
    "pucker": dict(w=0.017, top=0.014, bottom=0.016),
    "press": dict(w=0.05, cu=0.004, cl=0.004),
    "teeth": dict(w=0.044, cu=-0.002, cl=-0.002, bottom=0.012),
    "mid": dict(w=0.044, top=0.01, bottom=0.028),
    "ch": dict(w=0.038, top=0.008, bottom=0.018),
}
COLS, ROWS, LINE = 32, 5, 0.0085

def mouth_outline(w, cu=0.0, cl=0.0, top=0.0, bottom=0.0, shift=0.0):
    s = -np.cos(np.linspace(0, math.pi, COLS))
    oval = np.sqrt(np.clip(1 - s * s, 0, 1))
    line = np.clip(1 - s * s, 0, 1) ** 0.3
    upper = -cu * (1 - s * s) + top * oval + LINE * line
    lower = -cl * (1 - s * s) - bottom * oval - LINE * line
    x = shift + w * s
    rows = np.linspace(0, 1, ROWS)
    y = upper[None, :] * (1 - rows[:, None]) + lower[None, :] * rows[:, None]
    return np.stack([np.broadcast_to(x, y.shape), y], axis=-1).reshape(-1, 2)

def mouth_mesh(sdf, my):
    def place(xy):
        p, n = front_point(sdf, xy[:, 0], xy[:, 1] + my)
        return p + n * 0.003, n
    rest, normals = place(mouth_outline(**MOUTH_REST))
    idx = []
    for r in range(ROWS - 1):
        for c in range(COLS - 1):
            a, b = r * COLS + c, r * COLS + c + 1
            idx += [a, a + COLS, b, b, a + COLS, b + COLS]
    targets = list(MOUTHS)
    deltas = [place(mouth_outline(**MOUTHS[t]))[0] - rest for t in targets]
    return dict(pos=rest, nrm=normals, idx=np.array(idx, np.uint32), targets=deltas, material=3), targets

def cheeks_mesh(sdf, blush, rings=6, segments=28):
    """Two blush ovals in concentric rings, so they follow the body's curve; the target grows them."""
    bx, by = blush
    radius = np.concatenate([[0], np.repeat(np.linspace(1 / rings, 1, rings), segments)])
    angle = np.concatenate([[0], np.tile(np.linspace(0, 2 * math.pi, segments, endpoint=False), rings)])
    unit = np.stack([np.cos(angle) * radius * 0.075, np.sin(angle) * radius * 0.045], axis=1)
    disc = []
    for r in range(rings):
        for j in range(segments):
            k = (j + 1) % segments
            if r == 0:
                disc += [0, 1 + j, 1 + k]
            else:
                a, b = 1 + (r - 1) * segments + j, 1 + (r - 1) * segments + k
                c, d = a + segments, b + segments
                disc += [a, c, b, b, c, d]
    def place(scale):
        xy = np.concatenate([unit * scale + np.array([side * bx, by]) for side in (-1, 1)])
        p, n = front_point(sdf, xy[:, 0], xy[:, 1])
        return p + n * 0.0035, n
    rest, normals = place(1.0)
    grown, _ = place(1.35)
    idx = np.concatenate([np.array(disc), np.array(disc) + len(unit)]).astype(np.uint32)
    return dict(pos=rest, nrm=normals, idx=idx, targets=[grown - rest], material=4), ["blush"]

# ---------------------------------------------------------------- fur textures

def fur_textures(size=512, seed=7):
    """A tileable height field of short downward strands, as a grain texture and a normal map."""
    rng = np.random.default_rng(seed)
    noise = rng.standard_normal((size, size))
    fy = np.fft.fftfreq(size)[:, None]; fx = np.fft.fftfreq(size)[None, :]
    # Elongated along v (down the body): strands.
    fine = np.exp(-((fx / 0.16) ** 2 + (fy / 0.035) ** 2))
    coarse = np.exp(-((fx / 0.03) ** 2 + (fy / 0.02) ** 2))
    spectrum = np.fft.fft2(noise)
    h = np.real(np.fft.ifft2(spectrum * fine)) + 0.6 * np.real(np.fft.ifft2(spectrum * coarse))
    h = (h - h.mean()) / h.std()
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) / 2
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) / 2
    strength = 0.3
    n = np.stack([-dx * strength, dy * strength, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    normal = Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8))
    grain = np.clip(0.93 + 0.045 * h, 0, 1)
    color = Image.fromarray((np.stack([grain] * 3, axis=-1) * 255).astype(np.uint8))
    def png(img):
        out = io.BytesIO(); img.save(out, 'PNG', optimize=True); return out.getvalue()
    return png(color), png(normal)

# ---------------------------------------------------------------- glTF writer

def srgb_to_linear(hex_color):
    c = [int(hex_color[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return [round(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4, 5) for x in c]

class Glb:
    def __init__(self):
        self.bin = bytearray()
        self.gltf = {"asset": {"version": "2.0", "generator": "plush_avatars.py (Spine Avatar Lab)"},
                     "buffers": [{}], "bufferViews": [], "accessors": [], "meshes": [], "nodes": [],
                     "materials": [], "textures": [], "images": [], "samplers": [], "animations": [], "scenes": [{"nodes": [0]}], "scene": 0}

    def view(self, data, target=None):
        while len(self.bin) % 4: self.bin.append(0)
        v = {"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data)}
        if target: v["target"] = target
        self.bin += data
        self.gltf["bufferViews"].append(v)
        return len(self.gltf["bufferViews"]) - 1

    def accessor(self, array, kind, target=None, minmax=False):
        array = np.ascontiguousarray(array)
        ctype = 5125 if array.dtype == np.uint32 else 5126
        if ctype == 5126: array = array.astype(np.float32)
        count = array.size if kind == "SCALAR" else array.shape[0]
        a = {"bufferView": self.view(array.tobytes(), target), "componentType": ctype, "count": count, "type": kind}
        if minmax:
            a["min"] = [float(x) for x in array.reshape(count, -1).min(0)]
            a["max"] = [float(x) for x in array.reshape(count, -1).max(0)]
        self.gltf["accessors"].append(a)
        return len(self.gltf["accessors"]) - 1

    def image(self, png, name):
        self.gltf["images"].append({"name": name, "mimeType": "image/png", "bufferView": self.view(png)})
        return len(self.gltf["images"]) - 1

    def mesh(self, name, prims, target_names=()):
        out = []
        for p in prims:
            prim = {"attributes": {
                "POSITION": self.accessor(p['pos'], "VEC3", 34962, True),
                "NORMAL": self.accessor(p['nrm'], "VEC3", 34962)}, "indices": self.accessor(p['idx'], "SCALAR", 34963), "material": p['material']}
            if 'uv' in p: prim["attributes"]["TEXCOORD_0"] = self.accessor(p['uv'], "VEC2", 34962)
            if p.get('targets'):
                prim["targets"] = [{"POSITION": self.accessor(t, "VEC3", None, True)} for t in p['targets']]
            out.append(prim)
        mesh = {"name": name, "primitives": out}
        if target_names:
            mesh["weights"] = [0.0] * len(target_names)
            mesh["extras"] = {"targetNames": list(target_names)}
        self.gltf["meshes"].append(mesh)
        return len(self.gltf["meshes"]) - 1

    def node(self, name, mesh=None, t=None, children=()):
        n = {"name": name}
        if mesh is not None: n["mesh"] = mesh
        if t is not None: n["translation"] = [float(x) for x in t]
        if children: n["children"] = list(children)
        self.gltf["nodes"].append(n)
        return len(self.gltf["nodes"]) - 1

    def animation(self, name, channels):
        """channels: (node, path, times, values) with values as rows."""
        out = {"name": name, "samplers": [], "channels": []}
        for node, path, times, values in channels:
            kind = {"translation": "VEC3", "scale": "VEC3", "rotation": "VEC4"}[path]
            out["samplers"].append({"input": self.accessor(np.array(times, np.float32), "SCALAR", None, True),
                                    "output": self.accessor(np.array(values, np.float32), kind), "interpolation": "LINEAR"})
            out["channels"].append({"sampler": len(out["samplers"]) - 1, "target": {"node": node, "path": path}})
        self.gltf["animations"].append(out)

    def bytes(self):
        self.gltf["buffers"][0]["byteLength"] = len(self.bin)
        js = json.dumps(self.gltf, separators=(',', ':')).encode()
        js += b' ' * (-len(js) % 4)
        binary = bytes(self.bin) + b'\0' * (-len(self.bin) % 4)
        total = 12 + 8 + len(js) + 8 + len(binary)
        return (struct.pack('<III', 0x46546C67, 2, total) + struct.pack('<II', len(js), 0x4E4F534A) + js
                + struct.pack('<II', len(binary), 0x004E4942) + binary)

def quat(axis, angle):
    s = math.sin(angle / 2)
    return [axis[0] * s, axis[1] * s, axis[2] * s, math.cos(angle / 2)]

def wave(duration, peaks, steps=24):
    """Times and a 0..1..0 envelope with `peaks` swings, for looping clips that start and end at rest."""
    t = np.linspace(0, duration, steps + 1)
    return t, np.sin(np.pi * peaks * t / duration)

# ---------------------------------------------------------------- one avatar

def build(key, spec, fur):
    sdf = spec["sdf"]
    g = Glb()
    sampler = 0
    g.gltf["samplers"].append({"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497})
    grain = g.image(fur[0], "fur-grain"); normal = g.image(fur[1], "fur-normal")
    g.gltf["textures"] += [{"sampler": sampler, "source": grain}, {"sampler": sampler, "source": normal}]

    def fur_material(name, hex_color, sheen):
        return {"name": name, "pbrMetallicRoughness": {"baseColorFactor": srgb_to_linear(hex_color) + [1], "baseColorTexture": {"index": 0},
                "metallicFactor": 0, "roughnessFactor": 0.92}, "normalTexture": {"index": 1, "scale": 0.8},
                "extensions": {"KHR_materials_sheen": {"sheenColorFactor": srgb_to_linear(sheen), "sheenRoughnessFactor": 0.45}}}
    g.gltf["materials"] = [
        fur_material("fur", spec["color"], spec["sheen"]),
        {"name": "eye", "pbrMetallicRoughness": {"baseColorFactor": srgb_to_linear("#15131A") + [1], "metallicFactor": 0, "roughnessFactor": 0.14}},
        {"name": "shine", "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "metallicFactor": 0, "roughnessFactor": 0.4}, "emissiveFactor": [1, 1, 1]},
        {"name": "mouth", "pbrMetallicRoughness": {"baseColorFactor": srgb_to_linear("#3B1F26") + [1], "metallicFactor": 0, "roughnessFactor": 0.55}, "doubleSided": True},
        {"name": "blush", "pbrMetallicRoughness": {"baseColorFactor": srgb_to_linear("#FF8FA3") + [0.42], "metallicFactor": 0, "roughnessFactor": 0.9}, "alphaMode": "BLEND"},
        fur_material("accent", spec["accent"], "#FFFFFF"),
    ]
    g.gltf["extensionsUsed"] = ["KHR_materials_sheen"]

    body = body_mesh(sdf); body["material"] = 0
    eyes, eye_targets = eyes_mesh(sdf, spec["eye"])
    mouth, mouth_targets = mouth_mesh(sdf, spec["mouth"])
    cheeks, cheek_targets = cheeks_mesh(sdf, spec["blush"])
    m_body = g.mesh("Body", [body])
    m_eyes = g.mesh("Eyes", eyes, eye_targets)
    m_mouth = g.mesh("Mouth", [mouth], mouth_targets)
    m_cheeks = g.mesh("Cheeks", [cheeks], cheek_targets)

    # 0 Root, 1 Body (pivot at the feet), 2 BodyMesh, 3 Face, 4 Eyes, 5 Mouth, 6 Cheeks, then extras.
    feet = -0.5
    g.node("Root", children=[1])
    g.node("Body", t=(0, feet, 0), children=[2, 3])
    g.node("BodyMesh", m_body, t=(0, -feet, 0))
    g.node("Face", t=(0, -feet, 0), children=[4, 5, 6])
    g.node("Eyes", m_eyes)
    g.node("Mouth", m_mouth)
    g.node("Cheeks", m_cheeks)
    wiggle = []
    if "leaves" in spec["extras"]:
        tip = CENTRE + np.array([0, 1, 0]) * ray_surface(sdf, CENTRE, np.array([[0, 1.0, 0]]))[0]
        leaf = leaf_mesh(); leaf["material"] = 5
        m_leaf = g.mesh("Leaf", [leaf])
        for side in (-1, 1):
            i = g.node(f"Leaf{'L' if side < 0 else 'R'}", m_leaf, t=tip - np.array([0, feet + 0.03, 0]))
            g.gltf["nodes"][i]["rotation"] = quat((0, 0, 1), side * -0.75)
            g.gltf["nodes"][1]["children"].append(i)
            wiggle.append((i, side))
    if "antenna" in spec["extras"]:
        tip = CENTRE + np.array([0, 1, 0]) * ray_surface(sdf, CENTRE, np.array([[0, 1.0, 0]]))[0]
        stem = cylinder_mesh(0.012, 0.16); stem["material"] = 5
        ball = ellipsoid_mesh((0.05, 0.05, 0.05), 12, 16, centre=(0, 0.19, 0)); ball["material"] = 5
        m_ant = g.mesh("Antenna", [stem, ball])
        i = g.node("Antenna", m_ant, t=tip - np.array([0, feet + 0.03, 0]))
        g.gltf["nodes"][i]["rotation"] = quat((0, 0, 1), -0.12)
        g.gltf["nodes"][1]["children"].append(i)
        wiggle.append((i, 1))

    # Clips: node 1 is the body. They start and end at rest, so they add onto poses.
    def rot_clip(name, duration, axis, angle, peaks, extra=()):
        t, e = wave(duration, peaks)
        g.animation(name, [(1, "rotation", t, [quat(axis, angle * v) for v in e]), *extra])
    def accessory(duration, amount, peaks):
        out = []
        for node, side in wiggle:
            rest = g.gltf["nodes"][node].get("rotation", [0, 0, 0, 1])
            t, e = wave(duration, peaks)
            rows = []
            for v in e:
                q = quat((0, 0, 1), side * amount * v)
                x, y, z, w = rest; a, b, c, d = q
                rows.append([d * x + a * w + b * z - c * y, d * y - a * z + b * w + c * x, d * z + a * y - b * x + c * w, d * w - a * x - b * y - c * z])
            out.append((node, "rotation", t, rows))
        return out

    t, e = wave(4.0, 2)
    breath = [[1 - 0.012 * v, 1 + 0.02 * v, 1 - 0.012 * v] for v in np.abs(e)]
    g.animation("idle_a", [(1, "scale", t, breath), *accessory(4.0, 0.12, 2)])
    rot_clip("idle_b", 4.8, (0, 0, 1), 0.05, 2, accessory(4.8, 0.18, 4))
    t, e = wave(1.2, 4)
    g.animation("connect", [(1, "translation", t, [[0, feet + 0.05 * abs(v), 0] for v in e]), *accessory(1.2, 0.25, 4)])
    rot_clip("think", 3.0, (0, 0, 1), 0.09, 1, accessory(3.0, 0.1, 2))
    rot_clip("listen_enter", 0.8, (1, 0, 0), 0.1, 1)
    rot_clip("nod", 0.9, (1, 0, 0), 0.14, 4)
    rot_clip("shake", 0.9, (0, 1, 0), 0.22, 4)
    rot_clip("lean_in", 1.0, (1, 0, 0), 0.12, 1)
    t = np.linspace(0, 0.8, 33)
    air = np.where((t > 0.16) & (t < 0.6), np.sin(np.pi * np.clip((t - 0.16) / 0.44, 0, 1)), 0)
    crouch = np.exp(-((t - 0.1) / 0.05) ** 2) + np.exp(-((t - 0.66) / 0.05) ** 2)
    hop = [[0, feet + 0.16 * v, 0] for v in air]
    squash = [[1 + 0.06 * c - 0.03 * a, 1 - 0.09 * c + 0.05 * a, 1 + 0.06 * c - 0.03 * a] for c, a in zip(crouch, air)]
    g.animation("hop", [(1, "translation", t, hop), (1, "scale", t, squash), *accessory(0.8, 0.4, 2)])
    t, e = wave(0.45, 1)
    g.animation("interrupt", [(1, "scale", t, [[1 + 0.05 * v, 1 - 0.07 * v, 1 + 0.05 * v] for v in e])])
    rot_clip("wiggle", 1.0, (0, 0, 1), 0.12, 4, accessory(1.0, 0.3, 4))

    return g.bytes(), dict(body=1, eyes=4, mouth=5, cheeks=6, m_eyes=m_eyes, m_mouth=m_mouth, m_cheeks=m_cheeks,
                           eye_targets=eye_targets, mouth_targets=mouth_targets)

# ---------------------------------------------------------------- package

VISEMES = ["sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"]
VISEME_MOUTHS = {
    "sil": {}, "PP": {"press": 1}, "FF": {"teeth": 1}, "TH": {"teeth": 0.7, "mid": 0.3}, "DD": {"mid": 1},
    "kk": {"mid": 0.85}, "CH": {"ch": 1}, "SS": {"ch": 0.8}, "nn": {"mid": 0.6}, "RR": {"round": 0.5, "mid": 0.3},
    "aa": {"open": 1}, "E": {"wide": 1}, "I": {"wide": 0.8}, "O": {"round": 1}, "U": {"pucker": 1},
}

def package(key, spec, glb, ids, posters):
    def eye(name, v): return {"node": ids["eyes"], "mesh": ids["m_eyes"], "primitives": [0, 1], "targetIndex": ids["eye_targets"].index(name), "value": v}
    def mouth(name, v): return {"node": ids["mouth"], "mesh": ids["m_mouth"], "primitives": [0], "targetIndex": ids["mouth_targets"].index(name), "value": v}
    def blush(v): return {"node": ids["cheeks"], "mesh": ids["m_cheeks"], "primitives": [0], "targetIndex": 0, "value": v}
    def tilt(axis, angle): return {"node": ids["body"], "property": "rotation", "value": [round(x, 6) for x in quat(axis, angle)]}

    poses = {
        "idle": [], "connecting": [], "speaking": [], "reduced_motion": [],
        "listening": [eye("wide", 0.18), tilt((1, 0, 0), 0.05)],
        "thinking": [eye("squint", 0.35), mouth("side", 0.6), tilt((0, 0, 1), 0.06)],
        "interrupted": [eye("wide", 0.6), mouth("round", 0.4)],
        "muted": [mouth("small", 0.8), eye("squint", 0.2)],
        "expr_neutral": [],
        "expr_happy": [eye("happy", 1), mouth("open_smile", 1), blush(1)],
        "expr_curious": [eye("wide", 0.35), mouth("small", 0.6), tilt((0, 0, 1), 0.12)],
        "expr_thinking": [eye("squint", 0.4), mouth("side", 0.8), tilt((0, 0, 1), -0.08)],
        "expr_concerned": [eye("wide", 0.15), mouth("frown", 0.9)],
        "expr_surprised": [eye("wide", 1), mouth("round", 0.9)],
        "expr_apologetic": [eye("squint", 0.3), mouth("frown", 0.5), tilt((1, 0, 0), 0.1)],
        "expr_confident": [eye("squint", 0.25), mouth("smile", 1), blush(0.5)],
    }
    for v in VISEMES:
        poses["viseme_" + v] = [mouth(name, w) for name, w in VISEME_MOUTHS[v].items()]

    bindings = {
        "schemaVersion": "1.0", "renderer": "native3d",
        "animations": {n: n for n in ("idle_a", "idle_b", "connect", "think", "listen_enter", "nod", "shake", "lean_in", "hop", "interrupt", "wiggle")},
        "poses": poses,
        "parameters": {
            "outputLevel": [{"node": ids["body"], "property": "scale", "min": [1, 1, 1], "max": [1.025, 1.045, 1.025]}],
            "inputLevel": [{"node": ids["eyes"], "property": "scale", "min": [1, 1, 1], "max": [1.06, 1.06, 1.06]}],
        },
        "channelMasks": {"expression": ["eyes", "mouthCorners", "body"], "speech": ["mouthShape"], "idle": ["body"], "reflex": ["eyes", "body"]},
        "framing": {"cameraPosition": [0, 0.02, 3.3], "lookAt": [0, -0.02, 0], "verticalFov": 30, "safeInset": 0.06, "fit": "contain"},
        "gaze": {"node": ids["eyes"], "rangeMeters": 0.02},
        "blink": {"node": ids["eyes"], "mesh": ids["m_eyes"], "targetIndex": ids["eye_targets"].index("blink"),
                  "suppressExpressionTargets": [ids["eye_targets"].index(n) for n in ("happy", "wide", "squint")], "expressionScaleRule": "oneMinusBlink"},
        "stateAnimations": {"connecting": "connect", "thinking": "think"},
        "headNode": ids["body"], "eyesNode": ids["eyes"], "mouthNode": ids["mouth"],
        "speechMouthMesh": ids["m_mouth"],
        "expressionMouthTargets": [ids["mouth_targets"].index(n) for n in ("smile", "open_smile", "frown", "small", "side")],
    }
    channels = ["eyes", "mouthCorners", "body"]
    manifest = {
        "schemaVersion": "1.0", "id": f"plush-{key}", "displayName": spec["name"], "assetVersion": "1.0.0",
        "minRuntimeVersion": "1.0.0", "profile": "character",
        "representations": [{
            "id": "native3d-primary", "renderer": "native3d", "format": "glb",
            "model": f"models/plush-{key}.glb", "bindings": "bindings/native3d.json",
            "capabilities": ["ambient", "expressions", "audioReactive", "gestures", "reducedMotion", "themeSlots", "speechArticulation", "gaze", "blink"],
            "platforms": ["android", "ios", "maccatalyst", "windows"],
            "idleOwner": "scheduler", "blinkOwner": "scheduler", "gazeOwner": "scheduler",
        }],
        "states": {s: {"pose": s, "transitionMs": 90 if s == "interrupted" else 200, "inputReactive": s == "listening",
                       "outputReactive": s == "speaking", "gaze": "processing" if s == "thinking" else "engaged"}
                   for s in ("idle", "connecting", "listening", "thinking", "speaking", "interrupted", "muted")},
        "expressions": {e: {"pose": "expr_" + e, "channels": channels, "transitionMs": 100 if e == "surprised" else 220}
                        for e in ("neutral", "happy", "curious", "thinking", "concerned", "surprised", "apologetic", "confident")},
        "speech": {"mode": "canonicalVisemes", "canonicalProfile": "oculus15-v1",
                   "mapping": {str(i): "viseme_" + v for i, v in enumerate(VISEMES)}, "expressionMouthScale": 0.45, "releaseMs": 80},
        "motions": {"idleVariants": ["idle_a", "idle_b"],
                    "gestures": {"nod": "nod", "shake": "shake", "lean": "lean_in", "hop": "hop", "wiggle": "wiggle", "interrupt": "interrupt"},
                    "seedable": True, "blinkIntervalSeconds": [2.6, 6.0], "breathPeriodSeconds": [3.6, 5.2]},
        "themes": {"slots": {
            "body": {"light": spec["color"], "dark": spec["dark"], "bindings": ["material:0"]},
            "accent": {"light": spec["accent"], "dark": spec["accent"], "bindings": ["material:5"]},
        }},
        "reducedMotion": {"pose": "reduced_motion", "retainSpeech": True, "transitionMs": 180},
        "posters": {"light": "previews/poster-light.png", "dark": "previews/poster-dark.png"},
        "license": "LICENSE.txt", "provenance": "provenance.json",
    }
    poster = lambda theme: open(f"{posters}/plush-{key}-{theme}.png", 'rb').read() if posters else tiny_png()
    files = {
        f"models/plush-{key}.glb": glb,
        "bindings/native3d.json": json.dumps(bindings, indent=1).encode(),
        "previews/poster-light.png": poster("light"),
        "previews/poster-dark.png": poster("dark"),
        "LICENSE.txt": b"Generated for the Spine Avatar Lab by source/plush_avatars.py. Same licence as the Maui.Spine repository.\n",
        "provenance.json": json.dumps({
            "model": "procedural: smooth-union ellipsoid body, fur normal map, dot eyes, band mouth (source/plush_avatars.py)",
            "posters": "captured from the lab's three.js surface" if posters else "placeholder 1x1, not a render",
        }, indent=1).encode(),
        "source/plush_avatars.py": open(__file__, 'rb').read(),
    }
    manifest["files"] = [{"path": p, "sha256": hashlib.sha256(b).hexdigest(), "bytes": len(b)} for p, b in sorted(files.items())]
    out = io.BytesIO()
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr("avatar.json", json.dumps(manifest, indent=1))
        for p, b in sorted(files.items()):
            z.writestr(p, b)
    return out.getvalue()

def tiny_png():
    img = Image.new('RGBA', (1, 1)); out = io.BytesIO(); img.save(out, 'PNG'); return out.getvalue()

if __name__ == "__main__":
    args = sys.argv[1:]
    posters = None
    if "--posters" in args:
        i = args.index("--posters"); posters = args[i + 1]; del args[i:i + 2]
    out_dir, names = args[0], args[1:] or list(CHARACTERS)
    fur = fur_textures()
    for key in names:
        glb, ids = build(key, CHARACTERS[key], fur)
        data = package(key, CHARACTERS[key], glb, ids, posters)
        path = os.path.join(out_dir, f"plush-{key}.spineavatar")
        open(path, 'wb').write(data)
        print(path, len(data), "bytes")
