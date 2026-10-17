# seethrough.py IMAGE [ROWFRAC=0.35] : see-through at the crown = pixels inside the hair's filled silhouette (upper ROWFRAC of the
# image) that are background-like (luminance > 150, saturation < 0.25) or skin-like; printed as a share of the silhouette.
import sys, numpy as np
from PIL import Image
from scipy import ndimage
path = sys.argv[1]; rowfrac = float(sys.argv[2]) if len(sys.argv) > 2 else 0.35
a = np.array(Image.open(path).convert('RGB')).astype(float); H = a.shape[0]
r, g, b = a[..., 0], a[..., 1], a[..., 2]; mx = a.max(-1); mn = a.min(-1); sat = (mx - mn) / np.maximum(mx, 1); lum = 0.3 * r + 0.59 * g + 0.11 * b
hair = (b > r + 12) & (b > g + 4) & (sat > 0.18) & (mx > 30)
filled = ndimage.binary_fill_holes(ndimage.binary_closing(hair, iterations=6))
band = np.zeros_like(filled); band[:int(H * rowfrac)] = True
sil = filled & band; inner = ndimage.binary_erosion(sil, iterations=3)
bg = (lum > 150) & (sat < 0.25); skin = (r > g) & (g > b) & (r > 150) & (r - b > 40)
print(f"{path.split('/')[-1]:24s} crown silhouette px {int(inner.sum()):7d} | background-like {float((bg & inner).sum() / max(1, inner.sum())):.3f} | skin-like {float((skin & inner).sum() / max(1, inner.sum())):.3f} | hair {float((hair & inner).sum() / max(1, inner.sum())):.3f}")
