"""Builds Kairo from a Pixal3D scan of the owner's front image, for the Spine Avatar Lab (#524).

Usage: python kairo_scan.py <work-dir> <out-dir> [poster-light.png poster-dark.png]

<work-dir> holds what the generator produced from the front image:
  kairo-front.png               the source image (1254 px, the owner's front view)
  kairo-s42.glb                 Pixal3D single-view output (pixal3d.cpp, --webp off)
  kairo-s42_base.png            its texture atlas
  kairo-s42.svviews/input.png   the generator's cropped, matted input (camera space)
Needs numpy, scipy, Pillow, trimesh, pymeshlab and scikit-image.

Pipeline:
 1. Weld the scan, turn it to face +Z and decimate it, keeping the eyes, brows and mouth at full
    density (their morph targets move texture, which needs vertices).
 2. Drop the hidden inner shell the generator leaves (faces never seen from 96 directions) and orient
    the rest outward.
 3. Texture: faces seen from the front camera map straight into the source image (the generator is
    pixel-aligned, so the face is the picture itself); the rest get a per-triangle atlas coloured
    from the generator's own texture. A closed shell just inside the hair fills the gaps between
    strands.
 4. Morph targets as displacement fields drawn in image space and lifted back onto the surface:
    blink, wide, squint and happy lids, eyes looking around, brows, and mouth shapes for
    expressions and the 15 visemes. Written as sparse accessors.
 5. The head and the turtleneck are separate nodes; the head turns about the neck.
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
# Lid margins measured on the source (x, y): the upper lid's edge where lashes meet the white, and
# the lower lid. The lash band sits above the upper margin; the lid fold above that.
LIDS = [dict(ux=[435, 460, 485, 510, 535, 552, 562], uy=[520, 495, 486, 484, 489, 500, 540],
             lx=[435, 460, 490, 520, 550, 562], ly=[522, 540, 550, 553, 550, 542]),
        dict(ux=[698, 715, 740, 765, 790, 815, 828], uy=[542, 505, 489, 484, 486, 497, 515],
             lx=[698, 720, 750, 780, 810, 828], ly=[546, 552, 554, 550, 538, 518])]
LASH, FOLD = 25.0, 48.0
SRC_W = 1254.0
BROWS = [dict(x0=470.0, x1=600.0, y=425.0, inner=600.0), dict(x0=655.0, x1=830.0, y=423.0, inner=655.0)]
MOUTH = dict(cx=626.0, y=743.0, left=550.0, right=702.0, top=712.0, bottom=782.0)
COLLAR_Y = 868.0
CHIN_Y = 864.0              # source y of the chin's lower edge; the jaw line rises from it            # source y where the neck enters the turtleneck, at the centre
FEATURES = [(507.5, 510.0, 105.0, 80.0), (747.5, 508.0, 105.0, 80.0), (626.0, 748.0, 120.0, 75.0)]

# ---------------------------------------------------------------- stage 1: weld, turn, decimate

def load_scan(work):
    import trimesh, pymeshlab
    g = list(trimesh.load(f'{work}/kairo-s42.glb').geometry.values())[0]
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
    ms.meshing_decimation_quadric_edge_collapse(targetfacenum=110000, qualitythr=0.4, preservenormal=True,
                                                optimalplacement=True, planarquadric=True, selected=True)
    m = ms.current_mesh()
    print('decimated', m.face_number(), 'faces; features kept', keep.sum())
    return m.vertex_matrix(), m.face_matrix(), orig

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

def outer_surface(P, F, R=640, n=96):
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
        vis = (depth <= zb[v, u] + 0.004).reshape(nf, nb).mean(1) > 0.5
        seen += vis; dir_sum[vis] += d
    fn = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]])
    flip = (fn * dir_sum).sum(1) < 0
    F2 = F.copy(); F2[flip] = F2[flip][:, ::-1]; F2 = F2[seen > 0]
    used = np.unique(F2); remap = np.full(len(P), -1); remap[used] = np.arange(len(used))
    print('outer faces', len(F2), 'of', nf)
    return P[used], remap[F2]

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
    oval = (((xx - 626) / 205) ** 2 + ((yy - 640) / 268) ** 2 < 1) & (yy < jaw)
    hair = (a[..., 2] > a[..., 0] + 12) | (a.sum(-1) < 150)
    hair = ndimage.binary_dilation(ndimage.binary_opening(hair, iterations=2), iterations=4)
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

SURFACE_SMOOTHING = 2.0
GRID_NORMALS = []
FACE_INFO = []

def face_surface(P, F, src):
    """Replace the scan's face with one dense, smooth height field over the face, at the scan's own
    depth (smoothed, with the eye sockets filled). The generator's eyes are hollows and its face is
    crumpled; the picture is the face, so a smooth surface under it reads true, lids slide cleanly
    over it, and no seams are left round the eyes. Hair and the sides keep the scan."""
    mask_src = face_mask_src(src)
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
    # Crop-space mask of the face.
    srcx = ((xx - OX) / S).astype(int).clip(0, mask_src.shape[1] - 1); srcy = ((yy - OY) / S).astype(int).clip(0, mask_src.shape[0] - 1)
    face = mask_src[srcy, srcx]
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
    near_face = inner[vi2[:, 1], vi2[:, 0]] & (vz < zs[vi2[:, 1], vi2[:, 0]] + 0.12)
    over = near_face[F].any(1)
    F = F[~over]
    # The grid, a hair in front of where the scan was.
    step = 1.6
    gx = np.arange(0, N, step); gy = np.arange(0, N, step)
    GX, GY = np.meshgrid(gx, gy)
    ins = face[GY.astype(int), GX.astype(int)]
    vid = np.full(GX.shape, -1); vid[ins] = np.arange(ins.sum()) + len(P)
    px = np.stack([GX[ins], GY[ins]], 1)
    # Sampled bilinearly: the grid step (1.6 px) against whole-pixel lookups quilted the surface.
    gp = unproject(px, ndimage.map_coordinates(zs, [px[:, 1], px[:, 0]], order=1) - 0.004)
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
    return np.concatenate([P, gp]), np.concatenate([F, gf]).astype(np.int32), len(P)

def vertex_normals(P, F):
    fn = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]])
    vn = np.zeros_like(P)
    for k in range(3): np.add.at(vn, F[:, k], fn)
    return vn / (np.linalg.norm(vn, axis=1, keepdims=True) + 1e-12)

def underlay(P, F):
    import pymeshlab
    from skimage import measure
    lo, hi = P.min(0) - 0.03, P.max(0) + 0.03; res = 0.006
    shape = np.ceil((hi - lo) / res).astype(int) + 1
    rng = np.random.default_rng(0)
    pts = np.concatenate([np.einsum('k,fkd->fd', w, P[F]) for w in rng.dirichlet((1, 1, 1), size=24)] + [P])
    idx = np.clip(((pts - lo) / res).astype(int), 0, shape - 1)
    occ = np.zeros(shape, bool); occ[idx[:, 0], idx[:, 1], idx[:, 2]] = True
    occ = ndimage.binary_closing(occ, structure=ndimage.generate_binary_structure(3, 1), iterations=4)
    occ = ndimage.binary_erosion(ndimage.binary_fill_holes(occ), iterations=2)
    V, Fu, _, _ = measure.marching_cubes(ndimage.gaussian_filter(occ.astype(float), 1.2), 0.5)
    ms = pymeshlab.MeshSet(); ms.add_mesh(pymeshlab.Mesh(V * res + lo, Fu[:, ::-1].astype(np.int32)))
    ms.meshing_decimation_quadric_edge_collapse(targetfacenum=30000, preservenormal=True)
    m = ms.current_mesh(); UP, UF = m.vertex_matrix(), m.face_matrix()
    # Only the hair needs it: across the face it came up to the filled eye sockets and showed through.
    fc, _ = project(UP[UF].mean(1))
    face = src_to_crop((626, 640)); r = np.array([175, 250]) * S
    in_face = (((fc - face) / r) ** 2).sum(1) < 1
    UF = UF[~(in_face & (UP[UF].mean(1)[:, 2] > 0))]
    un = np.cross(UP[UF[:, 1]] - UP[UF[:, 0]], UP[UF[:, 2]] - UP[UF[:, 0]])
    if (un * (UP[UF].mean(1) - UP.mean(0))).sum(1).mean() < 0:
        UF = UF[:, ::-1]
    return UP, UF

# ---------------------------------------------------------------- stage 3: textures

def atlas(P, F, sample, size, darken=1.0):
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
        tex[py.reshape(-1), px.reshape(-1)] = (sample(pts.reshape(-1, 3)) * darken).astype(np.uint8)
    return uv, tex

def front_faces(P, F, work):
    inp, zc = project(P)
    zb_at, zbuf = front_zbuffer(P, F)
    vi = np.clip(inp.astype(int), 0, N - 1)
    visible = zc <= zb_at(inp) + 0.02
    alpha = np.asarray(Image.open(f'{work}/kairo-s42.svviews/input.png').convert('RGBA'))[..., 3]
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
            continue                                # the lid overlay does these
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
        elif kind.startswith('look_'):
            # The iris slides inside the opening; the white compresses on the side it moves to.
            r = np.sqrt(((x - e['cx']) / e['half']) ** 2 + ((y - (yu + yl) / 2) / np.maximum((yl - yu) / 2, 1e-3)) ** 2)
            w = 1 - smooth(0.55, 0.98, r)
            amount = {'look_left': (-11, 0), 'look_right': (11, 0), 'look_up': (0, -6), 'look_down': (0, 5)}[kind]
            d += np.stack([w * amount[0], w * amount[1]], 1) * ((y > yu - 2) & (y < yl + 2))[:, None]
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

def lid_skin_texture(src):
    """A small texture of clean lid skin for the closing lids: the skin under each eye (no lashes,
    no brow hairs), a touch darker toward the crease and the corners."""
    a = np.asarray(src).astype(float)
    tiles = []
    for lid in LIDS:
        cx = (lid['ux'][0] + lid['ux'][-1]) / 2
        y = max(lid['ly']) + 26
        patch = a[int(y) - 6:int(y) + 6, int(cx) - 30:int(cx) + 30].reshape(-1, 3)
        tiles.append(np.median(patch, axis=0))
    tex = np.zeros((32, 64, 4))
    for i, c in enumerate(tiles):
        v = np.linspace(0, 1, 32)[:, None]                 # 0 at the crease, 1 at the lash line
        u = np.abs(np.linspace(-1, 1, 32))[None, :]
        # A soft shadow in the crease and toward the lashes, as a closed lid has.
        shade = (0.9 + 0.06 * np.sin(np.pi * v) ** 0.5 - 0.05 * v ** 4) * (1 - 0.04 * u ** 2)
        tex[:, i * 32:(i + 1) * 32, :3] = c[None, None, :] * shade[..., None]
        # Faded in from the crease, so the lid has no edge against the face above it.
        tex[:, i * 32:(i + 1) * 32, 3] = 255 * np.clip(v / 0.35, 0, 1) ** 1.5 * np.ones_like(u)
    return Image.fromarray(np.clip(tex, 0, 255).astype(np.uint8), 'RGBA')

def lid_overlays(front_depth, surface_normal):
    """Upper lids as their own surface: at rest folded flat into the lid crease, so the picture's own
    eyes and lashes show untouched; closing unfolds it down over the eye as clean lid skin (taken from
    just above the crease) with a copy of the lash line along its edge. Stretching the picture's own
    lid instead streaked the long lashes and eyeliner across the skin."""
    cols, skin_rows, lash_rows = 48, 10, 4
    LASH_LINE = 11.0                     # the dense root of the lashes and the liner above the margin
    pos, uv, idx_skin, idx_lash, targets = [], [], [], [], {n: [] for n in TARGETS}
    for lid in LIDS:
        # A little past both corners, so the closed lid has no edge at the corners.
        x = np.linspace(lid['ux'][0] - 6, lid['ux'][-1] + 6, cols)
        margin = np.interp(x, lid['ux'], lid['uy']) + 2.0
        lower = np.interp(x, lid['lx'], lid['ly'])
        # The crease follows the margin but flattens toward the corners, as a lid fold does.
        t = (x - x.mean()) / (x[-1] - x[0]) * 2
        fold = margin - FOLD * (1 - 0.35 * t * t)
        rows = skin_rows + lash_rows + 1
        # Texture: skin rows from a clean band above the crease, lash rows from the lash line.
        fs = np.linspace(0, 1, skin_rows)
        fl = np.linspace(0, 1, lash_rows + 1)
        eye = LIDS.index(lid)
        su = (eye + 0.06 + 0.88 * (x - x[0]) / (x[-1] - x[0])) / 2           # into the lid-skin texture
        skin_uv = np.stack([np.broadcast_to(su, (skin_rows, cols)), np.broadcast_to(0.04 + 0.92 * fs[:, None], (skin_rows, cols))], -1).reshape(-1, 2)
        lash_y = (margin - LASH_LINE - 1)[None, :] + LASH_LINE * fl[:, None]
        lash_uv = np.stack([np.broadcast_to(x, lash_y.shape), lash_y], -1).reshape(-1, 2) / np.array([SRC_W, SRC_W])
        tex = np.concatenate([skin_uv, lash_uv])
        def shape(edge):
            """The lid down to `edge`: skin from the crease to the lash line, then the lash rows."""
            top = fold
            lash_top = edge - LASH_LINE
            ys = np.concatenate([top[None, :] + (lash_top - top)[None, :] * fs[:, None], lash_top[None, :] + (LASH_LINE + 1) * fl[:, None]])
            return np.stack([np.broadcast_to(x, ys.shape), ys], -1).reshape(-1, 2)
        # Rest: every row on the crease, a line with no area.
        rest = np.stack([np.broadcast_to(x, (rows, cols)), np.broadcast_to(fold, (rows, cols))], -1).reshape(-1, 2)
        shapes = {'blink': shape(lower + 3.5), 'squint': shape(margin + (lower - margin) * 0.38)}
        base = sum(len(p) for p in pos)
        crop = src_to_crop(rest)
        p0 = unproject(crop, front_depth(crop) - 0.003)
        pos.append(p0); uv.append(tex)
        for name in TARGETS:
            if name in shapes:
                c2 = src_to_crop(shapes[name])
                targets[name].append(unproject(c2, front_depth(c2) - 0.004) - p0)
            else:
                targets[name].append(np.zeros((len(rest), 3)))
        # Skin rows and lash rows are separate grids (their textures differ); the skin's last row and
        # the lash grid's first row lie on the same line.
        for r in range(skin_rows - 1):
            for c in range(cols - 1):
                a = base + r * cols + c
                idx_skin.append([a, a + cols, a + 1, a + 1, a + cols, a + cols + 1])
        for r in range(lash_rows):
            for c in range(cols - 1):
                a = base + (skin_rows + r) * cols + c
                idx_lash.append([a, a + cols, a + 1, a + 1, a + cols, a + cols + 1])
    P = np.concatenate(pos); UV = np.concatenate(uv)
    T = [np.concatenate(targets[n]) for n in TARGETS]
    blink = T[TARGETS.index('blink')]
    NRM = surface_normal(P + blink)
    out = []
    for faces, material in ((np.array(idx_skin).reshape(-1), 3), (np.array(idx_lash).reshape(-1), 0)):
        used = np.unique(faces); remap = np.full(len(P), -1); remap[used] = np.arange(len(used))
        out.append(dict(pos=P[used], nrm=NRM[used], uv=UV[used], idx=remap[faces].astype(np.uint32), material=material,
                        targets=[t[used] for t in T]))
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

def jpeg(img, quality=90):
    out = io.BytesIO(); img.save(out, 'JPEG', quality=quality); return out.getvalue()

# ---------------------------------------------------------------- build

def build(work):
    P, F, (op, ouv) = load_scan(work)
    P, F = outer_surface(P, F)
    P, F, grid_start = face_surface(P, F, Image.open(f'{work}/kairo-front.png').convert('RGB'))
    UP, UF = underlay(P, F)
    # Nothing of the underlayer just behind the face: it rose to the face's surface in patches.
    uc, uz = project(UP[UF].mean(1)); ui = np.clip(uc.astype(int), 0, N - 1)
    face_depth, face_area = FACE_INFO
    over_face = face_area[ui[:, 1], ui[:, 0]] & (uz < face_depth[ui[:, 1], ui[:, 0]] + 0.05)
    UF = UF[~over_face]
    base = np.asarray(Image.open(f'{work}/kairo-s42_base.png').convert('RGB'))
    src = Image.open(f'{work}/kairo-front.png').convert('RGB')
    tree = cKDTree(op)
    def sample(points):
        _, i = tree.query(points, k=1); u = ouv[i]
        return base[np.clip(((1 - u[:, 1]) * base.shape[0]).astype(int), 0, base.shape[0] - 1),
                    np.clip((u[:, 0] * base.shape[1]).astype(int), 0, base.shape[1] - 1)]

    front, visible, zbuf = front_faces(P, F, work)
    # The face grid is the picture by construction: the visibility test missed scattered grid faces,
    # which then took the generator's colours as spots.
    on_grid = (F >= grid_start).all(1)
    front |= on_grid
    visible[grid_start:] = True
    # The front-most surface depth, holes filled and a little spread so a lid sits over the eyeball.
    zfill = np.where(np.isinf(zbuf), np.nan, zbuf)
    idxn = ndimage.distance_transform_edt(np.isnan(zfill), return_distances=False, return_indices=True)
    zfill = ndimage.minimum_filter(zfill[idxn[0], idxn[1]], size=5)
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
    rest = np.where(~front)[0]
    rest_uv, rest_tex = atlas(P, F[rest], sample, 4096)
    UVN = vertex_normals(UP, UF)
    under_uv, under_tex = atlas(UP, UF, sample, 2048, darken=0.72)
    print('front faces', front.sum(), 'rest', len(rest), 'under', len(UF))

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

    # Head and turtleneck: faces above the collar line go with the head.
    collar_px = src_to_crop((MOUTH['cx'], COLLAR_Y))
    near = np.argsort(((inp - collar_px) ** 2).sum(1) + (~visible) * 1e9)[:20]
    collar_y = P[near, 1].mean()
    neck = P[(np.abs(P[:, 1] - collar_y) < 0.02) & (np.abs(P[:, 0]) < 0.12)]
    pivot = np.array([0.0, collar_y, neck[:, 2].mean() if len(neck) else 0.0])
    print('collar y', round(collar_y, 3), 'pivot', np.round(pivot, 3))

    prims = {'head': [], 'body': []}
    fpos, fuv, fnrm, fvis = P, uv_front, VN, visible.astype(float)
    groups = [(F[front], fpos, fuv, fnrm, fvis, 0, True),
              (np.arange(len(rest) * 3).reshape(-1, 3), P[F[rest]].reshape(-1, 3), rest_uv.reshape(-1, 2), VN[F[rest]].reshape(-1, 3),
               visible[F[rest]].reshape(-1).astype(float), 1, True),
              (np.arange(len(UF) * 3).reshape(-1, 3), UP[UF].reshape(-1, 3), under_uv.reshape(-1, 2), UVN[UF].reshape(-1, 3),
               np.zeros(len(UF) * 3), 2, False)]
    for faces, pos, uv, nrm, vis, material, morph in groups:
        is_head = pos[faces].mean(1)[:, 1] > collar_y
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
    for lid in lid_overlays(front_depth, surface_normal):
        lid['pos'] = lid['pos'] - pivot
        prims['head'].append(lid)

    g = Glb()
    g.gltf["samplers"].append({"magFilter": 9729, "minFilter": 9987, "wrapS": 33071, "wrapT": 33071})
    for name, img in (("front", src), ("rest", Image.fromarray(rest_tex)), ("under", Image.fromarray(under_tex)), ("lidskin", lid_skin_texture(src))):
        if img.mode == 'RGBA':
            data = io.BytesIO(); img.save(data, 'PNG'); i = g.image(data.getvalue(), "image/png", name)
        else:
            i = g.image(jpeg(img, 92 if name == "front" else 88), "image/jpeg", name)
        g.gltf["textures"].append({"sampler": 0, "source": i})
        material = {"name": name, "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "baseColorTexture": {"index": i},
                                                           "metallicFactor": 0, "roughnessFactor": 0.62}}
        if name in ("front", "lidskin"):
            # The picture already carries its light: shown mostly as it is (emissive), with a little
            # shading on top for depth when the head turns. Fully lit, the scan's uneven normals
            # mottled the face and the turtleneck.
            material["pbrMetallicRoughness"]["baseColorFactor"] = [0.1, 0.1, 0.1, 1]
            material["emissiveTexture"] = {"index": i}
            material["emissiveFactor"] = [0.9, 0.9, 0.9]
            material["pbrMetallicRoughness"]["roughnessFactor"] = 1.0       # no highlights on the baked light
        if name == "lidskin":
            material["alphaMode"] = "BLEND"
        g.gltf["materials"].append(material)
    # 0 Root, 1 Body (turtleneck), 2 HeadPivot (at the neck), 3 Head.
    g.node("Root", children=[1, 2])
    g.node("Body", g.mesh("Body", prims['body']))
    g.node("HeadPivot", t=pivot, children=[3])
    m_head = g.mesh("Head", prims['head'], TARGETS)
    g.node("Head", m_head)
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
