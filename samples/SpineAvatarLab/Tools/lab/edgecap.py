# edgecap.py GREEN.png X0 Y0 X1 Y1 : in a flat-green-cap capture, green pixels at the hair's edge (within 6 px of skin or
# background) = cap outside the hair; green enclosed by hair = the cap as backing between strands (counted separately).
import sys, numpy as np
from PIL import Image
from scipy import ndimage
path = sys.argv[1]; x0, y0, x1, y1 = (int(v) for v in sys.argv[2:6])
q = np.array(Image.open(path).convert('RGB')).astype(int)[y0:y1, x0:x1]; r, g, b = q[..., 0], q[..., 1], q[..., 2]
green = (g > 150) & (r < 100) & (b < 100); skin = (r > g) & (g > b) & (r > 150) & (r - b > 40); bg = (q.min(-1) > 215) & (q.max(-1) - q.min(-1) < 25)
edge = green & ndimage.binary_dilation(skin | bg, iterations=6)
lab, n = ndimage.label(edge); sizes = sorted((int(s) for s in ndimage.sum(edge, lab, range(1, n + 1))), reverse=True) if n else []
print(f'{path.split("/")[-1]}: green {int(green.sum())} px, at the hair edge {int(edge.sum())} px in {n} blobs {sizes[:5]}')
