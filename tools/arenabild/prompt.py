"""Komponerar förbättringsprompten till bildmodellen.

Prompten skrivs på engelska — bildmodeller är genomgående bättre tränade där — och
består av tre delar: vad bilden föreställer, vilket ljus som råder, och vad modellen
inte får röra. Den tredje delen är den som betyder något. Diffusion målar gärna dit
mogen barrskog där det står ungskog, och just i orientering är terrängen hela poängen.
"""

LOOK = {
    "sommar": "late-summer Swedish countryside, deep green forest, dry sandy ground",
    "var":    "early-spring Swedish countryside, fresh light-green birch, bare fields",
    "host":   "autumn Swedish countryside, yellow and rust foliage, harvested fields",
    "vinter": "Swedish winter landscape under snow cover, dark conifer stands",
}

COMPASS = ["north", "north-east", "east", "south-east",
           "south", "south-west", "west", "north-west"]


def _shadow_dir(az):
    """Skuggorna pekar dit solen inte är."""
    return COMPASS[int(((az + 180) % 360 + 22.5) // 45) % 8]


# Placeringen är mätdata, gestaltningen är fri. Skillnaden måste stå i prompten,
# annars läser modellen "gör träden verkliga" som tillåtelse att plantera skog.
LIFE = """Render the vegetation and buildings that are already present as real
three-dimensional objects rather than a flat photographic overlay: individual tree
crowns with volume and gaps between them, trunks and canopy depth at forest edges,
buildings with walls, roof pitch and eaves. Every tree and building casts a shadow
onto the ground consistent with the stated light direction, {shadow_note}
This changes only how the existing objects are rendered — never where they are.
Their positions, extents and outlines come from the photograph and are fixed."""

KEEP = """Preserve the photographed terrain exactly. Do not add, remove, move or reshape
any landform, road, track, path, field boundary, treeline, clearing, building, water body
or quarry bench. Do not invent vegetation: forest stays where it is and open ground stays
open. Do not draw any text, letters, numbers, logos, flags, banners, arrows, outlines or
map symbols anywhere in the frame. Keep the camera, framing and horizon identical."""


LAMP = """A warm artificial light source stands on the ground at the small orange
glow already visible in the image — floodlighting at the event arena. Render it as a
real light: warm falloff across the ground around it, nearby vegetation and snow
catching the light on the side facing it, soft shadows radiating outwards away from
it, and a subtle glow in the air. Keep it exactly where the glow already is; do not
add any other light source, lamp post, vehicle or lit window anywhere in the frame."""


# Hårt specad, för muren är arrangörens faktiska gränsdragning. Flyttar modellen den
# ljuger bilden om var tävlingsområdet ligger — till skillnad från ett träd som råkar
# hamna en meter fel.
WALL = """A continuous solid orange wall stands on the ground exactly along the orange
band already drawn in the image, enclosing the competition area. Render it as a real
physical barrier: a smooth vertical face in saturated safety orange, a lighter orange
cap along its top edge, a visible shadow cast on the ground beside it, and the whole
wall following the rise and fall of the terrain it stands on. It is roughly fifteen
metres tall and unbroken — no gates, gaps, posts, fencing, mesh or panels. Keep it
precisely where the orange band already is: do not move, straighten, shorten, extend
or re-route it, and do not add any other wall, fence or barrier in the frame."""


WALL_NIGHT = """At night the wall is only faintly lit: a dim, deep warm-orange glow
in the material itself, just bright enough to trace its line across the dark landscape.
It is understated, not a neon strip and not a light installation. It spills almost no
light — at most a barely perceptible warmth on the snow within a metre or two of its
base — and it must stay dimmer than the floodlit arena. The moonlit snow, the terrain
and the treelines remain clearly readable and are what the eye settles on; the wall is
a quiet line at the edge of the scene, not its subject."""


def enhancement(ev, season, light, when_text, lamp=False, wall=False):
    where = ", ".join(x for x in (ev.get("district"), "Sweden") if x)
    if light["night"]:
        sky = ("clear night under a bright full moon, no sun. Cool blue moonlight is "
               "strong enough to read the whole landscape by: ground, treelines and "
               "open areas are all clearly visible in silvery blue, with soft moon "
               "shadows. Deep blue rather than black, and a starry sky above")
        life = LIFE.format(shadow_note="soft and long under low moonlight.")
    else:
        a = light["alt"]
        sky = (f"sunlit day, sun {a:.0f} degrees above the horizon at azimuth "
               f"{light['az']:.0f} degrees, "
               + ("long raking shadows and warm golden light" if a < 18 else
                  "crisp directional light and clear shadows"))
        life = LIFE.format(
            shadow_note=f"falling towards the {_shadow_dir(light['az'])} and "
                        + ("stretched long by the low sun." if a < 18 else "moderately long."))
    return (
        f"Photorealistic oblique aerial photograph of {LOOK[season]}, near {where}. "
        f"{sky}. Shot on a full-frame camera with a long lens from a helicopter. "
        f"High dynamic range: open shadows, controlled highlights, rich but natural colour. "
        f"Sharp micro-detail in vegetation and ground texture.\n\n{life}\n\n"
        + (WALL + (" " + WALL_NIGHT if light["night"] else "") + "\n\n" if wall else "")
        + (LAMP + "\n\n" if lamp else "") + f"{KEEP}\n\n"
        f"Scene: {ev['name']}, {when_text}."
    )
