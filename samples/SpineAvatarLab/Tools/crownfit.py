# Fits the owner's generated crown view (the head pitched down, seen from the front) to Kairo's hair envelope by
# silhouette: the pitch about the head's pivot, and the picture's scale and offset against the generator's crop
# camera (picture px = crop px * scale + offset, as the SIDE_VIEWS are fitted). The generator's hair_projector
# then paints the crown from it as a fifth view (the four turntable views see the top of the head at grazing
# angles only, and it drew the fallback's ring pattern).
#   crownfit.py WORKDIR [picture=assets-hair/crown.png] [env=mv-hair/kairo-env.glb] [pivot=0.005,0.053,-0.046] [out=mv-hair/crown-fit.json]
import sys, os, json, math, numpy as np, trimesh
from PIL import Image
from scipy import ndimage, optimize
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kairo_scan as K
work = sys.argv[1]; opts = dict(a.split('=', 1) for a in sys.argv[2:])
pic_path = f"{work}/{opts.get('picture', 'assets-hair/crown.png')}"; env_path = f"{work}/{opts.get('env', 'mv-hair/kairo-env.glb')}"
pivot = np.array([float(v) for v in opts.get('pivot', '0.005,0.053,-0.046').split(',')]); out = f"{work}/{opts.get('out', 'mv-hair/crown-fit.json')}"
g = list(trimesh.load(env_path, force='scene').geometry.values())[0]
V = np.asarray(g.vertices, float) * np.array([-1.0, 1.0, -1.0]); F = np.asarray(g.faces, int)
rng = np.random.default_rng(1); P = np.einsum('bk,fkd->fbd', rng.dirichlet((1, 1, 1), size=4), V[F]).reshape(-1, 3)
pic = np.asarray(Image.open(pic_path).convert('RGB')).astype(float); S = pic.shape[0]
sat = (pic.max(-1) - pic.min(-1)) / (pic.max(-1) + 1e-6); lum = pic @ np.array([0.299, 0.587, 0.114])
hair = (pic[..., 2] > pic[..., 0] + 10) & ((sat > 0.25) | (lum < 135))
hair = ndimage.binary_fill_holes(ndimage.binary_closing(ndimage.binary_opening(hair, iterations=1), iterations=6))
def rot(th):
    c, s = math.cos(th), math.sin(th); return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
def sil(params):
    th, sc, tx, ty = params
    q = pivot + (P - pivot) @ rot(th).T
    px, _ = K.project(q); x = (px[:, 0] * sc + tx).astype(int); y = (px[:, 1] * sc + ty).astype(int)
    ok = (x >= 0) & (x < S) & (y >= 0) & (y < S)
    m = np.zeros((S, S), bool); m[y[ok], x[ok]] = True
    return ndimage.binary_closing(m, iterations=4)
def iou(params):
    m = sil(params); return (m & hair).sum() / max((m | hair).sum(), 1)
ys, xs = np.where(hair); pc = np.array([xs.mean(), ys.mean()]); pw = xs.max() - xs.min()
best = None
for deg in range(15, 61, 5):
    th = math.radians(deg)
    px0, _ = K.project(pivot + (P - pivot) @ rot(th).T); mc = px0.mean(0); mw = px0[:, 0].max() - px0[:, 0].min()
    sc0 = pw / mw; t0 = pc - mc * sc0
    res = optimize.minimize(lambda q: -iou((q[0], q[1], q[2], q[3])), [th, sc0, t0[0], t0[1]], method='Nelder-Mead', options=dict(xatol=1e-3, fatol=1e-4, maxiter=400))
    print(f'start {deg} deg: IoU {-res.fun:.4f} at pitch {math.degrees(res.x[0]):.1f} deg scale {res.x[1]:.4f} offset {res.x[2]:.1f} {res.x[3]:.1f}')
    if best is None or -res.fun > best[0]:
        best = (-res.fun, res.x)
th, sc, tx, ty = best[1]
json.dump({'pitch': math.degrees(th), 'scale': float(sc), 'offset': [float(tx), float(ty)], 'pivot': pivot.tolist(), 'iou': float(best[0]), 'picture': os.path.relpath(pic_path, work),
           'frame': 'model points rotated about the pivot by the pitch (about x), projected by kairo_scan.project; picture px = crop px * scale + offset'}, open(out, 'w'), indent=1)
m = sil(best[1]); o = pic.astype(np.uint8).copy(); edge = m & ~ndimage.binary_erosion(m, iterations=2); o[edge] = (255, 255, 0)
Image.fromarray(o).save(out.replace('.json', '.png'))
print(f'crown fit: IoU {best[0]:.4f} pitch {math.degrees(th):.1f} deg; wrote {out}')
