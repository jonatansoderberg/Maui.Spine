# rasterize_cap.py DATA.npz OUT.png [--size 1024] : the undercoat strands drawn over a dark base in the cap's spherical UV
# (u = (atan2(x, -y) + pi) / 2pi about the pivot, v = 1 - phi/pi), opaque. Deterministic.
import sys, math, numpy as np
from PIL import Image, ImageDraw, ImageFilter
args = sys.argv[1:]; data = np.load(args[0]); out = args[1]
N = int(args[args.index('--size') + 1]) if '--size' in args else 1024
SS = 3
pos, offs, rnd, piv = data['pos'], data['offs'], data['rnd'], data['piv']
BASE = (30, 33, 92); C0 = np.array([0.15, 0.17, 0.44]); C1 = np.array([0.40, 0.44, 0.74])   # the hair's own dark tones: a black cap read as a gash at the parting
def ramp(r): r = r ** 1.6; return C0 * (1 - r) + C1 * r
img = Image.new('RGB', (N * SS, N * SS), BASE); d = ImageDraw.Draw(img)
dd = pos - piv[None, :]
th = np.arctan2(dd[:, 0], -dd[:, 1]); u = (th + math.pi) / (2 * math.pi)
r = np.linalg.norm(dd, axis=1); ph = np.arccos(np.clip(dd[:, 2] / np.maximum(r, 1e-9), -1, 1)); v = 1 - ph / math.pi
x = u * N * SS; y = (1 - v) * N * SS
drawn = 0
for si in range(len(offs) - 1):
    a, b = offs[si], offs[si + 1]
    if b - a < 2: continue
    col = ramp(float(rnd[si])); n = b - a
    for j in range(a, b - 1):
        if abs(u[j] - u[j + 1]) > 0.5: continue
        t = (j - a) / max(1, n - 1)
        shade = 0.8 + 0.2 * t; rgb = tuple(int(255 * min(1, c * shade)) for c in col)
        w = max(1, int(round((3.2 - 1.6 * t) * SS)))   # coarser roots: the cap reads as combed hair along the parting, not a smooth sheet
        d.line([(x[j], y[j]), (x[j + 1], y[j + 1])], fill=rgb, width=w)
    drawn += 1
small = img.resize((N, N), Image.BOX).filter(ImageFilter.GaussianBlur(0.4))
small.save(out, optimize=True)
print("cap texture", N, "strands", drawn, "bytes", __import__('os').path.getsize(out))
