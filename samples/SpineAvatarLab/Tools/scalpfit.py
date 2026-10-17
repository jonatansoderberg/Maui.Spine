# Fits the owner's scalp views (edited renders of the lab's bald head: front, right, left, back and the crown
# with the head pitched down) to the bald scan by silhouette: per view the yaw of the model (the lab's camera
# orbit), the head's pitch about its pivot, and picture px = crop px * scale + offset, by IoU above the collar.
#   scalpfit.py OUTDIR [ASSETDIR]   -> prints SCALP_VIEWS entries, writes OUTDIR/fit-<name>.png (picture | model | overlap)
import sys, os, math, numpy as np
from PIL import Image
from scipy import ndimage, optimize
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kairo_scan as K
work = os.path.expanduser('~/.cache/spine-avatar-lab/kairo'); out = sys.argv[1]; os.makedirs(out, exist_ok=True)
assets = sys.argv[2] if len(sys.argv) > 2 else f'{work}/assets-scalp'
PIVOT = np.array([0.005, 0.053, -0.046])        # the head's pivot (compose prints it: ear level on the neck's axis)
VIEWS = [('front.png', 0.0, 0.0), ('right.png', 90.0, 0.0), ('left.png', -90.0, 0.0), ('back.png', 180.0, 0.0), ('crown-head-down-35.png', 0.0, -35.0)]
BP, BF, _ = K.load_scan(f'{work}/bald/kairo-bald-s42.glb', 90000); BP, BF = K.outer_surface(BP, BF)
BP = K.reframe(BP, K.OY - K.BALD_OY)
rng = np.random.default_rng(1)
pts = np.concatenate([np.einsum('k,fkd->fd', b, BP[BF]) for b in rng.dirichlet((1, 1, 1), size=8)] + [BP])
cpx = K.src_to_crop([(K.MOUTH['cx'], K.COLLAR_Y)]); collar_row = float(cpx[0][1])
collar_y = K.unproject(np.array([[K.N / 2, collar_row]]), np.array([K.D]))[0, 1]
def rot_x(a):
    c, s = math.cos(math.radians(a)), math.sin(math.radians(a)); return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
def posed(p, yaw, pitch):
    """The model at the lab's pose: the head (above the collar) pitched about the pivot, then everything turned about y."""
    q = p.copy()
    if pitch:
        w = np.clip((p[:, 1] - collar_y) / 0.06, 0, 1)[:, None]          # the neck's upper part follows the head
        q = p + w * ((p - PIVOT) @ rot_x(pitch).T + PIVOT - p)
    return q @ K.rot_y(yaw).T
def model_mask(yaw, pitch, cell=2):
    px, _ = K.project(posed(pts, yaw, pitch)); ix = np.clip((px / cell).astype(int), 0, K.N // cell - 1)
    m = np.zeros((K.N // cell, K.N // cell), bool); m[ix[:, 1], ix[:, 0]] = True
    m = ndimage.binary_closing(m, iterations=2)
    return np.repeat(np.repeat(m, cell, 0), cell, 1)
def iou_at(mm, pm, s, ox, oy, rows):
    H, W = pm.shape
    yy, xx = np.mgrid[0:H, 0:W]
    mx = (xx - ox) / s; my = (yy - oy) / s
    inside = (mx >= 0) & (mx < K.N - 1) & (my >= 0) & (my < K.N - 1)
    mt = np.zeros_like(pm); mt[inside] = mm[my[inside].astype(int), mx[inside].astype(int)]
    cut = my < rows
    a = pm & cut; b = mt & cut
    return (a & b).sum() / max((a | b).sum(), 1), mt
print('collar: crop row', round(collar_row, 1), 'y', round(collar_y, 4))
for name, yaw, pitch in VIEWS:
    im = Image.open(f'{assets}/{name}').convert('RGBA'); a = np.asarray(im).astype(float); pm = a[..., 3] > 127; pic = a[..., :3]
    best = None
    for yaw_ in ([yaw] if pitch == 0 else [yaw]):
        for pitch_ in ([0.0] if pitch == 0 else [pitch, -pitch]):
            mm = model_mask(yaw_, pitch_)
            prows = np.where(pm.any(1))[0]; top = prows[0]; band = pm[top:top + int((prows[-1] - top) * 0.55)]; pcols = np.where(band.any(0))[0]
            mrows = np.where(mm[:int(collar_row)].any(1))[0]; mcols = np.where(mm[:int(collar_row)].any(0))[0]
            s0 = (pcols[-1] - pcols[0]) / (mcols[-1] - mcols[0]); ox0 = pcols[0] - mcols[0] * s0; oy0 = top - mrows[0] * s0
            rows = collar_row if pitch_ == 0 else collar_row + 40        # the pitched head's chin reaches below the collar row
            f = lambda v: -iou_at(mm, pm, v[0], v[1], v[2], rows)[0]
            r = optimize.minimize(f, [s0, ox0, oy0], method='Nelder-Mead', options=dict(xatol=0.1, fatol=1e-5, maxiter=400))
            iou, mt = iou_at(mm, pm, *r.x, rows)
            print(f'{name} yaw {yaw_:g} pitch {pitch_:g}: s={r.x[0]:.4f} ox={r.x[1]:.1f} oy={r.x[2]:.1f} IoU {iou:.4f}')
            if best is None or iou > best[0]:
                best = (iou, yaw_, pitch_, r.x, mt)
    iou, yaw_, pitch_, v, mt = best
    print(f"  ('{name}', {yaw_:g}, {pitch_:g}, {v[0]:.4f}, {v[1]:.1f}, {v[2]:.1f}),  # IoU {iou:.4f}")
    H, W = pm.shape
    ov = pic.copy(); ov[mt & ~pm] = [255, 0, 0]; ov[pm & ~mt] = [0, 0, 255]
    o = Image.new('RGB', (W * 3 // 2, H // 2)); o.paste(Image.fromarray(pic.astype(np.uint8)).resize((W // 2, H // 2)), (0, 0))
    o.paste(Image.fromarray((mt * 255).astype(np.uint8)).convert('RGB').resize((W // 2, H // 2)), (W // 2, 0)); o.paste(Image.fromarray(ov.astype(np.uint8)).resize((W // 2, H // 2)), (W, 0))
    o.save(f'{out}/fit-{name}')
