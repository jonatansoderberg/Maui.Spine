"""Builds Kairo from Pixal3D scans of the owner's pictures, for the Spine Avatar Lab (#524).

Usage: python kairo_scan.py <work-dir> <out-dir> [poster-light.png poster-dark.png]

<work-dir> holds what the generator produced:
  kairo-front.png                       the owner's front photo (1254 px)
  kairo-s42.glb, kairo-s42_base.png     Pixal3D single-view run of the photo (pixal3d.cpp, --webp off)
  kairo-s42.svviews/input.png           its cropped, matted input (camera space)
  bald/kairo-bald-front.png             the owner's bald picture, same framing
  bald/kairo-bald-s42.glb (+ _base.png, .svviews)   Pixal3D run of the bald picture
  assets-v2/kairo-eyes-closed-front.png the owner's eyes-closed picture, same framing
Needs numpy, scipy, Pillow, trimesh, pymeshlab and scikit-image.

Pipeline:
 1. Weld, turn to face +Z and decimate both scans; keep only their outer surfaces. The bald run is
    moved into the photo's crop (the cutter centres each subject, so the crops differ by 51 px).
 2. Compose: skull, ears, neck and turtleneck from the bald scan; only the hair from the haired one.
    The neck inside the collar and the turtleneck stay put when the head turns.
 3. The face is one smooth height field textured with the photo, up to the hairline, with holes at
    the eye openings: an eyeball (the photo with the irises painted out) and a round iris disc sit
    behind them, and gaze moves the disc. Camera-facing head faces show the photo, the collar the bald
    picture; the rest get per-triangle atlases from the generators' textures, colour-matched and
    darkened by baked ambient occlusion.
 4. Morph targets as displacement fields drawn in image space and lifted back onto the surface:
    lids (their own surface, textured with the eyes-closed picture), brows and mouth shapes for
    expressions and the 15 visemes. Written as sparse accessors.
 5. The head turns about the neck's axis at the collar.
"""
import hashlib, io, json, math, os, sys, zipfile
import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.interpolate import LinearNDInterpolator
from scipy.spatial import cKDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from plush_avatars import quat, wave, tiny_png, srgb_to_linear

# ---------------------------------------------------------------- the camera

D, T, N = 2.83564091, math.tan(0.34906584 / 2), 1020   # pixal3d.cpp single-view camera, crop size
S, OX, OY = 0.751, 45.0, 31.0                           # source px -> crop px (fitted, mean error 1.5)
BALD_OY = -20.0     # the bald run's crop: the cutter centres the subject, and without hair it sits higher

def reframe(p, dy):
    """Points moved down the crop by dy px at their own depth: from one run's crop into another's."""
    px, zc = project(p); px[:, 1] += dy
    return unproject(px, zc)

def project(p):
    """3D (facing +Z, camera at +D) -> crop px, and distance along the view axis."""
    zc = D - p[:, 2]
    return np.stack([(p[:, 0] / zc / T * 0.5 + 0.5) * N, (0.5 - p[:, 1] / zc / T * 0.5) * N], axis=1), zc

def unproject(px, zc):
    x = (px[:, 0] / N - 0.5) * 2 * zc * T
    y = (0.5 - px[:, 1] / N) * 2 * zc * T
    return np.stack([x, y, D - zc], axis=1)

def src_to_crop(xy):
    return np.asarray(xy, float) * S + np.array([OX, OY])

# ---------------------------------------------------------------- landmarks (source image px)

EYES = [dict(cx=507.5, cy=522.0, half=72.0, upper=487.0, lower=552.0, crease=468.0),
        dict(cx=747.5, cy=520.0, half=72.0, upper=485.0, lower=551.0, crease=466.0)]
IRIS_R = 34.0
IRISES = [(507.5, 520.5), (747.5, 518.5)]   # iris centres (source px), from the unoccluded left, right and lower edges
LOOK = {'look_left': (-10.0, 0.0), 'look_right': (10.0, 0.0), 'look_up': (0.0, -6.0), 'look_down': (0.0, 5.0)}   # source px; further, the iris ran under the corners
# Lid margins measured on the source (x, y): the upper lid's edge where lashes meet the white, and
# the lower lid. The lash band sits above the upper margin; the lid fold above that.
LIDS = [dict(ux=[435, 460, 485, 510, 535, 552, 562], uy=[520, 495, 486, 484, 489, 500, 540],
             lx=[435, 460, 490, 520, 550, 562], ly=[522, 540, 550, 553, 550, 542]),
        dict(ux=[698, 715, 740, 765, 790, 815, 828], uy=[542, 505, 489, 484, 486, 497, 515],
             lx=[698, 720, 750, 780, 810, 828], ly=[546, 552, 554, 550, 538, 518])]
LASH, FOLD = 14.0, 18.0     # source px above the lid margin: the photo's upper lash tips; the lid crease (measured)
SRC_W = 1254.0
BROWS = [dict(x0=470.0, x1=600.0, y=425.0, inner=600.0), dict(x0=655.0, x1=830.0, y=423.0, inner=655.0)]
MOUTH = dict(cx=626.0, y=743.0, left=550.0, right=702.0, top=712.0, bottom=782.0)
COLLAR_Y = 868.0
CHIN_Y = 864.0              # source y of the chin's lower edge; the jaw line rises from it            # source y where the neck enters the turtleneck, at the centre
FEATURES = [(507.5, 510.0, 105.0, 80.0), (747.5, 508.0, 105.0, 80.0), (626.0, 748.0, 120.0, 75.0)]

# ---------------------------------------------------------------- stage 1: weld, turn, decimate

def load_scan(path, faces=110000):
    import trimesh, pymeshlab
    g = list(trimesh.load(path).geometry.values())[0]
    v = np.asarray(g.vertices, float); f = np.asarray(g.faces, np.int32); uv = np.asarray(g.visual.uv, float)
    turn = np.array([-1.0, 1.0, -1.0])                  # the generator writes -Z forward; a rotation, not a mirror
    orig = (v * turn, uv)
    _, weld = np.unique(np.round(v * 1e6), axis=0, return_inverse=True); weld = weld.reshape(-1)
    first = np.zeros(weld.max() + 1, int); first[weld[::-1]] = np.arange(len(v))[::-1]
    P, F = v[first] * turn, weld[f].astype(np.int32)
    # Keep the face features dense: only faces outside them may be simplified.
    px, _ = project(P[F].mean(1))
    keep = np.zeros(len(F), bool)
    for x, y, rx, ry in FEATURES:
        c = src_to_crop((x, y)); r = np.array([rx, ry]) * S
        keep |= (((px - c) / r) ** 2).sum(1) < 1.0
    keep &= P[F].mean(1)[:, 2] > 0
    ms = pymeshlab.MeshSet()
    ms.add_mesh(pymeshlab.Mesh(vertex_matrix=P, face_matrix=F, f_scalar_array=(~keep).astype(float)))
    ms.meshing_remove_duplicate_faces(); ms.meshing_remove_unreferenced_vertices()
    ms.compute_selection_by_condition_per_face(condselect='fq > 0.5')
    ms.meshing_decimation_quadric_edge_collapse(targetfacenum=faces, qualitythr=0.4, preservenormal=True,
                                                optimalplacement=True, planarquadric=True, selected=True)
    m = ms.current_mesh()
    print('decimated', m.face_number(), 'faces; features kept', keep.sum())
    return m.vertex_matrix(), m.face_matrix(), orig

def compact(P, F):
    used = np.unique(F); remap = np.full(len(P), -1); remap[used] = np.arange(len(used))
    return P[used], remap[F].astype(np.int32)

def front_zbuffer(P, F, cell=4):
    """The nearest depth per image cell. Point samples leave most full-resolution pixels empty, and
    an empty pixel let the back of the head count as visible; a coarser buffer has no holes."""
    rng = np.random.default_rng(2)
    pts = np.concatenate([np.einsum('k,fkd->fd', b, P[F]) for b in rng.dirichlet((1, 1, 1), size=6)] + [P])
    sp, sz = project(pts); ix = np.clip((sp / cell).astype(int), 0, N // cell - 1)
    zb = np.full((N // cell, N // cell), np.inf); np.minimum.at(zb, (ix[:, 1], ix[:, 0]), sz)
    def at(px):
        i = np.clip((px / cell).astype(int), 0, N // cell - 1)
        return zb[i[:, 1], i[:, 0]]
    full = np.repeat(np.repeat(zb, cell, 0), cell, 1)
    return at, full

# ---------------------------------------------------------------- stage 2: outer surface only

def seen_from_outside(P, F, R=640, n=96, share=0.5):
    """How many of n directions round the model see each face (orthographic, a z-buffer per direction),
    and the sum of those directions."""
    rng = np.random.default_rng(1)
    bary = np.concatenate([np.eye(3), rng.dirichlet((1, 1, 1), size=9), [[1 / 3, 1 / 3, 1 / 3]]])
    pts = np.einsum('bk,fkd->fbd', bary, P[F]); nf, nb = pts.shape[:2]; flat = pts.reshape(-1, 3)
    k = np.arange(n) + 0.5
    phi = np.arccos(1 - 2 * k / n); th = math.pi * (1 + 5 ** 0.5) * k
    dirs = np.stack([np.sin(phi) * np.cos(th), np.cos(phi), np.sin(phi) * np.sin(th)], 1)
    ext = np.linalg.norm(P, axis=1).max() * 1.02
    seen = np.zeros(nf, int); dir_sum = np.zeros((nf, 3))
    for d in dirs:
        up = np.array([0, 1, 0]) if abs(d[1]) < 0.9 else np.array([1, 0, 0])
        x = np.cross(up, d); x /= np.linalg.norm(x); y = np.cross(d, x)
        u = ((flat @ x / ext * 0.5 + 0.5) * (R - 1)).astype(int); v = ((flat @ y / ext * 0.5 + 0.5) * (R - 1)).astype(int)
        depth = -(flat @ d)
        zb = np.full((R, R), np.inf); np.minimum.at(zb, (v, u), depth)
        vis = (depth <= zb[v, u] + 0.004).reshape(nf, nb).mean(1) > share
        seen += vis; dir_sum[vis] += d
    return seen, dir_sum

def face_toward(P, F, dir_sum):
    fn = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]])
    flip = (fn * dir_sum).sum(1) < 0
    F = F.copy(); F[flip] = F[flip][:, ::-1]
    return F

def outer_surface(P, F):
    seen, dir_sum = seen_from_outside(P, F)
    F2 = face_toward(P, F, dir_sum)[seen > 0]
    print('outer faces', len(F2), 'of', len(F))
    return compact(P, F2)

def eye_mask(scale=1.0):
    """Crop-space mask over both eyes and their sockets."""
    yy, xx = np.mgrid[0:N, 0:N]
    m = np.zeros((N, N), bool)
    for lid in LIDS:
        cx = (lid['ux'][0] + lid['ux'][-1]) / 2; cy = (min(lid['uy']) + max(lid['ly'])) / 2
        c = src_to_crop((cx, cy)); rx = (lid['ux'][-1] - lid['ux'][0]) / 2 * S * 1.25 * scale; ry = 62 * S * scale
        m |= ((xx - c[0]) / rx) ** 2 + ((yy - c[1]) / ry) ** 2 < 1
    return m

def face_mask_src(src):
    """The skin of the face in source px: an oval from brow to chin and cheek to cheek, minus hair
    (blue) and anything outside the cutout."""
    a = np.asarray(src).astype(float)
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    # Down to the jaw line only: below it the scan's own neck carries on into the collar.
    jaw = CHIN_Y - 0.0042 * (xx - 626.0) ** 2
    # Up to the hairline: ending on the bare forehead, the grid's edge showed as a broken arc there.
    up = yy < 640
    oval = (((xx - 626) / np.where(up, 235, 205)) ** 2 + ((yy - 640) / np.where(up, 390, 268)) ** 2 < 1) & (yy < jaw)
    hair = (a[..., 2] > a[..., 0] + 12) | (a.sum(-1) < 150)
    # Strands thinner than ~10 px stay on the face: cut out, they left a crack across the forehead.
    hair = ndimage.binary_dilation(ndimage.binary_opening(hair, iterations=5), iterations=4)
    m = oval & ~hair
    # Keep the eyes, brows and mouth whole even where they read as dark or blue.
    for x, y, rx, ry in FEATURES:
        m |= ((xx - x) / rx) ** 2 + ((yy - y) / ry) ** 2 < 1
    m &= oval
    m = ndimage.binary_opening(ndimage.binary_fill_holes(m), iterations=3)
    labels, n = ndimage.label(m)
    if n > 1:
        sizes = ndimage.sum(m, labels, range(1, n + 1)); m = labels == (1 + int(np.argmax(sizes)))
    return m

def face_mask_crop(src):
    yy, xx = np.mgrid[0:N, 0:N]
    mask = face_mask_src(src)
    return mask[((yy - OY) / S).astype(int).clip(0, mask.shape[0] - 1), ((xx - OX) / S).astype(int).clip(0, mask.shape[1] - 1)]

def eye_openings(shape, inset=0.0):
    """The eye openings in source px: between the upper lid's margin and the lower lid, `inset` px
    in from both (negative grows them)."""
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]].astype(float)
    m = np.zeros(shape, bool)
    for lid in LIDS:
        x0, x1 = min(lid['ux'][0], lid['lx'][0]) + inset, max(lid['ux'][-1], lid['lx'][-1]) - inset
        up = np.interp(xx, lid['ux'], lid['uy']); lo = np.interp(xx, lid['lx'], lid['ly'])
        m |= (xx > x0) & (xx < x1) & (yy > up + inset) & (yy < lo - inset)
    return m

def eye_holes(shape, inset=0.0, grow_x=0.0):
    """The face grid's holes at the eyes: the opening and the upper lid above it up to the crease.
    The upper lid is its own surface (lid_overlays), so the whole lid moves when the eye closes."""
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]].astype(float)
    m = np.zeros(shape, bool)
    for lid in LIDS:
        x0, x1 = lid_span(lid); x0, x1 = x0 - grow_x, x1 + grow_x
        top = np.interp(xx, lid['ux'], lid['uy']) + 0.5           # the lid margin: the lashes stay on the face
        # Down past the lower lid's line: a grid edge on it kept a sliver of the photo's iris, which
        # stayed behind when the iris moved.
        bottom = np.interp(xx, lid['lx'], lid['ly']) + 1.5
        m |= (xx > x0) & (xx < x1) & (yy > top + inset) & (yy < bottom - inset)
    return m

def src_mask_to_crop(mask):
    yy, xx = np.mgrid[0:N, 0:N]
    return mask[((yy - OY) / S).astype(int).clip(0, mask.shape[0] - 1), ((xx - OX) / S).astype(int).clip(0, mask.shape[1] - 1)]

def sclera_photo(src):
    """The photo with the irises painted out of the openings: the eyeball behind the lids shows this,
    and a round iris moves over it. Each pixel takes the white just outside the iris at its own angle
    (or, where that falls on a lid, the white beside it on its row); nothing outside the iris is
    touched or smoothed (a wider, diffused fill blurred the white's detail round the iris)."""
    a = np.asarray(src).astype(float); out = a.copy()
    h, w = a.shape[:2]
    opening = eye_openings((h, w), -1.0)
    for cx, cy in IRISES:
        R = IRIS_R + 3.5                     # past the dark limbus, which otherwise stayed as a ring
        y0, y1, x0, x1 = int(cy - R) - 1, int(cy + R) + 2, int(cx - R) - 8, int(cx + R) + 9
        yy, xx = np.mgrid[y0:y1, x0:x1].astype(float)
        r = np.hypot(xx - cx, yy - cy)
        inside = opening[y0:y1, x0:x1]
        fill, white = (r <= R) & inside, (r > R + 1.5) & inside
        th = np.arctan2(yy - cy, xx - cx)
        sx, sy = cx + (R + 3) * np.cos(th), cy + (R + 3) * np.sin(th)
        radial_ok = opening[np.clip(np.round(sy).astype(int), 0, h - 1), np.clip(np.round(sx).astype(int), 0, w - 1)]
        radial = np.stack([ndimage.map_coordinates(a[..., c], [sy, sx], order=1) for c in range(3)], -1)
        row, have = np.zeros_like(radial), np.zeros(fill.shape[0], bool)
        for j in range(fill.shape[0]):
            cols = np.where(white[j])[0]
            left, right = cols[cols < cx - x0], cols[cols > cx - x0]
            if len(left) and len(right):
                l, rr = left.max(), right.min()
                t = np.clip((np.arange(fill.shape[1]) - l) / max(rr - l, 1), 0, 1)[:, None]
                row[j] = a[y0 + j, x0 + l] * (1 - t) + a[y0 + j, x0 + rr] * t
                have[j] = True
        good = np.where(have)[0]
        for j in np.where(~have)[0]:
            row[j] = row[good[np.argmin(np.abs(good - j))]]
        out[y0:y1, x0:x1] = np.where(fill[..., None], np.where(radial_ok[..., None], radial, row), out[y0:y1, x0:x1])
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))

IRIS_TILE, IRIS_SPAN = 192, 38.0            # one eye per tile; the tile covers +-IRIS_SPAN source px

def iris_texture(src):
    """Both irises, completed: the photo's upper lid hides the top of each, which is filled from the
    mirror image below (an iris is near enough symmetric). The rim fades into the white."""
    a = np.asarray(src).astype(float)
    tex = np.zeros((IRIS_TILE, IRIS_TILE * 2, 4))
    t = (np.arange(IRIS_TILE) + 0.5 - IRIS_TILE / 2) * IRIS_SPAN / (IRIS_TILE / 2)
    dx, dy = np.meshgrid(t, t)
    for i, ((cx, cy), lid) in enumerate(zip(IRISES, LIDS)):
        x, y = cx + dx, cy + dy
        hidden = y < np.interp(x, lid['ux'], lid['uy']) + 3.0
        sy = np.where(hidden, cy - dy, y)
        # The filled top sits in the lid's shadow: mirrored as it is, it showed as a pale cap.
        shade = 0.8 + 0.2 * smooth(-IRIS_R, -IRIS_R * 0.4, dy)
        for ch in range(3):
            tex[:, i * IRIS_TILE:(i + 1) * IRIS_TILE, ch] = ndimage.map_coordinates(a[..., ch], [sy, x], order=1) * shade
        r = np.hypot(dx, dy)
        tex[:, i * IRIS_TILE:(i + 1) * IRIS_TILE, 3] = 255 * (1 - smooth(IRIS_R - 1.5, IRIS_R + 1.5, r))
    return Image.fromarray(np.clip(tex, 0, 255).astype(np.uint8), 'RGBA')

def eye_parts(zs, surface_normal):
    """An eyeball behind each opening (the sclera photo at the face's depth, 5 mm behind the face
    grid) and a round iris disc just in front of it. Gaze moves the disc: the lids cover it as real
    lids do, so the iris stays round (moving the photo's own iris under the lids squeezed it)."""
    def at_depth(src_px, dz):
        c = src_to_crop(src_px)
        return unproject(c, ndimage.map_coordinates(zs, [c[:, 1], c[:, 0]], order=1) + dz)
    out = []
    # Eyeball: a grid over each opening, grown well under the lids.
    # Wide, sideways too: ending at the hole's sides, it let the room show through as a white line
    # across the iris when the eyes looked toward a corner.
    ball = eye_holes((int(SRC_W), int(SRC_W)), -22.0, 22.0)
    ys, xs = np.mgrid[0:int(SRC_W):2, 0:int(SRC_W):2]
    inside = ball[ys, xs]
    vid = np.full(xs.shape, -1); vid[inside] = np.arange(inside.sum())
    a, b, c, d = vid[:-1, :-1], vid[:-1, 1:], vid[1:, :-1], vid[1:, 1:]
    ok = (a >= 0) & (b >= 0) & (c >= 0) & (d >= 0)
    faces = np.concatenate([np.stack([a[ok], c[ok], b[ok]], 1), np.stack([b[ok], c[ok], d[ok]], 1)]).reshape(-1)
    spx = np.stack([xs[inside], ys[inside]], 1).astype(float)
    pos = at_depth(spx, 0.003)          # 3 mm behind the iris: closer, it covered the iris on a turn
    out.append(dict(pos=pos, nrm=surface_normal(pos), uv=spx / SRC_W, idx=faces.astype(np.uint32), material=0,
                    targets=[np.zeros_like(pos) for _ in TARGETS]))
    # Iris discs.
    rings, segs = 10, 64
    pts, uv, idx, base = [], [], [], 0
    for i, (cx, cy) in enumerate(IRISES):
        r = np.linspace(0, IRIS_R + 2, rings + 1)[1:]; th = np.linspace(0, 2 * math.pi, segs, endpoint=False)
        ring = np.stack([cx + np.outer(r, np.cos(th)).ravel(), cy + np.outer(r, np.sin(th)).ravel()], 1)
        p = np.concatenate([[[cx, cy]], ring])
        pts.append(p)
        uv.append(np.stack([(i * IRIS_TILE + IRIS_TILE / 2 + (p[:, 0] - cx) * (IRIS_TILE / 2) / IRIS_SPAN) / (2 * IRIS_TILE),
                            (IRIS_TILE / 2 + (p[:, 1] - cy) * (IRIS_TILE / 2) / IRIS_SPAN) / IRIS_TILE], 1))
        for s_ in range(segs):
            idx += [base, base + 1 + (s_ + 1) % segs, base + 1 + s_]
        for k in range(rings - 1):
            for s_ in range(segs):
                a0 = base + 1 + k * segs + s_; a1 = base + 1 + k * segs + (s_ + 1) % segs
                idx += [a0, a1 + segs, a0 + segs, a0, a1, a1 + segs]
        base += len(p)
    spx = np.concatenate(pts); pos = at_depth(spx, 0.0)
    _, zc = project(pos)
    targets = []
    for name in TARGETS:
        if name in LOOK:
            targets.append(unproject(src_to_crop(spx + np.array(LOOK[name])), zc) - pos)
        else:
            targets.append(np.zeros_like(pos))
    out.append(dict(pos=pos, nrm=surface_normal(pos), uv=np.concatenate(uv), idx=np.array(idx, np.uint32), material=5, targets=targets))
    return out

SURFACE_SMOOTHING = 2.0
DROP_BEHIND = 0.025         # scan faces this far behind the face grid go; deeper ones (under the jaw) stay
GRID_NORMALS = []
FACE_INFO = []

def face_surface(P, F, src, hair=None):
    """Replace the scan's face with one dense, smooth height field over the face, at the scan's own
    depth (smoothed, with the eye sockets filled). The generator's eyes are hollows and its face is
    crumpled; the picture is the face, so a smooth surface under it reads true, lids slide cleanly
    over it, and no seams are left round the eyes. Hair and the sides keep the scan."""
    inp, zc = project(P)
    # The front-most depth of the scan, densely sampled over its triangles.
    rng = np.random.default_rng(4)
    bary = rng.dirichlet((1, 1, 1), size=24)
    zraw = np.full((N, N), np.inf)
    for chunk in np.array_split(np.arange(len(F)), 8):
        pts = np.einsum('bk,fkd->fbd', bary, P[F[chunk]]).reshape(-1, 3)
        sp, sz = project(pts); ix = np.clip(sp.astype(int), 0, N - 1)
        np.minimum.at(zraw, (ix[:, 1], ix[:, 0]), sz)
    yy, xx = np.mgrid[0:N, 0:N]
    face = face_mask_crop(src)
    # Trusted depth: on the face, outside the eye sockets, and not an outlier against its neighbourhood
    # (hair strands in front, holes behind). Everything else is filled smoothly from it, so nothing
    # outside the face (bangs, the back of the head) pulls on the surface.
    finite = np.isfinite(zraw)
    # Where the scan's front has holes, the nearest surface is the back of the head, 20-30 cm behind:
    # only depths near the front-most one in the neighbourhood count.
    front_env = ndimage.minimum_filter(np.where(finite, zraw, 9.0), size=21)
    med = ndimage.median_filter(np.where(finite, zraw, 9.0), size=15)
    trusted = face & finite & ~eye_mask(1.05) & (zraw - front_env < 0.02) & (np.abs(zraw - med) < 0.006)
    trusted &= zraw < np.median(zraw[trusted]) + 0.08
    # One smooth surface through the trusted depth (a smoothing thin-plate spline): stitching measured
    # and filled regions together left creases where they met.
    from scipy.interpolate import RBFInterpolator
    ty, tx = np.where(trusted[::6, ::6]); ty, tx = ty * 6, tx * 6
    rbf = RBFInterpolator(np.stack([tx, ty], 1).astype(float), zraw[ty, tx], kernel='thin_plate_spline', smoothing=SURFACE_SMOOTHING)
    region = ndimage.binary_dilation(face, iterations=12)
    ry, rx = np.where(region)
    zs = np.full((N, N), np.median(zraw[trusted]))
    zs[ry, rx] = rbf(np.stack([rx, ry], 1).astype(float))
    print('face surface residual (mm)', round(float(np.std(zs[ty, tx] - zraw[ty, tx]) * 1000), 2))
    for lid in LIDS:
        cx = (lid['ux'][0] + lid['ux'][-1]) / 2; cy = (min(lid['uy']) + max(lid['ly'])) / 2
        c = src_to_crop((cx, cy)); rx = (lid['ux'][-1] - lid['ux'][0]) / 2 * S; ry = 34 * S
        zs -= 0.006 * np.clip(1 - ((xx - c[0]) / (rx * 1.3)) ** 2 - ((yy - c[1]) / (ry * 1.5)) ** 2, 0, None) ** 2
    # Drop the scan's front faces over the face (not the back of the head, which projects there too).
    # Any face with a corner over the face region and near its depth goes: scan faces left behind the
    # grid poked through it (the scan is noisier than the smoothed surface) as brown patches.
    vp, vz = project(P); vi2 = np.clip(vp.astype(int), 0, N - 1)
    # Only well inside the grid's edge, so the scan overlaps the edge instead of leaving a gap.
    inner = ndimage.binary_erosion(face, iterations=4)
    near_face = inner[vi2[:, 1], vi2[:, 0]] & (vz < zs[vi2[:, 1], vi2[:, 0]] + DROP_BEHIND)
    # All corners inside: dropping a face with one corner inside left a hole past the grid's edge
    # (the bald scan's faces are larger than the margin), and the hair behind showed through as specks.
    over = near_face[F].all(1)
    if hair is not None:
        # Hair over the face is the picture's own (thin strands stay in it): scan strands left at the
        # grid's edge poked through it as dark specks.
        wide = ndimage.binary_dilation(face, iterations=3)
        over |= hair & wide[vi2[:, 1], vi2[:, 0]][F].any(1) & (vz < zs[vi2[:, 1], vi2[:, 0]] + 0.01)[F].any(1)
    F = F[~over]
    # The grid, a hair in front of where the scan was.
    step = 1.6
    gx = np.arange(0, N, step); gy = np.arange(0, N, step)
    GX, GY = np.meshgrid(gx, gy)
    # The eye openings are holes: an eyeball and a round iris sit behind them (eye_parts).
    ins = face[GY.astype(int), GX.astype(int)] & ~src_mask_to_crop(eye_holes((int(SRC_W), int(SRC_W)), 1.0))[GY.astype(int), GX.astype(int)]
    vid = np.full(GX.shape, -1); vid[ins] = np.arange(ins.sum()) + len(P)
    px = np.stack([GX[ins], GY[ins]], 1)
    # Sampled bilinearly: the grid step (1.6 px) against whole-pixel lookups quilted the surface.
    # Toward its edge the grid comes back onto the head's own surface: a step there showed as a thin
    # broken line round the forehead once the lab's camera saw it from a slightly different place.
    edge = np.clip(ndimage.distance_transform_edt(face) / 14.0, 0, 1)
    head = np.where(finite & (zraw - front_env < 0.02), zraw, zs)
    head = ndimage.gaussian_filter(np.where(np.abs(head - zs) < 0.03, head, zs), 2)
    zg = edge * (zs - 0.004) + (1 - edge) * (head - 0.001)
    gp = unproject(px, ndimage.map_coordinates(zg, [px[:, 1], px[:, 0]], order=1))
    a, b, c, d = vid[:-1, :-1], vid[:-1, 1:], vid[1:, :-1], vid[1:, 1:]
    ok = (a >= 0) & (b >= 0) & (c >= 0) & (d >= 0)
    gf = np.concatenate([np.stack([a[ok], c[ok], b[ok]], 1), np.stack([b[ok], c[ok], d[ok]], 1)])
    # Shading normals from a softer copy of the surface: the measured depth keeps a millimetre of
    # noise that the picture does not have, and lit at full detail it showed as blotches.
    zn = ndimage.gaussian_filter(zs, 7)
    def surf(x, y):
        return unproject(np.stack([x, y], 1), ndimage.map_coordinates(zn, [y, x], order=1))
    ex = surf(px[:, 0] + 1, px[:, 1]) - surf(px[:, 0] - 1, px[:, 1])
    ey = surf(px[:, 0], px[:, 1] + 1) - surf(px[:, 0], px[:, 1] - 1)
    gn = np.cross(ey, ex); gn /= np.linalg.norm(gn, axis=1, keepdims=True) + 1e-12
    gn = np.where((gn[:, 2] < 0)[:, None], -gn, gn)
    GRID_NORMALS[:] = [gn]
    FACE_INFO[:] = [zs, ndimage.binary_dilation(face, iterations=3)]
    print('face grid', ins.sum(), 'vertices,', len(gf), 'faces; scan faces dropped', over.sum())
    return np.concatenate([P, gp]), np.concatenate([F, gf]).astype(np.int32), len(P), ~over

def vertex_normals(P, F):
    fn = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]])
    vn = np.zeros_like(P)
    for k in range(3): np.add.at(vn, F[:, k], fn)
    return vn / (np.linalg.norm(vn, axis=1, keepdims=True) + 1e-12)

def texture_sampler(orig, path):
    """Colours of a generator texture at points near its own surface."""
    op, ouv = orig
    tex = np.asarray(Image.open(path).convert('RGB')); tree = cKDTree(op)
    def sample(points):
        _, i = tree.query(points, k=1); u = ouv[i]
        return tex[np.clip(((1 - u[:, 1]) * tex.shape[0]).astype(int), 0, tex.shape[0] - 1),
                   np.clip((u[:, 0] * tex.shape[1]).astype(int), 0, tex.shape[1] - 1)].astype(float)
    return sample

def vote(P, F, flag, r=0.008):
    """A per-face flag, settled by the faces around it: single faces of the wrong colour flecked it."""
    c = P[F].mean(1)
    return np.array([flag[n].mean() for n in cKDTree(c).query_ball_point(c, r)]) > 0.5

SKIN, HAIR, KNIT, GRID, NECK, CORE, CORE_BODY = 0, 1, 2, 3, 4, 5, 6   # NECK: skin that stays with the body; CORE: dark inside

def compose(work):
    """The body from the bald reconstruction (skull, ears, neck, turtleneck), the hair from the haired
    one. The haired scan alone fused hair and head into one shell with holes; under a separate hair
    layer the bald head stays whole, and a gap between strands shows scalp, not the room. Mixing the
    two turtlenecks failed: they disagree by 1-2 cm round the neck."""
    HP, HF, horig = load_scan(f'{work}/kairo-s42.glb'); HP, HF = outer_surface(HP, HF)
    BP, BF, borig = load_scan(f'{work}/bald/kairo-bald-s42.glb', 90000); BP, BF = outer_surface(BP, BF)
    # Each run is pixel-aligned to its own crop and the two source images to each other, so the bald
    # mesh moves by the difference of the crops (51 px, about 4.5 cm at the head).
    BP = reframe(BP, OY - BALD_OY); borig = (reframe(borig[0], OY - BALD_OY), borig[1])
    hair_tex = texture_sampler(horig, f'{work}/kairo-s42_base.png')
    bald_tex = texture_sampler(borig, f'{work}/bald/kairo-bald-s42_base.png')
    hc, bc = HP[HF].mean(1), BP[BF].mean(1)
    hcol, bcol = hair_tex(hc), bald_tex(bc)
    # Both runs share the generator's camera and crop (the images are framed alike), so the two
    # meshes line up as they are. Hair: dark blue; knit: light, grey-lavender; skin: warm.
    lum = hcol.mean(1)
    hair = vote(HP, HF, ((hcol[:, 2] > hcol[:, 0] + 10) & (lum < 115)) | (lum < 40))
    at, _ = front_zbuffer(BP, BF)
    cpx = src_to_crop([(MOUTH['cx'], COLLAR_Y)])
    collar_y = unproject(cpx, at(cpx))[0, 1]
    hair &= hc[:, 1] > collar_y - 0.02
    # Where the photo shows skin, no strand lies on it: scan strands there poked through the forehead.
    photo = np.asarray(Image.open(f'{work}/kairo-front.png').convert('RGB')).astype(float)
    cp, hz = project(hc); sp = np.clip(((cp - np.array([OX, OY])) / S).astype(int), 0, photo.shape[0] - 1)
    pc = photo[sp[:, 1], sp[:, 0]]
    hair &= ~((pc[:, 0] > pc[:, 2] + 30) & (hz < at(cp) + 0.015))
    # The turtleneck stays put while the head turns: the bald run's knit, below the chin.
    knit = vote(BP, BF, ~(bcol[:, 0] > bcol[:, 2] + 15)) & (bc[:, 1] < collar_y + 0.08)
    # The neck inside the collar stays with the body: turning with the head it swept out through the
    # collar's side. The split follows the collar's rim, which rises toward the back.
    ring = bc[~knit & (np.abs(bc[:, 1] - collar_y) < 0.015)]
    axis = np.array([ring[:, 0].mean(), ring[:, 2].mean()])
    def sector(c): return ((np.arctan2(c[:, 0] - axis[0], c[:, 2] - axis[1]) + math.pi) / (2 * math.pi) * 36).astype(int) % 36
    radius = np.hypot(bc[:, 0] - axis[0], bc[:, 2] - axis[1])
    collar = knit & (radius < 0.12)
    rim = np.full(36, collar_y); np.maximum.at(rim, sector(bc[collar]), bc[collar, 1])
    # Everything below the rim stays, whatever its colour: warm specks in the knit's texture went with
    # the head and swept over the collar.
    neck = ~knit & (bc[:, 1] < rim[sector(bc)] + 0.002)
    # On the neck itself the split rises to just under the chin (and toward the jaw's angle at the
    # sides): the neck turns about its own axis, so a seam on it slides along the surface instead of
    # opening, and the neck no longer looks cut off and pushed out under the jaw.
    r_neck = np.percentile(np.hypot(ring[:, 0] - axis[0], ring[:, 2] - axis[1]), 80)
    cpx = src_to_crop([(MOUTH['cx'], CHIN_Y)]); chin_y = unproject(cpx, at(cpx))[0, 1]
    # The whole neck stays with the body, up to where the jaw's underside begins: per sector, the
    # lowest face inside the neck's reach that faces down. Taking a fixed height instead put the
    # jaw's underside on the body, where it stuck out as a flap when the head turned.
    bn = np.cross(BP[BF[:, 1]] - BP[BF[:, 0]], BP[BF[:, 2]] - BP[BF[:, 0]]); bn /= np.linalg.norm(bn, axis=1, keepdims=True) + 1e-12
    bn *= np.sign((bn * (bc - np.array([axis[0], bc[:, 1].mean(), axis[1]]) * [1, 0, 1])).sum(1, keepdims=True) + 1e-9)
    bn = np.cross(BP[BF[:, 1]] - BP[BF[:, 0]], BP[BF[:, 2]] - BP[BF[:, 0]]); bn /= np.linalg.norm(bn, axis=1, keepdims=True) + 1e-12
    under_jaw = ~knit & (bn[:, 1] < -0.5) & (bc[:, 1] > collar_y + 0.03) & (radius < r_neck + 0.05)
    jaw = np.full(36, chin_y + 0.12); np.minimum.at(jaw, sector(bc[under_jaw]), bc[under_jaw, 1])
    jaw = ndimage.median_filter(jaw, 5, mode='wrap')
    neck |= ~knit & (radius < r_neck + 0.01) & (bc[:, 1] < jaw[sector(bc)] - 0.004)
    print('neck top per sector (cm above the collar)', np.round((jaw - collar_y) * 100).astype(int))
    top_y = np.median(jaw)
    print('compose: bald', len(BF), 'knit', knit.sum(), 'hair', hair.sum(), 'collar y', round(float(collar_y), 3))
    SP_, SF = hair_shell(HP, HF[hair])
    # Not over the face: where the photo shows skin near the front, no hair lies.
    sc_ = SP_[SF].mean(1); cp, sz = project(sc_); sp = np.clip(((cp - np.array([OX, OY])) / S).astype(int), 0, photo.shape[0] - 1)
    pc = photo[sp[:, 1], sp[:, 0]]
    SF = SF[~((pc[:, 0] > pc[:, 2] + 30) & (sz < at(cp) + 0.04))]
    CP, CF = dark_core(BP, BF)
    cc = CP[CF].mean(1)
    P = np.concatenate([BP, HP, SP_, CP])
    F = np.concatenate([BF, HF[hair] + len(BP), SF + len(BP) + len(HP), CF + len(BP) + len(HP) + len(SP_)])
    label = np.concatenate([np.where(knit, KNIT, np.where(neck, NECK, SKIN)), np.full(hair.sum() + len(SF), HAIR),
                            np.where(cc[:, 1] < top_y, CORE_BODY, CORE)])
    used = np.unique(F); remap = np.full(len(P), -1); remap[used] = np.arange(len(used))
    hair_points, hair_cols = hc[hair], hcol[hair]
    epx = src_to_crop([(626.0, IRISES[0][1])])
    eye_y = unproject(epx, at(epx))[0, 1]
    def bald_colour(points, gain=1.0):
        """The bald texture, turned to the hair's own dark under the hair: the scalp between strands."""
        col = bald_tex(points) * gain
        d, i = cKDTree(hair_points).query(points)
        # Only the scalp, above the eyes: lower down the hair hangs over the cheeks, which stay skin.
        w = (smooth(0.03, 0.012, d) * smooth(eye_y + 0.05, eye_y + 0.09, points[:, 1]))[:, None]
        return col * (1 - w) + hair_cols[i] * 0.55 * w
    # The head turns and nods about the neck's own axis at the neck's top, so the neck does not shift.
    pivot = np.array([axis[0], top_y, axis[1]])
    return P[used], remap[F].astype(np.int32), label, bald_colour, hair_tex, pivot

def hair_shell(P, F, res=0.005):
    """A closed surface just inside the hair (voxelised strands, closed and eroded): the gaps between
    the scan's strands showed the room through as white spots."""
    import pymeshlab
    from skimage import measure
    lo = P[F].reshape(-1, 3).min(0) - 0.03; shape = np.ceil((P[F].reshape(-1, 3).max(0) + 0.03 - lo) / res).astype(int) + 1
    rng = np.random.default_rng(5)
    pts = np.concatenate([np.einsum('k,fkd->fd', w, P[F]) for w in rng.dirichlet((1, 1, 1), size=16)])
    occ = np.zeros(shape, bool); i = ((pts - lo) / res).astype(int); occ[i[:, 0], i[:, 1], i[:, 2]] = True
    occ = ndimage.binary_closing(occ, structure=ndimage.generate_binary_structure(3, 1), iterations=3)
    occ = ndimage.binary_erosion(occ, iterations=1)
    V, Fs, _, _ = measure.marching_cubes(ndimage.gaussian_filter(occ.astype(float), 1.0), 0.5)
    ms = pymeshlab.MeshSet(); ms.add_mesh(pymeshlab.Mesh(V * res + lo, Fs[:, ::-1].astype(np.int32)))
    ms.meshing_decimation_quadric_edge_collapse(targetfacenum=25000, preservenormal=True)
    m = ms.current_mesh()
    print('hair shell', m.face_number(), 'faces')
    return m.vertex_matrix(), m.face_matrix().astype(np.int32)

def dark_core(P, F, res=0.005):
    """A closed surface 1 cm inside the head and body, shown black: wherever the scan's surface has a
    gap (behind the ears, between strands) the eye meets dark, not the room behind."""
    import pymeshlab
    from skimage import measure
    lo = P.min(0) - 0.02; shape = np.ceil((P.max(0) + 0.02 - lo) / res).astype(int) + 1
    rng = np.random.default_rng(6)
    pts = np.concatenate([np.einsum('k,fkd->fd', w, P[F]) for w in rng.dirichlet((1, 1, 1), size=8)] + [P])
    occ = np.zeros(shape, bool); i = ((pts - lo) / res).astype(int); occ[i[:, 0], i[:, 1], i[:, 2]] = True
    occ = ndimage.binary_closing(occ, structure=ndimage.generate_binary_structure(3, 1), iterations=1)
    # Filled slice by slice (horizontal): the body is open at the bottom, so a 3D fill finds no inside.
    for y in range(shape[1]):
        occ[:, y, :] = ndimage.binary_fill_holes(occ[:, y, :])
    occ = ndimage.binary_erosion(occ, iterations=2)
    V, Fs, _, _ = measure.marching_cubes(ndimage.gaussian_filter(occ.astype(float), 1.0), 0.5)
    ms = pymeshlab.MeshSet(); ms.add_mesh(pymeshlab.Mesh(V * res + lo, Fs[:, ::-1].astype(np.int32)))
    ms.meshing_decimation_quadric_edge_collapse(targetfacenum=12000, preservenormal=True)
    m = ms.current_mesh()
    V, Fc = m.vertex_matrix(), m.face_matrix().astype(np.int32)
    # Never near the skin: filled over concave places (the collar's fold, under the jaw, round the
    # ears) it came through as black bands.
    near = cKDTree(pts).query(V)[0] < 0.006
    Fc = Fc[~near[Fc].any(1)]
    print('dark core', len(Fc), 'faces')
    return V, Fc

def occlusion(P, F, VN, res=0.004, reach=0.06):
    """Ambient occlusion per vertex from a voxelised surface: the room light reaching a point past
    the shapes round it. The generator's textures carry almost none, so ears, the neck under the
    jaw and the collar read flat once the head turns."""
    lo = P.min(0) - reach; shape = np.ceil((P.max(0) + reach - lo) / res).astype(int) + 1
    rng = np.random.default_rng(3)
    pts = np.concatenate([np.einsum('k,fkd->fd', w, P[F]) for w in rng.dirichlet((1, 1, 1), size=6)] + [P])
    occ = np.zeros(shape, bool); i = ((pts - lo) / res).astype(int); occ[i[:, 0], i[:, 1], i[:, 2]] = True
    k = np.arange(40) + 0.5
    phi = np.arccos(1 - 2 * k / 40); th = math.pi * (1 + 5 ** 0.5) * k
    dirs = np.stack([np.sin(phi) * np.cos(th), np.cos(phi), np.sin(phi) * np.sin(th)], 1)
    steps = np.arange(0.012, reach, res)
    ao = np.ones(len(P))
    for chunk in np.array_split(np.arange(len(P)), max(1, len(P) // 8000)):
        n = VN[chunk]; w = np.clip(n @ dirs.T, 0, None)                       # cosine weight per direction
        o = P[chunk] + n * 0.006
        hit = np.zeros(w.shape, bool)
        for d in steps:
            q = ((o[:, None, :] + dirs[None] * d - lo) / res).astype(int)
            q = np.clip(q, 0, shape - 1)
            hit |= occ[q[..., 0], q[..., 1], q[..., 2]]
        ao[chunk] = 1 - (w * hit).sum(1) / (w.sum(1) + 1e-9)
    return ao

# ---------------------------------------------------------------- stage 3: textures

def atlas(P, F, sample, size, shade=None):
    """Every triangle its own half-cell in an atlas, coloured by `sample` at points on it."""
    cells = int(np.ceil(np.sqrt(len(F) / 2))); cs = size // cells
    lo, hi = 1.0 / cs, 1 - 1.0 / cs
    tri = np.array([[[lo, lo], [hi, lo], [lo, hi]], [[hi, hi], [lo, hi], [hi, lo]]])
    gx, gy = np.meshgrid((np.arange(cs) + 0.5) / cs, (np.arange(cs) + 0.5) / cs)
    lower = (gx + gy) <= 1.0
    tex = np.zeros((size, size, 3), np.uint8)
    k = np.arange(len(F)); cell, half = k // 2, k % 2
    uv = (tri[half] + np.stack([cell % cells, cell // cells], 1)[:, None, :]) * cs / size
    for h in (0, 1):
        ks = np.arange(h, len(F), 2)
        a, b, c = P[F[ks, 0]], P[F[ks, 1]], P[F[ks, 2]]
        m = lower if h == 0 else ~lower
        tx, ty = gx[m], gy[m]
        w1, w2 = ((tx - lo) / (hi - lo), (ty - lo) / (hi - lo)) if h == 0 else ((hi - ty) / (hi - lo), (hi - tx) / (hi - lo))
        w0 = 1 - w1 - w2
        pts = a[:, None] * w0[None, :, None] + b[:, None] * w1[None, :, None] + c[:, None] * w2[None, :, None]
        ck = ks // 2
        px = (ck % cells)[:, None] * cs + np.where(m)[1][None, :]
        py = (ck // cells)[:, None] * cs + np.where(m)[0][None, :]
        col = sample(pts.reshape(-1, 3)).reshape(len(ks), -1, 3)
        if shade is not None:
            col = col * (shade[F[ks, 0]][:, None] * w0 + shade[F[ks, 1]][:, None] * w1 + shade[F[ks, 2]][:, None] * w2)[..., None]
        tex[py.reshape(-1), px.reshape(-1)] = np.clip(col, 0, 255).reshape(-1, 3).astype(np.uint8)
    return uv, tex

def front_faces(P, F, work):
    inp, zc = project(P)
    zb_at, zbuf = front_zbuffer(P, F)
    vi = np.clip(inp.astype(int), 0, N - 1)
    visible = zc <= zb_at(inp) + 0.02
    alpha = np.asarray(Image.open(f'{work}/kairo-s42.svviews/input.png').convert('RGBA'))[..., 3]
    bald = np.roll(np.asarray(Image.open(f'{work}/bald/kairo-bald-s42.svviews/input.png').convert('RGBA'))[..., 3], int(OY - BALD_OY), 0)
    alpha = np.maximum(alpha, bald)
    solid = alpha[vi[:, 1], vi[:, 0]] > 235
    fn = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]]); fn /= np.linalg.norm(fn, axis=1, keepdims=True) + 1e-12
    view = np.array([0, 0, D]) - P[F].mean(1); view /= np.linalg.norm(view, axis=1, keepdims=True)
    # No facing test: the dense, crumpled eye geometry has visible faces turned edge-on or away, and
    # giving those the generator's texture speckled the eyes.
    front = (visible[F].sum(1) >= 2) & solid[F].all(1) & ((fn * view).sum(1) > -0.7)
    return front, visible, zbuf

# ---------------------------------------------------------------- stage 4: morph fields (source px)

def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)

def lid_curves(e, x):
    t = np.clip((x - e['cx']) / e['half'], -1, 1)
    yu = e['upper'] + 40 * t * t
    yl = e['lower'] - 22 * t * t
    return t, yu, yl

def eye_field(xy, kind):
    """Displacement (dx, dy) in source px for a lid or gaze shape."""
    d = np.zeros_like(xy)
    for e in EYES:
        x, y = xy[:, 0], xy[:, 1]
        t, yu, yl = lid_curves(e, x)
        inside_x = np.abs(x - e['cx']) < e['half'] * 1.08
        top = e['crease'] - 16
        across = 1 - smooth(0.9, 1.08, np.abs(x - e['cx']) / e['half'])
        if kind in ('blink', 'squint'):
            continue                                # the lid surface does these
        if kind == 'blink-scan':
            # The upper lid (crease to lid edge, with its lashes) slides down to the lower lid; the eye
            # between them folds into a sliver under it.
            # A little past the lower lid line, so no white shows under the lashes.
            target = yl + 3.0
            floor = yl + 6.0
            up = (y >= top) & (y <= yu)
            mid = (y > yu) & (y <= floor)
            ny = np.where(up, top + (y - top) * (target - top) / np.maximum(yu - top, 1e-3), y)
            ny = np.where(mid, target + (y - yu) * (floor - target) / np.maximum(floor - yu, 1e-3), ny)
            d[:, 1] += np.where(inside_x, (ny - y) * across, 0)
        elif kind == 'happy':
            # The lower lid rises: the eye becomes a smiling arch.
            lift = (yl - yu) * 0.55
            bot = yl + 24
            band = (y >= yu) & (y <= bot)
            ny = np.where(band, yu + (y - yu) * (bot - lift - yu) / np.maximum(bot - yu, 1e-3), y)
            d[:, 1] += np.where(inside_x, (ny - y) * across, 0)
        elif kind == 'wide':
            band = (y >= top - 22) & (y <= yl)
            w = np.where(y < yu, smooth(top - 22, yu, y), 1 - smooth(yu, yl, y))
            d[:, 1] += np.where(inside_x & band, -7.0 * w * across, 0)
        elif kind == 'squint':
            target_u = yu + (yl - yu) * 0.32; target_l = yl - (yl - yu) * 0.18
            up = (y >= top) & (y <= yu)
            ny = np.where(up, top + (y - top) * (target_u - top) / np.maximum(yu - top, 1e-3), y)
            low = (y >= yl) & (y <= yl + 22)
            ny = np.where(low, target_l + (y - yl) * (yl + 22 - target_l) / 22, ny)
            mid = (y > yu) & (y < yl)
            ny = np.where(mid, target_u + (y - yu) * (target_l - target_u) / np.maximum(yl - yu, 1e-3), ny)
            d[:, 1] += np.where(inside_x, (ny - y) * across, 0)
        # look_*: the iris disc moves (eye_parts); the face stays.
    return d

def brow_field(xy, kind):
    d = np.zeros_like(xy)
    x, y = xy[:, 0], xy[:, 1]
    for i, b in enumerate(BROWS):
        span = smooth(b['x0'] - 30, b['x0'], x) * (1 - smooth(b['x1'], b['x1'] + 30, x))
        vert = np.exp(-((y - b['y']) / 26) ** 2) * (y < b['y'] + 40)
        inner = np.exp(-((x - b['inner']) / 45) ** 2)
        if kind == 'raise':
            d[:, 1] += -9 * span * vert
        elif kind == 'inner_up':
            d[:, 1] += -9 * inner * vert * span
        elif kind == 'frown':
            d[:, 1] += 5 * inner * vert * span
            d[:, 0] += (6 if i == 0 else -6) * inner * vert * span
        elif kind == 'one_up' and i == 1:
            d[:, 1] += -10 * span * vert
    return d

def mouth_field(xy, kind):
    m = MOUTH
    x, y = xy[:, 0], xy[:, 1]
    d = np.zeros_like(xy)
    half = (m['right'] - m['left']) / 2
    u = (x - m['cx']) / half                     # -1 at the left corner, +1 at the right
    across = 1 - smooth(1.0, 1.7, np.abs(u))
    below = smooth(m['y'] - 2, m['y'] + 4, y) * (1 - smooth(m['bottom'] + 40, m['bottom'] + 110, y))
    above = (1 - smooth(m['y'] - 4, m['y'] + 2, y)) * smooth(m['top'] - 30, m['top'] + 5, y)
    lips = np.exp(-((y - m['y']) / 34) ** 2)
    def corners(amount_x, amount_y, which=(-1, 1)):
        for s in which:
            cx = m['cx'] + s * half
            w = np.exp(-(((x - cx) / 36) ** 2 + ((y - m['y']) / 30) ** 2))
            d[:, 0] += s * amount_x * w
            d[:, 1] += amount_y * w
    if kind == 'open':
        d[:, 1] += 24 * below * across * (1 - 0.5 * np.abs(u).clip(0, 1) ** 2)
        d[:, 1] += -3 * above * across * (1 - np.abs(u).clip(0, 1) ** 2)
    elif kind == 'wide':
        corners(13, -2); d[:, 1] += 8 * below * across
    elif kind == 'round':
        corners(-20, 0); d[:, 1] += 15 * below * across * (1 - np.abs(u).clip(0, 1) ** 2)
    elif kind == 'small_o':
        corners(-14, 0); d[:, 1] += 8 * below * across * (1 - np.abs(u).clip(0, 1) ** 2)
    elif kind == 'smile':
        corners(9, -11)
        cheeks = np.exp(-(((np.abs(x - m['cx']) - 120) / 50) ** 2 + ((y - 680) / 40) ** 2))
        d[:, 1] += -5 * cheeks
    elif kind == 'smirk':
        corners(7, -10, which=(1,))
    elif kind == 'frown':
        corners(-3, 8)
    elif kind == 'press':
        d[:, 1] += -3 * below * across * lips
    elif kind == 'teeth':
        d[:, 1] += 7 * below * across * (1 - np.abs(u).clip(0, 1) ** 2)
    return d

EYE_TARGETS = ['blink', 'wide', 'squint', 'happy', 'look_left', 'look_right', 'look_up', 'look_down']
BROW_TARGETS = ['raise', 'inner_up', 'frown', 'one_up']
MOUTH_TARGETS = ['open', 'wide', 'round', 'small_o', 'smile', 'smirk', 'frown', 'press', 'teeth']
TARGETS = EYE_TARGETS + ['brow_' + b for b in BROW_TARGETS] + MOUTH_TARGETS

def field(xy, name):
    if name in EYE_TARGETS: return eye_field(xy, name)
    if name.startswith('brow_'): return brow_field(xy, name[5:])
    return mouth_field(xy, name)

LID_CROP = (400, 420, 860, 630)        # source px of the eyes-closed picture kept for the lids
LID_SCALE = 2                          # texture px per source px
HANG = 16.0                            # how far the closed lashes hang below the lid's edge (source px)

def lid_span(lid):
    """The lid surface's x range: well past the outer corner, so the photo's lash wing goes with the
    lid, and just past the inner one (wider there, it carried skin specks)."""
    outer_left = LIDS.index(lid) == 0
    return lid['ux'][0] - (16 if outer_left else 6), lid['ux'][-1] + (6 if outer_left else 16)

def lid_fold(lid, x):
    """The upper lid's crease over source x: an arc that meets the margin at both corners, so the
    closing lid tapers into the corners (a crease flattened toward them made a square-ended strip)."""
    x0, x1 = lid_span(lid)
    t = np.clip((x - (x0 + x1) / 2) / ((x1 - x0) / 2), -1, 1)
    margin = np.interp(x, lid['ux'], lid['uy']) + 2.0
    return margin - FOLD * (1 - t * t) ** 0.6, t

def closed_lash_line(closed, lid, x):
    """Where the closed lid's lash line runs in the eyes-closed picture: the darkest row near the
    lower lid, smoothed along the lid."""
    lum = np.asarray(closed).astype(float).mean(-1)
    lum = ndimage.gaussian_filter(lum, 1.5)
    lower = np.interp(x, lid['lx'], lid['ly'])
    ys = np.array([lo - 12 + np.argmin(lum[int(lo - 12):int(lo + 14), int(round(xi))]) for xi, lo in zip(x, lower)], float)
    return ndimage.gaussian_filter1d(ndimage.median_filter(ys, 5, mode='nearest'), 2, mode='nearest')

def lid_texture(closed, src):
    """The eyes-closed picture round both eyes, for the closing lids: real lid skin, crease and
    lashes (a flat synthetic skin looked like plastic). Alpha fades in below the crease and, under the
    lash line, keeps only the dark lashes, so they hang free over the eye while the lid moves."""
    x0, y0, x1, y1 = LID_CROP
    a = np.asarray(closed).astype(float)
    yy, xx = np.mgrid[y0:y1:1 / LID_SCALE, x0:x1:1 / LID_SCALE]
    rgb = np.stack([ndimage.map_coordinates(a[..., c], [yy, xx], order=1) for c in range(3)], -1)
    alpha = np.zeros(yy.shape)
    lum = rgb.mean(-1)
    photo = np.asarray(src).astype(float)
    for lid in LIDS:
        xs = np.linspace(*lid_span(lid), 64)
        edge = closed_lash_line(closed, lid, xs)
        fold, t = lid_fold(lid, xs)
        e, f = np.interp(xx, xs, edge), np.interp(xx, xs, fold)
        inx = (xx >= xs[0]) & (xx <= xs[-1])
        # The closed picture's light differs a little from the photo's: matched just above the crease,
        # where both pictures show the same skin (below it the photo has lashes).
        band = np.zeros(a.shape[:2], bool)
        for xi, fi, ti in zip(xs, fold, t):
            if abs(ti) < 0.6:
                band[int(fi - 22):int(fi - 6), int(xi)] = True
        g = np.median(photo[band], 0) / (np.median(a[band], 0) + 1e-6)
        rgb = np.where(inx[..., None], rgb * np.clip(g, 0.8, 1.25), rgb)
        # Faded in below the crease and out toward both corners: hard edges there read as a pasted strip.
        side = smooth(0.0, 0.12, 1 - np.abs(np.interp(xx, xs, t)))
        top = np.interp(xx, lid['ux'], lid['uy']) + 2.0 - LASH
        # Faded in over the 10 px above the lid's top: a closed lid shows no edge against the brow.
        skin = np.clip((yy - (top - 13)) / 10.0, 0, 1) * (yy <= e) * side
        skin_lum = np.interp(xx, xs, [np.median(lum[(np.abs(yy - (ei + HANG + 6)) < 3) & (np.abs(xx - xi) < 4)]) for xi, ei in zip(xs, edge)])
        lashes = np.clip((skin_lum - lum) / 45.0, 0, 1) * (yy > e) * (1 - smooth(e + HANG - 4, e + HANG, yy)) * side
        alpha = np.where(inx, np.maximum(alpha, np.maximum(skin, lashes)), alpha)
    return Image.fromarray(np.clip(np.dstack([rgb, alpha * 255]), 0, 255).astype(np.uint8), 'RGBA')

def lid_overlays(front_depth, surface_normal, closed):
    """The upper lids as their own surface (the face grid has a hole there, so the face itself has no
    lid or lashes to leave behind). From the crease down: lid skin from the eyes-closed picture, then
    the photo's lash band, then the eyes-closed picture's hanging lashes. Open, the skin is a sliver
    in the crease and the lash band is the photo exactly; closing, the skin unfolds down over the eye,
    the photo's band folds into the lid's edge and the closed lashes unfold below it."""
    cols, skin_rows, band_rows, hang_rows = 48, 12, 6, 5
    x0c, y0c, x1c, y1c = LID_CROP
    parts = []                                              # (rest ys, {target: ys}, uv, material) per eye and part
    for lid in LIDS:
        x = np.linspace(*lid_span(lid), cols)
        margin = np.interp(x, lid['ux'], lid['uy']) + 2.0
        lower = np.interp(x, lid['lx'], lid['ly'])
        fold, _ = lid_fold(lid, x)
        edge = closed_lash_line(closed, lid, x)
        top = margin - LASH                                 # the band's top: the lash tips
        fs, fb, fh = np.linspace(0, 1, skin_rows), np.linspace(0, 1, band_rows), np.linspace(0, 1, hang_rows)
        span = lambda a_, b_, f: a_[None] + (b_ - a_)[None] * f[:, None]
        flat = lambda y, n: np.broadcast_to(y, (n, cols))
        # The lid's top is the lash tips' line (the skin above stays on the face, as in the photo), a
        # little under the face's edge so the hole's jagged rim leaves no gap.
        crease = top - 3.0
        def opened(bottom):
            """Skin squeezed into the crease, the band ending at `bottom`, the hanging lashes folded."""
            t_ = bottom - (margin - 1.5 - top)
            return span(crease, t_, fs), span(t_, bottom, fb), flat(bottom, hang_rows)
        fade_rows = 4; ff = np.linspace(0, 1, fade_rows)
        hidden = flat(crease, fade_rows)                    # the fade above the lid: folded away unless closed
        # Open, the band ends on the lash line itself: 2 px lower it carried the top of the photo's iris,
        # which stayed in place when the iris moved up.
        rest = opened(margin - 1.5) + (hidden,)
        closed_shape = (span(crease, edge - 3, fs), flat(edge - 3, band_rows), span(edge - 3, edge + HANG, fh), span(crease - 10, crease, ff))
        # The blink shows the owner's painted half-closed and closed eyes as frames (eye_frames); the
        # lid surface only squints.
        shapes = {'squint': opened(margin - 1.5 + (lower - margin) * 0.38) + (hidden,),
                  'wide': opened(margin - 8.5) + (hidden,)}
        grid = lambda ys: np.stack([np.broadcast_to(x, ys.shape), ys], -1).reshape(-1, 2)
        to_closed = lambda px: (px - np.array([x0c, y0c])) / np.array([x1c - x0c, y1c - y0c])
        for k, material in ((0, 3), (1, 0), (2, 3), (3, 3)):
            uv = grid(rest[1]) / SRC_W if k == 1 else to_closed(grid(closed_shape[k]))
            # Depth: open, just behind the face (the grid covers the crease's top); closing, the lid comes
            # forward over the eye, except its top row, which stays tucked under the face's edge.
            # The fade lies just in front of the face above the lid (0.5 mm).
            forward = np.repeat(fs if k == 0 else np.full(len(rest[k]), 0.222 if k == 3 else 1.0), cols)
            parts.append((grid(rest[k]), {n: grid(v[k]) for n, v in shapes.items()}, uv, material, len(rest[k]), forward))
    out = []
    for material in (3, 0):
        pos, uv, nrm, idx, targets, base = [], [], [], [], {n: [] for n in TARGETS}, 0
        for rest_px, shape_px, uv_k, mat, rows, forward in parts:
            if mat != material:
                continue
            crop = src_to_crop(rest_px)
            p0 = unproject(crop, front_depth(crop) + 0.0005)
            pos.append(p0); uv.append(uv_k)
            for name in TARGETS:
                if name in shape_px:
                    c2 = src_to_crop(shape_px[name])
                    targets[name].append(unproject(c2, front_depth(c2) + 0.0005 - (0.0045 if name == 'blink' else 0.001) * forward) - p0)
                else:
                    targets[name].append(np.zeros_like(p0))
            for r in range(rows - 1):
                for c in range(cols - 1):
                    a_ = base + r * cols + c
                    idx.append([a_, a_ + cols, a_ + 1, a_ + 1, a_ + cols, a_ + cols + 1])
            base += len(p0)
        P = np.concatenate(pos)
        out.append(dict(pos=P, nrm=surface_normal(P), uv=np.concatenate(uv), idx=np.array(idx, np.uint32).reshape(-1), material=material,
                        targets=[np.concatenate(targets[n]) for n in TARGETS]))
    return out

# ---------------------------------------------------------------- GLB writer with sparse targets

class Glb:
    def __init__(self):
        self.bin = bytearray()
        self.gltf = {"asset": {"version": "2.0", "generator": "kairo_scan.py (Spine Avatar Lab)"}, "buffers": [{}], "bufferViews": [],
                     "accessors": [], "meshes": [], "nodes": [], "materials": [], "textures": [], "images": [], "samplers": [],
                     "animations": [], "scenes": [{"nodes": [0]}], "scene": 0}

    def view(self, data, target=None):
        while len(self.bin) % 4: self.bin.append(0)
        v = {"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data)}
        if target: v["target"] = target
        self.bin += data; self.gltf["bufferViews"].append(v)
        return len(self.gltf["bufferViews"]) - 1

    def accessor(self, a, kind, target=None, minmax=False):
        a = np.ascontiguousarray(a); ct = 5125 if a.dtype == np.uint32 else 5126
        if ct == 5126: a = a.astype(np.float32)
        count = a.size if kind == "SCALAR" else a.shape[0]
        acc = {"bufferView": self.view(a.tobytes(), target), "componentType": ct, "count": count, "type": kind}
        if minmax:
            acc["min"] = [float(x) for x in a.reshape(count, -1).min(0)]; acc["max"] = [float(x) for x in a.reshape(count, -1).max(0)]
        self.gltf["accessors"].append(acc); return len(self.gltf["accessors"]) - 1

    def sparse_vec3(self, delta):
        nz = np.where(np.abs(delta).max(1) > 1e-7)[0].astype(np.uint32)
        acc = {"componentType": 5126, "count": len(delta), "type": "VEC3",
               "min": [float(x) for x in np.minimum(delta.min(0), 0)], "max": [float(x) for x in np.maximum(delta.max(0), 0)]}
        if len(nz):
            acc["sparse"] = {"count": int(len(nz)), "indices": {"bufferView": self.view(nz.tobytes()), "componentType": 5125},
                             "values": {"bufferView": self.view(delta[nz].astype(np.float32).tobytes())}}
        else:
            acc["sparse"] = {"count": 1, "indices": {"bufferView": self.view(np.zeros(1, np.uint32).tobytes()), "componentType": 5125},
                             "values": {"bufferView": self.view(np.zeros(3, np.float32).tobytes())}}
        self.gltf["accessors"].append(acc); return len(self.gltf["accessors"]) - 1

    def image(self, data, mime, name):
        self.gltf["images"].append({"name": name, "mimeType": mime, "bufferView": self.view(data)})
        return len(self.gltf["images"]) - 1

    def mesh(self, name, prims, target_names=()):
        out = []
        for p in prims:
            prim = {"attributes": {"POSITION": self.accessor(p['pos'], "VEC3", 34962, True), "NORMAL": self.accessor(p['nrm'], "VEC3", 34962),
                                   "TEXCOORD_0": self.accessor(p['uv'], "VEC2", 34962)},
                    "indices": self.accessor(p['idx'].astype(np.uint32), "SCALAR", 34963), "material": p['material']}
            if p.get('targets') is not None:
                prim["targets"] = [{"POSITION": self.sparse_vec3(t)} for t in p['targets']]
            out.append(prim)
        mesh = {"name": name, "primitives": out}
        if target_names:
            mesh["weights"] = [0.0] * len(target_names); mesh["extras"] = {"targetNames": list(target_names)}
        self.gltf["meshes"].append(mesh); return len(self.gltf["meshes"]) - 1

    def node(self, name, mesh=None, t=None, children=()):
        n = {"name": name}
        if mesh is not None: n["mesh"] = mesh
        if t is not None: n["translation"] = [float(x) for x in t]
        if children: n["children"] = list(children)
        self.gltf["nodes"].append(n); return len(self.gltf["nodes"]) - 1

    def animation(self, name, channels):
        out = {"name": name, "samplers": [], "channels": []}
        for node, path, times, values in channels:
            kind = {"translation": "VEC3", "scale": "VEC3", "rotation": "VEC4"}[path]
            out["samplers"].append({"input": self.accessor(np.array(times, np.float32), "SCALAR", None, True),
                                    "output": self.accessor(np.array(values, np.float32), kind), "interpolation": "LINEAR"})
            out["channels"].append({"sampler": len(out["samplers"]) - 1, "target": {"node": node, "path": path}})
        self.gltf["animations"].append(out)

    def bytes(self):
        self.gltf["buffers"][0]["byteLength"] = len(self.bin)
        js = json.dumps(self.gltf, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4)
        binary = bytes(self.bin) + b'\0' * (-len(self.bin) % 4)
        import struct
        return (struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(binary)) + struct.pack('<II', len(js), 0x4E4F534A) + js
                + struct.pack('<II', len(binary), 0x004E4942) + binary)

EYE_FRAMES = 'eye-layers-2'      # the owner's layers: kairo-lid-half.png, kairo-lid-closed.png (full canvas, same framing)
BLINK_FRAMES = [('kairo-lid-half.png', 0.3, 0.8), ('kairo-lid-closed.png', 0.8, 2.0)]

def eye_frame(work, name, front_depth, surface_normal):
    """One painted eye state as a flat sheet over both eyes, 1 mm in front of the face, with its
    alpha softened at the edge (it comes from a different render than the photo)."""
    a = np.asarray(Image.open(f'{work}/{EYE_FRAMES}/{name}').convert('RGBA')).astype(float)
    # No hair strands (they belong to the hair layer, and stood still in the sheet when the head
    # turned); the colour under the transparent part spread from the solid one, since its black
    # bled into the edge as a dotted dark line when the texture was filtered.
    hair = ((a[..., 2] > a[..., 0] + 12) | (a[..., :3].sum(-1) < 150)) & (a[..., 3] > 10)
    hair = ndimage.binary_opening(hair, iterations=1)
    a[..., 3] = np.where(hair & (np.arange(a.shape[0])[:, None] < 470), 0, a[..., 3])
    solid = a[..., 3] > 10
    _, (iy, ix) = ndimage.distance_transform_edt(~(a[..., 3] > 200), return_indices=True)
    a[..., :3] = a[iy, ix, :3]
    ys, xs = np.where(solid)
    x0, y0, x1, y1 = xs.min() - 2, ys.min() - 2, xs.max() + 3, ys.max() + 3
    alpha = np.minimum(a[..., 3], 255 * np.clip(ndimage.distance_transform_edt(solid) / 8.0, 0, 1))
    tex = Image.fromarray(np.dstack([a[y0:y1, x0:x1, :3], alpha[y0:y1, x0:x1]]).astype(np.uint8), 'RGBA')
    gy, gx = np.mgrid[y0:y1:2, x0:x1:2]
    inside = ndimage.binary_dilation(solid, iterations=3)[np.clip(gy, 0, solid.shape[0] - 1), np.clip(gx, 0, solid.shape[1] - 1)]
    vid = np.full(gx.shape, -1); vid[inside] = np.arange(inside.sum())
    q = [vid[:-1, :-1], vid[:-1, 1:], vid[1:, :-1], vid[1:, 1:]]
    ok = np.all([v >= 0 for v in q], 0)
    idx = np.concatenate([np.stack([q[0][ok], q[2][ok], q[1][ok]], 1), np.stack([q[1][ok], q[2][ok], q[3][ok]], 1)]).reshape(-1)
    spx = np.stack([gx[inside], gy[inside]], 1).astype(float)
    c = src_to_crop(spx); pos = unproject(c, front_depth(c) - 0.001)
    uv = (spx - np.array([x0, y0])) / np.array([x1 - x0, y1 - y0])
    return dict(pos=pos, nrm=surface_normal(pos), uv=uv, idx=idx.astype(np.uint32)), tex

def jpeg(img, quality=90):
    out = io.BytesIO(); img.save(out, 'JPEG', quality=quality); return out.getvalue()

# ---------------------------------------------------------------- build

def build(work):
    P, F, label, skin_colour, scan_colour, pivot = compose(work)
    src = Image.open(f'{work}/kairo-front.png').convert('RGB')
    P, F, grid_start, kept = face_surface(P, F, src, label == HAIR)
    label = np.concatenate([label[kept], np.full(len(F) - kept.sum(), GRID)])

    front, visible, zbuf = front_faces(P, F, work)
    # The face grid is the picture by construction: the visibility test missed scattered grid faces,
    # which then took the generator's colours as spots.
    on_grid = (F >= grid_start).all(1)
    front |= on_grid
    # Head faces turned to the camera take the photo even where the visibility test missed them: as
    # atlas faces (darkened by occlusion) their slivers at the grid's edge and along the hairline
    # showed as dark specks. Hair is wound either way, so it counts by angle alone.
    fn = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]]); fn /= np.linalg.norm(fn, axis=1, keepdims=True) + 1e-12
    toward = (fn * (np.array([0, 0, D]) - P[F].mean(1))).sum(1) / np.linalg.norm(np.array([0, 0, D]) - P[F].mean(1), axis=1)
    # Only where the camera actually saw the face (near the front depth): faces behind the jaw's
    # silhouette took the picture's background there, a grey strip between cheek and ear on a turn.
    fc_px, fc_z = project(P[F].mean(1)); fi = np.clip(fc_px.astype(int), 0, N - 1)
    seen = fc_z <= zbuf[fi[:, 1], fi[:, 0]] + 0.02
    front |= ((label == SKIN) & (toward > 0.45) & seen) | ((label == HAIR) & (np.abs(toward) > 0.5) & (P[F].mean(1)[:, 2] > 0))
    # The head shows the owner's photo; the neck inside the collar and the turtleneck show the bald
    # picture, which is drawn to the same frame and fits the bald body there. Where the two pictures
    # met on the forehead their slightly different light drew a broken line.
    front &= label < CORE
    photo_px = np.asarray(src).astype(float)
    hair_px = (photo_px[..., 2] > photo_px[..., 0] + 12) | (photo_px.sum(-1) < 150)
    near_hair = ndimage.distance_transform_edt(~hair_px) < 30
    # The bald picture, toned to the photo on the face's open skin (it has the same face, lit alike).
    bald_px = np.asarray(Image.open(f'{work}/bald/kairo-bald-front.png').convert('RGB')).astype(float)
    open_skin = face_mask_src(src) & ~ndimage.binary_dilation(near_hair, iterations=10)
    tone = np.median(photo_px[open_skin], 0) / (np.median(bald_px[open_skin], 0) + 1e-6)
    print('bald picture tone', np.round(tone, 3))
    # One texture for all skin: the photo, crossfaded into the toned bald picture (same face, same
    # frame, no hair) below the eyes near the photo's hair and its shadow, and on the neck, whose
    # geometry is the bald scan's. Switching texture per face drew grey strips and patch edges on the
    # side of the head and a light line along the jaw once the head turned.
    yy = np.arange(photo_px.shape[0])[:, None].astype(float)
    hair_d = ndimage.distance_transform_edt(~hair_px)
    w = (1 - smooth(4.0, 34.0, hair_d)) * smooth(490.0, 560.0, yy)
    w = np.maximum(w, smooth(CHIN_Y - 40, CHIN_Y + 5, yy))
    w = ndimage.gaussian_filter(w, 4)[..., None]
    bald_src = Image.fromarray(np.clip(photo_px * (1 - w) + bald_px * tone * w, 0, 255).astype(np.uint8))
    # The photo itself only on the hair; all skin and the knit show the blend.
    from_photo = label == HAIR
    visible[grid_start:] = True
    # The front-most surface depth, holes filled and a little spread so a lid sits over the eyeball.
    zfill = np.where(np.isinf(zbuf), np.nan, zbuf)
    idxn = ndimage.distance_transform_edt(np.isnan(zfill), return_distances=False, return_indices=True)
    zfill = zfill[idxn[0], idxn[1]]
    # Over the openings (holes in the grid) the lids close at the face's level, not onto what is behind.
    hole = src_mask_to_crop(eye_holes((int(SRC_W), int(SRC_W)), -2.0))
    zfill[hole] = FACE_INFO[0][hole] - 0.004
    zfill = ndimage.minimum_filter(zfill, size=5)
    def front_depth(px):
        i = np.clip(px.astype(int), 0, N - 1)
        return zfill[i[:, 1], i[:, 0]]
    VN = vertex_normals(P, F)
    inp, zc = project(P)
    # Shade the front with smoothed normals: the picture already carries the face's light, and the
    # scan's crumpled surface (and the seam round the eye patches) shaded in ragged patches.
    # The face grid's normals come from its own smoothed surface: smoothing them together with the
    # layers behind (hair, the scan's eyes) turned whole regions away.
    VN[grid_start:] = GRID_NORMALS[0]
    fv = np.unique(F[front]); fv = fv[fv < grid_start]
    ftree = cKDTree(P[fv])
    n = VN[fv].copy()
    for _ in range(3):
        nb = ftree.query_ball_point(P[fv], r=0.03)
        n = np.array([n[i].mean(0) for i in nb])
        n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
    VN[fv] = n
    uv_front = ((inp - np.array([OX, OY])) / S) / np.array(src.size)
    # The generator's colours, matched to the picture where both show: off the picture's edge they
    # were paler and redder, and turning the head drew a line down the cheek.
    def gain(kind, colour, image):
        picture = np.asarray(image).astype(float)
        fs = np.where(front & (label == kind) & ~on_grid)[0]
        c = P[F[fs]].mean(1); cp, _ = project(c)
        sp = np.clip(((cp - np.array([OX, OY])) / S).astype(int), 0, src.size[0] - 1)
        pic, own = picture[sp[:, 1], sp[:, 0]], colour(c)
        # Only where both show the same thing: skin against skin, hair against hair.
        alike = (pic[:, 0] > pic[:, 2] + 30) == (own[:, 0] > own[:, 2] + 30)
        g = np.median(pic[alike], 0) / (np.median(own[alike], 0) + 1e-6)
        print('colour gain', kind, np.round(g, 2))
        return np.clip(g, 0.6, 1.6)
    bald_gain = np.ones((5, 3)); bald_gain[SKIN] = bald_gain[NECK] = gain(SKIN, skin_colour, bald_src); bald_gain[KNIT] = gain(KNIT, skin_colour, bald_src)
    hair_gain = gain(HAIR, scan_colour, src)
    ao = occlusion(P, F, VN)
    shade = 0.75 + 0.25 * ao          # stronger, the hanging hair greyed the cheek beneath it
    rest = np.where(~front & (label == HAIR))[0]
    rest_uv, rest_tex = atlas(P, F[rest], lambda q: scan_colour(q) * hair_gain, 4096, shade)
    bare = np.where(~front & (label != HAIR) & (label < CORE))[0]
    dark = np.where(label >= CORE)[0]
    bare_kind = cKDTree(P[F[bare]].mean(1))
    # Skin seen only from the side keeps the generator's light and shade but takes the photo's skin
    # tone: its own colour drifted grey-beige there, a strip between cheek and ear on a turn.
    cheek = np.asarray(bald_src).astype(float)[open_skin]
    skin_ref = np.median(cheek, 0); skin_lum = skin_ref.mean()
    def bare_colour(q):
        kind = label[bare][bare_kind.query(q)[1]]
        col = skin_colour(q, bald_gain[kind])
        lum = col.mean(1, keepdims=True)
        # A narrow brightness range: the generator's side light and the occlusion under the hanging
        # hair made the side of the face a darker patchwork against the photo's evenly lit cheek.
        toned = skin_ref[None] * np.clip(lum / skin_lum, 0.8, 1.1)
        is_skin = ((kind == SKIN) | (kind == NECK))[:, None]
        return np.where(is_skin, 0.15 * col + 0.85 * toned, col)
    skin_uv, skin_tex = atlas(P, F[bare], bare_colour, 4096, 0.6 + 0.4 * shade)
    print('front faces', front.sum(), 'hair', len(rest), 'bald', len(bare))

    # The surface seen from the camera, as depth over image position: targets slide points along it.
    vis_px, vis_z = inp[visible], zc[visible]
    depth = LinearNDInterpolator(vis_px, vis_z)
    def lift(points, vis):
        """Morph deltas for points: each field's image-space shift, back onto the surface. Every point
        near the visible surface moves, seen or not: moving only the visible ones tore the eyes where
        a lid edge hides the vertices just under it."""
        px, z0 = project(points)
        sxy = (px - np.array([OX, OY])) / S
        surface = depth(px)
        near = (~np.isnan(surface)) & (z0 - np.nan_to_num(surface, nan=0) < 0.035)
        out = []
        for name in TARGETS:
            dxy = field(sxy, name) * near[:, None]
            moved = np.where(np.abs(dxy).sum(1) > 1e-4)[0]
            delta = np.zeros_like(points)
            if len(moved):
                npx = (sxy[moved] + dxy[moved]) * S + np.array([OX, OY])
                # Each point keeps its own depth and slides in the image plane: re-sampling the depth
                # at the new position threw vertices across the jump between lashes and eyeball.
                delta[moved] = unproject(npx, z0[moved]) - points[moved]
            out.append(delta)
        return out

    # The head turns about the neck's own axis at the collar: off the axis, the neck swung out sideways.
    print('pivot', np.round(pivot, 3))

    prims = {'head': [], 'body': []}
    fpos, fuv, fnrm, fvis = P, uv_front, VN, visible.astype(float)
    def own(sel, uv):
        return (np.arange(len(sel) * 3).reshape(-1, 3), P[F[sel]].reshape(-1, 3), uv.reshape(-1, 2), VN[F[sel]].reshape(-1, 3),
                visible[F[sel]].reshape(-1).astype(float), label[sel])
    photo, drawn = front & from_photo, front & ~from_photo
    groups = [(F[photo], fpos, fuv, fnrm, fvis, label[photo], 0, True),
              (F[drawn], fpos, fuv, fnrm, fvis, label[drawn], 4, True),
              (*own(rest, rest_uv), 1, True),
              (*own(bare, skin_uv), 2, True),
              (*own(dark, np.zeros((len(dark), 3, 2))), 6, False)]
    for faces, pos, uv, nrm, vis, kind, material, morph in groups:
        # The turtleneck stays; everything else turns with the head.
        is_head = (kind != KNIT) & (kind != NECK) & (kind != CORE_BODY)
        for part, mask in (('head', is_head), ('body', ~is_head)):
            fs = faces[mask]
            if not len(fs): continue
            used = np.unique(fs); remap = np.full(len(pos), -1); remap[used] = np.arange(len(used))
            p = pos[used] - pivot if part == 'head' else pos[used]
            prim = dict(pos=p, nrm=nrm[used], uv=uv[used], idx=remap[fs].reshape(-1), material=material)
            if part == 'head':
                prim['targets'] = lift(pos[used], vis[used]) if morph else [np.zeros_like(p) for _ in TARGETS]
            prims[part].append(prim)

    front_vertices = np.unique(F[front])
    ntree = cKDTree(P[front_vertices])
    def surface_normal(points):
        _, i = ntree.query(points, k=6)
        n = VN[front_vertices][i].mean(1)
        return n / (np.linalg.norm(n, axis=1, keepdims=True) + 1e-12)
    for part in eye_parts(FACE_INFO[0], surface_normal):
        part['pos'] = part['pos'] - pivot
        prims['head'].append(part)
    closed = Image.open(f'{work}/assets-v2/kairo-eyes-closed-front.png').convert('RGB')
    # No lid surfaces: the open lid and its lashes are the photo on the face, and the blink shows the
    # owner's painted frames over it. Transparent lid sheets drew over those frames as a dotted line.

    g = Glb()
    g.gltf["samplers"].append({"magFilter": 9729, "minFilter": 9987, "wrapS": 33071, "wrapT": 33071})
    for name, img in (("front", sclera_photo(src)), ("rest", Image.fromarray(rest_tex)), ("skin", Image.fromarray(skin_tex)),
                      ("lidskin", lid_texture(closed, src)), ("bald", bald_src), ("iris", iris_texture(src)),
                      ("core", Image.new('RGB', (4, 4), (8, 6, 8)))):
        if img.mode == 'RGBA':
            data = io.BytesIO(); img.save(data, 'PNG'); i = g.image(data.getvalue(), "image/png", name)
        else:
            i = g.image(jpeg(img, 92 if name in ("front", "bald") else 88), "image/jpeg", name)
        g.gltf["textures"].append({"sampler": 0, "source": i})
        material = {"name": name, "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "baseColorTexture": {"index": i},
                                                           "metallicFactor": 0, "roughnessFactor": 0.62}}
        # The scan's winding is unreliable (thin hair sheets, the collar seen from inside): culled back
        # faces were the holes in the neck, the collar and the turtleneck's spots.
        material["doubleSided"] = True
        if name in ("front", "lidskin", "bald", "iris"):
            # The picture already carries its light: shown mostly as it is (emissive), with a little
            # shading on top for depth when the head turns. Fully lit, the scan's uneven normals
            # mottled the face and the turtleneck.
            material["pbrMetallicRoughness"]["baseColorFactor"] = [0.1, 0.1, 0.1, 1]
            material["emissiveTexture"] = {"index": i}
            material["emissiveFactor"] = [0.9, 0.9, 0.9]
            material["pbrMetallicRoughness"]["roughnessFactor"] = 1.0       # no highlights on the baked light
        if name == "core":
            material["pbrMetallicRoughness"]["roughnessFactor"] = 1.0
        if name in ("lidskin", "iris"):
            material["alphaMode"] = "BLEND"
        g.gltf["materials"].append(material)
    # 0 Root, 1 Body (turtleneck), 2 HeadPivot (at the neck), 3 Head.
    g.node("Root", children=[1, 2])
    g.node("Body", g.mesh("Body", prims['body']))
    frames = [eye_frame(work, n, front_depth, surface_normal) for n, _, _ in BLINK_FRAMES]
    frame_materials = []
    for (prim, tex), (n, _, _) in zip(frames, BLINK_FRAMES):
        data = io.BytesIO(); tex.save(data, 'PNG'); i = g.image(data.getvalue(), "image/png", n)
        g.gltf["textures"].append({"sampler": 0, "source": i})
        g.gltf["materials"].append({"name": n, "doubleSided": True, "alphaMode": "BLEND", "emissiveTexture": {"index": i}, "emissiveFactor": [0.9, 0.9, 0.9],
                                    "pbrMetallicRoughness": {"baseColorFactor": [0.1, 0.1, 0.1, 1], "baseColorTexture": {"index": i}, "metallicFactor": 0, "roughnessFactor": 1.0}})
        prim['material'] = len(g.gltf["materials"]) - 1
        prim['pos'] = prim['pos'] - pivot
    # 0 Root, 1 Body, 2 HeadPivot, 3 Head, 4.. the blink frames (shown by blinkFrames).
    g.node("HeadPivot", t=pivot, children=[3] + [4 + k for k in range(len(frames))])
    m_head = g.mesh("Head", prims['head'], TARGETS)
    g.node("Head", m_head)
    for k, (prim, _) in enumerate(frames):
        g.node(f"BlinkFrame{k}", g.mesh(f"BlinkFrame{k}", [prim]))
    clips(g)
    return g.bytes(), dict(m_head=m_head, head_prims=len(prims['head']))

def clips(g):
    pivot = 2
    def rot_clip(name, duration, axis, angle, peaks):
        t, e = wave(duration, peaks)
        g.animation(name, [(pivot, "rotation", t, [quat(axis, angle * v) for v in e])])
    t, e = wave(5.0, 2)
    g.animation("idle_a", [(pivot, "rotation", t, [quat((0, 0, 1), 0.02 * v) for v in e])])
    t, e = wave(6.0, 2)
    g.animation("idle_b", [(pivot, "rotation", t, [quat((0, 1, 0), 0.035 * v) for v in e])])
    rot_clip("nod", 0.9, (1, 0, 0), 0.1, 4)
    rot_clip("shake", 0.9, (0, 1, 0), 0.16, 4)
    rot_clip("lean_in", 1.0, (1, 0, 0), 0.07, 1)
    rot_clip("interrupt", 0.5, (1, 0, 0), -0.05, 1)
    rot_clip("think", 3.0, (0, 0, 1), -0.035, 1)

# ---------------------------------------------------------------- package

VISEMES = ["sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"]
VISEME_MOUTHS = {
    "sil": {}, "PP": {"press": 1}, "FF": {"teeth": 0.7}, "TH": {"teeth": 0.5, "open": 0.15}, "DD": {"open": 0.35},
    "kk": {"open": 0.3}, "CH": {"wide": 0.5, "small_o": 0.3}, "SS": {"wide": 0.6, "teeth": 0.3}, "nn": {"open": 0.22},
    "RR": {"round": 0.45}, "aa": {"open": 0.85}, "E": {"wide": 0.8, "open": 0.2}, "I": {"wide": 0.65}, "O": {"round": 0.85}, "U": {"small_o": 0.9},
}

def package(glb, ids, posters):
    prims = list(range(ids['head_prims']))
    def w(name, v): return {"node": 3, "mesh": ids['m_head'], "primitives": prims, "targetIndex": TARGETS.index(name), "value": v}
    def turn(yaw=0.0, pitch=0.0, roll=0.0):
        q = np.array(quat((0, 1, 0), yaw))
        for axis, a in (((1, 0, 0), pitch), ((0, 0, 1), roll)):
            r = quat(axis, a); x, y, z, ww = q; a2, b2, c2, d2 = r
            q = np.array([ww * a2 + x * d2 + y * c2 - z * b2, ww * b2 - x * c2 + y * d2 + z * a2, ww * c2 + x * b2 - y * a2 + z * d2, ww * d2 - x * a2 - y * b2 - z * c2])
        return {"node": 2, "property": "rotation", "value": [round(float(v), 6) for v in q]}
    poses = {
        "idle": [w("smile", 0.15)],
        "connecting": [turn(yaw=-0.34, roll=0.04), w("look_left", 0.8), w("smile", 0.3)],
        "listening": [w("wide", 0.4), w("brow_inner_up", 0.5), w("smile", 0.3), turn(roll=-0.05)],
        "thinking": [w("look_up", 0.9), w("look_right", 0.6), w("brow_one_up", 1), w("smirk", 0.8), turn(yaw=0.06, roll=0.05)],
        "speaking": [w("brow_raise", 0.25), w("wide", 0.15)],
        "interrupted": [w("wide", 0.9), w("brow_raise", 1), w("small_o", 0.7), turn(pitch=-0.04)],
        "muted": [w("blink", 1), w("smile", 0.45), w("brow_inner_up", 0.2), turn(pitch=0.05, roll=0.04)],
        "reduced_motion": [],
        "expr_neutral": [],
        "expr_happy": [w("smile", 1), w("happy", 0.6), w("brow_raise", 0.2)],
        "expr_curious": [w("brow_one_up", 0.8), w("wide", 0.3), w("small_o", 0.35), turn(roll=0.07)],
        "expr_thinking": [w("look_up", 0.7), w("look_right", 0.5), w("brow_one_up", 0.8), w("smirk", 0.7)],
        "expr_concerned": [w("brow_inner_up", 1), w("brow_frown", 0.3), w("frown", 0.8)],
        "expr_surprised": [w("wide", 1), w("brow_raise", 1), w("round", 0.7)],
        "expr_apologetic": [w("brow_inner_up", 0.8), w("squint", 0.3), w("frown", 0.4), turn(pitch=0.05)],
        "expr_confident": [w("smirk", 0.6), w("smile", 0.5), w("squint", 0.25)],
    }
    for v in VISEMES:
        poses["viseme_" + v] = [w(name, x) for name, x in VISEME_MOUTHS[v].items()]
    bindings = {
        "schemaVersion": "1.0", "renderer": "native3d",
        "animations": {n: n for n in ("idle_a", "idle_b", "nod", "shake", "lean_in", "interrupt", "think")},
        "poses": poses, "parameters": {},
        "channelMasks": {"expression": ["face"], "speech": ["mouthShape"], "idle": ["head"], "reflex": ["face"]},
        "framing": {"cameraPosition": [0, 0.04, 2.3], "lookAt": [0, 0.0, 0], "verticalFov": 22, "safeInset": 0.02, "fit": "contain"},
        "lighting": "portrait", "springs": False,
        "gaze": {"node": 3, "morphs": {"mesh": ids['m_head'], "primitives": prims, **{k: TARGETS.index("look_" + k) for k in ("left", "right", "up", "down")}},
                 "turn": {"node": 2, "factor": 0.45}},
        "blink": [{"node": 3, "mesh": ids['m_head'], "primitives": prims, "targetIndex": TARGETS.index("blink"),
                   "suppressExpressionTargets": [TARGETS.index(n) for n in ("wide", "squint", "happy")]}],
        "stateAnimations": {"thinking": "think"},
        "blinkFrames": [{"node": 4 + k, "from": a, "to": b} for k, (_, a, b) in enumerate(BLINK_FRAMES)],
        "headNode": 2,
        "speechMouthMesh": ids['m_head'],
        "expressionMouthTargets": [TARGETS.index(n) for n in ("smile", "smirk", "frown", "small_o")],
    }
    states = ("idle", "connecting", "listening", "thinking", "speaking", "interrupted", "muted")
    manifest = {
        "schemaVersion": "1.0", "id": "kairo", "displayName": "Kairo", "assetVersion": "2.0.0", "minRuntimeVersion": "1.0.0", "profile": "character",
        "representations": [{"id": "native3d-primary", "renderer": "native3d", "format": "glb", "model": "models/kairo.glb", "bindings": "bindings/native3d.json",
                             "capabilities": ["ambient", "expressions", "gestures", "reducedMotion", "speechArticulation", "gaze", "blink"],
                             "platforms": ["android", "ios", "maccatalyst", "windows"], "idleOwner": "scheduler", "blinkOwner": "scheduler", "gazeOwner": "scheduler"}],
        "states": {s: {"pose": s, "transitionMs": 90 if s == "interrupted" else 260, "inputReactive": s == "listening", "outputReactive": s == "speaking",
                       "gaze": "processing" if s == "thinking" else "engaged"} for s in states},
        "expressions": {e: {"pose": "expr_" + e, "channels": ["face"], "transitionMs": 120 if e == "surprised" else 260}
                        for e in ("neutral", "happy", "curious", "thinking", "concerned", "surprised", "apologetic", "confident")},
        "speech": {"mode": "canonicalVisemes", "canonicalProfile": "oculus15-v1", "mapping": {str(i): "viseme_" + v for i, v in enumerate(VISEMES)},
                   "expressionMouthScale": 0.5, "releaseMs": 90},
        "motions": {"idleVariants": ["idle_a", "idle_b"], "gestures": {"nod": "nod", "shake": "shake", "lean": "lean_in", "interrupt": "interrupt"},
                    "seedable": True, "blinkIntervalSeconds": [2.8, 6.0]},
        "themes": {"slots": {}},
        "reducedMotion": {"pose": "reduced_motion", "retainSpeech": True, "transitionMs": 180},
        "posters": {"light": "previews/poster-light.png", "dark": "previews/poster-dark.png"},
        "license": "LICENSE.txt", "provenance": "provenance.json",
    }
    files = {
        "models/kairo.glb": glb, "bindings/native3d.json": json.dumps(bindings, indent=1).encode(),
        "previews/poster-light.png": open(posters[0], 'rb').read() if posters else tiny_png(),
        "previews/poster-dark.png": open(posters[1], 'rb').read() if posters else tiny_png(),
        "LICENSE.txt": b"Kairo: the owner's character images, reconstructed with Pixal3D (MIT code and flow weights; DINOv3 encoder under the DINOv3 License) and rigged by source/kairo_scan.py.\n",
        "provenance.json": json.dumps({"model": "Pixal3D single view (pixal3d.cpp Q8_0) from the owner's front image, seed 42; textured from the image; rigged by source/kairo_scan.py",
                                       "posters": "captured from the lab" if posters else "placeholder 1x1, not a render"}, indent=1).encode(),
        "source/kairo_scan.py": open(__file__, 'rb').read(),
    }
    manifest["files"] = [{"path": p, "sha256": hashlib.sha256(b).hexdigest(), "bytes": len(b)} for p, b in sorted(files.items())]
    out = io.BytesIO()
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr("avatar.json", json.dumps(manifest, indent=1))
        for p, b in sorted(files.items()): z.writestr(p, b)
    return out.getvalue()

if __name__ == "__main__":
    work, out_dir, posters = sys.argv[1], sys.argv[2], sys.argv[3:5]
    glb, ids = build(work)
    data = package(glb, ids, posters)
    path = os.path.join(out_dir, "kairo.spineavatar")
    open(path, 'wb').write(data)
    print(path, len(data), "bytes; glb", len(glb))
