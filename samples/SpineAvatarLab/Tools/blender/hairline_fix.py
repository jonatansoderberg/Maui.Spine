import bpy, bmesh, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def head_bvh():
    dg = bpy.context.evaluated_depsgraph_get()
    head = bpy.data.objects['Head']
    me = head.evaluated_get(dg).to_mesh(); me.calc_loop_triangles()
    return BVHTree.FromPolygons([head.matrix_world @ v.co for v in me.vertices], [tuple(t.vertices) for t in me.loop_triangles])


def fix_hairline(src='TripoMerged5', out='TripoMerged6', zmin=0.12, ymax=-0.02, near=0.025,
                 lift=0.0012, tip=(0.003, 0.009), blend=0.015, cuts=3, seed=7):
    """The Tripo hair's lower edges over the forehead and temples end in a straight line a few mm off the
    skin, above the painted roots. Snap them onto the scalp and break them into uneven strand tips."""
    HB = head_bvh()
    s_ob = bpy.data.objects[src]
    if out in bpy.data.objects:
        bpy.data.objects.remove(bpy.data.objects[out])
    ob = s_ob.copy(); ob.data = s_ob.data.copy(); ob.name = out; ob.data.name = out
    s_ob.users_collection[0].objects.link(ob)
    M = ob.matrix_world.copy(); Mi = M.inverted()
    bm = bmesh.new(); bm.from_mesh(ob.data)

    def zone(e):
        p = M @ ((e.verts[0].co + e.verts[1].co) / 2)
        if not (p.z > zmin and p.y < ymax):
            return False
        if HB.find_nearest(p)[3] >= near:
            return False
        # a lock's root end along the hairline: the edge runs across, with its face above/behind it
        a, b = M @ e.verts[0].co, M @ e.verts[1].co
        c = M @ e.link_faces[0].calc_center_median()
        return abs((b - a).normalized().z) < 0.6 and (c - p).dot(Vector((0, 0.45, 1)).normalized()) > 0

    edges = [e for e in bm.edges if e.is_boundary and zone(e)]
    n_edges = len(edges); length = sum(e.calc_length() for e in edges)
    bmesh.ops.subdivide_edges(bm, edges=edges, cuts=cuts)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    edges = [e for e in bm.edges if e.is_boundary and zone(e)]
    rim = {v for e in edges for v in e.verts}
    rng = np.random.default_rng(seed)
    target = {}
    for v in rim:
        p = M @ v.co
        loc, nrm, _, d = HB.find_nearest(p)
        on = loc + nrm * lift
        # away from the lock, along the scalp: a strand tip reaching into the painted roots
        inner = [M @ e.other_vert(v).co for e in v.link_edges if not e.is_boundary]
        if inner:
            away = p - sum(inner, Vector()) / len(inner)
            away -= nrm * away.dot(nrm)
            if away.length > 1e-6:
                k = rng.random()
                reach = tip[0] + (tip[1] - tip[0]) * k if k > 0.45 else tip[0] * k
                on = on + away.normalized() * reach
        target[v] = on
    # the moved rim pulls the lock within `blend` of it along
    others = [v for v in bm.verts if v not in rim]
    R = np.array([(M @ v.co)[:] for v in rim]); D = np.array([(target[v] - M @ v.co)[:] for v in rim])
    moved = 0
    for v in others:
        p = np.array((M @ v.co)[:])
        dist = np.linalg.norm(R - p, axis=1)
        j = np.argmin(dist)
        if dist[j] < blend:
            w = 1 - dist[j] / blend; w = w * w * (3 - 2 * w)
            v.co = Mi @ Vector(p + D[j] * w); moved += 1
    for v, t in target.items():
        v.co = Mi @ t
    after = [HB.find_nearest(M @ v.co)[3] for v in rim]
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    return {'edges': n_edges, 'length_m': round(length, 3), 'rim_verts': len(rim), 'blended': moved,
            'rim_dist_mm_max': round(max(after) * 1000, 2) if after else 0}
