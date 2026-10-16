
# kairo_hair_style.py: guides with S-waves and depth, children clumped to their nearest guide. exec after kairo_hair_trace.py.
from mathutils import kdtree

class StyledField(HairField):
    DEFAULTS = dict(HairField.DEFAULTS, max_len=0.7, wave_len=0.14, wave_amp=0.022, wave_rad=0.006, clump=0.8,
                    clump_rise=0.06, tip_spread=0.2, child_jitter=0.25, n_guides=220, guide_lams=(0.95, 0.75, 0.55, 0.95))
    def trace_wave(self, root, n_root, lam, phase, amp, wl, rad_amp):
        P = self.P; h = P['step']; p = root + n_root * 0.001; pts = [p.copy()]; s_len = 0.0; ss = [0.0]
        u = (p - PIV).normalized()
        if not self.R.is_valid(u, 2): return pts, ss, 'root-outside'
        why = 'max_len'; A = 2 * math.pi * amp / wl
        while s_len < P['max_len']:
            beta = smoothstep(s_len / P['lift']); n_s = self.R.normal(self.R.sm, u); d = self.comb(p, n_s)
            if beta > 0.9 and n_s.z < -0.2 and d.z > P['curl_stop']: why = 'curl'; break
            alpha = A * math.sin(2 * math.pi * s_len / wl + phase) * beta
            d = (d * math.cos(alpha) + n_s.cross(d) * math.sin(alpha)).normalized()
            q = p + d * h; u2 = (q - PIV).normalized()
            if not self.R.is_valid(u2, 1): why = 'hem'; break
            r = self.radius_at(u2, lam, beta) + rad_amp * math.sin(2 * math.pi * s_len / wl + phase + 1.6) * beta
            r = min(r, self.R.sample(self.R.sm, u2) - 0.001)
            newp = self.push_out(PIV + u2 * r)
            if beta > 0.9 and (u2 - u).length * self.R.sample(self.R.sm, u2) <= 0.15 * h: why = 'stuck'; break
            s_len += (newp - p).length; p = newp; u = u2; pts.append(p.copy()); ss.append(s_len)
        return pts, ss, why
    @staticmethod
    def along(pts, ss, s):
        if s <= 0: return pts[0]
        if s >= ss[-1]: return pts[-1]
        i = bisect.bisect_right(ss, s) - 1; t = (s - ss[i]) / max(1e-9, ss[i + 1] - ss[i])
        return pts[i].lerp(pts[i + 1], t)
    def grow_styled(self, n_children, seed=7):
        P = self.P; rng = random.Random(seed + 100)
        guides = []
        for gi, (pt, n, u) in enumerate(self.roots(P['n_guides'], seed + 1)):
            lam = P['guide_lams'][gi % len(P['guide_lams'])]; phase = rng.uniform(0, 2 * math.pi)
            amp = P['wave_amp'] * rng.uniform(0.7, 1.3); wl = P['wave_len'] * rng.uniform(0.85, 1.15)
            self.bias = (rng.random() - 0.5) * 0.3
            pts, ss, why = self.trace_wave(pt, n, lam, phase, amp, wl, P['wave_rad'])
            guides.append(dict(root=pt, pts=pts, ss=ss, lam=lam, phase=phase, amp=amp, wl=wl, bias=self.bias, why=why))
        kd = kdtree.KDTree(len(guides))
        for i, g in enumerate(guides): kd.insert(g['root'], i)
        kd.balance()
        strands = []; lams = []; whys = {}; gidx = []
        for pt, n, u in self.roots(n_children, seed):
            co, gi, dist = kd.find(pt); g = guides[gi]
            j = P['child_jitter']; r1 = rng.random(); r2 = rng.random()
            lam = min(1.0, max(0.1, g['lam'] + (u - 0.5) * 0.25))
            self.bias = g['bias'] + (r1 - 0.5) * 0.1
            pts, ss, why = self.trace_wave(pt, n, lam, g['phase'] + (r2 - 0.5) * j * 1.5, g['amp'] * (1 + (u - 0.5) * j), g['wl'], P['wave_rad'])
            whys[why] = whys.get(why, 0) + 1
            if len(pts) < 3: continue
            Lg = g['ss'][-1]; out = []
            for p, s in zip(pts, ss):
                if s > Lg + 0.02: break
                w = P['clump'] * (1 - math.exp(-s / P['clump_rise']))
                if Lg > 0: w *= 1 - P['tip_spread'] * smoothstep((s - 0.75 * Lg) / (0.25 * Lg))
                G = self.along(g['pts'], g['ss'], s)
                out.append(self.push_out(p + (G - p) * w))
            if len(out) >= 3: strands.append(out); lams.append(lam); gidx.append(gi)
        self.guides = guides
        return strands, lams, whys, gidx


class StyledField2(StyledField):
    """v6: the hair at the parting lies combed back before it diverges; stronger backward flow on the crown's back."""
    def comb(self, p, n):
        P = self.P
        s = max(-1.0, min(1.0, (p.x - self.x_part(p.y)) / P['part_width']))
        rel = p - PIV; up = max(0.0, n.z)
        back = rel.y > 0.05; front_top = rel.y < -0.10 and p.z > PIV.z + 0.06
        comb = Vector((s, P['back_drift'] + P.get('part_back', 0.4) * (1 - abs(s)), 0.0))
        if back: comb = Vector((P['side_drift'] * (1 if rel.x > 0 else -1) + self.bias, P.get('crown_back', 0.5), 0.0))
        g_w = P['grav_top'] + (P['grav_side'] - P['grav_top']) * (1 - up)
        if front_top: g_w *= P['bangs_grav']; comb.x *= P['bangs_comb']
        for e in self.ears:
            dv = p - e; dv.z *= 0.5
            if dv.length < P['ear_radius']:
                w = 1 - dv.length / P['ear_radius']; comb += Vector((0, 1 if dv.y >= 0 else -1, 0)) * P['ear_push'] * w
        v = comb + Vector((0, 0, -g_w)); d = v - n * v.dot(n)
        if d.length < 1e-6: d = Vector((1.0, 0, 0)) - n * n.x
        return d.normalized()

T6_PARAMS = dict(max_len=0.9, wave_len=0.30, wave_amp=0.02, wave_rad=0.008, n_guides=140, clump=0.85, tip_spread=0.3, part_back=0.4, crown_back=0.5)


class StyledField3(StyledField2):
    """v7: her right front (s<0, in front of the pivot) is combed forward-down over the forehead (the bangs); the
    under-curl stop only where the surface faces down steeply; softer lift."""
    def comb(self, p, n):
        P = self.P
        s = max(-1.0, min(1.0, (p.x - self.x_part(p.y)) / P['part_width']))
        rel = p - PIV; up = max(0.0, n.z)
        back = rel.y > 0.05; front = rel.y < -0.12 and p.z > PIV.z + 0.05
        comb = Vector((s, P['back_drift'] + P.get('part_back', 0.4) * (1 - abs(s)), 0.0))
        if back: comb = Vector((P['side_drift'] * (1 if rel.x > 0 else -1) + self.bias, P.get('crown_back', 0.5), 0.0))
        g_w = P['grav_top'] + (P['grav_side'] - P['grav_top']) * (1 - up)
        if front and s < 0:
            comb = Vector((s * P['bangs_comb'], -P.get('bangs_forward', 0.5), 0.0)); g_w *= P['bangs_grav']
        for e in self.ears:
            dv = p - e; dv.z *= 0.5
            if dv.length < P['ear_radius']:
                w = 1 - dv.length / P['ear_radius']; comb += Vector((0, 1 if dv.y >= 0 else -1, 0)) * P['ear_push'] * w
        v = comb + Vector((0, 0, -g_w)); d = v - n * v.dot(n)
        if d.length < 1e-6: d = Vector((1.0, 0, 0)) - n * n.x
        return d.normalized()
    def trace_wave(self, root, n_root, lam, phase, amp, wl, rad_amp):
        P = self.P; h = P['step']; p = root + n_root * 0.001; pts = [p.copy()]; s_len = 0.0; ss = [0.0]
        u = (p - PIV).normalized()
        if not self.R.is_valid(u, 2): return pts, ss, 'root-outside'
        why = 'max_len'; A = 2 * math.pi * amp / wl
        while s_len < P['max_len']:
            beta = smoothstep(s_len / P['lift']); n_s = self.R.normal(self.R.sm, u); d = self.comb(p, n_s)
            if beta > 0.9 and n_s.z < -0.6 and d.z > 0.3: why = 'curl'; break
            alpha = A * math.sin(2 * math.pi * s_len / wl + phase) * beta
            d = (d * math.cos(alpha) + n_s.cross(d) * math.sin(alpha)).normalized()
            q = p + d * h; u2 = (q - PIV).normalized()
            if not self.R.is_valid(u2, 1): why = 'hem'; break
            r = self.radius_at(u2, lam, beta) + rad_amp * math.sin(2 * math.pi * s_len / wl + phase + 1.6) * beta
            r = min(r, self.R.sample(self.R.sm, u2) - 0.001)
            newp = self.push_out(PIV + u2 * r)
            if beta > 0.9 and (u2 - u).length * self.R.sample(self.R.sm, u2) <= 0.15 * h: why = 'stuck'; break
            s_len += (newp - p).length; p = newp; u = u2; pts.append(p.copy()); ss.append(s_len)
        return pts, ss, why

T7_PARAMS = dict(T6_PARAMS, lift=0.05, bangs_forward=0.5, bangs_grav=0.9, bangs_comb=1.0)


class StyledField4(StyledField3):
    """v8: the ear repeller only for strands near the head (lam < ear_lam); outer strands hang past the ear.
    grow_under(): a short flat undercoat on the scalp (no waves, no clumps)."""
    cur_lam = 1.0
    def comb(self, p, n):
        P = self.P
        s = max(-1.0, min(1.0, (p.x - self.x_part(p.y)) / P['part_width']))
        rel = p - PIV; up = max(0.0, n.z)
        back = rel.y > 0.05; front = rel.y < -0.12 and p.z > PIV.z + 0.05
        comb = Vector((s, P['back_drift'] + P.get('part_back', 0.4) * (1 - abs(s)), 0.0))
        if back: comb = Vector((P['side_drift'] * (1 if rel.x > 0 else -1) + self.bias, P.get('crown_back', 0.5), 0.0))
        g_w = P['grav_top'] + (P['grav_side'] - P['grav_top']) * (1 - up)
        if front and s < 0:
            comb = Vector((s * P['bangs_comb'], -P.get('bangs_forward', 0.5), 0.0)); g_w *= P['bangs_grav']
        if self.cur_lam < P.get('ear_lam', 0.5):
            for e in self.ears:
                dv = p - e; dv.z *= 0.5
                if dv.length < P['ear_radius']:
                    w = 1 - dv.length / P['ear_radius']; comb += Vector((0, 1 if dv.y >= 0 else -1, 0)) * P['ear_push'] * w
        v = comb + Vector((0, 0, -g_w)); d = v - n * v.dot(n)
        if d.length < 1e-6: d = Vector((1.0, 0, 0)) - n * n.x
        return d.normalized()
    def trace_wave(self, root, n_root, lam, phase, amp, wl, rad_amp):
        self.cur_lam = lam
        return StyledField3.trace_wave(self, root, n_root, lam, phase, amp, wl, rad_amp)
    def grow_under(self, count, seed=11, length=0.08, lam=0.02):
        old = dict(self.P); self.P.update(max_len=length, lift=0.03)
        strands = []
        for pt, n, u in self.roots(count, seed):
            self.bias = (u - 0.5) * 0.2
            pts, ss, why = self.trace_wave(pt, n, lam, 0.0, 0.0, 1.0, 0.0)
            if len(pts) >= 3: strands.append(pts)
        self.P = old
        return strands

T8_PARAMS = dict(T7_PARAMS, lift=0.10, ear_lam=0.5)


class StyledField5(StyledField4):
    """v9: the ear pockets (invalid cells near an ear with hair below them) are not the hem: outer strands
    (lam >= pass_lam) pass over them at the filled surface, inner strands slide along the pocket's rim."""
    def __init__(self, params=None):
        StyledField4.__init__(self, params)
        R = self.R; pocket = np.zeros_like(R.valid)
        for i in range(R.nth):
            col = R.valid[:, i]
            if not col.any(): continue
            last = int(np.max(np.where(col)[0])); pocket[:last, i] = ~col[:last]
        # only near the ears
        for j in range(R.nph):
            for i in range(R.nth):
                if pocket[j, i]:
                    u = dir_of((i + 0.5) * 2 * math.pi / R.nth, (j + 0.5) * math.pi / R.nph)
                    pt = PIV + u * R.filled[j, i]
                    if min((pt - e).length for e in self.ears) > self.P.get('pocket_reach', 0.12): pocket[j, i] = False
        R.pocket = pocket
        print("pocket cells", int(pocket.sum()))
    def cell_state(self, u):
        R = self.R; x, y = R.idx(u); i = int(round(x)) % R.nth; j = min(max(int(round(y)), 0), R.nph - 1)
        if R.is_valid(u, 1): return 'ok'
        return 'pocket' if R.pocket[j, i] else 'hem'
    def trace_wave(self, root, n_root, lam, phase, amp, wl, rad_amp):
        self.cur_lam = lam
        P = self.P; h = P['step']; p = root + n_root * 0.001; pts = [p.copy()]; s_len = 0.0; ss = [0.0]
        u = (p - PIV).normalized()
        if not self.R.is_valid(u, 2): return pts, ss, 'root-outside'
        why = 'max_len'; A = 2 * math.pi * amp / wl; slides = 0
        while s_len < P['max_len']:
            beta = smoothstep(s_len / P['lift']); n_s = self.R.normal(self.R.sm, u); d = self.comb(p, n_s)
            if beta > 0.9 and n_s.z < -0.6 and d.z > 0.3: why = 'curl'; break
            alpha = A * math.sin(2 * math.pi * s_len / wl + phase) * beta
            d = (d * math.cos(alpha) + n_s.cross(d) * math.sin(alpha)).normalized()
            q = p + d * h; u2 = (q - PIV).normalized(); st = self.cell_state(u2)
            if st == 'pocket' and lam < P.get('pass_lam', 0.75):
                e = min(self.ears, key=lambda e: (p - e).length)
                side = 1.0 if (p.y - e.y) >= 0 else -1.0
                d2 = n_s.cross(d).normalized()
                if d2.y * side < 0: d2 = -d2
                d = (d2 * 0.85 + d * 0.15).normalized(); q = p + d * h; u2 = (q - PIV).normalized(); st = self.cell_state(u2)
                slides += 1
                if st == 'hem' or slides > 40: why = 'pocket-rim'; break
            elif st == 'hem': why = 'hem'; break
            r = self.radius_at(u2, lam, beta) + rad_amp * math.sin(2 * math.pi * s_len / wl + phase + 1.6) * beta
            r = min(r, self.R.sample(self.R.sm, u2) - 0.001)
            newp = self.push_out(PIV + u2 * r)
            if beta > 0.9 and (u2 - u).length * self.R.sample(self.R.sm, u2) <= 0.15 * h: why = 'stuck'; break
            s_len += (newp - p).length; p = newp; u = u2; pts.append(p.copy()); ss.append(s_len)
        return pts, ss, why

T9_PARAMS = dict(T8_PARAMS, pass_lam=0.75, pocket_reach=0.12)


class StyledField6(StyledField5):
    """v10: the radius may rise at most rise_max per step (a strand under a thicker mass stays an inner strand) and a
    drop steeper than drop_max per step ends the strand (a cliff in the wig surface is a hem)."""
    def trace_wave(self, root, n_root, lam, phase, amp, wl, rad_amp):
        self.cur_lam = lam
        P = self.P; h = P['step']; p = root + n_root * 0.001; pts = [p.copy()]; s_len = 0.0; ss = [0.0]
        u = (p - PIV).normalized()
        if not self.R.is_valid(u, 2): return pts, ss, 'root-outside'
        why = 'max_len'; A = 2 * math.pi * amp / wl; slides = 0
        while s_len < P['max_len']:
            beta = smoothstep(s_len / P['lift']); n_s = self.R.normal(self.R.sm, u); d = self.comb(p, n_s)
            if beta > 0.9 and n_s.z < -0.6 and d.z > 0.3: why = 'curl'; break
            alpha = A * math.sin(2 * math.pi * s_len / wl + phase) * beta
            d = (d * math.cos(alpha) + n_s.cross(d) * math.sin(alpha)).normalized()
            q = p + d * h; u2 = (q - PIV).normalized(); st = self.cell_state(u2)
            if st == 'pocket' and lam < P.get('pass_lam', 0.75):
                e = min(self.ears, key=lambda e: (p - e).length)
                side = 1.0 if (p.y - e.y) >= 0 else -1.0
                d2 = n_s.cross(d).normalized()
                if d2.y * side < 0: d2 = -d2
                d = (d2 * 0.85 + d * 0.15).normalized(); q = p + d * h; u2 = (q - PIV).normalized(); st = self.cell_state(u2)
                slides += 1
                if st == 'hem' or slides > 40: why = 'pocket-rim'; break
            elif st == 'hem': why = 'hem'; break
            r = self.radius_at(u2, lam, beta) + rad_amp * math.sin(2 * math.pi * s_len / wl + phase + 1.6) * beta
            r = min(r, self.R.sample(self.R.sm, u2) - 0.001)
            r_prev = (p - PIV).length
            if beta > 0.9 and r < r_prev - P['drop_max']: why = 'cliff'; break
            r = min(r, r_prev + P['rise_max'])
            newp = self.push_out(PIV + u2 * r)
            if beta > 0.9 and (u2 - u).length * self.R.sample(self.R.sm, u2) <= 0.15 * h: why = 'stuck'; break
            s_len += (newp - p).length; p = newp; u = u2; pts.append(p.copy()); ss.append(s_len)
        return pts, ss, why

T10_PARAMS = dict(T9_PARAMS, drop_max=0.0045, rise_max=0.01)


class StyledField7(StyledField6):
    """v11: gap cells (invalid, not an ear pocket, hair below them in the column within gap_span deg) are passable for
    every strand at the filled surface; a strand ends where the surface faces down below the ears (the hem's underside)."""
    def __init__(self, params=None):
        StyledField6.__init__(self, params)
        R = self.R; gap = np.zeros_like(R.valid); span = int(self.P.get('gap_span', 40) / 180 * R.nph)
        for i in range(R.nth):
            col = R.valid[:, i]
            for j in range(R.nph):
                if not col[j] and not R.pocket[j, i] and col[j + 1:min(R.nph, j + 1 + span)].any() and col[:j].any():
                    gap[j, i] = True
        R.gap = gap; print("gap cells", int(gap.sum()))
    def cell_state(self, u):
        R = self.R; x, y = R.idx(u); i = int(round(x)) % R.nth; j = min(max(int(round(y)), 0), R.nph - 1)
        if R.is_valid(u, 1): return 'ok'
        if R.pocket[j, i]: return 'pocket'
        return 'gap' if R.gap[j, i] else 'hem'
    def trace_wave(self, root, n_root, lam, phase, amp, wl, rad_amp):
        self.cur_lam = lam
        P = self.P; h = P['step']; p = root + n_root * 0.001; pts = [p.copy()]; ss = [0.0]; s_len = 0.0
        u = (p - PIV).normalized()
        if not self.R.is_valid(u, 2): return pts, ss, 'root-outside'
        why = 'max_len'; A = 2 * math.pi * amp / wl; slides = 0
        while s_len < P['max_len']:
            beta = smoothstep(s_len / P['lift']); n_s = self.R.normal(self.R.sm, u); d = self.comb(p, n_s)
            if beta > 0.9 and n_s.z < -P.get('underside', 0.5) and p.z < PIV.z: why = 'underside'; break
            alpha = A * math.sin(2 * math.pi * s_len / wl + phase) * beta
            d = (d * math.cos(alpha) + n_s.cross(d) * math.sin(alpha)).normalized()
            q = p + d * h; u2 = (q - PIV).normalized(); st = self.cell_state(u2)
            if st == 'pocket' and lam < P.get('pass_lam', 0.75):
                e = min(self.ears, key=lambda e: (p - e).length)
                side = 1.0 if (p.y - e.y) >= 0 else -1.0
                d2 = n_s.cross(d).normalized()
                if d2.y * side < 0: d2 = -d2
                d = (d2 * 0.85 + d * 0.15).normalized(); q = p + d * h; u2 = (q - PIV).normalized(); st = self.cell_state(u2)
                slides += 1
                if st == 'hem' or slides > 40: why = 'pocket-rim'; break
            elif st == 'hem': why = 'hem'; break
            r = self.radius_at(u2, lam, beta) + rad_amp * math.sin(2 * math.pi * s_len / wl + phase + 1.6) * beta
            r = min(r, self.R.sample(self.R.sm, u2) - 0.001)
            r_prev = (p - PIV).length
            if beta > 0.9 and r < r_prev - P['drop_max']: why = 'cliff'; break
            r = min(r, r_prev + P['rise_max'])
            newp = self.push_out(PIV + u2 * r)
            if beta > 0.9 and (u2 - u).length * self.R.sample(self.R.sm, u2) <= 0.15 * h: why = 'stuck'; break
            s_len += (newp - p).length; p = newp; u = u2; pts.append(p.copy()); ss.append(s_len)
        return pts, ss, why

T11_PARAMS = dict(T10_PARAMS, gap_span=40, underside=0.5)


class StyledField8(StyledField7):
    """v12: no underside rule; a strand below the ears that stops descending (less than stall_dz over stall_steps
    steps, i.e. it slides along the hem) ends there."""
    def trace_wave(self, root, n_root, lam, phase, amp, wl, rad_amp):
        self.cur_lam = lam
        P = self.P; h = P['step']; p = root + n_root * 0.001; pts = [p.copy()]; ss = [0.0]; s_len = 0.0
        u = (p - PIV).normalized()
        if not self.R.is_valid(u, 2): return pts, ss, 'root-outside'
        why = 'max_len'; A = 2 * math.pi * amp / wl; slides = 0; zs = [p.z]
        K = P.get('stall_steps', 8); DZ = P.get('stall_dz', 0.005)
        while s_len < P['max_len']:
            beta = smoothstep(s_len / P['lift']); n_s = self.R.normal(self.R.sm, u); d = self.comb(p, n_s)
            if beta > 0.9 and p.z < PIV.z and len(zs) > K and zs[-K - 1] - p.z < DZ: why = 'hem-stall'; break
            alpha = A * math.sin(2 * math.pi * s_len / wl + phase) * beta
            d = (d * math.cos(alpha) + n_s.cross(d) * math.sin(alpha)).normalized()
            q = p + d * h; u2 = (q - PIV).normalized(); st = self.cell_state(u2)
            if st == 'pocket' and lam < P.get('pass_lam', 0.75):
                e = min(self.ears, key=lambda e: (p - e).length)
                side = 1.0 if (p.y - e.y) >= 0 else -1.0
                d2 = n_s.cross(d).normalized()
                if d2.y * side < 0: d2 = -d2
                d = (d2 * 0.85 + d * 0.15).normalized(); q = p + d * h; u2 = (q - PIV).normalized(); st = self.cell_state(u2)
                slides += 1
                if st == 'hem' or slides > 40: why = 'pocket-rim'; break
            elif st == 'hem': why = 'hem'; break
            r = self.radius_at(u2, lam, beta) + rad_amp * math.sin(2 * math.pi * s_len / wl + phase + 1.6) * beta
            r = min(r, self.R.sample(self.R.sm, u2) - 0.001)
            r_prev = (p - PIV).length
            if beta > 0.9 and r < r_prev - P['drop_max']: why = 'cliff'; break
            r = min(r, r_prev + P['rise_max'])
            newp = self.push_out(PIV + u2 * r)
            if beta > 0.9 and (u2 - u).length * self.R.sample(self.R.sm, u2) <= 0.15 * h: why = 'stuck'; break
            s_len += (newp - p).length; p = newp; u = u2; pts.append(p.copy()); ss.append(s_len); zs.append(p.z)
        return pts, ss, why

T12_PARAMS = dict(T11_PARAMS, stall_steps=8, stall_dz=0.005)

T13_PARAMS = dict(T12_PARAMS, drop_max=0.05)   # v13: no cliff rule; the gap cells carry the strands over the temple band, the hem stall ends them


class StyledField9(StyledField8):
    """v14: below the ears the comb's lateral terms fade out (hang_top..hang_bottom), so the hair hangs by gravity."""
    def comb(self, p, n):
        P = self.P
        s = max(-1.0, min(1.0, (p.x - self.x_part(p.y)) / P['part_width']))
        rel = p - PIV; up = max(0.0, n.z)
        back = rel.y > 0.05; front = rel.y < -0.12 and p.z > PIV.z + 0.05
        comb = Vector((s, P['back_drift'] + P.get('part_back', 0.4) * (1 - abs(s)), 0.0))
        if back: comb = Vector((P['side_drift'] * (1 if rel.x > 0 else -1) + self.bias, P.get('crown_back', 0.5), 0.0))
        g_w = P['grav_top'] + (P['grav_side'] - P['grav_top']) * (1 - up)
        if front and s < 0:
            comb = Vector((s * P['bangs_comb'], -P.get('bangs_forward', 0.5), 0.0)); g_w *= P['bangs_grav']
        if self.cur_lam < P.get('ear_lam', 0.5):
            for e in self.ears:
                dv = p - e; dv.z *= 0.5
                if dv.length < P['ear_radius']:
                    w = 1 - dv.length / P['ear_radius']; comb += Vector((0, 1 if dv.y >= 0 else -1, 0)) * P['ear_push'] * w
        hang = min(1.0, max(0.0, (p.z - (PIV.z - P.get('hang_bottom', 0.10))) / (P.get('hang_top', 0.02) + P.get('hang_bottom', 0.10))))
        comb *= hang
        v = comb + Vector((0, 0, -g_w)); d = v - n * v.dot(n)
        if d.length < 1e-6: d = Vector((1.0, 0, 0)) - n * n.x
        return d.normalized()

T14_PARAMS = dict(T13_PARAMS, hang_top=0.02, hang_bottom=0.10)


class StyledField10(StyledField9):
    """v15: a child clumps to the nearest of four guides on its own side of the parting within guide_reach; the pull
    fades with the root distance; no guide in reach = the child's own path."""
    def grow_styled(self, n_children, seed=7):
        P = self.P; rng = random.Random(seed + 100)
        guides = []
        for gi, (pt, n, u) in enumerate(self.roots(P['n_guides'], seed + 1)):
            lam = P['guide_lams'][gi % len(P['guide_lams'])]; phase = rng.uniform(0, 2 * math.pi)
            amp = P['wave_amp'] * rng.uniform(0.7, 1.3); wl = P['wave_len'] * rng.uniform(0.85, 1.15)
            self.bias = (rng.random() - 0.5) * 0.3
            pts, ss, why = self.trace_wave(pt, n, lam, phase, amp, wl, P['wave_rad'])
            side = 1 if pt.x - self.x_part(pt.y) >= 0 else -1
            guides.append(dict(root=pt, pts=pts, ss=ss, lam=lam, phase=phase, amp=amp, wl=wl, bias=self.bias, why=why, side=side))
        kd = kdtree.KDTree(len(guides))
        for i, g in enumerate(guides): kd.insert(g['root'], i)
        kd.balance()
        reach = P.get('guide_reach', 0.05)
        strands = []; lams = []; whys = {}; gidx = []; unclumped = 0
        for pt, n, u in self.roots(n_children, seed):
            side = 1 if pt.x - self.x_part(pt.y) >= 0 else -1
            cands = [(co, gi, dist) for co, gi, dist in kd.find_n(pt, 4) if guides[gi]['side'] == side and dist <= reach]
            j = P['child_jitter']; r1 = rng.random(); r2 = rng.random()
            if cands:
                co, gi, dist = cands[0]; g = guides[gi]; pull = 1 - P.get('pull_fade', 0.3) * dist / reach
                lam = min(1.0, max(0.1, g['lam'] + (u - 0.5) * 0.25)); self.bias = g['bias'] + (r1 - 0.5) * 0.1
                phase = g['phase'] + (r2 - 0.5) * j * 1.5; amp = g['amp'] * (1 + (u - 0.5) * j); wl = g['wl']
            else:
                gi = -1; g = None; pull = 0.0; unclumped += 1
                lam = 1 - u * u * 0.9; self.bias = (r1 - 0.5) * 0.3; phase = rng.uniform(0, 2 * math.pi); amp = P['wave_amp']; wl = P['wave_len']
            pts, ss, why = self.trace_wave(pt, n, lam, phase, amp, wl, P['wave_rad'])
            whys[why] = whys.get(why, 0) + 1
            if len(pts) < 3: continue
            if g is None:
                out = pts
            else:
                Lg = g['ss'][-1]; out = []
                for p, s in zip(pts, ss):
                    if s > Lg + 0.02: break
                    w = pull * P['clump'] * (1 - math.exp(-s / P['clump_rise']))
                    if Lg > 0: w *= 1 - P['tip_spread'] * smoothstep((s - 0.75 * Lg) / (0.25 * Lg))
                    out.append(self.push_out(p + (self.along(g['pts'], g['ss'], s) - p) * w))
            if len(out) >= 3: strands.append(out); lams.append(lam); gidx.append(gi)
        self.guides = guides; print("children without a guide in reach:", unclumped)
        return strands, lams, whys, gidx

T15_PARAMS = dict(T14_PARAMS, guide_reach=0.05)

T16_PARAMS = dict(T15_PARAMS, pull_fade=0.3)   # v16: the pull to the guide stays 0.7..1.0 within reach (t15's fade to 0 lost the locks)
