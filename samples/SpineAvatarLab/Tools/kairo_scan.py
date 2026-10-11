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
    # Wider and taller above the cheekbones, narrowing smoothly below: a step in the width at one
    # height left a notch in the grid's side, a patch on the cheek once the head turned.
    t = smooth(720.0, 520.0, yy.astype(float))
    oval = (((xx - 626) / (205 + 30 * t)) ** 2 + ((yy - 640) / np.where(yy < 640, 390, 268)) ** 2 < 1) & (yy < jaw)
    hair = (a[..., 2] > a[..., 0] + 12) | (a.sum(-1) < 150)
    # Strands thinner than ~10 px stay on the face: cut out, they left a crack across the forehead.
    hair = ndimage.binary_dilation(ndimage.binary_opening(hair, iterations=5), iterations=4)
    # Not past the subject's outline: the oval reached the background beside the cheeks, and the
    # grid's edge there carried the backdrop's grey, a pale patch on the cheek once the head turned.
    corner = np.median(np.concatenate([a[:20, :20].reshape(-1, 3), a[:20, -20:].reshape(-1, 3)]), 0)
    backdrop = np.abs(a - corner).max(-1) < 14
    lab_, _ = ndimage.label(backdrop)
    edge_labels = np.unique(np.concatenate([lab_[0], lab_[-1], lab_[:, 0], lab_[:, -1]]))
    backdrop = np.isin(lab_, edge_labels[edge_labels > 0])
    m = oval & ~hair & ~ndimage.binary_dilation(backdrop, iterations=8)
    # Keep the eyes, brows and mouth whole even where they read as dark or blue.
    for x, y, rx, ry in FEATURES:
        m |= ((xx - x) / rx) ** 2 + ((yy - y) / ry) ** 2 < 1
    m &= oval
    m = ndimage.binary_opening(ndimage.binary_fill_holes(m), iterations=3)
    # Corners rounded: where the oval met the jaw line, the grid's sharp corner left a notch at the
    # cheek that showed as a hole once the head turned.
    m = ndimage.gaussian_filter(m.astype(float), 10) > 0.5
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
        # The opening's own corners: reaching past the outer one (for the old lid surfaces), the hole
        # put the photo's lash wing on the eyeball behind it, which stuck out past the face on a turn.
        x0, x1 = lid['ux'][0] - 2 - grow_x, lid['ux'][-1] + 2 + grow_x
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

def eye_sockets():
    """A wall from the face's edge round each eye hole in to the eyeball, coloured by the photo at the
    edge (liner and lashes above, skin below): the lid's own thickness. Without it the gap between
    the hole's edge and the ball let the eye see into the head (or out through the nose's root)."""
    a, b, pa, pb = EYE_RIM
    centres = np.array([c for c, _ in EYE_BALLS]); radii = np.array([r for _, r in EYE_BALLS])
    k = np.argmin(((a[:, None, :] - centres[None]) ** 2).sum(-1), 1)
    def onto(p):
        d = p - centres[k]; d /= np.linalg.norm(d, axis=1, keepdims=True)
        return centres[k] + d * (radii[k] * 0.97)[:, None]
    ta, tb = onto(a), onto(b)
    n = len(a)
    pos = np.concatenate([a, b, ta, tb])
    # Shaded skin, lighter at the face's edge and darker in toward the ball (the photo's edge pixels,
    # stretched inward, streaked the inner corners).
    uv = np.concatenate([np.tile([0.5, 0.05], (2 * n, 1)), np.tile([0.5, 0.95], (2 * n, 1))])
    i = np.arange(n)
    idx = np.stack([i, i + n, i + 2 * n, i + n, i + 3 * n, i + 2 * n], 1).reshape(-1)
    nrm = np.concatenate([centres[k] - a, centres[k] - b, centres[k] - ta, centres[k] - tb])
    nrm = -nrm / (np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-12)
    print('eye sockets', n, 'edges')
    return dict(pos=pos, nrm=nrm, uv=uv, idx=idx.astype(np.uint32), material=SOCKET_MATERIAL, targets=[np.zeros_like(pos) for _ in TARGETS])

def eyeball_texture(src):
    """The whites for the eyeballs: the photo's sclera (irises painted out) inside the openings,
    spread outward so the parts of the ball seen past the openings on a turn are white, not lid."""
    a = np.asarray(sclera_photo(src)).astype(float)
    # Only the clear white: nearer the lids the opening holds lid skin and lashes, which spread pink
    # over the ball.
    # (with the photo's own shading: the lid's shadow over the top of the white)
    white = ndimage.binary_erosion(eye_openings(a.shape[:2], 0.0), iterations=2) & (a.mean(-1) > 120) & (a[..., 0] + 10 > a[..., 2])
    _, (iy, ix) = ndimage.distance_transform_edt(~white, return_indices=True)
    out = a[iy, ix]
    far = ndimage.distance_transform_edt(~white) > 10
    out[far] = np.percentile(a[white], 30, axis=0)
    return Image.fromarray(ndimage.gaussian_filter(out, (2, 2, 0)).astype(np.uint8))

SOCKET_MATERIAL = 11          # index of the "socket" material
EYEBALL_MATERIAL = 10         # index of the "eyeball" material in build's list
EYE_RADIUS = 1.35              # eyeball radius over the opening's half-width
LOOK_DEG = {'look_left': (-6.0, 0.0), 'look_right': (6.0, 0.0), 'look_up': (0.0, 4.5), 'look_down': (0.0, -4.0)}   # the ball is large: 12 degrees threw the iris to the corner

def eye_parts(zg, surface_normal):
    """A whole eyeball sphere behind each opening, its front just behind the face's surface there, and an
    iris lying on it. Gaze turns the iris about the ball's centre, as a real eye turns. (A flat
    sheet behind the opening read as a hollow from the side.)"""
    cam = np.array([0.0, 0.0, D])
    def depth_at(src_px):
        c = src_to_crop(src_px)
        return c, ndimage.map_coordinates(zg, [c[:, 1], c[:, 0]], order=1)
    out_ball, out_iris = [], []
    EYE_BALLS.clear()
    for i, ((cx, cy), lid) in enumerate(zip(IRISES, LIDS)):
        c0, z0 = depth_at(np.array([[cx, cy]]))
        front = unproject(c0, z0)[0]
        half = (lid['ux'][-1] - lid['ux'][0]) / 2
        ends = unproject(src_to_crop(np.array([[cx - half, cy], [cx + half, cy]])), np.repeat(z0, 2))
        R = np.linalg.norm(ends[1] - ends[0]) / 2 * EYE_RADIUS
        toward = (cam - front) / np.linalg.norm(cam - front)
        # The ball's front 1 mm behind the face at the iris, as in the picture (fitted to the opening's
        # edge it stood a centimetre in front of the face, the eyes bulging and the iris off its place
        # whenever the camera moved).
        centre = (front + toward * -0.001) - toward * R
        EYE_BALLS.append((centre, R))
        # The ball: a cap of the sphere facing the camera, out to 95 degrees.
        up = np.array([0.0, 1.0, 0.0]); right = np.cross(up, toward); right /= np.linalg.norm(right); up = np.cross(toward, right)
        th = np.linspace(0, np.radians(178), 60); ph = np.linspace(0, 2 * math.pi, 96, endpoint=False)
        T_, P_ = np.meshgrid(th, ph, indexing='ij')
        d = (np.cos(T_)[..., None] * toward + np.sin(T_)[..., None] * (np.cos(P_)[..., None] * right + np.sin(P_)[..., None] * up)).reshape(-1, 3)
        v = centre + R * d
        rows, cols = len(th), len(ph)
        idx = []
        for r in range(rows - 1):
            for k in range(cols):
                a0 = r * cols + k; a1 = r * cols + (k + 1) % cols
                idx += [a0, a0 + cols, a1, a1, a0 + cols, a1 + cols]
        px, _ = project(v); uv = ((px - np.array([OX, OY])) / S) / SRC_W
        out_ball.append((v, d, uv, np.array(idx)))
        # The iris: a disc projected onto the ball along the camera's rays, lifted 0.3 mm.
        rings, segs = 12, 64
        r_ = np.linspace(0, IRIS_R + 2, rings + 1)[1:]; a_ = np.linspace(0, 2 * math.pi, segs, endpoint=False)
        disc = np.concatenate([[[cx, cy]], np.stack([cx + np.outer(r_, np.cos(a_)).ravel(), cy + np.outer(r_, np.sin(a_)).ravel()], 1)])
        cp = src_to_crop(disc)
        ray = unproject(cp, np.ones(len(cp))) - cam; ray /= np.linalg.norm(ray, axis=1, keepdims=True)
        oc = cam - centre; bq = (ray * oc).sum(1); cq = (oc * oc).sum() - R * R
        t = -bq - np.sqrt(np.maximum(bq * bq - cq, 0))
        q = cam + ray * t[:, None]
        n = (q - centre) / R
        q = q + n * 0.0003
        tri = []
        for s_ in range(segs):
            tri += [0, 1 + (s_ + 1) % segs, 1 + s_]
        for k in range(rings - 1):
            for s_ in range(segs):
                a0 = 1 + k * segs + s_; a1 = 1 + k * segs + (s_ + 1) % segs
                tri += [a0, a1 + segs, a0 + segs, a0, a1, a1 + segs]
        iuv = np.stack([(i * IRIS_TILE + IRIS_TILE / 2 + (disc[:, 0] - cx) * (IRIS_TILE / 2) / IRIS_SPAN) / (2 * IRIS_TILE),
                        (IRIS_TILE / 2 + (disc[:, 1] - cy) * (IRIS_TILE / 2) / IRIS_SPAN) / IRIS_TILE], 1)
        targets = []
        for name in TARGETS:
            if name in LOOK_DEG:
                yaw, pitch = np.radians(LOOK_DEG[name])
                # Turn about the ball's centre: yaw about up, pitch about right (positive = up).
                def rot(axis, ang, x):
                    axis = axis / np.linalg.norm(axis)
                    return x * math.cos(ang) + np.cross(axis, x) * math.sin(ang) + axis * (x @ axis)[:, None] * (1 - math.cos(ang))
                rel = rot(up, yaw, q - centre); rel = rot(right, -pitch, rel)
                targets.append(centre + rel - q)
            else:
                targets.append(np.zeros_like(q))
        out_iris.append((q, n, iuv, np.array(tri), targets))
    def merge(parts):
        pos, nrm, uv, idx, base = [], [], [], [], 0
        for p_, n_, u_, i_, *_ in parts:
            pos.append(p_); nrm.append(n_); uv.append(u_); idx.append(i_ + base); base += len(p_)
        return np.concatenate(pos), np.concatenate(nrm), np.concatenate(uv), np.concatenate(idx).astype(np.uint32)
    bp, bn, bu, bi = merge(out_ball)
    ip, in_, iu, ii = merge(out_iris)
    it = [np.concatenate([t[4][k] for t in out_iris]) for k in range(len(TARGETS))]
    return [dict(pos=bp, nrm=bn, uv=bu, idx=bi, material=EYEBALL_MATERIAL, targets=[np.zeros_like(bp) for _ in TARGETS]),
            dict(pos=ip, nrm=in_, uv=iu, idx=ii, material=5, targets=it)]

SURFACE_SMOOTHING = 2.0
DROP_BEHIND = 0.025         # scan faces this far behind the face grid go; deeper ones (under the jaw) stay
GRID_NORMALS = []
FACE_INFO = []

EYE_RIM = []                  # the face grid's edges round the eye holes (positions and crop px of both ends)
EYE_BALLS = []                # (centre, radius) per eye, set by eye_parts
FACE_MASK = []                # the face's skin (source px), set by face_surface

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
    FACE_MASK[:] = [face_mask_src(src)]
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
    edge = np.clip(ndimage.distance_transform_edt(face) / 45.0, 0, 1)    # 45 crop px: at the temples the fitted surface bulged out past the skull
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
    # The grid's edge round the eye holes, for the sockets (eye_sockets).
    local0 = gf - len(P)
    e0 = np.concatenate([local0[:, [0, 1]], local0[:, [1, 2]], local0[:, [2, 0]]])
    _, inv0, cnt0 = np.unique(np.sort(e0, 1), axis=0, return_inverse=True, return_counts=True)
    b0 = e0[cnt0[inv0.reshape(-1)] == 1]
    eye_near = src_mask_to_crop(ndimage.binary_dilation(eye_holes((int(SRC_W), int(SRC_W)), 0.0, 0.0), iterations=10))
    vi0 = np.clip(px.astype(int), 0, N - 1)
    near_v = eye_near[vi0[:, 1], vi0[:, 0]]
    b0 = b0[near_v[b0[:, 0]] & near_v[b0[:, 1]]]
    # The hole's edge on the lid curves exactly: on the grid's steps it was a jagged lid line.
    rim_v = np.unique(b0)
    sxy = (px[rim_v] - np.array([OX, OY])) / S
    for k, ((icx, icy), lid) in enumerate(zip(IRISES, LIDS)):
        mine = np.abs(sxy[:, 0] - icx) < 110
        if not mine.any():
            continue
        x = sxy[mine, 0]
        up = np.interp(x, lid['ux'], lid['uy']) + 0.5; lo = np.interp(x, lid['lx'], lid['ly']) + 1.5
        y = np.where(sxy[mine, 1] < (up + lo) / 2, up, lo)
        inside_x = (x > lid['ux'][0] - 1) & (x < lid['ux'][-1] + 1)
        sxy[np.where(mine)[0][inside_x], 1] = y[inside_x]
    new_px = src_to_crop(sxy)
    px[rim_v] = new_px
    gp[rim_v] = unproject(new_px, ndimage.map_coordinates(zg, [new_px[:, 1], new_px[:, 0]], order=1))
    EYE_RIM[:] = [gp[b0[:, 0]], gp[b0[:, 1]], px[b0[:, 0]], px[b0[:, 1]]]
    # A skirt round the grid's sides, folding 1.5 cm back into the head: seen from the side, the slit
    # between the grid's edge and the scan behind it showed through the cheek. Not along the jaw
    # (there it read as a pale band under the chin) nor round the eyes (the eyeballs fill those).
    local = gf - len(P)
    edges = np.concatenate([local[:, [0, 1]], local[:, [1, 2]], local[:, [2, 0]]])
    _, inv, count = np.unique(np.sort(edges, 1), axis=0, return_inverse=True, return_counts=True)
    border = edges[count[inv.reshape(-1)] == 1]
    sxy = (px - np.array([OX, OY])) / S
    jaw_line = CHIN_Y - 0.0042 * (sxy[:, 0] - 626.0) ** 2
    keep_v = (sxy[:, 1] < jaw_line - 50) & ~src_mask_to_crop(eye_holes((int(SRC_W), int(SRC_W)), -8.0, 8.0))[np.clip(px[:, 1].astype(int), 0, N - 1), np.clip(px[:, 0].astype(int), 0, N - 1)]
    border = border[keep_v[border[:, 0]] & keep_v[border[:, 1]]]
    rim = np.unique(border)
    back = np.full(len(gp), -1); back[rim] = len(gp) + np.arange(len(rim))
    _, gz = project(gp[rim])
    gp = np.concatenate([gp, unproject(px[rim], gz + 0.015)])
    gn = np.concatenate([gn, gn[rim]])
    a0, b0 = border[:, 0], border[:, 1]
    gf = np.concatenate([gf, np.concatenate([np.stack([b0, a0, back[a0]], 1), np.stack([b0, back[a0], back[b0]], 1)]) + len(P)])
    print('face grid skirt', len(border), 'edges')
    GRID_NORMALS[:] = [gn]
    FACE_INFO[:] = [zs, ndimage.binary_dilation(face, iterations=3), zg]
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
    # Nor in front of it, at any depth: strands hanging in front of the temple that the photo shows
    # as skin took skin colour and broke up into beige shards once the head turned.
    skin_px = (photo[..., 0] > photo[..., 2] + 30) & (photo.sum(-1) > 200)
    skin_px = ndimage.binary_erosion(skin_px, iterations=3)
    hair &= ~(skin_px[sp[:, 1], sp[:, 0]] & (hz < at(cp) + 0.06))
    # The turtleneck stays put while the head turns: the bald run's knit, below the chin.
    knit = vote(BP, BF, ~(bcol[:, 0] > bcol[:, 2] + 15)) & (bc[:, 1] < collar_y + 0.08)
    # The neck inside the collar stays with the body: turning with the head it swept out through the
    # collar's side. The split follows the collar's rim, which rises toward the back.
    ring = bc[~knit & (np.abs(bc[:, 1] - collar_y) < 0.015)]
    axis = np.array([ring[:, 0].mean(), ring[:, 2].mean()])
    # The neck's own axis, midway between throat and nape and between its sides, just above the
    # collar: the ring's centroid sat near the throat (the collar hides the nape at that height), and
    # turning about it swung the back of the neck sideways, as if it were broken.
    level = ~knit & (np.abs(bc[:, 1] - (collar_y + 0.02)) < 0.005)
    mid = level & (np.abs(bc[:, 0] - axis[0]) < 0.015)
    side = level & (np.abs(bc[:, 2] - axis[1]) < 0.03)
    if mid.sum() > 4 and side.sum() > 4:
        axis = np.array([(np.percentile(bc[side, 0], 2) + np.percentile(bc[side, 0], 98)) / 2,
                         (bc[mid, 2].min() + bc[mid, 2].max()) / 2])
        nape_z, throat_z = bc[mid, 2].min(), bc[mid, 2].max()
    else:
        nape_z, throat_z = axis[1] - 0.06, axis[1] + 0.06
    print('neck axis', np.round(axis, 3))
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
    # The neck twists from the collar's rim up to where the head's underside begins, per direction
    # round the neck: the jaw in front, the skull's base behind (the lowest faces turned down). The
    # weight is continuous per vertex and every face below weight 1 belongs to the skinned neck, so
    # nothing tears where the neck meets the head. One rigid joint at the jaw left a step in the
    # neck's outline on every turn.
    bn = np.cross(BP[BF[:, 1]] - BP[BF[:, 0]], BP[BF[:, 2]] - BP[BF[:, 0]]); bn /= np.linalg.norm(bn, axis=1, keepdims=True) + 1e-12
    under = ~knit & (bn[:, 1] < -0.5) & (bc[:, 1] > collar_y + 0.02) & (radius < r_neck + 0.06)
    hi = np.full(36, chin_y + 0.06); np.minimum.at(hi, sector(bc[under]), bc[under, 1])
    hi = ndimage.median_filter(hi, 5, mode='wrap')
    lo = rim + 0.006
    # At least NECK_TWIST high all round, and smooth round the neck: at the sides (the outline from
    # behind) a 2 cm zone bent the neck sharply on every turn.
    jaw_under = hi.copy()
    hi = ndimage.gaussian_filter1d(np.maximum(hi, lo + NECK_TWIST), 2, mode='wrap')
    # ...but never above the jaw's underside: in front that is the chin, which must turn rigidly with
    # the head (twisting partly, it went out of step on every turn).
    hi = np.maximum(np.minimum(hi, jaw_under - 0.006), lo + 0.012)
    # Only the neck's own cylinder twists: the jaw and the chin lie outside it (further from the
    # axis) and turn rigidly with the head. Twisting them bent the chin out of shape on every turn.
    r_cyl = np.full(36, r_neck)
    for k_ in range(36):
        m = ~knit & (sector(bc) == k_) & (bc[:, 1] > rim[k_] + 0.01) & (bc[:, 1] < rim[k_] + 0.035)
        if m.sum() > 5:
            r_cyl[k_] = np.percentile(radius[m], 90)
    r_cyl = ndimage.gaussian_filter1d(ndimage.median_filter(r_cyl, 5, mode='wrap'), 1.5, mode='wrap')
    def twist(p):
        t = (np.arctan2(p[:, 0] - axis[0], p[:, 2] - axis[1]) + math.pi) / (2 * math.pi) * 36 - 0.5
        i0 = np.floor(t).astype(int); f = t - i0; i0 %= 36; i1 = (i0 + 1) % 36
        l = lo[i0] * (1 - f) + lo[i1] * f; h = hi[i0] * (1 - f) + hi[i1] * f
        r = r_cyl[i0] * (1 - f) + r_cyl[i1] * f
        t = np.clip((p[:, 1] - l) / (h - l), 0, 1)
        w = 0.5 * (t + t * t * (3 - 2 * t))             # half linear: an even twist with soft ends
        out = smooth(r + 0.006, r + 0.022, np.hypot(p[:, 0] - axis[0], p[:, 2] - axis[1]))
        # The throat follows the jaw more than the nape follows the skull: looking down, a throat
        # weighted like the nape stayed put and came through the chin.
        throat = smooth(0.0, 0.06, p[:, 2] - axis[1]) * np.sqrt(t)
        return np.maximum(np.maximum(w, 0.85 * throat), out * (p[:, 1] > l + 0.01))
    wv = twist(BP)
    neck = ~knit & (wv[BF] < 0.999).any(1)
    jaw = hi
    print('neck top per sector (cm above the collar)', np.round((hi - rim) * 100).astype(int))
    top_y = np.median(hi)
    NECK_SPAN[:] = [twist, collar_y]
    print('compose: bald', len(BF), 'knit', knit.sum(), 'hair', hair.sum(), 'collar y', round(float(collar_y), 3))
    if BALD:
        hair[:] = False
    SP_, SF = hair_shell(HP, HF[hair]) if hair.any() else (np.zeros((0, 3)), np.zeros((0, 3), np.int32))
    # Strands far outside the hair's closed volume are the scan's loose shards: jagged tips round the
    # top and the temples, with the background showing between them.
    if hair.any():
        d_out = cKDTree(SP_).query(hc)[0]
        print('hair distance to its shell (mm) p50/p90/p99', np.round(np.percentile(d_out[hair], [50, 90, 99]) * 1000, 1))
        hair &= d_out < HAIR_REACH
        hair = vote(HP, HF, hair, r=0.006) & hair
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
        if not len(hair_points):
            return col
        d, i = cKDTree(hair_points).query(points)
        # Only the scalp, from eye level up: lower down the hair hangs over the cheeks, which stay skin.
        w = (smooth(0.025, 0.01, d) * smooth(eye_y - 0.02, eye_y + 0.01, points[:, 1]))[:, None]
        return col * (1 - w) + hair_cols[i] * 0.55 * w
    # The head turns and nods where a real one does: at the top of the spine (the atlas and axis
    # vertebrae), level with the ear canals and well behind the neck's middle, since the spine runs
    # along the back of the neck. The neck below twists over its length (the skinned neck), the chin
    # turns rigidly with the skull. Turning about the collar swung the whole head sideways and sheared
    # the chin against the neck.
    head = ~knit & (bc[:, 1] > collar_y + 0.12)
    reach = np.abs(bc[:, 0] - axis[0])
    ears_ = head & (reach > np.percentile(reach[head], 99))
    ear_y = bc[ears_, 1].mean() if ears_.any() else top_y + 0.06
    pivot = np.array([axis[0], ear_y - 0.01, nape_z + 0.35 * (throat_z - nape_z)])
    print('head pivot at ear level', np.round(pivot, 3))
    return P[used], remap[F].astype(np.int32), label, bald_colour, hair_tex, pivot

BALD = os.environ.get('KAIRO_BALD') == '1'   # build the head without the hair layer (layer by layer)
UNDER_CHIN_PHOTO = 0.0         # under the chin: share of the picture's own shadow (1 showed as a dark patch when the head tilted)
NECK_TWIST = 0.05              # the neck's shortest twisting length above the collar (model units)
NECK_SPAN = []                 # (neck axis xz, collar rim y per sector, jaw underside y per sector), set by compose
NECK_OVERLAP = 0.012           # the neck's top this far is in both body and head, covering the seam on turns
HAIR_REACH = 0.010             # strands further than this outside the hair's closed volume are dropped
HAIR_GAPS = 16                 # source px: gaps between the photo's strands narrower than twice this take hair colour

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
    if not len(F):
        return np.zeros((0, 3, 2)), np.zeros((4, 4, 3), np.uint8)
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
        a = np.ascontiguousarray(a); ct = {np.dtype(np.uint32): 5125, np.dtype(np.uint16): 5123}.get(a.dtype, 5126)
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
            if p.get('joints') is not None:
                prim["attributes"]["JOINTS_0"] = self.accessor(p['joints'].astype(np.uint16), "VEC4", 34962)
                prim["attributes"]["WEIGHTS_0"] = self.accessor(p['weights'].astype(np.float32), "VEC4", 34962)
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
BLINK_FRAMES = []               # (layer, from, to): painted states shown as frames; the lids below replaced them
LID_MATERIAL = 12              # index of the "lid" material
LID_LAYER = 'kairo-lid-closed.png'

def lid_sheets(work, zg):
    """The upper lids: the owner's painted closed eye on a sheet per eye that lies over the eyeball
    (1.5 mm out from it) and over the face round it. Open, the sheet is folded up at its top edge;
    blinking unrolls it down over the eye, lashes at its edge, so the lid covers the eye as it
    closes and follows the ball's curve on a turn (flat frames floated in front of the face)."""
    a, solid, alpha, (x0, y0, x1, y1) = frame_layer(work, LID_LAYER)
    tex = Image.fromarray(np.dstack([a[y0:y1, x0:x1, :3], alpha[y0:y1, x0:x1]]).astype(np.uint8), 'RGBA')
    cam = np.array([0.0, 0.0, D])
    cols, rows = 72, 28
    pos, uvs, tg_blink, tg_squint, idx, base = [], [], [], [], [], 0
    for k, (centre, R) in enumerate(EYE_BALLS):
        side = (np.arange(solid.shape[1]) < SRC_W / 2) if k == 0 else (np.arange(solid.shape[1]) >= SRC_W / 2)
        m = solid & side[None]
        ys, xs = np.where(m)
        x = np.linspace(xs.min(), xs.max(), cols)
        xi = np.clip(np.round(x).astype(int), 0, solid.shape[1] - 1)
        top = np.array([np.where(m[:, i])[0].min() if m[:, i].any() else np.nan for i in xi])
        bot = np.array([np.where(m[:, i])[0].max() if m[:, i].any() else np.nan for i in xi])
        ok = ~np.isnan(top)
        top = np.interp(x, x[ok], top[ok]) - 1; bot = np.interp(x, x[ok], bot[ok]) + 1
        f = np.linspace(0, 1, rows)[:, None]
        full = np.stack([np.broadcast_to(x, (rows, cols)), top[None] + (bot - top)[None] * f], -1).reshape(-1, 2)
        rest = np.stack([np.broadcast_to(x, (rows, cols)), np.broadcast_to(top, (rows, cols))], -1).reshape(-1, 2)
        def place(src_px):
            c = src_to_crop(src_px)
            zface = ndimage.map_coordinates(zg, [c[:, 1], c[:, 0]], order=1) - 0.0015
            ray = unproject(c, np.ones(len(c))) - cam; ray /= np.linalg.norm(ray, axis=1, keepdims=True)
            oc = cam - centre; bq = (ray * oc).sum(1); cq = (oc * oc).sum() - (R + 0.0015) ** 2
            disc = bq * bq - cq
            t = -bq - np.sqrt(np.maximum(disc, 0))
            zball = D - (cam + ray * t[:, None])[:, 2]
            zc = np.where(disc > 0, np.minimum(zface, zball), zface)
            return unproject(c, zc)
        p_full, p_rest = place(full), place(rest)
        squint = rest + (full - rest) * 0.38
        pos.append(p_rest); uvs.append((full - np.array([x0, y0])) / np.array([x1 - x0, y1 - y0]))
        tg_blink.append(p_full - p_rest); tg_squint.append(place(squint) - p_rest)
        for r in range(rows - 1):
            for c_ in range(cols - 1):
                a0 = base + r * cols + c_
                idx += [a0, a0 + cols, a0 + 1, a0 + 1, a0 + cols, a0 + cols + 1]
        base += rows * cols
    P = np.concatenate(pos)
    targets = [np.concatenate(tg_blink) if n == 'blink' else np.concatenate(tg_squint) if n == 'squint' else np.zeros_like(P) for n in TARGETS]
    nrm = np.tile([0.0, 0.0, 1.0], (len(P), 1))
    return dict(pos=P, nrm=nrm, uv=np.concatenate(uvs), idx=np.array(idx, np.uint32), material=LID_MATERIAL, targets=targets), tex

def frame_layer(work, name):
    """One of the owner's painted eye layers, cleaned: starting under the photo's brows, no strokes of
    its own, colour spread under its transparent edge; with its bounding box and softened alpha."""
    a = np.asarray(Image.open(f'{work}/{EYE_FRAMES}/{name}').convert('RGBA')).astype(float)
    # No hair strands (they belong to the hair layer, and stood still in the sheet when the head
    # turned); the colour under the transparent part spread from the solid one, since its black
    # bled into the edge as a dotted dark line when the texture was filtered.
    # Starting under the photo's brows and fading in below them: the layers reach up into the brow
    # with strokes of their own, a misplaced line over the closed lid.
    photo = np.asarray(Image.open(f'{work}/bald/kairo-bald-front.png').convert('RGB')).astype(float)
    brow = (photo[..., 2] > photo[..., 0] + 12) & (np.arange(photo.shape[0])[:, None] > 380) & (np.arange(photo.shape[0])[:, None] < 475)
    brow = ndimage.binary_opening(brow, iterations=1)
    rows = np.arange(photo.shape[0])[:, None]
    bottom = np.where(brow.any(0), np.where(brow, rows, 0).max(0), 440)
    bottom = ndimage.maximum_filter1d(ndimage.median_filter(bottom, 9), 15)
    a[..., 3] *= smooth(bottom + 3.0, bottom + 15.0, rows.astype(float))
    # And none of the layer's own brow strokes, wherever they lie (past the brow's ends too).
    stroke = (a[..., 2] > a[..., 0] + 12) & (rows < 480)
    a[..., 3] *= 1 - ndimage.gaussian_filter(ndimage.binary_dilation(stroke, iterations=3).astype(float), 1.5)
    solid = a[..., 3] > 10
    _, (iy, ix) = ndimage.distance_transform_edt(~(a[..., 3] > 200), return_indices=True)
    a[..., :3] = a[iy, ix, :3]
    ys, xs = np.where(solid)
    x0, y0, x1, y1 = xs.min() - 2, ys.min() - 2, xs.max() + 3, ys.max() + 3
    alpha = np.minimum(a[..., 3], 255 * np.clip(ndimage.distance_transform_edt(solid) / 8.0, 0, 1))
    return a, solid, alpha, (x0, y0, x1, y1)

def eye_frame(work, name, front_depth, surface_normal):
    """One painted eye state as a flat sheet over both eyes, just in front of the face."""
    a, solid, alpha, (x0, y0, x1, y1) = frame_layer(work, name)
    tex = Image.fromarray(np.dstack([a[y0:y1, x0:x1, :3], alpha[y0:y1, x0:x1]]).astype(np.uint8), 'RGBA')
    gy, gx = np.mgrid[y0:y1:2, x0:x1:2]
    inside = ndimage.binary_dilation(solid, iterations=3)[np.clip(gy, 0, solid.shape[0] - 1), np.clip(gx, 0, solid.shape[1] - 1)]
    vid = np.full(gx.shape, -1); vid[inside] = np.arange(inside.sum())
    q = [vid[:-1, :-1], vid[:-1, 1:], vid[1:, :-1], vid[1:, 1:]]
    ok = np.all([v >= 0 for v in q], 0)
    idx = np.concatenate([np.stack([q[0][ok], q[2][ok], q[1][ok]], 1), np.stack([q[1][ok], q[2][ok], q[3][ok]], 1)]).reshape(-1)
    spx = np.stack([gx[inside], gy[inside]], 1).astype(float)
    # On the face grid's own depth (just in front): on the fitted surface instead, the far eye's frame
    # stood out past the temple, in the air, when the head turned. Only over the face grid.
    c = src_to_crop(spx)
    keep = FACE_INFO[1][np.clip(c[:, 1].astype(int), 0, N - 1), np.clip(c[:, 0].astype(int), 0, N - 1)]
    remap = np.full(len(spx), -1); remap[keep] = np.arange(keep.sum())
    tri = idx.reshape(-1, 3); tri = tri[keep[tri].all(1)]
    idx = remap[tri].reshape(-1); spx, c = spx[keep], c[keep]
    pos = unproject(c, ndimage.map_coordinates(FACE_INFO[2], [c[:, 1], c[:, 0]], order=1) - 0.0015)
    uv = (spx - np.array([x0, y0])) / np.array([x1 - x0, y1 - y0])
    return dict(pos=pos, nrm=surface_normal(pos), uv=uv, idx=idx.astype(np.uint32)), tex

def hair_photo(src):
    """The photo for the hair layer: every pixel that is not hair takes the colour of the nearest hair
    pixel. Strands in front of the photo's skin (gaps between strands, the temple) took skin colour
    and broke up into beige shards once the head turned."""
    a = np.asarray(src).astype(float)
    hair = (a[..., 2] > a[..., 0] + 12) | (a.sum(-1) < 150)
    hair = ndimage.binary_opening(hair, iterations=1)
    _, (iy, ix) = ndimage.distance_transform_edt(~hair, return_indices=True)
    filled = a[iy, ix]
    filled = np.where(hair[..., None], a, ndimage.gaussian_filter(filled, (2, 2, 0)))
    return Image.fromarray(np.clip(filled, 0, 255).astype(np.uint8))

# A paintable layout for the skin the photo does not cover: orthographic views in fixed windows of
# model space (so a painting stays put when the mesh changes), one 1024 px tile each.
# (name, tile column, tile row, outward axis, sign, window size in model units)
SKIN_TILE = 1024
SKIN_CHARTS = [("right", 0, 0, 0, 1, 0.72), ("left", 1, 0, 0, -1, 0.72), ("back", 2, 0, 2, -1, 0.72), ("top", 3, 0, 1, 1, 0.6),
               ("under", 0, 1, 1, -1, 0.72), ("front", 1, 1, 2, 1, 0.72), ("ear right", 2, 1, 0, 1, 0.14), ("ear left", 3, 1, 0, -1, 0.14)]
SKIN_PAINT = 'paint/kairo-skin.png'      # the owner's painting over the layout (RGBA: alpha 0 keeps the bake)
PAINT_REPLACES = False         # True: the painting as it is; False: only its detail over the bake (as is, its tones met the photo in patches)

def skin_layout(P, F, centre, ears):
    """Chart per face and the layout position (px) of every face corner."""
    c = P[F].mean(1)
    n = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]]); n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
    n *= np.sign((n * (c - centre)).sum(1, keepdims=True) + 1e-12)
    chart = np.zeros(len(F), int)
    best = np.abs(n).argmax(1)
    for k, (_, _, _, axis, sign, _) in enumerate(SKIN_CHARTS[:6]):
        chart[(best == axis) & (np.sign(n[np.arange(len(F)), axis]) == sign)] = k
    for k, (ec, side) in enumerate(ears):
        near = (np.linalg.norm((c - ec)[:, 1:], axis=1) < 0.045) & ((c[:, 0] - centre[0]) * side > (ec[0] - centre[0]) * side - 0.035)
        chart[near] = 6 + k
    return chart, layout_px(P[F].reshape(-1, 3), np.repeat(chart, 3), centre, ears).reshape(-1, 3, 2)

def layout_px(q, chart, centre, ears):
    d = q - centre
    uv = np.zeros((len(q), 2))
    for k, (_, col, row, axis, sign, size) in enumerate(SKIN_CHARTS):
        m = chart == k
        if not m.any():
            continue
        dd = d[m] - (ears[k - 6][0] - centre if k >= 6 else 0)
        # The view's right and up, for a viewer outside on the chart's axis.
        if axis == 0:
            u, v = -sign * dd[:, 2], dd[:, 1]
        elif axis == 2:
            u, v = sign * dd[:, 0], dd[:, 1]
        else:
            u, v = dd[:, 0], -sign * dd[:, 2]
        v_off = 0.10 - centre[1] if axis != 1 and k < 6 else 0.0
        scale = SKIN_TILE / size
        uv[m, 0] = col * SKIN_TILE + SKIN_TILE / 2 + u * scale
        uv[m, 1] = row * SKIN_TILE + SKIN_TILE / 2 - (v - v_off) * scale
    return uv

def skin_bake(P, F, chart, px, colour, shade, work, ear_colour=None, photo_weight=None):
    """The skin's colours drawn into the layout: dense points on every face, the outermost along its
    chart's axis winning, the gaps filled from the nearest drawn texel."""
    W, H = SKIN_TILE * 4, SKIN_TILE * 2
    img = np.zeros((H, W, 3)); depth = np.full((H, W), -np.inf); wimg = np.zeros((H, W))
    rng = np.random.default_rng(7)
    bary = np.concatenate([np.eye(3), rng.dirichlet((1, 1, 1), size=160)])
    for part in np.array_split(np.arange(len(F)), 80):
        pts = np.einsum('bk,fkd->fbd', bary, P[F[part]]).reshape(-1, 3)
        uv = np.einsum('bk,fkd->fbd', bary, px[part]).reshape(-1, 2)
        sh = np.einsum('bk,fk->fb', bary, shade[F[part]]).reshape(-1)
        ch = np.repeat(chart[part], len(bary))
        axis = np.array([c[3] for c in SKIN_CHARTS])[ch]; sign = np.array([c[4] for c in SKIN_CHARTS])[ch]
        dep = pts[np.arange(len(pts)), axis] * sign
        col = colour(pts)
        if ear_colour is not None:
            ear = ch >= 6
            col[ear] = ear_colour(pts[ear])
        col = col * sh[:, None]
        x = np.clip(uv[:, 0].astype(int), 0, W - 1); y = np.clip(uv[:, 1].astype(int), 0, H - 1)
        o = np.argsort(dep)
        better = dep[o] > depth[y[o], x[o]]
        img[y[o][better], x[o][better]] = col[o][better]; depth[y[o][better], x[o][better]] = dep[o][better]
        if photo_weight is not None:
            wimg[y[o][better], x[o][better]] = np.repeat(photo_weight[part], len(bary))[o][better]
    drawn = np.isfinite(depth)
    # Blue texels on the skin are hair the generator painted on the bald head (along the nape): they
    # take the nearest skin instead.
    stray = drawn & (img[..., 2] > img[..., 0] - 5)
    _, (iy, ix) = ndimage.distance_transform_edt(~drawn | stray, return_indices=True)
    img = img[iy, ix]
    paint = f'{work}/{SKIN_PAINT}'
    if os.path.exists(paint):
        p_ = np.asarray(Image.open(paint).convert('RGBA').resize((W, H), Image.LANCZOS)).astype(float)
        cover = p_[..., 3] > 128
        # Feathered, so a painted area has no edge against the bake.
        a = ndimage.gaussian_filter(ndimage.binary_erosion(cover, iterations=6).astype(float), 4)[..., None]
        # Each chart was painted in its own tone, and a face takes one chart: as painted, the seams
        # between charts showed as a patchwork. Elsewhere only the painting's detail (pores, creases)
        # goes onto the bake; on the ears the painting itself, toned to the bake.
        filled = np.where(cover[..., None], p_[..., :3], 0)
        weight = ndimage.gaussian_filter(cover.astype(float), 24)[..., None]
        low = ndimage.gaussian_filter(filled, (24, 24, 0)) / np.maximum(weight, 1e-3)
        detail = p_[..., :3] - low
        out = img + detail
        if not PAINT_REPLACES:
            a[SKIN_TILE:, 2 * SKIN_TILE:] = 0     # detail only, and not on the ears (no fit with the scan's ear)
        if PAINT_REPLACES:
            out = p_[..., :3]          # painted over this bake, so its tones already agree across charts
            # ...but not where the skin blends into the photo (beside the face): there the painting's
            # tone met the photo's in a seam down the temple.
            a = a * (1 - ndimage.gaussian_filter(wimg, 6))[..., None]
        img = img * (1 - a) + out * a
        print('skin painting applied from', paint)
    return np.clip(img, 0, 255).astype(np.uint8), drawn

def skin_template(tex, drawn, path, corners=None):
    """The layout for painting: the current colours, each chart framed and named, undrawn texels grey."""
    from PIL import ImageDraw, ImageFont
    used = ndimage.binary_closing(drawn, iterations=4)
    img = Image.fromarray(np.where(used[..., None], tex, 128).astype(np.uint8))
    d = ImageDraw.Draw(img)
    for name, col, row, *_ in SKIN_CHARTS:
        x0, y0 = col * SKIN_TILE, row * SKIN_TILE
        d.rectangle([x0, y0, x0 + SKIN_TILE - 1, y0 + SKIN_TILE - 1], outline=(255, 0, 255), width=3)
        d.text((x0 + 20, y0 + 14), name, fill=(255, 0, 255), font=ImageFont.load_default(size=56))
    img.save(path)
    if corners is not None:
        # The same with the model's triangles drawn on, to see how the surface lies in each view.
        wire = img.copy(); d = ImageDraw.Draw(wire)
        for t in corners[::1]:
            d.polygon([tuple(x) for x in t], outline=(40, 40, 40))
        wire.save(path.replace('.png', '-wire.png'))

def jpeg(img, quality=90):
    out = io.BytesIO(); img.save(out, 'JPEG', quality=quality); return out.getvalue()

# ---------------------------------------------------------------- build

def build(work):
    P, F, label, skin_colour, scan_colour, pivot = compose(work)
    # Bald, the face shows the bald picture (same face, same framing, no hair painted on it).
    src = Image.open(f'{work}/{"bald/kairo-bald-front.png" if BALD else "kairo-front.png"}').convert('RGB')
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
    # Skin off the face grid is always atlas-textured, its colour blended from the picture by how
    # squarely the camera saw it (see bare_colour): switching from the picture to the generator's
    # colour at one angle left a visible edge down the side of the head.
    project_weight = smooth(0.05, 0.4, toward) * seen
    front &= ~(((label == SKIN) | (label == NECK)) & ~on_grid)
    front |= ((label == HAIR) & (np.abs(toward) > 0.5) & (P[F].mean(1)[:, 2] > 0))
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
    blend = photo_px * (1 - w) + bald_px * tone * w
    # Under the photo's hair, gaps between strands included, the skin shows the hair's own colour: as
    # skin colour, the gaps at the temple came through the hair layer as beige shards.
    envelope = ndimage.binary_closing(hair_px, iterations=HAIR_GAPS)
    envelope &= ~ndimage.binary_erosion(face_mask_src(src), iterations=6)    # never into the open face
    soft = ndimage.gaussian_filter(envelope.astype(float), 1.5)[..., None]
    blend = blend * (1 - soft) + np.asarray(hair_photo(src)).astype(float) * soft
    bald_src = Image.fromarray(np.clip(blend, 0, 255).astype(np.uint8))
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
        # Matched where the camera saw the faces squarely (skin off the grid is never `front` now).
        fs = np.where((front | (project_weight > 0.8)) & (label == kind) & ~on_grid)[0]
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
    # Smoothed over 5 mm: the scan's ears are noisy, and at full detail their occlusion speckled.
    near_v = cKDTree(P).query_ball_point(P, r=0.005)
    ao = np.array([ao[n_].mean() for n_ in near_v])
    shade = 0.75 + 0.25 * ao          # stronger, the hanging hair greyed the cheek beneath it
    rest = np.where(~front & (label == HAIR))[0]
    rest_uv, rest_tex = atlas(P, F[rest], lambda q: scan_colour(q) * hair_gain, 4096, shade)
    bare = np.where(~front & (label != HAIR) & (label < CORE))[0]
    # The turtleneck keeps a per-triangle atlas; only skin goes into the paintable layout (seen from
    # below, the shoulders covered the chin's underside there).
    knit_bare = bare[label[bare] == KNIT]; bare = bare[label[bare] != KNIT]
    # The body's core is dark only inside the turtleneck; above the collar a crack between the
    # twisting neck and the head showed it as a black sliver under the jaw.
    low = P[F].mean(1)[:, 1] < NECK_SPAN[1]
    dark = np.where((label == CORE_BODY) & low)[0]
    inner = np.where((label == CORE) | ((label == CORE_BODY) & ~low))[0]
    bare_kind = cKDTree(P[F[bare]].mean(1))
    chin_tree = cKDTree(P[grid_start:])
    # Skin seen only from the side keeps the generator's light and shade but takes the photo's skin
    # tone: its own colour drifted grey-beige there, a strip between cheek and ear on a turn.
    cheek = np.asarray(bald_src).astype(float)[open_skin]
    skin_ref = np.median(cheek, 0); skin_lum = skin_ref.mean()
    picture = np.asarray(bald_src).astype(float)
    def projected(q):
        c, _ = project(q); sp = (c - np.array([OX, OY])) / S
        return np.stack([ndimage.map_coordinates(picture[..., k], [sp[:, 1], sp[:, 0]], order=1) for k in range(3)], -1)
    bare_down = -np.cross(P[F[bare, 1]] - P[F[bare, 0]], P[F[bare, 2]] - P[F[bare, 0]])
    bare_down = bare_down[:, 1] / (np.linalg.norm(bare_down, axis=1) + 1e-12)
    bare_down = np.abs(bare_down) * (VN[F[bare]].mean(1)[:, 1] < 0)     # winding is mixed: the smoothed normals give the sign
    def bare_colour(q):
        near = bare_kind.query(q)[1]
        kind = label[bare][near]
        col = skin_colour(q, bald_gain[kind])
        lum = col.mean(1, keepdims=True)
        # A narrow brightness range: the generator's side light and the occlusion under the hanging
        # hair made the side of the face a darker patchwork against the photo's evenly lit cheek.
        # Faces turned down lie in a little shadow (more, and the chin's underside read as a dark band).
        toned = skin_ref[None] * np.clip(lum / skin_lum, 0.8, 1.1) * (1 - 0.1 * bare_down[near])[:, None]
        # Not the scalp under the hair (already turned to the hair's colour): toned to skin, it showed
        # through the bangs at the temple as beige shards.
        is_skin = (((kind == SKIN) | (kind == NECK)) & (col[:, 0] > col[:, 2] + 10))[:, None]
        side = np.where(is_skin, 0.15 * col + 0.85 * toned, col)
        w = (project_weight[bare][near] * ((kind == SKIN) | (kind == NECK)))[:, None]
        out = side * (1 - w) + projected(q) * w
        # Under the chin and jaw: the picture's own shadow there (what the camera sees below the jaw
        # line), so the chin's underside matches the neck beneath it and the chin reads as one piece.
        c_, _ = project(q); sx = (c_[:, 0] - OX) / S; sy = (c_[:, 1] - OY) / S
        jaw_y = CHIN_Y - 0.0042 * (sx - 626.0) ** 2
        below = (sy > jaw_y - 4) & (np.abs(sx - 626) < 190)
        pc = projected(q)
        on_skin = pc[:, 0] > pc[:, 2] + 25          # never the backdrop beside the jaw (a pale patch on a turn)
        wc = UNDER_CHIN_PHOTO * (smooth(0.04, 0.01, chin_tree.query(q)[0]) * below * on_skin * ((kind == SKIN) | (kind == NECK)))[:, None]
        return out * (1 - wc) + pc * wc
    # The skin's own layout (paintable), see SKIN_CHARTS.
    centre = np.array([pivot[0], P[grid_start:, 1].mean(), pivot[2]])
    sk = P[F[bare]].reshape(-1, 3)
    ears = []
    for side in (1, -1):
        band = sk[(np.abs(sk[:, 1] - centre[1]) < 0.08)]
        rim = band[np.argmax((band[:, 0] - centre[0]) * side)]
        ears.append((rim - np.array([side * 0.02, 0, 0]), side))
    chart, corner_px = skin_layout(P, F[bare], centre, ears)
    # Faces that fall outside their chart's window (the neck deep inside the collar, never seen) go
    # with the turtleneck's atlas instead of spilling into the next chart.
    tile = np.array([[c[1], c[2]] for c in SKIN_CHARTS])[chart] * SKIN_TILE
    inside = ((corner_px >= tile[:, None, :] + 2) & (corner_px < tile[:, None, :] + SKIN_TILE - 2)).all((1, 2))
    knit_bare = np.concatenate([knit_bare, bare[~inside]]); bare, chart, corner_px = bare[inside], chart[inside], corner_px[inside]
    bare_kind = cKDTree(P[F[bare]].mean(1))
    bare_down = bare_down[inside]
    knit_uv, knit_tex = atlas(P, F[knit_bare], lambda q: skin_colour(q, bald_gain[KNIT]), 2048, 0.6 + 0.4 * shade)
    # Ears: clean skin a touch pinker, their form from the baked occlusion alone (the generator's ear
    # texture was blotchy and the painting did not fit the scan's ear).
    ear_tint = skin_ref * np.array([1.03, 0.96, 0.94])
    skin_tex, drawn = skin_bake(P, F[bare], chart, corner_px, bare_colour, np.where(np.isin(np.arange(len(P)), F[bare][chart >= 6]), 0.45 + 0.55 * ao, 0.6 + 0.4 * shade), work,
                                ear_colour=lambda q: np.broadcast_to(ear_tint, (len(q), 3)).copy(), photo_weight=project_weight[bare])
    skin_template(skin_tex, drawn, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'obj', 'kairo-skin-template.png'), corner_px)
    skin_uv = corner_px / np.array([SKIN_TILE * 4, SKIN_TILE * 2])
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

    print('pivot', np.round(pivot, 3))

    prims = {'head': [], 'body': []}
    fpos, fuv, fnrm, fvis = P, uv_front, VN, visible.astype(float)
    def own(sel, uv):
        return (np.arange(len(sel) * 3).reshape(-1, 3), P[F[sel]].reshape(-1, 3), uv.reshape(-1, 2), VN[F[sel]].reshape(-1, 3),
                visible[F[sel]].reshape(-1).astype(float), label[sel], overlap[sel])
    # The neck's top NECK_OVERLAP is in both the body and the head: turning, the head's copy slides over the
    # neck and covers the gap that opened at the jaw's angle (the dark core showed there as a wedge).
    centres = P[F].mean(1)
    head_skin = cKDTree(centres[label == SKIN])
    overlap = (label == NECK) & (head_skin.query(centres)[0] < NECK_OVERLAP)
    photo, drawn = front & from_photo, front & ~from_photo
    groups = [(F[photo], fpos, fuv, fnrm, fvis, label[photo], overlap[photo], 7, True),
              (F[drawn], fpos, fuv, fnrm, fvis, label[drawn], overlap[drawn], 4, True),
              (*own(rest, rest_uv), 1, True),
              (*own(bare, skin_uv), 2, True),
              (*own(knit_bare, knit_uv), 9, True),
              (*own(dark, np.zeros((len(dark), 3, 2))), 6, False),
              (*own(inner, np.zeros((len(inner), 3, 2))), 8, False)]
    # The neck twists: skinned to the body and the head, its weight rising from the collar to the jaw.
    # Rigid with the body, the head slid over it on every turn and nod, and the seam showed.
    twist, collar_y = NECK_SPAN
    prims['neck'] = []
    grid_tree = cKDTree(P[grid_start:])
    for faces, pos, uv, nrm, vis, kind, both, material, morph in groups:
        body = (kind == KNIT) | (kind == CORE_BODY)
        neck = kind == NECK
        for part, mask in (('head', ~body & ~neck), ('body', body), ('neck', neck)):
            fs = faces[mask]
            if not len(fs): continue
            used = np.unique(fs); remap = np.full(len(pos), -1); remap[used] = np.arange(len(used))
            p = pos[used] - pivot if part == 'head' else pos[used]
            prim = dict(pos=p, nrm=nrm[used], uv=uv[used], idx=remap[fs].reshape(-1), material=material)
            if part == 'neck':
                # Next to the face grid (the chin) fully with the head: weighted, the scan under the
                # chin slid against the grid's rigid edge, a second chin and a gap on every nod.
                w = np.maximum(twist(p), smooth(0.025, 0.006, grid_tree.query(p)[0]))
                # Along the chain body - N1 - N2 - head: each vertex between the two joints at its height.
                s_ = np.clip(w * 3, 0, 3 - 1e-6); k0 = np.floor(s_).astype(int); f_ = s_ - k0
                prim['joints'] = np.stack([k0, k0 + 1, np.zeros_like(k0), np.zeros_like(k0)], 1)
                prim['weights'] = np.stack([1 - f_, f_, np.zeros_like(f_), np.zeros_like(f_)], 1)
            if part == 'head':
                prim['targets'] = lift(pos[used], vis[used]) if morph else [np.zeros_like(p) for _ in TARGETS]
            prims[part].append(prim)

    front_vertices = np.unique(F[front])
    ntree = cKDTree(P[front_vertices])
    def surface_normal(points):
        _, i = ntree.query(points, k=6)
        n = VN[front_vertices][i].mean(1)
        return n / (np.linalg.norm(n, axis=1, keepdims=True) + 1e-12)
    eyes = eye_parts(FACE_INFO[2], surface_normal) + [eye_sockets()]
    lid, lid_tex = lid_sheets(work, FACE_INFO[2])
    for part in eyes + [lid]:
        part['pos'] = part['pos'] - pivot
        prims['head'].append(part)
    closed = Image.open(f'{work}/assets-v2/kairo-eyes-closed-front.png').convert('RGB')

    g = Glb()
    g.gltf["samplers"].append({"magFilter": 9729, "minFilter": 9987, "wrapS": 33071, "wrapT": 33071})
    for name, img in (("front", sclera_photo(src)), ("rest", Image.fromarray(rest_tex)), ("skin", Image.fromarray(skin_tex)),
                      ("lidskin", lid_texture(closed, src)), ("bald", bald_src), ("iris", iris_texture(src)),
                      ("core", Image.new('RGB', (4, 4), (8, 6, 8))), ("hairphoto", hair_photo(src)),
                      # Inside the head the core is shadowed skin: through the scan's gaps (round the eye
                      # sockets, behind the face's edge) black read as holes in the cheek.
                      ("coreskin", Image.new('RGB', (4, 4), tuple(int(c * 0.8) for c in skin_ref))), ("knit", Image.fromarray(knit_tex)),
                      ("eyeball", eyeball_texture(src)),
                      ("socket", Image.fromarray(np.tile((skin_ref[None, :] * np.array([1.0, 0.86, 0.86]) * np.linspace(0.95, 0.75, 64)[:, None])[:, None, :], (1, 4, 1)).astype(np.uint8))),
                      ("lid", lid_tex)):
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
        # The skin atlas too: lit by the scene while the face beside it showed the picture's own light,
        # the skull and the sides came out a different tone, an edge round the face.
        if name in ("front", "lidskin", "bald", "iris", "hairphoto", "skin", "coreskin", "knit", "eyeball", "socket", "lid"):
            # The picture already carries its light: shown mostly as it is (emissive), with a little
            # shading on top for depth when the head turns. Fully lit, the scan's uneven normals
            # mottled the face and the turtleneck.
            material["pbrMetallicRoughness"]["baseColorFactor"] = [0.1, 0.1, 0.1, 1]
            material["emissiveTexture"] = {"index": i}
            material["emissiveFactor"] = [0.9, 0.9, 0.9]
            material["pbrMetallicRoughness"]["roughnessFactor"] = 1.0       # no highlights on the baked light
        if name in ("core", "coreskin"):
            material["pbrMetallicRoughness"]["roughnessFactor"] = 1.0
        if name in ("lidskin", "iris", "lid"):
            material["alphaMode"] = "BLEND"
        g.gltf["materials"].append(material)
    # 0 Root, 1 Body (turtleneck), 2 HeadPivot (at the neck), 3 Head.
    neck_node = 4 + len(BLINK_FRAMES)
    # The neck's joints on the spine, at a third and two thirds of the way from the collar up to the
    # head's pivot (the spine runs up the back of the neck): turning, they share the head's rotation
    # as the cervical vertebrae do, so the neck bends along its length, not at one joint.
    collar_y = NECK_SPAN[1]
    j1 = np.array([pivot[0], collar_y + (pivot[1] - collar_y) / 3, pivot[2]])
    j2 = np.array([pivot[0], collar_y + (pivot[1] - collar_y) * 2 / 3, pivot[2]])
    n1_node, n2_node = neck_node + 1, neck_node + 2
    g.node("Root", children=[1, n1_node, neck_node])
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
    g.node("HeadPivot", t=pivot - j2, children=[3] + [4 + k for k in range(len(frames))])
    m_head = g.mesh("Head", prims['head'], TARGETS)
    g.node("Head", m_head)
    for k, (prim, _) in enumerate(frames):
        g.node(f"BlinkFrame{k}", g.mesh(f"BlinkFrame{k}", [prim]))
    inverse_bind = np.stack([np.eye(4)] * 4)
    for k_, at in ((1, j1), (2, j2), (3, pivot)):
        inverse_bind[k_, :3, 3] = -at
    skin_ibm = g.accessor(inverse_bind.transpose(0, 2, 1).reshape(-1, 16).astype(np.float32), "MAT4")
    g.gltf["skins"] = [{"joints": [1, n1_node, n2_node, 2], "inverseBindMatrices": skin_ibm}]
    n = g.node("Neck", g.mesh("Neck", prims['neck']))
    assert n == neck_node
    g.gltf["nodes"][n]["skin"] = 0
    assert g.node("NeckLower", t=j1, children=[n2_node]) == n1_node
    assert g.node("NeckUpper", t=j2 - j1, children=[2]) == n2_node
    clips(g)
    return g.bytes(), dict(m_head=m_head, head_prims=len(prims['head']), neck_chain=(n1_node, n2_node))

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
        "neckChain": [{"node": ids['neck_chain'][0], "share": 0.25}, {"node": ids['neck_chain'][1], "share": 0.3}],
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
