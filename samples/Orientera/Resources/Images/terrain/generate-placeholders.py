"""Genererar provisoriska terrängbilder: stiliserade lager, aldrig fotografier.

Varje bild är en uppsättning horisontella lager i en palett som hör till disciplinen,
med en enkel siluettlinje mellan lagren. Poängen är att HeroImage har något att slå upp
innan de riktiga, kurerade bilderna finns — inte att låtsas vara en plats.
"""
import math
from PIL import Image, ImageDraw, ImageFilter

W, H = 1200, 800

# (himmel, fjärran, mellan, nära) — mörknar framåt så en scrim i underkanten bär text
PALETTES = {
    "sprint_urban":      ((214, 222, 226), (166, 181, 188), (110, 128, 133), (58, 70, 74)),
    "sprint_default":    ((206, 224, 210), (150, 184, 158), (92, 138, 104), (44, 84, 58)),
    "middle_skog":       ((198, 214, 202), (132, 168, 138), (74, 118, 86), (32, 68, 46)),
    "middle_moran":      ((203, 210, 196), (146, 158, 130), (96, 112, 82), (48, 62, 40)),
    "long_skog":         ((190, 209, 197), (120, 160, 132), (64, 110, 80), (26, 62, 42)),
    "long_moran":        ((199, 205, 190), (138, 152, 124), (88, 106, 76), (42, 58, 36)),
    "long_fjall":        ((208, 218, 226), (163, 180, 186), (120, 138, 132), (66, 82, 74)),
    "ultralong_fjall":   ((214, 216, 220), (170, 176, 178), (122, 130, 126), (62, 72, 68)),
    "night_skog":        ((38, 48, 62), (30, 42, 54), (20, 32, 42), (10, 18, 26)),
    "relay_skog":        ((194, 212, 200), (126, 164, 136), (68, 114, 84), (28, 64, 44)),
    "middle_default":    ((198, 214, 202), (132, 168, 138), (74, 118, 86), (32, 68, 46)),
    "long_default":      ((190, 209, 197), (120, 160, 132), (64, 110, 80), (26, 62, 42)),
    "ultralong_default": ((205, 213, 214), (158, 172, 170), (108, 124, 118), (54, 66, 60)),
    "night_default":     ((38, 48, 62), (30, 42, 54), (20, 32, 42), (10, 18, 26)),
    "relay_default":     ((194, 212, 200), (126, 164, 136), (68, 114, 84), (28, 64, 44)),
    "indoor_default":    ((222, 218, 212), (192, 184, 174), (150, 140, 128), (92, 84, 74)),
}

# Hur kuperad siluetten är per terrängtyp, och hur många toppar
SHAPE = {
    "urban":   (0.030, 7),
    "default": (0.055, 4),
    "skog":    (0.070, 5),
    "moran":   (0.090, 6),
    "fjall":   (0.150, 3),
}


def ridge(draw, base_y, amp, peaks, colour, seed):
    """En siluettlinje som fylls nedåt. Deterministisk — samma fil varje körning."""
    pts = []
    for x in range(0, W + 1, 8):
        t = x / W
        y = base_y
        for k in range(1, peaks + 1):
            y -= amp * H / k * math.sin(2 * math.pi * k * t + seed * (k + 1))
        pts.append((x, y))
    draw.polygon(pts + [(W, H), (0, H)], fill=colour)


def make(name, palette):
    terrain = name.split("_", 1)[1]
    amp, peaks = SHAPE.get(terrain, SHAPE["default"])
    sky, far, mid, near = palette

    img = Image.new("RGB", (W, H), sky)
    d = ImageDraw.Draw(img)

    # Himlen tonar mot horisonten hela vägen ned — ett avbrott ger ett synligt band
    for y in range(H):
        f = min(1.0, y / (H * 0.62))
        d.line([(0, y), (W, y)], fill=tuple(
            round(sky[i] + (far[i] - sky[i]) * f) for i in range(3)))

    ridge(d, H * 0.58, amp * 0.55, peaks, far, 0.7)
    ridge(d, H * 0.72, amp * 0.80, peaks + 1, mid, 1.9)
    ridge(d, H * 0.88, amp, peaks + 2, near, 3.4)

    img = img.filter(ImageFilter.GaussianBlur(0.6))

    # Mörk gradient i underkanten (P7): märken ovanpå ska klara kontrastkravet
    scrim = Image.new("L", (1, H), 0)
    for y in range(H):
        t = max(0.0, (y - H * 0.60) / (H * 0.40))
        scrim.putpixel((0, y), int(150 * t ** 1.6))
    img = Image.composite(Image.new("RGB", (W, H), (0, 0, 0)), img,
                          scrim.resize((W, H)))

    out = f"/Users/jonatansoderberg/Code/GitHub/Maui.Spine/samples/Orientera/Resources/Images/terrain/terrain_{name}.jpg"
    img.save(out, "JPEG", quality=78, optimize=True)
    return out


for name, palette in PALETTES.items():
    print(make(name, palette))
