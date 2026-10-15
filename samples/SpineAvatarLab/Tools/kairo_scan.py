"""Builds Kairo from Pixal3D scans of the owner's pictures, for the Spine Avatar Lab (#524).

Usage: python kairo_scan.py <work-dir> <out-dir> [poster-light.png poster-dark.png]

<work-dir> holds what the generator produced:
  kairo-front.png                       the owner's front photo (1254 px)
  kairo-s42.glb, kairo-s42_base.png     Pixal3D single-view run of the photo (pixal3d.cpp, --webp off)
  kairo-s42.svviews/input.png           its cropped, matted input (camera space)
  bald/kairo-bald-front.png             the owner's bald picture, same framing
  bald/kairo-bald-s42.glb (+ _base.png, .svviews)   Pixal3D run of the bald picture
  assets-v2/kairo-eyes-closed-front.png the owner's eyes-closed picture, same framing
  assets-v2/kairo-mouth-open-front.png, kairo-mouth-o-front.png   the owner's open ("aa") and o mouths, same framing
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
    expressions and the 15 visemes. Written as sparse accessors. The face is cut along the lips: the
    open and o shapes carry them exactly onto the owner's mouth pictures, and behind them sheets from
    the open picture (teeth, tongue) and the o picture's dark make the inside of the mouth.
 5. The head turns about the neck's axis at the collar.
"""
import faulthandler, hashlib, io, json, math, os, signal, sys, zipfile
import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.interpolate import LinearNDInterpolator
from scipy.spatial import cKDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
faulthandler.register(signal.SIGUSR1)       # kill -USR1 <pid> prints where a long build is
from plush_avatars import quat, wave, tiny_png, srgb_to_linear

# ---------------------------------------------------------------- the camera

D, T, N = 2.83564091, math.tan(0.34906584 / 2), 1020   # pixal3d.cpp single-view camera, crop size
CAMERA_D = D                                           # the camera's distance under its own name, for scopes that name a directory D
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

# The owner's generated views of the bald avatar (assets-v3: front and back three-quarters, profiles,
# back; rendered from this avatar's own front/side/back renders, so lit like it), fitted to the bald
# scan by silhouette above the collar (tools/sidefit.py, IoU 0.94-0.99): the model turned about y by
# the angle faces the camera, and picture px = crop px * scale + offset. Only their fine detail
# (pores, grain, small creases, under DETAIL_SCALE px) goes onto the skin's one tone below the eyes;
# their light does not: projected as they were, each view met the next and the photo's cheek in
# seams and patches (owner: "ser inte alls bra ut"). The hair covers the rest.
SIDE_VIEWS = [('gen-1.png', -135.0, 1.3954, -74.1, -87.2), ('gen-2.png', 135.0, 1.3972, -85.2, -89.6),
              ('gen-3.png', -45.0, 1.3120, -34.7, -71.1), ('gen-4.png', 45.0, 1.2632, -39.6, -59.8),
              ('gen-5.png', 180.0, 1.3797, -77.1, -79.1), ('gen-6.png', -90.0, 1.3830, -70.4, -80.4),
              ('gen-7.png', 90.0, 1.3642, -75.4, -80.5)]

DETAIL_SCALE = 12.0     # px in the views: variation finer than this is skin detail, coarser is their light

# The owner's scalp views (assets-scalp: the lab's own renders of the bald head with the root layer painted on the
# scalp: front, both profiles, back and the crown with the head pitched down 35 deg), fitted to the bald scan by
# silhouette (Tools/scalpfit.py, IoU 0.99): the model turned about y by the yaw, the head pitched about its pivot,
# and picture px = crop px * scale + offset. Where they show the root layer the scalp under the hair (the skull's
# skin and the root cap) takes their pixels and their outline; where they show skin the skull is skin.
SCALP_DIR = 'assets-scalp'
SCALP_VIEWS = [('front.png', 0, 0, 2.2362, -116.8, -119.4), ('right.png', 90, 0, 2.2463, -119.5, -122.8),
               ('left.png', -90, 0, 2.2515, -128.2, -124.6), ('back.png', 180, 0, 2.2428, -120.1, -121.4),
               ('crown-head-down-35.png', 0, 35, 2.2808, -139.4, -136.5)]
SCALP_ROOT_LUM = (150.0, 110.0)   # luminance over which a pixel of a scalp view goes from skin (or the knit) to the root layer
SCALP_ROOT_HUE = (0.0, 30.0)      # blue minus red over which it goes from skin to the root layer (the pictures' soft hairline and nape fade carry through)
SCALP_MIN_PX = 4000         # px: a smaller indigo component (an iris, a brow) is not the root layer
SCALP_FEATHER = 2.0         # px of blur on the root weight
SCALP_FACING = (0.15, 0.5)  # cosine range over which a view's weight comes in (a view at a grazing angle maps its silhouette's fringe onto centimetres of surface: counted fully, the left view's nape fringe ran down the neck as a streak). The root weight is the union of the views' (the owner's front and left pictures disagree at the temple's front edge), the colour comes from the views that show hair there.
SCALP_SETTLE = (0.45, 0.6)  # the feathered root weight settles to skin below and roots above: in the pictures' hairline zone (strands over skin) the per-pixel weight averaged to a half and baked as a translucent band; the strands overlay draws that zone
SCALP_SEEN = (0.1, 0.4)     # total view weight over which the pictures replace the generator's scalp
SCALP_DEPTH_TOL = 0.01

# The lab's camera, for the owner's scalp pictures (captures of the lab at `size 1200`, 2400 px, resized by the owner to
# 2048): the package's framing (camera at LAB_CAMERA looking at the origin, vertical field of view LAB_FOV, a square
# view), the view yaw about y, and the head's turn spread down the neck chain as the rig does (NeckLower 0.3,
# NeckUpper 0.35, the head the rest, each about its own joint). Fitted by silhouette with the generator's own crop
# camera (20 deg, 2.8 units away) the pictures matched at the outline but not inside it: a point on the head's
# side near the silhouette mapped a hundred pixels off, onto the temple's skin, and the scalp sampler read skin
# where the picture shows roots (the pale band at the temple).
LAB_CAMERA = np.array([0.0, 0.04, 2.3]); LAB_FOV = 22.0
LAB_CHAIN = (0.3, 0.35)        # the neck joints' shares of the head's turn (bindings neckChain), the head takes the rest

def _quat_rot(axis, ang):
    axis = np.asarray(axis, float); c, s_ = math.cos(ang), math.sin(ang); x, y, z = axis
    return np.array([[c + x * x * (1 - c), x * y * (1 - c) - z * s_, x * z * (1 - c) + y * s_],
                     [y * x * (1 - c) + z * s_, c + y * y * (1 - c), y * z * (1 - c) - x * s_],
                     [z * x * (1 - c) - y * s_, z * y * (1 - c) + x * s_, c + z * z * (1 - c)]])

def lab_pose(p, head_yaw, head_pitch, pivot, collar_y):
    """Points (the generator's frame) under the lab's head turn (degrees): the rig's quaternion Qy(yaw) Qx(-pitch),
    split by slerp shares over NeckLower (at j1), NeckUpper (at j2) and the head (at the pivot), as GltfAvatarRig does."""
    if not head_yaw and not head_pitch:
        return p
    j1 = np.array([pivot[0], collar_y + (pivot[1] - collar_y) / 3, pivot[2]]); j2 = np.array([pivot[0], collar_y + (pivot[1] - collar_y) * 2 / 3, pivot[2]])
    # the turn as an axis-angle, so a share of it is the same axis at a share of the angle (slerp from identity)
    R = _quat_rot([0, 1, 0], math.radians(head_yaw)) @ _quat_rot([1, 0, 0], -math.radians(head_pitch))
    ang = math.acos(np.clip((np.trace(R) - 1) / 2, -1, 1))
    axis = np.array([R[2, 1] - R[1, 2], R[0, 2] - R[2, 0], R[1, 0] - R[0, 1]]); axis /= np.linalg.norm(axis) + 1e-12
    q = p
    for origin, share in ((pivot, 1 - sum(LAB_CHAIN)), (j2, LAB_CHAIN[1]), (j1, LAB_CHAIN[0])):     # the head first, then up the chain to the root
        q = (q - origin) @ _quat_rot(axis, ang * share).T + origin
    return q

def lab_project(p, view_yaw, size=2048):
    """The lab's view of posed points: picture px (size x size) and the depth along the camera's axis. The view yaw
    turns the model about y (the container's orientation), the camera sits at LAB_CAMERA looking at the origin."""
    q = p @ _quat_rot([0, 1, 0], math.radians(view_yaw)).T
    f = -LAB_CAMERA / np.linalg.norm(LAB_CAMERA); r = np.cross(f, [0, 1, 0]); r /= np.linalg.norm(r); u = np.cross(r, f)
    d = q - LAB_CAMERA
    x, y, z = d @ r, d @ u, d @ f
    t = math.tan(math.radians(LAB_FOV) / 2)
    return np.stack([(x / (z + 1e-9) / t + 1) / 2 * size, (1 - y / (z + 1e-9) / t) / 2 * size], 1), z

def lab_zbuffer(p, F, view_yaw, size=2048, cell=4):
    """The nearest depth per cell of the lab's view of a mesh (dense face samples), as front_zbuffer is for the crop camera."""
    rng = np.random.default_rng(2)
    pts = np.concatenate([np.einsum('k,fkd->fd', b, p[F]) for b in rng.dirichlet((1, 1, 1), size=6)] + [p])
    px, z = lab_project(pts, view_yaw, size); n = size // cell
    ix = np.clip((px / cell).astype(int), 0, n - 1)
    zb = np.full((n, n), np.inf); np.minimum.at(zb, (ix[:, 1], ix[:, 0]), z)
    def at(q):
        i = np.clip((q / cell).astype(int), 0, n - 1)
        return zb[i[:, 1], i[:, 0]]
    return at

def pose_model(p, yaw, pitch, pivot, collar_y):
    """Points at the lab's pose: the head (above the collar, the neck's upper part following) pitched about the
    pivot, then everything turned about y."""
    q = p
    if pitch:
        c, s_ = math.cos(math.radians(pitch)), math.sin(math.radians(pitch)); Rx = np.array([[1, 0, 0], [0, c, -s_], [0, s_, c]])
        w = np.clip((p[:, 1] - collar_y) / 0.06, 0, 1)[:, None]
        q = p + w * ((p - pivot) @ Rx.T + pivot - p)
    return q @ rot_y(yaw).T

def scalp_views(work, P, F, pivot):
    """The owner's scalp views as one sampler: points (model frame) -> (colour, root weight 0..1, total view
    weight). Each view weighs a point by how squarely it faces the camera (a radial normal: the skull is
    convex), unoccluded by the bald model, inside the picture's silhouette. None without the pictures."""
    cpx = src_to_crop([(MOUTH['cx'], COLLAR_Y)]); collar_y = unproject(cpx, np.array([D]))[0, 1]
    # The bald model's own vertices only: with every vertex of P in the depth buffer, the hair envelope's (2-4 cm
    # off the skull) hid the scalp cell by cell and the roots came out as a checkerboard of skin.
    used = np.unique(F.reshape(-1)); remap = np.full(len(P), -1); remap[used] = np.arange(len(used))
    P, F = P[used], remap[F]
    centre = P[P[:, 1] > pivot[1]].mean(0)
    views = []
    for name, yaw, pitch, sc, ox, oy in SCALP_VIEWS:
        path = f'{work}/{SCALP_DIR}/{name}'
        if not os.path.exists(path):
            continue
        a = np.asarray(Image.open(path).convert('RGBA')).astype(float); pic, fg = a[..., :3], a[..., 3] > 127
        lum_w = np.array([0.299, 0.587, 0.114]); lum = pic @ lum_w
        root = fg * smooth(SCALP_ROOT_LUM[0], SCALP_ROOT_LUM[1], lum) * smooth(SCALP_ROOT_HUE[0], SCALP_ROOT_HUE[1], pic[..., 2] - pic[..., 0]) * smooth(-5.0, 10.0, pic[..., 2] - pic[..., 1])
        core = ndimage.binary_opening(root > 0.5, iterations=2)
        lab_, _ = ndimage.label(core); sizes = np.bincount(lab_.ravel()); small = sizes < SCALP_MIN_PX; small[0] = False
        core = core & ~small[lab_]
        root = smooth(SCALP_SETTLE[0], SCALP_SETTLE[1], ndimage.gaussian_filter(root * ndimage.binary_dilation(core, iterations=12), SCALP_FEATHER))
        # a pixel's darkness against the local skin, for the colour: the views that show strands or roots at a point colour it
        skin = fg & (root < 0.1)
        est = np.stack([ndimage.gaussian_filter(pic[..., k] * skin, 25) for k in range(3)], -1) / np.maximum(ndimage.gaussian_filter(skin.astype(float), 25), 1e-3)[..., None]
        root_lum = np.median(lum[core]) if core.any() else 60.0
        dark = np.clip((est @ lum_w - lum) / np.maximum(est @ lum_w - root_lum, 1.0), 0, 1)
        at, _ = front_zbuffer(pose_model(P, yaw, pitch, pivot, collar_y), F)
        views.append(dict(yaw=yaw, pitch=pitch, scale=sc, off=np.array([ox, oy]), pic=pic, fg=fg.astype(float),
                          root=root, hair=np.maximum(root, ndimage.gaussian_filter(dark, 1.0)), at=at,
                          centre=pose_model(centre[None], yaw, pitch, pivot, collar_y)[0], shape=fg.shape))
        print('scalp view', name, 'root px (weight > 0.5)', int((root > 0.5).sum()))
    if not views:
        return None
    root_rgb = np.median(np.concatenate([v['pic'][v['hair'] > 0.7] for v in views]), 0)
    def sample(q):
        tot = np.zeros(len(q)); col = np.zeros((len(q), 3)); ctot = np.zeros(len(q)); keep = np.ones(len(q))
        for v in views:
            qp = pose_model(q, v['yaw'], v['pitch'], pivot, collar_y)
            px, z = project(qp)
            n = qp - v['centre']; n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
            cam = np.array([0.0, 0.0, D]) - qp; cam /= np.linalg.norm(cam, axis=1, keepdims=True) + 1e-12
            seen = z <= v['at'](px) + SCALP_DEPTH_TOL
            ppx = px * v['scale'] + v['off']; H_, W_ = v['shape']
            inside = (ppx[:, 0] >= 0) & (ppx[:, 0] < W_ - 1) & (ppx[:, 1] >= 0) & (ppx[:, 1] < H_ - 1)
            yx = [np.clip(ppx[:, 1], 0, H_ - 1), np.clip(ppx[:, 0], 0, W_ - 1)]
            w = smooth(SCALP_FACING[0], SCALP_FACING[1], (n * cam).sum(1)) * seen * inside * ndimage.map_coordinates(v['fg'], yx, order=1, mode='constant')
            c = np.stack([ndimage.map_coordinates(v['pic'][..., k], yx, order=1, mode='nearest') for k in range(3)], -1)
            r = ndimage.map_coordinates(v['root'], yx, order=1, mode='constant')
            h = ndimage.map_coordinates(v['hair'], yx, order=1, mode='constant')
            keep *= 1.0 - r * w
            cw = w * (0.05 + 0.95 * h)
            tot += w; ctot += cw; col += cw[:, None] * c
        ok = ctot > 1e-6
        col[ok] /= ctot[ok, None]
        return col, 1.0 - keep, tot
    sample.views = views
    sample.collar_y = collar_y
    sample.root_rgb = root_rgb
    return sample

STRANDS_LIFT = 0.0004            # m the hairline overlay stands off the surface it lies on
STRANDS_BAND_PX = (60, 70)       # px of a scalp view past the root core's edge over the skin, and inside it, that the overlay covers (inside: the face grid's top edge lies 4-5 mm above the owner's hairline and shows the bald picture's skin over the skull's roots there; the overlay carries the roots across it)
STRANDS_EYE_PX = (115, 95, 15)   # px of a scalp view: the ellipse round each eye (half-widths, raised by the third) that gets no overlay (the brows and the lashes are dark against the skin as the strands are)
STRANDS_DARK = (0.05, 0.5)       # a pixel's darkness against the local skin (0 skin, 1 the roots' own) over which the overlay's alpha goes 0 -> 1 (a thin strand is half dark after the resampling and must still show)
STRANDS_HAIR_BIAS = 0.25         # a view's claim on a face is its facing times (this + the alpha it draws there): the view that shows strands or roots on a face wins over one that shows bare skin (the left picture's hairline sits further back than the front's at the temple; by facing alone the left view took the temple's faces and drew nothing)
STRANDS_FACING = 0.35            # cosine below which a view hangs no overlay on a skull face
STRANDS_FACING_GRID = 0.05       # ...or on a grid face
STRANDS_ATLAS = 2048

def hairline_strands(P, F, faces, VN, scalp, pivot, eyes, is_grid=None):
    """The owner's strands over the skin at the hairline, as in their scalp views: a thin overlay on the outermost
    surface there (the face grid over the forehead and the temples, the skull elsewhere) textured straight from the
    views' pixels, each pixel's alpha its darkness against the local skin (de-matted, so drawn over the skin it gives
    the picture back). The skull's bake cannot carry them: over the forehead the surface is the face grid, which
    shows the bald picture and nothing else. The grid's faces (`is_grid`) take the overlay wherever the views show
    roots, not only in the band: at the temples the grid's edge zone stands in front of the skull's roots and showed
    the bald picture's skin there. `faces`: candidate face indices into F (grid + skull skin)."""
    lum_w = np.array([0.299, 0.587, 0.114])
    faces = np.asarray(faces); is_grid = np.zeros(len(faces), bool) if is_grid is None else np.asarray(is_grid)
    used = np.unique(F[faces].reshape(-1)); remap = np.full(len(P), -1); remap[used] = np.arange(len(used))
    Pc, Fc = P[used], remap[F[faces]]
    centres = Pc[Fc].mean(1); nrm = VN[used]
    best = np.full(len(faces), -1); best_w = np.zeros(len(faces))
    per = []
    for v in scalp.views:
        if v['pitch']:
            continue
        pic, fg, root = v['pic'], v['fg'] > 0.5, v['root']
        core = root > 0.5
        band = ndimage.binary_dilation(core, iterations=STRANDS_BAND_PX[0]) & ~ndimage.binary_erosion(core, iterations=STRANDS_BAND_PX[1]) & fg
        skin = fg & (root < 0.1)
        est = np.stack([ndimage.gaussian_filter(pic[..., k] * skin, 25) for k in range(3)], -1) / np.maximum(ndimage.gaussian_filter(skin.astype(float), 25), 1e-3)[..., None]
        root_lum = np.median((pic @ lum_w)[core]) if core.any() else 60.0
        dark = (est @ lum_w - pic @ lum_w) / np.maximum(est @ lum_w - root_lum, 1.0)
        alpha = np.maximum(smooth(STRANDS_DARK[0], STRANDS_DARK[1], dark) * band, smooth(0.5, 0.7, root) * fg)
        # Nothing round the eyes: the brows and the lashes are dark against the skin as the strands are.
        yy_, xx_ = np.mgrid[0:alpha.shape[0], 0:alpha.shape[1]]
        epx_, _ = project(pose_model(np.asarray(eyes), v['yaw'], 0, pivot, scalp.collar_y)); epx_ = epx_ * v['scale'] + v['off']
        for ex_, ey_ in epx_:
            alpha[((xx_ - ex_) / STRANDS_EYE_PX[0]) ** 2 + ((yy_ - ey_ + STRANDS_EYE_PX[2]) / STRANDS_EYE_PX[1]) ** 2 < 1] = 0.0
        colour = np.clip(est + (pic - est) / np.maximum(alpha, 0.25)[..., None], 0, 255)
        at, _ = front_zbuffer(pose_model(Pc, v['yaw'], 0, pivot, scalp.collar_y), Fc)
        cp = pose_model(centres, v['yaw'], 0, pivot, scalp.collar_y); px, z = project(cp)
        n = pose_model(centres + nrm[Fc].mean(1), v['yaw'], 0, pivot, scalp.collar_y) - cp; n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
        cam = np.array([0.0, 0.0, D]) - cp; cam /= np.linalg.norm(cam, axis=1, keepdims=True) + 1e-12
        ppx = px * v['scale'] + v['off']; H_, W_ = band.shape
        inside = (ppx[:, 0] >= 1) & (ppx[:, 0] < W_ - 2) & (ppx[:, 1] >= 1) & (ppx[:, 1] < H_ - 2)
        yi = np.clip(ppx[:, 1].astype(int), 0, H_ - 1); xi = np.clip(ppx[:, 0].astype(int), 0, W_ - 1)
        covered = ndimage.binary_dilation(alpha > 0.05, iterations=3)
        in_band = np.where(is_grid, covered[yi, xi], (covered & ndimage.binary_dilation(band, iterations=3))[yi, xi])
        drawn = ndimage.map_coordinates(ndimage.maximum_filter(alpha, size=5), [ppx[:, 1], ppx[:, 0]], order=1, mode='constant')
        w = (n * cam).sum(1) * (z <= at(px) + SCALP_DEPTH_TOL) * inside * in_band * (STRANDS_HAIR_BIAS + drawn)
        take = w > np.maximum(best_w, np.where(is_grid, STRANDS_FACING_GRID, STRANDS_FACING))
        best[take] = len(per); best_w[take] = w[take]
        rows = np.where((alpha > 0.05).any(1))[0]; cols = np.where((alpha > 0.05).any(0))[0]
        if not len(rows):
            per.append(None); continue
        box = (max(cols[0] - 4, 0), max(rows[0] - 4, 0), min(cols[-1] + 5, W_), min(rows[-1] + 5, H_))
        per.append(dict(view=v, alpha=alpha, colour=colour, box=box))
    sel = np.where(best >= 0)[0]
    if not len(sel):
        return None
    # the atlas: every view's band crop on a shelf, scaled down together if they overflow
    crops = [(i, p['box'][2] - p['box'][0], p['box'][3] - p['box'][1]) for i, p in enumerate(per) if p is not None and (best[sel] == i).any()]
    scale = 1.0; x = y = shelf = 0; place = {}
    for i, w_, h_ in crops:
        if x + w_ > STRANDS_ATLAS:
            x = 0; y += shelf; shelf = 0
        place[i] = (x, y); x += w_; shelf = max(shelf, h_)
    total_h = y + shelf
    if total_h > STRANDS_ATLAS:
        scale = STRANDS_ATLAS / total_h
    atlas = np.zeros((STRANDS_ATLAS, STRANDS_ATLAS, 4), np.uint8)
    for i, w_, h_ in crops:
        p = per[i]; x0, y0, x1, y1 = p['box']; ax, ay = place[i]
        rgba = np.dstack([p['colour'][y0:y1, x0:x1], p['alpha'][y0:y1, x0:x1] * 255]).astype(np.uint8)
        if scale < 1.0:
            im = Image.fromarray(rgba).resize((max(int(w_ * scale), 1), max(int(h_ * scale), 1)), Image.LANCZOS); rgba = np.asarray(im)
            ax, ay = int(ax * scale), int(ay * scale)
        h2, w2 = rgba.shape[:2]; atlas[ay:ay + h2, ax:ax + w2] = rgba
        place[i] = (ax, ay)
    pos, nr, uv, idx = [], [], [], []
    for i, p in enumerate(per):
        mine = sel[best[sel] == i]
        if p is None or not len(mine):
            continue
        v = p['view']; x0, y0 = p['box'][:2]; ax, ay = place[i]
        verts = Fc[mine].reshape(-1)
        q = pose_model(Pc[verts], v['yaw'], 0, pivot, scalp.collar_y); px, _ = project(q)
        ppx = px * v['scale'] + v['off']
        u = (ax + (ppx[:, 0] - x0) * scale) / STRANDS_ATLAS; vv = (ay + (ppx[:, 1] - y0) * scale) / STRANDS_ATLAS
        pos.append(Pc[verts] + nrm[verts] * STRANDS_LIFT); nr.append(nrm[verts]); uv.append(np.stack([u, vv], 1))
    posw = np.concatenate(pos)
    print('hairline strands:', len(sel), 'faces on the overlay, views', sorted({int(b) for b in best[sel]}), 'atlas scale', round(scale, 3))
    return dict(pos=posw, nrm=np.concatenate(nr), uv=np.concatenate(uv), idx=np.arange(len(posw), dtype=np.uint32), material=0, material_name='strands', tex=atlas)
DETAIL_GAIN = 3.0       # the grain's amplification on the skin

def rot_y(a):
    c, s_ = math.cos(math.radians(a)), math.sin(math.radians(a))
    return np.array([[c, 0, s_], [0, 1, 0], [-s_, 0, c]])

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

def lock_mask_src(src):
    """The photo's hair as locks: the hair rule's pixels (blue, or dark) with the eyes, brows and mouth taken out
    (blue or dark too), the gaps between a lock's strands closed over HAIR_LOCK_CLOSE px. The face mask keeps
    strands under 10 px as face, and by it the bangs' lock's thin end over the temple was cut out of the hair
    layer (the owner's hole in the bangs, twice); by this mask the lock stays whole and is cut only round it."""
    a = np.asarray(src).astype(float)
    m = (a[..., 2] > a[..., 0] + 12) | (a.sum(-1) < 150)
    yy, xx = np.mgrid[0:a.shape[0], 0:a.shape[1]]
    for x, y, rx, ry in FEATURES:
        m &= ~(((xx - x) / (rx * 1.1)) ** 2 + ((yy - y) / (ry * 1.1)) ** 2 < 1)
    for b in BROWS:          # the brows' outer thirds lie outside the eye ellipses
        m &= ~((xx > b['x0'] - 12) & (xx < b['x1'] + 12) & (np.abs(yy - b['y']) < 28))
    m = ndimage.binary_closing(ndimage.binary_opening(m, iterations=1), iterations=HAIR_LOCK_CLOSE)
    return ndimage.binary_dilation(m, iterations=2)

def photo_offset(frame, photo):
    """Where the photo sits in a staged view that holds it pixel for pixel (mvstage.py pads it into a larger
    square): the (x, y) of its top-left corner, by matching a patch round her right eye."""
    from numpy.lib.stride_tricks import sliding_window_view
    f = np.asarray(frame, float).mean(-1); p = np.asarray(photo, float).mean(-1)
    y0, x0, n = 460, 460, 96
    t = p[y0:y0 + n, x0:x0 + n]
    ry, rx = f.shape[0] - p.shape[0] + 1, f.shape[1] - p.shape[1] + 1
    best = (np.inf, 0, 0)
    for dy in range(ry):
        w = sliding_window_view(f[y0 + dy:y0 + dy + n, x0:x0 + n + rx - 1], (n, n))[0]
        d = np.abs(w - t).mean((1, 2)); k = int(d.argmin())
        if d[k] < best[0]:
            best = (float(d[k]), k, dy)
    assert best[0] < 2.0, f'the photo was not found in the staged view (best mean difference {best[0]:.1f})'
    return best[1], best[2]

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

def face_surface(P, F, src, hair=None, hair_src=None):
    """Replace the scan's face with one dense, smooth height field over the face, at the scan's own
    depth (smoothed, with the eye sockets filled). The generator's eyes are hollows and its face is
    crumpled; the picture is the face, so a smooth surface under it reads true, lids slide cleanly
    over it, and no seams are left round the eyes. Hair and the sides keep the scan."""
    inp, zc = project(P)
    # The front-most depth of the scan, densely sampled over its triangles.
    rng = np.random.default_rng(4)
    bary = rng.dirichlet((1, 1, 1), size=24)
    zraw = np.full((N, N), np.inf)
    # The head's own faces only: with the hair in the model the front-most depth over the forehead was
    # the bangs' surface, and the grid, fitted to it, drew the forehead out to the hair as a plate
    # (owner: "ingen jättebula i pannan, någonsin"). The head's shape never depends on the hair.
    Fh = F if hair is None else F[~hair]
    for chunk in np.array_split(np.arange(len(Fh)), 8):
        pts = np.einsum('bk,fkd->fbd', bary, P[Fh[chunk]]).reshape(-1, 3)
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
    # Not the hair: this is the bald picture's oval, which covers the temples under the photo's hair, and
    # the hair over them went with the skin (the grid's bald skin lay over the hair since the face took
    # the bald picture). Hair over the face has its own rule below, with the haired photo's mask.
    if hair is not None:
        over &= ~hair
    # Not the chin's underside: seen from the front it lies behind the grid's lowest rows, and dropping
    # its front part left a notch under the chin in profile.
    fc_px, _ = project(P[F].mean(1)); fc_s = (fc_px - np.array([OX, OY])) / S
    fn_ = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]]); fn_ /= np.linalg.norm(fn_, axis=1, keepdims=True) + 1e-12
    over &= ~((np.abs(fn_[:, 1]) > 0.6) & (fc_s[:, 1] > CHIN_Y - 0.0042 * (fc_s[:, 0] - 626.0) ** 2 - 25))
    if hair is not None:
        # Hair over the face is the picture's own (thin strands stay in it): scan strands left at the
        # grid's edge poked through it as dark specks. The face here is the haired photo's (skin the
        # photo shows): the bald picture's oval covers the temples under the photo's hair, and with it
        # every hair face over the temples went and the grid's bald skin lay over the hair.
        # (Not with the projector: its alpha cuts the layer where the photo shows skin, strand by
        # strand, and this rule chopped the bangs' lock where it thins out over the forehead, since
        # strands under 10 px stay inside the face mask.)
        wide = ndimage.binary_dilation(face_mask_crop(hair_src) if hair_src is not None else face, iterations=3)
        drop_face = hair & wide[vi2[:, 1], vi2[:, 0]][F].any(1) & (vz < zs[vi2[:, 1], vi2[:, 0]] + 0.01)[F].any(1)
        if hair_src is not None and PROJ_HAIR:
            # Never the photo's own strands: the mask keeps strands under 10 px as face, and with them the lock's
            # thin end over the temple went, a hole in the bangs showing the slab's inner wall (owner, twice).
            # The eyes, brows and mouth (dark or blue too) stay face, so nothing of the hair lies over them.
            on_strand = src_mask_to_crop(lock_mask_src(hair_src))[vi2[:, 1], vi2[:, 0]][F].any(1)
            print('face-mask drop rule: hair faces spared on the photo\'s strands', int((drop_face & on_strand).sum()))
            drop_face &= ~on_strand
        over |= drop_face
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
    # Only onto the head's surface where it is the same surface: below the chin's silhouette the scan's
    # front is the neck, 3 cm back, and feathering onto it bent the grid's lower rows back into a flat
    # shelf under the chin (a flap on every turn and tilt).
    head = ndimage.gaussian_filter(np.where(np.abs(head - zs) < 0.008, head, zs), 2)
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
    # The mouth: the grid is cut along the closed lips' meeting line so the lips can part. Faces with
    # corners on both sides of it go, and those corners move onto the line from their own side, so at
    # rest the cut is closed and the picture unchanged; each corner remembers its side for the morphs.
    side = np.zeros(len(px))
    c_ = MOUTH_SHAPES['closed']
    sxy_m = (px - np.array([OX, OY])) / S
    u_m = 2 * (sxy_m[:, 0] - c_['x0']) / (c_['x1'] - c_['x0']) - 1
    line = np.interp(u_m, MU, c_['A'])
    upper = sxy_m[:, 1] < line
    local_m = gf - len(P)
    near_line = (np.abs(u_m) < 1.05) & (np.abs(sxy_m[:, 1] - line) < 6)
    cut = near_line[local_m].all(1) & upper[local_m].any(1) & ~upper[local_m].all(1)
    lip_edge = np.unique(local_m[cut])
    side[lip_edge] = np.where(upper[lip_edge], 1, -1)
    on_line = sxy_m[lip_edge].copy(); on_line[:, 1] = line[lip_edge]
    px[lip_edge] = src_to_crop(on_line)
    gp[lip_edge] = unproject(px[lip_edge], ndimage.map_coordinates(zg, [px[lip_edge, 1], px[lip_edge, 0]], order=1))
    gf = gf[~cut]
    lm = gf - len(P)
    e_m = np.concatenate([lm[:, [0, 1]], lm[:, [1, 2]], lm[:, [2, 0]]])
    _, inv_m, cnt_m = np.unique(np.sort(e_m, 1), axis=0, return_inverse=True, return_counts=True)
    b_m = e_m[cnt_m[inv_m.reshape(-1)] == 1]
    b_m = b_m[(side[b_m[:, 0]] != 0) & (side[b_m[:, 0]] == side[b_m[:, 1]])]
    b_m = b_m[np.linalg.norm(px[b_m[:, 0]] - px[b_m[:, 1]], axis=1) > 1e-6]
    SLIT_RIM[:] = [gp[b_m[:, 0]], gp[b_m[:, 1]], px[b_m[:, 0]], px[b_m[:, 1]], side[b_m[:, 0]]]
    print('mouth cut', cut.sum(), 'faces;', (side > 0).sum(), 'upper and', (side < 0).sum(), 'lower edge corners;', len(b_m), 'rim edges')
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
    keep_v &= side == 0                     # the lips' cut has walls of its own (mouth_inside)
    border = border[keep_v[border[:, 0]] & keep_v[border[:, 1]]]
    rim = np.unique(border)
    back = np.full(len(gp), -1); back[rim] = len(gp) + np.arange(len(rim))
    _, gz = project(gp[rim])
    gp = np.concatenate([gp, unproject(px[rim], gz + 0.015)])
    gn = np.concatenate([gn, gn[rim]])
    a0, b0 = border[:, 0], border[:, 1]
    gf = np.concatenate([gf, np.concatenate([np.stack([b0, a0, back[a0]], 1), np.stack([b0, back[a0], back[b0]], 1)]) + len(P)])
    print('face grid skirt', len(border), 'edges')
    SKIRT_EDGES[:] = [len(border)]
    GRID_NORMALS[:] = [gn]
    SIDE[:] = [np.concatenate([np.zeros(len(P)), side, np.zeros(len(gp) - len(side))])]
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
    HP, HF, horig = load_scan(f'{work}/{HAIR_GLB}'); HP, HF = outer_surface(HP, HF)
    BP, BF, borig = load_scan(f'{work}/bald/kairo-bald-s42.glb', 90000); BP, BF = outer_surface(BP, BF)
    # Each run is pixel-aligned to its own crop and the two source images to each other, so the bald
    # mesh moves by the difference of the crops (51 px, about 4.5 cm at the head).
    BP = reframe(BP, OY - BALD_OY); borig = (reframe(borig[0], OY - BALD_OY), borig[1])
    BP, BF = subdivide_neck(BP, BF)
    hair_tex = texture_sampler(horig, f'{work}/{HAIR_TEX}')
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
    if not PROJ_HAIR:      # with the projector the alpha mask cuts the layer at the photo's skin; these rules took the skirt the mask is for
        hair &= ~((pc[:, 0] > pc[:, 2] + 30) & (hz < at(cp) + 0.015))
    # Nor in front of it, at any depth: strands hanging in front of the temple that the photo shows
    # as skin took skin colour and broke up into beige shards once the head turned.
    skin_px = (photo[..., 0] > photo[..., 2] + 30) & (photo.sum(-1) > 200)
    skin_px = ndimage.binary_erosion(skin_px, iterations=3)
    # (Within 2 cm of the front only: at 6 cm the rule took the temple's side back to the ear with it,
    # and the scalp showed there from the side. Those strands no longer take skin colour: the hair's
    # texture is the photo's hair palette whatever the generator painted.)
    if not PROJ_HAIR:
        hair &= ~(skin_px[sp[:, 1], sp[:, 0]] & (hz < at(cp) + HAIR_FACE_DEPTH))
    # The turtleneck stays put while the head turns: the bald run's knit, below the chin.
    # (Up to 2.5 cm above the collar line only: higher, the shadowed skin under the chin read as knit,
    # held the chin's underside to the body and tore it into a flap on every turn.)
    knit = vote(BP, BF, ~(bcol[:, 0] > bcol[:, 2] + 15)) & (bc[:, 1] < collar_y + 0.025)
    # The neck inside the collar stays with the body: turning with the head it swept out through the
    # collar's side. The split follows the collar's rim, which rises toward the back.
    ring = bc[~knit & (np.abs(bc[:, 1] - collar_y) < 0.015)]
    axis = np.array([ring[:, 0].mean(), ring[:, 2].mean()])
    # The neck's own axis, midway between throat and nape and between its sides, just above the
    # collar: the ring's centroid sat near the throat (the collar hides the nape at that height), and
    # turning about it swung the back of the neck sideways, as if it were broken.
    # The throat only from upright faces: this chin's underside comes down to the collar and lay in
    # the middle column at that height, so the "throat" was the chin's edge and the axis sat at the
    # throat's surface. Then the back of the head swung sideways on every turn.
    bfn0 = np.cross(BP[BF[:, 1]] - BP[BF[:, 0]], BP[BF[:, 2]] - BP[BF[:, 0]])
    upright = np.abs(bfn0[:, 1]) < 0.6 * (np.linalg.norm(bfn0, axis=1) + 1e-12)
    level = ~knit & (np.abs(bc[:, 1] - (collar_y + 0.02)) < 0.005)
    band = ~knit & upright & (bc[:, 1] > collar_y - 0.01) & (bc[:, 1] < collar_y + 0.025)
    mid = level & (np.abs(bc[:, 0] - axis[0]) < 0.015)
    mid_band = band & (np.abs(bc[:, 0] - axis[0]) < 0.015)
    if mid.sum() > 4 and mid_band.sum() > 4:
        nape_z = bc[mid, 2].min()
        throat_z = np.percentile(bc[mid_band & (bc[:, 2] < nape_z + 0.3), 2], 98)
        # The head turns about the top of the spine, which runs along the back of the neck: the axis
        # sits two fifths of the way from the nape to the throat, where the ear canals are.
        axis_z = nape_z + 0.4 * (throat_z - nape_z)
        side = level & (np.abs(bc[:, 2] - axis_z) < 0.03)
        axis = np.array([(np.percentile(bc[side, 0], 2) + np.percentile(bc[side, 0], 98)) / 2 if side.sum() > 4 else axis[0], axis_z])
        print('neck nape/throat z', round(float(nape_z), 3), round(float(throat_z), 3))
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
    lo = rim + 0.006
    # The neck's own cylinder, measured just above the collar, and per direction the height where the
    # surface leaves it (the jaw in front, its angle at the sides, the skull's base behind): that is
    # where the head begins. (The lowest down-facing face, used before, was the collar's own fold, so
    # the neck twisted over 2 cm only.)
    r_cyl = np.full(36, r_neck)
    sec_f = sector(bc)
    for k_ in range(36):
        m = ~knit & (sec_f == k_) & (bc[:, 1] > rim[k_] + 0.01) & (bc[:, 1] < rim[k_] + 0.035)
        if m.sum() > 5:
            r_cyl[k_] = np.percentile(radius[m], 90)
    r_cyl = ndimage.gaussian_filter1d(ndimage.median_filter(r_cyl, 5, mode='wrap'), 1.5, mode='wrap')
    # The chin's underside, shadowed and as low as the collar in front, is not knit: well outside the
    # neck and above the collar line it is the head.
    knit &= ~((radius > r_cyl[sec_f] + 0.03) & (bc[:, 1] > collar_y - 0.01))
    hi = lo + 0.08
    for k_ in range(36):
        m = ~knit & (sec_f == k_) & (bc[:, 1] > rim[k_] + 0.01) & (bc[:, 1] < rim[k_] + 0.16)
        if m.sum() < 10:
            continue
        ys = ((bc[m, 1] - rim[k_]) / 0.005).astype(int)
        prof = np.full(33, np.nan); np.fmax.at(prof, ys, radius[m])
        out = np.where(prof[4:] > r_cyl[k_] + 0.015)[0]
        if len(out):
            hi[k_] = rim[k_] + (out[0] + 4) * 0.005
    hi = ndimage.gaussian_filter1d(ndimage.median_filter(np.maximum(hi, lo + 0.04), 5, mode='wrap'), 1.5, mode='wrap')
    print('neck top per sector (cm above the collar)', np.round((hi - rim) * 100).astype(int))
    # The weights: 0 on the collar, 1 on the head (above the neck's top, or outside its cylinder:
    # the jaw, the chin and its underside), and in between the smoothest field over the surface (a
    # harmonic one), so the neck twists evenly along its length and no band of faces shears. Rules
    # by height and distance alone left the under-chin half-weighted, a flap on every turn.
    from scipy.sparse import coo_matrix, diags
    from scipy.sparse.linalg import spsolve
    vs = ((np.arctan2(BP[:, 0] - axis[0], BP[:, 2] - axis[1]) + math.pi) / (2 * math.pi) * 36).astype(int) % 36
    vr = np.hypot(BP[:, 0] - axis[0], BP[:, 2] - axis[1])
    knit_v = np.zeros(len(BP), bool); knit_v[BF[knit].reshape(-1)] = True
    used_v = np.zeros(len(BP), bool); used_v[BF.reshape(-1)] = True
    # (Low but outside the neck's cylinder is the chin's underside, which comes down to the collar's
    # height in front: that belongs to the head.)
    zero = knit_v | ((BP[:, 1] < lo[vs]) & ((vr < r_cyl[vs] + 0.012) | (BP[:, 1] < collar_y - 0.01)))
    one = ~zero & ((BP[:, 1] > hi[vs]) | (vr > r_cyl[vs] + 0.012))
    # The jaw's underside shares the twist: it is the skin between the chin and the throat, and this
    # chin comes down to the collar, so with the underside rigid the whole turn happened in a 2 cm
    # band of upright skin under the jaw's angle and the neck creased like a rope. (The flap it once
    # made came from the axis at the throat: the chin swung far while the throat stood still.)
    bvn = vertex_normals(BP, BF)
    bvn *= np.sign(((BP - np.array([axis[0], 0.0, axis[1]])) * bvn).sum(1, keepdims=True) + 1e-9)     # outward
    underside = (bvn[:, 1] < -0.35) & (BP[:, 2] > axis[1]) & (np.abs(BP[:, 0] - axis[0]) < 0.17) & (BP[:, 1] < collar_y + 0.14)
    one &= ~underside
    # The face (and the chin with it) is rigid: everything the photo's face covers, near the front.
    fm = ndimage.binary_dilation(face_mask_src(Image.open(f'{work}/kairo-front.png').convert('RGB')), iterations=12)
    vp, vz = project(BP); vsrc = np.clip(((vp - np.array([OX, OY])) / S).astype(int), 0, fm.shape[0] - 1)
    zfront, _ = front_zbuffer(BP, BF)
    one |= ~zero & fm[vsrc[:, 1], vsrc[:, 0]] & (vz < zfront(vp) + 0.03)
    # The chin's underside: this chin comes down to the collar's height, so the rules above (the
    # neck's cylinder is measured where the chin already is) held it to the body while the chin turned.
    # It is free instead: its weight runs smoothly from the chin (1) back to the throat (0).
    bfn = np.cross(BP[BF[:, 1]] - BP[BF[:, 0]], BP[BF[:, 2]] - BP[BF[:, 0]])
    level = ~knit & (np.abs(bfn[:, 1]) > 0.5 * np.linalg.norm(bfn, axis=1)) & (np.abs(bc[:, 1] - collar_y) < 0.02) & (bc[:, 2] > axis[1] + 0.06)
    under = np.zeros(len(BP), bool); under[BF[level].reshape(-1)] = True
    under &= ~knit_v
    print('chin underside:', int(under.sum()), 'vertices freed from the body')
    zero &= ~under
    free = used_v & ~zero & ~one
    e = np.concatenate([BF[:, [0, 1]], BF[:, [1, 2]], BF[:, [2, 0]]]); e = np.concatenate([e, e[:, ::-1]])
    A = coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(len(BP), len(BP))).tocsr(); A.data[:] = 1
    L = diags(np.asarray(A.sum(1)).ravel()) - A
    fi = np.where(free)[0]; on = np.where(one)[0]
    wv = np.where(one, 1.0, 0.0)
    if len(fi):
        rhs = -(L[fi][:, on] @ np.ones(len(on)))
        # A little regularisation: islands touching neither boundary made the system singular (NaN
        # weights, a neck that vanished).
        from scipy.sparse import identity
        sol = spsolve((L[fi][:, fi] + identity(len(fi)) * 1e-6).tocsc(), rhs)
        wv[fi] = np.clip(np.nan_to_num(sol, nan=1.0), 0, 1)
        print('neck weight NaNs before fix', int(np.isnan(sol).sum()))
    print('neck weights: free', len(fi), 'head', len(on), 'collar', int(zero.sum()))
    vtree = cKDTree(BP)
    def twist(p):
        return wv[vtree.query(p)[1]]
    neck = ~knit & (wv[BF] < 0.999).any(1)
    # Loose bits of the scan (joined to neither the head nor the body) go. Level faces at the collar's
    # height in front of the neck went too once, as a "web" that flapped on turns; they are the chin's
    # own underside (this chin comes down to the collar's height), and without them the profile had a
    # notch under the chin and, looking up, the inside of the head showed as a dark patch. Outside the
    # neck's cylinder they now turn rigidly with the head.
    web = np.zeros(len(BF), bool)
    from scipy.sparse.csgraph import connected_components
    keepf = ~web
    e_ = np.concatenate([BF[keepf][:, [0, 1]], BF[keepf][:, [1, 2]]])
    ncomp, lab_v = connected_components(coo_matrix((np.ones(len(e_)), (e_[:, 0], e_[:, 1])), shape=(len(BP), len(BP))), directed=False)
    comp_f = lab_v[BF[:, 0]]
    sizes = np.bincount(comp_f[keepf], minlength=ncomp)
    web |= keepf & (sizes[comp_f] < 200)
    print('loose scan bits removed:', int(web.sum()), 'faces')
    WEB[:] = [web]
    top_y = np.median(hi)
    NECK_SPAN[:] = [twist, collar_y]
    print('compose: bald', len(BF), 'knit', knit.sum(), 'hair', hair.sum(), 'collar y', round(float(collar_y), 3))
    if BALD:
        hair[:] = False
    SP_, SF, hair_occ = hair_shell(HP, HF[hair]) if hair.any() else (np.zeros((0, 3)), np.zeros((0, 3), np.int32), None)
    # Strands far outside the hair's closed volume are the scan's loose shards: jagged tips round the
    # top and the temples, with the background showing between them.
    if hair.any():
        d_out = cKDTree(SP_).query(hc)[0]
        # Inside the closed volume every strand stays (the shell at the strands' envelope has an inner
        # surface too, and the distance to it is unsigned); outside, only within HAIR_REACH.
        occ_, lo_, res_ = hair_occ
        vi = ((hc - lo_) / res_).astype(int); ok_ = (vi >= 0).all(1) & (vi < np.array(occ_.shape)).all(1)
        inside = np.zeros(len(hc), bool); inside[ok_] = occ_[vi[ok_, 0], vi[ok_, 1], vi[ok_, 2]]
        print('hair distance to its shell (mm) p50/p90/p99', np.round(np.percentile(d_out[hair], [50, 90, 99]) * 1000, 1), 'inside the volume', int((hair & inside).sum()), 'of', int(hair.sum()))
        hair &= inside | (d_out < HAIR_REACH)
        hair = vote(HP, HF, hair, r=0.006) & hair
        # Anchored by the scalp or the hair's main surface only: the shell's closing bridges 15 mm, so a
        # shard that far out got a bump of shell round it and counted as attached.
        keep_ = loose_pieces(HP, HF[hair], BP[np.unique(BF[~knit])])
        hair[np.where(hair)[0][~keep_]] = False
        SP_, SF, hair_occ = hair_shell(HP, HF[hair])      # the envelope without those shards' bumps
        if not HAIR_SHELL_FACES:
            SF = SF[:0]
    if PROJ_HAIR and hair.any():
        # Nothing of the hair layer inside the head: the envelope is a closed shell, and its inner and
        # far surfaces lie inside the skull, behind the face grid. Hidden there except through the eye
        # openings, where they showed as fragments over the irises once the face-mask drop rule was off
        # (owner: "massa saker i ögonen"). The head's volume is voxelised and filled as dark_core does.
        res_h = 0.004
        lo_h = BP.min(0) - 0.02; shape_h = np.ceil((BP.max(0) + 0.02 - lo_h) / res_h).astype(int) + 1
        rng_h = np.random.default_rng(7)
        pts_h = np.concatenate([np.einsum('k,fkd->fd', w, BP[BF]) for w in rng_h.dirichlet((1, 1, 1), size=8)] + [BP])
        occ_h = np.zeros(shape_h, bool); ih = ((pts_h - lo_h) / res_h).astype(int); occ_h[ih[:, 0], ih[:, 1], ih[:, 2]] = True
        occ_h = ndimage.binary_closing(occ_h, structure=ndimage.generate_binary_structure(3, 1), iterations=1)
        for y_ in range(shape_h[1]):
            occ_h[:, y_, :] = ndimage.binary_fill_holes(occ_h[:, y_, :])
        # Well inside: the volume eroded by HAIR_IN_HEAD_VOXELS. (Tested against the distance to the scan's
        # surface samples, outer faces of the hair hugging the nape went one by one, since the bald scan's
        # samples are sparse there: single missing triangles showing the neck from behind.)
        occ_h = ndimage.binary_erosion(occ_h, iterations=HAIR_IN_HEAD_VOXELS)
        hv_ = ((hc - lo_h) / res_h).astype(int); okh = (hv_ >= 0).all(1) & (hv_ < np.array(shape_h)).all(1)
        in_head = np.zeros(len(hc), bool); in_head[okh] = occ_h[hv_[okh, 0], hv_[okh, 1], hv_[okh, 2]]
        print('hair faces inside the head dropped:', int((hair & in_head).sum()), 'of', int(hair.sum()))
        hair &= ~in_head
    # Not over the face: where the photo shows skin near the front, no hair lies.
    sc_ = SP_[SF].mean(1); cp, sz = project(sc_); sp = np.clip(((cp - np.array([OX, OY])) / S).astype(int), 0, photo.shape[0] - 1)
    pc = photo[sp[:, 1], sp[:, 0]]
    SF = SF[~((pc[:, 0] > pc[:, 2] + 30) & (sz < at(cp) + HAIR_FACE_DEPTH))]
    CP, CF = dark_core(BP, BF)
    cc = CP[CF].mean(1)
    # None across the neck: the body's part stayed put while the head turned and came out under the
    # chin as a flap; the head's part would come out through the twisting neck.
    cv = ((np.arctan2(cc[:, 0] - axis[0], cc[:, 2] - axis[1]) + math.pi) / (2 * math.pi) * 36).astype(int) % 36
    keep_c = (cc[:, 1] < collar_y + 0.01) | (cc[:, 1] > hi[cv] + 0.02)
    CF = CF[keep_c]; cc = cc[keep_c]
    P = np.concatenate([BP, HP, SP_, CP])
    keep_b = ~WEB[0]
    F = np.concatenate([BF[keep_b], HF[hair] + len(BP), SF + len(BP) + len(HP), CF + len(BP) + len(HP) + len(SP_)])
    label = np.concatenate([np.where(knit, KNIT, np.where(neck, NECK, SKIN))[keep_b], np.full(hair.sum() + len(SF), HAIR),
                            np.where(cc[:, 1] < collar_y + 0.01, CORE_BODY, CORE)])
    SHELL[:] = [np.concatenate([np.zeros(keep_b.sum() + hair.sum(), bool), np.ones(len(SF), bool), np.zeros(len(CF), bool)])]
    used = np.unique(F); remap = np.full(len(P), -1); remap[used] = np.arange(len(used))
    styled = hair_style(horig, work, Image.open(f'{work}/kairo-front.png').convert('RGB'), axis)
    hair_look = hair_projector(work, HP, HF[hair], axis, fallback_style=styled, head_pts=BP[np.unique(BF[~knit])]) if PROJ_HAIR else styled
    hair_look.style = styled           # the palette's strands at any base luminance, for the root cap
    photo_hair = ((photo[..., 2] > photo[..., 0] + 12) | (photo.sum(-1) < 150)) & ~skin_px
    face_px = ndimage.binary_dilation(face_mask_src(Image.open(f'{work}/kairo-front.png').convert('RGB')), iterations=10)
    bald_face = ndimage.binary_dilation(face_mask_src(Image.open(f'{work}/bald/kairo-bald-front.png').convert('RGB')), iterations=6)
    face_d = ndimage.distance_transform_edt(~face_px)
    photo_env = ndimage.binary_closing(photo_hair, iterations=HAIR_GAPS) & ~ndimage.binary_erosion(face_px, iterations=16)
    hair_look.photo_env = photo_env    # the photo's hair with the gaps between its strands, for the hairline skirt's roots
    hair_points = hc[hair]; hair_cols = hair_look(hair_points) if hair.any() else hcol[hair]
    epx = src_to_crop([(626.0, IRISES[0][1])])
    eye_y = unproject(epx, at(epx))[0, 1]
    def hair_over(points):
        """1 where hair lies over a point of the skull (a ray out from the skull's centre meets the
        hair's closed volume within 10 cm). The hair layer stands 2-4 cm off this scalp, so nearness
        to a strand said 'bare' for most of it, and the scalp under the hair stayed skin-coloured
        with dark rings where the distance crossed the fade: beige between the strands."""
        if hair_occ is None:
            return np.zeros(len(points))
        occ, lo, res = hair_occ
        ctr = np.array([axis[0], eye_y + 0.07, axis[1]])
        d = points - ctr; d /= np.linalg.norm(d, axis=1, keepdims=True) + 1e-12
        hit = np.zeros(len(points), bool)
        for t in np.arange(0.0, 0.10, res * 0.7):
            i = ((points + d * t - lo) / res).astype(int)
            ok = (i >= 0).all(1) & (i < np.array(occ.shape)).all(1)
            hit[ok] |= occ[i[ok, 0], i[ok, 1], i[ok, 2]]
        return hit.astype(float)
    def hair_weight(points):
        """How much of the hair's own dark a point of the skull takes (0 bare, 1 under the hair)."""
        if not len(hair_points):
            return np.zeros(len(points))
        d = cKDTree(hair_points).query(points)[0]
        # Only the scalp, from eye level up: lower down the hair hangs over the cheeks, which stay skin.
        # Along the hairline the strip of skull between the face grid and the hair has no hair over it
        # and showed as beige slivers through the bangs: where the photo's hair (its closed envelope,
        # gaps between strands included) covers a point near the front, it is scalp under hair.
        cp_, z_ = project(points); sp_ = np.clip(((cp_ - np.array([OX, OY])) / S).astype(int), 0, photo_env.shape[0] - 1)
        under_photo = photo_env[sp_[:, 1], sp_[:, 0]] & (z_ > at(cp_) - 0.08)
        # Behind the face (its projection outside the photo's face) and above the brows the skull is
        # scalp whether or not a strand hangs over it: at the temple in front of the ear this scan has
        # no hair where the photo showed skin from the front, and the bare skin there read as a shaved
        # patch from the side.
        # (Feathered over 25 px of the photo from the face's edge and over 4 cm above the brows: as a
        # cut it drew a hard line down the temple.)
        behind_face = np.maximum(smooth(0.0, 25.0, face_d[sp_[:, 1], sp_[:, 0]]), smooth(0.015, 0.05, at(cp_) - z_))
        above_brow = smooth(eye_y + 0.005, eye_y + 0.045, points[:, 1])
        # Behind the ear canals the hair hangs to the nape (the owner's back and side views): the skull
        # there is scalp down to 2 cm above the collar, not only above the brows (skin showed pink
        # between the locks behind the ear from the side).
        behind_ear = smooth(axis[1] + 0.01, axis[1] - 0.02, points[:, 2]) * smooth(collar_y + 0.02, collar_y + 0.05, points[:, 1])
        w = np.maximum(np.maximum(hair_over(points), under_photo), smooth(0.025, 0.01, d)) * smooth(eye_y - 0.02, eye_y + 0.01, points[:, 1])
        w = np.maximum(w, behind_face * np.maximum(above_brow, behind_ear))
        # Not the skull's front under the face grid: in the grid's edge zone the grid sits a millimetre
        # in front of the skull's depth map and the skull's triangles poke through in blocks; skin on
        # skin that is invisible, dark on skin it drew a staircase along the hairline.
        under_grid = bald_face[sp_[:, 1], sp_[:, 0]] & (z_ > at(cp_) - 0.008)
        w = w * (1 - under_grid)
        # Nor where the owner's side and back views show skin (the temple below the bangs' edge, the neck below
        # the hair): the front photo's hair sweeps over the temple at a distance, and by it alone the temple was scalp.
        side_skin = getattr(hair_look, 'side_skin', None)
        if side_skin is not None:
            w = w * (1 - side_skin(points))
        # ...and fading out under the hair layer's edge (edge_fade): the darkening and the root cap end inside
        # the strand tips, never beside them.
        edge_fade = getattr(hair_look, 'edge_fade', None)
        if edge_fade is not None:
            w = w * edge_fade(points)
        return w
    override = {}        # build() may put the owner's scalp views here ('weight', 'colour'): the scalp from their pictures
    def bald_colour(points, gain=1.0):
        """The bald texture, turned to the hair's own dark under the hair: the scalp between strands."""
        col = bald_tex(points) * gain
        if not len(hair_points):
            return col
        w = override.get('weight', hair_weight)(points)[:, None]
        # Hair close to the head: the palette's dark with the strands, not the nearest strand's colour.
        look = override['colour'](points) if 'colour' in override else hair_look(points, base=SCALP_BASE)
        return col * (1 - w) + look * w
    bald_colour.override = override
    # The head turns and nods where a real one does: at the top of the spine (the atlas and axis
    # vertebrae), level with the ear canals and well behind the neck's middle, since the spine runs
    # along the back of the neck. The neck below twists over its length (the skinned neck), the chin
    # turns rigidly with the skull. Turning about the collar swung the whole head sideways and sheared
    # the chin against the neck.
    head = ~knit & (bc[:, 1] > collar_y + 0.12)
    reach = np.abs(bc[:, 0] - axis[0])
    ears_ = head & (reach > np.percentile(reach[head], 99))
    ear_y = bc[ears_, 1].mean() if ears_.any() else top_y + 0.06
    # On the neck's own axis (its middle): behind it, the skinned neck's vertices in front swung far
    # between the joints and folded out as flaps under the jaw on a turn.
    pivot = np.array([axis[0], ear_y - 0.01, axis[1]])
    print('head pivot at ear level', np.round(pivot, 3))
    return P[used], remap[F].astype(np.int32), label, bald_colour, hair_look, pivot, hair_weight

BALD = os.environ.get('KAIRO_BALD') == '1'   # build the head without the hair layer (layer by layer)
# The hair's source scan: the single-view run of the photo, or (KAIRO_HAIR=mv) the Pixal3D multiview run
# of the photo plus the owner's generated profiles and back, fitted into the single-view frame by tools/mvfit.py.
# KAIRO_HAIR=proj: the hair is the multiview volume's smooth envelope (tools/hairenv.py), painted per
# texel from the owner's generated views through Pixal3D's cameras (hair_projector).
PROJ_HAIR = os.environ.get('KAIRO_HAIR') == 'proj'
MV_HAIR = os.environ.get('KAIRO_HAIR') in ('mv', 'proj')
HAIR_GLB, HAIR_TEX = ((os.environ.get('KAIRO_ENV', 'mv-hair/kairo-env.glb'), 'mv-hair/kairo-env.png') if PROJ_HAIR else ('mv-hair/kairo-mv-fit.glb', 'mv-hair/kairo-mv_base.png')) if MV_HAIR else ('kairo-s42.glb', 'kairo-s42_base.png')
# (KAIRO_ENV names another envelope file under the work dir, so candidate envelopes can be built side by side.)
UNDER_CHIN_PHOTO = 0.0         # under the chin: share of the picture's own shadow (1 showed as a dark patch when the head tilted)
NECK_TWIST = 0.05              # the neck's shortest twisting length above the collar (model units)
WEB = []                       # bald-scan faces to drop (loose bits), set by compose
NECK_SPAN = []                 # (neck axis xz, collar rim y per sector, jaw underside y per sector), set by compose
NECK_OVERLAP = 0.012           # the neck's top this far is in both body and head, covering the seam on turns
HAIR_REACH = 0.0               # strands outside the hair's closed volume are dropped (at 6 mm the sheets outside the shell read as flaps and shards off the hair)
EAR_POCKET = 0.010             # the hair keeps this far from the auricle (below EAR_TOP_SHARE of the ear), so the ear sits in a pocket of hair instead of under bits of it
HAIR_HEAD_GAP = 0.015          # m: a hair face closer than this to the skull or the neck goes: the envelope's sheet lying against the head (the inner shell on the scalp); the scalp carries the owner's roots and hairline itself, and through the hair's gaps those show (the owner chose 15 mm of 8/12/15/20 in the lab)
HAIR_GRID_GAP = 0.025          # m: the same over the face grid (the forehead): at 15 mm thin strips of the forehead's slab stayed along the hairline and at the temples, which the owner marked
EAR_TOP_SHARE = 0.35           # the ear's top this share of its height may carry hair (the views show the hair's edge over the upper helix)
EAR_FRONT_MARGIN = 0.008       # over the ear's top, hair stays only this far outside the auricle's outer surface: in front of the ear, never on its rim (owner: in front, behind, or cut)
HAIR_SHELL_CLOSE = 1 if MV_HAIR else 5           # voxels (5 mm) of closing on the hair's volume: gaps between strand sheets up to twice this are hair (at 3 the fringe of sheets along the back and the crown's shards stood apart with the room between)
HAIR_SHELL_OPEN = 0 if MV_HAIR else 1            # voxels of opening on the hair's volume before the shell: spikes thinner than two voxels go
HAIR_SHELL_ERODE = 0           # voxels (5 mm) the shell sits inside the strands: 0 puts it at their envelope, filling the gaps between them at their own level
SHELL = []                     # per composed face: part of the hair's shell (set by compose)
HAIR_BEHIND_GRID_MAX = 0.06    # m behind the face grid beyond which a hair face is the back of the head, not something behind the eyes
HAIR_BEHIND_GRID = 0.002       # m behind the face grid's depth, within the face, beyond which a hair face is dropped (projected hair)
HAIR_IN_HEAD_VOXELS = 2        # 4 mm voxels the head's filled volume is eroded by before hair faces inside it are dropped (projected hair)
HAIR_FACE_DEPTH = 0.006 if MV_HAIR else 0.02         # strands and shell over the photo's skin go only this close behind the front (m)
HAIR_SHELL_FACES = not MV_HAIR    # the shell's faces fill the scan's gaps; the multiview hair is a closed volume already, and the shell hid its locks
HAIR_LOOSE = 0.0015            # a piece of hair mesh further than this from both the hair's main surface and the scalp is loose and goes
HAIR_GAPS = 16                 # source px: gaps between the photo's strands narrower than twice this take hair colour
HAIR_LOCK_CLOSE = 8            # source px: gaps between a lock's strands narrower than twice this are lock (lock_mask_src)

def subdivide_neck(P, F, edge=0.005):
    """The scan's faces from the collar to the chin split until no edge is longer than `edge`: the
    neck is skinned with weights that vary along it, and a decimated face spanning the chin's
    underside (weight 1) to the throat (near 0) stretched into a flap whenever the head turned."""
    import pymeshlab
    c = P[F].mean(1)
    cpx = src_to_crop([(MOUTH['cx'], COLLAR_Y)])
    zone = (c[:, 1] > -0.2) & (c[:, 1] < -0.06)
    ms = pymeshlab.MeshSet(); ms.add_mesh(pymeshlab.Mesh(vertex_matrix=P, face_matrix=F, f_scalar_array=zone.astype(float)))
    ms.meshing_repair_non_manifold_edges(method=0)
    ms.meshing_repair_non_manifold_vertices()
    ms.compute_selection_by_condition_per_face(condselect='fq > 0.5')
    ms.meshing_surface_subdivision_midpoint(iterations=4, threshold=pymeshlab.PureValue(edge), selected=True)
    m = ms.current_mesh()
    print('neck subdivided:', len(F), '->', m.face_number(), 'faces')
    return m.vertex_matrix(), m.face_matrix().astype(np.int32)

def loose_pieces(P, F, anchors):
    """Which faces of a hair mesh to keep: a connected piece joined to neither the hair's main surface
    nor the scalp (flakes and slivers hanging in the air a few millimetres off the hair, 350 of them
    in the scan, and the fragments the face grid's cut leaves) goes."""
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    if not len(F):
        return np.ones(0, bool)
    e = np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]])
    nc, lab = connected_components(coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(len(P), len(P))), directed=False)
    cf = lab[F[:, 0]]; sizes = np.bincount(cf, minlength=nc); main = sizes.argmax()
    main_tree = cKDTree(P[np.unique(F[cf == main])]); anchor_tree = cKDTree(anchors)
    drop = np.zeros(nc, bool)
    for c in np.where((sizes > 0) & (np.arange(nc) != main))[0]:
        pts = P[np.unique(F[cf == c])]
        if main_tree.query(pts)[0].min() > HAIR_LOOSE and anchor_tree.query(pts)[0].min() > HAIR_LOOSE:
            drop[c] = True
    keep = ~drop[cf]
    print('hair loose pieces dropped:', int(drop.sum()), 'of', int((sizes > 0).sum()), 'pieces,', int((~keep).sum()), 'faces')
    return keep

def hair_shell(P, F, res=0.005, erode=HAIR_SHELL_ERODE):
    """A closed surface just inside the hair (voxelised strands, closed and eroded): the gaps between
    the scan's strands showed the room through as white spots."""
    import pymeshlab
    from skimage import measure
    lo = P[F].reshape(-1, 3).min(0) - 0.03; shape = np.ceil((P[F].reshape(-1, 3).max(0) + 0.03 - lo) / res).astype(int) + 1
    rng = np.random.default_rng(5)
    pts = np.concatenate([np.einsum('k,fkd->fd', w, P[F]) for w in rng.dirichlet((1, 1, 1), size=16)])
    occ = np.zeros(shape, bool); i = ((pts - lo) / res).astype(int); occ[i[:, 0], i[:, 1], i[:, 2]] = True
    occ = ndimage.binary_closing(occ, structure=ndimage.generate_binary_structure(3, 1), iterations=HAIR_SHELL_CLOSE)
    if HAIR_SHELL_OPEN:
        # Opened by a voxel: a lone shard's voxels made spikes on the envelope, at the crown and the tips.
        occ = ndimage.binary_opening(occ, iterations=HAIR_SHELL_OPEN)
    if erode:
        occ = ndimage.binary_erosion(occ, iterations=erode)
    V, Fs, _, _ = measure.marching_cubes(ndimage.gaussian_filter(occ.astype(float), 1.0), 0.5)
    ms = pymeshlab.MeshSet(); ms.add_mesh(pymeshlab.Mesh(V * res + lo, Fs[:, ::-1].astype(np.int32)))
    ms.meshing_decimation_quadric_edge_collapse(targetfacenum=25000, preservenormal=True)
    m = ms.current_mesh()
    print('hair shell', m.face_number(), 'faces')
    return m.vertex_matrix(), m.face_matrix().astype(np.int32), (occ, lo, res)

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
    tex = np.zeros((size, size, 3), np.uint8); drawn = np.zeros((size, size), bool)
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
        drawn[py.reshape(-1), px.reshape(-1)] = True
    # The gutters between the cells take the nearest cell's colour: black, they bled into every
    # triangle's edge through the sampler's filtering and the JPEG's ringing, and a high-contrast
    # atlas (the hair's projected strands) read as cut glass.
    _, (iy, ix) = ndimage.distance_transform_edt(~drawn, return_indices=True)
    tex = tex[iy, ix]
    return uv, tex

HAIR_SKIRT_DIST = 0.012        # m from the face grid within which a near-front hair face is skirt (its own chart)
HAIR_CHART_SMOOTH = 0.03       # radius (m) the normals are averaged over for the chart choice
HAIR_CHART_ISLAND = 0.012      # radius (m): a face takes the chart most of its neighbourhood uses (no single-face islands)
HAIR_CHART_MIN_OWN = 0.3       # a face whose own normal leans less than this (cosine) to its chart's axis is baked in the chart it faces most instead
HAIR_CHART_FORESHORTEN = 0.75  # a face may use a chart that foreshortens it at most this much relative to its best chart (own normal)
HAIR_CHART_VISIBLE = 0.6       # share of a face's samples that must be outermost in a chart for the face to count as visible there
HAIR_FRONT_CHART_COS = 0.7     # faces in the front band go to the front chart only when they face it this squarely (at 0.5 the sheet in front of the ear, facing mostly sideways, was stretched twice over in it)
HAIR_CHART_TILE = 1152          # px per chart tile of the projected hair's texture (six box charts, the hairline chart, the side charts' second layer: 4 x 2 tiles)
HAIR_CHART_SECOND_OWN = 0.6     # a side-facing face hidden in its side chart goes to the second-layer chart unless its chart sees its own normal this squarely
HAIR_ALPHA_SPECK = (1000, 300)  # texels: smaller islands in the baked alpha (hair in a cut region; a cut inside the hair) take their surroundings
HAIR_CUT_SPECK_FACES = 3        # a connected group of this many cut faces or fewer, with uncut faces all round, is drawn uncut (a triangle of skin through the hair)
HAIR_ALPHA_OPEN = 3             # texels (at the hair's tile size) the baked alpha is opened and closed by: strand tips and notches under 2.4 mm go
HAIR_ALPHA_MEDIAN = 5           # texels of the median filter on the baked alpha (single-texel notches and specks along the edges go)
HAIR_ALPHA_BLUR = 1.2           # texels of blur on the baked alpha: the MASK cutoff draws a rounded edge instead of a staircase

def hair_charts(P, F, sample, shade, VN, alpha_fn=None, skirt=None, tile=None):
    """The projected hair's texture as six box charts (right, left, top, under, front, back: each face in
    the chart its normal faces most), baked by dense sampling with the outermost sample winning, the
    gaps filled from the nearest texel. A per-triangle atlas put every triangle's edge at a jump in
    the texture: the sampler took the coarsest mip level there and drew the mesh as grain."""
    T = tile or HAIR_CHART_TILE; W, H = 4 * T, 2 * T
    fn = VN[F].mean(1); fn /= np.linalg.norm(fn, axis=1, keepdims=True) + 1e-12
    # The chart from normals averaged over 3 cm: by each face's own normal the three-quarter regions
    # alternated between the side and the front chart triangle by triangle, and each chart's bake
    # (its own outermost-wins direction) gave them slightly different texels, a glitter of seams.
    fc_ = P[F].mean(1); tree_ = cKDTree(fc_)
    for _ in range(2):
        nb = tree_.query_ball_point(fc_, r=HAIR_CHART_SMOOTH)
        fn = np.array([fn[i].mean(0) for i in nb]); fn /= np.linalg.norm(fn, axis=1, keepdims=True) + 1e-12
    tile = np.array([(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1), (3, 0), (3, 1)])
    inplane = {0: (2, 1), 1: (2, 1), 2: (0, 2), 3: (0, 2), 4: (0, 1), 5: (0, 1), 6: (0, 1), 7: (2, 1)}
    lo = P[F].reshape(-1, 3).min(0) - 0.01; hi = P[F].reshape(-1, 3).max(0) + 0.01; span = (hi - lo).max()
    # Each face takes the chart it faces most among those in which it is not hidden: in a chart's
    # projection the outermost surface wins the texels, and a face under an overhang (the locks over the
    # ear, the hair's thickest side) showed the overhang's strands as slivers. Hidden is measured
    # against a coarse depth map per chart (1 cm cells, 8 mm tolerance).
    dirs = np.array([(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)], float)
    score = fn @ dirs.T                                     # how squarely each chart sees the face
    # Hidden is measured as the bake measures it: every face's samples into each chart's depth map at the
    # chart's own resolution, the outermost sample per texel winning; a face is hidden in a chart where under
    # HAIR_CHART_VISIBLE of its own samples win. (A coarse 1 cm map with an 8 mm tolerance missed the locks
    # overhanging the side of the head by a few millimetres, and the faces under them, baked in the top
    # chart, took the overhang's texels as flat facets.)
    hidden = np.zeros((len(F), 6), bool)
    rng_ = np.random.default_rng(2); b16 = rng_.dirichlet((1, 1, 1), size=16)
    spts = np.einsum('bk,fkd->fbd', b16, P[F]).reshape(-1, 3); sfid = np.repeat(np.arange(len(F)), len(b16))
    for ch in range(6):
        a, b = inplane[ch]; ax = ch // 2; sg = 1.0 if ch % 2 == 0 else -1.0
        u = (spts[:, a] - lo[a]) / span; v = (spts[:, b] - lo[b]) / span
        gi = np.clip((u * (T - 1)).astype(int), 0, T - 1); gj = np.clip((v * (T - 1)).astype(int), 0, T - 1)
        dep = spts[:, ax] * sg
        dmap = np.full((T, T), -np.inf); np.maximum.at(dmap, (gi, gj), dep)
        wins = dep >= dmap[gi, gj] - 0.002
        share = np.bincount(sfid, wins, minlength=len(F)) / len(b16)
        hidden[:, ch] = share < HAIR_CHART_VISIBLE
    own = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]]); own /= np.linalg.norm(own, axis=1, keepdims=True) + 1e-12
    own_score = np.abs(own @ dirs.T)                        # how little each chart foreshortens the face (its own normal)
    def signed(axis_):
        return axis_ * 2 + (fn[np.arange(len(F)), axis_] < 0).astype(int)
    # The chart: among those where the face is visible, foreshortened at most HAIR_CHART_FORESHORTEN of its best and
    # faced by its smoothed normal, the one the smoothed normal faces most (coherent regions, no flicker along the
    # ridges); with no such chart, the visible one its own normal faces most; with none visible, the one it faces
    # most. (By the smoothed normals alone the steep sides of every lock went to the top or front chart as slivers
    # of texels stretched over each face: flat facets and comb teeth in a field of strands.)
    ok = ~hidden & (own_score >= HAIR_CHART_FORESHORTEN * own_score.max(1, keepdims=True)) & (score > 0)
    cand = np.where(ok, score, -np.inf); vis_own = np.where(hidden, -np.inf, own_score)
    chart = np.where(np.isfinite(cand.max(1)), cand.argmax(1), np.where(np.isfinite(vis_own.max(1)), signed(vis_own.argmax(1) // 2), signed(own_score.argmax(1) // 2)))
    best = score.argmax(1)
    # The side charts' second layer: a side-facing face hidden in its side chart (the walls of the pocket between
    # the bob's side and the temple, behind the side itself) took the front or top chart, where it is a sliver of
    # texels stretched over the whole face: vertical stripes from three quarters. Such faces share one more
    # tile, a side projection of their own (both sides, half a tile each) in which they are the outermost.
    # (By the face's own normal: in a groove under an overhanging lock the 3 cm smoothed normal is the lock's,
    #  and the groove's faces, hidden in their side chart, were baked foreshortened in the top chart as facets.)
    own_axis = own_score.argmax(1)
    side_chart = (fn[:, 0] < 0).astype(int)
    ax_now = np.where(chart == 6, 2, np.where(chart == 7, 0, chart // 2))
    second = (own_axis == 0) & hidden[np.arange(len(F)), side_chart] & ((ax_now == 0) | (own_score[np.arange(len(F)), ax_now] < HAIR_CHART_SECOND_OWN))
    chart = np.where(second, 7, chart)
    # The front band (the hairline, the skirt over the forehead) goes to the front chart whenever it is
    # not hidden there: by the averaged normals the curved forehead's faces leaned into the side and
    # top charts, where they are not the outermost and took those charts' texels, so the alpha cut
    # at the hairline came out as a patchwork of charts.
    # (Only faces that face the front at least HAIR_FRONT_CHART_COS: at any facing, the bob's side in front of
    #  the ear, sideways-facing and within the band, went to the front chart, where it is a sliver of texels
    #  stretched across each face: comb teeth along its edge from every angle since P25.)
    near_front = fc_[:, 2] > np.percentile(fc_[:, 2], 99.5) - HAIR_MASK_DEPTH
    chart = np.where(near_front & ~hidden[:, 4] & (score[:, 4] > HAIR_FRONT_CHART_COS), 4, chart)
    # The skirt (the hair layer lying on the face near the hairline) has its own chart, a front
    # projection in which only skirt faces are baked: under the bangs it is the outermost surface in
    # none of the six box charts and took their texels, a patchwork at the hairline.
    if skirt is not None:
        chart = np.where(skirt, 6, chart)
    # Islands: a face whose chart differs from most of its neighbourhood (HAIR_CHART_ISLAND radius) takes the
    # neighbourhood's chart when that chart is one it may use (visible, not foreshortened, faced): single
    # faces baked in another projection were flat-coloured facets in a field of strands. The skirt keeps its chart.
    keep_ = skirt if skirt is not None else np.zeros(len(F), bool)
    for _ in range(2):
        nb = tree_.query_ball_point(fc_, r=HAIR_CHART_ISLAND)
        mode = np.array([np.bincount(chart[i], minlength=8).argmax() for i in nb])
        allowed = (mode < 6) & ok[np.arange(len(F)), np.clip(mode, 0, 5)]
        chart = np.where(~keep_ & allowed, mode, chart)
    # Slivers: a face whose own normal leans under HAIR_CHART_MIN_OWN to its chart's axis is a sliver of texels
    # there and takes the chart its own normal faces most among those where it is not hidden.
    ax_of = np.where(chart == 6, 2, np.where(chart == 7, 0, chart // 2))
    own_now = np.abs(own[np.arange(len(F)), ax_of])
    sliver = (own_now < HAIR_CHART_MIN_OWN) & np.isfinite(vis_own.max(1)) & (vis_own.max(1) > HAIR_CHART_MIN_OWN + 0.2) & (chart != 6)
    chart = np.where(sliver, signed(vis_own.argmax(1) // 2), chart)
    print('hair charts: slivers moved to the chart their own normal faces', int(sliver.sum()))
    axis = np.where(chart == 6, 2, np.where(chart == 7, 0, chart // 2))
    sign = np.where(chart == 6, 1.0, np.where(chart == 7, np.where(fn[:, 0] >= 0, 1.0, -1.0), np.where(chart % 2 == 0, 1.0, -1.0)))
    print('hair charts: faces hidden in their best chart', int(hidden[np.arange(len(F)), score.argmax(1)].sum()), 'of', len(F))
    def px_of(pts, ch, side=0.0):
        a, b = inplane[ch]
        u = (pts[:, a] - lo[a]) / span; v = (pts[:, b] - lo[b]) / span
        if ch in (0, 4, 6): u = 1 - u                       # seen from +x / +z the horizontal axis runs the other way
        if ch in (2,): v = 1 - v
        if ch == 7:
            # a quarter of the tile per (side of the head, way it faces): faces of one quarter share one depth test
            quad = int(side); facing, side_of = quad % 2, quad // 2
            u = ((1 - u) if facing else u) * 0.5 + 0.5 * facing; v = v * 0.5 + 0.5 * side_of
        return np.stack([tile[ch, 0] * T + 1 + u * (T - 2), tile[ch, 1] * T + 1 + (1 - v) * (T - 2)], 1)
    uv = np.zeros((len(F), 3, 2))
    for ch in range(7):
        m = chart == ch
        if m.any():
            uv[m] = px_of(P[F[m]].reshape(-1, 3), ch).reshape(-1, 3, 2)
    cx_ = (lo[0] + hi[0]) / 2
    def quad_of(m):
        return (fc_[m, 0] >= cx_).astype(int) * 2 + (fn[m, 0] >= 0).astype(int)
    def second_layer(m):
        chart[m] = 7; axis[m] = 0; sign[m] = np.where(fn[m, 0] >= 0, 1.0, -1.0)
        idx_m = np.where(m)[0]; q_ = quad_of(m)
        for q in range(4):
            mq = idx_m[q_ == q]
            if len(mq):
                uv[mq] = px_of(P[F[mq]].reshape(-1, 3), 7, q).reshape(-1, 3, 2)
    if (chart == 7).any():
        second_layer(chart == 7)
    img = np.zeros((H, W, 3)); depth = np.full((H, W), -np.inf); drawn = np.zeros((H, W), bool); aimg = np.ones((H, W))
    def bake(sel):
        area = 0.5 * np.abs(np.cross(uv[sel, 1] - uv[sel, 0], uv[sel, 2] - uv[sel, 0]))
        bucket = np.digitize(area, [60, 250, 1000])
        for k, n in enumerate((16, 32, 64, 128)):
            bary = lattice(n); faces = sel[bucket == k]
            if not len(faces):
                continue
            for part in np.array_split(faces, max(1, len(faces) * len(bary) // 1500000)):
                pts = np.einsum('bk,fkd->fbd', bary, P[F[part]]).reshape(-1, 3)
                puv = np.einsum('bk,fkd->fbd', bary, uv[part]).reshape(-1, 2)
                sh = np.einsum('bk,fk->fb', bary, shade[F[part]]).reshape(-1)
                ax = np.repeat(axis[part], len(bary)); sg = np.repeat(sign[part], len(bary)); fid = np.repeat(part, len(bary))
                dep = pts[np.arange(len(pts)), ax] * sg
                col = sample(pts) * sh[:, None]
                x = np.clip(puv[:, 0].astype(int), 0, W - 1); y = np.clip(puv[:, 1].astype(int), 0, H - 1)
                o = np.argsort(dep); better = dep[o] > depth[y[o], x[o]]
                img[y[o][better], x[o][better]] = col[o][better]; depth[y[o][better], x[o][better]] = dep[o][better]; drawn[y[o][better], x[o][better]] = True
                if alpha_fn is not None:
                    aimg[y[o][better], x[o][better]] = alpha_fn(pts)[o][better]
    bake(np.arange(len(F)))
    _, (iy, ix) = ndimage.distance_transform_edt(~drawn, return_indices=True)
    img = img[iy, ix]; aimg = aimg[iy, ix]
    if alpha_fn is not None:
        # Specks: an island of hair texels smaller than HAIR_ALPHA_SPECK inside a cut region is a flake
        # (the views' strand-level masks on a surface that does not follow them to the strand: from three
        # quarters the bob's side in front of the ear read as flakes over the cheek), and a pinhole of
        # cut texels that small inside the hair is a hole in it; both take their surroundings.
        cut = aimg < 0.5
        for region, value, limit in ((~cut, 0.0, HAIR_ALPHA_SPECK[0]), (cut, 1.0, HAIR_ALPHA_SPECK[1])):
            lab_, _ = ndimage.label(region)
            sizes = np.bincount(lab_.ravel()); small = sizes < limit; small[0] = False
            aimg = np.where(small[lab_], value, aimg)
        print('hair charts: alpha specks filled', int((cut != (aimg < 0.5)).sum()), 'texels')
        # Faces whose texels another face in front of them cut (texels are shared wherever a chart's projection
        # stacks faces): a cut face, or two or three, with uncut faces all round is a triangle of skin through the
        # hair (the nape from behind). Their texels are drawn again, uncut, outermost or not.
        from scipy.sparse import coo_matrix
        from scipy.sparse.csgraph import connected_components
        b4 = np.array([[1 / 3, 1 / 3, 1 / 3], [0.6, 0.2, 0.2], [0.2, 0.6, 0.2], [0.2, 0.2, 0.6]])
        suv = np.einsum('bk,fkd->fbd', b4, uv); sx = np.clip(suv[..., 0].astype(int), 0, W - 1); sy = np.clip(suv[..., 1].astype(int), 0, H - 1)
        fcut = (aimg[sy, sx] < 0.5).mean(1) >= 0.5
        _, vid = np.unique(np.round(P, 5), axis=0, return_inverse=True); Fu = vid.reshape(-1)[F]
        es = np.sort(np.concatenate([Fu[:, [0, 1]], Fu[:, [1, 2]], Fu[:, [2, 0]]]), 1); fe = np.tile(np.arange(len(F)), 3)
        ek = es[:, 0] * (Fu.max() + 1) + es[:, 1]; o_ = np.argsort(ek, kind='stable'); ek, fe = ek[o_], fe[o_]
        pair = ek[1:] == ek[:-1]; fa_, fb_ = fe[:-1][pair], fe[1:][pair]
        both = fcut[fa_] & fcut[fb_]
        ncc, lab_f = connected_components(coo_matrix((np.ones(both.sum()), (fa_[both], fb_[both])), shape=(len(F), len(F))), directed=False)
        csize = np.bincount(lab_f[fcut], minlength=ncc)
        redo = np.where(fcut & (csize[lab_f] <= HAIR_CUT_SPECK_FACES))[0]
        for part in np.array_split(redo, max(1, len(redo) // 2000)):
            if not len(part):
                continue
            bary = lattice(32)
            pts = np.einsum('bk,fkd->fbd', bary, P[F[part]]).reshape(-1, 3)
            puv = np.einsum('bk,fkd->fbd', bary, uv[part]).reshape(-1, 2)
            sh = np.einsum('bk,fk->fb', bary, shade[F[part]]).reshape(-1)
            x = np.clip(puv[:, 0].astype(int), 0, W - 1); y = np.clip(puv[:, 1].astype(int), 0, H - 1)
            img[y, x] = sample(pts) * sh[:, None]; aimg[y, x] = 1.0
        print('hair charts: cut faces drawn again', len(redo), 'of', int(fcut.sum()), 'cut faces')
        # The alpha's edge smoothed: cut texel by texel from the views' masks, the layer's edges were staircases
        # and torn strand tips along the hairline, the ears and the bob's lower edge (owner: "jämna till hack
        # och ojämnheter"). A median over HAIR_ALPHA_MEDIAN texels takes the single-texel notches, a blur of
        # HAIR_ALPHA_BLUR rounds the edge the MASK cutoff then draws.
        k_ = max(1, int(round(HAIR_ALPHA_OPEN * T / HAIR_CHART_TILE)))
        hard = aimg >= 0.5
        aimg = ndimage.binary_closing(ndimage.binary_opening(hard, iterations=k_), iterations=k_).astype(float)
        aimg = ndimage.median_filter(aimg, size=HAIR_ALPHA_MEDIAN)
        aimg = ndimage.gaussian_filter(aimg, HAIR_ALPHA_BLUR)
    print('hair charts: faces per chart', np.bincount(chart, minlength=8), 'drawn texels', int(drawn.sum()), 'cut texels', int((aimg < 0.5).sum()))
    if alpha_fn is None:
        return uv / np.array([W, H]), np.clip(img, 0, 255).astype(np.uint8)
    return uv / np.array([W, H]), np.dstack([np.clip(img, 0, 255), aimg * 255]).astype(np.uint8)

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
            # The lower lid rises: the eye becomes a smiling arch. The skin under the lid stretches up
            # with it, fading out 60 px down (moved as one band to 24 px below the lid, with the rows
            # under that still, it tore: a stretched rectangle under the eye).
            lift = (yl - yu) * 0.55
            bot = yl + 60
            eye = (y >= yu) & (y <= yl)
            below = (y > yl) & (y <= bot)
            ny = np.where(eye, yu + (y - yu) * (yl - lift - yu) / np.maximum(yl - yu, 1e-3), y)
            ny = np.where(below, (yl - lift) + (y - yl) * (bot - yl + lift) / (bot - yl), ny)
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
    """The expressions' mouth corners (smile, smirk, frown); the shapes that part the lips are
    slit_field's."""
    m = MOUTH
    x, y = xy[:, 0], xy[:, 1]
    d = np.zeros_like(xy)
    half = (m['right'] - m['left']) / 2
    def corners(amount_x, amount_y, which=(-1, 1)):
        for s in which:
            cx = m['cx'] + s * half
            w = np.exp(-(((x - cx) / 36) ** 2 + ((y - m['y']) / 30) ** 2))
            d[:, 0] += s * amount_x * w
            d[:, 1] += amount_y * w
    if kind == 'smile':
        corners(9, -11)
        cheeks = np.exp(-(((np.abs(x - m['cx']) - 120) / 50) ** 2 + ((y - 680) / 40) ** 2))
        d[:, 1] += -5 * cheeks
    elif kind == 'smirk':
        corners(7, -10, which=(1,))
    elif kind == 'frown':
        corners(-3, 8)
    return d

# ---------------------------------------------------------------- the mouth, from the owner's pictures

# The closed photo and the owner's open ("aa", teeth) and o-mouth pictures, all pixel-aligned.
MOUTH_PICTURES = {'closed': 'bald/kairo-bald-front.png', 'open': 'assets-v2/kairo-mouth-open-front.png', 'round': 'assets-v2/kairo-mouth-o-front.png'}
# The mouth's corners (source px), measured by hand: the closed lips' line, the open mouth's opening and
# the o-mouth's lip line end there. The outlines between are measured from the pictures.
MOUTH_CORNERS = {'closed': ((550.0, 744.0), (702.0, 744.0)), 'open': ((554.0, 752.0), (700.0, 749.0)), 'round': ((568.0, 752.0), (682.0, 752.0))}
OPENING_DARK = {'closed': 0.0, 'open': 100.0, 'round': 60.0}   # the o-mouth's closed lip line is dark too (lum 65-90)
MU = np.linspace(-1.0, 1.0, 121)            # across the mouth, -1 at the left corner, +1 at the right
MOUTH_SHAPES = {}                           # per shape: corners x0, x1 and over MU the upper lip's top U, the opening's top A and bottom B, the lower lip's bottom L; the chin's edge
SLIT_TARGETS = ['open', 'mouth_wide', 'round', 'small_o', 'press', 'teeth']
MOUTH_GRID = (430, 640)                     # the harmonic fields' origin (source px)
MOUTH_FIELDS = {}                           # per slit target: (dx, dy) over the grid
SIDE = []                                   # per vertex: +1 upper lip's edge, -1 lower lip's edge, 0 elsewhere (set by face_surface)
SLIT_RIM = []                               # the cut's edges: positions and crop px of both ends, and their side
SKIRT_EDGES = []                            # how many border edges the face grid's skirt folds back from (its faces are the last 2 x that many of the grid's), set by face_surface

def lip_outline(work, name):
    """The lips in one picture, per position across the mouth: the upper lip's top U, the opening's top A
    and bottom B (the meeting line where the lips touch), the lower lip's bottom L; the teeth's edges
    in the opening; the chin's lower edge. Lips are the saturated red, the opening is dark or teeth."""
    a = ndimage.gaussian_filter(np.asarray(Image.open(f'{work}/{MOUTH_PICTURES[name]}').convert('RGB')).astype(float), (1, 1, 0))
    R, G, B = a[..., 0], a[..., 1], a[..., 2]; lum = a.mean(-1)
    # Redder and less yellow than skin; on the shadowed left side the lips are only R-G 60-75 against
    # the skin's 46-52, so the yellow (G-B: lips under 14, skin over 16) carries the difference.
    lip = ndimage.binary_opening((R - G - 2.5 * (G - B) > 28) & (R > 125), iterations=1)
    teeth = (R - G < 50) & (G - B < 15) & (R > 120) & (name == 'open')
    hole = ~lip & ((lum < OPENING_DARK[name]) | teeth)
    (xl, yl), (xr, yr) = MOUTH_CORNERS[name]
    x = xl + (MU + 1) / 2 * (xr - xl); corner = yl + (MU + 1) / 2 * (yr - yl)
    xi = np.round(x).astype(int)
    # The lips' meeting line: the darkest row near the straight line between the corners (a free search
    # found the shadow under the lower lip at the corners).
    line = np.array([y0 - 8 + np.argmin(lum[int(y0) - 8:int(y0) + 9, i]) for i, y0 in zip(xi, np.round(corner))], float)
    U, L = np.full(len(MU), np.nan), np.full(len(MU), np.nan)
    A, Bm, Tu, Tl = line.copy(), line.copy(), line.copy(), line.copy()
    for k, i in enumerate(xi):
        ys = np.where(lip[690:850, i])[0] + 690
        if len(ys) < 3:
            continue
        U[k], L[k] = ys.min(), ys.max()
        hy = np.where(hole[ys.min():ys.max() + 1, i])[0] + ys.min()
        if len(hy) >= 2:
            A[k], Bm[k] = hy.min(), hy.max() + 1
            ty = np.where(teeth[int(A[k]):int(Bm[k]), i])[0] + A[k]; mid = (A[k] + Bm[k]) / 2
            Tu[k] = ty[ty < mid].max() + 1 if (ty < mid).any() else A[k]
            Tl[k] = ty[ty >= mid].min() if (ty >= mid).any() else Bm[k]
    def clean(v):
        ok = ~np.isnan(v); ok[[0, -1]] = False
        v = np.interp(MU, np.concatenate([[-1.0], MU[ok], [1.0]]), np.concatenate([[corner[0]], v[ok], [corner[-1]]]))
        v = ndimage.gaussian_filter1d(ndimage.median_filter(v, 5, mode='nearest'), 1.5, mode='nearest')
        v[[0, -1]] = corner[[0, -1]]
        return v
    U, A, Bm, L = clean(U), clean(A), clean(Bm), clean(L)
    A = np.maximum(A, U); Bm = np.maximum(Bm, A); L = np.maximum(L, Bm)
    g = np.diff(ndimage.gaussian_filter(lum, 1.5), axis=0)
    chin_x = np.arange(552.0, 704.0, 4.0)
    chin = ndimage.median_filter(np.array([828 + np.argmin(g[828:905, int(c)]) for c in chin_x], float), 5, mode='nearest')
    return dict(x0=xl, x1=xr, U=U, A=A, B=Bm, L=L, Tu=clean(Tu), Tl=clean(Tl), chin_x=chin_x, chin=chin)

def mouth_band(xy, name, side):
    """Points on the closed lips moved to where shape `name` has them (source px): per position across
    the mouth the upper lip [U, M] onto [U', A'] and the lower lip [M, L] onto [B', L'], the corners onto
    the shape's corners. Returns the moved points and which points lie on the lips. side: +1 the upper
    lip's edge, -1 the lower's, 0 by height (the cut's two edges lie on the same line at rest)."""
    c, t = MOUTH_SHAPES['closed'], MOUTH_SHAPES[name]
    x, y = xy[:, 0], xy[:, 1]
    u = 2 * (x - c['x0']) / (c['x1'] - c['x0']) - 1
    at = lambda arr: np.interp(u, MU, arr)
    U0, M0, L0 = at(c['U']), at(c['A']), at(c['L'])
    upper = np.where(side != 0, side > 0, y < M0)
    inside = (np.abs(u) <= 1) & np.where(upper, y >= U0 - 0.5, y <= L0 + 0.5)
    tx = t['x0'] + (u + 1) / 2 * (t['x1'] - t['x0'])
    f_up = np.clip((y - U0) / np.maximum(M0 - U0, 1e-3), 0, 1)
    f_lo = np.clip((y - M0) / np.maximum(L0 - M0, 1e-3), 0, 1)
    ty = np.where(upper, at(t['U']) + f_up * (at(t['A']) - at(t['U'])), at(t['B']) + f_lo * (at(t['L']) - at(t['B'])))
    moved = np.stack([tx, ty], 1)
    # The cut runs a little past the corners, both edges moving with the corner: ending inside, the
    # face's last triangle across it stretched into a skin-coloured strip over the open mouth's corner.
    past = (side != 0) & (np.abs(u) > 1)
    right = u > 0
    shift = np.stack([np.where(right, t['x1'] - c['x1'], t['x0'] - c['x0']), np.where(right, t['A'][-1] - c['A'][-1], t['A'][0] - c['A'][0])], 1)
    return np.where(past[:, None], xy + shift, moved), inside | past

def mouth_setup(work):
    """The lip outlines of every mouth shape and, round the lips, the smoothest displacement that carries
    them there (harmonic: zero at the cheeks and under the nose, the chin's edge where the picture has
    it). Shapes not pictured are drawn from the closed outline."""
    c, o, r = (lip_outline(work, n) for n in ('closed', 'open', 'round'))
    c['A'] = c['B'] = (c['A'] + c['B']) / 2
    taper = np.clip(1 - MU ** 2, 0, None) ** 0.6
    M = c['A']
    def like(base, **k):
        s = dict(base); s.update(k); return s
    MOUTH_SHAPES.clear()
    MOUTH_SHAPES.update(closed=c, open=o, round=r)
    MOUTH_SHAPES['teeth'] = like(c, U=c['U'] - taper, A=M - 6 * taper, B=M + 8 * taper, L=c['L'] + 3 * taper)
    MOUTH_SHAPES['mouth_wide'] = like(c, x0=c['x0'] - 13, x1=c['x1'] + 13, U=c['U'] + 2 * taper - 2 * MU ** 2, A=M - 1.5 * taper - 2 * MU ** 2,
                                B=M + 4 * taper - 2 * MU ** 2, L=c['L'] - 3 * taper - 2 * MU ** 2)
    MOUTH_SHAPES['press'] = like(c, U=c['U'] + 4 * taper, L=c['L'] - 6 * taper)
    mid = (r['A'] + r['B']) / 2
    MOUTH_SHAPES['small_o'] = like(r, x0=r['x0'] + 6, x1=r['x1'] - 6, A=mid - 0.45 * (mid - r['A']), B=mid + 0.45 * (r['B'] - mid),
                                   chin=c['chin'] + 0.5 * (r['chin'] - c['chin']))
    for n, s in MOUTH_SHAPES.items():
        k = len(MU) // 2
        print(f'mouth {n}: corners {s["x0"]:.0f}-{s["x1"]:.0f}, centre U {s["U"][k]:.0f} A {s["A"][k]:.0f} B {s["B"][k]:.0f} L {s["L"][k]:.0f}, chin {s["chin"][len(s["chin"]) // 2]:.0f}')
    from scipy.sparse import coo_matrix
    from scipy.sparse.linalg import splu
    gx0, gy0 = MOUTH_GRID
    yy, xx = np.mgrid[gy0:910, gx0:822].astype(float)
    pts = np.stack([xx.ravel(), yy.ravel()], 1)
    dom = ((xx - 626) / 190) ** 2 + ((yy - 778) / 128) ** 2 < 1
    nose = (yy < 684) & (np.abs(xx - 626) < 60)
    lips = mouth_band(pts, 'closed', np.zeros(len(pts)))[1].reshape(xx.shape)
    chin_y = np.interp(xx, c['chin_x'], c['chin'])
    # The chin's lower part moves as one, down to below its edge: everything the front camera sees
    # there, the chin's front and its underside behind it, takes the same drop. A thin band moved the
    # chin's lowest rows further than the underside just above them in the image, a kink under the chin.
    chin = (np.abs(xx - 626) < 66) & (yy > chin_y - 22) & (yy <= chin_y + 30)
    # The jaw's outline away from the chin stays: moving, it pulled the jaw's silhouette off the neck
    # (which has no morphs) and the inside of the head showed at the jaw's angles.
    jaw_side = (yy > CHIN_Y - 0.0042 * (xx - 626.0) ** 2 + 3) & (np.abs(xx - 626) > 120)
    free = dom & ~nose & ~lips & ~chin & ~jaw_side
    idx = np.full(xx.shape, -1); idx[free] = np.arange(free.sum())
    rows, cols = [], []
    fy, fx = np.where(free)
    for dy_, dx_ in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        nb = idx[fy + dy_, fx + dx_]; ok = nb >= 0
        rows.append(idx[fy, fx][ok]); cols.append(nb[ok])
    rows, cols = np.concatenate(rows), np.concatenate(cols)
    n = free.sum()
    Lap = (coo_matrix((np.full(n, 4.0), (np.arange(n), np.arange(n))), shape=(n, n)) - coo_matrix((np.ones(len(rows)), (rows, cols)), shape=(n, n))).tocsc()
    lu = splu(Lap)
    for name in SLIT_TARGETS:
        t = MOUTH_SHAPES[name]
        moved, _ = mouth_band(pts, name, np.zeros(len(pts)))
        d = np.where(lips[..., None], (moved - pts).reshape(*xx.shape, 2), 0.0)
        d[..., 1] = np.where(chin, np.interp(xx, t['chin_x'], t['chin']) - chin_y, d[..., 1])
        for k in (0, 1):
            fixed = np.where(free, 0.0, d[..., k])
            rhs = np.zeros(n)
            for dy_, dx_ in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                rhs += fixed[fy + dy_, fx + dx_]
            d[..., k][free] = lu.solve(rhs)
        MOUTH_FIELDS[name] = d
    print('mouth fields', n, 'free px')

LIP_ROUND = 0.008                   # how far (model units) the lips' inner edges go back once the mouth is open

def slit_depth(xy, name, side):
    """How far points on the lips go back (model units) as the lips part: the inner edges round into
    the mouth, more the wider the opening at that column. Kept at their own depth, the parted edges
    were the lips' front-most points and stood out in profile as thin spikes."""
    c, t = MOUTH_SHAPES['closed'], MOUTH_SHAPES[name]
    x, y = xy[:, 0], xy[:, 1]
    u = 2 * (x - c['x0']) / (c['x1'] - c['x0']) - 1
    at = lambda arr: np.interp(u, MU, arr)
    U0, M0, L0 = at(c['U']), at(c['A']), at(c['L'])
    upper = np.where(side != 0, side > 0, y < M0)
    inside = (np.abs(u) <= 1) & np.where(upper, y >= U0 - 0.5, y <= L0 + 0.5)
    edge = np.where(upper, np.clip((y - U0) / np.maximum(M0 - U0, 1e-3), 0, 1), 1 - np.clip((y - M0) / np.maximum(L0 - M0, 1e-3), 0, 1))
    opening = np.clip((at(t['B']) - at(t['A'])) / 15.0, 0, 1)
    # Less where the lip is thin and none at the corners: there the lip edge going back pulled the
    # faces across the corner into a skin-coloured strip over the open mouth.
    thick = np.clip(np.where(upper, M0 - U0, L0 - M0) / 25.0, 0, 1)
    taper = 1 - smooth(0.7, 0.98, np.abs(u))
    return LIP_ROUND * opening * thick * taper * smooth(0.2, 1.0, edge) ** 1.2 * inside

def slit_field(xy, name, side):
    """Displacement (source px) for a mouth shape that parts the lips: exact on the lips (by side), the
    harmonic field round them."""
    moved, inside = mouth_band(xy, name, side)
    d = MOUTH_FIELDS[name]
    gy, gx = xy[:, 1] - MOUTH_GRID[1], xy[:, 0] - MOUTH_GRID[0]
    h = np.stack([ndimage.map_coordinates(d[..., k], [gy, gx], order=1, mode='constant', cval=0.0) for k in (0, 1)], 1)
    return np.where(inside[:, None], moved - xy, h)

MOUTH_MATERIAL, LIPWALL_MATERIAL, OSHADE_MATERIAL = 13, 14, 15
O_TEX = (580, 734, 672, 770)         # source px of the o-mouth picture kept for its dark opening
LIP_CURL = 3.0                       # source px the lips' inner sides turn away from the opening, back to the mouth's inside
MOUTH_TEX = (500, 690, 756, 850)     # source px of the open picture kept for the inside of the mouth

def mouth_textures(work):
    """The inside of the mouth from the owner's open picture: teeth, tongue and the dark behind them,
    spread on under the lips (the upper teeth up into gum, the lower down, dark past the corners) so a
    turned head sees into the mouth past the opening's edge, not lip. And the lips' inner side: the
    picture's wet inner lip, darkening into the mouth."""
    o = MOUTH_SHAPES['open']
    a = np.asarray(Image.open(f'{work}/{MOUTH_PICTURES["open"]}').convert('RGB')).astype(float)
    x0, y0, x1, y1 = MOUTH_TEX
    yy, xx = np.mgrid[y0:y1:0.5, x0:x1:0.5]
    rgb = np.stack([ndimage.map_coordinates(a[..., k], [yy, xx], order=1) for k in range(3)], -1)
    u = 2 * (xx - o['x0']) / (o['x1'] - o['x0']) - 1
    top, bot = np.interp(u, MU, o['A']), np.interp(u, MU, o['B'])
    inside = (np.abs(u) < 0.98) & (yy > top + 2) & (yy < bot - 2)
    dist, (iy, ix) = ndimage.distance_transform_edt(~inside, return_indices=True)
    fill = rgb[iy, ix]
    gum, dark = np.array([150.0, 72.0, 70.0]), np.array([45.0, 18.0, 18.0])
    towards = gum * (1 - smooth(0.85, 1.05, np.abs(u)))[..., None] + dark * smooth(0.85, 1.05, np.abs(u))[..., None]
    g = smooth(2.0, 20.0, dist)[..., None]
    out = np.where(inside[..., None], rgb, fill * (1 - g) + towards * g)
    lip = (a[..., 0] - a[..., 1] > 76) & (a[..., 1] - a[..., 2] < 16)
    hole = np.zeros(a.shape[:2], bool)
    hy, hx = np.mgrid[700:800, 540:715]
    hu = 2 * (hx - o['x0']) / (o['x1'] - o['x0']) - 1
    hole[700:800, 540:715] = (np.abs(hu) < 1) & (hy > np.interp(hu, MU, o['A'])) & (hy < np.interp(hu, MU, o['B']))
    wet = lip & ndimage.binary_dilation(hole, iterations=5) & ~hole
    inner = np.median(a[wet], 0)
    ramp = np.linspace(0, 1, 64)[:, None]
    wall = inner[None] * (1 - ramp) + dark[None] * ramp
    print('inner lip colour', np.round(inner).astype(int))
    # The o-mouth's opening: the picture's dark, spread past its edge.
    r = MOUTH_SHAPES['round']
    b = np.asarray(Image.open(f'{work}/{MOUTH_PICTURES["round"]}').convert('RGB')).astype(float)
    x0, y0, x1, y1 = O_TEX
    yy, xx = np.mgrid[y0:y1:0.5, x0:x1:0.5]
    rgb = np.stack([ndimage.map_coordinates(b[..., k], [yy, xx], order=1) for k in range(3)], -1)
    ur = 2 * (xx - r['x0']) / (r['x1'] - r['x0']) - 1
    opening = (yy > np.interp(ur, MU, r['A']) + 1.5) & (yy < np.interp(ur, MU, r['B']) - 1.5)
    _, (iy, ix) = ndimage.distance_transform_edt(~opening, return_indices=True)
    o_tex = np.where(opening[..., None], rgb, ndimage.gaussian_filter(rgb[iy, ix], (3, 3, 0)))
    return (Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)), Image.fromarray(np.tile(wall[:, None, :], (1, 4, 1)).astype(np.uint8)),
            Image.fromarray(np.clip(o_tex, 0, 255).astype(np.uint8)))

def mouth_inside(zg):
    """The inside of the mouth: two curved sheets behind the lips, textured with the open picture. The
    upper one (upper teeth and the dark under them) stays with the skull; the lower one (lower teeth and
    tongue) goes down with the lower lip's edge. At full 'open' both are where the picture has them.
    Plus the lips' inner sides: a wall from each cut edge back into the mouth, so the parted lips have
    thickness when the head turns. Depth: behind the deepest face point the sheets can lie under, at
    rest or open, and further back toward the corners (a dental arch)."""
    o, c = MOUTH_SHAPES['open'], MOUTH_SHAPES['closed']
    x0, y0, x1, y1 = MOUTH_TEX
    xs = np.arange(x0 + 18, x1 - 18 + 1, 2.0)
    X, Y = np.meshgrid(xs, np.arange(700.0, 832.0, 2.0))
    cp = src_to_crop(np.stack([X.ravel(), Y.ravel()], 1))
    zref = ndimage.gaussian_filter1d(ndimage.map_coordinates(zg, [cp[:, 1], cp[:, 0]], order=1).reshape(X.shape).max(0), 3, mode='nearest')
    u = 2 * (xs - o['x0']) / (o['x1'] - o['x0']) - 1
    z_up = zref + 0.002 + 0.005 * np.minimum(u * u, 1.44)
    z_lo = z_up + 0.0015
    print('mouth inside depth behind the face (mm) centre/corner', round(float((z_up - zref)[len(xs) // 2]) * 1000, 1), round(float((z_up - zref)[0]) * 1000, 1))
    Tu, Tl = np.interp(u, MU, o['Tu']), np.interp(u, MU, o['Tl'])
    def along(name, arr):
        t = MOUTH_SHAPES[name]
        return np.interp(xs, t['x0'] + (MU + 1) / 2 * (t['x1'] - t['x0']), arr)
    jaw = {n: along(n, MOUTH_SHAPES[n]['B'] - c['A']) for n in SLIT_TARGETS}
    lift_up = {}
    for n in ('round', 'small_o'):
        t = MOUTH_SHAPES[n]
        bt, at_ = along(n, t['B']), along(n, t['A'])
        opening = bt - at_ > 1.5
        if opening.any():
            # The o-mouth shows only dark: the lower teeth stay under the lower lip, the upper above the
            # upper lip's edge (the picture has no teeth in it).
            jaw[n] = np.maximum(jaw[n], (bt + 2 - Tl + jaw['open'])[opening].max())
            lift_up[n] = min(0.0, (at_ - 2 - Tu)[opening].min())
    print('mouth inside: lower teeth drop (px) open/round/small_o', round(float(jaw['open'].max()), 1), round(float(jaw['round'].max()), 1),
          round(float(jaw['small_o'].max()), 1), 'upper teeth lift', {k: round(v, 1) for k, v in lift_up.items()})
    rows = 26; f = np.linspace(0, 1, rows)[:, None]
    def place(x, y, z):
        return unproject(src_to_crop(np.stack([x.ravel(), y.ravel()], 1)), np.broadcast_to(z, x.shape).ravel())
    def sheet(ys, z, moves, rest_dy):
        xg = np.broadcast_to(xs, ys.shape)
        p0 = place(xg, ys + rest_dy, z)
        uv = np.stack([(xg.ravel() - x0) / (x1 - x0), (ys.ravel() - y0) / (y1 - y0)], 1)
        targets = [place(xg, ys + rest_dy + moves[n], z) - p0 if n in moves else np.zeros_like(p0) for n in TARGETS]
        idx = []
        for r in range(rows - 1):
            for k in range(len(xs) - 1):
                a0 = r * len(xs) + k
                idx += [a0, a0 + len(xs), a0 + 1, a0 + 1, a0 + len(xs), a0 + len(xs) + 1]
        return dict(pos=p0, nrm=np.tile([0.0, 0.0, 1.0], (len(p0), 1)), uv=uv, idx=np.array(idx, np.uint32), material=MOUTH_MATERIAL, targets=targets)
    upper = sheet(700 + (Tu + 3 - 700)[None] * f, z_up, {n: np.full(len(xs), v) for n, v in lift_up.items()}, 0.0)
    # Down to well under the lower lip's edge, but never below the jaw's outline in any shape: there the
    # sheet stood out in front of the neck.
    B1 = np.interp(u, MU, o['B'])
    reach = np.max([jaw[n] for n in SLIT_TARGETS], 0) - jaw['open']
    bottom = np.minimum(B1 + 30, CHIN_Y - 0.0042 * (xs - 626.0) ** 2 - 14 - np.maximum(reach, 0))
    lower = sheet((Tu - 3)[None] + (bottom - (Tu - 3))[None] * f, z_lo, {n: jaw[n] for n in SLIT_TARGETS}, -jaw['open'])
    # The o-mouth's dark, a sheet of its own: behind both sheets until the lips are fully rounded, then
    # in front of them (the picture shows no teeth or tongue in the o). It crosses the sheets late, so
    # half-rounded lips still show the teeth.
    ox = np.arange(O_TEX[0], O_TEX[2] + 1, 2.0); oy = np.arange(O_TEX[1], O_TEX[3] + 1, 2.0)
    OX_, OY_ = np.meshgrid(ox, oy)
    z_o = np.interp(OX_, xs, z_up)
    po = place(OX_, OY_, z_o + 0.0045)
    forward = place(OX_, OY_, z_o - 0.0007) - po
    o_idx = []
    for r_ in range(len(oy) - 1):
        for k in range(len(ox) - 1):
            a0 = r_ * len(ox) + k
            o_idx += [a0, a0 + len(ox), a0 + 1, a0 + 1, a0 + len(ox), a0 + len(ox) + 1]
    oshade = dict(pos=po, nrm=np.tile([0.0, 0.0, 1.0], (len(po), 1)), idx=np.array(o_idx, np.uint32), material=OSHADE_MATERIAL,
                  uv=np.stack([(OX_.ravel() - O_TEX[0]) / (O_TEX[2] - O_TEX[0]), (OY_.ravel() - O_TEX[1]) / (O_TEX[3] - O_TEX[1])], 1),
                  targets=[forward if n in ('round', 'small_o') else np.zeros_like(po) for n in TARGETS])
    # The lips' inner sides, from the cut's edges back to the sheet behind them.
    pa, pb, ca, cb, sd = SLIT_RIM
    n_e = len(pa)
    za, zb = project(pa)[1], project(pb)[1]
    sa, sb = (ca - np.array([OX, OY])) / S, (cb - np.array([OX, OY])) / S
    zba = np.where(sd > 0, np.interp(sa[:, 0], xs, z_up), np.interp(sa[:, 0], xs, z_lo)) + 0.0005
    zbb = np.where(sd > 0, np.interp(sb[:, 0], xs, z_up), np.interp(sb[:, 0], xs, z_lo)) + 0.0005
    # Back and away from the opening, as lips curl in (the lower lip's inside down, the upper's up):
    # straight back, the lower lip's inside faced the lab's camera, a dotted line along the lip's edge.
    # Not at the corners: there the upper and lower sides, turning apart, opened a gap the cheek's
    # inside showed through, a light line across the corner when the head turned.
    def curl(s_):
        u_ = 2 * (s_[:, 0] - MOUTH_SHAPES['closed']['x0']) / (MOUTH_SHAPES['closed']['x1'] - MOUTH_SHAPES['closed']['x0']) - 1
        return np.stack([np.zeros_like(sd), -sd * LIP_CURL * (1 - smooth(0.6, 0.95, np.abs(u_)))], 1)
    ba, bb = sa + curl(sa), sb + curl(sb)
    wall_pos = np.concatenate([pa, pb, unproject(src_to_crop(ba), zba), unproject(src_to_crop(bb), zbb)])
    wall_src = np.concatenate([sa, sb, ba, bb]); wall_z = np.concatenate([za, zb, zba, zbb]); wall_side = np.tile(sd, 4)
    targets = []
    for n in TARGETS:
        if n in MOUTH_TARGETS:
            d = field(wall_src, n, wall_side)
            front = np.arange(len(wall_src)) < 2 * n_e
            dz = slit_depth(wall_src, n, wall_side) * front if n in SLIT_TARGETS else 0.0
            targets.append(unproject(src_to_crop(wall_src + d), wall_z + dz) - wall_pos)
        else:
            targets.append(np.zeros_like(wall_pos))
    i = np.arange(n_e)
    wall_idx = np.stack([i, i + n_e, i + 2 * n_e, i + n_e, i + 3 * n_e, i + 2 * n_e], 1).reshape(-1)
    wall_uv = np.concatenate([np.tile([0.5, 0.02], (2 * n_e, 1)), np.tile([0.5, 0.98], (2 * n_e, 1))])
    walls = dict(pos=wall_pos, nrm=np.tile([0.0, 0.0, 1.0], (len(wall_pos), 1)), uv=wall_uv, idx=wall_idx.astype(np.uint32),
                 material=LIPWALL_MATERIAL, targets=targets)
    return [upper, lower, oshade, walls]

EYE_TARGETS =['blink', 'wide', 'squint', 'happy', 'look_left', 'look_right', 'look_up', 'look_down']
BROW_TARGETS = ['raise', 'inner_up', 'frown', 'one_up']
MOUTH_TARGETS = ['open', 'mouth_wide', 'round', 'small_o', 'smile', 'smirk', 'frown', 'press', 'teeth']
TARGETS = EYE_TARGETS + ['brow_' + b for b in BROW_TARGETS] + MOUTH_TARGETS

def field(xy, name, side=None):
    if name in EYE_TARGETS: return eye_field(xy, name)
    if name.startswith('brow_'): return brow_field(xy, name[5:])
    if name in SLIT_TARGETS: return slit_field(xy, name, np.zeros(len(xy)) if side is None else side)
    return mouth_field(xy, name)

LID_CROP = (400, 420, 860, 630)        # source px of the eyes-closed picture kept for the lids
LID_SCALE = 2                          # texture px per source px
HANG = 16.0                            # how far the closed lashes hang below the lid's edge (source px)

def lid_span(lid):
    """The lid surface's x range: well past the outer corner, so the photo's lash wing goes with the
    lid, and just past the inner one (wider there, it carried skin specks)."""
    outer_left = lid['outer_left'] if 'outer_left' in lid else LIDS.index(lid) == 0
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
        nz = np.where(np.abs(delta).max(1) > 5e-5)[0].astype(np.uint32)       # 0.05 crop px: the harmonic fields' tails, invisible
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

    @staticmethod
    def weld(p):
        """One vertex where faces share every attribute (position, normal, uv, skin, every morph): the
        atlas-textured parts had three vertices per face, and the haired model went over the
        container's 32 MiB."""
        cols = [p['pos'], p['nrm'], p['uv']] + [p[k] for k in ('joints', 'weights') if p.get(k) is not None] + list(p.get('targets') or [])
        key = np.ascontiguousarray(np.concatenate([np.asarray(c, np.float32).reshape(len(p['pos']), -1) for c in cols], 1))
        _, first, inv = np.unique(key.view(np.dtype((np.void, key.dtype.itemsize * key.shape[1]))).ravel(), return_index=True, return_inverse=True)
        if len(first) == len(key):
            return p
        q = dict(p)
        for k in ('pos', 'nrm', 'uv', 'joints', 'weights'):
            if p.get(k) is not None:
                q[k] = np.asarray(p[k])[first]
        if p.get('targets') is not None:
            q['targets'] = [np.asarray(t)[first] for t in p['targets']]
        q['idx'] = inv.reshape(-1)[np.asarray(p['idx']).astype(np.int64)].astype(np.uint32)
        return q

    def mesh(self, name, prims, target_names=()):
        out = []
        for p in prims:
            p = self.weld(p)
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
EAR_MATERIAL = 16              # index of the "ear" material (the auricles' per-triangle atlas)
ROOT_MATERIAL = 17             # index of the "roots" material (the root cap under the hair)
ROOT_CAP = os.environ.get('KAIRO_ROOTS', '0') == '1'   # the root cap under the hair layer (KAIRO_ROOTS=1 builds with it). Off since the skull carries the owner's roots itself (C31): drawn 1-2 mm over the same roots it only softened the strands; the owner turned it off. Kept for the hair-on-grey review variant.
HAIR_CARDS = os.environ.get('KAIRO_CARDS', '0') == '1' # hair cards for the lock ends (Tools/haircards.py; KAIRO_CARDS=1 builds them). Off: the owner judged them not worth it once the scalp carried their own roots (eight pieces at the nape's corners).
HAIRLINE_SKIRT = os.environ.get('KAIRO_SKIRT', '0') == '1'   # the face grid's skirt as its own layer (KAIRO_SKIRT=1 builds it). Off on the owner's call: with the owner's scalp pictures on the skull it added nothing visible.
ROOT_COVER = 0.5               # hair weight (compose's coverage field) above which a skull face is capped
ROOT_LIFT = (0.0005, 0.002)    # how far the cap stands off the skin at its edge and in its middle (no z-fighting, no step at the edge; the hairline skirt is pulled to 4 mm in front of the head)
ROOT_BASE = 40.0               # luminance of the roots: the palette's dark end, under the scalp's own 48, so the cap reads as shadowed roots
ROOT_TILE = 512                # px per box chart tile of the cap's texture (2048 x 1024, mostly hidden)
ROOT_EDGE_PX = 12              # px of a view (about 3 mm) inside the hair's edge over which the root cap and the scalp's darkening fade in
LID_LAYER = 'kairo-lid-closed.png'
LID_RETOUCH = 'hairfree-package/kairo-lid-texture-hairfree.png'   # the owner's retouch of the lid texture crop (RGB; the alpha is ours)
LID_MIRROR = True               # her right lid (image left) is her left one mirrored about the face's midline (owner: the painted right lid kept a dark defect in the blink)
MIDLINE = (IRISES[0][0] + IRISES[1][0]) / 2

def mirror_x(x):
    return 2 * MIDLINE - np.asarray(x, float)

def mirrored_lid(lid):
    """The other eye's lid landmarks mirrored about the midline, x increasing again."""
    return dict(ux=list(mirror_x(lid['ux'])[::-1]), uy=list(lid['uy'][::-1]), lx=list(mirror_x(lid['lx'])[::-1]), ly=list(lid['ly'][::-1]),
                outer_left=True)

LID_TARGETS = ['blink_%d' % k for k in range(1, 10)] + ['blink', 'squint']   # the lids' own mesh: closures 0.1..0.9, closed, squint
LID_ROWS = (6, 18, 5)           # grid rows above the crease, crease to lash line, hanging lashes
SQUINT_CLOSURE = 0.38

def lid_shape(top, fold, open_margin, margin, v, w):
    """Image y of the lid's texture rows at closure w, per column: above the crease the painting's skin
    stretches up from the crease; from the crease to the lash line the lid slides down at the painting's
    own scale by the lash line's travel (open margin to closed), rows not yet out of the fold collapsed
    on it (a sheet squeezed between the crease and the lash line showed the whole closed lid, lashes and
    all, as a thin band under the brow); below the lash line the lashes unfold to their painted length.
    Open (w = 0) the painting's rows above its lash line lie over the photo's own lid band, crease to
    margin, and lid_sheets puts them behind the face there: sliding out of the crease instead, they
    unrolled above the photo's lashes, two lash lines for a quarter of the blink. Linear in w between
    keyframes, so the chain of targets at 0.1 steps keeps the rows on the eyeball and the squeeze to
    the row just leaving the fold."""
    L = margin - open_margin
    above = v <= fold
    lid = (v > fold) & (v <= margin)
    # The rows above the crease never move: they are the face's own pixels with the lash tips filled
    # in, hidden behind the face while the eye is open and out from the first keyframe (lid_sheets), so
    # the photo's lashes go in the blink's first frame. Stretched up from the crease instead they
    # squeezed lighter skin from above into a pale plate, and a pale wedge at the corner.
    y = np.where(above, v, 0.0)
    y = np.where(lid, fold + np.maximum(0.0, (v - fold) - (1 - w) * L), y)
    return np.where(~above & ~lid, open_margin + w * L + (v - margin) * w, y)

LID_HIDDEN = 0.0003             # how far behind the face the open lid's lowest rows wait (m): out by closure 0.017 (at 1 mm, out at 0.04, the painting's fold shadow showed as a dark line on the crease for the last frames of every reopening)

def lid_sheets(work, zg, src, surface_normal):
    """The upper lids: the owner's painted closed eye on a sheet per eye that lies over the eyeball
    (1.5 mm out from it) and over the face round it. Open, the sheet is gathered on the crease; closing
    rolls it down over the eye at the painting's scale (lid_shape), lashes at its edge, following the
    ball's curve on a turn. The sheet below the hanging lashes is cut (faded) so the photo's lower lid
    stays; above the brows and over the brow strokes frame_layer has already cleared it."""
    a, solid, alpha, (x0, y0, x1, y1) = frame_layer(work, LID_LAYER)
    lids = list(LIDS)
    if LID_MIRROR:
        xi = np.clip(np.round(mirror_x(np.arange(a.shape[1]))).astype(int), 0, a.shape[1] - 1)
        left = np.arange(a.shape[1]) < MIDLINE
        a[:, left] = a[:, xi][:, left]; solid[:, left] = solid[:, xi][:, left]; alpha[:, left] = alpha[:, xi][:, left]
        lids[0] = mirrored_lid(LIDS[1])
        ys, xs = np.where(solid); x0, y0, x1, y1 = xs.min() - 2, ys.min() - 2, xs.max() + 3, ys.max() + 3
    # A box of the sheet's own past each outer corner (34 px out, 50 px up, down past the hanging
    # lashes), whatever the painting covers there: the photo's outermost lash tip lay beyond the
    # painting's edge and stayed on the face through the blink.
    for k_, lid_ in enumerate(lids):
        cx_, cy_ = (lid_['ux'][0], lid_['uy'][0]) if k_ == 0 else (lid_['ux'][-1], lid_['uy'][-1])
        xa, xb = (cx_ - 34, cx_ + 4) if k_ == 0 else (cx_ - 4, cx_ + 34)
        box = np.zeros(solid.shape, bool); box[int(cy_ - 52):int(cy_ + HANG + 8), int(xa):int(xb)] = True
        alpha = np.maximum(alpha, 255 * np.clip(ndimage.gaussian_filter(box.astype(float), 6) * 1.25, 0, 1)); solid |= box
    ys, xs = np.where(solid); x0, y0, x1, y1 = xs.min() - 2, ys.min() - 2, xs.max() + 3, ys.max() + 3
    layer = Image.fromarray(a[..., :3].astype(np.uint8))
    lum = a[..., :3].mean(-1)
    photo_rgb = np.asarray(src.convert('RGB')).astype(float)
    face_inside = FACE_INFO[1].astype(bool)
    face_nearest = ndimage.distance_transform_edt(~face_inside, return_indices=True)[1]
    # Nothing of the sheet outside the face grid: past its edge the temple falls away behind the
    # borrowed depth, and the corner box stood out from the head's silhouette as a flap in a
    # three-quarter view. Source-pixel mask of the grid, eroded 6 px and feathered.
    sy_, sx_ = np.mgrid[0:solid.shape[0], 0:solid.shape[1]]
    cgrid = src_to_crop(np.stack([sx_.ravel(), sy_.ravel()], 1).astype(float))
    in_src = ndimage.map_coordinates(face_inside.astype(float), [cgrid[:, 1], cgrid[:, 0]], order=1, mode='constant') > 0.5
    in_src = in_src.reshape(solid.shape)
    grid_cut = np.clip((ndimage.distance_transform_edt(in_src) - 6) / 6.0, 0, 1)
    # The brows stay brows: the lash fill below only takes dark pixels under the photo's brow line.
    brow_px = (photo_rgb[..., 2] > photo_rgb[..., 0] + 12) & (np.arange(photo_rgb.shape[0])[:, None] > 380) & (np.arange(photo_rgb.shape[0])[:, None] < 475)
    brow_px = ndimage.binary_opening(brow_px, iterations=1)
    brow_bottom = np.where(brow_px.any(0), np.where(brow_px, np.arange(photo_rgb.shape[0])[:, None], 0).max(0), 440)
    brow_bottom = ndimage.maximum_filter1d(ndimage.median_filter(brow_bottom, 9), 15)
    cam = np.array([0.0, 0.0, D])
    cols = 72
    nA, nB, nC = LID_ROWS
    rows = nA + nB + nC + 1
    pos, uvs, idx, base = [], [], [], 0
    shapes = {n: [] for n in LID_TARGETS}
    cut = np.ones(solid.shape)
    yy = np.arange(solid.shape[0])[:, None].astype(float)
    for k, ((centre, R), lid) in enumerate(zip(EYE_BALLS, lids)):
        side = (np.arange(solid.shape[1]) < SRC_W / 2) if k == 0 else (np.arange(solid.shape[1]) >= SRC_W / 2)
        m = solid & side[None]
        ys, xs = np.where(m)
        sx0, sx1 = lid_span(lid)
        # The sheet ends just past the inner corner (the painting goes on to the nose, and that skin
        # showed as a pale tab there); past the outer corner it keeps the lash wing.
        inner_end = sx1 + 4 if k == 0 else sx0 - 4
        x = np.linspace(xs.min(), min(xs.max(), inner_end), cols) if k == 0 else np.linspace(max(xs.min(), inner_end), xs.max(), cols)
        xi = np.clip(np.round(x).astype(int), 0, solid.shape[1] - 1)
        top = np.array([np.where(m[:, i])[0].min() if m[:, i].any() else np.nan for i in xi])
        ok = ~np.isnan(top)
        top = np.interp(x, x[ok], top[ok]) - 1
        margin = closed_lash_line(layer, lid, x)
        open_margin = np.interp(x, lid['ux'], lid['uy']) + 2.0
        fold, _ = lid_fold(lid, x)
        inside = (x >= sx0) & (x <= sx1)
        open_margin = np.where(inside, np.minimum(open_margin, margin), margin)   # outside the lid's span the lid has no length
        fold = np.where(inside, np.minimum(fold, open_margin), open_margin)
        # The sheet starts at most 30 px above the crease: past the corner the painting reaches the brow,
        # and 80 px of brow skin squeezed into a few made a pale wedge at the corner early in the blink.
        # Past the corner 50: the photo's outermost lashes curl up to 45 px above the corner (owner:
        # "a small lash is still left"); the layer's own alpha keeps the brow out.
        top = np.maximum(top, fold - np.where(inside, 30, 50))
        bottom = margin + HANG + 6
        # The painting below the hanging lashes fades out: the photo's lower lid shows there.
        cols_all = np.arange(solid.shape[1])
        mcol = np.interp(cols_all, x, margin)
        cut[:, side] = np.minimum(cut[:, side], 1 - smooth(mcol[side] + HANG - 2, mcol[side] + HANG + 6, yy))
        # A wide fade: the painting's lighter skin at the corner, stretched up from the margin, was a pale streak.
        cut[:, side] *= (1 - smooth(inner_end - 14, inner_end, cols_all[side].astype(float))) if k == 0 else smooth(inner_end, inner_end + 14, cols_all[side].astype(float))
        # The painting's skin brought to the face's tone round this eye (owner: "a pink patch"): one
        # gain per channel from the painting's lid just under the crease to the photo's skin above the
        # crease and below the lower lid, lashes spared. Her left lid is lighter and pinker than the skin
        # round her right eye once mirrored there. (Spatial ratio fields failed: see the changelog.)
        lower = np.interp(x, lid['lx'], lid['ly'])
        src_px, dst_px = [], []
        for xi_, f_, lo_ in zip(np.round(x).astype(int), fold, lower):
            if not (sx0 + 10 <= xi_ <= sx1 - 10): continue
            src_px.append(a[int(f_ + 4):int(f_ + 22), xi_, :3])
            dst_px.append(np.concatenate([photo_rgb[int(f_ - 25):int(f_ - 8), xi_], photo_rgb[int(lo_ + 6):int(lo_ + 22), xi_]]))
        src_px = np.concatenate(src_px); dst_px = np.concatenate(dst_px)
        src_px = src_px[src_px.mean(-1) > 110]; dst_px = dst_px[(dst_px.mean(-1) > 110) & (dst_px[:, 2] < dst_px[:, 0])]
        gain = np.clip(np.median(dst_px, 0) / np.maximum(np.median(src_px, 0), 1), 0.7, 1.3)
        print(f'lid {k}: painting {np.round(np.median(src_px, 0))} -> skin {np.round(np.median(dst_px, 0))}, gain {np.round(gain, 3)}')
        spare = smooth(60, 120, lum[:, side])[..., None]
        a[:, side, :3] = np.clip(a[:, side, :3] * (1 + (gain - 1) * spare), 0, 255)
        lum[:, side] = a[:, side, :3].mean(-1)
        # Past the outer corner the sheet keeps its skin, so that unrolling it covers the photo's own
        # up-curled wing lashes (owner: "the lashes stay after the lid starts to go down"), but that
        # skin is the face's own picture, pixel for pixel, with the painting's lashes on top: the
        # painting's skin there is the eyes-closed picture's temple, in the hair's shadow, a dark patch
        # (or, mirrored, a pale tab) against the face whenever the lid closed.
        corner = lid['ux'][0] if k == 0 else lid['ux'][-1]
        beyond = smooth(corner - 10, corner - 2, cols_all.astype(float)) if k == 0 else 1 - smooth(corner + 2, corner + 10, cols_all.astype(float))
        beyond = 1 - beyond
        skin_lum = np.median(lum[solid & side[None] & (yy < margin.mean())])
        lashness = np.clip((skin_lum - lum) / 45.0, 0, 1)
        mix = (beyond[None, :] * (1 - lashness))[:, side][..., None]
        a[:, side, :3] = a[:, side, :3] * (1 - mix) + photo_rgb[:, side] * mix
        # Above the crease (and past the corner, above the margin): the face's own pixels with the
        # photo's lashes filled from the skin round them, fading out over the top 10 px. These rows
        # never move (lid_shape), so they are the face itself until the blink's first frame, when they
        # come forward and the lashes are gone.
        fcol = np.interp(cols_all, x, fold)
        above = (yy < fcol[None, :] - 1) | (beyond[None, :] > 0.5) & (yy < mcol[None, :])
        photo_lum = photo_rgb.mean(-1)
        photo_skin = np.median(photo_lum[(yy < fcol[None, :] - 1) & solid & side[None] & (yy > fcol[None, :] - 30)])
        photo_lash = ndimage.maximum_filter(np.clip((photo_skin - photo_lum) / 40.0, 0, 1), 3) * (yy > brow_bottom[None, :] + 2)
        keep = (1 - photo_lash)
        fill = np.stack([ndimage.gaussian_filter(photo_rgb[..., c] * keep, 6) / np.maximum(ndimage.gaussian_filter(keep, 6), 1e-3) for c in range(3)], -1)
        wing_tex = photo_rgb * (1 - photo_lash)[..., None] + fill * photo_lash[..., None]
        a[:, side, :3] = np.where(above[:, side, None], wing_tex[:, side], a[:, side, :3])
        tcol = np.interp(cols_all, x, top)
        cut[:, side] *= smooth(tcol[None, :], tcol[None, :] + 10, yy)[:, side]
        v = np.concatenate([top[None] + (fold - top)[None] * np.linspace(0, 1, nA + 1)[:-1, None],
                            fold[None] + (margin - fold)[None] * np.linspace(0, 1, nB + 1)[:-1, None],
                            margin[None] + (bottom - margin)[None] * np.linspace(0, 1, nC + 1)[:, None]])
        assert v.shape == (rows, cols)
        def place(src_px, behind=None):
            c = src_to_crop(src_px)
            # Depth only from inside the face grid: outside it the height field is the spline's
            # extrapolation, and the corner box past the outer corners reached there — the lid stood out
            # from the temples as spikes in a three-quarter view.
            ci = np.clip(np.round(c).astype(int), 0, N - 1)
            cy_, cx_ = face_nearest[0][ci[:, 1], ci[:, 0]], face_nearest[1][ci[:, 1], ci[:, 0]]
            cc = np.where(face_inside[ci[:, 1], ci[:, 0]][:, None], c, np.stack([cx_, cy_], 1).astype(float))
            zface = ndimage.map_coordinates(zg, [cc[:, 1], cc[:, 0]], order=1) - 0.0015
            ray = unproject(c, np.ones(len(c))) - cam; ray /= np.linalg.norm(ray, axis=1, keepdims=True)
            oc = cam - centre; bq = (ray * oc).sum(1); cq = (oc * oc).sum() - (R + 0.0015) ** 2
            disc = bq * bq - cq
            t = -bq - np.sqrt(np.maximum(disc, 0))
            zball = D - (cam + ray * t[:, None])[:, 2]
            zc = np.where(disc > 0, np.minimum(zface, zball), zface)
            if behind is not None:
                zc = np.where(behind, zface + 0.0015 + LID_HIDDEN, zc)
            return unproject(c, zc)
        grid = lambda ys: np.stack([np.broadcast_to(x, ys.shape), ys], -1).reshape(-1, 2)
        y_rest = lid_shape(top, fold, open_margin, margin, v, 0.0)
        p_rest = place(grid(y_rest), behind=((y_rest > fold[None] + 0.01) | (v < fold[None] - 0.5)).reshape(-1))
        pos.append(p_rest); uvs.append((grid(v) - np.array([x0, y0])) / np.array([x1 - x0, y1 - y0]))
        for n in LID_TARGETS:
            # squint is a closure through the chain (the binding's `closures`), not a shape of its own: a
            # straight path to a 38 % shape squeezed the sheet like the old single target did.
            w = 0.0 if n == 'squint' else 1.0 if n == 'blink' else int(n[6:]) / 10
            shapes[n].append(place(grid(lid_shape(top, fold, open_margin, margin, v, w))) - p_rest)
        for r in range(rows - 1):
            for c_ in range(cols - 1):
                a0 = base + r * cols + c_
                idx += [a0, a0 + cols, a0 + 1, a0 + 1, a0 + cols, a0 + cols + 1]
        base += rows * cols
    rgb = a[y0:y1, x0:x1, :3]
    # The owner's retouch of this crop (hair residue inpainted from the skin round it), pixel for pixel
    # if it is the same crop; a changed crop box would misplace it, so the sizes must match.
    retouch = f'{work}/{LID_RETOUCH}'
    if os.path.exists(retouch) and not LID_MIRROR:          # the retouch fixed the right lid's strand; mirrored, that lid is gone
        r = Image.open(retouch).convert('RGBA')
        if r.size == (x1 - x0, y1 - y0):
            rgb = np.asarray(r).astype(float)[..., :3]
        else:
            print(f'lid retouch {LID_RETOUCH} is {r.size}, the crop is {(x1 - x0, y1 - y0)}: not used')
    cut *= grid_cut
    tex = Image.fromarray(np.dstack([rgb, (alpha * cut)[y0:y1, x0:x1]]).astype(np.uint8), 'RGBA')
    P = np.concatenate(pos)
    # The face's own normals: with a flat normal the sheet's lit tenth shaded a little differently from
    # the face under it, and the rows that copy the face's pixels showed as a faintly lighter patch.
    nrm = surface_normal(P)
    return dict(pos=P, nrm=nrm, uv=np.concatenate(uvs), idx=np.array(idx, np.uint32), material=LID_MATERIAL,
                targets=[np.concatenate(shapes[n]) for n in LID_TARGETS]), tex

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
    # And none of the layer's own brow strokes and hair strands, wherever they lie (past the brow's
    # ends too). Their colour goes as well: the fringe of a strand kept its dark blue under the cut's
    # alpha ramp, a dark streak at the outer corner of the lid whenever it closed.
    # A strand over skin is a muted purple, bluer and darker than the skin round it rather than blue
    # in itself: found against a local median of the layer.
    rb = a[..., 0] - a[..., 2]; lum = a[..., :3].mean(-1)
    tint = (ndimage.median_filter(rb, 21) - rb > 12) | (ndimage.median_filter(lum, 21) - lum > 40)
    stroke = ((a[..., 2] > a[..., 0] + 12) | (a[..., :3].sum(-1) < 150) | tint) & (rows < 500)
    a[..., 3] *= 1 - ndimage.gaussian_filter(ndimage.binary_dilation(stroke, iterations=3).astype(float), 1.5)
    solid = a[..., 3] > 10
    source = (a[..., 3] > 200) & ~ndimage.binary_dilation(stroke, iterations=5)
    _, (iy, ix) = ndimage.distance_transform_edt(~source, return_indices=True)
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
    hair = ndimage.binary_opening(hair, iterations=1) & ~face_mask_src(src)     # not the brows, lashes and irises, dark too
    _, (iy, ix) = ndimage.distance_transform_edt(~hair, return_indices=True)
    filled = a[iy, ix]
    filled = np.where(hair[..., None], a, ndimage.gaussian_filter(filled, (2, 2, 0)))
    return Image.fromarray(np.clip(filled, 0, 255).astype(np.uint8))

def hair_style(horig, work, src, axis):
    """The hair atlas's colour: the generator's hair texture with every non-hair texel (skin, knit and
    background in the atlas's gaps: the beige and grey patches between strands) replaced by the nearest
    hair texel, re-coloured along the photo's own hair palette by luminance (the generator's sides carry
    pink, lilac and beige the photo's indigo has not), with fine strands along the hair's flow. The
    flow runs down the sides and the nape and out from the crown, and lines of constant angle about
    the neck's axis are both, so the strands are waves in that angle, drifting a little with height."""
    a = np.asarray(Image.open(f'{work}/{HAIR_TEX}').convert('RGB')).astype(float)
    hair = (a[..., 2] > a[..., 0] + 12) | (a.sum(-1) < 150)
    _, (iy, ix) = ndimage.distance_transform_edt(~hair, return_indices=True)
    filled = np.where(hair[..., None], a, ndimage.gaussian_filter(a[iy, ix], (2, 2, 0)))
    lum_w = np.array([0.299, 0.587, 0.114])
    L = filled @ lum_w
    op, ouv = horig; tree = cKDTree(op)
    p = np.asarray(src).astype(float)
    ph = (p[..., 2] > p[..., 0] + 12) | (p.sum(-1) < 150)
    # The hair rule also takes the photo's bluish-grey background and the knit; they lifted the palette's
    # light end to the background's grey and the sides came out pale and grey.
    bg_ = np.median(np.concatenate([p[:40, :40].reshape(-1, 3), p[:40, -40:].reshape(-1, 3)]), 0)
    ph &= (np.abs(p - bg_).max(-1) > 28) & (np.arange(p.shape[0])[:, None] < COLLAR_Y - 18)
    ph = ndimage.binary_erosion(ndimage.binary_opening(ph, iterations=1), iterations=3)
    pl = np.clip(p[ph] @ lum_w, 0, 255).astype(int); pc = p[ph]
    cnt = np.bincount(pl, minlength=256).astype(float); ramp = np.zeros((256, 3))
    for k in range(3): ramp[:, k] = np.bincount(pl, pc[:, k], minlength=256)
    have = cnt > 20; ramp[have] /= cnt[have, None]
    xs = np.arange(256)
    for k in range(3): ramp[:, k] = np.interp(xs, xs[have], ramp[have, k])
    ramp = ndimage.gaussian_filter1d(ramp, 6, axis=0)
    print('hair palette: dark', ramp[30].round(0), 'mid', ramp[128].round(0), 'light', ramp[220].round(0))
    rng = np.random.default_rng(11)
    waves = [(k, rng.uniform(0, 2 * np.pi), d, amp) for k, d, amp in HAIR_STRANDS]
    def texel(i):
        u = ouv[i]
        return L[np.clip(((1 - u[..., 1]) * L.shape[0]).astype(int), 0, L.shape[0] - 1), np.clip((u[..., 0] * L.shape[1]).astype(int), 0, L.shape[1] - 1)]
    def sample(points, gain=1.0, base=None):
        if base is not None:
            th = np.arctan2(points[:, 0] - axis[0], points[:, 2] - axis[1]); r = np.hypot(points[:, 0] - axis[0], points[:, 2] - axis[1])
            s = points[:, 1] + 0.5 * r; streak = np.zeros(len(points))
            # Only the fine octaves: the locks' waves on a flat base read as a striped cap along the hairline.
            for k, ph_, d, amp in waves:
                if k >= 90:
                    streak += amp * smooth(0.002, 0.004, 2 * np.pi * r / k) * np.sin(k * th + d * s + ph_)
            return ramp[np.clip(base + SCALP_STRANDS * streak, 0, 255).astype(int)]
        d, i = tree.query(points, k=12)
        # On a strand its own texel; off the strands (the shell between them) the mean of the twelve
        # nearest, since one strand's texel per point speckled the shell.
        l = np.where(d[:, 0] < HAIR_ON_STRAND, texel(i[:, 0]), texel(i).mean(1)) * gain
        th = np.arctan2(points[:, 0] - axis[0], points[:, 2] - axis[1]); r = np.hypot(points[:, 0] - axis[0], points[:, 2] - axis[1])
        s = points[:, 1] + 0.5 * r
        streak = np.zeros(len(points))
        for k, ph_, d, amp in waves:
            # an octave fades out where its period round the axis falls under 2 mm (the crown), or it aliases
            streak += amp * smooth(0.002, 0.004, 2 * np.pi * r / k) * np.sin(k * th + d * s + ph_)
        return ramp[np.clip(l + streak, 0, 255).astype(int)]
    sample.palette = lambda l: ramp[int(np.clip(l, 0, 255))]
    return sample

SCALP_STRANDS = 0.6           # the strands' amplitude on the scalp (at 1 the scalp read as a striped cap along the hairline)
SCALP_BASE = 48.0              # luminance of the scalp under the hair before the strands (the palette's dark indigo)
HAIR_ON_STRAND = 0.0025        # a sample point nearer the scan's surface than this (m) lies on a strand
HAIR_FORM = (0.62, 0.42)         # the hair atlas's baked light: ambient, and the lit side's gain (times the smoothed normal's lambert term)
HAIR_STRANDS = ((18, 25.0, 14.0), (36, 40.0, 10.0), (90, 60.0, 14.0), (180, 90.0, 10.0), (360, 140.0, 7.0))   # (waves round the axis, drift with height, amplitude in luminance): locks, then strands

def hair_projector(work, P, F, axis, fallback_style=None, head_pts=None):
    """The hair's colour from the owner's generated views: each point of the hair's envelope takes the
    views that see it (Pixal3D's cameras, which the views were reconstructed with, so every view's
    silhouette is the envelope's), weighted by how squarely they see it; the front view is the photo.
    Per view the hair's luminance is matched to the photo's (two points, p10 and p90), so the views'
    differing light cannot meet in seams; the strands themselves are each view's own."""
    import json
    D = f'{work}/mv-hair'
    fit = json.load(open(f'{D}/fit.json')); s_, t_ = fit['scale'], np.array(fit['translation'])
    VD = f'{D}/views1452' if os.path.isdir(f'{D}/views1452') else D       # the views at the photo's own resolution, if staged
    meta = json.load(open(f'{VD}/transforms.json')); fov = meta['camera_angle_x']
    lum_w = np.array([0.299, 0.587, 0.114])
    def to_world(q):
        m = (q - t_) / s_
        return np.stack([m[:, 0], -m[:, 2], m[:, 1]], 1)      # the turned frame -> Pixal3D's world (perm (0, 2, 1), sign (-1, 1, 1) on the raw axes)
    def dir_world(n):
        return np.stack([n[:, 0], -n[:, 2], n[:, 1]], 1)
    fn = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]]); fn /= np.linalg.norm(fn, axis=1, keepdims=True) + 1e-12
    fc = P[F].mean(1)
    ctr = np.array([axis[0], np.median(fc[:, 1]), axis[1]])
    fn *= np.sign(((fc - ctr) * fn).sum(1, keepdims=True) + 1e-9)       # outward
    ftree = cKDTree(fc)
    # Normals smoothed over 2 cm: per face, the views' weights jumped between neighbouring triangles and
    # the hair read as cut glass (every triangle its own mix of views).
    for _ in range(2):
        nb = ftree.query_ball_point(fc, r=HAIR_PROJ_SMOOTH)
        fn = np.array([fn[i].mean(0) for i in nb]); fn /= np.linalg.norm(fn, axis=1, keepdims=True) + 1e-12
    rng = np.random.default_rng(9); bary = rng.dirichlet((1, 1, 1), size=48)
    samples = np.einsum('bk,fkd->fbd', bary, P[F]).reshape(-1, 3)
    Ws = to_world(samples)
    views = []
    ref = None
    def inpaint_edge(rgb, hairpx, px=2):
        """The view's pixels within `px` of the hair's edge replaced by the nearest pixel further in: the edge
        pixels are hair and skin (or backdrop) mixed, light, and projected onto the layer's edge they drew a
        bright dotted outline along every cut (the comb teeth on the sheet in front of the ear)."""
        core = ndimage.binary_erosion(hairpx, iterations=px)
        _, (iy, ix) = ndimage.distance_transform_edt(~core, return_indices=True)
        return np.where((hairpx & ~core)[..., None], rgb[iy, ix], rgb)
    for fr in meta['frames']:
        im = Image.open(f"{VD}/{fr['file_path']}").convert('RGBA'); S_ = im.size[0]
        a = np.asarray(im).astype(float); rgb, alpha = a[..., :3], a[..., 3] / 255.0
        # Softened a little: single-pixel noise in the generated views became grain on the hair.
        rgb = ndimage.gaussian_filter(rgb, (HAIR_PROJ_BLUR, HAIR_PROJ_BLUR, 0))
        # Hair by hue and saturation: the strands' light highlights pass (a luminance cap dropped them, and
        # the fallback dark showed as confetti); the knit (lavender, low saturation) and the skin do not.
        sat = (rgb.max(-1) - rgb.min(-1)) / (rgb.max(-1) + 1e-6)
        hairpx = (rgb[..., 2] > rgb[..., 0] + 10) & ((sat > 0.25) | ((rgb @ lum_w) < 135)) & (alpha > 0.5)
        hairpx = ndimage.binary_opening(hairpx, iterations=1)
        raw_ = hairpx.copy()
        rgb = inpaint_edge(rgb, hairpx)
        # every non-hair pixel as the nearest hair pixel: the cards sample along their strips' edges
        _, (fy_, fx_) = ndimage.distance_transform_edt(~ndimage.binary_erosion(hairpx, iterations=2), return_indices=True)
        filled_ = rgb[fy_, fx_]
        c2w = np.array(fr['transform_matrix'], float); w2c = np.linalg.inv(c2w)
        focal = (S_ / 2.0) / np.tan(fov / 2.0)
        def proj(W, w2c=w2c, focal=focal, S_=S_):
            pc = W @ w2c[:3, :3].T + w2c[:3, 3]
            dist = -pc[:, 2]
            return focal * pc[:, 0] / (dist + 1e-8) + S_ / 2.0, -focal * pc[:, 1] / (dist + 1e-8) + S_ / 2.0, dist
        # the view's depth buffer from the envelope (4 px cells, the nearest sample per cell, spread by one cell)
        x, y, d = proj(Ws); cell = 4; B = S_ // cell
        ix = np.clip((x / cell).astype(int), 0, B - 1); iy = np.clip((y / cell).astype(int), 0, B - 1)
        zb = np.full((B, B), np.inf); np.minimum.at(zb, (iy, ix), d)
        zb_env = zb.copy(); zb_head = np.full((B, B), np.inf)      # the envelope's and the head's own depths (unspread): the hair cards' silhouette and occlusion
        if head_pts is not None:
            # ...and from the head: a view cuts and colours only what it sees, and the bob's side in front of the
            # ear lies behind the cheek from the front (the photo shows cheek there because the cheek is in front).
            xh, yh, dh = proj(to_world(head_pts))
            ixh = np.clip((xh / cell).astype(int), 0, B - 1); iyh = np.clip((yh / cell).astype(int), 0, B - 1)
            okh = (xh >= 0) & (xh < S_) & (yh >= 0) & (yh < S_) & (dh > 0)
            np.minimum.at(zb, (iyh[okh], ixh[okh]), dh[okh]); np.minimum.at(zb_head, (iyh[okh], ixh[okh]), dh[okh])
        zb = ndimage.minimum_filter(zb, size=3)
        lum = rgb @ lum_w
        pl = np.percentile(lum[hairpx], [10, 90]) if hairpx.sum() > 100 else np.array([40.0, 120.0])
        skinpx = ndimage.binary_erosion((rgb[..., 0] > rgb[..., 2] + 30) & (rgb.sum(-1) > 200) & (alpha > 0.5), iterations=1)
        # Where the view shows the subject and not hair (skin, eyes, brows, lashes, the knit): the hair
        # layer is cut there. By skin alone the eyes and the brows kept hair over them once the
        # face-mask drop rule was off.
        nothair = (alpha > 0.5) & ~ndimage.binary_dilation(hairpx, iterations=2)
        if views:
            # Closed by HAIR_VIEW_CLOSE px first: the bob's side in front of the ear is strands with the cheek
            # between them in the profile views, and cut strand by strand on a surface it read as ragged.
            closed_ = ndimage.binary_closing(hairpx, iterations=HAIR_VIEW_CLOSE)
            # ...and smoothed over HAIR_VIEW_SMOOTH px: the strand-level outline cut the surface in a saw-tooth.
            closed_ = ndimage.gaussian_filter(closed_.astype(float), HAIR_VIEW_SMOOTH) > 0.5
            nothair = (alpha > 0.5) & ~ndimage.binary_dilation(closed_, iterations=2)
            # The side and back views cut the layer only where they show skin in the open (the cheek, the ear,
            # the neck below the hair): a small patch of skin enclosed by hair is the neck or the scalp seen
            # between two strands of that view, and cut into the envelope's smooth surface it was a hole
            # showing the pink neck from behind (the owner's circles at the nape). The surface keeps its
            # texels there (the views give them no colour, so they take the dark strands).
            lab_, _ = ndimage.label(nothair)
            sizes_ = np.bincount(lab_.ravel()); small_ = sizes_ < HAIR_VIEW_GAP_PX; small_[0] = False
            nothair &= ~small_[lab_]
        if not views:
            # The front view is the photo: its face (eyes, brows and mouth included, which are blue or
            # dark and pass the hair rule) is never hair. Only strands wider than 10 px are hair inside
            # the face's oval; the mask's own edge follows them.
            # The photo is staged in a larger frame: the mask is built on the photo and placed at the photo's
            # offset in the frame (built on the frame's own pixels with the landmarks in photo coordinates, it
            # sat 106 x 90 px off: it covered her right temple and missed half of each eye). The photo's locks
            # stay hair inside it: by the mask's own rule strands under 10 px were face, and the bangs' lock's
            # thin end over the temple was cut out of the layer (the owner's hole in the bangs).
            photo_ = Image.open(f'{work}/kairo-front.png').convert('RGB')
            ox_, oy_ = photo_offset(a[..., :3], np.asarray(photo_))
            def in_frame(m_):
                out_ = np.zeros((S_, S_), bool); h_, w_ = m_.shape
                out_[oy_:oy_ + h_, ox_:ox_ + w_] = m_[:S_ - oy_, :S_ - ox_]
                return out_
            fm_ = in_frame(face_mask_src(photo_)); lock_ = in_frame(lock_mask_src(photo_)) & fm_
            print('hair view', fr['file_path'], 'holds the photo at', (ox_, oy_), '; lock px inside the face mask', int(lock_.sum()))
            # Inside the face mask the locks are hair pixel for pixel, skin between their strands included: the
            # layer is continuous over them and shows the photo there (cut strand by strand, the gaps drew the
            # dark fallback as torn triangles along the fringe; the surface lies 4-20 mm off the forehead, so
            # the photo's skin on it meets the skin behind).
            # The fringe's outline closed over HAIR_FRONT_CLOSE px and smoothed as the side views' are: the strand
            # tips' gaps cut the slab's edge into a saw-tooth of slivers.
            hairpx = ndimage.binary_closing(hairpx, iterations=HAIR_FRONT_CLOSE)
            hairpx = ndimage.gaussian_filter(hairpx.astype(float), HAIR_VIEW_SMOOTH) > 0.5
            hairpx = hairpx | lock_
            raw_ = (raw_ | lock_) & ~(fm_ & ~lock_)        # the cards' hair: the irises and the brows pass the hue rule
            nothair = (alpha > 0.5) & ~ndimage.binary_dilation(hairpx, iterations=2)
            nothair |= fm_ & ~lock_ & (alpha > 0.5)
        views.append(dict(rgb=rgb, alpha=alpha, hairpx=hairpx, raw=raw_, filled=filled_, skinpx=skinpx, nothair=nothair, proj=proj, zb=zb, zb_env=zb_env, zb_head=zb_head, cell=cell, S=S_, cam=c2w[:3, 3], c2w=c2w, focal=focal, pl=pl, name=fr['file_path']))
        if ref is None:
            ref = pl
    crown_fit = f'{D}/crown-fit.json'
    if os.path.exists(crown_fit) and os.path.exists(f'{work}/{CROWN_VIEW}'):
        # The owner's crown view (the head pitched down, seen from the front; tools/crownfit.py fitted its pitch,
        # scale and offset by silhouette) as a fifth view: the turntable's four see the top of the head at grazing
        # angles only, and it drew the fallback's ring pattern instead of the parting's strands.
        cf = json.load(open(crown_fit)); th_ = math.radians(cf['pitch']); sc_ = cf['scale']; off_ = np.array(cf['offset']); piv_ = np.array(cf['pivot'])
        R_ = np.array([[1, 0, 0], [0, math.cos(th_), -math.sin(th_)], [0, math.sin(th_), math.cos(th_)]])
        def to_model(Wq):
            return np.stack([Wq[:, 0], Wq[:, 2], -Wq[:, 1]], 1) * s_ + t_
        def proj_c(Wq, R_=R_, sc_=sc_, off_=off_, piv_=piv_):
            px, zc = project(piv_ + (to_model(Wq) - piv_) @ R_.T)
            return px[:, 0] * sc_ + off_[0], px[:, 1] * sc_ + off_[1], zc
        im = Image.open(f'{work}/{CROWN_VIEW}').convert('RGB'); S_ = im.size[0]
        rgb = np.asarray(im).astype(float)
        corner = np.median(np.concatenate([rgb[:30, :30].reshape(-1, 3), rgb[:30, -30:].reshape(-1, 3)]), 0)
        alpha = ndimage.binary_fill_holes(ndimage.binary_closing(np.abs(rgb - corner).max(-1) > 22, iterations=3)).astype(float)
        rgb = ndimage.gaussian_filter(rgb, (HAIR_PROJ_BLUR, HAIR_PROJ_BLUR, 0))
        sat = (rgb.max(-1) - rgb.min(-1)) / (rgb.max(-1) + 1e-6)
        hairpx = (rgb[..., 2] > rgb[..., 0] + 10) & ((sat > 0.25) | ((rgb @ lum_w) < 135)) & (alpha > 0.5)
        hairpx = ndimage.binary_opening(hairpx, iterations=1)
        rgb = inpaint_edge(rgb, hairpx)
        x, y, d = proj_c(Ws); cell = 4; B = S_ // cell
        ix = np.clip((x / cell).astype(int), 0, B - 1); iy = np.clip((y / cell).astype(int), 0, B - 1)
        zb = np.full((B, B), np.inf); np.minimum.at(zb, (iy, ix), d)
        if head_pts is not None:
            xh, yh, dh = proj_c(to_world(head_pts)); okh = (xh >= 0) & (xh < S_) & (yh >= 0) & (yh < S_) & (dh > 0)
            np.minimum.at(zb, (np.clip((yh[okh] / cell).astype(int), 0, B - 1), np.clip((xh[okh] / cell).astype(int), 0, B - 1)), dh[okh])
        zb = ndimage.minimum_filter(zb, size=3)
        lum = rgb @ lum_w
        pl = np.percentile(lum[hairpx], [10, 90]) if hairpx.sum() > 100 else np.array([40.0, 120.0])
        skinpx = ndimage.binary_erosion((rgb[..., 0] > rgb[..., 2] + 30) & (rgb.sum(-1) > 200) & (alpha > 0.5), iterations=1)
        closed_ = ndimage.gaussian_filter(ndimage.binary_closing(hairpx, iterations=HAIR_VIEW_CLOSE).astype(float), HAIR_VIEW_SMOOTH) > 0.5
        nothair = (alpha > 0.5) & ~ndimage.binary_dilation(closed_, iterations=2)
        lab_, _ = ndimage.label(nothair); sizes_ = np.bincount(lab_.ravel()); small_ = sizes_ < HAIR_VIEW_GAP_PX; small_[0] = False
        nothair &= ~small_[lab_]
        cam_w = to_world((piv_ + (np.array([0.0, 0.0, CAMERA_D]) - piv_) @ R_)[None])[0]
        views.append(dict(rgb=rgb, alpha=alpha, hairpx=hairpx, skinpx=skinpx, nothair=nothair, proj=proj_c, zb=zb, cell=cell, S=S_, cam=cam_w, pl=pl, name='crown'))
        print('hair view crown: pitch', round(cf['pitch'], 1), 'deg, fit IoU', round(cf['iou'], 3))
    for v in views:
        v['edge_d'] = ndimage.distance_transform_edt(v['nothair'])      # px into the view's skin from the hair's edge
        v['hair_d'] = ndimage.distance_transform_edt(~v['nothair'])     # px into the view's hair from the skin's edge
        # luminance matched to the front's by two points
        g_ = (ref[1] - ref[0]) / max(v['pl'][1] - v['pl'][0], 1.0); o_ = ref[0] - g_ * v['pl'][0]
        lum = v['rgb'] @ lum_w
        v['rgb'] = v['rgb'] * ((g_ * lum + o_) / np.maximum(lum, 1.0))[..., None]
        if 'filled' in v:
            lf_ = v['filled'] @ lum_w
            v['filled'] = v['filled'] * ((g_ * lf_ + o_) / np.maximum(lf_, 1.0))[..., None]
        print('hair view', v['name'], 'hair px', int(v['hairpx'].sum()), 'lum p10/p90', np.round(v['pl'], 0), '-> gain', round(float(g_), 3))
    fallback = np.array([44.0, 41.0, 86.0])
    def sample(points, gain=1.0, base=None):
        if base is not None:
            return np.broadcast_to(fallback * (base / 48.0), (len(points), 3)).copy()
        W = to_world(points)
        n = dir_world(fn[ftree.query(points)[1]])
        acc = np.zeros((len(points), 3)); tot = np.zeros(len(points))
        for v in views:
            x, y, d = v['proj'](W); S_ = v['S']
            inside = (x >= 0) & (x < S_ - 1) & (y >= 0) & (y < S_ - 1) & (d > 0)
            xi = np.clip(x.astype(int), 0, S_ - 1); yi = np.clip(y.astype(int), 0, S_ - 1)
            cx = np.clip((x / v['cell']).astype(int), 0, v['zb'].shape[1] - 1); cy = np.clip((y / v['cell']).astype(int), 0, v['zb'].shape[0] - 1)
            vis = d <= v['zb'][cy, cx] + HAIR_PROJ_TOL
            dcam = v['cam'][None, :] - W; dcam /= np.linalg.norm(dcam, axis=1, keepdims=True) + 1e-12
            # Unsigned: the layer is a sheet shown from both sides, and a groove's far wall between two locks,
            # facing away from every camera, took the fallback and showed as black facets beside the lock.
            cosang = np.abs((n * dcam).sum(1))
            if v is views[0]:
                # Along the hairline the front view counts at least as a grazing view (cosine HAIR_FRONT_MIN_COS):
                # the slab's edge walls under the strand tips, seen squarely by no view, took the dark fallback
                # and showed as dark flecks along the fringe; with the photo's pixels they are the strands' thickness.
                cosang = np.where(band_px[yi, xi], np.maximum(cosang, HAIR_FRONT_MIN_COS), cosang)
            w = inside * vis * v['hairpx'][yi, xi] * v['alpha'][yi, xi] * cosang ** HAIR_PROJ_POWER
            acc += w[:, None] * v['rgb'][yi, xi]; tot += w
        # Where no view sees (undersides, the inside of the shell seen through a gap): the palette's dark
        # with the strands, not a flat colour (flat patches read as holes).
        fb = fallback_style(points, base=HAIR_PROJ_FALLBACK_BASE) if fallback_style is not None else np.broadcast_to(fallback, (len(points), 3))
        # (A point seen by no view better than grazing, HAIR_PROJ_MIN_W: the undersides and inner walls of the bangs'
        #  slab over the temple took the front view's pixels alone, normalised to full weight and stretched along
        #  the view into stripes; they are the shadow under the hair and take the dark strands.)
        out = np.where((tot > HAIR_PROJ_MIN_W)[:, None], acc / np.maximum(tot, 1e-6)[:, None], fb)
        return out * gain
    front_view = views[0]; z_front = np.percentile(P[:, 2], 99.5)
    def alpha(points):
        """0 where the hair layer must not show: a point that a view sees squarely and unoccluded
        where that view shows skin. From the front that makes the hairline the photo's own hair mask,
        strand by strand, instead of the envelope's voxel edge; from the sides it takes the envelope's
        flaps in front of and under the ear, which lie over the cheek in the owner's views and, with
        no view giving them hair, came out as dark shards (owner: "ser inte ut som hår alls")."""
        W = to_world(points)
        n = dir_world(fn[ftree.query(points)[1]])
        cut = np.zeros(len(points), bool)
        for k_, v in enumerate(views):
            x, y, d = v['proj'](W); S_ = v['S']
            inside = (x >= 0) & (x < S_ - 1) & (y >= 0) & (y < S_ - 1) & (d > 0)
            xi = np.clip(x.astype(int), 0, S_ - 1); yi = np.clip(y.astype(int), 0, S_ - 1)
            cx = np.clip((x / v['cell']).astype(int), 0, v['zb'].shape[1] - 1); cy = np.clip((y / v['cell']).astype(int), 0, v['zb'].shape[0] - 1)
            vis = d <= v['zb'][cy, cx] + HAIR_PROJ_TOL
            dcam = v['cam'][None, :] - W; dcam /= np.linalg.norm(dcam, axis=1, keepdims=True) + 1e-12
            squarely = (n * dcam).sum(1) > HAIR_MASK_COS
            depth_ok = (points[:, 2] > z_front - HAIR_MASK_DEPTH) if k_ == 0 else np.ones(len(points), bool)
            if k_ == 0:
                # The front view cuts whatever it sees over skin within its band, whichever way it faces: the
                # slab's side walls and back round a thin lock, 2-5 cm off the skin and facing sideways, showed
                # through the cut front face as a dark striped box (a hug rule within 2 cm of the skin missed them).
                # A point it sees edge-on, though, only where the skin reaches HAIR_EDGE_ON_PX past the hair's
                # edge: on the bob's side in front of the ear, parallel to the view, the mask's edge projected as
                # comb teeth along the sheet's lower edge (a pixel of the mask is centimetres along the sheet).
                deep = v['edge_d'][yi, xi] >= HAIR_EDGE_ON_PX
                squarely = squarely | deep
            cut |= inside & vis & squarely & v['nothair'][yi, xi] & depth_ok
        ear_cut_ = getattr(sample, 'ear_cut', None)
        if ear_cut_ is not None:
            cut |= ear_cut_(points)
        return np.where(cut, 0.0, 1.0)
    sample.alpha = alpha
    def side_skin(points):
        """True where a side or back view sees the point squarely and unoccluded and shows skin there: the
        temple below the bangs' edge, the neck below the hair. The scalp's coverage (compose's hair weight) stops
        there, so the skin bake keeps its skin and the root cap ends: with the front photo alone deciding, the
        temple under the bangs' sweep counted as scalp, and its dark root strands stood between the bangs and
        the ear as a striped wall from three quarters."""
        W = to_world(points)
        n = dir_world(fn[ftree.query(points)[1]])
        seen = np.zeros(len(points), bool)
        for v in views[1:]:
            x, y, d = v['proj'](W); S_ = v['S']
            inside = (x >= 0) & (x < S_ - 1) & (y >= 0) & (y < S_ - 1) & (d > 0)
            xi = np.clip(x.astype(int), 0, S_ - 1); yi = np.clip(y.astype(int), 0, S_ - 1)
            cx = np.clip((x / v['cell']).astype(int), 0, v['zb'].shape[1] - 1); cy = np.clip((y / v['cell']).astype(int), 0, v['zb'].shape[0] - 1)
            vis = d <= v['zb'][cy, cx] + HAIR_PROJ_TOL
            dcam = v['cam'][None, :] - W; dcam /= np.linalg.norm(dcam, axis=1, keepdims=True) + 1e-12
            seen |= inside & vis & ((n * dcam).sum(1) > HAIR_MASK_COS) & v['nothair'][yi, xi]
        return seen
    sample.side_skin = side_skin
    def edge_fade(points):
        """0..1: how far inside the hair's edge a point lies, by the views that see it (the front within its
        depth band): the distance in a view's pixels from the point's pixel to that view's skin, from
        ROOT_EDGE_PX / 2 to 1.5 x ROOT_EDGE_PX. The root cap and the scalp's darkening fade out under the
        hair layer's edge instead of standing beside it as dark flecks along the fringe and the temples."""
        W = to_world(points)
        fade = np.ones(len(points))
        for k_, v in enumerate(views):
            x, y, d = v['proj'](W); S_ = v['S']
            inside = (x >= 0) & (x < S_ - 1) & (y >= 0) & (y < S_ - 1) & (d > 0)
            xi = np.clip(x.astype(int), 0, S_ - 1); yi = np.clip(y.astype(int), 0, S_ - 1)
            cx = np.clip((x / v['cell']).astype(int), 0, v['zb'].shape[1] - 1); cy = np.clip((y / v['cell']).astype(int), 0, v['zb'].shape[0] - 1)
            vis = d <= v['zb'][cy, cx] + HAIR_PROJ_TOL
            if k_ == 0:
                sees = points[:, 2] > z_front - HAIR_MASK_DEPTH
            else:
                dcam = v['cam'][None, :] - W; dcam /= np.linalg.norm(dcam, axis=1, keepdims=True) + 1e-12
                sees = (dir_world(fn[ftree.query(points)[1]]) * dcam).sum(1) > HAIR_MASK_COS
            f = smooth(ROOT_EDGE_PX * 0.5, ROOT_EDGE_PX * 1.5, v['hair_d'][yi, xi])
            fade = np.where(inside & vis & sees, np.minimum(fade, f), fade)
        return fade
    sample.edge_fade = edge_fade
    band_px = ndimage.binary_dilation(front_view['nothair'], iterations=HAIR_BAND_PX)
    def hairline(points):
        """True for points near the front whose pixel in the front view lies on or within HAIR_BAND_PX of
        the photo's skin: the hairline band, baked in a chart of its own (a front projection) so that
        no face there is foreshortened in a side chart or overwritten by the side's hair."""
        W = to_world(points); v = front_view
        x, y, d = v['proj'](W); S_ = v['S']
        inside = (x >= 0) & (x < S_ - 1) & (y >= 0) & (y < S_ - 1) & (d > 0)
        xi = np.clip(x.astype(int), 0, S_ - 1); yi = np.clip(y.astype(int), 0, S_ - 1)
        return inside & band_px[yi, xi] & (points[:, 2] > z_front - HAIR_MASK_DEPTH)
    sample.hairline = hairline
    sample.views = views
    sample.to_world = to_world
    def from_world(Wq):
        return np.stack([Wq[:, 0], Wq[:, 2], -Wq[:, 1]], 1) * s_ + t_
    sample.from_world = from_world
    sample.normals = (to_world(fc), dir_world(fn))     # the envelope's face centres and smoothed outward normals, in the views' world: the hair cards' facing
    return sample

HAIR_MASK_COS = 0.4             # a side or back view cuts the hair layer where it shows skin only for points it sees this squarely (cosine); the front view at any facing
HAIR_BAND_PX = 30               # px of the front view round the photo's skin that count as the hairline band
HAIR_VIEW_GAP_PX = 600          # px of a side or back view: a smaller patch of skin enclosed by hair does not cut the layer (a gap between that view's strands)
HAIR_VIEW_CLOSE = 8             # px a side or back view's hair mask is closed by before it cuts (strands with skin between them stay one surface)
HAIR_VIEW_SMOOTH = 3.0          # px of blur on a view's closed hair mask before it cuts (a strand-level outline cut the surface in a saw-tooth)
HAIR_FRONT_CLOSE = 4            # px the front view's hair mask is closed by outside the face mask (the fringe's strand tips)
HAIR_FRONT_MIN_COS = 0.35       # along the hairline band the front view counts at least this squarely (its walls took the dark fallback otherwise)
HAIR_EDGE_ON_PX = 40            # px of the front view: a point it sees edge-on is cut only this far inside the photo's skin
HAIR_MASK_DEPTH = 0.10          # m behind the front within which the photo's skin cuts the hair layer (alpha 0)
HAIR_PROJ_FALLBACK_BASE = 52.0  # luminance of the strand-textured fallback where no view sees
CROWN_VIEW = 'assets-hair/crown.png'   # the owner's crown view, painted as a fifth view when mv-hair/crown-fit.json exists (tools/crownfit.py)
# (the tolerance spans the shell's thickness on purpose: a point on the inner surface, seen only through a gap, then
#  takes the outer surface's strands from the view instead of the fallback; the far side of the head stays out at 15 cm+)
HAIR_PROJ_TOL = 0.22            # world units (the head is ~1): a point this far behind the envelope's depth in a view still counts as seen
# (0.22: at 0.08 the walls of the pocket between the bob's side and the temple, 0.08-0.15 behind the side the profile
#  view sees, took the dark fallback and read as a flat wall from three quarters, and at 0.16 the deepest of them still
#  took the front view's pixels alone, stretched into stripes; the far side of the head stays out at 0.3)
HAIR_PROJ_BLUR = 0.6           # px of blur on the 1024 px views before sampling
HAIR_PROJ_SMOOTH = 0.02        # radius (m) the envelope's normals are averaged over for the views' weights
HAIR_PROJ_POWER = 4.0            # the views' weight by how squarely they see a point: high, so one view dominates and strands stay crisp
HAIR_PROJ_MIN_W = 0.3 ** 4       # a point's best view must see it at least this squarely (cosine 0.3 at power 4) or the fallback paints it

# A paintable layout for the skin the photo does not cover: orthographic views in fixed windows of
# model space (so a painting stays put when the mesh changes), one 1024 px tile each.
# (name, tile column, tile row, outward axis, sign, window size in model units)
# The ear tiles are unused since the auricles got a per-triangle atlas of their own (`ear` material:
# in a side projection the concha's floor shared texels with the helix overhanging it); they stay so
# the other charts keep their place for paintings.
UNDER_GRID_DEPTH = 0.012       # m behind the front-most surface within which skull skin under the bald picture's oval takes the picture's pixels
SKIN_TILE = 1024
SKIN_CHARTS = [("right", 0, 0, 0, 1, 0.72), ("left", 1, 0, 0, -1, 0.72), ("back", 2, 0, 2, -1, 0.72), ("top", 3, 0, 1, 1, 0.6),
               ("under", 0, 1, 1, -1, 0.72), ("front", 1, 1, 2, 1, 0.72), ("ear right", 2, 1, 0, 1, 0.20), ("ear left", 3, 1, 0, -1, 0.20)]
SKIN_PAINT = 'paint/kairo-skin.png'      # the owner's painting over the layout (RGBA: alpha 0 keeps the bake)
PAINT_REPLACES = False         # True: the painting as it is; False: only its detail over the bake (as is, its tones met the photo in patches)

def skin_layout(P, F, centre, ears):
    """Chart per face and the layout position (px) of every face corner."""
    c = P[F].mean(1)
    n = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]]); n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
    n *= np.sign((n * (c - centre)).sum(1, keepdims=True) + 1e-12)
    chart = np.zeros(len(F), int)
    # Behind the centre, faces turned down go to the under chart only when they face nearly straight
    # down: the fold at the skull's base (down and back at 45 degrees) baked into the under chart as a
    # ring of grazing slivers a shade darker than the back chart, a jagged crease across the nape.
    w = np.where((n[:, 1] < 0) & (c[:, 2] < centre[2]), 0.6, 1.0)
    best = (np.abs(n) * np.stack([np.ones(len(F)), w, np.ones(len(F))], 1)).argmax(1)
    for k, (_, _, _, axis, sign, _) in enumerate(SKIN_CHARTS[:6]):
        chart[(best == axis) & (np.sign(n[np.arange(len(F)), axis]) == sign)] = k
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

def lattice(n):
    """Barycentric coordinates of a regular lattice with n steps along each edge."""
    i, j = np.meshgrid(np.arange(n + 1), np.arange(n + 1)); m = i + j <= n
    a, b = i[m] / n, j[m] / n
    return np.stack([1 - a - b, a, b], 1)

def skin_bake(P, F, chart, px, colour, shade, work, ear_colour=None, ear_weight=None, photo_weight=None, hair_weight=None, root_weight=None):
    """The skin's colours drawn into the layout: dense points on every face, the outermost along its
    chart's axis winning, the gaps filled from the nearest drawn texel. `ear_weight` (per vertex, 0..1)
    blends `colour` into `ear_colour`."""
    W, H = SKIN_TILE * 4, SKIN_TILE * 2
    img = np.zeros((H, W, 3)); depth = np.full((H, W), -np.inf); wimg = np.zeros((H, W)); himg = np.zeros((H, W))
    # Samples per face by its area in texels: the ear tiles are five times as fine as the side tiles,
    # and the same 163 random points on every face left texels there for the surface behind (the back
    # of the helix, the skull) to win, a salt-and-pepper speckle along the helix and the lobe.
    area = 0.5 * np.abs(np.cross(px[:, 1] - px[:, 0], px[:, 2] - px[:, 0]))
    bucket = np.digitize(area, [60, 250, 1000])
    for k, n in enumerate((16, 32, 64, 128)):
        bary = lattice(n)
        faces = np.where(bucket == k)[0]
        if not len(faces):
            continue
        for part in np.array_split(faces, max(1, len(faces) * len(bary) // 1500000)):
            pts = np.einsum('bk,fkd->fbd', bary, P[F[part]]).reshape(-1, 3)
            uv = np.einsum('bk,fkd->fbd', bary, px[part]).reshape(-1, 2)
            sh = np.einsum('bk,fk->fb', bary, shade[F[part]]).reshape(-1)
            ch = np.repeat(chart[part], len(bary))
            axis = np.array([c[3] for c in SKIN_CHARTS])[ch]; sign = np.array([c[4] for c in SKIN_CHARTS])[ch]
            dep = pts[np.arange(len(pts)), axis] * sign
            col = colour(pts, np.repeat(part, len(bary)))
            if ear_colour is not None:
                e = np.einsum('bk,fk->fb', bary, ear_weight[F[part]]).reshape(-1)[:, None]
                if root_weight is not None:
                    e = e * (1 - root_weight(pts))[:, None]      # the owner's roots behind and above the ear win over the ear's flat tone
                col = col * (1 - e) + ear_colour(pts) * e
            col = col * sh[:, None]
            x = np.clip(uv[:, 0].astype(int), 0, W - 1); y = np.clip(uv[:, 1].astype(int), 0, H - 1)
            o = np.argsort(dep)
            better = dep[o] > depth[y[o], x[o]]
            img[y[o][better], x[o][better]] = col[o][better]; depth[y[o][better], x[o][better]] = dep[o][better]
            if photo_weight is not None:
                wimg[y[o][better], x[o][better]] = np.repeat(photo_weight[part], len(bary))[o][better]
            if hair_weight is not None:
                himg[y[o][better], x[o][better]] = hair_weight(pts)[o][better]
    drawn = np.isfinite(depth)
    # Blue texels on the skin are hair the generator painted on the bald head (along the nape): they
    # take the nearest skin instead.
    # (Not the scalp under the hair layer, which the hair rule turned to the hair's own dark on purpose:
    # taken for strays, the whole scalp came back as skin with dark rings along the rule's fade, and
    # showed beige between the strands.)
    stray = drawn & (img[..., 2] > img[..., 0] - 5) & (himg < 0.5)
    dist, (iy, ix) = ndimage.distance_transform_edt(~drawn | stray, return_indices=True)
    # Near a chart its edge colours spread out (bilinear sampling at the triangles' edges); the rest
    # of the layout takes the skin's median. Filled from the nearest edge throughout, the texture's
    # coarse mip levels averaged every chart's border, and the pixels on a chart seam (where the
    # sampler jumps between tiles and picks the coarsest level) drew a light line across the nape.
    img = np.where((dist <= 8)[..., None], img[iy, ix], np.median(img[drawn & ~stray], 0))
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

def jpeg(img, quality=90, subsampling=None):
    out = io.BytesIO(); img.save(out, 'JPEG', quality=quality, **({} if subsampling is None else {'subsampling': subsampling})); return out.getvalue()

# ---------------------------------------------------------------- build

def build(work):
    mouth_setup(work)
    P, F, label, skin_colour, scan_colour, pivot, hair_weight = compose(work)
    # Bald, the face shows the bald picture (same face, same framing, no hair painted on it).
    # The head is the bald head whether or not the hair is on: the face shows the owner's bald picture
    # (same face, same framing, no hair painted on it) and nothing on it is filled, toned or cut for
    # the hair (owner: "huvudet ska inte ändras av att man lägger på hår"). The haired photo goes to
    # the hair layer alone.
    src = Image.open(f'{work}/bald/kairo-bald-front.png').convert('RGB')
    hsrc = Image.open(f'{work}/kairo-front.png').convert('RGB')
    P, F, grid_start, kept = face_surface(P, F, src, label == HAIR, hsrc)
    label = np.concatenate([label[kept], np.full(len(F) - kept.sum(), GRID)])
    shell_f = np.concatenate([SHELL[0][kept], np.zeros(len(F) - kept.sum(), bool)])
    is_skirt = np.zeros(len(F), bool); is_skirt[len(F) - 2 * SKIRT_EDGES[0]:] = True       # the grid's folded-back border, its own layer below
    hair_f = np.where((label == HAIR) & ~shell_f)[0]
    if len(hair_f):
        keep_h = loose_pieces(P, F[hair_f], P[np.unique(F[(label == SKIN) | (label == NECK) | shell_f])])
        drop_h = np.zeros(len(F), bool); drop_h[hair_f[~keep_h]] = True
        F, label, shell_f, is_skirt = F[~drop_h], label[~drop_h], shell_f[~drop_h], is_skirt[~drop_h]
    if PROJ_HAIR:
        # No hair behind the face grid within the face: the envelope's inner surface and its far side
        # lie there, hidden by the grid except through the eye openings, where they showed as
        # fragments over the irises (the head's voxel fill misses them: the bald scan's eyes are
        # hollows, so they are outside the "head"). The lock in front of the grid stays.
        hc_ = P[F].mean(1); hpx, hzc = project(hc_); hi_ = np.clip(hpx.astype(int), 0, N - 1)
        in_face = FACE_INFO[1][hi_[:, 1], hi_[:, 0]]
        # Within 6 cm only: the back of the head projects onto the face's pixels too, and the rule took
        # the hair there (a bald patch behind the ear on every turn).
        gz_ = FACE_INFO[0][hi_[:, 1], hi_[:, 0]]
        behind_grid = (label == HAIR) & in_face & (hzc > gz_ + HAIR_BEHIND_GRID) & (hzc < gz_ + HAIR_BEHIND_GRID_MAX)
        print('hair faces behind the face grid dropped:', int(behind_grid.sum()), 'of', int((label == HAIR).sum()))
        F, label, shell_f, is_skirt = F[~behind_grid], label[~behind_grid], shell_f[~behind_grid], is_skirt[~behind_grid]

    if PROJ_HAIR:
        n0 = len(F); F, label, shell_f = fill_small_holes(P, F, label, shell_f); is_skirt = np.concatenate([is_skirt, np.zeros(len(F) - n0, bool)])
    front, visible, zbuf = front_faces(P, F, work)
    # The face grid is the picture by construction: the visibility test missed scattered grid faces,
    # which then took the generator's colours as spots.
    on_grid = (F >= grid_start).all(1)
    front |= on_grid
    if PROJ_HAIR:
        # The grid's skirt is its own layer (hairline_skirt below): under the hair it drew the bald picture's
        # skin as triangles between the strands at the temples (owner: render the roots there instead).
        front &= ~is_skirt
    # Head faces turned to the camera take the photo even where the visibility test missed them: as
    # atlas faces (darkened by occlusion) their slivers at the grid's edge and along the hairline
    # showed as dark specks. Hair is wound either way, so it counts by angle alone.
    fn = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]]); fn /= np.linalg.norm(fn, axis=1, keepdims=True) + 1e-12
    toward = (fn * (np.array([0, 0, D]) - P[F].mean(1))).sum(1) / np.linalg.norm(np.array([0, 0, D]) - P[F].mean(1), axis=1)
    # Only where the camera actually saw the face (near the front depth): faces behind the jaw's
    # silhouette took the picture's background there, a grey strip between cheek and ear on a turn.
    fc_px, fc_z = project(P[F].mean(1)); fi = np.clip(fc_px.astype(int), 0, N - 1)
    seen = (fc_z <= zbuf[fi[:, 1], fi[:, 0]] + 0.02).astype(float)
    # Softened over the neighbouring faces (about 2 cm): face by face, the line between the photo and
    # the toned skin was ragged, a torn edge under the chin when the head tilted up.
    v_count = np.maximum(np.bincount(F.reshape(-1), minlength=len(P)), 1)
    for _ in range(6):
        seen = (np.bincount(F.reshape(-1), np.repeat(seen, 3), len(P)) / v_count)[F].mean(1)
    # Skin off the face grid is always atlas-textured, its colour blended from the picture by how
    # squarely the camera saw it (see bare_colour): switching from the picture to the generator's
    # colour at one angle left a visible edge down the side of the head.
    project_weight = smooth(0.05, 0.4, toward) * seen
    front &= ~(((label == SKIN) | (label == NECK)) & ~on_grid)
    if PROJ_HAIR:
        # Every hair face goes through the projector's charts (the front view is the photo at its own
        # resolution): as `front` faces they took the hairphoto material, which has no alpha, so the
        # mask never cut the hairline and the skirt over the forehead showed its voxel edge.
        front &= ~(label == HAIR)
    else:
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
    # Hair outside the face only: the hair rule (blue, or dark) also takes the brows, lashes and irises,
    # and the closing bridged the brow to the bangs above it, so the forehead between them was
    # "under hair" and the nearest-hair-pixel fill drew the brow's own pixels up into the hairline
    # (owner: "ögonbrynet sugs ut i håret"). The fill is the palette's dark, flat: the hair layer in
    # front carries the photo's strands, and a nearest-pixel fill streaks whatever it starts from.
    # (No hair colour on the face under the photo's hair any more: that fill is what drew the brow up
    # into the hairline. Gaps in the hair layer are the hair's to close.)
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
    hair_gain = gain(HAIR, scan_colour, hsrc)
    ao = occlusion(P, F, VN)
    # Smoothed over 5 mm: the scan's ears are noisy, and at full detail their occlusion speckled.
    # (Averaging only among vertices facing the same way, to keep the auricle's two sides apart,
    # left the concha in dark and light facets: the scan's ear is too crumpled for that.)
    near_v = cKDTree(P).query_ball_point(P, r=0.005)
    ao = np.array([ao[n_].mean() for n_ in near_v])
    ao_ear = np.array([ao[n_].mean() for n_ in near_v])        # once more for the auricles' atlas, whose triangles show every step
    shade = 0.75 + 0.25 * ao          # stronger, the hanging hair greyed the cheek beneath it
    rest = np.where(~front & (label == HAIR))[0]
    bare = np.where(~front & (label != HAIR) & (label < CORE) & ~is_skirt)[0]
    centre = np.array([pivot[0], P[grid_start:, 1].mean(), pivot[2]])
    ears, ear_w, auricle = find_ears(P, F, bare, centre)
    ear_bare = bare[auricle]
    if PROJ_HAIR and len(ear_bare):
        # The ears' pockets: hair within EAR_POCKET of an auricle, below its top EAR_TOP_SHARE, is cut (alpha, every
        # view) and dropped (below): strands of the views crossing the ear left dark bits in the concha and a
        # ragged rim round it (owner: "skräp i örat"). The hair over the ear's top stays as the views show it.
        apts = P[np.unique(F[ear_bare])]
        limit = np.zeros(len(apts)); outer = {}
        for ec, side in ears:
            mine = np.sign(apts[:, 0] - centre[0]) == side
            if mine.any():
                top, bottom = apts[mine, 1].max(), apts[mine, 1].min()
                limit[mine] = top - EAR_TOP_SHARE * (top - bottom)
                # The auricle's outer extent per 3 mm cell of (y, z), spread over 9 mm: hair over the ear's top
                # stays only outside it (hair lay through the helix, owner: "hår ska inte kunna penetrera örat").
                yz = apts[mine, 1:]; lo_e = yz.min(0) - 0.006; n_e = np.ceil((yz.max(0) + 0.006 - lo_e) / 0.003).astype(int) + 1
                grid = np.full(n_e, -np.inf); ie = ((yz - lo_e) / 0.003).astype(int)
                np.maximum.at(grid, (ie[:, 0], ie[:, 1]), (apts[mine, 0] - centre[0]) * side)
                outer[side] = (lo_e, n_e, ndimage.maximum_filter(grid, size=3))
        def ear_outer(q):
            o_ = np.full(len(q), -np.inf)
            for side, (lo_e, n_e, grid) in outer.items():
                mine = np.sign(q[:, 0] - centre[0]) == side
                ie = ((q[mine, 1:] - lo_e) / 0.003).astype(int); ok = (ie >= 0).all(1) & (ie < n_e).all(1)
                v_ = np.full(int(mine.sum()), -np.inf); v_[ok] = grid[ie[ok, 0], ie[ok, 1]]; o_[mine] = v_
            return o_
        def ear_cut(q):
            """True for points of the hair the ears forbid: within EAR_POCKET of an auricle and either below its
            top share or not outside its outer surface by EAR_FRONT_MARGIN."""
            d_, i_ = cKDTree(apts).query(q)
            return (d_ < EAR_POCKET) & ((q[:, 1] < limit[i_]) | (np.abs(q[:, 0] - centre[0]) < ear_outer(q) + EAR_FRONT_MARGIN))
        scan_colour.ears = (apts, limit)
        scan_colour.ear_cut = ear_cut
    # The hair's form is baked into its atlas, as the photo's light is into the front: the material is
    # nine tenths emissive, so the scene's light shapes nothing, and with occlusion alone the sides
    # were a flat mop. Normals smoothed over 1 cm (the scan's are crumpled) give the locks, not the
    # scan's wrinkles; the light comes from above and in front, as in the owner's reference views.
    hv = np.unique(F[label == HAIR])
    hn = VN[hv].copy(); htree = cKDTree(P[hv])
    for _ in range(2):
        nb = htree.query_ball_point(P[hv], r=0.01)
        hn = np.array([hn[i].mean(0) for i in nb]); hn /= np.linalg.norm(hn, axis=1, keepdims=True) + 1e-12
    hair_light = np.array([0.2, 0.75, 0.6]); hair_light /= np.linalg.norm(hair_light)
    lam = (np.abs(hn @ hair_light) + 0.5) / 1.5   # wrapped: the scan is wound either way, and the sides went dark with a plain lambert term
    hair_shade = shade.copy(); hair_shade[hv] = 1.0 if PROJ_HAIR else (0.6 + 0.4 * ao[hv]) * (HAIR_FORM[0] + HAIR_FORM[1] * lam)     # the views carry their light; the occlusion term darkened the front against the photo
    hair_lift = float(hair_gain @ np.array([0.299, 0.587, 0.114]))        # the palette keeps the hue; only the tone lifts to the photo's
    if PROJ_HAIR:
        rc_ = P[F[rest]].mean(1)
        skirt = scan_colour.hairline(rc_) if hasattr(scan_colour, 'hairline') else np.zeros(len(rc_), bool)
        print('hair skirt faces', int(skirt.sum()))
        rest_uv, rest_tex = hair_charts(P, F[rest], lambda q: scan_colour(q, hair_lift), hair_shade, VN, alpha_fn=getattr(scan_colour, 'alpha', None), skirt=skirt)
    else:
        rest_uv, rest_tex = atlas(P, F[rest], lambda q: scan_colour(q, hair_lift), 4096, hair_shade)
    # The turtleneck keeps a per-triangle atlas; only skin goes into the paintable layout (seen from
    # below, the shoulders covered the chin's underside there).
    knit_bare = bare[label[bare] == KNIT]; bare = bare[label[bare] != KNIT]
    # The body's core is dark only inside the turtleneck; above the collar a crack between the
    # twisting neck and the head showed it as a black sliver under the jaw.
    low = P[F].mean(1)[:, 1] < NECK_SPAN[1]
    dark = np.where((label == CORE_BODY) & low)[0]
    inner = np.where((label == CORE) | ((label == CORE_BODY) & ~low))[0]
    # Not behind the mouth: the inside of the mouth closes the view there, and the core, 1 cm in,
    # came through the parted lips at the corners.
    ic_px, ic_z = project(P[F[inner]].mean(1)); ic_s = (ic_px - np.array([OX, OY])) / S
    behind_mouth = (ic_s[:, 0] > MOUTH_TEX[0]) & (ic_s[:, 0] < MOUTH_TEX[2]) & (ic_s[:, 1] > MOUTH_TEX[1]) & (ic_s[:, 1] < MOUTH_TEX[3]) & (P[F[inner]].mean(1)[:, 2] > pivot[2])
    inner = inner[~behind_mouth]
    bare_kind = cKDTree(P[F[bare]].mean(1))
    chin_tree = cKDTree(P[grid_start:])
    # Skin seen only from the side keeps the generator's light and shade but takes the photo's skin
    # tone: its own colour drifted grey-beige there, a strip between cheek and ear on a turn.
    cheek = np.asarray(bald_src).astype(float)[open_skin]
    skin_ref = np.median(cheek, 0)
    # The skull under the face grid's edge zone (the bald picture's oval, within UNDER_GRID_DEPTH of the front):
    # it takes the picture's pixels whatever way its noisy faces turn. By facing alone the forehead's top under
    # the fringe was a patchwork of the picture and the one tone, and with the head pitched up that strip
    # showed between the grid's edge and the hair as a pink band (owner: "det rosa strecket över ögonbrynen").
    bald_oval = ndimage.binary_dilation(face_mask_src(src), iterations=6)
    def under_grid_px(q):
        cpx_, cz_ = project(q); spi_ = np.clip(((cpx_ - np.array([OX, OY])) / S).astype(int), 0, bald_oval.shape[0] - 1)
        return bald_oval[spi_[:, 1], spi_[:, 0]] & (cz_ < front_depth(cpx_) + UNDER_GRID_DEPTH)
    # The side views on the skin below the eyes: per view the faces turned to its camera and seen from
    # it (a coarse z-buffer in that view, softened over the neighbouring faces as the front's is),
    # sampled where the picture shows skin.
    fvn = VN[F].mean(1); fvn /= np.linalg.norm(fvn, axis=1, keepdims=True) + 1e-12
    fc = P[F].mean(1)
    fvn *= np.sign(((fc - np.array([pivot[0], fc[:, 1].mean(), pivot[2]])) * fvn).sum(1, keepdims=True) + 1e-12)
    v_count = np.maximum(np.bincount(F.reshape(-1), minlength=len(P)), 1)
    side_views = []
    for name, ang, sc, sox, soy in SIDE_VIEWS:
        R = rot_y(ang)
        pic = np.asarray(Image.open(f'{work}/assets-v3/{name}').convert('RGB')).astype(float)
        corner = np.median(np.concatenate([pic[:20, :20].reshape(-1, 3), pic[:20, -20:].reshape(-1, 3)]), 0)
        fg = ndimage.binary_closing(np.abs(pic - corner).max(-1) > 14, iterations=3)
        lab_, _ = ndimage.label(fg); sizes = np.bincount(lab_.ravel()); sizes[0] = 0
        fg = lab_ == sizes.argmax()
        # Skin only: the eye's white and iris and the brow printed on the temple beside the eye, and
        # the turtleneck's grey-blue on the neck above the scan's collar.
        skin_px = (pic[..., 0] > pic[..., 2] + 25) & (pic[..., 0] > pic[..., 1] + 8) & (pic.sum(-1) > 240) & (pic.sum(-1) < 740)
        fg &= ndimage.binary_erosion(skin_px, iterations=6)
        fg = ndimage.binary_erosion(fg, iterations=8).astype(float)
        # The view's detail as a ratio to its own local mean (1 = no detail), clamped: it multiplies
        # the skin's tone, so the views' light, their differences and the edge of each view vanish.
        blur = np.stack([ndimage.gaussian_filter(pic[..., k] * fg, DETAIL_SCALE) for k in range(3)], -1) / np.maximum(ndimage.gaussian_filter(fg, DETAIL_SCALE), 1e-3)[..., None]
        ratio = pic / np.maximum(blur, 1.0)
        # Only the grain: where the view has an edge (the ear's rim, a brow's end, the chin's
        # highlight) the ratio swings far from 1 and printed that edge on the skin; such places take
        # no detail. The grain itself is faint in these smooth renders (a few levels), so it is
        # amplified DETAIL_GAIN times.
        edge = ndimage.gaussian_filter(np.abs(ratio - 1).max(-1), 6) > 0.03
        edge = ndimage.binary_dilation(edge, iterations=6)
        pic = np.where(edge[..., None], 1.0, np.clip(1 + DETAIL_GAIN * (ratio - 1), 0.9, 1.1))
        Pr = P @ R.T
        at_, _ = front_zbuffer(Pr, F)
        cpx, cz = project(fc @ R.T)
        seen_ = (cz <= at_(cpx) + 0.02).astype(float)
        for _ in range(6):
            seen_ = (np.bincount(F.reshape(-1), np.repeat(seen_, 3), len(P)) / v_count)[F].mean(1)
        cam = R.T @ np.array([0.0, 0.0, D])
        tow = (fvn * (cam - fc)).sum(1) / np.linalg.norm(cam - fc, axis=1)
        side_views.append(dict(name=name, R=R, scale=sc, off=np.array([sox, soy]), pic=pic, fg=fg, weight=smooth(0.1, 0.45, tow) * seen_))
    def side_sample(view, q):
        px = project(q @ view['R'].T)[0] * view['scale'] + view['off']
        col = np.stack([ndimage.map_coordinates(view['pic'][..., k], [px[:, 1], px[:, 0]], order=1, mode='nearest', cval=1.0) for k in range(3)], -1)
        return col, ndimage.map_coordinates(view['fg'], [px[:, 1], px[:, 0]], order=1, mode='constant')
    epx = src_to_crop([(626.0, IRISES[0][1])]); ei = epx.astype(int)
    eye_y = unproject(epx, zbuf[ei[:, 1], ei[:, 0]])[0, 1]
    # The owner's scalp views: the skull's roots and their outline from their pictures (the root cap takes them too).
    scalp = scalp_views(work, P, F[bare], pivot) if os.path.isdir(f'{work}/{SCALP_DIR}') else None
    scalp_cap_colour = None
    if scalp is not None:
        base_weight = hair_weight
        def hair_weight(points, base_weight=base_weight):
            _, root_, tot_ = scalp(points); s_ = smooth(SCALP_SEEN[0], SCALP_SEEN[1], tot_)
            return root_ * s_ + base_weight(points) * (1 - s_)
        style_ = getattr(scan_colour, 'style', scan_colour)
        def scalp_colour(points, base=SCALP_BASE):
            col_, _, tot_ = scalp(points); s_ = smooth(SCALP_SEEN[0], SCALP_SEEN[1], tot_)[:, None]
            return col_ * s_ + (scan_colour(points, base=base) if base == SCALP_BASE else style_(points, base=base)) * (1 - s_)
        skin_colour.override['weight'] = hair_weight
        skin_colour.override['colour'] = scalp_colour
        scalp_cap_colour = lambda q: scalp_colour(q, base=ROOT_BASE)
        def scalp_root_weight(points):
            _, root_, tot_ = scalp(points)
            return root_ * smooth(SCALP_SEEN[0], SCALP_SEEN[1], tot_)
    else:
        scalp_root_weight = None
    def side_weight(view, q, near):
        # Below the eyes, and not on or round the ears (their own atlas; a picture's ear a few px off
        # the scan's would print the concha on the skull behind).
        return (view['weight'][bare][near] * smooth(eye_y + 0.04, eye_y, q[:, 1]) * (1 - ear_w[F[bare][near]].max(1)))
    picture = np.asarray(bald_src).astype(float)
    def projected(q):
        c, _ = project(q); sp = (c - np.array([OX, OY])) / S
        return np.stack([ndimage.map_coordinates(picture[..., k], [sp[:, 1], sp[:, 0]], order=1) for k in range(3)], -1)
    c_sx = lambda q: (project(q)[0][:, 0] - OX) / S
    c_sy = lambda q: (project(q)[0][:, 1] - OY) / S
    bare_down = -np.cross(P[F[bare, 1]] - P[F[bare, 0]], P[F[bare, 2]] - P[F[bare, 0]])
    bare_down = bare_down[:, 1] / (np.linalg.norm(bare_down, axis=1) + 1e-12)
    bare_down = np.abs(bare_down) * (VN[F[bare]].mean(1)[:, 1] < 0)     # winding is mixed: the smoothed normals give the sign
    def bare_colour(q, faces=None):
        # `faces`: each point's face among `bare` when the caller knows it (the bake does; looking it
        # up for 15 million points took most of the build).
        near = bare_kind.query(q)[1] if faces is None else faces
        kind = label[bare][near]
        col = skin_colour(q, bald_gain[kind])
        # One tone everywhere off the photo, nine tenths of the cheek (which faces the light): form
        # comes from the baked occlusion and the scene's light. The generator's own light (a clip
        # round that tone; its side light and the occlusion under the hanging hair made the side of
        # the face a patchwork, the back of the skull met the neck in a step), a shadow on down-facing
        # skin in front and the photo's neck tone under the chin each drew a band with an edge: dark
        # under the jaw against the neck's side when the head tilts up, a step at the skull's base.
        toned = np.broadcast_to(skin_ref[None] * 0.90, col.shape).copy()
        # Not the scalp under the hair (already turned to the hair's colour): toned to skin, it showed
        # through the bangs at the temple as beige shards. Bald, all of it is skin: the generator's
        # texture is greyish behind the ears and down the nape, and kept as it was it drew a dark
        # streak behind each ear.
        is_skin = (((kind == SKIN) | (kind == NECK)) & (BALD | (col[:, 0] > col[:, 2] + 10)))[:, None]
        # None of the generator's own colour on the skin: its 15 % share put grey-pink specks and a
        # darker band across the nape at the skull's base, where its bald texture has hair.
        side = np.where(is_skin, toned, col)
        # The photo only down to the jaw line, and nothing of the chin's shadow below it: the photo's
        # shadowed throat drew a dark stripe down the middle of the neck, and a 6-30 px fade below the
        # line kept the shadow's edge as a brown hook on the side of the throat.
        jaw_y = CHIN_Y - 0.0042 * (c_sx(q) - 626.0) ** 2
        # Nor on skin turned down (the chin's underside within the camera's depth tolerance took the
        # lit chin's edge: a light bib under the chin whenever the head tilted up).
        w = (project_weight[bare][near] * ((kind == SKIN) | (kind == NECK)) * smooth(jaw_y + 6, jaw_y - 8, c_sy(q))
             * smooth(0.6, 0.3, bare_down[near]))[:, None]
        w = np.maximum(w, (under_grid_px(q) & (kind == SKIN) & (c_sy(q) < jaw_y - 8))[:, None].astype(float))
        if scalp_root_weight is not None:
            # Not where the owner's scalp views show roots: at the temple the skull under the oval's edge took the bald
            # picture's skin over the roots (a pale patch inside the hairline).
            w = w * (1 - scalp_root_weight(q))[:, None]
        pc = projected(q)
        # The views' detail, averaged by their weights, on the skin's tone (1 where no view sees).
        total, detail = np.zeros((len(q), 1)), np.zeros((len(q), 3))
        on_skin = ((kind == SKIN) | (kind == NECK))
        for view in side_views:
            sc_, fg_ = side_sample(view, q)
            wi = (side_weight(view, q, near) * fg_ * on_skin)[:, None]
            total = total + wi; detail = detail + wi * sc_
        detail = (detail + np.maximum(1 - total, 0)) / np.maximum(total, 1.0)
        out = side * detail * (1 - w) + pc * w
        if UNDER_CHIN_PHOTO == 0:
            return out
        # Under the chin and jaw: the picture's own shadow there (what the camera sees below the jaw
        # line), so the chin's underside matches the neck beneath it and the chin reads as one piece.
        c_, _ = project(q); sx = (c_[:, 0] - OX) / S; sy = (c_[:, 1] - OY) / S
        jaw_y = CHIN_Y - 0.0042 * (sx - 626.0) ** 2
        below = (sy > jaw_y - 4) & (np.abs(sx - 626) < 190)
        on_skin = pc[:, 0] > pc[:, 2] + 25          # never the backdrop beside the jaw (a pale patch on a turn)
        wc = UNDER_CHIN_PHOTO * (smooth(0.04, 0.01, chin_tree.query(q)[0]) * below * on_skin * ((kind == SKIN) | (kind == NECK)))[:, None]
        return out * (1 - wc) + pc * wc
    # The skin's own layout (paintable), see SKIN_CHARTS.
    ears, ear_w, auricle = find_ears(P, F, bare, centre)     # again on the skin faces left after the knit split
    bc_ = P[F[bare]].mean(1)
    ear_bare = bare[auricle]
    bare, bc_, bare_down = bare[~auricle], bc_[~auricle], bare_down[~auricle]
    bare_kind = cKDTree(P[F[bare]].mean(1))
    chart, corner_px = skin_layout(P, F[bare], centre, ears)
    # Faces that fall outside their chart's window (the neck deep inside the collar, never seen) go
    # with the turtleneck's atlas instead of spilling into the next chart.
    tile = np.array([[c[1], c[2]] for c in SKIN_CHARTS])[chart] * SKIN_TILE
    inside = ((corner_px >= tile[:, None, :] + 2) & (corner_px < tile[:, None, :] + SKIN_TILE - 2)).all((1, 2))
    knit_bare = np.concatenate([knit_bare, bare[~inside]]); bare, chart, corner_px = bare[inside], chart[inside], corner_px[inside]
    bc_ = bc_[inside]
    bare_kind = cKDTree(P[F[bare]].mean(1))
    bare_down = bare_down[inside]
    knit_uv, knit_tex = atlas(P, F[knit_bare], lambda q: skin_colour(q, bald_gain[KNIT]), 2048, 0.6 + 0.4 * shade)
    # Ears: the tone of the skin round them, flat, their form from the baked occlusion alone (the
    # generator's ear texture was blotchy and the painting did not fit the scan's ear; a pinker tint,
    # and the jaw's toning and the photo on the lobe, made the ear two-coloured against the temple).
    ring = np.zeros(len(bare), bool)
    for ec, side in ears:
        d_ = bc_ - ec
        ring |= (chart < 6) & (ear_w[F[bare]].max(1) < 0.01) & (np.abs(d_[:, 1]) < 0.12) & (np.abs(d_[:, 2]) < 0.10) & ((d_[:, 0] * side) > -0.06)
    ring_col = bare_colour(bc_[ring], np.where(ring)[0])
    # Skin only: with the owner's scalp views the skull behind the ears is roots, and their median made the ears indigo.
    ring_skin = ring_col[:, 0] > ring_col[:, 2] + 15
    ear_tint = np.median(ring_col[ring_skin], 0) if ring_skin.sum() >= 50 else skin_ref * 0.9
    print('ear tone from', int(ring.sum()), 'faces round the ears:', np.round(ear_tint), 'cheek', np.round(skin_ref))
    skull_near_ear = cKDTree(P[F[np.setdiff1d(bare[label[bare] == SKIN], ear_bare)]].mean(1))
    def ear_colour(q):
        # The skull under and behind the helix is in the auricle's atlas (it overlaps the helix in the side projection)
        # and took the ear's flat tone; where the owner's scalp views show roots it is roots (one flat tone: a per-triangle
        # atlas carries no strands). Only at the skull's surface (within 4 mm of a skull face): the helix's rim, seen by
        # the back view against the roots behind it, took a dark fringe.
        tint = np.broadcast_to(ear_tint, (len(q), 3)).copy()
        if scalp_root_weight is None:
            return tint
        col_, root_, tot_ = scalp(q)
        w = (root_ * smooth(SCALP_SEEN[0], SCALP_SEEN[1], tot_) * smooth(0.004, 0.002, skull_near_ear.query(q)[0]))[:, None]
        return tint * (1 - w) + col_ * w
    ear_uv, ear_tex = atlas(P, F[ear_bare], ear_colour, 1024, 0.5 + 0.5 * ao_ear)
    # The skull's skin round the ear keeps the skin's mild shading: with the auricle's stronger
    # occlusion it showed dark facets behind the ear (the scan is coarse and noisy there).
    # The occlusion carries the skin's form now that its tone is flat (ears as before, their tone
    # was set with the lighter shade): the hollow under the jaw, the nape under the skull.
    skin_shade = (0.6 + 0.4 * shade) * ear_w + (0.4 + 0.6 * shade) * (1 - ear_w)
    skin_tex, drawn = skin_bake(P, F[bare], chart, corner_px, bare_colour, skin_shade, work,
                                ear_colour=lambda q: np.broadcast_to(ear_tint, (len(q), 3)).copy(), ear_weight=ear_w, photo_weight=project_weight[bare], hair_weight=hair_weight, root_weight=scalp_root_weight)
    skin_template(skin_tex, drawn, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'obj', 'kairo-skin-template.png'), corner_px)
    skin_uv = corner_px / np.array([SKIN_TILE * 4, SKIN_TILE * 2])
    print('front faces', front.sum(), 'hair', len(rest), 'bald', len(bare))
    cap = root_cap(P, F, np.setdiff1d(bare[label[bare] == SKIN], ear_bare), hair_weight, VN, shade, scan_colour, ear_w, pivot, colour_fn=scalp_cap_colour) if ROOT_CAP and len(rest) else None
    skirt = hairline_skirt(P, F, is_skirt, VN, scan_colour, projected) if PROJ_HAIR and HAIRLINE_SKIRT and is_skirt.any() else None
    strands = None
    if scalp is not None:
        grid_faces = np.where(front)[0]; skull_faces = bare[label[bare] == SKIN]
        cand = np.concatenate([grid_faces, skull_faces]); cand_grid = np.r_[np.ones(len(grid_faces), bool), np.zeros(len(skull_faces), bool)]
        eyes_px = src_to_crop([(e['cx'], e['cy']) for e in EYES]); eyes_i = np.clip(eyes_px.astype(int), 0, N - 1)
        eyes3d = unproject(eyes_px, zbuf[eyes_i[:, 1], eyes_i[:, 0]])
        strands = hairline_strands(P, F, cand, VN, scalp, pivot, eyes3d, cand_grid)
    cards = None
    if PROJ_HAIR and HAIR_CARDS and hasattr(scan_colour, 'views'):
        import haircards
        cards = haircards.build_cards(scan_colour.views, scan_colour.from_world, getattr(scan_colour, 'ear_cut', None), scan_colour.normals)
        if cards is not None:
            cards['material'] = 0; cards['material_name'] = 'cards'

    # The surface seen from the camera, as depth over image position: targets slide points along it.
    vis_px, vis_z = inp[visible], zc[visible]
    depth = LinearNDInterpolator(vis_px, vis_z)
    # Where the head meets the skinned neck (which has no morphs) the mouth's shapes fade out: the
    # jaw's hinge reaches far back under the chin, and the head's side of that seam moving alone left
    # small flaps at the throat.
    neck_seam = cKDTree(P[np.unique(F[label == NECK])])
    def lift(points, vis, side=None):
        """Morph deltas for points: each field's image-space shift, back onto the surface. Every point
        near the visible surface moves, seen or not: moving only the visible ones tore the eyes where
        a lid edge hides the vertices just under it. `side` tells the lips' cut edges apart."""
        px, z0 = project(points)
        sxy = (px - np.array([OX, OY])) / S
        surface = depth(px)
        behind = z0 - np.nan_to_num(surface, nan=0)
        hinge = smooth(790.0, 830.0, sxy[:, 1])          # under the chin (see jaw_fade)
        near = (~np.isnan(surface)) & (behind < 0.035 + 0.065 * hinge)
        # The mouth's shapes fade with depth behind the face: the jaw's underside moves with the chin
        # at the front and not at all where it meets the neck (whose part has no morphs). Under the
        # chin over the underside's whole length, as a jaw hinges: fading within 3.5 cm, the dropped
        # chin left a kink where the underside stopped following.
        jaw_fade = 1 - smooth(0.012, 0.035, behind)
        jaw_fade = jaw_fade * (1 - hinge) + (1 - smooth(0.02, 0.10, behind)) * hinge
        jaw_fade = jaw_fade * smooth(0.0, 0.02, neck_seam.query(points)[0])
        out = []
        for name in TARGETS:
            dxy = field(sxy, name, side) * near[:, None]
            if name in MOUTH_TARGETS:
                dxy = dxy * jaw_fade[:, None]
            dz = slit_depth(sxy, name, np.zeros(len(sxy)) if side is None else side) * near if name in SLIT_TARGETS else np.zeros(len(sxy))
            moved = np.where((np.abs(dxy).sum(1) > 1e-4) | (dz > 1e-7))[0]
            delta = np.zeros_like(points)
            if len(moved):
                npx = (sxy[moved] + dxy[moved]) * S + np.array([OX, OY])
                # Each point keeps its own depth and slides in the image plane: re-sampling the depth
                # at the new position threw vertices across the jump between lashes and eyeball.
                # (The lips' inner edges alone go back as they part, see slit_depth.)
                delta[moved] = unproject(npx, z0[moved] + dz[moved]) - points[moved]
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
    # The ear's pocket: shell faces over the auricle go (the shell's wider closing wrapped the ear).
    if len(ear_bare):
        d_ear, i_ear = cKDTree(P[np.unique(F[ear_bare])]).query(P[F].mean(1))
        pocket = shell_f & (d_ear < EAR_POCKET)
        if PROJ_HAIR and hasattr(scan_colour, 'ear_cut'):
            pocket |= (label == HAIR) & scan_colour.ear_cut(P[F].mean(1))
    else:
        pocket = np.zeros(len(F), bool)
    print('ear pocket: shell faces dropped', int(pocket.sum()))
    if PROJ_HAIR and HAIR_HEAD_GAP > 0:
        rng_h = np.random.default_rng(1)
        def surface_pts(faces):
            return np.concatenate([np.einsum('k,fkd->fd', b_, P[F[faces]]) for b_ in rng_h.dirichlet((1, 1, 1), size=6)] + [P[np.unique(F[faces])]])
        against = (label == HAIR) & ((cKDTree(surface_pts(np.where((label == SKIN) | (label == NECK))[0])).query(centres)[0] < HAIR_HEAD_GAP)
                                     | (cKDTree(surface_pts(np.where(front)[0])).query(centres)[0] < HAIR_GRID_GAP))
        print('hair faces against the head dropped:', int(against[rest].sum()), 'of', len(rest))
        pocket = pocket | against
    keep_rest = ~pocket[rest]; rest, rest_uv = rest[keep_rest], rest_uv[keep_rest]
    if PROJ_HAIR and len(rest):
        # Only the piece joined to the main hair stays (owner's rule: no loose hair pieces ever; 2026-10-15 the
        # envelope's inner shell, 2 914 faces hidden inside the hair but for specks at the parting and the nape,
        # and the scraps the pockets and the head gap leave).
        from scipy.sparse import coo_matrix
        from scipy.sparse.csgraph import connected_components
        Fr = F[rest]; e_r = np.concatenate([Fr[:, [0, 1]], Fr[:, [1, 2]], Fr[:, [2, 0]]])
        nc_r, lab_r = connected_components(coo_matrix((np.ones(len(e_r)), (e_r[:, 0], e_r[:, 1])), shape=(len(P), len(P))), directed=False)
        comp_r = lab_r[Fr[:, 0]]; size_r = np.bincount(comp_r, minlength=nc_r)
        scrap = comp_r != np.argmax(size_r)
        print('hair pieces off the main hair dropped:', int(scrap.sum()), 'faces in', int((size_r > 0).sum()) - 1, 'pieces, the largest', sorted(size_r[size_r > 0].tolist(), reverse=True)[1:6])
        rest, rest_uv = rest[~scrap], rest_uv[~scrap]
    front = front & ~pocket
    photo, drawn = front & from_photo, front & ~from_photo
    groups = [(F[photo], fpos, fuv, fnrm, fvis, label[photo], overlap[photo], 7, True),
              (F[drawn], fpos, fuv, fnrm, fvis, label[drawn], overlap[drawn], 4, True),
              (*own(rest, rest_uv), 1, True),
              (*own(bare, skin_uv), 2, True),
              (*own(ear_bare, ear_uv), EAR_MATERIAL, True),
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
                w = twist(p)
                # Along the chain body - N1 - N2 - head: each vertex between the two joints at its height.
                s_ = np.clip(w * 3, 0, 3 - 1e-6); k0 = np.floor(s_).astype(int); f_ = s_ - k0
                prim['joints'] = np.stack([k0, k0 + 1, np.zeros_like(k0), np.zeros_like(k0)], 1)
                prim['weights'] = np.stack([1 - f_, f_, np.zeros_like(f_), np.zeros_like(f_)], 1)
            if part == 'head':
                prim['targets'] = lift(pos[used], vis[used], SIDE[0][used] if pos is fpos else None) if morph else [np.zeros_like(p) for _ in TARGETS]
            prims[part].append(prim)

    for extra in (cap, skirt, cards, strands):
        if extra is not None:
            extra['targets'] = lift(extra['pos'], None)
            extra['pos'] = extra['pos'] - pivot
            prims['head'].append(extra)
    cap_tex = cap.pop('tex') if cap is not None else None
    skirt_tex = skirt.pop('tex') if skirt is not None else None
    cards_tex = cards.pop('tex') if cards is not None else None
    strands_tex = strands.pop('tex') if strands is not None else None
    front_vertices = np.unique(F[front])
    ntree = cKDTree(P[front_vertices])
    def surface_normal(points):
        _, i = ntree.query(points, k=6)
        n = VN[front_vertices][i].mean(1)
        return n / (np.linalg.norm(n, axis=1, keepdims=True) + 1e-12)
    eyes = eye_parts(FACE_INFO[2], surface_normal) + [eye_sockets()]
    lid, lid_tex = lid_sheets(work, FACE_INFO[2], src, surface_normal)
    mouth = mouth_inside(FACE_INFO[2])
    mouth_tex, lipwall_tex, oshade_tex = mouth_textures(work)
    for part in eyes + mouth:
        part['pos'] = part['pos'] - pivot
        prims['head'].append(part)
    lid['pos'] = lid['pos'] - pivot
    closed = Image.open(f'{work}/assets-v2/kairo-eyes-closed-front.png').convert('RGB')

    g = Glb()
    g.gltf["samplers"].append({"magFilter": 9729, "minFilter": 9987, "wrapS": 33071, "wrapT": 33071})
    # No mipmaps for the ears' per-triangle atlas: minified, its levels average unrelated triangles
    # into grain. Linear sampling reads each pixel's own triangle instead.
    g.gltf["samplers"].append({"magFilter": 9729, "minFilter": 9729, "wrapS": 33071, "wrapT": 33071})
    for name, img in ((("front", sclera_photo(src)), ("rest", Image.fromarray(rest_tex, 'RGBA' if rest_tex.shape[-1] == 4 else 'RGB')), ("skin", Image.fromarray(skin_tex)),
                      ("lidskin", lid_texture(closed, src)), ("bald", bald_src), ("iris", iris_texture(src)),
                      ("core", Image.new('RGB', (4, 4), (8, 6, 8))), ("hairphoto", hair_photo(hsrc)),
                      # Inside the head the core is shadowed skin: through the scan's gaps (round the eye
                      # sockets, behind the face's edge) black read as holes in the cheek.
                      ("coreskin", Image.new('RGB', (4, 4), tuple(int(c * 0.8) for c in skin_ref))), ("knit", Image.fromarray(knit_tex)),
                      ("eyeball", eyeball_texture(src)),
                      ("socket", Image.fromarray(np.tile((skin_ref[None, :] * np.array([1.0, 0.86, 0.86]) * np.linspace(0.95, 0.75, 64)[:, None])[:, None, :], (1, 4, 1)).astype(np.uint8))),
                      ("lid", lid_tex), ("mouth", mouth_tex), ("lipwall", lipwall_tex), ("oshade", oshade_tex), ("ear", Image.fromarray(ear_tex)))
                     + ((("roots", Image.fromarray(cap_tex, 'RGBA')),) if cap is not None else ())
                     + ((("hairline", Image.fromarray(skirt_tex, 'RGBA')),) if skirt is not None else ())
                     + ((("cards", Image.fromarray(cards_tex, 'RGBA')),) if cards is not None else ())
                     + ((("strands", Image.fromarray(strands_tex, 'RGBA')),) if strands is not None else ())):
        if img.mode == 'RGBA' or name == "ear":        # the ear atlas lossless: JPEG rings at its cells' hard edges
            data = io.BytesIO(); img.save(data, 'PNG'); i = g.image(data.getvalue(), "image/png", name)
        else:
            i = g.image(jpeg(img, 92 if name in ("front", "bald", "rest") else 88, 0 if name == "rest" else None), "image/jpeg", name)
        # No mipmaps for the ear's per-triangle atlas (minified, its levels averaged the black between
        # cells into every triangle's edge). The hair atlas has its gutters filled and keeps mipmaps:
        # without them the minified strands aliased into triangle-sized grain.
        g.gltf["textures"].append({"sampler": 1 if name == "ear" else 0, "source": i})
        material = {"name": name, "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "baseColorTexture": {"index": i},
                                                           "metallicFactor": 0, "roughnessFactor": 0.62}}
        # The scan's winding is unreliable (thin hair sheets, the collar seen from inside): culled back
        # faces were the holes in the neck, the collar and the turtleneck's spots.
        material["doubleSided"] = True
        # The skin atlas too: lit by the scene while the face beside it showed the picture's own light,
        # the skull and the sides came out a different tone, an edge round the face.
        # The projected hair carries the views' own light, like the front: emissive too. (Lit, with its
        # roughness 0.62, the envelope's grooves caught the scene's speculars as glitter: "cut glass".)
        if name in ("front", "lidskin", "bald", "iris", "hairphoto", "skin", "coreskin", "knit", "eyeball", "socket", "lid", "mouth", "lipwall", "oshade", "ear", "roots", "hairline", "cards", "strands") or (name == "rest" and PROJ_HAIR):
            # The picture already carries its light: shown mostly as it is (emissive), with a little
            # shading on top for depth when the head turns. Fully lit, the scan's uneven normals
            # mottled the face and the turtleneck.
            material["pbrMetallicRoughness"]["baseColorFactor"] = [0.1, 0.1, 0.1, 1]
            material["emissiveTexture"] = {"index": i}
            material["emissiveFactor"] = [0.9, 0.9, 0.9]
            material["pbrMetallicRoughness"]["roughnessFactor"] = 1.0       # no highlights on the baked light
        if name in ("core", "coreskin"):
            material["pbrMetallicRoughness"]["roughnessFactor"] = 1.0
        if name in ("lidskin", "iris", "lid", "strands"):
            material["alphaMode"] = "BLEND"
        if name in ("rest", "roots", "hairline", "cards") and img.mode == 'RGBA':
            material["alphaMode"] = "MASK"; material["alphaCutoff"] = 0.5
        g.gltf["materials"].append(material)
    names_ = [m_['name'] for m_ in g.gltf['materials']]
    for p_ in prims['head']:
        if 'material_name' in p_:
            p_['material'] = names_.index(p_.pop('material_name'))
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
    # 0 Root, 1 Body, 2 HeadPivot, 3 Head, 4.. the blink frames (shown by blinkFrames), then the neck
    # and its joints, then the lids (their own mesh: the blink's chain of targets stays off the head's
    # hundred thousand vertices).
    lids_node = neck_node + 3
    g.node("HeadPivot", t=pivot - j2, children=[3, lids_node] + [4 + k for k in range(len(frames))])
    # Floating scraps of the scan go: a sliver a few millimetres off the temple past her left eye's outer
    # corner showed as a speck beside the eye from the front and outside the head from behind.
    prims['head'] = [drop_floating(p_) if p_['material'] in (2, EAR_MATERIAL, ROOT_MATERIAL) else p_ for p_ in prims['head']]
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
    m_lids = g.mesh("Lids", [lid], LID_TARGETS)
    assert g.node("Lids", m_lids) == lids_node
    clips(g)
    return g.bytes(), dict(m_head=m_head, head_prims=len(prims['head']), neck_chain=(n1_node, n2_node), m_lids=m_lids, lids_node=lids_node)

def fill_small_holes(P, F, label, shell_f, max_edges=8):
    """Boundary loops of up to `max_edges` edges in the hair layer, fan-triangulated: the drops above (faces
    inside the head, behind the face grid, loose pieces) and the envelope's own decimation leave single
    missing triangles in a closed surface, each a hole showing the skin behind. Larger openings (the
    face, the ears, the nape's lower edge) stay as they are."""
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    hair = np.where(label == HAIR)[0]
    if not len(hair):
        return F, label, shell_f
    Fh = F[hair]
    e = np.sort(np.concatenate([Fh[:, [0, 1]], Fh[:, [1, 2]], Fh[:, [2, 0]]]), 1)
    key, idx_, cnt = np.unique(e[:, 0].astype(np.int64) * len(P) + e[:, 1], return_index=True, return_counts=True)
    be = e[idx_[cnt == 1]]
    if not len(be):
        return F, label, shell_f
    nc, lab_ = connected_components(coo_matrix((np.ones(len(be)), (be[:, 0], be[:, 1])), shape=(len(P), len(P))), directed=False)
    loop = lab_[be[:, 0]]; nedge = np.bincount(loop, minlength=nc)
    # orientation from the faces: a boundary edge (a, b) as its face winds it, so the fan winds the same way
    wound = {}
    for f in Fh:
        for a, b in ((f[0], f[1]), (f[1], f[2]), (f[2], f[0])):
            wound[(min(a, b), max(a, b))] = (a, b)
    new = []
    for c in np.where((nedge >= 3) & (nedge <= max_edges))[0]:
        es = [wound[(int(a), int(b))] for a, b in be[loop == c]]
        nxt = {a: b for a, b in es}
        if len(nxt) != len(es):
            continue
        start = es[0][0]; ring = [start]
        while len(ring) <= len(es):
            n_ = nxt.get(ring[-1])
            if n_ is None or n_ == start:
                break
            ring.append(n_)
        if len(ring) != len(es):
            continue
        # the hole's winding is the faces' around it; the fan fills it with the opposite winding (a hole's rim runs against its faces)
        for k in range(1, len(ring) - 1):
            new.append((ring[0], ring[k + 1], ring[k]))
    if not new:
        print('hair holes: none to fill'); return F, label, shell_f
    new = np.array(new, np.int32)
    print('hair holes filled:', len(new), 'triangles in', int(((nedge >= 3) & (nedge <= max_edges)).sum()), 'loops of up to', max_edges, 'edges')
    return np.concatenate([F, new]), np.concatenate([label, np.full(len(new), HAIR)]), np.concatenate([shell_f, np.zeros(len(new), bool)])

def find_ears(P, F, bare, centre):
    """The auricles among the skull's bare faces: per side the ear's window about the point standing out most
    sideways, and in it the faces that overlap another in the side projection or stand well out from the
    skull. Returns (ears [(centre, side)], ear_w per vertex (the window), auricle mask over `bare`)."""
    sk = P[F[bare]].reshape(-1, 3)
    ears = []
    ear_w = np.zeros(len(P))
    auricle = np.zeros(len(bare), bool)
    bc_ = P[F[bare]].mean(1)
    for side in (1, -1):
        band = sk[(np.abs(sk[:, 1] - centre[1]) < 0.08)]
        rim = band[np.argmax((band[:, 0] - centre[0]) * side)]
        # The whole ear, lobe included: what stands out sideways from the skull, per height, inside
        # the ear's window. The skull's side is measured behind the ear (toward the nape) and in front
        # of it (the jaw) and interpolated between. A disc round the helix's outermost point caught the
        # upper ear only; the lobe and the lower concha went to the side and under charts, toned like
        # the jaw and the photo, and the ear showed in two colours. The ear's colour rule covers the
        # whole window, skull skin included (the concha's floor and the tragus lie at the skull's
        # surface and showed the generator's grey there): its tone is that skin's own. The auricle
        # gets a per-triangle atlas of its own: every face of the window that overlaps another in the
        # side projection (the helix over the concha, the auricle over the skull behind it, the two
        # sides of its shell) or stands well out from the skull. Standing out alone, with the skull
        # measured behind and in front of the ear, took the parietal bulge above the ear as well.
        dy, dz = bc_[:, 1] - rim[1], bc_[:, 2] - rim[2]
        xo = (bc_[:, 0] - centre[0]) * side
        bands = np.arange(-0.13, 0.10, 0.01) + 0.005
        def profile(zlo, zhi):
            prof = np.full(len(bands), np.nan)
            for k_, y0 in enumerate(bands):
                m = (np.abs(dy - y0) < 0.01) & (dz > zlo) & (dz < zhi)
                if m.sum() > 3:
                    prof[k_] = np.percentile(xo[m], 95)
            good = ~np.isnan(prof)
            prof = np.interp(bands, bands[good], prof[good])
            return ndimage.gaussian_filter1d(prof, 1.0)
        behind, front_ = profile(-0.06, -0.03), profile(0.06, 0.09)
        t = np.clip((dz + 0.045) / 0.12, 0, 1)
        skull = np.interp(dy, bands, behind) * (1 - t) + np.interp(dy, bands, front_) * t
        window = smooth(-0.13, -0.11, dy) * smooth(0.085, 0.065, dy) * smooth(-0.035, -0.02, dz) * smooth(0.08, 0.065, dz) * smooth(0.12, 0.14, xo)
        np.maximum.at(ear_w, F[bare].reshape(-1), np.repeat(window, 3))
        wf = np.where(window > 0.3)[0]
        pts = np.einsum('bk,fkd->fbd', lattice(6), P[F[bare[wf]]])
        yz = pts[..., 1:].reshape(-1, 2); xs = (pts[..., 0].reshape(-1) - centre[0]) * side
        cell = np.floor((yz - yz.min(0)) / 0.0015).astype(int); key = cell[:, 0] * 100000 + cell[:, 1]
        fid = np.repeat(wf, pts.shape[1])
        o_ = np.argsort(key, kind='stable'); key, xs, fid = key[o_], xs[o_], fid[o_]
        starts = np.r_[0, np.where(np.diff(key))[0] + 1]
        span = np.maximum.reduceat(xs, starts) - np.minimum.reduceat(xs, starts)
        cid = np.repeat(np.arange(len(starts)), np.diff(np.r_[starts, len(key)]))
        overlapped = np.zeros(len(bare), bool); overlapped[fid[span[cid] > 0.003]] = True
        stands = overlapped | (xo - skull > 0.008)
        stands = (window > 0.5) & (stands | vote(P, F[bare], stands, r=0.006))   # closed: the helix's top overlaps nothing of its own
        auricle |= stands
        lo_, hi_ = bc_[stands].min(0), bc_[stands].max(0)
        ears.append((np.array([rim[0] - side * 0.02, (lo_[1] + hi_[1]) / 2, (lo_[2] + hi_[2]) / 2]), side))
        print(f'ear {"+x" if side > 0 else "-x"}: {int(stands.sum())} auricle faces, y {lo_[1] - rim[1]:+.3f}..{hi_[1] - rim[1]:+.3f}, z {lo_[2] - rim[2]:+.3f}..{hi_[2] - rim[2]:+.3f} about the rim; skull side behind/front at the rim {np.interp(0, bands, behind):.3f}/{np.interp(0, bands, front_):.3f}, rim {xo.max():.3f}')
    return ears, ear_w, auricle

def root_cap(P, F, faces, hair_weight, VN, shade, hair_look, ear_w, pivot, colour_fn=None):
    """The hair's roots: the skull under the hair as a layer of its own, lifted off the skin, dark
    indigo with the fine root strands. Through any gap or alpha cut in the hair layer (the ear's
    pocket, the temples, the skirt along the hairline) the eye met the scalp's skin: hair-coloured in
    the lab, plain grey on the owner's grey review head. A cap that is hair reads as roots either way.
    Coverage is compose()'s own field (the hair volume's ray test, the photo's closed hair mask, the
    scalp behind the face and the ears, nothing under the face grid), never the auricles; its alpha is
    that coverage feathered and the views' skin cut, so its edge stays behind the hairline and off the
    ears whatever the geometry does."""
    c = P[F[faces]].mean(1)
    w = hair_weight(c)
    keep = faces[w > ROOT_COVER]        # the auricles are left out by the caller; the skull inside the ear's window is capped where the roots are (left out, the window showed as a lighter rectangle of the skull's own roots behind the ear)
    if len(keep) < 50:
        print('root cap: nothing to cap'); return None
    used = np.unique(F[keep]); remap = np.full(len(P), -1); remap[used] = np.arange(len(used))
    Fc = remap[F[keep]].astype(np.int32)
    # Normals smoothed over 1 cm (the scan's skull is crumpled) and turned outward from the head's axis.
    tree = cKDTree(P[used]); n = VN[used].copy()
    for _ in range(2):
        nb = tree.query_ball_point(P[used], r=0.01)
        n = np.array([n[i].mean(0) for i in nb]); n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
    centre = np.array([pivot[0], P[used, 1].mean(), pivot[2]])
    n *= np.sign(((P[used] - centre) * n).sum(1, keepdims=True) + 1e-12)
    wv = hair_weight(P[used])
    lift_ = ROOT_LIFT[0] + (ROOT_LIFT[1] - ROOT_LIFT[0]) * smooth(ROOT_COVER, 0.85, wv)
    Pc = P[used] + n * lift_[:, None]
    style = getattr(hair_look, 'style', hair_look)
    def colour(q):
        return colour_fn(q) if colour_fn is not None else style(q, base=ROOT_BASE)
    def alpha(q):
        # The coverage field alone. (The projector's view cut is for the envelope: its depth buffers hold
        # the envelope only, so the cap at the back of the head counted as seen through the face from
        # the front, and the hug rule cut it wherever the photo shows skin: three quarters of the cap.)
        return smooth(ROOT_COVER - 0.05, ROOT_COVER + 0.1, hair_weight(q))
    uv, tex = hair_charts(Pc, Fc, colour, shade[used], n, alpha_fn=alpha, tile=ROOT_TILE)       # the skull's own shade: the cap carries the same roots as the skull under it (as a darker layer it showed its every edge)
    print('root cap:', len(Fc), 'faces of', len(faces), 'skull faces; lift (mm) p50', round(float(np.median(lift_)) * 1000, 2))
    return dict(pos=Pc[Fc].reshape(-1, 3), nrm=n[Fc].reshape(-1, 3), uv=uv.reshape(-1, 2), idx=np.arange(len(Fc) * 3), material=ROOT_MATERIAL, material_name='roots', tex=tex)

ROOT_BLEND_PX = 10             # source px either side of the photo's hair (gaps closed) over which the skirt goes from skin to roots
ROOT_SKIRT_SHADE = 0.9         # the roots on the skirt a little darker than the cap's: it folds back into the head

def hairline_skirt(P, F, is_skirt, VN, hair_look, projected):
    """The face grid's skirt (its border folded 1.5 cm back into the head) as a layer of its own: the
    picture's own skin where its rim lies on the photo's skin, cut away where the rim lies under the
    photo's hair (feathered over ROOT_BLEND_PX). As part of the grid it carried the bald picture's skin
    at the rim, and at the temples, where the head recedes behind the grid's edge, it stood out between
    the hair's strands as skin-coloured triangles; drawn as roots it stood out just the same."""
    faces = np.where(is_skirt)[0]
    env = getattr(hair_look, 'photo_env', None)
    if env is None:
        return None
    inside = ndimage.distance_transform_edt(env); outside = ndimage.distance_transform_edt(~env)
    style = getattr(hair_look, 'style', hair_look)
    def under_hair(q):
        cp_, _ = project(q); sp_ = np.clip(((cp_ - np.array([OX, OY])) / S).astype(int), 0, env.shape[0] - 1)
        return smooth(-ROOT_BLEND_PX, ROOT_BLEND_PX, inside[sp_[:, 1], sp_[:, 0]] - outside[sp_[:, 1], sp_[:, 0]])
    def colour(q):
        return projected(q)
    def alpha(q):
        # Under the photo's hair the skirt is cut away: drawn as roots it still stood out of the receding temple as a
        # saw-tooth of triangles along the hairline (owner: "lila trianglar längs med huvudet"); the dark scalp and the
        # root cap behind the grid's edge are what the hair's gaps should show there.
        return 1.0 - under_hair(q)
    uv, tex = hair_charts(P, F[faces], colour, np.ones(len(P)), VN, alpha_fn=alpha, tile=256)
    print('hairline skirt:', len(faces), 'faces as their own layer')
    return dict(pos=P[F[faces]].reshape(-1, 3), nrm=VN[F[faces]].reshape(-1, 3), uv=uv.reshape(-1, 2), idx=np.arange(len(faces) * 3), material=0, material_name='hairline', tex=tex)

def drop_floating(prim, min_verts=60, gap=0.004):
    """The primitive without its small connected pieces that sit more than `gap` from its main surface
    (connectivity by position, the faces have their own vertices before welding)."""
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    from scipy.spatial import cKDTree
    P = np.asarray(prim['pos']); F = np.asarray(prim['idx']).reshape(-1, 3)
    _, uid = np.unique(np.round(P, 6), axis=0, return_inverse=True); uid = uid.reshape(-1)
    e = uid[np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]])]
    nc, lab = connected_components(coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(uid.max() + 1,) * 2), directed=False)
    lab = lab[uid]; sizes = np.bincount(lab)
    main = cKDTree(P[lab == sizes.argmax()])
    drop = np.zeros(nc, bool)
    for c in np.where(sizes < min_verts)[0]:
        if main.query(P[lab == c])[0].min() > gap:
            drop[c] = True
    keep_face = ~drop[lab[F[:, 0]]]
    if drop.any():
        print(f"dropped {drop.sum()} floating scraps ({(~keep_face).sum()} faces) from material {prim['material']}")
    q = dict(prim); q['idx'] = F[keep_face].reshape(-1).astype(np.uint32)
    return q

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
    # aa and O are the owner's open and o-mouth pictures at full weight.
    "sil": {}, "PP": {"press": 1}, "FF": {"teeth": 0.6}, "TH": {"teeth": 0.5, "open": 0.15}, "DD": {"open": 0.35},
    "kk": {"open": 0.3}, "CH": {"mouth_wide": 0.4, "small_o": 0.3, "teeth": 0.3}, "SS": {"mouth_wide": 0.5, "teeth": 0.5}, "nn": {"open": 0.2},
    "RR": {"round": 0.45}, "aa": {"open": 1.0}, "E": {"mouth_wide": 0.8, "open": 0.25}, "I": {"mouth_wide": 0.65, "teeth": 0.3}, "O": {"round": 1.0}, "U": {"small_o": 1.0},
}

def package(glb, ids, posters):
    prims = list(range(ids['head_prims']))
    def lids(name): return {"node": ids['lids_node'], "mesh": ids['m_lids'], "primitives": [0], "targetIndex": LID_TARGETS.index(name)}
    def w(name, v):
        # The lids' closures live on their own mesh; the head keeps a zero target of the same name for
        # the expression suppression and the poses that name it.
        head = {"node": 3, "mesh": ids['m_head'], "primitives": prims, "targetIndex": TARGETS.index(name), "value": v}
        return [head, {**lids(name), "value": v}] if name in LID_TARGETS else head
    def flat(writes): return [x for ws in writes for x in (ws if isinstance(ws, list) else [ws])]
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
    poses = {k: flat(v) for k, v in poses.items()}
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
                   "suppressExpressionTargets": [TARGETS.index(n) for n in ("wide", "squint", "happy")]},
                  # The lids roll down through a chain of targets at closures 0.1 .. 0.9 (steps), then closed.
                  {**lids("blink"), "steps": [LID_TARGETS.index(n) for n in LID_TARGETS if n.startswith('blink_')],
                   "closures": [{"targetIndex": LID_TARGETS.index("squint"), "closure": SQUINT_CLOSURE}],
                   "suppressExpressionTargets": [LID_TARGETS.index("squint")]}],
        "stateAnimations": {"thinking": "think"},
        "neckChain": [{"node": ids['neck_chain'][0], "share": 0.3}, {"node": ids['neck_chain'][1], "share": 0.35}],
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
