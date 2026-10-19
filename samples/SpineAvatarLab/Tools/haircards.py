"""Hair cards for Kairo's lock tips (#542).

The hair is an envelope cut by the owner's views' masks, and an envelope cannot carry strand-level tips: cut by
a view's strands it tears into slivers from every other angle. Real-time hair does it with cards: strips whose
geometry reaches past the visible hair and whose alpha texture carries the tapered strands. Here the envelope is
built a little inside the views' hair silhouettes where they are ragged (hairenv.py inset= / inset_front=), and
per view (front, right, back, left; not the crown) a ribbon of cards follows the envelope's silhouette in that
view, from OVERLAP_W inside the envelope to REACH_W past its rim, tilted away from the camera by the slope of
the hair's surface above the rim (it continues the sheet it hangs from, curling under as the sheet does, instead
of standing out as a fin), textured by projecting the view onto it: the view's own pixels and the view's hair
mask as alpha. Where the view's hair reaches past the envelope (the lock ends, the strands' tapered tips)
the ribbon draws it; where it does not, the ribbon is clear. A stretch of silhouette belongs to the view that
sees the surface behind it most squarely (the nape's ends to the back view, the bob's sides' to the side views,
the fringe's and the cheek locks' to the front): a card hangs in the plane of the hair sheet it continues, not
across it. The ribbon is cut into pieces where it has nothing to draw, and texels the head or an ear stands in
front of are cleared.
"""
import os
import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree
from skimage import measure

# Lengths below are in the views' world units (Pixal3D's: the head is about 1 unit, 1 px of a view about 0.00075).
OVERLAP_W = 0.010     # how far the ribbon reaches inside the envelope's rim (inside its volume, hidden)
REACH_W = 0.050       # how far the ribbon reaches past the rim along its tilt
ROWS = 102            # texels across the ribbon (0.8 px each)
FADE_ROWS = 12        # texels of fade at the ribbon's outer edge (hair reaching the edge ends soft, not in a line)
LIVE_PX = 40          # px past the silhouette within which the view's hair makes a silhouette px live
STEP_PX = 5           # px along the silhouette between the ribbon's sections (its vertex pairs)
GAP_PX = 30           # a run of empty silhouette px this long ends a piece
MIN_PIECE_PX = 15
MIN_HAIR_PX = 2       # a silhouette px is live when the view's hair reaches this far past the silhouette
SHOULDER_PX = 14      # px inside the silhouette where the surface's slope (the card's tilt) is measured
FACING_PX = (8, 16, 24, 32)   # px inside the silhouette where the surface's facing is measured (the best of them: the sheet above the lip's curl)
FACING_MIN = 0.55     # cosine between that surface's normal and the view's camera below which the view attaches no card
FACING_BIAS = 0.05    # a view keeps a stretch unless another view sees its surface this much more squarely
TILT_MAX = 50.0       # degrees the card tilts away from the camera at most
UP_MAX = -0.3         # a silhouette px whose outward direction points up more than this (image y down) gets no card: the crown's wisps
TANGENT_MIN = 0.64    # |cos| between the silhouette's direction and the view's horizontal below which a px gets no card: a card hung from a silhouette's side stretch (the contour turning up at the nape's corners) faces the perpendicular view as a strip
NEAR_MIN = 0.0        # a px whose rim lies behind the envelope's centre along the view's axis gets no card: a profile sees the far side's bottom edge under the near side's where the near side is shorter, and its card hung across the nape
RIM_TOWARD_MAX = 0.5  # |cos| between the rim's direction in 3D and the view's axis above which a px gets no card: where the silhouette's rim recedes along the view (the nape's corner from the side), the card's plane turns to face the perpendicular view and its texture smears; the direction is taken over RIM_BASELINE px either side on the depth smoothed by RIM_SIGMA px, so the nape's wavy edge (its depth wobbles by centimetres) does not read as receding
RIM_BASELINE = 40
RIM_SIGMA = 12.0
DEPTH_BAND = 0.08     # a px whose rim lies further behind the nearest live rim within DEPTH_WINDOW px along the silhouette than this gets no card: a profile sees the far side's bottom edge under the near side's where the near side is shorter, 9-13 cm behind it, and the card hung across the nape as a strip from behind
DEPTH_WINDOW = 240    # px along the silhouette
SHOULDER_DEPTH_TOL = 0.08   # a px whose rim lies further behind the surface SHOULDER_PX above it than this gets no card: a profile sees the far side's bottom edge under the near side's where the near side is shorter (the rim 13 cm behind the hair above it), and the card hung across the nape as a strip from behind
STRAND_P = (15, 30)   # percentiles of the view's hair luminance between which a texel past the rim fades out: the dark gaps between strands clear, so a lock's end frays into strands and a card seen edge-on breaks into dashes
STRAND_T = (0.006, 0.016)   # distance past the rim over which the strand fade comes in (the card's root stays solid against the envelope)
REACH_PAD = 6         # texel rows a piece's cards reach past its last drawn texel
ROOT_SHADE = (0.75, 0.008)  # the card's colour is scaled by the first factor at the rim, rising to 1 over the second distance past it: the view's edge pixels are lit from behind and pale, the envelope's rim is baked dark, and the band between them read as a pale rod under a dark edge
ATLAS = 2048
LIFT = 0.003          # toward the camera from the rim's depth (no z-fighting with the envelope)
ALPHA_BLUR = 0.7      # px of blur on the hair mask (the MASK cutoff draws a rounded edge)
HEAD_TOL = 0.005      # a texel the head stands nearer than this in front of is cleared
SEEN_TOL = 0.012      # another view sees a texel when it lies no further than this behind that view's depth buffer
OTHER_HAIR_PX = 6     # px another view's hair mask is grown by before a texel it sees outside it is cleared (a card's solid band at a corner of the silhouette stood out as a rod from the perpendicular view)
CONTOUR_SIGMA = 3.0   # px of blur on the envelope's coverage before its contour is traced (the depth cells' steps)
NORMAL_SIGMA = 4.0    # px the contour is smoothed over for its normals


def _unproject(view, px, py, d):
    focal, S, c2w = view['focal'], view['S'], view['c2w']
    pc = np.stack([(px - S / 2.0) * d / focal, -(py - S / 2.0) * d / focal, -d], 1)
    return pc @ c2w[:3, :3].T + c2w[:3, 3]


def _project(view, W):
    focal, S, c2w = view['focal'], view['S'], view['c2w']
    w2c = np.linalg.inv(c2w); pc = W @ w2c[:3, :3].T + w2c[:3, 3]; dist = -pc[:, 2]
    return focal * pc[:, 0] / (dist + 1e-8) + S / 2.0, -focal * pc[:, 1] / (dist + 1e-8) + S / 2.0, dist


def _resample(c, step=1.0):
    """A contour (N x 2, x y) resampled every `step` px of arc length."""
    d = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(c, axis=0), axis=1))]
    if d[-1] < step * 2:
        return None
    s = np.arange(0.0, d[-1], step)
    return np.stack([np.interp(s, d, c[:, 0]), np.interp(s, d, c[:, 1])], 1)


def coverage(view):
    """The envelope's coverage of the view at pixel resolution (from its depth cells)."""
    S, cell = view['S'], view['cell']
    cov = np.repeat(np.repeat(np.isfinite(view['zb_env']), cell, 0), cell, 1)
    out = np.zeros((S, S), bool); h, w = min(S, cov.shape[0]), min(S, cov.shape[1]); out[:h, :w] = cov[:h, :w]
    return out


def _depth_at(view, p, fallback=None):
    """The envelope's depth at image points p (N x 2), nan where no cell holds it (or `fallback` there)."""
    zb, cell = view['zb_env'], view['cell']
    z = zb[np.clip((p[:, 1] / cell).astype(int), 0, zb.shape[0] - 1), np.clip((p[:, 0] / cell).astype(int), 0, zb.shape[1] - 1)]
    z = np.where(np.isfinite(z), z, np.nan)
    if fallback is not None:
        z = np.where(np.isnan(z), fallback, z)
    return z


def _unit(v):
    return v / (np.linalg.norm(v, axis=-1, keepdims=True) + 1e-12)


def _other_view_masks(v):
    """A card view's grown hair mask and its full depth buffer (envelope and head, spread by one cell), cached on the view."""
    if '_raw_dil' not in v:
        v['_raw_dil'] = ndimage.binary_dilation(v['raw'], iterations=OTHER_HAIR_PX)
        v['_zb_all'] = ndimage.minimum_filter(np.minimum(v['zb_env'], v['zb_head']), size=3)
    return v['_raw_dil'], v['_zb_all']


def ribbon(view, cvs, normals, ear_cut=None, from_world=None):
    """The view's ribbon pieces: dicts with the silhouette px (c), the cards' rim points and unit directions in
    the views' world (rim, dir), the texels' image positions and depths (px, py, pz: rows across the ribbon,
    columns along it) and their alpha (tex). `cvs`: every card view (the facing contest; a texel another view sees
    outside its hair is cleared); `normals`: (centres, normals) of the envelope's faces in the views' world."""
    cams = [v['cam'] for v in cvs]
    S, cell = view['S'], view['cell']
    zb_head = view['zb_head']
    cov = coverage(view)
    covf = ndimage.gaussian_filter(cov.astype(float), CONTOUR_SIGMA)
    raw = view['raw']
    # The view's hair that belongs to the envelope's hair: pixels joined to its coverage (not an iris, which
    # passes the hue rule, seen across skin).
    near = ndimage.binary_dilation(cov, iterations=3)
    hair = raw & ndimage.binary_propagation(near, mask=near | raw)
    hairf = ndimage.gaussian_filter(hair.astype(float), ALPHA_BLUR)
    ts = np.linspace(-OVERLAP_W, REACH_W, ROWS)              # m along the card per texel row
    lum = view['rgb'] @ np.array([0.299, 0.587, 0.114])
    l_lo, l_hi = np.percentile(lum[hair], STRAND_P) if hair.sum() > 100 else (0.0, 1.0)
    strand_w = np.clip((ts - STRAND_T[0]) / (STRAND_T[1] - STRAND_T[0]), 0, 1)       # per texel row
    tree = cKDTree(normals[0]) if normals is not None else None
    own_i = [i for i, cm in enumerate(cams) if np.allclose(cm, view['cam'])][0]
    fade = np.ones(ROWS); fade[-FADE_ROWS - 1:-1] = np.linspace(1.0, 0.0, FADE_ROWS); fade[-1] = 0.0
    shade = ROOT_SHADE[0] + (1.0 - ROOT_SHADE[0]) * np.clip(ts / ROOT_SHADE[1], 0, 1)      # per texel row
    contours = measure.find_contours(covf, 0.5)
    boxes = [(c[:, 0].min(), c[:, 1].min(), c[:, 0].max(), c[:, 1].max()) for c in contours]
    pieces = []
    for i, cont in enumerate(contours):
        b = boxes[i]
        if any(j != i and o[0] < b[0] and o[1] < b[1] and o[2] > b[2] and o[3] > b[3] for j, o in enumerate(boxes)):
            continue                                # a hole (the ear's pocket): no card reaches into it
        c = _resample(cont[:, ::-1], 1.0)          # (row, col) -> (x, y)
        if c is None or len(c) < 60:
            continue
        closed = np.linalg.norm(cont[0] - cont[-1]) < 1.5
        mode = 'wrap' if closed else 'nearest'
        cs = np.stack([ndimage.gaussian_filter1d(c[:, k], NORMAL_SIGMA, mode=mode) for k in range(2)], 1)
        t = (np.roll(cs, -1, 0) - np.roll(cs, 1, 0)) if closed else np.gradient(cs, axis=0)
        t /= np.linalg.norm(t, axis=1, keepdims=True) + 1e-9
        n = np.stack([t[:, 1], -t[:, 0]], 1)
        probe = c + n * 4.0
        if cov[np.clip(probe[:, 1].astype(int), 0, S - 1), np.clip(probe[:, 0].astype(int), 0, S - 1)].mean() > 0.5:
            n = -n                                  # outward
        # the rim's depth per silhouette px: the first envelope cell stepping inward
        d = np.full(len(c), np.nan)
        for o in (2.0, 5.0, 9.0, 14.0):
            z = _depth_at(view, c - n * o)
            take = np.isnan(d) & np.isfinite(z)
            d[take] = z[take]
        # live: the view's hair within LIVE_PX past the silhouette, and no head just in front of it there
        live = np.zeros(len(c), bool)
        for o in range(MIN_HAIR_PX, LIVE_PX):
            p = c + n * o
            live |= hair[np.clip(p[:, 1].astype(int), 0, S - 1), np.clip(p[:, 0].astype(int), 0, S - 1)]
        live &= np.isfinite(d) & (n[:, 1] > UP_MAX) & (np.abs(t[:, 0]) >= TANGENT_MIN)
        tilt = np.zeros(len(c))
        if live.any():
            # The rim in 3D against the view's axis (the depth filled across gaps first): not receding along the
            # view, on the view's own half of the envelope, within the lip's curl of the hair above it, and no
            # further behind the nearest rim nearby than DEPTH_BAND.
            dfill = d.copy(); good = np.isfinite(dfill)
            if good.sum() >= 2:
                ii = np.arange(len(dfill)); dfill = np.interp(ii, ii[good], dfill[good])
                dfill = ndimage.median_filter(dfill, size=25, mode=mode)
                rim3 = _unproject(view, c[:, 0], c[:, 1], dfill)
                centre = normals[0].mean(0) if normals is not None else np.zeros(3)
                axis = _unit(view['cam'] - centre)
                rim3s = _unproject(view, c[:, 0], c[:, 1], ndimage.gaussian_filter1d(dfill, RIM_SIGMA, mode=mode))
                t3 = np.roll(rim3s, -RIM_BASELINE, 0) - np.roll(rim3s, RIM_BASELINE, 0)
                if not closed:
                    t3[:RIM_BASELINE] = t3[RIM_BASELINE]; t3[-RIM_BASELINE:] = t3[-RIM_BASELINE - 1]
                t3 = _unit(t3)
                near_c = (rim3 - centre) @ axis
                zs_sh = _depth_at(view, c - n * SHOULDER_PX, fallback=dfill)
                live &= (np.abs(t3 @ axis) <= RIM_TOWARD_MAX) & (near_c > NEAR_MIN) & (dfill - zs_sh <= SHOULDER_DEPTH_TOL)
                if live.any():
                    nearest = ndimage.maximum_filter1d(np.where(live, near_c, -np.inf), size=2 * DEPTH_WINDOW + 1, mode=mode)
                    live &= near_c >= nearest - DEPTH_BAND
        if tree is not None and live.any():
            # The facing contest: the surface above the silhouette (the best of FACING_PX inside it, above the lip's
            # curl), by the envelope's smoothed normals, against every card view's camera. A stretch whose surface
            # faces another camera more squarely is that view's (a front card across the bob's side stood out as a
            # box from three quarters), and one facing no camera (the crown's wisps) is nobody's.
            def facing_at(off):
                ps = c - n * off; zs = _depth_at(view, ps, fallback=d)
                W = _unproject(view, ps[:, 0], ps[:, 1], np.where(np.isfinite(zs), zs, 1.0))
                nw = normals[1][tree.query(W)[1]]
                return np.array([(nw * _unit(cm[None, :] - W)).sum(1) for cm in cams])
            F = np.max([facing_at(o) for o in FACING_PX], axis=0)
            own = F[own_i]; others = np.max(np.delete(F, own_i, 0), axis=0)
            live &= (own >= FACING_MIN) & (own + FACING_BIAS >= others)
            # the card's tilt away from the camera: the surface's slope at the shoulder
            tilt = np.minimum(np.arccos(np.clip(facing_at(SHOULDER_PX)[own_i], 0.0, 1.0)), np.radians(TILT_MAX))
        if not live.any():
            continue
        bridged = ndimage.binary_closing(live, iterations=GAP_PX // 2) | live
        lab, nlab = ndimage.label(bridged)
        for k in range(1, nlab + 1):
            idx = np.where(lab == k)[0]
            i0, i1 = idx[0], idx[-1]
            if i1 - i0 < MIN_PIECE_PX:
                continue
            i0 = max(i0 - STEP_PX, 0); i1 = min(i1 + STEP_PX, len(c) - 1)
            dd = d[i0:i1 + 1].copy(); good = np.isfinite(dd)
            if good.sum() < 2:
                continue
            ii = np.arange(len(dd)); dd = np.interp(ii, ii[good], dd[good])
            dd = ndimage.median_filter(dd, size=min(25, len(dd)), mode='nearest')
            tl = ndimage.median_filter(tilt[i0:i1 + 1], size=min(25, len(dd)), mode='nearest')
            cp, np_ = c[i0:i1 + 1], n[i0:i1 + 1]
            rim = _unproject(view, cp[:, 0], cp[:, 1], dd - LIFT)
            n3 = _unit(_unproject(view, cp[:, 0] + np_[:, 0], cp[:, 1] + np_[:, 1], dd - LIFT) - rim)
            away = _unit(rim - view['cam'][None, :])
            dr = _unit(np.cos(tl)[:, None] * n3 + np.sin(tl)[:, None] * away)
            L = len(cp)
            W = rim[None, :, :] + ts[:, None, None] * dr[None, :, :]          # ROWS x L x 3
            px, py, pz = _project(view, W.reshape(-1, 3))
            px = px.reshape(ROWS, L); py = py.reshape(ROWS, L); pz = pz.reshape(ROWS, L)
            a = ndimage.map_coordinates(hairf, [py.ravel(), px.ravel()], order=1, mode='constant').reshape(ROWS, L) * fade[:, None]
            lm = ndimage.map_coordinates(lum, [py.ravel(), px.ravel()], order=1, mode='nearest').reshape(ROWS, L)
            sf = np.clip((lm - l_lo) / max(l_hi - l_lo, 1.0), 0, 1)
            a *= 1.0 - strand_w[:, None] * (1.0 - sf)
            cx = np.clip((px / cell).astype(int), 0, zb_head.shape[1] - 1); cy = np.clip((py / cell).astype(int), 0, zb_head.shape[0] - 1)
            a[zb_head[cy, cx] < pz - HEAD_TOL] = 0.0           # the head stands in front
            if ear_cut is not None and from_world is not None:
                a[ear_cut(from_world(W.reshape(-1, 3))).reshape(ROWS, L)] = 0.0
            # Cleared where another view sees the texel (not behind its envelope or head) outside its own hair, and
            # wherever the opposite view sees it at all: a card faces its own view, and what the opposite view
            # sees of it past the neck is a strip hanging under the nape (the front's bottom corners from behind).
            for ov in cvs:
                if ov is view:
                    continue
                raw_dil, zb_all = _other_view_masks(ov)
                ox, oy, od = _project(ov, W.reshape(-1, 3)); S2, cell2 = ov['S'], ov['cell']
                inside = (ox >= 0) & (ox < S2 - 1) & (oy >= 0) & (oy < S2 - 1) & (od > 0)
                oxi = np.clip(ox.astype(int), 0, S2 - 1); oyi = np.clip(oy.astype(int), 0, S2 - 1)
                ocx = np.clip((ox / cell2).astype(int), 0, zb_all.shape[1] - 1); ocy = np.clip((oy / cell2).astype(int), 0, zb_all.shape[0] - 1)
                seen = inside & (od <= zb_all[ocy, ocx] + SEEN_TOL)
                opposite = np.dot(_unit(ov['cam']), _unit(view['cam'])) < -0.7
                a[(seen & (opposite | ~raw_dil[oyi, oxi])).reshape(ROWS, L)] = 0.0
            a[0, :] = 0.0; a[-1, :] = 0.0
            if not (a[:, STEP_PX:L - STEP_PX] > 0.5).any():
                continue
            rows_used = min(int(np.where((a > 0.5).any(1))[0].max()) + REACH_PAD, ROWS - 1) + 1
            a[rows_used - 1:, :] = 0.0
            pieces.append(dict(c=cp, rim=rim, dir=dr, px=px, py=py, pz=pz, tex=a, rows=rows_used, shade=shade))
    return pieces


def debug_overlay(view, pieces, path):
    """The view with its hair mask's edge (yellow), the envelope's silhouette (cyan) and every piece's inner
    (magenta) and outer (orange) edge drawn on it (KAIRO_CARDS_DEBUG=dir)."""
    from PIL import Image, ImageDraw
    a = np.clip(view['rgb'], 0, 255).astype(np.uint8).copy()
    cov = coverage(view); raw = view['raw']
    a[raw & ~ndimage.binary_erosion(raw, iterations=1)] = (255, 255, 0)
    a[cov & ~ndimage.binary_erosion(cov, iterations=2)] = (0, 255, 255)
    im = Image.fromarray(a); dr = ImageDraw.Draw(im)
    for p in pieces:
        inner = np.stack([p['px'][0], p['py'][0]], 1); outer = np.stack([p['px'][-1], p['py'][-1]], 1)
        dr.line([tuple(q) for q in inner[::3]], fill=(255, 0, 255), width=2)
        dr.line([tuple(q) for q in outer[::3]], fill=(255, 128, 0), width=1)
    im.save(path)


def dump_views(views, normals, path):
    """The views' arrays the ribbon needs, for working on it outside a build (KAIRO_CARDS_DUMP=file.npz)."""
    out = {}
    for i, v in enumerate(views):
        if 'zb_env' not in v:
            continue
        for k in ('rgb', 'raw', 'filled', 'zb_env', 'zb_head', 'c2w', 'cam'):
            out[f'{i}_{k}'] = v[k]
        out[f'{i}_meta'] = np.array([v['S'], v['cell'], v['focal']]); out[f'{i}_name'] = np.array(v['name'])
    if normals is not None:
        out['fc'], out['fn'] = normals
    np.savez_compressed(path, **out)


def load_views(path):
    z = np.load(path)
    views = []
    for i in range(8):
        if f'{i}_meta' not in z:
            break
        S, cell, focal = z[f'{i}_meta']
        views.append(dict(S=int(S), cell=int(cell), focal=float(focal), name=str(z[f'{i}_name']), **{k: z[f'{i}_{k}'] for k in ('rgb', 'raw', 'filled', 'zb_env', 'zb_head', 'c2w', 'cam')}))
    normals = (z['fc'], z['fn']) if 'fc' in z else None
    return views, normals


def card_views(views):
    return [v for v in views if v['name'] != 'crown' and 'zb_env' in v]


def build_cards(views, from_world, ear_cut=None, normals=None):
    """Geometry (model frame) and one RGBA atlas for every view's ribbon. Returns None without cards."""
    if os.environ.get('KAIRO_CARDS_DUMP'):
        dump_views(views, normals, os.environ['KAIRO_CARDS_DUMP'])
    cvs = card_views(views)
    pieces = []
    for v in cvs:
        ps = ribbon(v, cvs, normals, ear_cut, from_world)
        for p in ps:
            p['view'] = v
        pieces += ps
        print('hair cards: view', v['name'], 'pieces', len(ps), 'px along the silhouette', sum(len(p['c']) for p in ps))
        if os.environ.get('KAIRO_CARDS_DEBUG'):
            debug_overlay(v, ps, os.path.join(os.environ['KAIRO_CARDS_DEBUG'], 'cards-' + v['name'].replace('.png', '') + '.png'))
    if not pieces:
        return None
    rows = ATLAS // ROWS
    atlas = np.zeros((ATLAS, ATLAS, 4), np.uint8)
    chunks = []                                     # a piece wider than the atlas in chunks
    for p in pieces:
        L = len(p['c']); start = 0
        while start < L:
            end = min(start + ATLAS - 2, L)
            if end - start >= MIN_PIECE_PX or start == 0:
                chunks.append((p, start, end))
            start = end
    col = 0; row = 0
    pos, nrm, uv, idx = [], [], [], []
    for p, s0, s1 in chunks:
        L = s1 - s0
        if col + L + 2 > ATLAS:
            col = 0; row += 1
            if row >= rows:
                print('hair cards: the atlas is full, pieces dropped')
                break
        v = p['view']
        py_, px_ = p['py'][:, s0:s1], p['px'][:, s0:s1]
        rgb = np.stack([ndimage.map_coordinates(v['filled'][..., k], [py_.ravel(), px_.ravel()], order=1, mode='nearest') for k in range(3)], -1).reshape(ROWS, L, 3) * p['shade'][:, None, None]
        a = p['tex'][:, s0:s1].copy(); a[:, 0] = 0.0; a[:, -1] = 0.0
        atlas[row * ROWS:(row + 1) * ROWS, col + 1:col + 1 + L] = np.dstack([np.clip(rgb, 0, 255), np.clip(a, 0, 1) * 255]).astype(np.uint8)
        sec = np.r_[np.arange(0, L - 1, STEP_PX), L - 1]
        base = sum(len(q) for q in pos)
        R_ = p['rows']; reach = -OVERLAP_W + (REACH_W + OVERLAP_W) * (R_ - 1) / (ROWS - 1)
        v_in, v_out = (row * ROWS + 0.5) / ATLAS, (row * ROWS + R_ - 1 + 0.5) / ATLAS
        for i in sec:
            rim = p['rim'][s0 + i]; dr_ = p['dir'][s0 + i]
            inner = rim - dr_ * OVERLAP_W; outer = rim + dr_ * reach
            u_ = (col + 1 + i + 0.5) / ATLAS
            pos.append(np.stack([inner, outer]))
            uv.append(np.array([[u_, v_in], [u_, v_out]]))
            nc = v['cam'] - rim; nc /= np.linalg.norm(nc) + 1e-12
            nrm.append(np.stack([nc, nc]))
        for i in range(len(sec) - 1):
            a0, b0, a1, b1 = base + 2 * i, base + 2 * i + 1, base + 2 * i + 2, base + 2 * i + 3
            idx += [a0, b0, a1, b0, b1, a1]
        col += L + 2
    posw = np.concatenate(pos); nrmw = np.concatenate(nrm); uvs = np.concatenate(uv)
    print('hair cards:', len(pieces), 'pieces,', len(posw), 'vertices,', len(idx) // 3, 'triangles, atlas rows used', row + 1)
    pm = from_world(posw)
    nm = from_world(nrmw) - from_world(np.zeros_like(nrmw))
    nm /= np.linalg.norm(nm, axis=1, keepdims=True) + 1e-12
    return dict(pos=pm, nrm=nm, uv=uvs, idx=np.array(idx, np.uint32), tex=atlas)
