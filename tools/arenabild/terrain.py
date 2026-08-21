"""Terrängdata för ett tävlingsområde. Ingen API-nyckel krävs för något av detta."""
import math, os, urllib.parse, urllib.request, concurrent.futures as cf
import numpy as np
from PIL import Image
from pyproj import Transformer

Image.MAX_IMAGE_PIXELS = None
FWD = Transformer.from_crs("EPSG:4326", "EPSG:3006", always_xy=True)
INV = Transformer.from_crs("EPSG:3006", "EPSG:4326", always_xy=True)

HOJD_WMS = "https://minkarta.lantmateriet.se/map/hojdmodell/"
ORTO_WMS = "https://minkarta.lantmateriet.se/map/ortofoto/"
TERRARIUM = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png"
CACHE = "cache"


def sweref(lat, lon):
    return FWD.transform(lon, lat)


def _get(url, path):
    if not os.path.exists(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        req = urllib.request.Request(url, headers={"User-Agent": "orientera-prototype/0.1"})
        with urllib.request.urlopen(req, timeout=90) as r, open(path, "wb") as f:
            f.write(r.read())
    return path


def wms(base, layer, bounds, w, h, fmt="image/jpeg", tag="wms"):
    q = urllib.parse.urlencode({
        "SERVICE": "WMS", "VERSION": "1.1.1", "REQUEST": "GetMap",
        "LAYERS": layer, "STYLES": "", "SRS": "EPSG:3006",
        "BBOX": ",".join(f"{v:.2f}" for v in bounds),
        "WIDTH": w, "HEIGHT": h, "FORMAT": fmt,
    })
    key = f"{CACHE}/{tag}_{layer}_{int(bounds[0])}_{int(bounds[1])}_{w}x{h}.img"
    return np.asarray(Image.open(_get(f"{base}?{q}", key)))


def ortofoto(bounds, w, h):
    """Lantmäteriets ortofoto, 0,25 m. CC BY 4.0."""
    return wms(ORTO_WMS, "Ortofoto_0.25", bounds, w, h, "image/jpeg", "orto").astype(np.float32) / 255


def terrangskuggning(bounds, w, h):
    """Lantmäteriets terrängskuggning — härledd ur 1 m markhöjdmodell, alltså den
    verkliga markformen under skogen, även om vi bara får den som bild."""
    a = wms(HOJD_WMS, "terrangskuggning", bounds, w, h, "image/png", "shade")
    a = a[..., 0] if a.ndim == 3 else a
    return a.astype(np.float32) / 255


def _tile(z, x, y):
    p = f"{CACHE}/terrarium/{z}/{x}/{y}.png"
    try:
        return np.asarray(Image.open(_get(TERRARIUM.format(z=z, x=x, y=y), p)).convert("RGB"))
    except Exception:
        return np.zeros((256, 256, 3), np.uint8)


def elevation(bounds, w, h, z=15):
    """Höjdgeometri ur globala terrarium-rutor, samplad på samma grid som bilderna.

    Grov jämfört med Lantmäteriets 1 m — den bär storformerna, medan
    terrängskuggningen ovanpå bär detaljerna.
    """
    minx, miny, maxx, maxy = bounds
    ex = np.linspace(minx, maxx, w)
    ny = np.linspace(maxy, miny, h)
    E, N = np.meshgrid(ex, ny)
    lon, lat = INV.transform(E, N)

    n = 2 ** z
    sx = (lon + 180.0) / 360.0 * n
    s = np.sin(np.radians(lat))
    sy = (0.5 - np.log((1 + s) / (1 - s)) / (4 * math.pi)) * n

    tx0, tx1 = int(np.floor(sx.min())), int(np.floor(sx.max()))
    ty0, ty1 = int(np.floor(sy.min())), int(np.floor(sy.max()))
    jobs = [(x, y) for x in range(tx0, tx1 + 1) for y in range(ty0, ty1 + 1)]
    with cf.ThreadPoolExecutor(16) as ex_:
        tiles = list(ex_.map(lambda t: _tile(z, *t), jobs))

    mosaic = np.zeros(((ty1 - ty0 + 1) * 256, (tx1 - tx0 + 1) * 256, 3), np.uint8)
    for (x, y), t in zip(jobs, tiles):
        mosaic[(y - ty0) * 256:(y - ty0 + 1) * 256, (x - tx0) * 256:(x - tx0 + 1) * 256] = t
    m = mosaic.astype(np.float32)
    dem_px = (m[..., 0] * 256 + m[..., 1] + m[..., 2] / 256) - 32768

    px = (sx - tx0) * 256
    py = (sy - ty0) * 256
    return bilinear(dem_px, px, py)


def _box(a, r, axis):
    """Boxfiltrering via kumulativ summa — konstant kostnad oavsett radie."""
    if r < 1:
        return a
    n = a.shape[axis]
    pad = [(0, 0)] * a.ndim
    pad[axis] = (r, r)
    p = np.pad(a, pad, mode="edge")
    c = np.cumsum(p, axis=axis, dtype=np.float64)
    zero = np.zeros_like(np.take(c, [0], axis=axis))
    c = np.concatenate([zero, c], axis=axis)
    hi = np.take(c, np.arange(n) + 2 * r + 1, axis=axis)
    lo = np.take(c, np.arange(n), axis=axis)
    return ((hi - lo) / (2 * r + 1)).astype(a.dtype)


def smooth(a, sigma_px):
    """Gaussisk utjämning approximerad med tre boxfiltreringar.

    Tre pass av bredd w ger varians (w^2-1)/4, vilket löser ut radien nedan. Det
    här körs på fulldetaljerade 1 m-grid, så en faltning per rad i Python duger inte.
    """
    if sigma_px < 0.5:
        return a
    w = math.sqrt(4 * sigma_px**2 + 1)
    r = max(1, int(round((w - 1) / 2)))
    for _ in range(3):
        a = _box(_box(a, r, 0), r, 1)
    return a


def bilinear(a, x, y):
    h, w = a.shape[:2]
    x = np.clip(x, 0, w - 1.001); y = np.clip(y, 0, h - 1.001)
    x0 = np.floor(x).astype(np.int32); y0 = np.floor(y).astype(np.int32)
    fx = (x - x0)[..., None] if a.ndim == 3 else (x - x0)
    fy = (y - y0)[..., None] if a.ndim == 3 else (y - y0)
    a00 = a[y0, x0]; a10 = a[y0, x0 + 1]; a01 = a[y0 + 1, x0]; a11 = a[y0 + 1, x0 + 1]
    return (a00 * (1 - fx) * (1 - fy) + a10 * fx * (1 - fy)
            + a01 * (1 - fx) * fy + a11 * fx * fy)


def credentials():
    """Geotorget-inloggning ur miljovariabler, eller ur en fil som användaren äger.

    Filen läses men skrivs aldrig av koden. Skapa den själv:
        printf 'LM_USER=...\nLM_PASS=...\n' > ~/.config/lantmateriet.env
        chmod 600 ~/.config/lantmateriet.env
    """
    import os
    user, pwd = os.environ.get("LM_USER"), os.environ.get("LM_PASS")
    if user and pwd:
        return user, pwd
    path = os.path.expanduser(os.environ.get("LM_CREDS", "~/.config/lantmateriet.env"))
    if os.path.exists(path):
        vals = {}
        for line in open(path):
            if "=" in line and not line.strip().startswith("#"):
                k, _, v = line.strip().partition("=")
                vals[k.strip()] = v.strip()
        return vals.get("LM_USER"), vals.get("LM_PASS")
    return None, None


def _download_tile(href, user, pwd, tries=8):
    """Hämtar en höjdruta till disk.

    dl1 svarar sporadiskt 403 på fullt giltiga anrop — sannolikt lastbalansering där
    inte alla noder känner igen sessionen. Det går över på omförsök. GDAL:s /vsicurl
    ger upp direkt och utan insyn, så nedladdningen sköts här i stället.
    """
    import base64, time
    name = href.rsplit("/", 1)[1]
    path = f"{CACHE}/hojd/{name}"
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    auth = base64.b64encode(f"{user}:{pwd}".encode()).decode()
    for i in range(tries):
        try:
            req = urllib.request.Request(href, headers={"Authorization": f"Basic {auth}"})
            with urllib.request.urlopen(req, timeout=300) as r, open(path + ".part", "wb") as f:
                while chunk := r.read(1 << 20):
                    f.write(chunk)
            os.replace(path + ".part", path)
            return path
        except urllib.error.HTTPError as e:
            if e.code in (401,) and i == 0:
                raise SystemExit(
                    "Lantmäteriet avvisade inloggningen (401). Kontrollera behörigheten "
                    "till Markhöjdmodell Nedladdning i Geotorget, och att den ligger på "
                    "samma konto som LM_USER.") from None
            if e.code not in (403, 429, 500, 502, 503, 504) or i == tries - 1:
                raise
            time.sleep(1.5 * (i + 1))
    raise RuntimeError(f"kunde inte hämta {name}")


def elevation_lm(bounds, w, h):
    """Lantmäteriets 1 m markhöjdmodell, mosaikad till bildens grid.

    STAC-katalogen är öppen; GeoTIFF:erna bakom kräver ett Geotorget-konto med
    behörighet till Markhöjdmodell Nedladdning. Sätt LM_USER/LM_PASS, eller lägg dem
    i ~/.config/lantmateriet.env.
    """
    import json
    import concurrent.futures as cf
    import rasterio
    from rasterio.merge import merge
    from rasterio.enums import Resampling

    user, pwd = credentials()
    if not (user and pwd):
        return None

    lo = INV.transform(bounds[0], bounds[1])
    hi = INV.transform(bounds[2], bounds[3])
    url = ("https://api.lantmateriet.se/stac-hojd/v1/search"
           f"?bbox={lo[0]},{lo[1]},{hi[0]},{hi[1]}&limit=100")
    with urllib.request.urlopen(url, timeout=60) as r:
        feats = json.load(r)["features"]
    hrefs = [f["assets"]["data"]["href"] for f in feats
             if f["collection"].startswith("mhm-") and f["assets"]["data"]["href"].endswith(".tif")]
    if not hrefs:
        return None

    with cf.ThreadPoolExecutor(4) as ex:
        paths = list(ex.map(lambda u_: _download_tile(u_, user, pwd), hrefs))

    res = (bounds[2] - bounds[0]) / w
    srcs = [rasterio.open(p) for p in paths]
    dem, _ = merge(srcs, bounds=bounds, res=res, resampling=Resampling.bilinear)
    for s in srcs:
        s.close()
    dem = dem[0].astype(np.float32)
    dem[dem < -1000] = np.nan
    dem = np.where(np.isfinite(dem), dem, np.nanmedian(dem))

    # merge härleder höjden ur upplösningen och kan hamna en rad fel mot det grid
    # bilderna ligger på. Samplas om till exakt begärd storlek hellre än att lita
    # på att avrundningen råkar stämma.
    if dem.shape != (h, w):
        yy, xx = np.meshgrid(np.linspace(0, dem.shape[0] - 1, h),
                             np.linspace(0, dem.shape[1] - 1, w), indexing="ij")
        dem = bilinear(dem, xx, yy).astype(np.float32)
    return dem
