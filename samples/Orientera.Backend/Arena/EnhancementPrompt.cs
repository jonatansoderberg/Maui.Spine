using System.Globalization;
using Orientera.Services.Sources;

namespace Orientera.Backend.Arena;

/// <summary>
/// Komponerar förbättringsprompten till bildmodellen.
/// </summary>
/// <remarks>
/// Prompten skrivs på engelska — bildmodeller är genomgående bättre tränade där — och består
/// av tre delar: vad bilden föreställer, vilket ljus som råder, och vad modellen inte får
/// röra. Den tredje delen är den som betyder något. Diffusion målar gärna dit mogen barrskog
/// där det står ungskog, och just i orientering är terrängen hela poängen.
///
/// Texterna är prototypens, ordagrant — porten mäts strängexakt mot dess utdata.
/// </remarks>
public static class EnhancementPrompt
{
    private static readonly IReadOnlyDictionary<ArenaSeason, string> Look = new Dictionary<ArenaSeason, string>
    {
        [ArenaSeason.Sommar] = "late-summer Swedish countryside, deep green forest, dry sandy ground",
        [ArenaSeason.Var] = "early-spring Swedish countryside, fresh light-green birch, bare fields",
        [ArenaSeason.Host] = "autumn Swedish countryside, yellow and rust foliage, harvested fields",
        [ArenaSeason.Vinter] = "Swedish winter landscape under snow cover, dark conifer stands",
    };

    private static readonly string[] Compass =
    [
        "north", "north-east", "east", "south-east",
        "south", "south-west", "west", "north-west",
    ];

    private static readonly string[] Months =
    [
        "januari", "februari", "mars", "april", "maj", "juni",
        "juli", "augusti", "september", "oktober", "november", "december",
    ];

    // Placeringen är mätdata, gestaltningen är fri. Skillnaden måste stå i prompten,
    // annars läser modellen "gör träden verkliga" som tillåtelse att plantera skog.
    // Texten pekar ut grundproblemet — höjdmodellen saknar hus och kronor, ortofotot är
    // taget rakt uppifrån — så att modellen förstår att höjden ska diktas fram, tydligt.
    private const string Life = """
        The source image is draped with a flat orthophoto taken straight from above,
        so its trees and buildings look pressed into the ground. Give them back their height.
        Seen from this low oblique angle every tree is a standing volume: a rounded sunlit
        crown on the side facing the sun and a clearly shaded far side, visible trunks and
        canopy depth at forest edges, and mature forest rising well above the open ground.
        Buildings rise from their footprints as solid houses with visible walls, gable ends,
        roof pitch and eaves. Every tree and building is anchored by a shadow cast onto the
        ground consistent with the stated light direction, {0}
        Make the relief unmistakable rather than subtle — but change only how the existing
        objects are rendered, never where they are. Their positions, extents and outlines
        come from the photograph and are fixed.
        """;

    private const string Keep = """
        Preserve the photographed terrain exactly. Do not add, remove, move or reshape
        any landform, road, track, path, field boundary, treeline, clearing, building, water body
        or quarry bench. Do not invent vegetation: forest stays where it is and open ground stays
        open. Do not draw any text, letters, numbers, logos, flags, banners, arrows, outlines or
        map symbols anywhere in the frame. Keep the camera, framing and horizon identical.
        """;

    private const string Lamp = """
        A warm artificial light source stands on the ground at the small orange
        glow already visible in the image — floodlighting at the event arena. Render it as a
        real light: warm falloff across the ground around it, nearby vegetation and snow
        catching the light on the side facing it, soft shadows radiating outwards away from
        it, and a subtle glow in the air. Keep it exactly where the glow already is; do not
        add any other light source, lamp post, vehicle or lit window anywhere in the frame.
        """;

    // Hårt specad, för muren är arrangörens faktiska gränsdragning. Flyttar modellen den
    // ljuger bilden om var tävlingsområdet ligger — till skillnad från ett träd som råkar
    // hamna en meter fel.
    private const string Wall = """
        A continuous orange wall stands on the ground exactly along the orange
        band already drawn in the image, enclosing the competition area. Render it as a real
        physical barrier of semi-transparent safety-orange material, like tinted glass: the
        terrain and vegetation behind it stay visible through the face, dimmed and tinted
        orange, while the wall keeps a lighter orange cap along its top edge, casts a soft
        shadow on the ground beside it, and follows the rise and fall of the terrain it
        stands on. It is roughly fifteen metres tall and unbroken — no gates, gaps, posts,
        fencing, mesh or panels. Keep it precisely where the orange band already is: do not
        move, straighten, shorten, extend or re-route it, and do not add any other wall,
        fence or barrier in the frame.
        """;

    private const string WallNight = """
        At night the wall is only faintly lit: a dim, deep warm-orange glow
        in the material itself, just bright enough to trace its line across the dark landscape.
        It is understated, not a neon strip and not a light installation. It spills almost no
        light — at most a barely perceptible warmth on the snow within a metre or two of its
        base — and it must stay dimmer than the floodlit arena. The moonlit snow, the terrain
        and the treelines remain clearly readable and are what the eye settles on; the wall is
        a quiet line at the edge of the scene, not its subject.
        """;

    public static string Compose(
        string competitionName, string? district, ArenaSeason season, Lighting light,
        DateTime when, bool lamp, bool wall)
    {
        var where = string.Join(", ",
            new[] { district, "Sweden" }.Where(part => !string.IsNullOrEmpty(part)));

        string sky, life;
        if (light.Night)
        {
            sky = "clear night under a bright full moon, no sun. Cool blue moonlight is "
                + "strong enough to read the whole landscape by: ground, treelines and "
                + "open areas are all clearly visible in silvery blue, with soft moon "
                + "shadows. Deep blue rather than black, and a starry sky above";
            life = string.Format(Life, "soft and long under low moonlight.");
        }
        else
        {
            var altitude = light.Altitude;
            sky = string.Create(CultureInfo.InvariantCulture,
                $"sunlit day, sun {altitude:F0} degrees above the horizon at azimuth {light.Azimuth:F0} degrees, ")
                + (altitude < 18
                    ? "long raking shadows and warm golden light"
                    : "crisp directional light and clear shadows");
            life = string.Format(Life,
                $"falling towards the {ShadowDirection(light.Azimuth)} and "
                + (altitude < 18 ? "stretched long by the low sun." : "moderately long."));
        }

        return $"Photorealistic oblique aerial photograph of {Look[season]}, near {where}. "
            + $"{sky}. Shot on a full-frame camera with a long lens from a helicopter. "
            + "High dynamic range: open shadows, controlled highlights, rich but natural colour. "
            + $"Sharp micro-detail in vegetation and ground texture.\n\n{life}\n\n"
            + (wall ? Wall + (light.Night ? " " + WallNight : "") + "\n\n" : "")
            + (lamp ? Lamp + "\n\n" : "")
            + $"{Keep}\n\nScene: {competitionName}, {FormatDate(when)}.";
    }

    /// <summary>
    /// "3 mars 2027" — datumet, utan klockslag.
    /// </summary>
    /// <remarks>
    /// Datumet bär årstiden, och den syns i bilden. Klockslaget gör inte det: solhöjden
    /// väljs av <see cref="Lighting.For"/>, inte av starttiden, och ett "18:30" intill
    /// "sun 35 degrees above the horizon" vore en motsägelse mitt i prompten.
    /// </remarks>
    public static string FormatDate(DateTime when) => string.Create(CultureInfo.InvariantCulture,
        $"{when.Day} {Months[when.Month - 1]} {when.Year}");

    /// <summary>Skuggorna pekar dit solen inte är.</summary>
    private static string ShadowDirection(double azimuth) =>
        Compass[(int)((((azimuth + 180) % 360 + 22.5) / 45) % 8)];
}
