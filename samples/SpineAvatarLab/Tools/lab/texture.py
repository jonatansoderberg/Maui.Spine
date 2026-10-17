# texture.py IMAGE [REF_HAIR_WIDTH_PX] : the hair's texture grain. The image is scaled so the hair's bounding-box width equals
# REF_HAIR_WIDTH_PX (default: its own, i.e. no scaling), then over the hair mask: the mean gradient magnitude of luminance at
# 1 px (fine grain), at a 3 px Gaussian scale (lock-level tone), their ratio, and the luminance std. Straw = high fine/lock ratio.
import sys, numpy as np
from PIL import Image
from scipy import ndimage
path = sys.argv[1]; refw = float(sys.argv[2]) if len(sys.argv) > 2 else 0
im = Image.open(path).convert('RGB'); a = np.array(im).astype(float)
r, g, b = a[..., 0], a[..., 1], a[..., 2]; mx = a.max(-1); mn = a.min(-1); sat = (mx - mn) / np.maximum(mx, 1)
hair = (b > r + 12) & (b > g + 4) & (sat > 0.18) & (mx > 30); ys, xs = np.where(hair); w = xs.max() - xs.min() + 1
if refw and abs(w - refw) > 2:
    s = refw / w; im = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS); a = np.array(im).astype(float)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]; mx = a.max(-1); mn = a.min(-1); sat = (mx - mn) / np.maximum(mx, 1)
    hair = (b > r + 12) & (b > g + 4) & (sat > 0.18) & (mx > 30)
lum = 0.3 * r + 0.59 * g + 0.11 * b
inner = ndimage.binary_erosion(hair, iterations=4)
gx = ndimage.sobel(lum, axis=1) / 8; gy = ndimage.sobel(lum, axis=0) / 8; fine = np.hypot(gx, gy)[inner].mean()
lb = ndimage.gaussian_filter(lum, 3); gx2 = ndimage.sobel(lb, axis=1) / 8; gy2 = ndimage.sobel(lb, axis=0) / 8; lock = np.hypot(gx2, gy2)[inner].mean()
print(f"{path.split('/')[-1]:24s} hair width px {w:5d} -> {int(np.where(hair)[1].max() - np.where(hair)[1].min() + 1):5d} | fine grad {fine:5.2f} | lock grad (sigma 3) {lock:5.2f} | fine/lock {fine / max(lock, 1e-6):4.2f} | lum std {lum[inner].std():5.1f}")
