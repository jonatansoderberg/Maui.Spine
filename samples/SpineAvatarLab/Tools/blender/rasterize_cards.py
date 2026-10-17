# rasterize_cards.py DATA.npz OUT.png [--ss 4] [--depth 0.012] : every strand passing through a card's slab (within its half
# width laterally and +-depth along the normal) is drawn into the card's UV strip, deepest first and darker, frames
# interpolated along the card; alpha = coverage with a lateral edge fade and a tip fade. Deterministic.
import sys, numpy as np
from PIL import Image, ImageDraw
args = sys.argv[1:]; data = np.load(args[0]); out = args[1]
SS = int(args[args.index('--ss') + 1]) if '--ss' in args else 4
DEPTH = float(args[args.index('--depth') + 1]) if '--depth' in args else 0.012
sw, sh = (int(v) for v in (args[args.index('--strip') + 1] if '--strip' in args else '64,512').split(','))   # strip pixels; 56,448 keeps the package GLB under the archive's 32 MiB per-entry limit with 700 cards
pos, offs, gidx, rnd = data['pos'], data['offs'], data['gidx'], data['rnd']
samples, tang, norm, lat, width, strip, glen, card_of_guide = (data[k] for k in ('samples', 'tang', 'norm', 'lat', 'width', 'strip', 'glen', 'card_of_guide'))
cols, rows = int(data['cols']), int(data['rows']); W, H = cols * sw, rows * sh
n_cards, n_s = samples.shape[:2]
atlas = np.zeros((H, W, 4), np.float32)
def _vec(flag, default):
    return np.array([float(x) for x in args[args.index(flag) + 1].split(',')]) if flag in args else np.array(default)
C0 = _vec('--c0', [0.13, 0.15, 0.40]); C1 = _vec('--c1', [0.50, 0.54, 0.82]); GAMMA = float(args[args.index('--gamma') + 1]) if '--gamma' in args else 1.8
def ramp(r): r = r ** GAMMA; return C0 * (1 - r) + C1 * r
R_ROOT = float(args[args.index('--rroot') + 1]) if '--rroot' in args else 0.0009
R_TIP = float(args[args.index('--rtip') + 1]) if '--rtip' in args else 0.0003
SHADE_DEPTH = float(args[args.index('--shadedepth') + 1]) if '--shadedepth' in args else 0.04
SHADE_MIN = float(args[args.index('--shademin') + 1]) if '--shademin' in args else 0.6
ONLY = int(args[args.index('--only') + 1]) if '--only' in args else 0   # rasterize only the first N strips (a quick colour test)
# per-strand arc lengths
N = len(offs) - 1
arc = np.zeros(len(pos), np.float32); slen = np.zeros(N, np.float32)
for si in range(N):
    p = pos[offs[si]:offs[si + 1]]
    if len(p) > 1:
        s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))]); arc[offs[si]:offs[si + 1]] = s; slen[si] = s[-1]
drawn = 0
for k in range(ONLY or n_cards):
    hw = width[k] * 0.5; L = glen[k]; c = int(strip[k]) % cols; r = int(strip[k]) // cols
    sm = samples[k]; lo = sm.min(0) - hw - DEPTH; hi = sm.max(0) + hw + DEPTH
    # candidate points by the card's box, then strands with any candidate point
    inbox = np.all((pos >= lo) & (pos <= hi), axis=1)
    if not inbox.any(): continue
    cand = np.unique(np.searchsorted(offs, np.nonzero(inbox)[0], side='right') - 1)
    img = Image.new('RGBA', (sw * SS, sh * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    items = []
    for si in cand:
        p = pos[offs[si]:offs[si + 1]]
        if len(p) < 2: continue
        # nearest sample per point (3D), fractional index by the two nearest, then the interpolated frame
        dd = ((p[:, None, :] - sm[None, :, :]) ** 2).sum(2); i0 = dd.argmin(1)
        i1 = np.clip(i0 + np.where((i0 + 1 < n_s) & (dd[np.arange(len(p)), np.clip(i0 + 1, 0, n_s - 1)] < dd[np.arange(len(p)), np.clip(i0 - 1, 0, n_s - 1)]), 1, -1), 0, n_s - 1)
        t_seg = tang[k][i0]; base = sm[i0]; seg = sm[i1] - base; segl = (seg ** 2).sum(1)
        f = np.where(segl > 1e-10, ((p - base) * seg).sum(1) / np.maximum(segl, 1e-10), 0.0); f = np.clip(f, 0, 1)
        P = base + seg * f[:, None]; B = lat[k][i0] * (1 - f)[:, None] + lat[k][i1] * f[:, None]; Nn = norm[k][i0] * (1 - f)[:, None] + norm[k][i1] * f[:, None]
        idx = i0 + (i1 - i0) * f
        dlt = p - P; u = (dlt * B).sum(1) / hw; dep = (dlt * Nn).sum(1); v = idx / (n_s - 1)
        reach = 1.0 + 1.2 * np.clip(1 - v / 0.3, 0, 1)          # near the root the strip takes strands from a wider band (the clump has not formed yet)
        inside = (np.abs(u) <= reach) & (np.abs(dep) <= DEPTH)
        if inside.sum() < 2: continue
        v = idx / (n_s - 1)
        items.append((float(np.mean(dep[inside])), si, u, v, dep, inside))
    items.sort(key=lambda t: t[0])
    for mean_dep, si, u, v, dep, inside in items:
        col = ramp(float(rnd[si])); shade = float(np.clip(1.0 + mean_dep / SHADE_DEPTH, SHADE_MIN, 1.0))   # deeper strands darker: the reference's locks are dark between their crests
        # near the root the bundle is pulled toward the card's centre line (the clump forms over the first 6 cm), so the
        # card's root end is dense and the parting reads as a line, not a band
        ROOT_PULL = float(args[args.index('--rootpull') + 1]) if '--rootpull' in args else 0.25
        k_root = np.clip(1 - v / 0.3, 0, 1)
        u_draw = u * (1 - ROOT_PULL * k_root) / (1 + 1.2 * k_root)   # compressed into the strip near the root, never collapsed to a line
        u_draw = np.clip(u_draw, -0.98, 0.98)
        x = (0.5 + 0.5 * u_draw) * sw * SS; y = v * sh * SS
        s = arc[offs[si]:offs[si + 1]]; Ls = max(float(slen[si]), 1e-6)
        n = len(x)
        for j in range(n - 1):
            if not (inside[j] and inside[j + 1]): continue
            t = s[j] / Ls
            rad = (R_ROOT + (R_TIP - R_ROOT) * t) / max(width[k], 1e-6) * sw * SS
            sh_j = shade * (0.78 + 0.22 * t)
            rgb = tuple(int(255 * min(1, cc * sh_j)) for cc in col)
            a = 255 if t < 0.82 else int(255 * max(0.0, 1 - (t - 0.82) / 0.18) ** 0.8)
            d.line([(x[j], y[j]), (x[j + 1], y[j + 1])], fill=rgb + (a,), width=max(1, int(round(2 * rad))))
        drawn += 1
    small = np.asarray(img.resize((sw, sh), Image.BOX)).astype(np.float32) / 255
    atlas[r * sh:(r + 1) * sh, c * sw:(c + 1) * sw] = small
ramp_u = np.ones(sw); e = int(0.10 * sw); ramp_u[:e] = np.linspace(0, 1, e); ramp_u[-e:] = np.linspace(1, 0, e)
for c in range(cols): atlas[:, c * sw:(c + 1) * sw, 3] *= ramp_u[None, :]
# the card's own end: alpha fades over the strip's last 15 % so a card never ends in a hard edge where longer strands are cut by it
tip = np.ones(sh); e3 = int(0.15 * sh); tip[-e3:] = np.linspace(1, 0, e3) ** 0.7
for r in range(rows): atlas[r * sh:(r + 1) * sh, :, 3] *= tip[:, None]
# the root rows: alpha raised toward 1 over the first 4 % of the strip so the root edge is solid under the cap
ramp_v = np.ones(sh); e2 = int(0.04 * sh); ramp_v[:e2] = 1.0
# the card's root end feathered: alpha rises over the strip's first 6 % (the root block forced to 0.9 made every card a
# solid rectangle whose straight root edge stood as a tooth in the top and back silhouettes; the root pull keeps the roots dense)
rootf = np.ones(sh); e2 = int(0.06 * sh); rootf[:e2] = np.linspace(0, 1, e2) ** 0.7
for r in range(rows): atlas[r * sh:(r + 1) * sh, :, 3] *= rootf[:, None]
Image.fromarray((np.clip(atlas, 0, 1) * 255).astype(np.uint8), 'RGBA').save(out)
cov = atlas[..., 3]; print("atlas", W, H, "strand-card pairs drawn", drawn, "alpha>0.5 fraction", round(float((cov > 0.5).mean()), 3), "mean", round(float(cov.mean()), 3))
