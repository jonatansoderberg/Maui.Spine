"""Measures how far round each Mini's body its eyes may turn with both eyes whole inside the outline: the
figures' `reach` in mini_avatars.py, which the shader eases the look to a stop at.

Usage: python mini_eyereach.py
Prints reach=(normal, enlarged) per figure: the outer eye's turn in radians at normal eye size and at 1.3x
(listening, surprised, leaning in), talking eyes included; and the outline's foot (FEET in mini_avatars.py).
SHAPES mirrors the SkSL outlines in mini_avatars.py; keep the two in step.
"""
import math, os, sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mini_avatars import FIGURES


def length(x, y): return np.sqrt(x * x + y * y)
def ellipse(x, y, rx, ry): return (length(x / rx, y / ry) - 1.0) * np.minimum(rx, ry)
def round_box(x, y, bx, by, r):
    dx, dy = np.abs(x) - bx + r, np.abs(y) - by + r
    return length(np.maximum(dx, 0), np.maximum(dy, 0)) + np.minimum(np.maximum(dx, dy), 0) - r
def smax(a, b, k):
    h = np.clip(0.5 - 0.5 * (b - a) / k, 0, 1)
    return b + (a - b) * h + k * h * (1 - h)

SHAPES = {
    "round": lambda x, y: length(x, y) - 0.72,
    "squircle": lambda x, y: round_box(x, y, 0.66, 0.62, 0.32),
    "egg": lambda x, y: ellipse(x, y, 0.6 * (1 - 0.18 * np.clip(y / 0.78, -1, 1)), 0.78),
    "dome": lambda x, y: smax(ellipse(x, y + 0.5, 0.76, 1.06), -0.42 - y, 0.12),
    "pill": lambda x, y: round_box(x, y, 0.8, 0.5, 0.42),
    "oval": lambda x, y: ellipse(x, y, 0.6 * (1 + 0.1 * np.clip(y / 0.78, -1, 1)), 0.78),
    "tin": lambda x, y: (lambda t: round_box(x * (1 + 0.06 * y), y - np.where(y > 0, 0.1, 0.04) * (1 - t * t), 0.66, 0.6, 0.14))(np.clip(x / 0.68, -1, 1)),
    "plump": lambda x, y: ellipse(x, y, 0.76 * (1 - 0.06 * np.clip(y / 0.68, -1, 1)), 0.68),
}

# The shader cuts eyes 0.012 inside the outline, and the outline wobbles by up to about 0.035 while thinking.
MARGIN = 0.05
TALL = 1.6
ANGLES = np.linspace(0, 2 * math.pi, 96)


def eye_outline(fig, lon, lat, talk, scale):
    """The eye's bounding ellipse as eyeDistance() places and sizes it (it bounds the pill eyes too)."""
    turn = max(-1.57, min(1.57, lon))
    cx = fig["mapX"] * math.sin(turn)
    cy = fig["eyeY"] + 0.9 * 0.3 * math.sin(lat) + 0.04 * talk
    w = fig["w"] * scale * (0.22 + 0.78 * math.cos(turn) ** 0.7)
    h = fig["h"] * scale * (1 + (TALL - 1) * talk) * (0.75 + 0.25 * math.cos(lat))
    return cx + w * np.cos(ANGLES), cy + h * np.sin(ANGLES)


def fits(fig, reach, scale):
    shape = SHAPES[fig["shape"]]
    for talk in (0, 1):
        # Speaking faces the viewer, so a talking eye is checked over a narrower range of looks up and down.
        for lat in np.linspace(-0.35, 0.35, 5) if talk else np.linspace(-0.7, 0.6, 9):
            for lon in (reach, reach - 2 * fig["sep"], -reach, -reach + 2 * fig["sep"]):
                x, y = eye_outline(fig, lon, lat, talk, scale)
                if shape(x, y).max() > -MARGIN:
                    return False
    return True


def reach(fig, scale):
    return next((r / 100 for r in range(157, 0, -1) if fits(fig, r / 100, scale)), 0.0)


def foot(shape):
    """The outline's lowest point below its centre: FEET in mini_avatars.py."""
    ys = np.linspace(-1, 0, 2001)
    x, y = np.meshgrid(np.linspace(-1, 1, 801), ys)
    return -ys[(SHAPES[shape](x, y) < 0).any(axis=1)].min()


if __name__ == "__main__":
    for fig in FIGURES:
        print(f"{fig['id']:7} reach=({reach(fig, 1.0):.2f}, {reach(fig, 1.3):.2f})  foot {foot(fig['shape']):.3f}")
