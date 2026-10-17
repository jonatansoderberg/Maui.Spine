import bpy, bmesh, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def ear_bvh():
    dg = bpy.context.evaluated_depsgraph_get()
    V, T = [], []
    for name in ('Head', 'Neck'):
        ob = bpy.data.objects[name]
        me = ob.evaluated_get(dg).to_mesh()
        me.calc_loop_triangles()
        mi = {i for i, m in enumerate(ob.data.materials) if m and m.name == 'ear'}
        off = len(V)
        V += [ob.matrix_world @ v.co for v in me.vertices]
        T += [tuple(i + off for i in t.vertices) for t in me.loop_triangles if t.material_index in mi]
    return BVHTree.FromPolygons(V, T)


def fix_ears(src='TripoMerged4', out='TripoMerged5', margin=0.004, reach=0.030, iters=20, drop_faces=6, max_bend=0.025):
    """Hair locks that cross the ears go fully outside the ear or behind it, whichever side
    most of the lock already lies on. Small loose bits sitting inside the ear are removed."""
    EAR = ear_bvh()
    s_ob = bpy.data.objects[src]
    if out in bpy.data.objects:
        bpy.data.objects.remove(bpy.data.objects[out])
    ob = s_ob.copy(); ob.data = s_ob.data.copy(); ob.name = out; ob.data.name = out
    s_ob.users_collection[0].objects.link(ob)
    M = ob.matrix_world.copy(); Mi = M.inverted()

    def surf(y, z, s):
        o = EAR.ray_cast(Vector((s * 0.40, y, z)), Vector((-s, 0, 0)), 0.4)
        if o[0] is None:
            return None
        i = EAR.ray_cast(Vector((s * 0.06, y, z)), Vector((s, 0, 0)), 0.4)
        return s * o[0].x, s * i[0].x

    bm = bmesh.new(); bm.from_mesh(ob.data)

    def lockmap():
        bm.verts.index_update()
        par = list(range(len(bm.verts)))
        def f(a):
            while par[a] != a:
                par[a] = par[par[a]]; a = par[a]
            return a
        for e in bm.edges:
            a, b = f(e.verts[0].index), f(e.verts[1].index)
            if a != b:
                par[a] = b
        return np.array([f(i) for i in range(len(par))])

    def crossing():
        bm.verts.index_update(); bm.faces.index_update()
        tris = bm.calc_loop_triangles()
        B = BVHTree.FromPolygons([M @ v.co for v in bm.verts], [tuple(l.vert.index for l in t) for t in tris])
        return {tris[i][0].face for i, _ in B.overlap(EAR)}

    rng = np.random.default_rng(0)
    def vote(faces):
        lat = med = ins = 0
        for fc in faces:
            P = [np.array((M @ v.co)[:]) for v in fc.verts]
            for _ in range(12):
                w = rng.random(len(P)); w /= w.sum()
                p = sum(wi * pi for wi, pi in zip(w, P)); s = 1 if p[0] > 0 else -1
                r = surf(p[1], p[2], s)
                if r is None:
                    continue
                ax = s * p[0]
                if ax > r[0] + 0.002: lat += 1
                elif ax < r[1] - 0.002: med += 1
                else: ins += 1
        return lat, ins, med

    report = []
    cf = crossing(); comp = lockmap()
    faces_of = {}
    for fc in bm.faces:
        faces_of.setdefault(comp[fc.verts[0].index], []).append(fc)
    dele = []
    for c in {comp[fc.verts[0].index] for fc in cf}:
        fs = faces_of[c]; lat, ins, med = vote(fs)
        # small crossing locks go whole (bending them round the ear distorted them; whole Tripo locks cross the ear where the
        # model's ear was elsewhere), and any small piece sitting mostly inside the ear
        if len(fs) <= drop_faces or (len(fs) <= 6 and ins >= lat + med):
            dele += fs; report.append(('delete', len(fs)))
    bmesh.ops.delete(bm, geom=list({v for f in dele for v in f.verts}), context='VERTS')

    cf = crossing(); near = set(cf)
    for f in list(cf):
        for e in f.edges:
            near.update(e.link_faces)
    bmesh.ops.subdivide_edges(bm, edges=list({e for f in near for e in f.edges}), cuts=2, use_grid_fill=True)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])

    comp = lockmap(); cf = crossing(); side = {}
    for fc in cf:
        c = comp[fc.verts[0].index]
        if c not in side:
            lat, ins, med = vote([f for f in bm.faces if comp[f.verts[0].index] == c])
            side[c] = 1 if lat >= med else -1
            report.append(('outside' if side[c] > 0 else 'behind', lat, ins, med))
    bm.verts.ensure_lookup_table()
    lockverts = {c: [v for v in bm.verts if comp[v.index] == c] for c in side}
    orig = {v: (M @ v.co).copy() for lv in lockverts.values() for v in lv}

    def ss(x):
        x = np.clip(x, 0, 1); return x * x * (3 - 2 * x)

    for it in range(iters):
        total = 0
        bad = crossing()
        badv = {v for f in bad for v in f.verts}
        for c, lv in lockverts.items():
            sd = side[c]
            W = np.array([(M @ v.co)[:] for v in lv])
            need = []
            for k, (v, p) in enumerate(zip(lv, W)):
                s = 1 if p[0] > 0 else -1
                r = surf(p[1], p[2], s)
                if r is None:
                    continue
                o, inn = r; ax = s * p[0]
                in_slab = inn - margin + 1e-4 < ax < o + margin - 1e-4
                wrong = v in badv and ((sd > 0 and ax < o + margin) or (sd < 0 and ax > inn - margin))
                if in_slab or wrong:
                    need.append((k, s * ((o + margin) - ax if sd > 0 else (inn - margin) - ax)))
            if not need:
                continue
            total += len(need)
            D = np.zeros(len(W))
            for k, d in need:
                w = 1 - ss((np.linalg.norm(W - W[k], axis=1) - 0.006) / reach)
                cand = d * w
                D = np.where(np.abs(cand) > np.abs(D), cand, D)
            for v, p, d in zip(lv, W, D):
                if d:
                    q = Vector(p); q.x += d; v.co = Mi @ q
        n = len(crossing())
        if total == 0 and n == 0:
            break
    # A lock that had to bend further than max_bend round the ear looked broken (a kink of up to 7 cm): it goes whole instead.
    bent = []
    for c, lv in lockverts.items():
        if max(((M @ v.co) - orig[v]).length for v in lv) > max_bend:
            bent += lv
    if bent:
        report.append(('removed, bent too far', len(bent)))
        orig = {v: o for v, o in orig.items() if v not in set(bent)}
        bmesh.ops.delete(bm, geom=list(set(bent)), context='VERTS')
    n = len(crossing())
    moved = [((M @ v.co) - o).length for v, o in orig.items()]
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    return {'iters': it + 1, 'violating': total, 'crossing': n, 'locks': report,
            'max_move_mm': round(max(moved) * 1000, 1) if moved else 0,
            'p90_move_mm': round(float(np.percentile(moved, 90)) * 1000, 1) if moved else 0}
