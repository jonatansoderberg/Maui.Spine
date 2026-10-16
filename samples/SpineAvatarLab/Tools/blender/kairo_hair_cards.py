
# kairo_hair_cards.py: hair cards from the styled guides. exec after lib, trace and style.
import numpy as np

def resample(pts, ss, n):
    L = ss[-1]; out = []
    for i in range(n):
        s = L * i / (n - 1); out.append(StyledField.along(pts, ss, s))
    return out, L

def card_frames(F, samples):
    """per sample: tangent, wig-surface normal, lateral (binormal)"""
    fr = []
    for i, p in enumerate(samples):
        a = samples[max(0, i - 1)]; b = samples[min(len(samples) - 1, i + 1)]
        t = (b - a)
        if t.length < 1e-9: t = Vector((0, 0, -1))
        t.normalize()
        u = (p - PIV).normalized(); n = F.R.normal(F.R.sm, u)
        n = (n - t * n.dot(t)); n = n.normalized() if n.length > 1e-6 else u
        lat = n.cross(t).normalized()
        fr.append((t, n, lat))
    return fr

def clump_extent(samples, frames, children):
    """90th percentile of the children's lateral and normal offsets from the guide samples"""
    lat = []; nor = []
    for s in children:
        for p in s[::3]:
            i = min(range(len(samples)), key=lambda k: (samples[k] - p).length_squared)
            d = p - samples[i]; t, n, b = frames[i]
            lat.append(abs(d.dot(b))); nor.append(abs(d.dot(n)))
    if not lat: return 0.02, 0.01
    lat.sort(); nor.sort()
    return lat[int(0.9 * (len(lat) - 1))], nor[int(0.9 * (len(nor) - 1))]

def build_cards(F, strands, gidx, name='KairoCards', n_samples=16, cols=32, rows=5, min_w=0.025, max_w=0.06, w_scale=1.15, material='HairCards', cross=False, cross_min=0.012, cross_max=0.024, cross_scale=1.3, clip_out=None):
    guides = F.guides; by_guide = {}
    for s, gi in zip(strands, gidx):
        if gi >= 0: by_guide.setdefault(gi, []).append(s)
    verts = []; faces = []; uvs = []; info = []
    k = 0
    for gi, g in enumerate(guides):
        if len(g['pts']) < 4 or g['ss'][-1] < 0.06: continue
        samples, L = resample(g['pts'], g['ss'], n_samples); frames = card_frames(F, samples)
        if clip_out is not None:
            keep = n_samples
            for i, p in enumerate(samples):
                u_ = (p - PIV).normalized()
                if (p - PIV).length > F.R.sample(F.R.sm, u_) + clip_out: keep = max(4, i); break
            samples = samples[:keep]; frames = frames[:keep]
        lat, nor = clump_extent(samples, frames, by_guide.get(gi, []))
        w = min(max_w, max(min_w, lat * w_scale))
        col = k % cols; row = k // cols
        if row >= rows: break
        u0, u1 = col / cols, (col + 1) / cols; v0, v1 = row / rows, (row + 1) / rows
        base = len(verts)
        for i, (p, (t, n, b)) in enumerate(zip(samples, frames)):
            taper = 1.0 if i < len(samples) - 3 else (0.8, 0.65, 0.5)[i - (len(samples) - 3)]   # the mesh narrows over the last three samples; alpha does the rest
            half = w * 0.5 * taper
            verts.append(p - b * half); verts.append(p + b * half)
            vv = v0 + (v1 - v0) * i / (n_samples - 1)
            uvs.append((u0, vv)); uvs.append((u1, vv))
        for i in range(len(samples) - 1):
            L0, R0, L1, R1 = base + 2 * i, base + 2 * i + 1, base + 2 * i + 2, base + 2 * i + 3
            faces.append((L0, L1, R1, R0))
        if cross:
            cw = min(cross_max, max(cross_min, nor * cross_scale)); base2 = len(verts)
            for i, (p, (t, n, b)) in enumerate(zip(samples, frames)):
                half = cw * 0.5 * (1.0 if i < n_samples - 3 else 0.75)
                verts.append(p - n * half); verts.append(p + n * half)
                vv = v0 + (v1 - v0) * i / (n_samples - 1); uvs.append((u0, vv)); uvs.append((u1, vv))
            for i in range(n_samples - 1):
                L0, R0, L1, R1 = base2 + 2 * i, base2 + 2 * i + 1, base2 + 2 * i + 2, base2 + 2 * i + 3
                faces.append((L0, L1, R1, R0))
        info.append(dict(guide=gi, strip=k, width=w, depth=nor, length=L, lam=g['lam'], n_children=len(by_guide.get(gi, []))))
        k += 1
    old = bpy.data.objects.get(name)
    if old:
        od = old.data; bpy.data.objects.remove(old); bpy.data.meshes.remove(od)
    me = bpy.data.meshes.new(name); me.from_pydata([tuple(v) for v in verts], [], faces); me.update()
    uv = me.uv_layers.new(name='UVMap')
    for poly in me.polygons:
        for li in poly.loop_indices:
            uv.data[li].uv = uvs[me.loops[li].vertex_index]
    # orient faces outward (along the wig normal)
    flipped = 0
    for poly in me.polygons:
        c = poly.center; u = (c - PIV).normalized()
        if poly.normal.dot(u) < 0: flipped += 1
    if flipped > len(me.polygons) / 2:
        import bmesh as _bm
        bm = _bm.new(); bm.from_mesh(me); _bm.ops.reverse_faces(bm, faces=bm.faces); bm.to_mesh(me); bm.free(); me.update()
    mat = bpy.data.materials.get(material) or bpy.data.materials.new(material)
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me); bpy.data.collections['Collection'].objects.link(ob)
    root = bpy.data.objects['Root']; ob.parent = root; ob.matrix_parent_inverse = root.matrix_world.inverted(); ob.matrix_world = Matrix.Identity(4)
    ob['card_info'] = info
    return ob, info


def card_material(name='HairCards', image=None):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled'); tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = image; tex.interpolation = 'Linear'
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color']); nt.links.new(tex.outputs['Alpha'], bsdf.inputs['Alpha'])
    bsdf.inputs['Roughness'].default_value = 0.55
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
    nt.nodes.active = tex
    mat.blend_method = 'CLIP' if 'CLIP' in [i.identifier for i in bpy.types.Material.bl_rna.properties['blend_method'].enum_items] else 'HASHED'
    mat.alpha_threshold = 0.5; mat.use_backface_culling = False
    try: mat.surface_render_method = 'DITHERED'
    except Exception: pass
    return mat

def bake_cards(cards, sources, image, kind, samples=16, cage=0.015, max_dist=0.03, white_mat=None):
    """Cycles selected-to-active bake of the hair curves onto the cards' atlas image. kind: 'DIFFUSE' (colour) or 'EMIT'."""
    sc = bpy.context.scene; old_engine = sc.render.engine
    try: sc.render.engine = 'CYCLES'
    except TypeError as e: print("engine:", e)
    prefs = bpy.context.preferences.addons['cycles'].preferences
    try: prefs.compute_device_type = 'METAL'
    except Exception as e: print("device type:", e)
    for d in prefs.devices: d.use = (d.type == 'METAL')
    sc.cycles.device = 'GPU'; old_samples = sc.cycles.samples; sc.cycles.samples = samples
    sc.cycles.use_denoising = False
    b = sc.render.bake; b.use_selected_to_active = True; b.cage_extrusion = cage; b.max_ray_distance = max_dist; b.use_clear = True; b.margin = 2; b.target = 'IMAGE_TEXTURES'
    b.use_pass_direct = False; b.use_pass_indirect = False; b.use_pass_color = True
    swapped = []
    if white_mat is not None:
        for o in sources:
            for slot in o.material_slots: swapped.append((slot, slot.material)); slot.material = white_mat
    old_hide = {o.name: (o.hide_render, o.hide_get(), o.hide_viewport) for o in bpy.data.objects}
    for o in bpy.data.objects:
        if o.type in ('MESH', 'CURVES'): o.hide_render = (o not in sources and o is not cards)
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in sources: o.hide_set(False); o.hide_viewport = False; o.select_set(True)
    cards.hide_set(False); cards.hide_viewport = False; cards.select_set(True); bpy.context.view_layer.objects.active = cards
    mat = cards.data.materials[0]; tex = next(n for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE'); tex.image = image; mat.node_tree.nodes.active = tex
    t0 = time.time()
    res = bpy.ops.object.bake(type=kind)
    print("bake", kind, res, round(time.time() - t0, 1), "s")
    for slot, m in swapped: slot.material = m
    for n, (hr, hs, hv) in old_hide.items():
        o = bpy.data.objects.get(n)
        if o: o.hide_render = hr; o.hide_set(hs); o.hide_viewport = hv
    sc.cycles.samples = old_samples; sc.render.engine = old_engine
    return res

def white_material(name='HairWhite'):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); em = nt.nodes.new('ShaderNodeEmission'); em.inputs['Color'].default_value = (1, 1, 1, 1); em.inputs['Strength'].default_value = 1.0
    nt.links.new(em.outputs['Emission'], out.inputs['Surface']); return mat

def compose_atlas(color_img, cover_img, path):
    w, h = color_img.size
    c = np.empty(w * h * 4, np.float32); color_img.pixels.foreach_get(c); c = c.reshape(h, w, 4)
    a = np.empty(w * h * 4, np.float32); cover_img.pixels.foreach_get(a); a = a.reshape(h, w, 4)
    rgba = c.copy(); rgba[..., 3] = np.clip(a[..., 0], 0, 1)
    out = bpy.data.images.get('HairCardsAtlas') or bpy.data.images.new('HairCardsAtlas', w, h, alpha=True)
    if out.size[0] != w or out.size[1] != h: out.scale(w, h)
    out.pixels.foreach_set(rgba.ravel()); out.filepath_raw = path; out.file_format = 'PNG'; out.save()
    return out, float((rgba[..., 3] > 0.5).mean())


def add_strand_attributes(ob, seed=5):
    """per-curve 'hair_random' and per-point 'hair_t' so a mesh copy can shade like the curves"""
    cu = ob.data; rng = random.Random(seed)
    for n in ('hair_random', 'hair_t'):
        a = cu.attributes.get(n)
        if a: cu.attributes.remove(a)
    offs = np.empty(len(cu.curves) + 1, np.int64); cu.curve_offset_data.foreach_get('value', offs)
    rnd = cu.attributes.new('hair_random', 'FLOAT', 'CURVE'); rnd.data.foreach_set('value', [rng.random() for _ in range(len(cu.curves))])
    t = np.zeros(len(cu.points), np.float32)
    for i in range(len(cu.curves)):
        a, b = offs[i], offs[i + 1]; n = b - a
        t[a:b] = np.linspace(0, 1, n) if n > 1 else 0
    ta = cu.attributes.new('hair_t', 'FLOAT', 'POINT'); ta.data.foreach_set('value', t.tolist())

def strand_mesh_material(name='HairStrandMesh'):
    src = bpy.data.materials['HairStrand']
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    ar = nt.nodes.new('ShaderNodeAttribute'); ar.attribute_name = 'hair_random'; at = nt.nodes.new('ShaderNodeAttribute'); at.attribute_name = 'hair_t'
    ramp_src = next(n for n in src.node_tree.nodes if n.type == 'VALTORGB')
    ramp = nt.nodes.new('ShaderNodeValToRGB')
    for i, e in enumerate(ramp_src.color_ramp.elements): ramp.color_ramp.elements[i].color = e.color; ramp.color_ramp.elements[i].position = e.position
    mr = nt.nodes.new('ShaderNodeMapRange'); mr.inputs['To Min'].default_value = 0.55; mr.inputs['To Max'].default_value = 1.0
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs['Factor'].default_value = 1.0
    nt.links.new(ar.outputs['Fac'], ramp.inputs['Fac']); nt.links.new(at.outputs['Fac'], mr.inputs['Value'])
    nt.links.new(ramp.outputs['Color'], mix.inputs[6]); nt.links.new(mr.outputs['Result'], mix.inputs[7]); nt.links.new(mix.outputs[2], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.6; nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
    return mat

def make_strand_mesh(ob, name, resample=0.008, sides=3, radius=0.0012):
    """a mesh copy of a curves object: tubes with `sides` sides scaled by the radius attribute, built by a temporary
    mesh object whose geometry nodes pull the curves through an Object Info node (new_from_object needs a mesh object)"""
    for nm in (name, name + 'Tmp'):
        old = bpy.data.objects.get(nm)
        if old:
            od = old.data; bpy.data.objects.remove(old); bpy.data.meshes.remove(od)
    ng = bpy.data.node_groups.get('_StrandTube')
    if ng: bpy.data.node_groups.remove(ng)
    ng = bpy.data.node_groups.new('_StrandTube', 'GeometryNodeTree')
    ng.interface.new_socket('Geometry', in_out='INPUT', socket_type='NodeSocketGeometry'); ng.interface.new_socket('Geometry', in_out='OUTPUT', socket_type='NodeSocketGeometry')
    go = ng.nodes.new('NodeGroupOutput')
    oi = ng.nodes.new('GeometryNodeObjectInfo'); oi.inputs['Object'].default_value = ob; oi.transform_space = 'ORIGINAL'
    rs = ng.nodes.new('GeometryNodeResampleCurve')
    if hasattr(rs, 'mode'): rs.mode = 'LENGTH'
    else:
        ms = next((i for i in rs.inputs if i.name == 'Mode'), None)
        if ms is not None: ms.default_value = 'Length'
    rs.inputs['Length'].default_value = resample
    circ = ng.nodes.new('GeometryNodeCurvePrimitiveCircle'); circ.inputs['Resolution'].default_value = sides; circ.inputs['Radius'].default_value = radius   # a fixed radius: Curve to Mesh does not scale by the radius attribute here (1 m tubes hung the bake for 26 min)
    c2m = ng.nodes.new('GeometryNodeCurveToMesh')
    ng.links.new(oi.outputs['Geometry'], rs.inputs['Curve']); ng.links.new(rs.outputs['Curve'], c2m.inputs['Curve']); ng.links.new(circ.outputs['Curve'], c2m.inputs['Profile Curve'])
    ng.links.new(c2m.outputs['Mesh'], go.inputs['Geometry'])
    tmp_me = bpy.data.meshes.new(name + 'Tmp'); tmp = bpy.data.objects.new(name + 'Tmp', tmp_me); bpy.data.collections['Collection'].objects.link(tmp)
    tmp.matrix_world = ob.matrix_world.copy()
    m = tmp.modifiers.new('_tube', 'NODES'); m.node_group = ng
    dg = bpy.context.evaluated_depsgraph_get(); ev = tmp.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev); me.name = name
    bpy.data.objects.remove(tmp); bpy.data.meshes.remove(tmp_me); bpy.data.node_groups.remove(ng)
    me.materials.append(strand_mesh_material())
    mo = bpy.data.objects.new(name, me); bpy.data.collections['Collection'].objects.link(mo)
    mo.parent = ob.parent; mo.matrix_parent_inverse = ob.matrix_parent_inverse.copy(); mo.matrix_world = ob.matrix_world.copy()
    return mo

def export_cards_glb(cards, path, atlas_path=None, extra=()):
    """GLB of the cards mesh (and extra objects such as the cap) alone (Y-up, PNG atlas embedded); the JSON chunk is then
    patched so every material with a texture is alphaMode MASK (cutoff 0.5) and all are doubleSided. Returns (bytes, json)."""
    import json, struct
    objs = [cards] + list(extra); saved = []
    for o in objs:
        saved.append((o, o.parent, o.matrix_world.copy())); mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in objs: o.hide_set(False); o.hide_viewport = False; o.hide_render = False; o.select_set(True)
    bpy.context.view_layer.objects.active = cards
    kw = dict(filepath=path, export_format='GLB', use_selection=True, export_apply=True, export_materials='EXPORT', export_texcoords=True,
              export_normals=True, export_yup=True, export_animations=False, export_skins=False, export_morph=False, export_lights=False, export_cameras=False)
    try: bpy.ops.export_scene.gltf(**kw, export_image_format='AUTO')
    except TypeError: bpy.ops.export_scene.gltf(**kw)
    for o, par, mw in saved:
        o.parent = par; o.matrix_parent_inverse = par.matrix_world.inverted() if par else Matrix.Identity(4); o.matrix_world = mw
    data = open(path, 'rb').read()
    magic, ver, total = struct.unpack_from('<III', data, 0); assert magic == 0x46546C67, "not a GLB"
    jl, jt = struct.unpack_from('<II', data, 12); js = json.loads(data[20:20 + jl].decode()); rest = data[20 + jl:]
    for m in js.get('materials', []):
        m['doubleSided'] = True
        if 'Cards' in (m.get('name') or ''): m['alphaMode'] = 'MASK'; m['alphaCutoff'] = 0.5
        else: m.pop('alphaMode', None); m.pop('alphaCutoff', None)   # the cap is opaque
    jb = json.dumps(js, separators=(',', ':')).encode(); jb += b' ' * ((4 - len(jb) % 4) % 4)
    out = struct.pack('<III', magic, ver, 12 + 8 + len(jb) + len(rest)) + struct.pack('<II', len(jb), jt) + jb + rest
    open(path, 'wb').write(out)
    print("GLB", path, len(out), "bytes; materials", [(m.get('name'), m.get('alphaMode'), m.get('doubleSided')) for m in js.get('materials', [])], "images", [(i.get('mimeType'), i.get('name')) for i in js.get('images', [])], "meshes", [(m['name'], [p.get('mode', 4) for p in m['primitives']]) for m in js['meshes']])
    return out, js


def card_material_lab(name='HairCards', image=None, emissive=0.9):
    """the package's hair look: the texture nine tenths emissive over a tenth of lit base colour; alpha clip"""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled'); tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = image; tex.interpolation = 'Linear'
    dim = nt.nodes.new('ShaderNodeMix'); dim.data_type = 'RGBA'; dim.blend_type = 'MULTIPLY'; dim.inputs['Factor'].default_value = 1.0; dim.inputs[7].default_value = (1 - emissive, 1 - emissive, 1 - emissive, 1)
    nt.links.new(tex.outputs['Color'], dim.inputs[6]); nt.links.new(dim.outputs[2], bsdf.inputs['Base Color'])
    thr = nt.nodes.new('ShaderNodeMath'); thr.operation = 'GREATER_THAN'; thr.inputs[1].default_value = 0.5   # the lab's alphaMode MASK cutoff, not EEVEE's dithering
    nt.links.new(tex.outputs['Alpha'], thr.inputs[0]); nt.links.new(thr.outputs[0], bsdf.inputs['Alpha'])
    bsdf.inputs['Roughness'].default_value = 1.0
    if 'Emission Color' in bsdf.inputs:
        nt.links.new(tex.outputs['Color'], bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = emissive
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface']); nt.nodes.active = tex
    mat.blend_method = 'CLIP' if 'CLIP' in [i.identifier for i in bpy.types.Material.bl_rna.properties['blend_method'].enum_items] else 'HASHED'
    mat.alpha_threshold = 0.5; mat.use_backface_culling = False
    try: mat.surface_render_method = 'DITHERED'
    except Exception: pass
    return mat


def add_filler_guides(F, min_dist=0.032, candidates=3000, seed=21):
    """guides where no guide root lies within min_dist (the flow's sources: the parting, the crown) so every region
    starts a card; the children keep their t16 guides (the strips take their strands from the slab anyway)"""
    from mathutils import kdtree
    P = F.P; rng = random.Random(seed + 7)
    roots = [g['root'] for g in F.guides]
    added = []
    for pt, n, u in F.roots(candidates, seed):
        if min((pt - r).length for r in roots) < min_dist: continue
        lam = (0.95, 0.75)[len(added) % 2]; phase = rng.uniform(0, 2 * math.pi); amp = P['wave_amp'] * rng.uniform(0.7, 1.3); wl = P['wave_len'] * rng.uniform(0.85, 1.15)
        F.bias = (rng.random() - 0.5) * 0.3
        pts, ss, why = F.trace_wave(pt, n, lam, phase, amp, wl, P['wave_rad'])
        g = dict(root=pt, pts=pts, ss=ss, lam=lam, phase=phase, amp=amp, wl=wl, bias=F.bias, why=why, side=1 if pt.x - F.x_part(pt.y) >= 0 else -1, filler=True)
        F.guides.append(g); roots.append(pt); added.append(g)
    return added

def dump_cards(F, strands, gidx, info, path, cols, rows, n_samples=16):
    offs = np.zeros(len(strands) + 1, np.int64)
    for i, s in enumerate(strands): offs[i + 1] = offs[i] + len(s)
    pos = np.zeros((offs[-1], 3), np.float32)
    for i, s in enumerate(strands): pos[offs[i]:offs[i + 1]] = np.array([tuple(p) for p in s], np.float32)
    cu = bpy.data.objects['KairoHair'].data
    rnd = np.zeros(len(cu.curves), np.float32); cu.attributes['hair_random'].data.foreach_get('value', rnd)
    samples = np.zeros((len(info), n_samples, 3), np.float32); tang = np.zeros_like(samples); norm = np.zeros_like(samples); lat = np.zeros_like(samples)
    width = np.zeros(len(info), np.float32); strip = np.zeros(len(info), np.int32); guide_of = np.zeros(len(info), np.int32); glen = np.zeros(len(info), np.float32); glam = np.zeros(len(info), np.float32)
    for k, ci in enumerate(info):
        g = F.guides[int(ci['guide'])]; sm, L = resample(g['pts'], g['ss'], n_samples); fr = card_frames(F, sm)
        samples[k] = np.array([tuple(p) for p in sm], np.float32)
        tang[k] = np.array([tuple(t) for t, n, b in fr], np.float32); norm[k] = np.array([tuple(n) for t, n, b in fr], np.float32); lat[k] = np.array([tuple(b) for t, n, b in fr], np.float32)
        width[k] = ci['width']; strip[k] = ci['strip']; guide_of[k] = ci['guide']; glen[k] = L; glam[k] = ci['lam']
    card_of_guide = -np.ones(len(F.guides), np.int32)
    for k in range(len(info)): card_of_guide[guide_of[k]] = k
    np.savez(path, pos=pos, offs=offs, gidx=np.array(gidx, np.int32), lams=np.zeros(len(strands), np.float32), rnd=rnd, samples=samples, tang=tang, norm=norm, lat=lat,
             width=width, strip=strip, guide_of=guide_of, glen=glen, glam=glam, card_of_guide=card_of_guide, piv=np.array(tuple(PIV), np.float32), cols=cols, rows=rows)


def build_cap(name='KairoCap', source='ScalpHair', offset=0.0025, colour=(0.075, 0.08, 0.22), material='HairCap', emissive=0.9):
    """the scalp cap: the emitter faces lifted along their smoothed vertex normals, one dark opaque material; hides the skull
    between the cards (and the crown's light spot in the skull texture)"""
    old = bpy.data.objects.get(name)
    if old:
        od = old.data; bpy.data.objects.remove(old); bpy.data.meshes.remove(od)
    src_ob = bpy.data.objects[source]; me = src_ob.data.copy(); me.name = name
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    mw = src_ob.matrix_world; rot = mw.to_3x3()
    for v in bm.verts:
        n = v.normal
        if n.length == 0: continue
        wn = (rot @ n).normalized(); wp = mw @ v.co
        if wn.dot(wp - PIV) < 0: wn = -wn
        v.co = v.co + (rot.inverted() @ wn) * offset
    bm.to_mesh(me); bm.free()
    me.materials.clear()
    mat = bpy.data.materials.get(material) or bpy.data.materials.new(material); mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Base Color'].default_value = (colour[0] * (1 - emissive), colour[1] * (1 - emissive), colour[2] * (1 - emissive), 1); bsdf.inputs['Roughness'].default_value = 1.0
    if 'Emission Color' in bsdf.inputs: bsdf.inputs['Emission Color'].default_value = (*colour, 1); bsdf.inputs['Emission Strength'].default_value = emissive
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface']); mat.use_backface_culling = False
    me.materials.append(mat)
    ob = src_ob.copy(); ob.data = me; ob.name = name; bpy.data.collections['Collection'].objects.link(ob); ob.hide_render = False; ob.hide_set(False)
    return ob


def build_cap_zones(name='KairoCap', offset=0.0025, colour=(0.075, 0.08, 0.22), material='HairCap', emissive=0.9):
    """the cap from every skull face under the hair (ScalpZones class 1 = skin under hair, 3 = dark under hair), minus the eye
    zone and the forehead below the mirrored hairline: the crown's light spot in the skull texture is covered too"""
    EYES = (Vector((0.09, -0.137, 0.084)), Vector((-0.078, -0.138, 0.082)))
    HL = [(0.0, 0.262), (0.05, 0.262), (0.08, 0.244), (0.10, 0.232), (0.12, 0.20)]
    def hairline_z(ax):
        if ax <= HL[0][0]: return HL[0][1]
        for (a, za), (b, zb) in zip(HL, HL[1:]):
            if ax <= b: return za + (zb - za) * (ax - a) / (b - a)
        return HL[-1][1]
    zones = bpy.data.objects['ScalpZones']; zm = zones.data; mw = zones.matrix_world; zv = [mw @ v.co for v in zm.vertices]; zone = zm.attributes['zone'].data
    old = bpy.data.objects.get(name)
    if old:
        od = old.data; bpy.data.objects.remove(old); bpy.data.meshes.remove(od)
    bm = bmesh.new(); bm.from_mesh(zm); bm.faces.ensure_lookup_table(); kill = []
    for f in bm.faces:
        k = zone[f.index].value; c = sum((zv[v.index] for v in f.verts), Vector()) / len(f.verts)
        skull = c.z > PIV.z + 0.04 or (c.y - PIV.y) > 0.03   # the skull only: skin under the hair volume at the temple and the cheek in front of the ear showed as a dark patch in the lab
        keep = k in (1, 3) and skull and min((c - e).length for e in EYES) >= 0.045 and not ((c.y - PIV.y) < -0.06 and abs(c.x) < 0.12 and c.z < hairline_z(abs(c.x)) - 0.008)
        if not keep: kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    rot = mw.to_3x3(); bm.normal_update()
    for v in bm.verts:
        n = v.normal
        if n.length == 0: continue
        wn = (rot @ n).normalized(); wp = mw @ v.co
        if wn.dot(wp - PIV) < 0: wn = -wn
        v.co = v.co + (rot.inverted() @ wn) * offset
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    mat = bpy.data.materials.get(material) or bpy.data.materials.new(material); mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Base Color'].default_value = (colour[0] * (1 - emissive), colour[1] * (1 - emissive), colour[2] * (1 - emissive), 1); bsdf.inputs['Roughness'].default_value = 1.0
    if 'Emission Color' in bsdf.inputs: bsdf.inputs['Emission Color'].default_value = (*colour, 1); bsdf.inputs['Emission Strength'].default_value = emissive
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface']); mat.use_backface_culling = False
    me.materials.append(mat)
    ob = zones.copy(); ob.data = me; ob.name = name; bpy.data.collections['Collection'].objects.link(ob); ob.hide_render = False; ob.hide_set(False)
    return ob


def cap_material_lab(name='HairCap', image=None, emissive=0.9):
    """the cap with its strand texture, opaque, nine tenths emissive like the package's hair"""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled'); tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = image
    dim = nt.nodes.new('ShaderNodeMix'); dim.data_type = 'RGBA'; dim.blend_type = 'MULTIPLY'; dim.inputs['Factor'].default_value = 1.0; dim.inputs[7].default_value = (1 - emissive, 1 - emissive, 1 - emissive, 1)
    nt.links.new(tex.outputs['Color'], dim.inputs[6]); nt.links.new(dim.outputs[2], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 1.0
    if 'Emission Color' in bsdf.inputs: nt.links.new(tex.outputs['Color'], bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = emissive
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface']); nt.nodes.active = tex; mat.use_backface_culling = False
    try: mat.blend_method = 'OPAQUE'
    except Exception: pass
    return mat


FLAT_PULL = 0.4      # the flat layers' children keep most of their own spread: clumped at 0.85 each card was a narrow bundle with transparent sides (the cap showed between the bundles)
FLAT_SPREAD = 0.02   # root jitter round the guide root (m)
FRINGE_PARAMS = dict(bangs_comb=0.6, bangs_forward=0.6, bangs_grav=1.15, fringe_lam=0.3)   # deep: the fringe hangs 1-3 cm in front of the forehead instead of on the bangs' outer bulge   # the fringe layer falls steeper (about 60 deg) than t16's bangs, so its cards reach the brows across the forehead

def add_fringe_guides(F, xs=None, seed=33):
    """guides rooted on the front hairline on her right of the parting (the bangs): one every 12 mm from x -0.12 to +0.02,
    so the forehead under the bangs is covered by cards from the hairline down"""
    P = F.P; rng = random.Random(seed); added = []
    saved = dict(P); P.update(FRINGE_PARAMS)
    xs = xs if xs is not None else [x / 1000 for x in range(-120, 30, 12)]
    for x in xs:
        col = [v for v in F.em_v if abs(v.x - x) < 0.006 and v.z > PIV.z + 0.08]
        if not col: continue
        pt = min(col, key=lambda v: v.y); n = (pt - PIV).normalized()
        lam = P.get('fringe_lam', 0.3); phase = rng.uniform(0, 2 * math.pi); amp = P['wave_amp'] * rng.uniform(0.7, 1.3); wl = P['wave_len'] * rng.uniform(0.85, 1.15)
        F.bias = (rng.random() - 0.5) * 0.3
        pts, ss, why = F.trace_wave(pt, n, lam, phase, amp, wl, P['wave_rad'])
        if len(pts) < 4: continue
        g = dict(root=pt, pts=pts, ss=ss, lam=lam, phase=phase, amp=amp, wl=wl, bias=F.bias, why=why, side=1 if pt.x - F.x_part(pt.y) >= 0 else -1, fringe=True)
        F.guides.append(g); added.append(g)
    P.clear(); P.update(saved)
    return added


def grow_fringe_children(F, fringe, per_guide=60, spread=FLAT_SPREAD, seed=44):
    """children for the fringe guides: roots jittered on the emitter within `spread` of the guide's root, the guide's wave
    params with jitter, clumped to the guide like grow_styled's children. Returns (strands, gidx) to append."""
    P = F.P; rng = random.Random(seed)
    saved = dict(P); P.update(FRINGE_PARAMS)
    em_bvh = bvh_of(F.emitter)[0]
    out = []; gidx = []
    for g in fringe:
        gi = F.guides.index(g); Lg = g['ss'][-1]
        for _ in range(per_guide):
            off = Vector((rng.uniform(-spread, spread), rng.uniform(-spread, spread), rng.uniform(-spread, spread)))
            loc, nrm, idx, dist = em_bvh.find_nearest(g['root'] + off)
            if loc is None: continue
            n = (loc - PIV).normalized(); u = rng.random(); j = P['child_jitter']
            lam = min(1.0, max(0.1, g['lam'] + (u - 0.5) * 0.3)); F.bias = g['bias'] + (rng.random() - 0.5) * 0.1
            pts, ss, why = F.trace_wave(loc, n, lam, g['phase'] + (rng.random() - 0.5) * j * 1.5, g['amp'] * (1 + (u - 0.5) * j), g['wl'], P['wave_rad'])
            if len(pts) < 3: continue
            res = []
            for p, s in zip(pts, ss):
                if s > Lg + 0.02: break
                w = FLAT_PULL * (1 - math.exp(-s / P['clump_rise']))
                if Lg > 0: w *= 1 - P['tip_spread'] * smoothstep((s - 0.75 * Lg) / (0.25 * Lg))
                res.append(F.push_out(p + (F.along(g['pts'], g['ss'], s) - p) * w))
            if len(res) >= 3: out.append(res); gidx.append(gi)
    P.clear(); P.update(saved)
    return out, gidx


def parting_zone_coverage(F, cards_ob, cap_ob, half=0.04, zmin=None):
    """share of the cap's faces within `half` of the parting (x) on the top of the head that have a card in front of
    them along their normal (within 15 cm): the owner's "the top looks odd" measure"""
    zmin = PIV.z + 0.14 if zmin is None else zmin
    cb = bvh_of(cards_ob)[0]; mw = cap_ob.matrix_world; rot = mw.to_3x3(); n = cov = 0
    for p in cap_ob.data.polygons:
        c = mw @ p.center
        if c.z < zmin or abs(c.x - F.x_part(c.y)) > half: continue
        nrm = (rot @ p.normal).normalized()
        if nrm.dot(c - PIV) < 0: nrm = -nrm
        n += 1
        if cb.ray_cast(c + nrm * 0.002, nrm, 0.15)[0] is not None: cov += 1
    return cov, n

PARTING_PARAMS = dict(parting_lam=0.12, parting_child_lam=(0.06, 0.25))

def add_parting_guides(F, step=0.012, offset=0.012, seed=55):
    """guides rooted on both sides of the parting (x_part(y) +- offset) from the front hairline to the crown's back,
    one every `step` along y, lying close to the scalp (parting_lam) and flowing with the default comb (away from the
    parting and back): cards for the top of the head, whose own guide roots were random"""
    P = F.P; rng = random.Random(seed); added = []
    em_bvh = bvh_of(F.emitter)[0]
    y0, y1 = F.parting[0].y, F.parting[-1].y
    y = y0
    while y <= y1 + 1e-6:
        for side in (-1, 1):
            x = F.x_part(y) + side * offset
            loc, nrm, idx, dist = em_bvh.find_nearest(Vector((x, y, PIV.z + 0.3)))
            if loc is None or dist > 0.08: continue
            n = (loc - PIV).normalized(); lam = PARTING_PARAMS['parting_lam']
            phase = rng.uniform(0, 2 * math.pi); amp = P['wave_amp'] * rng.uniform(0.6, 1.0); wl = P['wave_len'] * rng.uniform(0.9, 1.2)
            F.bias = (rng.random() - 0.5) * 0.2
            pts, ss, why = F.trace_wave(loc, n, lam, phase, amp, wl, P['wave_rad'] * 0.5)
            if len(pts) < 4: continue
            g = dict(root=loc, pts=pts, ss=ss, lam=lam, phase=phase, amp=amp, wl=wl, bias=F.bias, why=why, side=side, parting=True)
            F.guides.append(g); added.append(g)
        y += step
    return added

def grow_parting_children(F, guides, per_guide=60, spread=FLAT_SPREAD, seed=56):
    """flat children for the parting guides (lam parting_child_lam), clumped to their guide like the fringe's"""
    P = F.P; rng = random.Random(seed); em_bvh = bvh_of(F.emitter)[0]; lo, hi = PARTING_PARAMS['parting_child_lam']
    out = []; gidx = []
    for g in guides:
        gi = F.guides.index(g); Lg = g['ss'][-1]
        for _ in range(per_guide):
            off = Vector((rng.uniform(-spread, spread), rng.uniform(-spread, spread), rng.uniform(-spread, spread)))
            loc, nrm, idx, dist = em_bvh.find_nearest(g['root'] + off)
            if loc is None: continue
            n = (loc - PIV).normalized(); u = rng.random(); j = P['child_jitter']
            lam = lo + (hi - lo) * u; F.bias = g['bias'] + (rng.random() - 0.5) * 0.1
            pts, ss, why = F.trace_wave(loc, n, lam, g['phase'] + (rng.random() - 0.5) * j * 1.5, g['amp'] * (1 + (u - 0.5) * j), g['wl'], P['wave_rad'] * 0.5)
            if len(pts) < 3: continue
            res = []
            for p, s in zip(pts, ss):
                if s > Lg + 0.02: break
                w = FLAT_PULL * (1 - math.exp(-s / P['clump_rise']))
                if Lg > 0: w *= 1 - P['tip_spread'] * smoothstep((s - 0.75 * Lg) / (0.25 * Lg))
                res.append(F.push_out(p + (F.along(g['pts'], g['ss'], s) - p) * w))
            if len(res) >= 3: out.append(res); gidx.append(gi)
    return out, gidx


SWEEP_PARAMS = dict(back_drift=1.0, grav_top=0.3, part_back=0.0, bangs_comb=1.0, lam=0.15, child_lam=(0.08, 0.3))

def add_sweep_guides(F, xs=None, seed=66):
    """guides rooted on her LEFT front hairline (x > the parting), combed back-and-sideways over the crown close to the
    scalp: in the reference that side is swept back from the hairline; without them the hairline band above her left
    brow had no card in front of the cap (51 % of the cap's pixels at yaw 0)"""
    P = F.P; rng = random.Random(seed); added = []; saved = dict(P); P.update({k: v for k, v in SWEEP_PARAMS.items() if k in P})
    xs = xs if xs is not None else [x / 1000 for x in range(20, 140, 10)]
    for x in xs:
        col = [v for v in F.em_v if abs(v.x - x) < 0.006 and v.z > PIV.z + 0.08]
        if not col: continue
        pt = min(col, key=lambda v: v.y); n = (pt - PIV).normalized(); lam = SWEEP_PARAMS['lam']
        phase = rng.uniform(0, 2 * math.pi); amp = P['wave_amp'] * rng.uniform(0.6, 1.0); wl = P['wave_len'] * rng.uniform(0.9, 1.2); F.bias = (rng.random() - 0.5) * 0.2
        pts, ss, why = F.trace_wave(pt, n, lam, phase, amp, wl, P['wave_rad'] * 0.5)
        if len(pts) < 4: continue
        g = dict(root=pt, pts=pts, ss=ss, lam=lam, phase=phase, amp=amp, wl=wl, bias=F.bias, why=why, side=1, sweep=True)
        F.guides.append(g); added.append(g)
    P.clear(); P.update(saved)
    return added

def grow_layer_children(F, guides, lam_range, per_guide=60, spread=FLAT_SPREAD, seed=67, params=None):
    """flat children for a guide layer (fringe/parting/sweep), clumped to their guide; params override the comb while tracing"""
    P = F.P; rng = random.Random(seed); em_bvh = bvh_of(F.emitter)[0]; lo, hi = lam_range
    saved = dict(P)
    if params: P.update({k: v for k, v in params.items() if k in P})
    out = []; gidx = []
    for g in guides:
        gi = F.guides.index(g); Lg = g['ss'][-1]
        for _ in range(per_guide):
            off = Vector((rng.uniform(-spread, spread), rng.uniform(-spread, spread), rng.uniform(-spread, spread)))
            loc, nrm, idx, dist = em_bvh.find_nearest(g['root'] + off)
            if loc is None: continue
            n = (loc - PIV).normalized(); u = rng.random(); j = P['child_jitter']
            lam = lo + (hi - lo) * u; F.bias = g['bias'] + (rng.random() - 0.5) * 0.1
            pts, ss, why = F.trace_wave(loc, n, lam, g['phase'] + (rng.random() - 0.5) * j * 1.5, g['amp'] * (1 + (u - 0.5) * j), g['wl'], P['wave_rad'] * 0.5)
            if len(pts) < 3: continue
            res = []
            for p, s in zip(pts, ss):
                if s > Lg + 0.02: break
                w = FLAT_PULL * (1 - math.exp(-s / P['clump_rise']))
                if Lg > 0: w *= 1 - P['tip_spread'] * smoothstep((s - 0.75 * Lg) / (0.25 * Lg))
                res.append(F.push_out(p + (F.along(g['pts'], g['ss'], s) - p) * w))
            if len(res) >= 3: out.append(res); gidx.append(gi)
    P.clear(); P.update(saved)
    return out, gidx

def camera_cap_exposure(F, cards_ob, cap_ob, yaws=(0, 60, -60), step=10):
    """share of the cap's pixels (a `step` px grid over the head in the 1200 px LabCamera frame) with no card in front"""
    cam = bpy.data.objects['LabCamera']; cm = cam.matrix_world; origin = cm.translation; t = math.tan(math.radians(11.0)); root = bpy.data.objects['Root']; out = {}
    for yaw in yaws:
        root.rotation_euler[2] = math.radians(yaw); bpy.context.view_layer.update()
        cb = bvh_of(cards_ob)[0]; capb = bvh_of(cap_ob)[0]; hb = bvh_of(bpy.data.objects['Head'])[0]; exposed = n = 0; where = []
        for px in range(300, 900, step):
            for py in range(100, 700, step):
                nx = (px + 0.5) / 1200 * 2 - 1; ny = 1 - (py + 0.5) / 1200 * 2; d = (cm.to_3x3() @ Vector((nx * t, ny * t, -1.0))).normalized()
                hc = capb.ray_cast(origin, d, 6.0)
                if hc[0] is None: continue
                hh = hb.ray_cast(origin, d, 6.0)
                if hh[0] is not None and hh[3] < hc[3]: continue      # the cap behind the head's own surface (seen through the face) is not visible
                n += 1; hk = cb.ray_cast(origin, d, 6.0)
                if hk[0] is None or hk[3] > hc[3]: exposed += 1; where.append((px, py, hc[0]))
        out[yaw] = (exposed, n, where)
    root.rotation_euler[2] = 0; bpy.context.view_layer.update()
    return out

def dump_layers(F, all_strands, all_gidx, info, rnd, path, cols, rows, n_samples=16):
    offs = np.zeros(len(all_strands) + 1, np.int64)
    for i, s in enumerate(all_strands): offs[i + 1] = offs[i] + len(s)
    pos = np.zeros((offs[-1], 3), np.float32)
    for i, s in enumerate(all_strands): pos[offs[i]:offs[i + 1]] = np.array([tuple(p) for p in s], np.float32)
    samples = np.zeros((len(info), n_samples, 3), np.float32); tang = np.zeros_like(samples); norm = np.zeros_like(samples); lat = np.zeros_like(samples)
    width = np.zeros(len(info), np.float32); strip = np.zeros(len(info), np.int32); guide_of = np.zeros(len(info), np.int32); glen = np.zeros(len(info), np.float32); glam = np.zeros(len(info), np.float32)
    for k, ci in enumerate(info):
        g = F.guides[int(ci['guide'])]; sm, L = resample(g['pts'], g['ss'], n_samples); frm = card_frames(F, sm)
        samples[k] = np.array([tuple(p) for p in sm], np.float32); tang[k] = np.array([tuple(tt) for tt, n, b in frm], np.float32); norm[k] = np.array([tuple(n) for tt, n, b in frm], np.float32); lat[k] = np.array([tuple(b) for tt, n, b in frm], np.float32)
        width[k] = ci['width']; strip[k] = ci['strip']; guide_of[k] = ci['guide']; glen[k] = L; glam[k] = ci['lam']
    card_of_guide = -np.ones(len(F.guides), np.int32)
    for k in range(len(info)): card_of_guide[guide_of[k]] = k
    np.savez(path, pos=pos, offs=offs, gidx=np.array(all_gidx, np.int32), lams=np.zeros(len(all_strands), np.float32), rnd=rnd, samples=samples, tang=tang, norm=norm, lat=lat,
             width=width, strip=strip, guide_of=guide_of, glen=glen, glam=glam, card_of_guide=card_of_guide, piv=np.array(tuple(PIV), np.float32), cols=cols, rows=rows)


def exposure_with_alpha(F, cards_ob, cap_ob, atlas_img, yaw, box, step=8, cols=32):
    """like the lab: rays through a pixel grid (1200 px frame, box in that frame), every card hit in front of the cap
    sampled in the atlas (Blender's own pixel convention); a pixel is exposed when no hit is opaque (alpha >= 0.5).
    Returns exposed/visible counts and, for the exposed pixels, which strips were hit transparent and how many had no card."""
    import numpy as np
    cam = bpy.data.objects['LabCamera']; cm = cam.matrix_world; origin = cm.translation; t = math.tan(math.radians(11.0)); root = bpy.data.objects['Root']
    root.rotation_euler[2] = math.radians(yaw); bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get(); ev = cards_ob.evaluated_get(dg); me = ev.to_mesh(); mw = cards_ob.matrix_world
    verts = [mw @ v.co for v in me.vertices]; me.calc_loop_triangles(); tris = [tuple(tr.vertices) for tr in me.loop_triangles]
    tri_loops = [tuple(tr.loops) for tr in me.loop_triangles]; uvl = me.uv_layers.active.data; uvs = [(uvl[l].uv.x, uvl[l].uv.y) for l in range(len(uvl))]
    cb = BVHTree.FromPolygons(verts, tris); capb = bvh_of(cap_ob)[0]; hb = bvh_of(bpy.data.objects['Head'])[0]
    W, H = atlas_img.size; px = np.empty(W * H * 4, np.float32); atlas_img.pixels.foreach_get(px); alpha = px.reshape(H, W, 4)[..., 3]
    def uv_at(idx, loc):
        a, b, c = (verts[i] for i in tris[idx]); ua, ub, uc = (uvs[l] for l in tri_loops[idx])
        v0 = b - a; v1 = c - a; v2 = loc - a; d00 = v0.dot(v0); d01 = v0.dot(v1); d11 = v1.dot(v1); d20 = v2.dot(v0); d21 = v2.dot(v1); den = d00 * d11 - d01 * d01
        if abs(den) < 1e-14: return ua
        w1 = (d11 * d20 - d01 * d21) / den; w2 = (d00 * d21 - d01 * d20) / den; w0 = 1 - w1 - w2
        return (w0 * ua[0] + w1 * ub[0] + w2 * uc[0], w0 * ua[1] + w1 * ub[1] + w2 * uc[1])
    x0, y0, x1, y1 = box; exposed = visible = nocard = 0; strips = {}
    for pxx in range(x0, x1, step):
        for pyy in range(y0, y1, step):
            nx = (pxx + 0.5) / 1200 * 2 - 1; ny = 1 - (pyy + 0.5) / 1200 * 2; d = (cm.to_3x3() @ Vector((nx * t, ny * t, -1.0))).normalized()
            hc = capb.ray_cast(origin, d, 6.0)
            if hc[0] is None: continue
            hh = hb.ray_cast(origin, d, 6.0)
            if hh[0] is not None and hh[3] < hc[3]: continue
            visible += 1; o = origin.copy(); dist = 0.0; opaque = False; hit_strips = []
            for _ in range(40):
                loc, nrm, idx, dd = cb.ray_cast(o, d, 6.0)
                if loc is None: break
                dist += dd
                if dist >= hc[3]: break
                u, v = uv_at(idx, loc); a = alpha[min(H - 1, max(0, int(v * H))), min(W - 1, max(0, int(u * W)))]
                hit_strips.append(int(u * cols) + cols * int(v * (H / (W / cols * 8))))
                if a >= 0.5: opaque = True; break
                o = loc + d * 1e-5
            if not opaque:
                exposed += 1
                if not hit_strips: nocard += 1
                for s in set(hit_strips): strips[s] = strips.get(s, 0) + 1
    ev.to_mesh_clear(); root.rotation_euler[2] = 0; bpy.context.view_layer.update()
    return exposed, visible, nocard, strips
