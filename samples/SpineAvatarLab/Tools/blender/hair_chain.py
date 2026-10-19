# hair_chain.py: exec in Blender's globals after kairo_hair_lib/trace/style/cards, tripo_fit, ear_fix, hairline_fix.
# From a merged Tripo hair (merge_hair3) to the exported GLB, every fix in order:
#   ears (fix_ears) -> hairline (fix_hairline) -> retoned atlas (tools/hair_retone.py) -> Tripo's own collar dropped -> export.
import subprocess, os, numpy as np, bmesh

KAIRO = '/Users/jonatansoderberg/.cache/spine-avatar-lab/kairo'
PY = '/Users/jonatansoderberg/.cache/spine-avatar-lab/pixal3d/.venv/bin/python'


def face_colours_of(ob):
    me = ob.data
    img = next(n.image for n in me.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE' and n.image)
    w, h = img.size; px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[:, :, :3]
    uvl = me.uv_layers.active.data
    uv = np.array([np.mean([uvl[l].uv[:] for l in p.loop_indices], 0) for p in me.polygons])
    x = np.clip((uv[:, 0] % 1) * w, 0, w - 1).astype(int); y = np.clip((uv[:, 1] % 1) * h, 0, h - 1).astype(int)
    return px[y, x] * 255


def drop_tripo_collar(src, out, z_max=-0.13, lum_min=140, r_max=0.15):
    """Tripo modelled a lilac turtleneck under the bust; its collar passed the hair's colour test and showed as a white ring
    with white triangles round the neck (owner: the collar has defects). Pale faces at the collar, and loose bits left there, go."""
    s_ob = bpy.data.objects[src]
    if out in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[out])
    ob = s_ob.copy(); ob.data = s_ob.data.copy(); ob.name = out; ob.data.name = out; s_ob.users_collection[0].objects.link(ob)
    M = ob.matrix_world; C = np.array([(M @ p.center)[:] for p in ob.data.polygons]); cols = face_colours_of(ob)
    lum = cols @ [0.299, 0.587, 0.114]; r = np.hypot(C[:, 0], C[:, 1])
    drop = set(np.where((C[:, 2] < z_max) & (lum > lum_min) & (r < r_max))[0])
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table(); bm.verts.index_update()
    par = list(range(len(bm.verts)))
    def f(a):
        while par[a] != a: par[a] = par[par[a]]; a = par[a]
        return a
    for e in bm.edges:
        a, b = f(e.verts[0].index), f(e.verts[1].index)
        if a != b: par[a] = b
    comp = {}
    for fc in bm.faces: comp.setdefault(f(fc.verts[0].index), []).append(fc.index)
    loose = 0
    for fs in comp.values():
        rest = [i for i in fs if i not in drop]
        if rest and len(rest) <= 8 and (C[rest, 2] < z_max + 0.01).all() and (r[rest] < r_max).all():
            drop |= set(rest); loose += len(rest)
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in drop], context='FACES')
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    return {'dropped': len(drop), 'loose': loose, 'faces': len(ob.data.polygons)}


def retone_into(src, out, k_low=2.2, k_high=1.3):
    """A copy of `src` whose material shows its atlas retoned toward the reference (tools/hair_retone.py)."""
    s_ob = bpy.data.objects[src]
    img0 = next(n.image for n in s_ob.data.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE' and n.image)
    src_png = bpy.path.abspath(img0.filepath_raw or img0.filepath)
    dst_png = f'{KAIRO}/blender/{out}-atlas-retone.png'
    r = subprocess.run([PY, f'{KAIRO}/tools/hair_retone.py', src_png, dst_png, str(k_low), str(k_high), '10'], capture_output=True, text=True)
    print(r.stdout.strip(), r.stderr.strip()[-300:])
    if out in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[out])
    ob = s_ob.copy(); ob.data = s_ob.data.copy(); ob.name = out; ob.data.name = out; s_ob.users_collection[0].objects.link(ob)
    m = s_ob.data.materials[0].copy(); m.name = out + 'Mat'; ob.data.materials[0] = m
    old = bpy.data.images.get(out + '-atlas')
    if old: bpy.data.images.remove(old)
    img = bpy.data.images.load(dst_png); img.name = out + '-atlas'
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image: n.image = img
    return dst_png


def run_chain(merged, tag, k_low=2.2, k_high=1.3):
    """merged -> {tag}E (ears) -> {tag}H (hairline) -> {tag}T (retoned) -> {tag} (collar dropped) -> GLB kairo-hair-{tag}.glb"""
    print('ears', fix_ears(merged, tag + 'E'))
    print('hairline', fix_hairline(tag + 'E', tag + 'H'))
    print('retone', retone_into(tag + 'H', tag + 'T', k_low, k_high))
    print('collar', drop_tripo_collar(tag + 'T', tag))
    path = f'{KAIRO}/blender/kairo-hair-{tag.lower()}.glb'
    export_tripo(tag, path, cap_mode='behind', under='TripoUnder', cap_image='HairCapTexDark')
    for o in bpy.data.objects:
        if o.name.startswith(tag) and o.type == 'MESH': o.hide_set(o.name != tag)
    return path


def drop_shards(src, out, max_faces=6, keep_ratio=4.0, keep_len=0.02, max_az=125.0):
    """Small loose pieces that are not strands: compact triangles from Tripo's debris and from splitting the models' shells
    face by face (merge_hair3 split_min) showed as shards at the part, over the sides and as a saw-tooth along the ends.
    A piece of max_faces or fewer is kept only when it is a strand: keep_ratio times longer than wide and keep_len long.
    Faces warmer than blue (Tripo's own skin and ears, grown into the hair) go too. Only within max_az of the front: at the back
    the pieces covered gaps between the locks (without them the neck's skin showed through)."""
    s_ob = bpy.data.objects[src]
    if out in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[out])
    ob = s_ob.copy(); ob.data = s_ob.data.copy(); ob.name = out; ob.data.name = out; s_ob.users_collection[0].objects.link(ob)
    cols = face_colours_of(ob); warm = cols[:, 0] > cols[:, 2]
    M = ob.matrix_world
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table(); bm.verts.index_update()
    par = list(range(len(bm.verts)))
    def f(a):
        while par[a] != a: par[a] = par[par[a]]; a = par[a]
        return a
    for e in bm.edges:
        a, b = f(e.verts[0].index), f(e.verts[1].index)
        if a != b: par[a] = b
    comp = {}
    for fc in bm.faces: comp.setdefault(f(fc.verts[0].index), []).append(fc)
    drop = set(); shards = strands = 0
    for fs in comp.values():
        if len(fs) > max_faces:
            continue
        pts = np.array([(M @ v.co)[:] for fc in fs for v in fc.verts])
        d_ = pts.mean(0) - np.array(PIV)
        if abs(np.degrees(np.arctan2(d_[0], -d_[1]))) > max_az:
            continue
        sv = np.linalg.svd(pts - pts.mean(0), compute_uv=False)
        length = np.ptp(pts @ np.linalg.svd(pts - pts.mean(0))[2][0])
        if sv[0] > keep_ratio * max(sv[1], 1e-9) and length > keep_len:
            strands += 1; continue
        drop |= set(fs); shards += 1
    # warm pieces (Tripo's own skin and ears grown into the hair) go whole; warm texels inside a lock stay (dropping them made holes)
    warm_faces = [fc for fs in comp.values() if sum(warm[fc.index] for fc in fs) > 0.5 * len(fs) for fc in fs]
    drop |= set(warm_faces)
    bmesh.ops.delete(bm, geom=list(drop), context='FACES')
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    return {'shards': shards, 'strands_kept': strands, 'warm': len(warm_faces), 'faces_dropped': len(drop), 'faces': len(ob.data.polygons)}


def drop_face_occluders(src, out, z_lo=-0.06, z_hi=0.135, x_half=0.20, reach=0.08, share=0.3):
    """Whole hair pieces in front of the face below the brows: flat panels of the side/back model beside the eyes, reaching
    forward from a piece whose centre is at the side, showed as rectangles over the eye corners and temples. A piece goes when
    `share` of its faces (between z_lo and z_hi, |x| < x_half) have the head right behind them (a ray toward +y hits it within
    `reach`)."""
    from mathutils.bvhtree import BVHTree
    s_ob = bpy.data.objects[src]
    if out in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[out])
    ob = s_ob.copy(); ob.data = s_ob.data.copy(); ob.name = out; ob.data.name = out; s_ob.users_collection[0].objects.link(ob)
    M = ob.matrix_world
    head = bpy.data.objects['Head']; dg = bpy.context.evaluated_depsgraph_get(); hm = head.evaluated_get(dg).to_mesh(); hm.calc_loop_triangles()
    HB = BVHTree.FromPolygons([head.matrix_world @ v.co for v in hm.vertices], [tuple(t.vertices) for t in hm.loop_triangles])
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.verts.index_update()
    par = list(range(len(bm.verts)))
    def f(a):
        while par[a] != a: par[a] = par[par[a]]; a = par[a]
        return a
    for e in bm.edges:
        a, b = f(e.verts[0].index), f(e.verts[1].index)
        if a != b: par[a] = b
    comp = {}
    for fc in bm.faces: comp.setdefault(f(fc.verts[0].index), []).append(fc)
    drop = set(); n = 0
    for fs in comp.values():
        hits = 0
        for fc in fs:
            p = M @ fc.calc_center_median()
            if not (z_lo < p.z < z_hi and abs(p.x) < x_half): continue
            hit = HB.ray_cast(p, Vector((0, 1, 0)), reach)
            if hit[0] is not None and hit[0].y > p.y: hits += 1
        if hits >= max(1, share * len(fs)): drop |= set(fs); n += 1
    bmesh.ops.delete(bm, geom=list(drop), context='FACES'); bm.to_mesh(ob.data); bm.free(); ob.data.update()
    return {'pieces': n, 'faces_dropped': len(drop), 'faces': len(ob.data.polygons)}
