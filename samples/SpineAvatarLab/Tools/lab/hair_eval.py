# hair_eval.py EVAL_DIR CANDIDATE... : every candidate's lab renders (EVAL_DIR/<cand>/<cand>-y<yaw>.png, yaws 0 +-30 +-60 +-90 180)
# against the owner's reference views (kairo/tools/ref: front = yaw 0, left = 90, right = -90, back = 180).
# Shape: hair-mask IoU after aligning both on the head (hair + skin) by its top and width.
# Colour: |dark share - ref| and |median lum - ref|.
# Defects over all 8 yaws: holes (non-hair inside the hair's closed outline), near-black hair (lum < 28),
# hair over the eyes (front, eye band, not the irises).
import sys, os, numpy as np
from PIL import Image
from scipy import ndimage

REF = os.path.expanduser('~/.cache/spine-avatar-lab/kairo/tools/ref')
PAIRS = {'0': 'kairo-front.jpg', '90': 'left.jpg', '-90': 'right.jpg', '180': 'back.jpg'}
YAWS = ['0', '30', '-30', '60', '-60', '90', '-90', '180']


def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(float)


def masks(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]; mx = a.max(-1); mn = a.min(-1); sat = (mx - mn) / np.maximum(mx, 1)
    hair = (b > r + 12) & (b > g + 4) & (sat > 0.18) & (mx > 30)
    skin = (r > g + 8) & (g > b) & (sat > 0.12) & ~hair
    return hair, skin


def norm(hair, skin, size=400):
    head = hair | skin
    ys, xs = np.where(head); x0, x1, y0 = xs.min(), xs.max(), ys.min()
    # the head's width at the hair's widest rows, the top of the hair: crop a square of 1.6 x width from the top
    w = x1 - x0 + 1; h = int(1.4 * w)
    crop = hair[y0:y0 + h, x0:x1 + 1]
    im = Image.fromarray((crop * 255).astype('uint8')).resize((size, int(size * crop.shape[0] / crop.shape[1])), Image.BILINEAR)
    out = np.zeros((int(size * 1.4), size), bool); a = np.array(im) > 127; out[:min(a.shape[0], out.shape[0])] = a[:out.shape[0]]
    return out


def colour(a, hair):
    L = (a @ [0.299, 0.587, 0.114])[hair]
    return float(np.mean(L < 60)), float(np.median(L))


refs = {}
for y, f in PAIRS.items():
    a = load(f'{REF}/{f}'); h, s = masks(a); refs[y] = (norm(h, s), colour(a, h))

print(f"{'candidate':12s} {'IoU front':>9s} {'left':>6s} {'right':>6s} {'back':>6s} {'mean':>6s} | {'dark d':>6s} {'lum d':>6s} | {'holes':>7s} {'black':>7s} {'eyes':>6s}")
for cand in sys.argv[2:]:
    d = f'{sys.argv[1]}/{cand}'
    ious = []; dd = []; ld = []
    holes = black = 0
    for y in YAWS:
        a = load(f'{d}/{cand}-y{y}.png'); h, s = masks(a)
        if y in PAIRS:
            n = norm(h, s); ref, (rdark, rlum) = refs[y]
            ious.append((n & ref).sum() / max((n | ref).sum(), 1))
            dk, lm = colour(a, h); dd.append(abs(dk - rdark)); ld.append(abs(lm - rlum))
        closed = ndimage.binary_closing(h, iterations=8)
        holes += int((closed & ~h & ~ndimage.binary_erosion(s, iterations=2)).sum() - 0)
        lum = a @ [0.299, 0.587, 0.114]
        black += int((ndimage.binary_dilation(h, iterations=2) & (lum < 28)).sum())
    a = load(f'{d}/{cand}-y0.png'); band = a[880:1080, 780:1660]; r, g, b = band[..., 0], band[..., 1], band[..., 2]
    hb, _ = masks(band); iris = (b > r + 40) & (b > g + 30) & (b > 120)
    eyes = int((hb & ~ndimage.binary_dilation(iris, iterations=6)).sum())
    print(f"{cand:12s} {ious[0]:9.3f} {ious[1]:6.3f} {ious[2]:6.3f} {ious[3]:6.3f} {np.mean(ious):6.3f} | {np.mean(dd):6.3f} {np.mean(ld):6.1f} | {holes:7d} {black:7d} {eyes:6d}")
