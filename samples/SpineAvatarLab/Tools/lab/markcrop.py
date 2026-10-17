# markcrop.py OWNER.png BOXES CAPTURE.png ... OUT.jpg : maps boxes (x0,y0,x1,y1;... in the owner's screenshot) into each lab
# capture by the hair's bounding box (as viewmatch.py aligns), crops them with a margin and puts owner | captures side by side.
# Also prints the dark-navy pixel count (the cap / skull roots: bluish, luminance < 70) inside each mapped box per capture.
import sys, numpy as np
from PIL import Image, ImageDraw
owner, boxes, *rest = sys.argv[1:]; out = rest[-1]; caps = rest[:-1]
boxes = [tuple(int(v) for v in b.split(',')) for b in boxes.split(';')]
def hairmask(im):
    x = np.array(im.convert('RGB')).astype(int); mx, mn = x.max(-1), x.min(-1); sat = (mx - mn) / np.maximum(mx, 1)
    return (sat > 0.18) & (mx > 40) & (x[..., 2] > x[..., 0])
def bbox(m):
    ys, xs = np.where(m); return xs.min(), ys.min(), xs.max(), ys.max()
def navy(im, box):
    x = np.array(im.convert('RGB')).astype(int)[box[1]:box[3], box[0]:box[2]]
    lum = 0.3 * x[..., 0] + 0.59 * x[..., 1] + 0.11 * x[..., 2]; return int(((x[..., 2] > x[..., 0] + 10) & (lum < 70)).sum())
oim = Image.open(owner); ob = bbox(hairmask(oim)); ow = ob[2] - ob[0]
tiles = []
for bi, b in enumerate(boxes):
    m = 40; crop = oim.crop((b[0] - m, b[1] - m, b[2] + m, b[3] + m)); d = ImageDraw.Draw(crop); d.rectangle([m, m, m + b[2] - b[0], m + b[3] - b[1]], outline=(255, 0, 0), width=3)
    row = [crop.resize((360, int(360 * crop.height / crop.width)))]
    for c in caps:
        cim = Image.open(c); cb = bbox(hairmask(cim)); s = (cb[2] - cb[0]) / ow
        mb = tuple(int(round(cb[0] + (v - ob[0]) * s)) if i % 2 == 0 else int(round(cb[1] + (v - ob[1]) * s)) for i, v in enumerate(b))
        mm = int(m * s); crop = cim.crop((mb[0] - mm, mb[1] - mm, mb[2] + mm, mb[3] + mm)); d = ImageDraw.Draw(crop); d.rectangle([mm, mm, mm + mb[2] - mb[0], mm + mb[3] - mb[1]], outline=(255, 0, 0), width=3)
        row.append(crop.resize((360, int(360 * crop.height / crop.width)))); print(f'box {bi} {c.split("/")[-1]}: mapped {mb}, dark navy px {navy(cim, mb)}')
    tiles.append(row)
H = sum(max(t.height for t in r) for r in tiles); W = max(len(r) for r in tiles) * 370
sheet = Image.new('RGB', (W, H), (40, 40, 40)); y = 0
for r in tiles:
    for i, t in enumerate(r): sheet.paste(t, (i * 370, y))
    y += max(t.height for t in r)
sheet.save(out, quality=88); print('sheet', out)
