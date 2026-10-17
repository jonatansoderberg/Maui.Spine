# hair_retone.py IN OUT [K_LOW=2.2] [K_HIGH=1.3] [SIGMA=10] : widen the Tripo hair atlas's tonal range toward the
# reference (lab: lum p5/p50/p95 50/72/95 against the reference's 27/68/122). Luminance about the hair's median is
# scaled K_LOW for the broad light and shade (blurred within the texels in use) and K_HIGH for the fine strand detail,
# keeping hue and saturation (colour scaled by the luminance ratio).
import sys, numpy as np
from PIL import Image
from scipy import ndimage
src, dst = sys.argv[1], sys.argv[2]
k_low = float(sys.argv[3]) if len(sys.argv) > 3 else 2.2
k_high = float(sys.argv[4]) if len(sys.argv) > 4 else 1.3
sigma = float(sys.argv[5]) if len(sys.argv) > 5 else 10
im = Image.open(src); mode = im.mode
a = np.asarray(im.convert('RGBA')).astype(np.float32)
rgb, alpha = a[..., :3], a[..., 3]
r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
mx = rgb.max(-1); mn = rgb.min(-1); sat = (mx - mn) / np.maximum(mx, 1)
used = (alpha > 0) & (mx > 8)
hair = used & (b > r + 6) & (sat > 0.15)
lum = 0.3 * r + 0.59 * g + 0.11 * b
m = float(np.median(lum[hair]))
w = used.astype(np.float32)
low = ndimage.gaussian_filter(lum * w, sigma) / np.maximum(ndimage.gaussian_filter(w, sigma), 1e-3)
high = lum - low
new = m + (low - m) * k_low + high * k_high
# a soft floor: the darkest texels (the locks' shadowed insides) went near black at k_low 2.5 and showed as black shapes
# between the locks; below KNEE they compress toward the reference's darkest hair (rendered p5 27-34)
KNEE = 40.0
new = np.where(new < KNEE, KNEE - (KNEE - new) * 0.3, new)
new = np.clip(new, 4, 250)
ratio = np.where(used, new / np.maximum(lum, 1), 1.0)
out = np.clip(rgb * ratio[..., None], 0, 255)
res = np.concatenate([out, alpha[..., None]], -1).astype(np.uint8)
Image.fromarray(res, 'RGBA').convert(mode if mode in ('RGB', 'RGBA') else 'RGBA').save(dst)
L0, L1 = lum[hair], (0.3 * out[..., 0] + 0.59 * out[..., 1] + 0.11 * out[..., 2])[hair]
print(f'median {m:.1f}; texel lum p5/p50/p95 before {np.percentile(L0,[5,50,95]).round(0)} after {np.percentile(L1,[5,50,95]).round(0)}; dark(<60) {np.mean(L0<60):.3f} -> {np.mean(L1<60):.3f}')
