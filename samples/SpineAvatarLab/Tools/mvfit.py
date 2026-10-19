# Aligns a Pixal3D multiview GLB to the frame of the haired single-view scan (kairo-s42.glb, the base
# frame compose() works in). Both come from the same front photo (the multiview run's frame 0 is the
# photo's crop), so their front silhouettes must coincide: scale and the x/y translation come from the
# front silhouettes above the collar (IoU maximised through the generator's own camera, project()),
# the depth translation from the faces' front-most surfaces. (ICP on the face region alone, used
# first, put the hair 27 px to the side and 11 % too small: the reconstruction's face is proportioned
# differently from the scan's.)
#   mvfit.py IN.glb OUT.glb [ref=kairo-s42.glb]
# Both meshes are read as load_scan reads them (trimesh, turned by [-1, 1, -1]: +z toward the camera).
import sys, os, json, numpy as np, trimesh
from scipy import ndimage, optimize
from scipy.spatial import cKDTree
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kairo_scan as K
src, dst = sys.argv[1], sys.argv[2]; opts = dict(a.split('=', 1) for a in sys.argv[3:])
W = opts.get('work', os.path.dirname(os.path.dirname(os.path.abspath(src)))); ref = opts.get('ref', f'{W}/kairo-s42.glb'); COLLAR = float(opts.get('collar', -0.156))
turn = np.array([-1.0, 1.0, -1.0])
def load(path):
    g = list(trimesh.load(path, force='scene').geometry.values())[0]
    return g, np.asarray(g.vertices, float) * turn, np.asarray(g.faces, int)
gm, Vm, Fm = load(src); gr, Vr, Fr = load(ref)
print('mv bounds', Vm.min(0).round(3), Vm.max(0).round(3), '| ref bounds', Vr.min(0).round(3), Vr.max(0).round(3))
rng = np.random.default_rng(1); bary = rng.dirichlet((1, 1, 1), size=6)
Pm = np.einsum('bk,fkd->fbd', bary, Vm[Fm]).reshape(-1, 3); Pr = np.einsum('bk,fkd->fbd', bary, Vr[Fr]).reshape(-1, 3)
CELL = 2; G = K.N // CELL
def sil(pts):
    px, _ = K.project(pts); ix = np.clip((px / CELL).astype(int), 0, G - 1)
    m = np.zeros((G, G), bool); m[ix[:, 1], ix[:, 0]] = True
    return ndimage.binary_closing(m, iterations=1)
ref_sil = sil(Pr[Pr[:, 1] > COLLAR])
def iou(params):
    s, tx, ty, tz = params
    p = s * Pm + np.array([tx, ty, tz])
    m = sil(p[p[:, 1] > COLLAR])
    return (m & ref_sil).sum() / max((m | ref_sil).sum(), 1)
def box(m):
    y, x = np.where(m); return x.min(), x.max(), y.min(), y.max(), x.mean(), y.mean()
m0 = sil(Pm[Pm[:, 1] > COLLAR]); b0, br = box(m0), box(ref_sil)
s0 = (br[1] - br[0]) / (b0[1] - b0[0])
zf = np.percentile(Pm[:, 2], 99); unit = K.project(np.array([[0, 0, zf], [1, 0, zf], [0, 1, zf]]))[0]
px_per_x = (unit[1] - unit[0])[0]; px_per_y = (unit[2] - unit[0])[1]
tx0 = ((br[4] - b0[4] * s0) * CELL) / px_per_x; ty0 = ((br[5] - b0[5] * s0) * CELL) / px_per_y
tz0 = np.percentile(Pr[:, 2], 99.5) - s0 * zf
print('initial: scale', round(s0, 4), 'translation', np.round([tx0, ty0, tz0], 4), 'IoU', round(iou((s0, tx0, ty0, tz0)), 4))
res = optimize.minimize(lambda q: -iou((q[0], q[1], q[2], tz0)), [s0, tx0, ty0], method='Nelder-Mead', options=dict(xatol=1e-4, fatol=1e-5, maxiter=300))
s, tx, ty = res.x
nose_m = np.percentile((s * Pm)[:, 2], 99.9); nose_r = np.percentile(Pr[:, 2], 99.9); tz = nose_r - nose_m
t = np.array([tx, ty, tz])
final = iou((s, tx, ty, tz))
print(f'fit: scale {s:.4f} translation {np.round(t, 4)} front silhouette IoU {final:.4f}')
def face(V):
    z_front = np.percentile(V[:, 2], 99.5); w = V[:, 0].max() - V[:, 0].min(); cx = (V[:, 0].max() + V[:, 0].min()) / 2
    h0, h1 = V[:, 1].min(), V[:, 1].max()
    m = (V[:, 2] > z_front - 0.06) & (np.abs(V[:, 0] - cx) < 0.2 * w) & (V[:, 1] > h0 + 0.45 * (h1 - h0)) & (V[:, 1] < h0 + 0.8 * (h1 - h0))
    return V[m]
d = cKDTree(face(Vr)).query(s * face(Vm) + t)[0]
print(f'face residual (mm) p50 {np.median(d) * 1000:.1f} p90 {np.percentile(d, 90) * 1000:.1f}')
gm.vertices = ((s * Vm + t) * turn)
trimesh.Scene(gm).export(dst)
json.dump({'scale': float(s), 'translation': [float(v) for v in t], 'front_iou': float(final), 'source': os.path.abspath(src),
           'frame': 'turned ([-1, 1, -1] on the GLB axes): scan = scale * mv + translation'}, open(os.path.join(os.path.dirname(os.path.abspath(dst)), 'fit.json'), 'w'), indent=1)
print('wrote', dst, 'and fit.json')
