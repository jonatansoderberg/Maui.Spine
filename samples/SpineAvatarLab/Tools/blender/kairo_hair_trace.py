
# kairo_hair_trace.py v4: strands traced on a smoothed radial wig surface (the HairEnvelope's outermost hit per direction
# from the head pivot PIV, 1 deg grid, box-blurred). The flow direction is projected onto the smooth surface's own tangent
# plane (finite-difference normal); roots come from ScalpHair; deterministic (seeded roots, fixed order).
import numpy as np, time, bisect, itertools

def smoothstep(x):
    x = min(1.0, max(0.0, x)); return x * x * (3 - 2 * x)

def box_blur_sphere(a, k):
    out = a.copy()
    for _ in range(3):
        acc = np.zeros_like(out)
        for d in range(-k, k + 1): acc += np.roll(out, d, axis=1)
        out = acc / (2 * k + 1)
        pad = np.pad(out, ((k, k), (0, 0)), mode='edge'); acc = np.zeros_like(out)
        for d in range(0, 2 * k + 1): acc += pad[d:d + out.shape[0]]
        out = acc / (2 * k + 1)
    return out

def fill_nan_nearest(a, iters=60):
    a = a.copy()
    for _ in range(iters):
        nan = np.isnan(a)
        if not nan.any(): break
        nb = [np.roll(a, 1, axis=1), np.roll(a, -1, axis=1), np.pad(a, ((1, 0), (0, 0)), mode='edge')[:-1], np.pad(a, ((0, 1), (0, 0)), mode='edge')[1:]]
        stack = np.stack(nb); cnt = (~np.isnan(stack)).sum(0); s = np.nansum(stack, 0)
        f = nan & (cnt > 0); a[f] = s[f] / cnt[f]
    return a

def dir_of(th, ph):
    return Vector((math.sin(ph) * math.cos(th), math.sin(ph) * math.sin(th), math.cos(ph)))

class RadialSurface:
    def __init__(self, bvh, nth=360, nph=180, max_r=0.6, last_hit=True):
        self.nth, self.nph = nth, nph
        r = np.full((nph, nth), np.nan)
        for j in range(nph):
            ph = (j + 0.5) * math.pi / nph
            for i in range(nth):
                th = (i + 0.5) * 2 * math.pi / nth; u = dir_of(th, ph)
                o = PIV.copy(); best = None
                for _ in range(24):
                    loc, nrm, idx, dist = bvh.ray_cast(o, u, max_r)
                    if loc is None: break
                    best = (loc - PIV).length; o = loc + u * 1e-4
                    if not last_hit: break
                if best is not None: r[j, i] = best
        self.raw = r; self.valid = ~np.isnan(r)
    def smooth(self, k):
        f = fill_nan_nearest(self.raw); f = np.where(np.isnan(f), np.nanmean(self.raw), f)
        self.filled = f; self.sm = box_blur_sphere(f, k)
    def angles(self, u):
        return math.atan2(u.y, u.x) % (2 * math.pi), math.acos(max(-1.0, min(1.0, u.z)))
    def idx(self, u):
        th, ph = self.angles(u); return th / (2 * math.pi) * self.nth - 0.5, ph / math.pi * self.nph - 0.5
    def sample(self, arr, u):
        x, y = self.idx(u); x0 = math.floor(x); y0 = math.floor(y); fx = x - x0; fy = y - y0
        y0 = min(max(y0, 0), self.nph - 2); i0 = x0 % self.nth; i1 = (x0 + 1) % self.nth
        return (arr[y0, i0] * (1 - fx) + arr[y0, i1] * fx) * (1 - fy) + (arr[y0 + 1, i0] * (1 - fx) + arr[y0 + 1, i1] * fx) * fy
    def point(self, arr, th, ph):
        u = dir_of(th, ph); return PIV + u * self.sample(arr, u)
    def normal(self, arr, u):
        th, ph = self.angles(u); dth = 2 * math.pi / self.nth; dph = math.pi / self.nph
        pt = self.point(arr, th + dth, ph) - self.point(arr, th - dth, ph)
        pp = self.point(arr, th, min(math.pi - 1e-4, ph + dph)) - self.point(arr, th, max(1e-4, ph - dph))
        n = pt.cross(pp)
        if n.length < 1e-12: return u
        n.normalize()
        return n if n.dot(u) >= 0 else -n
    def is_valid(self, u, grow=1):
        x, y = self.idx(u); i = int(round(x)) % self.nth; j = min(max(int(round(y)), 0), self.nph - 1)
        for dj in range(-grow, grow + 1):
            jj = min(max(j + dj, 0), self.nph - 1)
            for di in range(-grow, grow + 1):
                if self.valid[jj, (i + di) % self.nth]: return True
        return False

class HairField:
    DEFAULTS = dict(step=0.005, max_len=0.45, lift=0.02, shell_inset=0.002, head_gap=0.004, smooth_k=4,
                    back_drift=0.3, side_drift=0.05, grav_top=0.5, grav_side=1.5, bangs_grav=0.8, bangs_comb=1.2,
                    part_width=0.02, ear_push=2.0, ear_radius=0.08, curl_stop=0.1)
    def __init__(self, params=None):
        self.P = dict(self.DEFAULTS)
        if params: self.P.update(params)
        t0 = time.time()
        self.env = bpy.data.objects['HairEnvelope']; self.bvh_env, ev, et = bvh_of(self.env)
        hv, ht = [], []
        for hn in ('Head', 'Neck'):
            v, t, _ = world_mesh(bpy.data.objects[hn]); off = len(hv); hv += v; ht += [(a + off, b + off, c + off) for a, b, c in t]
        self.bvh_head = BVHTree.FromPolygons(hv, ht)
        self.R = RadialSurface(self.bvh_env); self.R.smooth(self.P['smooth_k'])
        self.H = RadialSurface(self.bvh_head, max_r=0.35); self.H.smooth(2)
        self.emitter = bpy.data.objects['ScalpHair']; self.em_v, _, self.em_p = world_mesh(self.emitter, evaluated=False)
        head = bpy.data.objects['Head']; mats = [s.material.name for s in head.material_slots]; ei = mats.index('ear'); mw = head.matrix_world
        L = []; Rr = []
        for p in head.data.polygons:
            if p.material_index == ei:
                c = mw @ p.center; (L if c.x > 0 else Rr).append(c)
        self.ears = [sum(L, Vector()) / len(L), sum(Rr, Vector()) / len(Rr)]
        top_z = max(v.z for v in self.em_v); self.parting = []
        for x, y in ((0.050, None), (0.040, -0.03), (0.025, 0.06)):
            if y is None:
                col = [v for v in self.em_v if abs(v.x - x) < 0.008 and v.z > PIV.z + 0.08]; f = min(col, key=lambda v: v.y); self.parting.append(Vector((x, f.y, f.z)))
            else: self.parting.append(Vector((x, PIV.y + y, top_z)))
        self.bias = 0.0
        print("HairField ready in", round(time.time() - t0, 1), "s; shell cells valid", int(self.R.valid.sum()), "/", self.R.valid.size)
    def x_part(self, y):
        pts = self.parting
        if y <= pts[0].y: return pts[0].x
        for a, b in zip(pts, pts[1:]):
            if y <= b.y: return a.x + (b.x - a.x) * (y - a.y) / max(1e-6, b.y - a.y)
        return pts[-1].x
    def comb(self, p, n):
        """unit flow direction at p, tangent to the surface with normal n"""
        P = self.P
        s = max(-1.0, min(1.0, (p.x - self.x_part(p.y)) / P['part_width']))
        rel = p - PIV; up = max(0.0, n.z)
        back = rel.y > 0.05; front_top = rel.y < -0.10 and p.z > PIV.z + 0.06
        comb = Vector((s, P['back_drift'], 0.0))
        if back: comb = Vector((P['side_drift'] * (1 if rel.x > 0 else -1) + self.bias, 0.15, 0.0))
        g_w = P['grav_top'] + (P['grav_side'] - P['grav_top']) * (1 - up)
        if front_top: g_w *= P['bangs_grav']; comb.x *= P['bangs_comb']
        for e in self.ears:
            dv = p - e; dv.z *= 0.5
            if dv.length < P['ear_radius']:
                w = 1 - dv.length / P['ear_radius']; comb += Vector((0, 1 if dv.y >= 0 else -1, 0)) * P['ear_push'] * w
        v = comb + Vector((0, 0, -g_w)); d = v - n * v.dot(n)
        if d.length < 1e-6: d = Vector((1.0, 0, 0)) - n * n.x
        return d.normalized()
    def radius_at(self, u, lam, beta):
        r_out = self.R.sample(self.R.sm, u); r_h = self.H.sample(self.H.sm, u); T = max(0.0, r_out - r_h)
        inset = self.P['shell_inset'] + (1 - lam) * max(0.0, T - self.P['shell_inset'] - self.P['head_gap']) * 0.95
        r_scalp = r_h + 0.003
        return r_scalp + (r_out - inset - r_scalp) * beta
    def push_out(self, p):
        c, n, idx, dist = self.bvh_head.find_nearest(p, self.P['head_gap'])
        if c is None: return p
        away = p - c
        if away.length < 1e-7: away = n
        return c + away.normalized() * self.P['head_gap']
    def trace(self, root, n_root, lam):
        P = self.P; h = P['step']; p = root + n_root * 0.001; pts = [p.copy()]; s_len = 0.0
        u = (p - PIV).normalized()
        if not self.R.is_valid(u, 2): return pts, 'root-outside'
        why = 'max_len'
        while s_len < P['max_len']:
            beta = smoothstep(s_len / P['lift'])
            n_s = self.R.normal(self.R.sm, u)
            d = self.comb(p, n_s)
            if beta > 0.9 and n_s.z < -0.2 and d.z > P['curl_stop']: why = 'curl'; break
            q = p + d * h; u2 = (q - PIV).normalized()
            if not self.R.is_valid(u2, 1): why = 'hem'; break
            newp = self.push_out(PIV + u2 * self.radius_at(u2, lam, beta))
            if beta > 0.9 and (u2 - u).length * self.R.sample(self.R.sm, u2) <= 0.15 * h: why = 'stuck'; break
            s_len += (newp - p).length; p = newp; u = u2; pts.append(p.copy())
        return pts, why
    def roots(self, count, seed=1):
        rng = random.Random(seed); areas = []; faces = []
        for vs, mi in self.em_p:
            areas.append((self.em_v[vs[1]] - self.em_v[vs[0]]).cross(self.em_v[vs[2]] - self.em_v[vs[0]]).length * 0.5); faces.append(vs)
        total = sum(areas); cum = list(itertools.accumulate(areas)); out = []
        for _ in range(count):
            fi = bisect.bisect_left(cum, rng.random() * total); vs = faces[min(fi, len(faces) - 1)]
            a, b, c = self.em_v[vs[0]], self.em_v[vs[1]], self.em_v[vs[2]]; r1, r2 = rng.random(), rng.random()
            if r1 + r2 > 1: r1, r2 = 1 - r1, 1 - r2
            pt = a + (b - a) * r1 + (c - a) * r2; n = (b - a).cross(c - a)
            if n.length > 0: n.normalize()
            if n.dot(pt - PIV) < 0: n = -n
            out.append((pt, n, rng.random()))
        return out
    def grow(self, count, seed, lam_pow=0.9, bias_amp=0.3):
        strands = []; lams = []; whys = {}
        for pt, n, u in self.roots(count, seed):
            lam = 1 - u * u * lam_pow; self.bias = (u - 0.5) * bias_amp
            s, why = self.trace(pt, n, lam); whys[why] = whys.get(why, 0) + 1
            if len(s) >= 3: strands.append(s); lams.append(lam)
        return strands, lams, whys

def build_hair_object(name, strands, lams, material='HairStrand', radius_root=0.0006, radius_tip=0.00015, surface='ScalpHair'):
    old = bpy.data.objects.get(name)
    if old:
        od = old.data; bpy.data.objects.remove(old); bpy.data.hair_curves.remove(od)
    cu = bpy.data.hair_curves.new(name); cu.add_curves([len(s) for s in strands])
    cu.position_data.foreach_set('vector', [v for s in strands for p in s for v in p])
    rad = cu.attributes.new('radius', 'FLOAT', 'POINT'); rv = []
    for s in strands:
        n = len(s)
        for i in range(n): rv.append(radius_root + (radius_tip - radius_root) * i / max(1, n - 1))
    rad.data.foreach_set('value', rv)
    lay = cu.attributes.new('layer', 'FLOAT', 'CURVE'); lay.data.foreach_set('value', list(lams))
    if surface and bpy.data.objects.get(surface): cu.surface = bpy.data.objects[surface]; cu.surface_uv_map = 'UVMap'
    mat = bpy.data.materials.get(material)
    if mat: cu.materials.append(mat)
    ob = bpy.data.objects.new(name, cu); bpy.data.collections['Collection'].objects.link(ob)
    root = bpy.data.objects['Root']; ob.parent = root; ob.matrix_parent_inverse = root.matrix_world.inverted(); ob.matrix_world = Matrix.Identity(4)
    return ob

def style_group(name='KairoHairStyle', clump=None, curl=None, noise=None, shrink=None):
    """Geometry-nodes chain Clump -> Curl -> Noise -> Shrinkwrap(Head) from Blender's hair assets; values set on the nodes."""
    old = bpy.data.node_groups.get(name)
    if old: bpy.data.node_groups.remove(old)
    ng = bpy.data.node_groups.new(name, 'GeometryNodeTree')
    ng.interface.new_socket('Geometry', in_out='INPUT', socket_type='NodeSocketGeometry')
    ng.interface.new_socket('Geometry', in_out='OUTPUT', socket_type='NodeSocketGeometry')
    gi = ng.nodes.new('NodeGroupInput'); go = ng.nodes.new('NodeGroupOutput'); prev = gi.outputs['Geometry']; x = 200
    for asset, vals in (('Clump Hair Curves', clump), ('Curl Hair Curves', curl), ('Hair Curves Noise', noise), ('Shrinkwrap Hair Curves', shrink)):
        if vals is None: continue
        n = ng.nodes.new('GeometryNodeGroup'); n.node_tree = bpy.data.node_groups[asset]; n.location = (x, 0); x += 250
        for k, v in vals.items():
            socks = [s for s in n.inputs if s.name == k]
            if k == 'Surface': socks = [s for s in socks if s.type == 'OBJECT']
            if not socks: print("  no input", k, "on", asset); continue
            socks[0].default_value = v
        ng.links.new(prev, n.inputs['Geometry']); prev = n.outputs['Geometry']
    go.location = (x, 0); ng.links.new(prev, go.inputs['Geometry'])
    return ng

def apply_style(ob, group='KairoHairStyle'):
    for m in list(ob.modifiers): ob.modifiers.remove(m)
    m = ob.modifiers.new('Style', 'NODES'); m.node_group = bpy.data.node_groups[group]; return m
