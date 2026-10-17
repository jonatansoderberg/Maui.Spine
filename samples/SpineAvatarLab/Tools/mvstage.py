# Stages four views for Pixal3D's multiview cascade (trellis-cli --views): one common square crop
# (the front view's alpha box, grown to put the subject at ~85 % of the frame), the same scale for all,
# a flat-background matte with a feathered edge, the canonical turntable transforms.json.
#   mvstage.py OUTDIR front.png right.png back.png left.png [size=1024] [tol=22]
# Pixal3D's canonical rig (transforms_json.cpp): front camera at -y looking +y, "right (azim 90)" at +x,
# back at +y, "left (azim 270)" at -x, z up. Right/left name the CAMERA's side: the camera at +x sees the
# subject's LEFT side with her nose pointing to the image's left. So right.png = the profile whose face
# points LEFT, left.png = the profile whose face points RIGHT. (The first run had them mirrored and
# reconstructed the face on the back of the head.)
import sys, os, json, numpy as np
from PIL import Image
from scipy import ndimage
out, paths = sys.argv[1], sys.argv[2:6]; opts = dict(a.split('=', 1) for a in sys.argv[6:])
SIZE = int(opts.get('size', 1024)); TOL = float(opts.get('tol', 22)); FILL = float(opts.get('fill', 0.85))
os.makedirs(out, exist_ok=True)
def matte(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(float)
    corners = np.concatenate([a[:30, :30].reshape(-1, 3), a[:30, -30:].reshape(-1, 3), a[-30:, :30].reshape(-1, 3), a[-30:, -30:].reshape(-1, 3)])
    bg = np.median(corners, 0)
    d = np.abs(a - bg).max(-1)
    # The background is what is near the backdrop's colour AND reaches the frame's border: the knit's
    # shaded folds are nearly as grey as the backdrop, but they are enclosed by the subject.
    near = d <= TOL
    lab, n = ndimage.label(near)
    border = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    bgm = np.isin(lab, border[border > 0])
    fg = ~bgm
    fg = ndimage.binary_opening(fg, iterations=2)
    lab, n = ndimage.label(fg)
    if n > 1:
        sizes = ndimage.sum(fg, lab, range(1, n + 1)); fg = lab == (1 + int(np.argmax(sizes)))
    fg = ndimage.binary_fill_holes(fg)
    alpha = ndimage.gaussian_filter(fg.astype(float), 1.0)
    # the edge keeps its own colour, not the background's: pull the border pixels toward the inside
    _, (iy, ix) = ndimage.distance_transform_edt(~ndimage.binary_erosion(fg, iterations=2), return_indices=True)
    rgb = np.where(ndimage.binary_erosion(fg, iterations=2)[..., None], a, a[iy, ix])
    return rgb, alpha, fg
views = [matte(p) for p in paths]
h, w = views[0][2].shape
ys, xs = np.where(views[0][2])
# the common box: the front's subject height sets the scale; centred on the subject's x, the box's top
# just above the head (the bust is cut by the frame's bottom, so the box hangs from the top)
top, bottom = ys.min(), ys.max(); cx = (xs.min() + xs.max()) / 2
# every view's subject should agree in height; report the spread
for p, v in zip(paths, views):
    yy, xx = np.where(v[2]); print(os.path.basename(p), 'subject rows', yy.min(), yy.max(), 'cols', xx.min(), xx.max())
height = bottom - top
side = int(round(height / FILL))
y0 = int(top - (side - height) / 2); x0 = int(round(cx - side / 2))
print('crop box', x0, y0, side, 'of', w, h)
names = ['00_front.png', '01_right.png', '02_back.png', '03_left.png']; azimuths = (0, 90, 180, 270)
def crop(img, fillv):
    H, W = img.shape[:2]
    canvas = np.full((side, side) + img.shape[2:], fillv, img.dtype)
    sx0, sy0 = max(x0, 0), max(y0, 0); sx1, sy1 = min(x0 + side, W), min(y0 + side, H)
    canvas[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = img[sy0:sy1, sx0:sx1]
    return canvas
for name, (rgb, alpha, fg) in zip(names, views):
    c = crop(rgb, 0.0); al = crop(alpha, 0.0)
    im = Image.fromarray(np.dstack([np.clip(c, 0, 255), np.clip(al * 255, 0, 255)]).astype(np.uint8), 'RGBA').resize((SIZE, SIZE), Image.LANCZOS)
    im.save(os.path.join(out, name))
# the canonical rig: elevation 0, FOV 20 deg, distance 3.119, azimuths 0/90/180/270 (camera-to-world, Blender/NeRF)
def cam(az_deg, dist=3.119):
    az = np.radians(az_deg)
    pos = np.array([dist * np.sin(az), -dist * np.cos(az), 0.0])      # azimuth 0 in front (-y), turning about z
    fwd = -pos / np.linalg.norm(pos); up = np.array([0, 0, 1.0]); right = np.cross(fwd, up); up2 = np.cross(right, fwd)
    M = np.eye(4); M[:3, 0] = right; M[:3, 1] = up2; M[:3, 2] = -fwd; M[:3, 3] = pos
    return M.tolist()
tj = {"camera_angle_x": 0.34906584, "mesh_scale": 1.0,
      "frames": [{"file_path": n, "transform_matrix": cam(az)} for n, az in zip(names, azimuths)]}
json.dump(tj, open(os.path.join(out, 'transforms.json'), 'w'), indent=1)
print('staged', out, 'subject height share', round(height / side, 3))
