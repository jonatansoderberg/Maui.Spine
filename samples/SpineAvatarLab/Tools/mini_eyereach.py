"""Measures the Minis' outlines: how far round the body the eyes may turn with both eyes whole inside the
outline (a face's `reach`, which the shader eases the look to a stop at), how low the foot is, and how far
the hand-drawn outline strays from its geometric shape.

Usage: python mini_eyereach.py    (prints the table mini_avatars.py computes when it generates the package)
The Python outlines in mini_avatars.py mirror its SkSL ones; keep the two in step.
"""
import math
import numpy as np


def length(x, y): return np.sqrt(x * x + y * y)
def ellipse(x, y, rx, ry): return (length(x / rx, y / ry) - 1.0) * np.minimum(rx, ry)
def round_box(x, y, bx, by, r):
    dx, dy = np.abs(x) - bx + r, np.abs(y) - by + r
    return length(np.maximum(dx, 0), np.maximum(dy, 0)) + np.minimum(np.maximum(dx, dy), 0) - r
def round_box4(x, y, bx, by, r):
    """r: top right, bottom right, top left, bottom left, as roundBox4 in the shader."""
    k = np.where(x > 0, np.where(y > 0, r[0], r[1]), np.where(y > 0, r[2], r[3]))
    dx, dy = np.abs(x) - bx + k, np.abs(y) - by + k
    return length(np.maximum(dx, 0), np.maximum(dy, 0)) + np.minimum(np.maximum(dx, dy), 0) - k
def smax(a, b, k):
    h = np.clip(0.5 - 0.5 * (b - a) / k, 0, 1)
    return b + (a - b) * h + k * h * (1 - h)
def drawn(x, y, d):
    """The hand-drawn deviation, as drawn() in the shader: d = dict(amp, phase, fuller)."""
    a = np.arctan2(y, x)
    return sum(amp * np.sin(n * a + ph) for n, amp, ph in zip((2, 3, 4, 5), d["amp"], d["phase"])) + d["fuller"] * x


# The shader cuts eyes 0.012 inside the outline, and the outline wobbles by up to about 0.035 while thinking.
MARGIN = 0.05
EYE_FAR = 0.65
ANGLES = np.linspace(0, 2 * math.pi, 96)


def eye_outline(face, size, lon, lat, talk, scale):
    """The eye's bounding ellipse as eyeDistance() places and sizes it (it bounds the tall eyes too)."""
    turn = max(-1.57, min(1.57, lon))
    scale *= 1 + 0.03 * talk
    cx = face["mapX"] * math.sin(turn)
    cy = face["eyeY"] + 0.9 * 0.3 * math.sin(lat) + 0.065 * talk
    w = size[0] * scale * (EYE_FAR + (1 - EYE_FAR) * math.cos(turn) ** 0.7)
    h = size[1] * scale * (1 + 0.08 * talk) * (0.75 + 0.25 * math.cos(lat))
    return cx + w * np.cos(ANGLES), cy + h * np.sin(ANGLES)


def fits(sdf, face, size, reach, scale):
    for talk in (0, 1):
        # Speaking faces the viewer, so a talking eye is checked over a narrower range of looks up and down.
        for lat in np.linspace(-0.35, 0.35, 5) if talk else np.linspace(-0.7, 0.6, 9):
            for lon in (reach, reach - 2 * face["sep"], -reach, -reach + 2 * face["sep"]):
                x, y = eye_outline(face, size, lon, lat, talk, scale)
                if sdf(x, y).max() > -MARGIN:
                    return False
    return True


def reach(sdf, face, size, scale):
    """The outer eye's furthest turn in radians, in hundredths; 0 when even a straight look does not fit."""
    return next((r / 100 for r in range(157, 0, -1) if fits(sdf, face, size, r / 100, scale)), 0.0)


def foot(sdf):
    """The outline's lowest point below its centre."""
    ys = np.linspace(-1, 0, 2001)
    x, y = np.meshgrid(np.linspace(-1, 1, 801), ys)
    return float(-ys[(sdf(x, y) < 0).any(axis=1)].min())


def deviation(d, radius=0.7):
    """The largest the hand-drawn deviation gets round an outline of about this radius."""
    return float(np.abs(drawn(radius * np.cos(ANGLES), radius * np.sin(ANGLES), d)).max())


if __name__ == "__main__":
    import os, sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import mini_avatars
    for row in mini_avatars.measure():
        print(row)
