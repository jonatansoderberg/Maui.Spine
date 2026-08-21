"""Renderar en snedbild över ett tävlingsområde.

Höjdfältet ritas med en klassisk voxel-strålmarsch: vyn samplas som ett rutnät i
kamerans eget koordinatsystem (djup x sidled), varje kolumn projiceras, och
ockludering faller ut gratis ur en suffixminimering. Allt vektoriserat i numpy —
en tävlingsbild ska renderas på sekunder i backend, inte minuter.
"""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
import terrain as T

SUN_AZ, SUN_ALT = math.radians(315.0), math.radians(38.0)   # reserv: kartografisk konvention

# Årstiden styr markens utseende och graderingen. Ljusets färg gör den inte — den
# följer solhöjden, som räknas fram ur tävlingens datum, tid och position.
SEASONS = {
    "sommar": dict(snow=False, gain=1.74, amb=0.34,
                   grade=dict(local=0.66, contrast=0.40, warmth=0.085, sat=1.32, veg=0.55)),
    "var":    dict(snow=False, gain=1.78, amb=0.36,
                   grade=dict(local=0.64, contrast=0.38, warmth=0.070, sat=1.34, veg=0.66)),
    "host":   dict(snow=False, gain=1.70, amb=0.33,
                   grade=dict(local=0.68, contrast=0.42, warmth=0.115, sat=1.28, veg=0.26)),
    "vinter": dict(snow=True,  gain=1.70, amb=0.38,
                   grade=dict(local=0.58, contrast=0.36, warmth=0.055, sat=1.06, veg=0.30)),
}


# Solhöjd under vilken arenan får tänd belysning i bilden. Över den är marken
# fortfarande solbelyst och en strålkastare skulle varken vara tänd eller synas.
# En kvällstävling slutar ofta i mörker ändå — höj talet om bilden ska visa det
# snarare än startögonblicket.
LAMP_BELOW = 8.0


def lit_arena(light):
    return light["night"] or light["alt"] < LAMP_BELOW


def lighting(alt_deg, az_deg):
    """Ljusets riktning, färg och styrka vid en given solhöjd.

    Tre regimer. I dagsljus går låg sol genom mer atmosfär, som sprider bort det blå —
    kvällstonen behöver inte väljas, den faller ut. Under horisonten finns ingen sol
    att skugga med, och då byts ljuskällan mot månen: vid fullmåne står den mitt emot
    solen, så azimuten är solens plus 180. Nattbilden är en stilisering — ortofotot är
    taget i dagsljus, och ingen efemerid säger att månen faktiskt lyser den natten.
    """
    if alt_deg < -6.0:                                   # natt
        return dict(az=(az_deg + 180.0) % 360.0, alt=32.0, night=True,
                    sun=np.array([0.58, 0.72, 1.00], np.float32),
                    sky=np.array([0.30, 0.38, 0.60], np.float32),
                    haze=np.array([0.13, 0.17, 0.30], np.float32), haze_k=0.50,
                    gain=0.66, amb=1.55,
                    grade=dict(local=0.34, contrast=0.22, warmth=-0.045, sat=0.68, veg=0.0),
                    label="natt, månsken (stiliserat)")

    t = np.clip((alt_deg - 4.0) / 46.0, 0, 1)            # 4 grader -> 50 grader
    lerp = lambda a, b: np.float32(np.array(a) + (np.array(b) - np.array(a)) * t)
    return dict(az=az_deg, alt=max(alt_deg, 5.0), night=False,
                sun=lerp([1.48, 0.90, 0.52], [1.06, 1.00, 0.93]),
                sky=lerp([0.50, 0.66, 1.08], [0.76, 0.85, 1.00]),
                haze=lerp([0.96, 0.85, 0.70], [0.82, 0.85, 0.90]),
                haze_k=float(0.50 - 0.12 * t), gain=1.0, amb=1.0, grade=None,
                label=f"solhöjd {alt_deg:.0f}°, azimut {az_deg:.0f}°")


def winterize(ortho):
    """Snötäcker ortofotot utifrån vad det visar.

    Barrskog skiljs från öppen mark på att den är både grön och mörk — en enda av
    egenskaperna räcker inte, åkrar är gröna och asfalt är mörk. Öppen mark får snö
    som behåller fotots ljushetsvariation, så vägar och diken syns igenom; skogen blir
    mörk och rimfrostad. Detta är en syntes, inte en mätning: det finns inget
    vinterortofoto bakom, och bilden ska märkas därefter.
    """
    lum = _lum(ortho)
    green = ortho[..., 1] - 0.5 * (ortho[..., 0] + ortho[..., 2])
    # Trösklarna är kalibrerade mot ortofotots faktiska fördelning, inte gissade:
    # grönheten ligger kring 0,03 och luminansen kring 0,30 i den här sortens bild.
    forest = (np.clip((green - 0.005) / 0.045, 0, 1)
              * np.clip((0.50 - lum) / 0.30, 0, 1))[..., None]

    snow = np.array([0.90, 0.93, 0.99], np.float32) * (0.80 + 0.30 * lum)[..., None]
    tree = np.array([0.17, 0.23, 0.21], np.float32) + 0.30 * np.array([0.55, 0.60, 0.66], np.float32)
    return np.clip(snow * (1 - forest) + tree * forest, 0, 1)


def shade_texture(ortho, shade, elev, res, season="sommar", sun=None):
    """Ortofoto som albedo, terrängskuggning som form, plus en egen ljussättning.

    Ortofotot ensamt över svensk skog är en grön filt. Skuggningen bär den riktiga
    markformen — den är härledd ur 1 m-modellen — så den multipliceras in.
    """
    S = SEASONS[season]
    L = lighting(*(sun[::-1] if sun else (math.degrees(SUN_ALT), math.degrees(SUN_AZ))))
    lam = hillshade(elev, res, az=math.radians(L["az"]), alt=math.radians(L["alt"]))

    micro = np.clip((shade - shade.mean()) * 1.5 + 0.5, 0.05, 1.0)
    direct = lam * (0.62 + 0.38 * micro)
    ambient = S["amb"] * L["amb"] * (0.55 + 0.45 * micro)

    # Lyft mättnaden lite — ortofoton är sammansatta av många flygpass och blir platta.
    base = winterize(ortho) if S["snow"] else ortho
    grey = base.mean(2, keepdims=True)
    base = np.clip(grey + (base - grey) * 1.35, 0, 1)
    lit = L["sun"] * (0.78 * direct)[..., None] + L["sky"] * ambient[..., None]
    return np.clip((base * lit * S["gain"] * L["gain"]) ** 0.90, 0, 1)


def bake_outline(tex, bounds, area_xy, res):
    """Tävlingsområdets gräns, ritad i markplanet så den draperas med terrängen."""
    h, w = tex.shape[:2]
    minx, miny, maxx, maxy = bounds
    to_px = lambda p: ((p[0] - minx) / (maxx - minx) * w, (maxy - p[1]) / (maxy - miny) * h)

    img = Image.fromarray((tex * 255).astype(np.uint8))
    d = ImageDraw.Draw(img, "RGBA")
    poly = [to_px(p) for p in area_xy]
    d.polygon(poly, fill=(255, 246, 214, 22))
    d.line(poly + [poly[0]], fill=(255, 108, 0, 235), width=max(2, int(4 / res)))
    return np.asarray(img).astype(np.float32) / 255


def view_extent(area_xy, azimuth):
    """Tävlingsområdets utsträckning längs och tvärs blickriktningen."""
    fx, fy = math.sin(azimuth), math.cos(azimuth)
    along = [p[0] * fx + p[1] * fy for p in area_xy]
    across = [p[0] * math.cos(azimuth) - p[1] * math.sin(azimuth) for p in area_xy]
    cx = sum(p[0] for p in area_xy) / len(area_xy)
    cy = sum(p[1] for p in area_xy) / len(area_xy)
    return (cx, cy), max(along) - min(along), max(across) - min(across)


def render(bounds, elev, tex, area_xy, season="sommar", sun=None, W=1920, H=1080, azimuth=0.0,
           pitch=34.0, fill=0.60, reach=2.2, vex=None, back=1.5, center_y=0.5,
           vex_max=1.7):
    """Snedbild av höjdfältet.

    Kameran ställs geometriskt: `pitch` är depressionsvinkeln mot områdets mitt och
    `fill` hur stor del av bildhöjden området ska uppta. Brännvidden faller ut ur
    dessa två, och huvudpunkten skjuts uppåt ur bild — ett tilt-shift, vilket är
    riktig perspektiv och håller horisonten utanför ramen.
    """
    minx, miny, maxx, maxy = bounds
    gh, gw = elev.shape
    th = math.radians(pitch)
    fx, fy = math.sin(azimuth), math.cos(azimuth)
    rx, ry = math.cos(azimuth), -math.sin(azimuth)

    (acx, acy), L, across = view_extent(area_xy, azimuth)
    base = float(np.percentile(elev, 30))
    relief = float(np.percentile(elev, 98) - np.percentile(elev, 2))

    R = reach * max(L, across)                 # snedavstånd till områdets mitt
    d_mid, cam_h = R * math.cos(th), R * math.sin(th)
    f = fill * H * d_mid / (L * math.sin(th))  # brännvidd i pixlar

    cam_e, cam_n = acx - fx * d_mid, acy - fy * d_mid
    cam_z = base + cam_h
    horizon = H * center_y - f * math.tan(th)  # negativ: huvudpunkten ligger ovanför bilden

    if vex is None:
        vex = float(np.clip(0.07 * H * d_mid / (f * max(relief, 1)), 1.0, vex_max))

    d_near = max(60.0, d_mid - L * 0.85)
    d_far = d_mid + L * back
    steps = 1500
    d = (d_near + np.linspace(0, 1, steps) ** 1.4 * (d_far - d_near))[::-1]

    D = d[:, None]
    off = (np.arange(W) - W / 2)[None, :] * D / f
    E, N = cam_e + fx * D + rx * off, cam_n + fy * D + ry * off

    px = (E - minx) / (maxx - minx) * (gw - 1)
    py = (maxy - N) / (maxy - miny) * (gh - 1)
    inside = (px >= 0) & (px <= gw - 1) & (py >= 0) & (py <= gh - 1)
    z = T.bilinear(elev, px, py)
    rgb = T.bilinear(tex, px, py)

    L = lighting(*(sun[::-1] if sun else (math.degrees(SUN_ALT), math.degrees(SUN_AZ))))
    haze = np.clip((D - d_near) / (d_far - d_near), 0, 1)[..., None] ** 1.7
    rgb = rgb * (1 - haze * L["haze_k"]) + L["haze"] * (haze * L["haze_k"])
    rgb = np.where(inside[..., None], rgb, L["haze"])

    ys = horizon - (base + (z - base) * vex - cam_z) * f / D
    ys = np.where(inside, ys, 1e6)

    # Suffixminimum längs djupet (raderna går fjärran -> nära, så suffixet är alltid
    # "allt som ligger närmare"). Det gör r stigande, och en binärsökning per bildrad
    # ger direkt det närmaste djupsteg som täcker pixeln. Ingen span-fyllning behövs.
    r = np.minimum.accumulate(ys[::-1], axis=0)[::-1]
    rows = np.arange(H, dtype=np.float32)
    idx = np.empty((H, W), np.int32)
    for c in range(W):
        idx[:, c] = np.searchsorted(r[:, c], rows, side="right") - 1

    top, bottom = (L["haze"] * 0.45, L["haze"] * 1.05) if L["night"] else (
        L["haze"] * 0.86, L["haze"] * 1.12)
    ramp = np.linspace(0, 1, H, dtype=np.float32)[:, None, None]
    img = np.clip(top + (bottom - top) * ramp, 0, 1) * np.ones((H, W, 1), np.float32)
    cc = np.broadcast_to(np.arange(W), (H, W))
    hit = idx >= 0
    img = np.where(hit[..., None], rgb[np.clip(idx, 0, steps - 1), cc], img)

    # Djupbuffert i meter, för att kunna avgöra om något står framför eller bakom terrängen.
    depth = np.where(hit, np.take(d, np.clip(idx, 0, steps - 1)), np.inf)

    def project(e, n, z_ground):
        """Världspunkt -> (kolumn, rad, avstånd). Avståndet jämförs mot djupbufferten."""
        vx, vy = e - cam_e, n - cam_n
        dd = vx * fx + vy * fy
        if dd <= 1.0:
            return None
        off_ = vx * rx + vy * ry
        return (W / 2 + off_ * f / dd,
                horizon - (base + (z_ground - base) * vex - cam_z) * f / dd,
                dd)

    return np.clip(img, 0, 1), dict(vex=round(vex, 2), relief=round(relief, 1),
                                    pitch=pitch, cam_h=round(cam_h), d_mid=round(d_mid),
                                    project=project, depth=depth)


def hillshade(elev, res, az=SUN_AZ, alt=SUN_ALT):
    gy, gx = np.gradient(elev, res)
    nz = 1.0 / np.sqrt(gx**2 + gy**2 + 1.0)
    return np.clip(-gx * nz * math.cos(alt) * math.sin(az)
                   - gy * nz * math.cos(alt) * math.cos(az)
                   + nz * math.sin(alt), 0, 1)


def local_relief(elev, res, sigma_m=18.0):
    """Höjd relativt omgivningen. Det är det här som lyfter fram diken, täktkanter
    och åsryggar — former som är små i höjd men skarpa i utbredning, och som en ren
    lutningsskuggning trycker ihop."""
    broad = T.smooth(elev, max(1.0, sigma_m / res))
    d = elev - broad
    s = max(float(np.percentile(np.abs(d), 96)), 0.05)
    return np.tanh(d / s)


def relief_texture(elev, res, shade=None):
    """Markformen naken, i gipsmodellens anda.

    Ljussättningen är medvetet högdagrad: `v` tillåts gå över 1 och klippas, så att
    solvända ytor blir rent vita och formen bärs av skuggsidorna. Det är gipsmodellens
    uttryck, och det tål inte att "rättas" till ett balanserat histogram — då blir det
    en grå gröt. Kontrasten sitter i skuggan, inte i mitten.

    Två skalor räcker: en slät som bär formen och en fin som bär detaljen. Riktig
    1 m-data har gott om mikrostruktur — plogfåror, laserbrus — som lokalreliefen
    förstärkte till melering, så den är borta.

    `shade` är Lantmäteriets färdiga terrängskuggning, och används bara när vi saknar
    höjddata med detalj nog att räkna fram den själva.
    """
    lam = hillshade(T.smooth(elev, max(1.0, 9.0 / res)), res)
    micro = (np.clip((shade - shade.mean()) * 1.8 + 0.5, 0, 1) if shade is not None
             else hillshade(T.smooth(elev, max(1.0, 2.5 / res)), res))

    v = np.clip(0.32 + 0.54 * lam + 0.38 * (micro - 0.5), 0, 1)

    t = np.clip((elev - np.percentile(elev, 3))
                / max(np.percentile(elev, 97) - np.percentile(elev, 3), 1), 0, 1)[..., None]
    cool = np.array([0.40, 0.50, 0.52], np.float32)
    warm = np.array([0.94, 0.88, 0.74], np.float32)
    return np.clip((cool + (warm - cool) * t) * (v[..., None] * 1.55), 0, 1)


def _font(sz, bold=False):
    for p in (f"/System/Library/Fonts/Supplemental/Arial{' Bold' if bold else ''}.ttf",
              "/System/Library/Fonts/Helvetica.ttc"):
        try:
            return ImageFont.truetype(p, sz)
        except Exception:
            pass
    return ImageFont.load_default()


ORANGE = (243, 112, 20)
INK = (26, 26, 30)


def _bezier(p0, p1, p2, n=36):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
             (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1])
            for t in (i / n for i in range(n + 1))]


def _flag_flat(fw, fh, label, font_path):
    """Duken utbredd och platt, innan den böjs. Returnerar (RGBA, mast-x)."""
    px = int(0.11 * fw) + 4
    W, H = px + fw + 6, fh + 6
    duk = (_bezier((px, 0), (px + 0.78 * fw, 0.005 * fh), (px + fw, 0.22 * fh))
           + [(px + fw, fh), (px, fh)])

    mask = Image.new("L", (W, H), 0)
    ImageDraw.Draw(mask).polygon(duk, fill=255)

    band = Image.new("RGBA", (W, H), (252, 252, 252, 255))
    bd = ImageDraw.Draw(band)
    o = fw * 2
    bd.polygon([(px - o, -o), (px + fw + o, -o),
                (px + fw + o, 0.34 * fh), (px - o, 0.10 * fh)], fill=ORANGE)
    bd.polygon([(px - o, fh - 0.32 * fh), (px + fw + o, fh - 0.09 * fh),
                (px + fw + o, fh + o), (px - o, fh + o)], fill=ORANGE)
    f = ImageFont.truetype(font_path, int(fw * 0.60))
    bd.text((px + fw / 2, fh * 0.45), label, font=f, fill=INK, anchor="mm")

    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    out.paste(band, mask=mask)
    return out, px


def flag_tile(height, label="TC", ss=4, wave=1.0):
    """Beachflag som böjd duk, inte platt dekal.

    Duken ritas först utbredd och böjs sedan: varje bildkolumn förskjuts i höjdled
    efter en sinus, och skuggas efter sinusens derivata. Det är derivatan som är
    poängen — den ger ytans lutning, alltså hur mycket ljus kolumnen fångar, och det
    är skillnaden mellan en flagga som ser tryckt ut och en som ser ut att fånga vind.
    """
    fh = int(height * 0.86 * ss)
    fw = int(fh * 0.30)
    flat, px = _flag_flat(fw, fh, label,
                          "/System/Library/Fonts/Supplemental/Arial Bold.ttf")
    a = np.asarray(flat).astype(np.float32) / 255.0
    H, W = a.shape[:2]

    u = (np.arange(W) - px) / max(fw, 1)          # 0 vid masten, 1 vid ytterkanten
    phase = 1.9 * math.pi * u
    amp = fh * 0.022 * wave * np.clip(u, 0, 1) ** 1.3
    shift = amp * np.sin(phase)
    slope = np.cos(phase) * np.clip(u, 0, 1) ** 1.3
    shade = np.clip(0.90 + 0.30 * slope - 0.08 * np.clip(u, 0, 1), 0.50, 1.20)

    rows = np.arange(H)[:, None] - shift[None, :]
    out = np.empty_like(a)
    for c in range(4):
        out[..., c] = T.bilinear(a[..., c],
                                 np.broadcast_to(np.arange(W), (H, W)).astype(np.float32),
                                 np.clip(rows, 0, H - 1).astype(np.float32))
    out[..., :3] = np.clip(out[..., :3] * shade[None, :, None], 0, 1)

    tile = Image.new("RGBA", (W, H + int(height * 0.16 * ss)), (0, 0, 0, 0))
    tile.paste(Image.fromarray((out * 255).astype(np.uint8)), (0, 0))

    d = ImageDraw.Draw(tile)
    mast_w = max(2, int(0.075 * fw))
    d.line([(px, 0), (px, tile.height - 2 * ss)], fill=INK, width=mast_w)
    d.line([(px - mast_w * 0.28, 0), (px - mast_w * 0.28, tile.height - 2 * ss)],
           fill=(150, 154, 162, 255), width=max(1, mast_w // 3))     # dager på masten
    foot, base_y = int(0.34 * fw), tile.height - 2 * ss
    for dx, dy in ((-foot, 0.30), (foot, 0.30), (-foot * 0.45, 0.55), (foot * 0.45, 0.55)):
        d.line([(px, base_y - foot * 0.30), (px + dx, base_y + foot * dy * 0.0 + 2 * ss)],
               fill=INK, width=max(2, int(0.05 * fw)))

    s = max(1, ss)
    return tile.resize((W // s, tile.height // s), Image.LANCZOS), px // s


def draw_flag(img, xy, height, label="TC", sun_dx=-0.9):
    """Vimpeln på marken vid xy, med markskugga så den står i bilden i stället för på den."""
    tile, px = flag_tile(height, label)
    x, y = int(xy[0]), int(xy[1])

    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    L = height * 0.42
    sd.polygon([(x - 3, y), (x + 3, y),
                (x + sun_dx * L + 5, y + L * 0.30), (x + sun_dx * L - 3, y + L * 0.30)],
               fill=(18, 22, 28, 120))
    img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(max(1, height * 0.02))))
    img.alpha_composite(tile, (x - px, y - tile.height + 1))
    return img


def _lum(img):
    # Explicit summa i stället för matmul: numpys matmul sätter spuriösa flyttalsflaggor
    # på den här plattformens BLAS. Resultatet är identiskt.
    return (img * np.array([0.2126, 0.7152, 0.0722], np.float32)).sum(-1)


def grade(img, local=0.62, contrast=0.38, warmth=0.075, sat=1.26, veg=0.0):
    """Slutgradering: lokalkontrast, filmisk S-kurva, delad toning och mättnad.

    Lokalkontrasten är det som läser som HDR — en oskarp mask på luminansen lyfter
    struktur i mellantonerna utan att röra den globala exponeringen. Delad toning
    lägger värme i högdagrarna och kyla i skuggorna, vilket är det som skiljer en
    solbelyst bild från en gråmulen.
    """
    img = np.nan_to_num(np.clip(img, 0, 1).astype(np.float32))
    L = _lum(img)
    Lb = T.smooth(L, max(2.0, img.shape[0] / 26))
    # Additivt, inte som kvot: i helsvarta pixlar är luminansen noll och kvoten spricker.
    img = np.clip(img + local * (L - Lb)[..., None], 0, 1)

    x = np.clip(img, 0, 1)
    img = x * (1 - contrast) + (x * x * (3 - 2 * x)) * contrast

    lum = np.clip(_lum(img), 0, 1)[..., None]
    cool = np.array([1 - warmth * 0.7, 1.0, 1 + warmth * 1.2], np.float32)
    warm = np.array([1 + warmth, 1.0, 1 - warmth * 1.5], np.float32)
    img = img * (cool + (warm - cool) * lum)

    g = _lum(img)[..., None]
    img = np.clip(g + (img - g) * sat, 0, 1)

    # Vegetationslyft: varmt ljus multiplicerat på grönt drar ur mättnaden, så den
    # läggs tillbaka selektivt där grönt dominerar. Motsvarar ett HSL-grepp, inte
    # en global mättnadshöjning som skulle göra sanden neonorange.
    if veg:
        m = np.clip((img[..., 1] - np.maximum(img[..., 0], img[..., 2])) / 0.06, 0, 1)[..., None]
        gl = _lum(img)[..., None]
        img = np.clip(img * (1 - m * veg) + (gl + (img - gl) * 1.9) * (m * veg), 0, 1)
    return img


def night_glow(img, xy, radius, strength=0.42):
    """Vimpeln som ljuskälla i mörker.

    Additivt, inte alfa över: ljus lägger sig till det som redan finns i stället för
    att ersätta det, så marken under fortsätter synas igenom skenet. Faller av
    kvadratiskt och plattas ut mot marken, eftersom ljuskällan står strax ovanför den.
    """
    a = np.asarray(img.convert("RGB")).astype(np.float32) / 255.0
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.sqrt(((xx - xy[0]) / radius) ** 2 + ((yy - xy[1]) / (radius * 0.42)) ** 2)
    fall = np.clip(1.0 - d, 0, 1) ** 2.2
    warm = np.array([1.00, 0.68, 0.34], np.float32)
    a = np.clip(a + fall[..., None] * warm * strength, 0, 1)
    return Image.fromarray((a * 255).astype(np.uint8)).convert("RGBA")
