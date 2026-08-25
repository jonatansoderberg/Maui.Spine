"""Genererar Hems provisoriska hjältebild: stiliserade lager, aldrig ett fotografi.

Motsvarigheten till terrängkatalogens generator, med två skillnader som följer av var
bilden ligger. Den är morgonvarm i stället för dagsljus, eftersom hälsningen ovanpå den
säger vilken tid på dygnet det är. Och gradienten sitter i *överkanten*, där texten står,
i stället för i underkanten där tävlingskortens märken sitter.

Deterministisk — samma kommando ger samma fil.
"""
import math
from PIL import Image, ImageDraw, ImageFilter

W, H = 1200, 800

# Morgonljus över barrskog: himlen varm, skogen mörknar framåt så texten har botten att stå på
SKY_TOP = (243, 214, 160)
SKY_HORIZON = (214, 196, 150)
LAYERS = [
    (0.52, (128, 142, 104), 4, 0.045, 0.9),
    (0.64, (86, 108, 76), 5, 0.060, 2.1),
    (0.78, (52, 78, 56), 6, 0.075, 3.6),
    (0.92, (26, 50, 38), 7, 0.090, 5.2),
]


def ridge(draw, base, colour, peaks, amp, seed):
    pts = []
    for x in range(0, W + 1, 6):
        t = x / W
        y = base * H
        for k in range(1, peaks + 1):
            y -= amp * H / k * math.sin(2 * math.pi * k * t + seed * (k + 1))
        pts.append((x, y))
    draw.polygon(pts + [(W, H), (0, H)], fill=colour)


def trees(draw, base, colour, count, height, seed):
    """Granar i siluett. En skog är inte en kulle, och lagren behöver kanten."""
    for i in range(count):
        t = (i + 0.5) / count
        x = t * W
        h = height * H * (0.7 + 0.6 * abs(math.sin(seed * (i + 1))))
        y = base * H - h
        w = h * 0.22
        for step in range(3):
            f = 1 - step * 0.28
            top = y + h * step * 0.26
            draw.polygon(
                [(x, top), (x - w * f, top + h * 0.42), (x + w * f, top + h * 0.42)],
                fill=colour)


img = Image.new("RGB", (W, H), SKY_TOP)
d = ImageDraw.Draw(img)

for y in range(H):
    f = min(1.0, y / (H * 0.55))
    d.line([(0, y), (W, y)], fill=tuple(
        round(SKY_TOP[i] + (SKY_HORIZON[i] - SKY_TOP[i]) * f) for i in range(3)))

# Solen står lågt till höger, som i konceptet — löparen sprang mot ljuset
sun = Image.new("L", (W, H), 0)
sd = ImageDraw.Draw(sun)
for r in range(320, 0, -8):
    sd.ellipse([880 - r, 190 - r, 880 + r, 190 + r], fill=int(150 * (1 - r / 320) ** 2))
img = Image.composite(Image.new("RGB", (W, H), (255, 236, 196)), img,
                      sun.filter(ImageFilter.GaussianBlur(40)))

for base, colour, peaks, amp, seed in LAYERS:
    ridge(d := ImageDraw.Draw(img), base, colour, peaks, amp, seed)
    trees(d, base, colour, int(peaks * 5), amp * 2.4, seed)

img = img.filter(ImageFilter.GaussianBlur(0.7))

# Gradienten i överkanten: hälsningen står där, och vit text på ljus himmel är ingen text
scrim = Image.new("L", (1, H), 0)
for y in range(H):
    t = max(0.0, 1 - y / (H * 0.72))
    scrim.putpixel((0, y), int(165 * t ** 1.3))
img = Image.composite(Image.new("RGB", (W, H), (0, 0, 0)), img, scrim.resize((W, H)))

out = ("/Users/jonatansoderberg/Code/GitHub/Maui.Spine/samples/Orientera/"
       "Resources/Images/hero/hero_home.jpg")
img.save(out, "JPEG", quality=80, optimize=True)
print(out)
