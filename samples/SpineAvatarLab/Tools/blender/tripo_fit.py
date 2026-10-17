# tripo_fit.py: exec inside Blender after kairo_hair_lib/trace/style/cards. A Tripo model (generate_3d) fitted to the Kairo head:
#   fit_tripo(src_name, out_name) -> the hair object: the mesh turned -90 deg about Z (Tripo looks +X, the head -Y), its faces split by the
#   base-colour texture (hair = saturated blue), scale + translation fitted on the face skin to our Head (rotation locked), Tripo's own
#   eyes and brows (blue faces lying on our face round the lids) dropped, a lab-look material for previews.
#   export_tripo(hair_name, path, cap_mode) -> GLB in the cards' slot (KairoCards + KairoCap) for KAIRO_HAIR=cards.
import numpy as np, bmesh
def _face_colours(me):
    mat = me.materials[0]; img = next(n.image for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith('Color'))
    W, H = img.size; px = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H, W, 4); uv = me.uv_layers.active.data
    cols = np.zeros((len(me.polygons), 3)); cen = np.zeros((len(me.polygons), 3))
    for p in me.polygons:
        us = np.mean([tuple(uv[l].uv) for l in p.loop_indices], 0); cols[p.index] = px[min(H - 1, int(us[1] * H)), min(W - 1, int(us[0] * W)), :3]; cen[p.index] = tuple(p.center)
    return cols, cen

def fit_tripo(src_name, out_name, rotate=True):
    o = bpy.data.objects[src_name]; me = o.data
    if rotate and not o.get('kairo_rotated'):
        me.transform(Matrix.Rotation(-math.pi / 2, 4, 'Z')); me.update(); o['kairo_rotated'] = True
    cols, cen = _face_colours(me)
    r, g, b = cols[:, 0], cols[:, 1], cols[:, 2]; mx = cols.max(1); mn = cols.min(1); sat = (mx - mn) / np.maximum(mx, 1e-6)
    hair = (b > r + 0.06) & (b > g + 0.03) & (sat > 0.25); skin = (r > g) & (g > b) & (r - b > 0.08)
    zt, zb = cen[:, 2].max(), cen[:, 2].min(); face = skin & (cen[:, 2] > zt - 0.55 * (zt - zb)) & (cen[:, 1] < np.percentile(cen[:, 1], 40))
    Pf = cen[face]; Ph = cen[hair]; hb = bvh_of(bpy.data.objects['Head'])[0]
    env = bpy.data.objects['HairEnvelope']; ev = np.array([tuple(env.matrix_world @ v.co) for v in env.data.vertices])
    s = (ev[:, 0].max() - ev[:, 0].min()) / (Ph[:, 0].max() - Ph[:, 0].min())
    t = np.array([(ev[:, 0].max() + ev[:, 0].min()) / 2 - s * (Ph[:, 0].max() + Ph[:, 0].min()) / 2, 0.0, ev[:, 2].max() - s * Ph[:, 2].max()])
    Q = s * Pf + t; near = np.array([tuple(hb.find_nearest(Vector(q))[0]) for q in Q]); t[1] += np.median(near[:, 1] - Q[:, 1])
    for it in range(40):
        Q = s * Pf + t; near = np.array([tuple(hb.find_nearest(Vector(q))[0]) for q in Q]); d = np.linalg.norm(near - Q, axis=1)
        keep = d <= np.percentile(d, 80); A, B = Pf[keep], near[keep]; ma, mb = A.mean(0), B.mean(0)
        s = float(((A - ma) * (B - mb)).sum() / ((A - ma) ** 2).sum()); t = mb - s * ma
    print(f"fit {src_name}: hair faces {int(hair.sum())}, face skin {int(face.sum())}, face median {np.median(d)*1000:.1f} mm p90 {np.percentile(d, 90)*1000:.1f} mm, scale {s:.3f}, t {np.round(t, 3)}")
    m2 = me.copy(); bm = bmesh.new(); bm.from_mesh(m2); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not hair[f.index]], context='FACES')
    for v in bm.verts: v.co = Vector(tuple(s * np.array(tuple(v.co)) + t))
    lids = bpy.data.objects['Lids']; lv = np.array([tuple(lids.matrix_world @ v.co) for v in lids.data.vertices]); lo, hi = lv.min(0) - 0.015, lv.max(0) + 0.015; hi[2] += 0.05
    bm.faces.ensure_lookup_table(); kill = []
    for f in bm.faces:
        c = f.calc_center_median()
        if all(lo[i] <= c[i] <= hi[i] for i in range(3)) and hb.find_nearest(c)[3] < 0.008: kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context='FACES'); bm.to_mesh(m2); bm.free(); m2.update()
    print(f"  Tripo's eyes/brows dropped: {len(kill)} faces; hair kept {len(m2.polygons)}")
    old = bpy.data.objects.get(out_name); oldme = old.data if old else None
    if old: bpy.data.objects.remove(old)
    if oldme is not None and oldme.users == 0: bpy.data.meshes.remove(oldme)
    m2.name = out_name; ob = bpy.data.objects.new(out_name, m2); bpy.context.scene.collection.objects.link(ob)
    o.hide_set(True); o.hide_render = True
    return ob, (s, t)

def lab_material_for(ob):
    src = ob.data.materials[0]; img = next(n.image for n in src.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith('Color'))
    m = bpy.data.materials.new(ob.name + 'Lab'); m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img; bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled'); bsdf.inputs['Roughness'].default_value = 1.0
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs[0].default_value = 1.0
    nt.links.new(tex.outputs['Color'], mix.inputs[6]); mix.inputs[7].default_value = (0.1, 0.1, 0.1, 1); nt.links.new(mix.outputs[2], bsdf.inputs['Base Color'])
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = 0.9; nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
    return m

def export_tripo(hair_name, path, cap_mode='behind', under=None, cap_image=None):
    """cap_mode 'behind': the skull cap kept only behind the Tripo hair (build_cap_for_cards with the hair as the cards),
    'full': the whole cap; the hair object and the cap exported as KairoCards + KairoCap"""
    ho = bpy.data.objects[hair_name]; real_mat = ho.data.materials[0]
    renamed = []
    for nm in ('KairoCards', 'KairoCap'):
        ob = bpy.data.objects.get(nm)
        if ob: ob.name = nm + 'Stash'; ob.data.name = nm + 'Stash'; renamed.append(ob)
    ho_name0 = ho.name; ho.name = 'KairoCards'; ho.data.name = 'KairoCards'
    try:
        if cap_mode == 'behind':
            cap = build_cap_for_cards(ho, edge_px=6, grow=1)
        elif cap_mode == 'under':
            # No scalp cap: its per-face test (behind the hair or not) cut it into jagged dark shards wherever the hair is thin
            # (temples, upper sides; owner: "random trianglar"). The skull's painted roots show there instead; the smooth
            # under-shell (joined below) still closes the see-through between the locks.
            cap = bpy.data.objects.new('KairoCapTmp', bpy.data.meshes.new('KairoCapTmp')); bpy.context.scene.collection.objects.link(cap)
            cap.data.materials.append(cap_material_lab('HairCap', bpy.data.images[cap_image]) if cap_image else bpy.data.materials.new('HairCap'))
        else:
            cap = build_cap_zones()
        if cap_image:
            cap.data.materials[0] = cap_material_lab('HairCap', bpy.data.images[cap_image])
        if under is not None:
            # the dark under-shell (the approved hair volume inset 2 cm, kept inside the hair's silhouette) joined into the cap's
            # mesh with the cap's material: the loader reads one material per mesh; seen through gaps between locks it is dark hair
            u = bpy.data.objects[under]; uc = u.copy(); uc.data = u.data.copy(); bpy.context.scene.collection.objects.link(uc)
            uc.data.materials.clear(); uc.data.materials.append(cap.data.materials[0])
            for p in uc.data.polygons: p.material_index = 0
            bpy.ops.object.select_all(action='DESELECT'); uc.select_set(True); cap.select_set(True); bpy.context.view_layer.objects.active = cap; bpy.ops.object.join()
            print("cap + under-shell faces", len(cap.data.polygons))
        cap.name = 'KairoCap'; cap.data.name = 'KairoCap'
        out, js = export_cards_glb(ho, path, extra=[cap])
        print("GLB MiB", round(os.path.getsize(path) / 2**20, 2), [m['name'] for m in js['meshes']])
        cap.name = hair_name + 'Cap'; cap.data.name = hair_name + 'Cap'; cap.hide_set(True); cap.hide_render = True
    finally:
        ho.name = ho_name0; ho.data.name = ho_name0
        for ob in renamed: n = ob.name[:-5]; ob.name = n; ob.data.name = n
    return path

def _umeyama(A, B):
    ma, mb = A.mean(0), B.mean(0); A0, B0 = A - ma, B - mb; C = B0.T @ A0 / len(A); U, D, Vt = np.linalg.svd(C); Sg = np.eye(3)
    if np.linalg.det(U @ Vt) < 0: Sg[2, 2] = -1
    R = U @ Sg @ Vt; s = np.trace(np.diag(D) @ Sg) / (A0 ** 2).sum(1).mean(); return s, R, mb - s * R @ ma

def register_to(src_name, target_name='TripoFull', n=3000, steps=12):
    """a Tripo figure generated from another reference view, registered to the fitted front figure as whole figures
    (head, hair, neck, shoulders): yaw search in `steps` angles, then trimmed similarity ICP. Returns (s, R, t, median mm)."""
    src = bpy.data.objects[src_name]; tgt = bpy.data.objects[target_name]
    P = np.array([tuple(src.matrix_world @ v.co) for v in src.data.vertices]); rng = np.random.default_rng(1); P = P[rng.choice(len(P), min(n, len(P)), replace=False)]
    tb = bvh_of(tgt)[0]; T = np.array([tuple(tgt.matrix_world @ v.co) for v in tgt.data.vertices])
    s0 = (T[:, 2].max() - T[:, 2].min()) / (P[:, 2].max() - P[:, 2].min())
    def icp(s, R, t, iters):
        for _ in range(iters):
            Q = (s * (R @ P.T)).T + t; near = np.array([tuple(tb.find_nearest(Vector(q))[0]) for q in Q]); d = np.linalg.norm(near - Q, axis=1)
            keep = d <= np.percentile(d, 80); s, R, t = _umeyama(P[keep], near[keep])
        return s, R, t, float(np.median(d))
    best = None
    for k in range(steps):
        th = 2 * math.pi * k / steps; R0 = np.array(Matrix.Rotation(th, 3, 'Z'))
        t0 = T.mean(0) - s0 * (R0 @ P.mean(0))
        s, R, t, med = icp(s0, R0, t0, 12)
        if best is None or med < best[3]: best = (s, R, t, med, th)
    s, R, t, med = icp(best[0], best[1], best[2], 40)
    yaw = math.degrees(math.atan2(R[1, 0], R[0, 0]))
    print(f"register {src_name}: start yaw {math.degrees(best[4]):.0f} deg -> yaw {yaw:.1f} deg, scale {s:.3f}, median {med*1000:.1f} mm")
    return s, R, t, med

def apply_fit(src_name, out_name, s, R, t):
    src = bpy.data.objects[src_name]; me = src.data.copy(); mw = src.matrix_world
    for v in me.vertices: v.co = Vector(tuple(s * (R @ np.array(tuple(mw @ v.co))) + t))
    me.update(); old = bpy.data.objects.get(out_name); oldme = old.data if old else None
    if old: bpy.data.objects.remove(old)
    if oldme is not None and oldme.users == 0: bpy.data.meshes.remove(oldme)
    me.name = out_name; ob = bpy.data.objects.new(out_name, me); bpy.context.scene.collection.objects.link(ob); return ob

def hair_faces_by_colour(me):
    cols, cen = _face_colours(me)
    r, g, b = cols[:, 0], cols[:, 1], cols[:, 2]; mx = cols.max(1); mn = cols.min(1); sat = (mx - mn) / np.maximum(mx, 1e-6)
    return (b > r + 0.06) & (b > g + 0.03) & (sat > 0.25), cen

def merge_hair(parts, out_name, atlas_px=4096, tile=2048):
    """parts: [(object name, (az_lo, az_hi) in degrees)]; azimuth about the head pivot, 0 = her front (-Y), +90 = her left (+X).
    Each part keeps its hair faces whose centre lies in its sector; the parts are joined into one mesh whose UVs point into a
    2 x 2 atlas of the parts' base-colour textures (one material, one image for the GLB). Tripo's eyes/brows on our face dropped."""
    hb = bvh_of(bpy.data.objects['Head'])[0]
    lids = bpy.data.objects['Lids']; lv = np.array([tuple(lids.matrix_world @ v.co) for v in lids.data.vertices]); lo, hi = lv.min(0) - 0.015, lv.max(0) + 0.015; hi[2] += 0.05
    atlas = np.zeros((atlas_px, atlas_px, 4), np.float32); atlas[..., 3] = 1
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap'); kept = {}
    for k, (name, (a0, a1)) in enumerate(parts):
        ob = bpy.data.objects[name]; me = ob.data; mw = ob.matrix_world
        hair, cen = hair_faces_by_colour(me)
        img = next(n.image for n in me.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith('Color'))
        W, H = img.size; px = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H, W, 4)
        if W != tile: from_img = bpy.data.images.new('_tmp', W, H); from_img.pixels.foreach_set(px.ravel()); from_img.scale(tile, tile); px = np.empty(tile * tile * 4, np.float32); from_img.pixels.foreach_get(px); px = px.reshape(tile, tile, 4); bpy.data.images.remove(from_img)
        ox, oy = (k % 2) * tile, (k // 2) * tile; atlas[oy:oy + tile, ox:ox + tile] = px
        uv = me.uv_layers.active.data; vmap = {}; n_kept = 0
        for p in me.polygons:
            if not hair[p.index]: continue
            c = mw @ p.center; d = c - PIV; az = math.degrees(math.atan2(d.x, -d.y))
            inside = (a0 <= az <= a1) if a0 <= a1 else (az >= a0 or az <= a1)
            if not inside: continue
            if all(lo[i] <= c[i] <= hi[i] for i in range(3)) and hb.find_nearest(c)[3] < 0.008: continue
            vs = []
            for vi in p.vertices:
                if vi not in vmap: vmap[vi] = bm.verts.new(mw @ me.vertices[vi].co)
                vs.append(vmap[vi])
            try: f = bm.faces.new(vs)
            except ValueError: continue
            for loop, li in zip(f.loops, p.loop_indices):
                u, v = uv[li].uv; loop[uvl].uv = ((ox + u * tile) / atlas_px, (oy + v * tile) / atlas_px)
            n_kept += 1
        kept[name] = n_kept
    me2 = bpy.data.meshes.new(out_name); bm.to_mesh(me2); bm.free()
    img = bpy.data.images.get(out_name + 'Atlas')
    if img: bpy.data.images.remove(img)
    img = bpy.data.images.new(out_name + 'Atlas', atlas_px, atlas_px); img.pixels.foreach_set(atlas.ravel()); img.filepath_raw = f"/Users/jonatansoderberg/.cache/spine-avatar-lab/kairo/blender/{out_name}-atlas.png"; img.file_format = 'PNG'; img.save()
    mat = bpy.data.materials.new(out_name + 'Mat'); mat.use_nodes = True; nt = mat.node_tree; bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img; nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 1.0
    me2.materials.append(mat)
    old = bpy.data.objects.get(out_name); oldme = old.data if old else None
    if old: bpy.data.objects.remove(old)
    if oldme is not None and oldme.users == 0: bpy.data.meshes.remove(oldme)
    me2.name = out_name; ob = bpy.data.objects.new(out_name, me2); bpy.context.scene.collection.objects.link(ob)
    print("merged", out_name, kept, "faces", len(me2.polygons)); return ob

def _components(bm, faces_set):
    seen = set(); comps = []
    for f in faces_set:
        if f.index in seen: continue
        stack = [f]; comp = []; seen.add(f.index)
        while stack:
            g = stack.pop(); comp.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen and h in faces_set: seen.add(h.index); stack.append(h)
        comps.append(comp)
    return comps

def merge_hair2(parts, out_name, atlas_px=4096, tile=2048, fill_rounds=3, eye_comp_max=60, eye_clear=0.006):
    """merge_hair with three fixes (the owner's marks on tripo3/4): (1) holes in the hair: a face the colour test missed is
    added when at least two of its edge neighbours are hair and it is not skin-coloured (a lighter texel in a lock left a
    pocket); (2) Tripo's eyes/brows: only small loose pieces (< eye_comp_max faces, mostly inside the eye box, on the face)
    go, a long lock passing the brow keeps its faces (it was cut at the brow's end); (3) locks in front of the eyes are
    pushed out along the head normal to at least eye_clear from the skin instead of being cut (a strand went through the
    closing lid)."""
    hb = bvh_of(bpy.data.objects['Head'])[0]
    lids = bpy.data.objects['Lids']; lv = np.array([tuple(lids.matrix_world @ v.co) for v in lids.data.vertices]); lo, hi = lv.min(0) - 0.015, lv.max(0) + 0.015; hi[2] += 0.05
    elo, ehi = lv.min(0).copy(), lv.max(0).copy(); elo[0] -= 0.012; ehi[0] += 0.012; elo[2] -= 0.008; ehi[2] += 0.012; elo[1] -= 0.03
    inbox = lambda c, a, b: all(a[i] <= c[i] <= b[i] for i in range(3))
    atlas = np.zeros((atlas_px, atlas_px, 4), np.float32); atlas[..., 3] = 1
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap'); stats = {}
    for k, part in enumerate(parts):
        name, (a0, a1) = part[:2]; crown_r = part[2] if len(part) > 2 else 0.0
        ob = bpy.data.objects[name]; me = ob.data; mw = ob.matrix_world
        cols, cen = _face_colours(me)
        r, g, b = cols[:, 0], cols[:, 1], cols[:, 2]; mx = cols.max(1); mn = cols.min(1); sat = (mx - mn) / np.maximum(mx, 1e-6)
        hair = (b > r + 0.06) & (b > g + 0.03) & (sat > 0.25); skin = (r > g) & (g > b) & (r - b > 0.08)
        # (1) fill pockets: non-hair, non-skin faces with >= 2 hair neighbours, a few rounds
        nb = [[] for _ in me.polygons]; ek = {}
        for p in me.polygons:
            for e in p.edge_keys: ek.setdefault(e, []).append(p.index)
        for fs in ek.values():
            for a in fs:
                for b2 in fs:
                    if a != b2: nb[a].append(b2)
        added = 0
        for _ in range(fill_rounds):
            new = [i for i in range(len(hair)) if not hair[i] and not skin[i] and sum(hair[j] for j in nb[i]) >= 2]
            for i in new: hair[i] = True
            added += len(new)
            if not new: break
        img = next(n.image for n in me.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith('Color'))
        W, H = img.size; px = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H, W, 4)
        if W != tile:
            ti = bpy.data.images.new('_tmp', W, H); ti.pixels.foreach_set(px.ravel()); ti.scale(tile, tile); px = np.empty(tile * tile * 4, np.float32); ti.pixels.foreach_get(px); px = px.reshape(tile, tile, 4); bpy.data.images.remove(ti)
        ox, oy = (k % 2) * tile, (k // 2) * tile; atlas[oy:oy + tile, ox:ox + tile] = px
        # sector selection
        sel = []
        for p in me.polygons:
            if not hair[p.index]: continue
            c = mw @ p.center; d = c - PIV; az = math.degrees(math.atan2(d.x, -d.y))
            if (a0 <= az <= a1) if a0 <= a1 else (az >= a0 or az <= a1): sel.append(p.index)
        sel_set = set(sel)
        # (2) eyes/brows: components of the selected faces; drop small ones lying on the face in the eye box
        comp_of = {}; comps = []
        for i in sel:
            if i in comp_of: continue
            stack = [i]; comp = []; comp_of[i] = len(comps)
            while stack:
                j = stack.pop(); comp.append(j)
                for q in nb[j]:
                    if q in sel_set and q not in comp_of: comp_of[q] = len(comps); stack.append(q)
            comps.append(comp)
        drop = set()
        for comp in comps:
            if len(comp) >= eye_comp_max: continue
            cs = [mw @ me.polygons[i].center for i in comp]
            if sum(1 for c in cs if inbox(c, lo, hi) and hb.find_nearest(c)[3] < 0.008) >= 0.5 * len(cs): drop.update(comp)
        uv = me.uv_layers.active.data; vmap = {}; n_kept = 0
        for i in sel:
            if i in drop: continue
            p = me.polygons[i]; vs = []
            for vi in p.vertices:
                if vi not in vmap: vmap[vi] = bm.verts.new(mw @ me.vertices[vi].co)
                vs.append(vmap[vi])
            try: f = bm.faces.new(vs)
            except ValueError: continue
            for loop, li in zip(f.loops, p.loop_indices):
                u, v = uv[li].uv; loop[uvl].uv = ((ox + u * tile) / atlas_px, (oy + v * tile) / atlas_px)
            n_kept += 1
        stats[name] = dict(kept=n_kept, pockets_filled=added, eye_pieces_dropped=len(drop))
    # (3) push locks in front of the eyes out to eye_clear from the skin
    pushed = 0
    for v in bm.verts:
        if not inbox(v.co, elo, ehi): continue
        loc, n, i, d = hb.find_nearest(v.co)
        if loc is None or d >= eye_clear: continue
        out = (v.co - loc); out = out.normalized() if out.length > 1e-6 else n
        v.co = loc + out * eye_clear; pushed += 1
    me2 = bpy.data.meshes.new(out_name); bm.to_mesh(me2); bm.free()
    img = bpy.data.images.get(out_name + 'Atlas')
    if img: bpy.data.images.remove(img)
    img = bpy.data.images.new(out_name + 'Atlas', atlas_px, atlas_px); img.pixels.foreach_set(atlas.ravel()); img.filepath_raw = f"/Users/jonatansoderberg/.cache/spine-avatar-lab/kairo/blender/{out_name}-atlas.png"; img.file_format = 'PNG'; img.save()
    mat = bpy.data.materials.new(out_name + 'Mat'); mat.use_nodes = True; nt = mat.node_tree; bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img; nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 1.0
    me2.materials.append(mat)
    old = bpy.data.objects.get(out_name); oldme = old.data if old else None
    if old: bpy.data.objects.remove(old)
    if oldme is not None and oldme.users == 0: bpy.data.meshes.remove(oldme)
    me2.name = out_name; ob = bpy.data.objects.new(out_name, me2); bpy.context.scene.collection.objects.link(ob)
    root = bpy.data.objects['Root']; ob.parent = root; ob.matrix_parent_inverse = root.matrix_world.inverted()
    print("merged2", out_name, stats, "verts pushed off the eyes", pushed, "faces", len(me2.polygons)); return ob

def merge_hair3(parts, out_name, atlas_px=4096, tile=2048, grow_rounds=4, eye_comp_max=60, eye_clear=0.006, split_min=0):
    """merge_hair2 with whole locks: the hair of each model is grown from the colour test into adjacent non-skin faces with
    b >= r - 0.02 (lock tips and edges with greyer texels were cut), split into connected locks, and a lock is kept whole by
    the model whose exclusive sector holds its area-weighted centre (sector borders cut 162 edges in tripo4).
    parts: [(object, (az_lo, az_hi)[, crown_r])] with sectors (overlapping ones keep both models' locks there)."""
    hb = bvh_of(bpy.data.objects['Head'])[0]
    lids = bpy.data.objects['Lids']; lv = np.array([tuple(lids.matrix_world @ v.co) for v in lids.data.vertices]); lo, hi = lv.min(0) - 0.015, lv.max(0) + 0.015; hi[2] += 0.05
    elo, ehi = lv.min(0).copy(), lv.max(0).copy(); elo[0] -= 0.012; ehi[0] += 0.012; elo[2] -= 0.008; ehi[2] += 0.012; elo[1] -= 0.03
    inbox = lambda c, a, b: all(a[i] <= c[i] <= b[i] for i in range(3))
    atlas = np.zeros((atlas_px, atlas_px, 4), np.float32); atlas[..., 3] = 1
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap'); stats = {}; cut_report = {}
    for k, part in enumerate(parts):
        name, (a0, a1) = part[:2]; crown_r = part[2] if len(part) > 2 else 0.0
        # cone (optional): (axis, lo_deg, hi_deg) - the part's locks/faces by their 3D angle from the axis instead of the azimuth
        # (azimuth cannot tell the top of the head from the front: the front model's flat top showed on the sides)
        cone = part[3] if len(part) > 3 else None
        ob = bpy.data.objects[name]; me = ob.data; mw = ob.matrix_world
        cols, cen = _face_colours(me)
        r, g, b = cols[:, 0], cols[:, 1], cols[:, 2]; mx = cols.max(1); mn = cols.min(1); sat = (mx - mn) / np.maximum(mx, 1e-6)
        hair = (b > r + 0.06) & (b > g + 0.03) & (sat > 0.25); skin = (r > g) & (g > b) & (r - b > 0.08)
        loose = (b >= r - 0.02) & ~skin
        ek = {}
        for p in me.polygons:
            for e in p.edge_keys: ek.setdefault(e, []).append(p.index)
        nb = [[] for _ in me.polygons]
        for fs in ek.values():
            for a in fs:
                for c in fs:
                    if a != c: nb[a].append(c)
        grown = 0
        for _ in range(grow_rounds):
            new = [i for i in range(len(hair)) if not hair[i] and loose[i] and any(hair[j] for j in nb[i])]
            for i in new: hair[i] = True
            grown += len(new)
            if not new: break
        areas = np.array([p.area for p in me.polygons]); wc = np.array([tuple(mw @ p.center) for p in me.polygons])
        comp = -np.ones(len(hair), int); comps = []
        for i in range(len(hair)):
            if not hair[i] or comp[i] >= 0: continue
            stack = [i]; comp[i] = len(comps); members = []
            while stack:
                j = stack.pop(); members.append(j)
                for q in nb[j]:
                    if hair[q] and comp[q] < 0: comp[q] = len(comps); stack.append(q)
            comps.append(members)
        img = next(n.image for n in me.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith('Color'))
        W, H = img.size; px = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H, W, 4)
        if W != tile:
            ti = bpy.data.images.new('_tmp', W, H); ti.pixels.foreach_set(px.ravel()); ti.scale(tile, tile); px = np.empty(tile * tile * 4, np.float32); ti.pixels.foreach_get(px); px = px.reshape(tile, tile, 4); bpy.data.images.remove(ti)
        ox, oy = (k % 2) * tile, (k // 2) * tile; atlas[oy:oy + tile, ox:ox + tile] = px
        uv = me.uv_layers.active.data; vmap = {}; kept_locks = kept_faces = eye_dropped = 0; keep = np.zeros(len(hair), bool)
        for members in comps:
            w = areas[members]; c = (wc[members] * w[:, None]).sum(0) / max(w.sum(), 1e-12); d = Vector(c) - PIV; az = math.degrees(math.atan2(d.x, -d.y))
            # crown_r: a lock centred within this horizontal distance of the head's axis (the crown, its azimuth arbitrary) is this part's
            in_sector = lambda az_: (a0 <= az_ < a1) if a0 <= a1 else (az_ >= a0 or az_ < a1)
            def in_cone(v_):
                ax_ = np.array(cone[0], float); ax_ /= np.linalg.norm(ax_); v_ = np.atleast_2d(v_)
                ang_ = np.degrees(np.arccos(np.clip(v_ @ ax_ / np.maximum(np.linalg.norm(v_, axis=1), 1e-9), -1, 1)))
                return (ang_ >= cone[1]) & (ang_ < cone[2])
            in_crown = crown_r > 0 and math.hypot(d.x, d.y) < crown_r and d.z > 0
            if split_min and len(members) >= split_min:
                # A large piece (a model's shell round the head) is taken face by face: whole, the front model's shell brought its
                # flat, featureless sides with it (owner: the sides not as good as the back); dropped, the crown went bare.
                dd = wc[members] - np.array(PIV); faz = np.degrees(np.arctan2(dd[:, 0], -dd[:, 1]))
                fcrown = (np.hypot(dd[:, 0], dd[:, 1]) < crown_r) & (dd[:, 2] > 0) if crown_r > 0 else np.zeros(len(members), bool)
                fin = in_cone(dd) if cone is not None else np.array([in_sector(a_) for a_ in faz], bool)
                members = [m for m, f_, c_ in zip(members, fin, fcrown) if c_ or f_]
                if not members: continue
            elif not (in_crown or (bool(in_cone(np.array(d))[0]) if cone is not None else in_sector(az))): continue
            if len(members) < eye_comp_max:
                cs = [Vector(wc[i]) for i in members]
                if sum(1 for q in cs if inbox(q, lo, hi) and hb.find_nearest(q)[3] < 0.008) >= 0.5 * len(cs): eye_dropped += 1; continue
            kept_locks += 1
            for i in members:
                keep[i] = True; p = me.polygons[i]; vs = []
                for vi in p.vertices:
                    if vi not in vmap: vmap[vi] = bm.verts.new(mw @ me.vertices[vi].co)
                    vs.append(vmap[vi])
                try: f = bm.faces.new(vs)
                except ValueError: continue
                for loop, li in zip(f.loops, p.loop_indices):
                    u, v = uv[li].uv; loop[uvl].uv = ((ox + u * tile) / atlas_px, (oy + v * tile) / atlas_px)
                kept_faces += 1
        cuts = {'hair': 0, 'non-skin': 0, 'skin': 0}
        for fs in ek.values():
            if len(fs) != 2 or keep[fs[0]] == keep[fs[1]]: continue
            o = fs[1] if keep[fs[0]] else fs[0]
            cuts['hair' if hair[o] else ('skin' if skin[o] else 'non-skin')] += 1
        stats[name] = dict(locks=kept_locks, faces=kept_faces, grown=grown, eye_pieces=eye_dropped); cut_report[name] = cuts
    pushed = 0
    for v in bm.verts:
        if not inbox(v.co, elo, ehi): continue
        loc, n, i, d = hb.find_nearest(v.co)
        if loc is None or d >= eye_clear: continue
        out = (v.co - loc); out = out.normalized() if out.length > 1e-6 else n; v.co = loc + out * eye_clear; pushed += 1
    me2 = bpy.data.meshes.new(out_name); bm.to_mesh(me2); bm.free()
    img = bpy.data.images.get(out_name + 'Atlas')
    if img: bpy.data.images.remove(img)
    img = bpy.data.images.new(out_name + 'Atlas', atlas_px, atlas_px); img.pixels.foreach_set(atlas.ravel()); img.filepath_raw = f"/Users/jonatansoderberg/.cache/spine-avatar-lab/kairo/blender/{out_name}-atlas.png"; img.file_format = 'PNG'; img.save()
    mat = bpy.data.materials.new(out_name + 'Mat'); mat.use_nodes = True; nt = mat.node_tree; bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img; nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 1.0
    me2.materials.append(mat)
    old = bpy.data.objects.get(out_name); oldme = old.data if old else None
    if old: bpy.data.objects.remove(old)
    if oldme is not None and oldme.users == 0: bpy.data.meshes.remove(oldme)
    me2.name = out_name; ob = bpy.data.objects.new(out_name, me2); bpy.context.scene.collection.objects.link(ob)
    root = bpy.data.objects['Root']; ob.parent = root; ob.matrix_parent_inverse = root.matrix_world.inverted()
    print("merged3", out_name, stats, "| cut edges per part (kept vs not kept):", cut_report, "| verts pushed off the eyes", pushed, "| faces", len(me2.polygons)); return ob


def merge_hair4(parts, out_name, atlas_px=4096, tile=2048, eye_comp_max=60, eye_clear=0.006, min_hair_frac=0.5):
    """Whole lock meshes only. A Tripo model is built of separate pieces, each either hair (its texture blue) or not (face,
    body, sweater): a piece with more than min_hair_frac of its area hair-coloured is a lock and is taken whole. (merge_hair3
    picked faces by colour, so greyer texels inside a lock left holes the dark cap showed through, and isolated blue texels
    elsewhere became loose triangles; its split_min cut shells face by face into broken locks: the owner's \"trasiga lockar,
    random trianglar\".) parts: [(object, (axis, lo_deg, hi_deg))]: a lock belongs to the part whose cone holds its
    area-weighted centre (seen from the head's pivot); cones may overlap."""
    hb = bvh_of(bpy.data.objects['Head'])[0]
    lids = bpy.data.objects['Lids']; lv = np.array([tuple(lids.matrix_world @ v.co) for v in lids.data.vertices]); lo, hi = lv.min(0) - 0.015, lv.max(0) + 0.015; hi[2] += 0.05
    elo, ehi = lv.min(0).copy(), lv.max(0).copy(); elo[0] -= 0.012; ehi[0] += 0.012; elo[2] -= 0.008; ehi[2] += 0.012; elo[1] -= 0.03
    inbox = lambda c, a, b: all(a[i] <= c[i] <= b[i] for i in range(3))
    atlas = np.zeros((atlas_px, atlas_px, 4), np.float32); atlas[..., 3] = 1
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap'); stats = {}
    for k, (name, cone) in enumerate(parts):
        ob = bpy.data.objects[name]; me = ob.data; mw = ob.matrix_world
        cols, _ = _face_colours(me)
        r, g, b = cols[:, 0], cols[:, 1], cols[:, 2]; mx = cols.max(1); mn = cols.min(1); sat = (mx - mn) / np.maximum(mx, 1e-6)
        hair = (b > r + 0.06) & (b > g + 0.03) & (sat > 0.25)
        par = list(range(len(me.vertices)))
        def f(a):
            while par[a] != a: par[a] = par[par[a]]; a = par[a]
            return a
        for e in me.edges:
            a_, b_ = f(e.vertices[0]), f(e.vertices[1])
            if a_ != b_: par[a_] = b_
        lab = np.array([f(p.vertices[0]) for p in me.polygons])
        areas = np.array([p.area for p in me.polygons]); wc = np.array([tuple(mw @ p.center) for p in me.polygons])
        comps = {}
        for i, l in enumerate(lab): comps.setdefault(l, []).append(i)
        ax = np.array(cone[0], float); ax /= np.linalg.norm(ax)
        img = next(n.image for n in me.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith('Color'))
        W, H = img.size; px = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H, W, 4)
        if W != tile:
            ti = bpy.data.images.new('_tmp', W, H); ti.pixels.foreach_set(px.ravel()); ti.scale(tile, tile); px = np.empty(tile * tile * 4, np.float32); ti.pixels.foreach_get(px); px = px.reshape(tile, tile, 4); bpy.data.images.remove(ti)
        ox, oy = (k % 2) * tile, (k // 2) * tile; atlas[oy:oy + tile, ox:ox + tile] = px
        uv = me.uv_layers.active.data; vmap = {}; locks = faces = eye_dropped = not_hair = out_cone = 0
        for members in comps.values():
            w = areas[members]
            if (w * hair[members]).sum() <= min_hair_frac * max(w.sum(), 1e-12): not_hair += 1; continue
            c = (wc[members] * w[:, None]).sum(0) / max(w.sum(), 1e-12); d = c - np.array(PIV)
            ang = math.degrees(math.acos(max(-1.0, min(1.0, float(d @ ax / max(np.linalg.norm(d), 1e-9))))))
            if not (cone[1] <= ang < cone[2]): out_cone += 1; continue
            if len(members) < eye_comp_max:
                cs = [Vector(wc[i]) for i in members]
                if sum(1 for q in cs if inbox(q, lo, hi) and hb.find_nearest(q)[3] < 0.008) >= 0.5 * len(cs): eye_dropped += 1; continue
            locks += 1
            for i in members:
                p = me.polygons[i]; vs = []
                for vi in p.vertices:
                    if vi not in vmap: vmap[vi] = bm.verts.new(mw @ me.vertices[vi].co)
                    vs.append(vmap[vi])
                try: fnew = bm.faces.new(vs)
                except ValueError: continue
                for loop, li in zip(fnew.loops, p.loop_indices):
                    u, v = uv[li].uv; loop[uvl].uv = ((ox + u * tile) / atlas_px, (oy + v * tile) / atlas_px)
                faces += 1
        stats[name] = dict(locks=locks, faces=faces, not_hair=not_hair, outside_cone=out_cone, eye_pieces=eye_dropped)
    pushed = 0
    for v in bm.verts:
        if not inbox(v.co, elo, ehi): continue
        loc, n, i, d = hb.find_nearest(v.co)
        if loc is None or d >= eye_clear: continue
        out = (v.co - loc); out = out.normalized() if out.length > 1e-6 else n; v.co = loc + out * eye_clear; pushed += 1
    me2 = bpy.data.meshes.new(out_name); bm.to_mesh(me2); bm.free()
    img = bpy.data.images.get(out_name + 'Atlas')
    if img: bpy.data.images.remove(img)
    img = bpy.data.images.new(out_name + 'Atlas', atlas_px, atlas_px); img.pixels.foreach_set(atlas.ravel()); img.filepath_raw = f"/Users/jonatansoderberg/.cache/spine-avatar-lab/kairo/blender/{out_name}-atlas.png"; img.file_format = 'PNG'; img.save()
    mat = bpy.data.materials.new(out_name + 'Mat'); mat.use_nodes = True; nt = mat.node_tree; bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img; nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 1.0
    me2.materials.append(mat)
    old = bpy.data.objects.get(out_name); oldme = old.data if old else None
    if old: bpy.data.objects.remove(old)
    if oldme is not None and oldme.users == 0: bpy.data.meshes.remove(oldme)
    me2.name = out_name; ob = bpy.data.objects.new(out_name, me2); bpy.context.scene.collection.objects.link(ob)
    root = bpy.data.objects['Root']; ob.parent = root; ob.matrix_parent_inverse = root.matrix_world.inverted()
    print("merged4", out_name, stats, "| verts pushed off the eyes", pushed, "| faces", len(me2.polygons)); return ob


def hair_dense(src_name, out_name, grow_rounds=6, min_piece=40, z_min=-0.13):
    """Hair of a dense generated model whose head and hair are one surface (Hunyuan3D, Rodin): faces by colour, grown into
    adjacent non-skin faces (strand edges with greyer texels), small loose pieces and Tripo-style eye/brow faces on the face
    dropped, nothing below the collar. The model's own material and UVs are kept (one texture, no atlas)."""
    src = bpy.data.objects[src_name]; me = src.data; mw = src.matrix_world
    cols, _ = _face_colours(me)
    r, g, b = cols[:, 0], cols[:, 1], cols[:, 2]; mx = cols.max(1); mn = cols.min(1); sat = (mx - mn) / np.maximum(mx, 1e-6)
    hair = (b > r + 0.06) & (b > g + 0.03) & (sat > 0.25); skin = (r > g) & (g > b) & (r - b > 0.08)
    loose = (b >= r - 0.02) & ~skin
    wc = np.array([tuple(mw @ p.center) for p in me.polygons])
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table(); bm.transform(mw)
    nb = [[g_.index for e in f.edges for g_ in e.link_faces if g_.index != f.index] for f in bm.faces]
    for _ in range(grow_rounds):
        new = [i for i in range(len(hair)) if not hair[i] and loose[i] and any(hair[j] for j in nb[i])]
        for i in new: hair[i] = True
        if not new: break
    hair &= wc[:, 2] > z_min
    hb = bvh_of(bpy.data.objects['Head'])[0]
    lids = bpy.data.objects['Lids']; lv = np.array([tuple(lids.matrix_world @ v.co) for v in lids.data.vertices]); lo, hi = lv.min(0) - 0.015, lv.max(0) + 0.015; hi[2] += 0.05
    on_face = np.array([all(lo[i] <= c[i] <= hi[i] for i in range(3)) and hb.find_nearest(Vector(c))[3] < 0.008 if hair[k] else False for k, c in enumerate(wc)])
    hair &= ~on_face
    comp = -np.ones(len(hair), int); sizes = []
    for i in range(len(hair)):
        if not hair[i] or comp[i] >= 0: continue
        stack = [i]; comp[i] = len(sizes); n = 0
        while stack:
            j = stack.pop(); n += 1
            for q in nb[j]:
                if hair[q] and comp[q] < 0: comp[q] = len(sizes); stack.append(q)
        sizes.append(n)
    sizes = np.array(sizes); small = hair & (sizes[np.maximum(comp, 0)] < min_piece)
    hair &= ~small
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not hair[f.index]], context='FACES')
    m2 = bpy.data.meshes.new(out_name); bm.to_mesh(m2); bm.free()
    for mat in me.materials: m2.materials.append(mat)
    old = bpy.data.objects.get(out_name); oldme = old.data if old else None
    if old: bpy.data.objects.remove(old)
    if oldme is not None and oldme.users == 0: bpy.data.meshes.remove(oldme)
    m2.name = out_name; ob = bpy.data.objects.new(out_name, m2); bpy.context.scene.collection.objects.link(ob)
    root = bpy.data.objects['Root']; ob.parent = root; ob.matrix_parent_inverse = root.matrix_world.inverted()
    print('hair_dense', out_name, 'faces', len(m2.polygons), 'eye/brow faces', int(on_face.sum()), 'small pieces faces', int(small.sum()))
    return ob
