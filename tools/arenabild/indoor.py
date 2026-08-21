"""Generisk inomhusbild för indoor-tävlingar.

Indoor har ingen arenakoordinat i Eventor och ingen terräng att rendera, så bilden
byggs från text i stället för från mätdata. Den föreställer därför ingen viss skola
och ska inte utges för att göra det.
"""

# Kontrollskärmen är det enda i bilden som måste bli exakt rätt: varje orienterare
# läser den utan att tänka, och en felritad skärm underkänner hela bilden. Därför
# beskrivs den i geometriska termer i stället för att nämnas vid namn.
CONTROL = """Hanging at chest height is an orienteering control marker: a flat square
panel about 20 cm across, mounted so it faces the camera, divided corner to corner by
a single diagonal running from its top-right corner to its bottom-left corner. The
triangle above that diagonal is plain white; the triangle below it is solid bright
safety orange. There is no other pattern, no lettering and no logo on it. Beside it,
on a small bracket, sits a compact electronic punching unit the size of a matchbox."""

STREAMER = """Orange-and-white striped plastic streamer tape is tied along the route
at waist height, running away from the marker and marking the way onward."""

CLEAN = """Photorealistic, shot on a full-frame camera at eye level, natural interior
lighting. No people in the frame. Do not render any text, letters, numbers, signage,
posters, logos or labels anywhere in the image."""

SCENES = {
    "korridor": """Interior of an ordinary Swedish secondary-school corridor on a
weekday evening. A long run of metal lockers down one wall, closed classroom doors
along the other, worn linoleum flooring, fluorescent ceiling panels, and daylight
falling in from a window at the far end. The control marker hangs from the corner of
a locker bank where the corridor turns.""",

    "klassrum": """Interior of an ordinary Swedish classroom, desks pushed to the
sides to clear the floor, chairs stacked, a whiteboard on the end wall and tall
windows along one side letting in low evening light. The control marker hangs from
the back of a stacked chair near the middle of the room.""",

    "trapphus": """Interior of a school stairwell: a concrete stair with a painted
steel handrail turning around a half-landing, a tall window throwing light across the
steps, plain painted walls. The control marker hangs from the handrail at the landing.""",
}


def prompt(scene):
    return f"{SCENES[scene]}\n\n{CONTROL}\n\n{STREAMER}\n\n{CLEAN}"
