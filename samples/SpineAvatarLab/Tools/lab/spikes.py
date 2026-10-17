# spikes.py CAPTURE.png [R=12] [OUT.png] : the hair silhouette's spikes = hair pixels an opening with a disk of radius R removes,
# outside the head (the head's own silhouette dilated 10 px is excluded), per side: top (above the head's top), back/sides.
# Prints the count and the largest blobs; OUT.png marks them red. A spike is a card end or a card edge past the silhouette.
import sys, numpy as np
from PIL import Image
from scipy import ndimage
path = sys.argv[1]; R = int(sys.argv[2]) if len(sys.argv) > 2 else 12; out = sys.argv[3] if len(sys.argv) > 3 else None
im = np.array(Image.open(path).convert('RGB')).astype(int); r, g, b = im[..., 0], im[..., 1], im[..., 2]
hair = (b > r + 10) & (b > g) & (r < 200)
skin = (r > g) & (g > b) & (r > 150) & (r - b > 40)
yy, xx = np.ogrid[-R:R + 1, -R:R + 1]; disk = (xx * xx + yy * yy) <= R * R
opened = ndimage.binary_opening(hair, structure=disk)
spikes = hair & ~opened & ~ndimage.binary_dilation(skin, iterations=10)
lab, n = ndimage.label(spikes); sizes = ndimage.sum(spikes, lab, range(1, n + 1)) if n else np.array([])
big = sizes >= 40; keep = np.isin(lab, np.nonzero(big)[0] + 1)
ys, xs = np.where(hair); top = ys.min() if len(ys) else 0
upper = keep & (np.arange(im.shape[0])[:, None] < top + (ys.max() - top) * 0.35)
print(f'{path.split("/")[-1]}: spike px {int(keep.sum())} in {int(big.sum())} blobs (>= 40 px), of which in the upper third {int(upper.sum())}; largest {sorted((int(s) for s in sizes[big]), reverse=True)[:6]}')
if out:
    vis = im.astype(np.uint8).copy(); vis[keep] = [255, 0, 0]; Image.fromarray(vis).save(out)
