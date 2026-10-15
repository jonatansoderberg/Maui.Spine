# The hair's smooth envelope from the fitted multiview reconstruction: its hair faces (by texture colour,
# above the collar) voxelised, closed, run through marching cubes, smoothed and decimated; written as a
# GLB in the scan's convention (y up, -z forward: load_scan turns it) with a uniform hair-blue texture
# so compose() classes every face as hair. The generator's projector (KAIRO_HAIR=proj) paints it from
# the owner's generated views.
#   hairenv.py MVDIR [res=0.003] [close=2] [faces=40000] [smooth=3] [hull=right,back,left] [hull_px=8] [front_px=8] [out=MVDIR/kairo-env.glb]
#   (hull_px / front_px < 0: the envelope ends that many px inside the views' hair silhouettes everywhere; inset= / inset_front= : only
#    where the silhouette is ragged (strand tips, gaps), blending from hull_px / front_px where it is smooth. The hair cards draw the outline there.)
#   reads MVDIR/kairo-mv-fit.glb (mvfit.py), writes MVDIR/kairo-env.glb + kairo-env.png (hull= empty: no carving, P35's envelope)
import sys, os, numpy as np, trimesh
from PIL import Image
from scipy import ndimage
from skimage import measure
import pymeshlab
D = sys.argv[1]; opts = dict(a.split('=', 1) for a in sys.argv[2:])
WORK = os.path.dirname(os.path.abspath(D.rstrip('/')))        # the kairo work dir (the photo, the scans) is MVDIR's parent
OUT = opts.get('out', f'{D}/kairo-env.glb')
HULL = [v for v in opts.get('hull', 'right,back,left').split(',') if v]   # views whose hair silhouettes carve the closed volume
HULL_PX = int(opts.get('hull_px', 8))       # px of the views (1452 px, 3.6 px per voxel) the side and back hair masks are grown by before carving: the alpha trims the rest
FRONT_PX = int(opts.get('front_px', 4))      # source px of the photo its hair mask is grown by before the front carve (below); 0 = no front carve
INSET = int(opts.get('inset', 0))             # px the envelope ends inside the side and back views' silhouettes where they are ragged (0: hull_px everywhere)
INSET_FRONT = int(opts.get('inset_front', 0)) # the same for the photo's silhouette in the front carve
RAGGED = (float(opts.get('ragged_lo', 0.06)), float(opts.get('ragged_hi', 0.14)))   # the ragged measure's range that blends hull_px into the inset
SECTOR = (0.5, 0.85)   # cosine between a voxel's horizontal direction from the volume's centre and the view's axis over which the view's inset comes in (a corner between two views was inset by both and the hair cards had a 5 mm solid band to draw there)
HULL_FILL_PX = int(opts.get('hull_fill_px', 600))   # px: a smaller hole in a side or back view's hair is a gap between strands and is filled before carving
HULL_CLOSE = int(opts.get('hull_close', 8))         # px the views' hair masks are closed by before carving (gaps between strands)
HULL_SMOOTH = float(opts.get('hull_smooth', 4))     # px of blur on the closed masks before carving (the strand tips' saw-tooth rounded)
RES = float(opts.get('res', 0.003)); CLOSE = int(opts.get('close', 2)); FACES = int(opts.get('faces', 40000)); SMOOTH = int(opts.get('smooth', 3))
COLLAR_Y = float(opts.get('collar', -0.156))
turn = np.array([-1.0, 1.0, -1.0])
def ragged_weight(sil, hairpx):
    """0..1 per px: how ragged the hair's outline is there, as the share of the smoothed silhouette that differs
    from the view's own hair within 30 px of the silhouette's edge, blurred over 12 px (strand tips and the gaps
    between them score high, the crown's smooth outline near 0)."""
    edge = ndimage.binary_dilation(sil, iterations=30) & ~ndimage.binary_erosion(sil, iterations=30)
    r = ndimage.gaussian_filter(((sil ^ hairpx) & edge).astype(float), 12)
    return np.clip((r - RAGGED[0]) / (RAGGED[1] - RAGGED[0]), 0, 1)
def offset_fields(sil, hairpx):
    """Per px: the signed distance outside the silhouette and the ragged weight."""
    return ndimage.distance_transform_edt(~sil) - ndimage.distance_transform_edt(sil), ragged_weight(sil, hairpx)
def outside(signed_px, w_px, sector, grow, inset):
    """True for voxels outside a silhouette grown by `grow` px where its outline is smooth and eroded by `inset` px
    where it is ragged, the inset weighted by the voxel's `sector` (0..1: how squarely the view faces it)."""
    ws = w_px * sector
    return signed_px > grow * (1 - ws) - inset * ws
g = list(trimesh.load(f'{D}/kairo-mv-fit.glb', force='scene').geometry.values())[0]
V = np.asarray(g.vertices, float) * turn; F = np.asarray(g.faces, int)
tex = np.asarray(g.visual.material.baseColorTexture.convert('RGB')).astype(float); uv = np.asarray(g.visual.uv, float)
c = uv[F].mean(1); x = np.clip((c[:, 0] * tex.shape[1]).astype(int), 0, tex.shape[1] - 1); y = np.clip(((1 - c[:, 1]) * tex.shape[0]).astype(int), 0, tex.shape[0] - 1)
col = tex[y, x]; lum = col @ np.array([0.299, 0.587, 0.114])
fc = V[F].mean(1)
hair = ((col[:, 2] > col[:, 0] + 10) & (lum < 115)) & (fc[:, 1] > COLLAR_Y - 0.03)
# Near the front the photo decides: faces turned to the camera within 8 cm of the front are hair where
# the photo shows hair and not where it shows skin (the reconstruction's face is wider than the
# photo's, and by its texture the hair over the temples was skin: the envelope had a hole there and
# the face grid lay over the hair).
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kairo_scan as K
photo = np.asarray(Image.open(f'{WORK}/kairo-front.png').convert('RGB')).astype(float)
ph = (photo[..., 2] > photo[..., 0] + 12) | (photo.sum(-1) < 150)
ph = ndimage.binary_opening(ph, iterations=1)
skin_px = ndimage.binary_erosion((photo[..., 0] > photo[..., 2] + 30) & (photo.sum(-1) > 200), iterations=3)
fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]]); fn /= np.linalg.norm(fn, axis=1, keepdims=True) + 1e-12
cp, cz = K.project(fc); sp = np.clip(((cp - np.array([K.OX, K.OY])) / K.S).astype(int), 0, photo.shape[0] - 1)
z_front = np.percentile(fc[:, 2], 99.5)
# Any face in the front 12 cm whose projection lands on the photo's hair is hair, whichever way it
# faces (the temples' side-facing faces were left to the reconstruction's texture, which paints the
# temple as skin, and the hole stayed); only faces turned to the camera over the photo's skin are not.
band = (fc[:, 2] > z_front - 0.20) & (fc[:, 1] > COLLAR_Y - 0.03)
front = band & (np.abs(fn[:, 2]) > 0.3)
# The photo's hair rule takes the blue irises and the dark brows too: the face mask (eyes, brows and
# mouth forced in) is never hair, or the envelope grew eyeball-shaped bumps textured with the irises.
fm = K.face_mask_src(Image.open(f'{WORK}/kairo-front.png').convert('RGB'))
ph = ph & ~fm
on_hair = ndimage.binary_dilation(ph, iterations=2)[sp[:, 1], sp[:, 0]]; on_skin = (skin_px | fm)[sp[:, 1], sp[:, 0]]
print('band faces', int(band.sum()), 'made hair by the photo', int((band & on_hair & ~hair).sum()), 'unmade by the photo', int((front & on_skin & hair).sum()))
hair = (hair | (band & on_hair)) & ~(front & on_skin)
print('mv faces', len(F), 'hair faces', int(hair.sum()))
# The front comes from the single-view scan as well: its hair strands over the forehead and the temples
# lie where the photo shows them (the reconstruction's front opening is wider: 27 % of the photo's hair
# near the face had no envelope surface in front of the head, the scan covers 97 %). Scan hair faces in
# the front 15 cm whose projection lands on the photo's hair join the volume.
gs = list(trimesh.load(f'{WORK}/kairo-s42.glb', force='scene').geometry.values())[0]
Vs = np.asarray(gs.vertices, float) * turn; Fs_ = np.asarray(gs.faces, int)
texs = np.asarray(gs.visual.material.baseColorTexture.convert('RGB')).astype(float); uvs = np.asarray(gs.visual.uv, float)
cs = uvs[Fs_].mean(1); xs_ = np.clip((cs[:, 0] * texs.shape[1]).astype(int), 0, texs.shape[1] - 1); ys_ = np.clip(((1 - cs[:, 1]) * texs.shape[0]).astype(int), 0, texs.shape[0] - 1)
cols = texs[ys_, xs_]; lums = cols @ np.array([0.299, 0.587, 0.114]); fcs = Vs[Fs_].mean(1)
scan_hair = ((cols[:, 2] > cols[:, 0] + 10) & (lums < 115)) & (fcs[:, 1] > COLLAR_Y - 0.02)
cps, _ = K.project(fcs); sps = np.clip(((cps - np.array([K.OX, K.OY])) / K.S).astype(int), 0, photo.shape[0] - 1)
zfs = np.percentile(fcs[:, 2], 99.5)
# By the photo alone, not the scan's texture colour: the lock beside her right eye carries light
# highlights (luminance over 115) and was not 'hair' by colour, so the hair ended chopped there.
scan_front = (fcs[:, 1] > COLLAR_Y - 0.02) & (fcs[:, 2] > zfs - 0.20) & ndimage.binary_dilation(ph, iterations=2)[sps[:, 1], sps[:, 0]]
print('scan hair faces joining the front', int(scan_front.sum()))
P = np.concatenate([V[F[hair]].reshape(-1, 3), Vs[Fs_[scan_front]].reshape(-1, 3)])
lo = P.min(0) - 0.03; shape = np.ceil((P.max(0) + 0.03 - lo) / RES).astype(int) + 1
rng = np.random.default_rng(5)
pts = np.concatenate([np.einsum('k,fkd->fd', w, V[F[hair]]) for w in rng.dirichlet((1, 1, 1), size=24)]
                     + [np.einsum('k,fkd->fd', w, Vs[Fs_[scan_front]]) for w in rng.dirichlet((1, 1, 1), size=24)])
n_mv = 24 * int(hair.sum())
occ = np.zeros(shape, bool); i = ((pts[:n_mv] - lo) / RES).astype(int); occ[i[:, 0], i[:, 1], i[:, 2]] = True
# The scan's front strands are sheets one voxel thick: the isosurface's blur and the smoothing passes
# dissolved them, and the hairline stayed where the reconstruction put it (coverage of the photo's hair
# near the face 0.72 with or without them). Thickened to five voxels before the closing they survive.
occ_s = np.zeros(shape, bool); j = ((pts[n_mv:] - lo) / RES).astype(int); occ_s[j[:, 0], j[:, 1], j[:, 2]] = True
occ |= ndimage.binary_dilation(occ_s, iterations=int(opts.get('front_thick', 2)))
occ = ndimage.binary_closing(occ, structure=ndimage.generate_binary_structure(3, 1), iterations=CLOSE)
print('voxels', shape, 'occupied', int(occ.sum()))
if HULL:
    # The visual hull of the owner's side and back views: a voxel of the closed volume that one of them
    # sees outside its hair silhouette (grown by HULL_PX) goes, on that view's own side of the head. The
    # closing bridges the concavity between the bob's side and the cheek in front of the ear, and the
    # thickened front strands stand as a slab round each lock at the temples; the views then cut all of
    # that strand by strand in the texture, and from three quarters the cut surface read as flakes over
    # the cheek. Carved here, the envelope ends where the views' hair ends and the alpha only trims tips.
    # (Not the front: the skirt over the forehead must stay for the alpha to draw the hairline on.)
    import json
    fit = json.load(open(f'{D}/fit.json')); s_, t_ = fit['scale'], np.array(fit['translation'])
    VD = f'{D}/views1452' if os.path.isdir(f'{D}/views1452') else D
    meta = json.load(open(f'{VD}/transforms.json')); fov = meta['camera_angle_x']
    idx = np.argwhere(occ); m = ((idx * RES + lo) - t_) / s_
    W = np.stack([m[:, 0], -m[:, 2], m[:, 1]], 1)        # the turned frame -> Pixal3D's world, as the generator's projector maps it
    ctr = W.mean(0); carve = np.zeros(len(idx), bool)
    for fr in meta['frames']:
        name = [v for v in ('front', 'right', 'back', 'left') if v in os.path.basename(fr['file_path'])]
        if not name or name[0] not in HULL:
            continue
        im = Image.open(f"{VD}/{fr['file_path']}").convert('RGBA'); S_ = im.size[0]
        a = np.asarray(im).astype(float); rgb, alpha = a[..., :3], a[..., 3] / 255.0
        sat = (rgb.max(-1) - rgb.min(-1)) / (rgb.max(-1) + 1e-6); lum = rgb @ np.array([0.299, 0.587, 0.114])
        hairpx = (rgb[..., 2] > rgb[..., 0] + 10) & ((sat > 0.25) | (lum < 135)) & (alpha > 0.5)
        hairpx = ndimage.binary_opening(hairpx, iterations=1)
        # The silhouette, not the strands: gaps between the view's strands (a closing, then enclosed holes under
        # HULL_FILL_PX) are filled so the carve trims the outline and never bores a pinhole through the volume;
        # the ear, a larger hole in the side views' hair, still carves its pocket.
        sil = ndimage.binary_closing(hairpx, iterations=HULL_CLOSE)
        sil = ndimage.gaussian_filter(sil.astype(float), HULL_SMOOTH) > 0.5     # the strand tips' saw-tooth rounded
        holes_, _ = ndimage.label(~sil); hsz_ = np.bincount(holes_.ravel()); small_ = hsz_ < HULL_FILL_PX; small_[0] = False
        sil = sil | small_[holes_]
        c2w = np.array(fr['transform_matrix'], float); w2c = np.linalg.inv(c2w); focal = (S_ / 2.0) / np.tan(fov / 2.0)
        pc = W @ w2c[:3, :3].T + w2c[:3, 3]; dist = -pc[:, 2]
        x = focal * pc[:, 0] / (dist + 1e-8) + S_ / 2.0; y = -focal * pc[:, 1] / (dist + 1e-8) + S_ / 2.0
        inside = (x >= 0) & (x < S_) & (y >= 0) & (y < S_) & (dist > 0)
        cam = c2w[:3, 3]; axis_ = cam / np.linalg.norm(cam); rel = W - ctr; near = (rel @ axis_) > 0
        yi_, xi_ = np.clip(y.astype(int), 0, S_ - 1), np.clip(x.astype(int), 0, S_ - 1)
        if INSET > 0:
            signed_, w_ = offset_fields(sil, hairpx)
            relh = rel.copy(); relh[:, 2] = 0; axh = axis_.copy(); axh[2] = 0      # horizontal: the turntable's cameras ring the vertical axis (W[:, 2] is up)
            sec_ = np.clip(((relh / (np.linalg.norm(relh, axis=1, keepdims=True) + 1e-9)) @ (axh / (np.linalg.norm(axh) + 1e-9)) - SECTOR[0]) / (SECTOR[1] - SECTOR[0]), 0, 1)
            out = inside & outside(signed_[yi_, xi_], w_[yi_, xi_], sec_, HULL_PX, INSET)
        else:
            keep = ndimage.distance_transform_edt(~sil) <= HULL_PX if HULL_PX >= 0 else ndimage.distance_transform_edt(sil) > -HULL_PX
            out = inside & ~keep[yi_, xi_]
        print('hull view', name[0], 'carves', int((near & out).sum()), 'voxels')
        carve |= near & out
    occ[idx[carve, 0], idx[carve, 1], idx[carve, 2]] = False
    print('hull: voxels carved', int(carve.sum()), 'of', len(idx), '-> occupied', int(occ.sum()))
if FRONT_PX:
    # The photo carves too, in the generator's own camera: a voxel whose pixel shows no hair (grown by FRONT_PX)
    # and which lies in front of the bald head's surface there (or where there is no head at all) goes. That is
    # the bob's sides bulging past the photo's outline at the ears' height (the owner's 'klump') and the hair
    # over the ears the photo shows bare; the hair behind the head on the same rays stays, and so does the
    # strands' skirt inside the face's oval, where the alpha mask draws the hairline (the ears lie outside it).
    idx = np.argwhere(occ); cen = idx * RES + lo
    pxv, zcv = K.project(cen); spv = ((pxv - np.array([K.OX, K.OY])) / K.S).astype(int)
    inside = (spv >= 0).all(1) & (spv < photo.shape[0]).all(1)
    if INSET_FRONT > 0:
        sil_f = ndimage.gaussian_filter(ndimage.binary_closing(ph, iterations=HULL_CLOSE).astype(float), HULL_SMOOTH) > 0.5
        signed_f, w_f = offset_fields(sil_f, ph)
        keep_px = None
    elif FRONT_PX >= 0:
        keep_px = ndimage.distance_transform_edt(~ph) <= FRONT_PX
    else:
        # inset: the photo's hair closed and smoothed as the hull views' silhouettes are, then eroded
        sil_f = ndimage.gaussian_filter(ndimage.binary_closing(ph, iterations=HULL_CLOSE).astype(float), HULL_SMOOTH) > 0.5
        keep_px = ndimage.distance_transform_edt(sil_f) > -FRONT_PX
    oval = ndimage.binary_dilation(fm, iterations=10)
    rb_ = np.random.default_rng(3).dirichlet((1, 1, 1), size=12)
    gb_ = list(trimesh.load(f'{WORK}/bald/kairo-bald-s42.glb', force='scene').geometry.values())[0]
    Vb_ = K.reframe(np.asarray(gb_.vertices, float) * turn, K.OY - K.BALD_OY); Fb_ = np.asarray(gb_.faces, int)
    pb_ = np.einsum('bk,fkd->fbd', rb_, Vb_[Fb_]).reshape(-1, 3); pxb_, zcb_ = K.project(pb_)
    CELL_ = 2; G_ = K.N // CELL_; ib_ = np.clip((pxb_ / CELL_).astype(int), 0, G_ - 1)
    zh_ = np.full((G_, G_), np.inf); np.minimum.at(zh_, (ib_[:, 1], ib_[:, 0]), zcb_)
    iv_ = np.clip((pxv / CELL_).astype(int), 0, G_ - 1)
    front_of_head = zcv < zh_[iv_[:, 1], iv_[:, 0]] + 0.01
    sp_ = np.clip(spv, 0, photo.shape[0] - 1)
    if keep_px is None:
        rel_f = cen - cen.mean(0); rel_f[:, 1] = 0                      # horizontal (y up in the scan's frame)
        sec_f = np.clip(rel_f[:, 2] / (np.linalg.norm(rel_f, axis=1) + 1e-9) - SECTOR[0], 0, SECTOR[1] - SECTOR[0]) / (SECTOR[1] - SECTOR[0])
        out_f = outside(signed_f[sp_[:, 1], sp_[:, 0]], w_f[sp_[:, 1], sp_[:, 0]], sec_f, max(FRONT_PX, 0), INSET_FRONT)
    else:
        out_f = ~keep_px[sp_[:, 1], sp_[:, 0]]
    carve_f = inside & out_f & ~oval[sp_[:, 1], sp_[:, 0]] & front_of_head
    occ[idx[carve_f, 0], idx[carve_f, 1], idx[carve_f, 2]] = False
    print('front carve: voxels carved', int(carve_f.sum()), '-> occupied', int(occ.sum()))
Vs, Fs, _, _ = measure.marching_cubes(ndimage.gaussian_filter(occ.astype(float), float(opts.get('sigma', 1.2))), 0.5)
ms = pymeshlab.MeshSet(); ms.add_mesh(pymeshlab.Mesh(Vs * RES + lo, Fs[:, ::-1].astype(np.int32)))
# the largest piece only (specks of hair off the main volume would make islands)
ms.meshing_remove_connected_component_by_face_number(mincomponentsize=2000)
for _ in range(SMOOTH):
    ms.apply_coord_taubin_smoothing(stepsmoothnum=5)
ms.meshing_decimation_quadric_edge_collapse(targetfacenum=FACES, preservenormal=True, planarquadric=True)
# Smoothed once more after the decimation: the decimated outline kept the voxel steps as sawtooth edges.
for _ in range(int(opts.get('post', 2))):
    ms.apply_coord_taubin_smoothing(stepsmoothnum=5)
m = ms.current_mesh(); Ve, Fe = m.vertex_matrix(), m.face_matrix().astype(np.int32)
# Out of the skull: where the photo shows hair near the face, the envelope's front surface must lie
# in front of the bald head (the head is a different mesh from the scan the strands came from, and
# at the temples it stood a few millimetres in front of the hair, so the grid's skin lay over it).
# Front-facing vertices behind the head's surface at their pixel move to 4 mm in front of it.
gb = list(trimesh.load(f'{WORK}/bald/kairo-bald-s42.glb', force='scene').geometry.values())[0]
Vb = K.reframe(np.asarray(gb.vertices, float) * turn, K.OY - K.BALD_OY); Fb = np.asarray(gb.faces, int)
rb = np.random.default_rng(3).dirichlet((1, 1, 1), size=12)
pb = np.einsum('bk,fkd->fbd', rb, Vb[Fb]).reshape(-1, 3); pxb, zcb = K.project(pb)
CELL = 2; G = K.N // CELL; ib = np.clip((pxb / CELL).astype(int), 0, G - 1)
zhead = np.full((G, G), np.inf); np.minimum.at(zhead, (ib[:, 1], ib[:, 0]), zcb)      # the head's nearest depth per cell
vn = np.zeros_like(Ve); fne = np.cross(Ve[Fe[:, 1]] - Ve[Fe[:, 0]], Ve[Fe[:, 2]] - Ve[Fe[:, 0]])
for kk in range(3): np.add.at(vn, Fe[:, kk], fne)
vn /= np.linalg.norm(vn, axis=1, keepdims=True) + 1e-12
cen = np.array([0.005, np.median(Ve[:, 1]), -0.046]); vn *= np.sign(((Ve - cen) * vn).sum(1, keepdims=True) + 1e-9)
pxe, zce = K.project(Ve); ie = np.clip((pxe / CELL).astype(int), 0, G - 1); spe = np.clip(((pxe - np.array([K.OX, K.OY])) / K.S).astype(int), 0, photo.shape[0] - 1)
zh = zhead[ie[:, 1], ie[:, 0]]
# ...and the skirt over the skin within 1.5 cm of the photo's hair: the alpha mask cuts it to the hair's
# edge, but only if it lies in front of the face (behind the grid it is invisible and nothing is cut).
ph_wide = ndimage.binary_dilation(ph, iterations=int(opts.get('skirt_px', 40)))
pull = (vn[:, 2] > 0.2) & np.isfinite(zh) & (zce > zh - 0.004) & (zce < zh + 0.05) & ph_wide[spe[:, 1], spe[:, 0]] & (Ve[:, 1] > COLLAR_Y)
PULL = float(opts.get('pull', 0.007))      # m in front of the head (the face grid sits up to 4 mm in front of it: at 4 mm the skirt fought it for depth)
Ve = Ve.copy(); Ve[pull] = K.unproject(pxe[pull], zh[pull] - PULL)
print('vertices pulled out of the skull', int(pull.sum()))
print('envelope', len(Ve), 'vertices', len(Fe), 'faces; y range', Ve[:, 1].min().round(3), Ve[:, 1].max().round(3))
# the GLB in the scan's own convention: undo the turn; a uniform hair-blue texture with every uv at the centre
img = Image.new('RGB', (8, 8), (58, 54, 118)); img.save(f'{D}/kairo-env.png')
mesh = trimesh.Trimesh(vertices=Ve * turn, faces=Fe, process=False)
mesh.visual = trimesh.visual.TextureVisuals(uv=np.full((len(Ve), 2), 0.5), image=img)
trimesh.Scene(mesh).export(OUT)
print('wrote', OUT)
