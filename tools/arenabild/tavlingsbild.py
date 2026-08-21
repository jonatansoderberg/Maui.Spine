#!/usr/bin/env python3
"""Genererar en tävlingsbild: snedbild i 3D över ett tävlingsområde.

    ./venv/bin/python tavlingsbild.py [eventor-id] [--when ISO] [årstid] [ai-bild.png]

Utan --when används tävlingens egen tidpunkt ur Eventor. Årstid och ljus räknas fram
ur datum, klockslag och arenans position — inget av det behöver en modell.

Sista argumentet lägger vimpel, gräns och text ovanpå en färdigt AI-förbättrad
render i stället för på min egen. Diffusion mosar tunna linjer och bokstäver — text
och flagga hör hemma efter det passet, aldrig före.

Data (öppet; höjddatat kräver Geotorget-behörighet, se terrain.credentials):
  * tävling, arena och tävlingsområde  — Eventor
  * ortofoto 0,25 m                    — Lantmäteriet, CC BY 4.0
  * markhöjdmodell 1 m                 — Lantmäteriet, CC BY 4.0
"""
import math, sys, time
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
import eventor, prompt, sol, terrain as T, render as R

RES = 1.25   # marktexlar; konturen trappar synligt vid grövre upplösning
CAM = dict(azimuth=math.radians(200), pitch=21, fill=0.66, reach=2.3,
           back=1.2, center_y=0.42)
W, H = 1920, 1080

# Utan polygon från Eventor ramas i stället en ruta runt arenan in. Storleken är en
# gissning på en medeldistans; arenan antas ligga mitt i, vilket den sällan gör.
DEFAULT_SIDE = 1300.0


def framing(ev):
    """Hörnen som ramar in bilden, och om de får ritas ut som gräns.

    Saknas området i Eventor ritas ingen kontur. Att gissa fram en och rita den
    vore att hitta på arrangörens gränsdragning.
    """
    arena = T.sweref(*ev["arena"])
    if ev["area"]:
        return [T.sweref(lat, lon) for lat, lon in ev["area"]], arena, True
    h = DEFAULT_SIDE / 2
    box = [(arena[0] - h, arena[1] - h), (arena[0] + h, arena[1] - h),
           (arena[0] + h, arena[1] + h), (arena[0] - h, arena[1] + h)]
    return box, arena, False


def frame_bounds(area_xy, azimuth, pitch, fill, reach, back, W, H):
    """Marktäckningen kamerans frustum faktiskt behöver — inte en ruta runt området."""
    th = math.radians(pitch)
    (acx, acy), L, across = R.view_extent(area_xy, azimuth)
    d_mid = reach * max(L, across) * math.cos(th)
    f = fill * H * d_mid / (L * math.sin(th))
    fx, fy = math.sin(azimuth), math.cos(azimuth)
    rx, ry = math.cos(azimuth), -math.sin(azimuth)
    cam = (acx - fx * d_mid, acy - fy * d_mid)
    xs, ys = [], []
    for d in (max(60.0, d_mid - L * 0.85), d_mid + L * back):
        half = d * (W / 2) / f + 80
        cx, cy = cam[0] + fx * d, cam[1] + fy * d
        for sgn in (-1, 1):
            xs.append(cx + rx * half * sgn); ys.append(cy + ry * half * sgn)
    return min(xs), min(ys), max(xs), max(ys)


def caption(img, ev, meta):
    d = ImageDraw.Draw(img, "RGBA")
    W_, H_ = img.size
    d.rectangle([0, H_ - 132, W_, H_], fill=(8, 12, 18, 205))
    d.text((44, H_ - 108), ev["name"], font=R._font(46, True), fill=(255, 255, 255, 255))
    sub = "  ·  ".join(x for x in (ev["club"], ev["district"], meta["when_text"]) if x)
    d.text((44, H_ - 52), sub, font=R._font(24), fill=(206, 214, 224, 255))
    # Två rader: en lång kreditrad högerställd korsar in i rubriken.
    lines = [
        meta["sources"],
        ("område: Eventor" if meta["outline"] else "område saknas i Eventor — ram kring arenan")
        + f"  ·  {meta['relief']} m relief, {meta['vex']}× överdrift"
        + f"  ·  {meta['light']}"
        + ("  ·  vinterstilisering, ortofotot är en sommarbild" if meta.get("snow") else ""),
    ]
    f = R._font(16)
    for i, line in enumerate(lines):
        d.text((W_ - d.textlength(line, font=f) - 44, H_ - 74 + i * 24),
               line, font=f, fill=(150, 162, 176, 255))
    return img


def main(event_id, season=None, enhanced=None, when_override=None, tag="",
         with_caption=False, bare=False, wall=False):
    t0 = time.time()
    ev = eventor.fetch(event_id)
    area_xy, arena_xy, outline = framing(ev)

    # Årstid och ljus ur tävlingens egen tidpunkt, om den gick att läsa.
    when = when_override or ev["when_dt"]
    season = season or (sol.season(when) if when else "sommar")
    sun = sol.solar_position(*ev["arena"], when) if when else None
    print(f"{ev['name']} — {ev['club']}, {ev['when']}")
    light = R.lighting(sun[0], sun[1]) if sun else R.lighting(38.0, 315.0)
    print(f"  {when:%Y-%m-%d %H:%M} · årstid {season} · {light['label']}")
    print(f"  område: {'Eventor, ' + str(len(ev['area'])) + ' hörn' if outline else 'saknas, ram kring arenan'}")

    b = frame_bounds(area_xy, CAM["azimuth"], CAM["pitch"], CAM["fill"],
                     CAM["reach"], CAM["back"], W, H)
    w, h = int((b[2] - b[0]) / RES), int((b[3] - b[1]) / RES)

    elev, lm = T.elevation_lm(b, w, h), True
    if elev is None:
        elev, lm = T.smooth(T.elevation(b, w, h), 6.0 / RES), False
    geo = ("höjdgeometri: Lantmäteriet markhöjdmodell 1 m" if lm
           else "höjdgeometri: Terrarium/Mapzen (~25 m)")
    shade = None if lm else T.terrangskuggning(b, w, h)

    tex = R.shade_texture(T.ortofoto(b, w, h),
                          shade if shade is not None else R.hillshade(elev, RES),
                          elev, RES, season=season, sun=(sun[1], sun[0]) if sun else None)
    # Draperad gräns när jag renderar själv; läggs den ovanpå en AI-bild ritas den
    # i stället i bildplanet efteråt, så modellen slipper smeta ut den.
    if outline and not enhanced and not bare:
        tex = R.bake_outline(tex, b, area_xy, RES)

    img, meta = R.render(b, elev, tex, area_xy, season=season,
                         sun=(sun[1], sun[0]) if sun else None, W=W, H=H,
                         vex_max=1.35 if lm else 1.7, **CAM)
    meta.update(sources=f"ortofoto © Lantmäteriet CC BY 4.0  ·  {geo}", outline=outline,
                snow=R.SEASONS[season]["snow"], light=light["label"],
                when_text=(sol.format_when(when) if when_override else ev["when"]))

    if enhanced:
        out = Image.open(enhanced).convert("RGBA")
        if out.size != (W, H):
            print(f"  AI-bilden är {out.size}, skalar till {(W, H)}")
            out = out.resize((W, H), Image.LANCZOS)
        meta["sources"] += "  ·  AI-förbättrad"
    else:
        out = Image.fromarray(
            (R.grade(img, **(light["grade"] or R.SEASONS[season]["grade"])) * 255)
            .astype(np.uint8)).convert("RGBA")
    # Den nakna renderingen går till bildmodellen och ska bara innehålla terräng.
    # Gräns, vimpel och text läggs på efteråt — diffusion mosar linjer och bokstäver.
    pos = arena_on_screen(arena_xy, elev, b, meta, out.size)
    # I skymning och mörker bär arenan en egen ljuskälla. Den ska in i den nakna
    # bilden också, annars ljussätter modellen marken som om den inte fanns.
    lowlight = R.lit_arena(light)
    if bare:
        if outline and wall:
            draw_wall(out, area_xy, elev, b, meta, glow=lowlight)
        if pos and lowlight:
            out = place_glow(out, pos, meta)
    elif pos:
        if outline and enhanced and not wall:
            draw_outline(out, area_xy, elev, b, meta)
        out = place_flag(out, pos, meta, night=light["night"])
    out = out.convert("RGB")
    name = f"tavlingsbild-{event_id}-{tag or season}{'-ai' if enhanced else ''}.png"
    # Bilden lämnas utan text. Krediteringen är då appens ansvar: ortofoto och
    # höjdmodell är CC BY 4.0, och attributionen måste följa bilden där den visas.
    (caption(out, ev, meta) if with_caption else out).save(name)
    print(f"-> {name}  ({time.time()-t0:.1f} s)"
          + (("  [naken: terräng" + (" + arenaljus]" if lowlight else "]")) if bare
             else "" if with_caption else "   [utan text — appen måste bära krediteringen]"))


def draw_outline(img, area_xy, elev, bounds, meta, step=6.0):
    """Tävlingsområdets gräns i bildplanet, med ockluderingstest mot djupbufferten.

    Kanterna följer marken, så de tätas till punkter var sjätte meter och varje punkt
    prövas mot djupet där den hamnar. Ett segment ritas bara när båda ändarna syns —
    annars skulle gränsen löpa tvärs igenom en ås som ligger framför den.
    """
    minx, miny, maxx, maxy = bounds
    gh, gw = elev.shape
    d = ImageDraw.Draw(img, "RGBA")
    W_, H_ = img.size

    ring = list(area_xy) + [area_xy[0]]
    pts = []
    for (e0, n0), (e1, n1) in zip(ring, ring[1:]):
        n = max(2, int(math.hypot(e1 - e0, n1 - n0) / step))
        for i in range(n):
            e, nn = e0 + (e1 - e0) * i / n, n0 + (n1 - n0) * i / n
            px = (e - minx) / (maxx - minx) * (gw - 1)
            py = (maxy - nn) / (maxy - miny) * (gh - 1)
            z = float(T.bilinear(elev, np.array(px), np.array(py)))
            p = meta["project"](e, nn, z)
            if p is None:
                pts.append(None); continue
            x, y, dist = p
            ok = (0 <= x < W_ and 0 <= y < H_
                  and dist <= meta["depth"][int(y), int(x)] + 40)
            pts.append((x, y) if ok else None)

    for a, b_ in zip(pts, pts[1:] + pts[:1]):
        if a and b_:
            d.line([a, b_], fill=(255, 108, 0, 240), width=4)


def arena_on_screen(arena_xy, elev, bounds, meta, size):
    """Var arenan hamnar i bild, och om terrängen låter den synas."""
    minx, miny, maxx, maxy = bounds
    gh, gw = elev.shape
    px = (arena_xy[0] - minx) / (maxx - minx) * (gw - 1)
    py = (maxy - arena_xy[1]) / (maxy - miny) * (gh - 1)
    z = float(T.bilinear(elev, np.array(px), np.array(py)))

    p = meta["project"](arena_xy[0], arena_xy[1], z)
    if p is None:
        return print("  arenan ligger bakom kameran")
    x, y, dist = p
    if not (0 <= x < size[0] and 0 <= y < size[1]):
        return print("  arenan hamnar utanför bilden")
    if dist > meta["depth"][int(y), int(x)] + 40:
        return print("  arenan skyms av terrängen")
    return x, y, dist


def flag_height(dist, meta):
    return H * 0.132 * float(np.clip(meta["d_mid"] / dist, 0.75, 1.4))


def place_glow(img, pos, meta):
    """Bara markskenet, utan vimpel.

    Går bilden till en bildmodell måste ljuskällan finnas i indata — modellen kan
    inte veta att det kommer stå en upplyst vimpel där, och skulle rendera marken
    beckmörk. Flaggan själv utelämnas: bokstäver är det diffusion är sämst på.
    """
    h = flag_height(pos[2], meta)
    return R.night_glow(img, (pos[0], pos[1] - h * 0.22), h * 1.7)


def draw_wall(img, area_xy, elev, bounds, meta, height_m=14.0, step=5.0, glow=False):
    """Tävlingsområdets gräns som en mur i markplanet.

    En volym i stället för ett streck: muren har överkant, sidoyta och skuggsida,
    vilket är sådant en bildmodell återger bra — ett hårstreck är däremot det första
    diffusion smetar ut. Kvadrarna sorteras bak-till-fram och ritas i den ordningen,
    annars täcker den närmaste delen av ringen den bortre.
    """
    minx, miny, maxx, maxy = bounds
    gh, gw = elev.shape
    W_, H_ = img.size

    ring = list(area_xy) + [area_xy[0]]
    samples = []
    for (e0, n0), (e1, n1) in zip(ring, ring[1:]):
        n = max(2, int(math.hypot(e1 - e0, n1 - n0) / step))
        for i in range(n):
            e, nn = e0 + (e1 - e0) * i / n, n0 + (n1 - n0) * i / n
            px = (e - minx) / (maxx - minx) * (gw - 1)
            py = (maxy - nn) / (maxy - miny) * (gh - 1)
            z = float(T.bilinear(elev, np.array(px), np.array(py)))
            g = meta["project"](e, nn, z)
            t = meta["project"](e, nn, z + height_m)
            if not (g and t):
                samples.append(None); continue
            ok = (0 <= g[0] < W_ and -H_ < g[1] < 2 * H_
                  and g[2] <= meta["depth"][int(np.clip(g[1], 0, H_ - 1)),
                                            int(np.clip(g[0], 0, W_ - 1))] + 40)
            samples.append((g, t) if ok else None)

    quads = []
    for a, b in zip(samples, samples[1:] + samples[:1]):
        if a and b:
            quads.append((max(a[0][2], b[0][2]),
                          [(a[1][0], a[1][1]), (b[1][0], b[1][1]),
                           (b[0][0], b[0][1]), (a[0][0], a[0][1])],
                          [(a[1][0], a[1][1]), (b[1][0], b[1][1])]))
    quads.sort(key=lambda q: -q[0])

    def paint(target, fill, cap, cap_w):
        dr = ImageDraw.Draw(target, "RGBA")
        for _, quad, top in quads:
            dr.polygon(quad, fill=fill)
            dr.line(top, fill=cap, width=cap_w)

    if glow:
        # I mörker ska muren lysa. Halon läggs additivt så marken under syns igenom,
        # och den skarpa muren ritas ovanpå — annars blir den suddig i stället för lysande.
        halo = Image.new("RGBA", img.size, (0, 0, 0, 0))
        paint(halo, (220, 96, 18, 255), (240, 150, 70, 255), 3)
        halo = halo.filter(ImageFilter.GaussianBlur(14))
        a = np.asarray(img.convert("RGB")).astype(np.float32) / 255
        hl = np.asarray(halo).astype(np.float32) / 255
        a = np.clip(a + hl[..., :3] * hl[..., 3:4] * 0.85, 0, 1)
        img.paste(Image.fromarray((a * 255).astype(np.uint8)).convert("RGBA"), (0, 0))
        paint(img, (186, 84, 22, 255), (226, 140, 70, 255), 2)
    else:
        paint(img, (236, 104, 12, 255), (255, 168, 74, 255), 3)
    return img


def place_flag(img, pos, meta, night=False):
    """Vimpeln på arenan. Returnerar bilden."""
    h = flag_height(pos[2], meta)
    if night:
        img = R.night_glow(img, (pos[0], pos[1] - h * 0.22), h * 1.7)
    R.draw_flag(img, (pos[0], pos[1]), height=h, sun_dx=meta.get("sun_dx", -0.9))
    return img


if __name__ == "__main__":
    a = sys.argv[1:]
    main(int(a[0]) if a else 59691,
         a[1] if len(a) > 1 and a[1] in R.SEASONS else None,
         next((x for x in a if x.endswith(".png")), None))
