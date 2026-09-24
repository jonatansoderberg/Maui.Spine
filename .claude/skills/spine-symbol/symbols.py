"""Draw, preview and install symbols for Plugin.Maui.Spine.Svg.Icons.

    python3 symbols.py render <draft.py> <out-dir> [--compare Bell,Clock]
    python3 symbols.py install <out-dir>/<Name@x>.svg <Name> [--framework]
    python3 symbols.py sheet <svg-dir-or-files...> <png>

A draft is a Python file that does `from symbols import *` and registers glyphs with @icon("Name@a").
Everything emitted uses only what the existing set uses: <path> elements in one <g>, M/L/C/Z,
the same attribute order, two decimals.
"""
import math
import os
import re
import runpy
import shutil
import subprocess
import sys
from decimal import Decimal, ROUND_HALF_UP

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
IMAGES = os.path.join(REPO, "src", "Plugin.Maui.Spine.Svg.Icons", "Images")
CONSTANTS = os.path.join(REPO, "src", "Plugin.Maui.Spine.Svg.Icons", "SpineIcons.cs")
FRAMEWORK_SVG = os.path.join(REPO, "src", "Plugin.Maui.Spine", "Resources", "Svg")
COUNT_FILES = [
    "README.md",
    "docs/wiki/svg.md",
    "docs/wiki/packages.md",
    "src/Plugin.Maui.Spine.Svg.Icons/README.md",
    ".claude/skills/spine-controls/SKILL.md",
    ".claude/skills/spine-setup/SKILL.md",
    "samples/MauiSpineSampleApp/Pages/SvgIcons/SvgIconsPage.ViewModel.cs",
]
COUNT_PATTERNS = [r"(\d+)( ready-made)", r"(\d+)( embedded SVG icons)", r"\((\d+)( SVG glyphs)", r"(the set has )(\d+)"]

K = 0.5522847498
G = 50 / 32  # the set's grid step: 1.56, 3.13, 4.69, 6.25, 7.81, 9.38, 10.94, 12.5 ...


def n(v):
    d = Decimal(repr(float(v))).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
    s = format(d.normalize(), "f")
    return "0" if s in ("-0", "-0.00") else s


def g(steps):
    """A coordinate on the set's grid, in steps of 50/32."""
    return steps * G


class P:
    """Path builder. arc() takes degrees in SVG orientation (0 = right, 90 = down) and emits cubics."""

    def __init__(self, tf=None):
        self.s, self.cur, self.tf = "", None, tf or (lambda p: p)

    def _pt(self, x, y):
        p = self.tf((x, y))
        return f"{n(p[0])} {n(p[1])}"

    def M(self, x, y):
        self.s += "M" + self._pt(x, y)
        self.cur = (x, y)
        return self

    def L(self, x, y):
        self.s += "L" + self._pt(x, y)
        self.cur = (x, y)
        return self

    def C(self, x1, y1, x2, y2, x, y):
        self.s += "C" + " ".join((self._pt(x1, y1), self._pt(x2, y2), self._pt(x, y)))
        self.cur = (x, y)
        return self

    def arc(self, cx, cy, r, a0, a1, move=False):
        segs = max(1, math.ceil(abs(a1 - a0) / 90 - 1e-9))
        da = (a1 - a0) / segs
        p0 = (cx + r * math.cos(math.radians(a0)), cy + r * math.sin(math.radians(a0)))
        if move:
            self.M(*p0)
        elif self.cur is None or math.dist(self.cur, p0) > 0.005:
            self.L(*p0)
        for i in range(segs):
            t0, t1 = math.radians(a0 + i * da), math.radians(a0 + (i + 1) * da)
            k = 4 / 3 * math.tan((t1 - t0) / 4) * r
            x0, y0 = cx + r * math.cos(t0), cy + r * math.sin(t0)
            x3, y3 = cx + r * math.cos(t1), cy + r * math.sin(t1)
            self.C(x0 - k * math.sin(t0), y0 + k * math.cos(t0), x3 + k * math.sin(t1), y3 - k * math.cos(t1), x3, y3)
        return self

    def Z(self):
        self.s += "Z"
        return self


def line(*pts, closed=False, tf=None):
    p = P(tf).M(*pts[0])
    for q in pts[1:]:
        p.L(*q)
    return p.Z() if closed else p


def circle(cx, cy, r, tf=None):
    """Four quarter arcs from the right, closed: the shape every circle in the set has."""
    p = P(tf).M(cx + r, cy)
    p.C(cx + r, cy + K * r, cx + K * r, cy + r, cx, cy + r)
    p.C(cx - K * r, cy + r, cx - r, cy + K * r, cx - r, cy)
    p.C(cx - r, cy - K * r, cx - K * r, cy - r, cx, cy - r)
    p.C(cx + K * r, cy - r, cx + r, cy - K * r, cx + r, cy)
    return p.Z()


def rrect(x0, y0, x1, y1, r, tf=None):
    p = P(tf).M(x0 + r, y0)
    p.L(x1 - r, y0).arc(x1 - r, y0 + r, r, -90, 0)
    p.L(x1, y1 - r).arc(x1 - r, y1 - r, r, 0, 90)
    p.L(x0 + r, y1).arc(x0 + r, y1 - r, r, 90, 180)
    p.L(x0, y0 + r).arc(x0 + r, y0 + r, r, 180, 270)
    return p.Z()


def rot(cx, cy, deg):
    """A transform for P(tf=...): rotate points about (cx, cy)."""
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return lambda p: (cx + (p[0] - cx) * c - (p[1] - cy) * s, cy + (p[0] - cx) * s + (p[1] - cy) * c)


def meet(c1, r1, c2, r2, upper=True):
    """Where two circles cross (the upper or lower point), for outlines built from lobes."""
    d = math.dist(c1, c2)
    a = (r1 * r1 - r2 * r2 + d * d) / (2 * d)
    h = math.sqrt(r1 * r1 - a * a)
    mx, my = c1[0] + a * (c2[0] - c1[0]) / d, c1[1] + a * (c2[1] - c1[1]) / d
    ox, oy = -h * (c2[1] - c1[1]) / d, h * (c2[0] - c1[0]) / d
    pts = ((mx + ox, my + oy), (mx - ox, my - oy))
    return min(pts, key=lambda t: t[1]) if upper else max(pts, key=lambda t: t[1])


def angle(c, p):
    return math.degrees(math.atan2(p[1] - c[1], p[0] - c[0]))


def S(p, w=2):
    """A stroked part. 2 for the glyph, 1 for secondary detail."""
    return (p.s if isinstance(p, P) else p, "stroke", w)


def F(p):
    """A filled part: dots, modules, levels, badges."""
    return (p.s if isinstance(p, P) else p, "fill", 0)


def dot(cx, cy, r=1.56):
    return F(circle(cx, cy, r))


def sq(x0, y0, x1, y1):
    return F(line((x0, y0), (x1, y0), (x1, y1), (x0, y1), closed=True))


def existing(name):
    """The parts of an existing icon, to build on it (a badge on Bell, a slash through Eye)."""
    text = open(os.path.join(IMAGES, name + ".svg")).read()
    parts = []
    for d, rest in re.findall(r'<path d="([^"]*)"\s+([^>]*)>', text):
        if 'fill="#000000"' in rest:
            parts.append((d, "fill", 0))
        elif d:
            parts.append((d, "stroke", float(re.search(r'stroke-width="([0-9.]+)"', rest).group(1))))
    return parts


def svg(parts):
    out = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 50 50" version="1.1">', "<g>"]
    for d, kind, w in parts:
        if kind == "stroke":
            out.append(f'<path d="{d}"  stroke="#000000" stroke-width="{n(w)}" stroke-opacity="1" fill-opacity="0"></path>')
        else:
            out.append(f'<path d="{d}"  stroke="#000000" stroke-width="0" stroke-opacity="1" fill="#000000" fill-opacity="1"></path>')
    return "\n".join(out + ["</g>", "</svg>", ""])


ICONS = {}


def icon(name):
    def reg(f):
        ICONS[name] = f
        return f
    return reg


# ------------------------------------------------------------------ previews

def sheet(paths, png, cols=6):
    """A contact sheet rendered by Quick Look; the only renderer on this Mac that needs nothing installed."""
    cell = 70
    rows = (len(paths) + cols - 1) // cols
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{cols * cell * 2}" height="{rows * cell * 2}" '
           f'viewBox="0 0 {cols * cell} {rows * cell}"><rect width="100%" height="100%" fill="#fff"/>']
    for i, path in enumerate(paths):
        x, y = (i % cols) * cell, (i // cols) * cell
        body = re.search(r"<g>(.*)</g>", open(path).read(), re.S).group(1)
        label = os.path.basename(path)[:-4]
        out.append(f'<rect x="{x + 10}" y="{y + 2}" width="50" height="50" fill="#eef"/>'
                   f'<g transform="translate({x + 10},{y + 2})">{body}</g>'
                   f'<text x="{x + 35}" y="{y + 64}" font-size="7" text-anchor="middle" font-family="Helvetica">{label}</text>')
    out.append("</svg>")
    src = os.path.splitext(png)[0] + ".svg"
    open(src, "w").write("\n".join(out))
    size = max(cols, rows) * cell * 2
    subprocess.run(["qlmanage", "-t", "-s", str(size), "-o", os.path.dirname(png) or ".", src], capture_output=True)
    rendered = src + ".png"
    if os.path.exists(rendered):
        os.replace(rendered, png)
    else:
        sys.exit(f"Quick Look did not render {src}")


def gallery(out_dir, names, compare):
    import html

    def inline(path):
        return open(path).read().replace("#000000", "currentColor").replace("<svg ", '<svg class="i" ', 1)

    def tile(label, path, note=""):
        s = inline(path)
        return (f'<figure><div class="big">{s}</div><div class="small"><span style="--s:28px">{s}</span>'
                f'<span style="--s:24px">{s}</span></div><figcaption><b>{html.escape(label)}</b>'
                f'{f"<small>{html.escape(note)}</small>" if note else ""}</figcaption></figure>')

    groups = {}
    for name in names:
        base, _, variant = name.partition("@")
        groups.setdefault(base, []).append((variant, name))
    body = []
    if compare:
        body.append('<section><h2>Existing icons, for scale</h2><div class="row">')
        body += [tile(c, os.path.join(IMAGES, c + ".svg")) for c in compare]
        body.append("</div></section>")
    for base, variants in groups.items():
        body.append(f'<section><h2>{html.escape(base)}</h2><div class="row">')
        body += [tile(v.upper() or base, os.path.join(out_dir, full + ".svg"), full) for v, full in variants]
        body.append("</div></section>")
    page = f'''<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Spine Symbol Drafts</title>
<style>
:root {{ --bg:#f6f6f4; --card:#fff; --fg:#1c1c1e; --muted:#6b6b70; --line:#e2e2de; --grid:#eef0f7; }}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg:#141416; --card:#1f1f22; --fg:#f2f2f4; --muted:#9a9aa0; --line:#2e2e33; --grid:#26272e; }} }}
:root[data-theme="dark"] {{ --bg:#141416; --card:#1f1f22; --fg:#f2f2f4; --muted:#9a9aa0; --line:#2e2e33; --grid:#26272e; }}
body {{ margin:0; background:var(--bg); color:var(--fg); font:15px/1.45 -apple-system, system-ui, sans-serif; }}
main {{ max-width:1100px; margin:0 auto; padding:24px 16px 64px; }}
h1 {{ font-size:22px; margin:0 0 4px; }} h2 {{ font-size:16px; margin:28px 0 10px; }}
.lead {{ color:var(--muted); margin:0; }}
.row {{ display:flex; flex-wrap:wrap; gap:10px; }}
figure {{ margin:0; width:112px; background:var(--card); border:1px solid var(--line); border-radius:10px; padding:10px; box-sizing:border-box; }}
.big {{ width:90px; height:90px; background-image:linear-gradient(var(--grid) 1px, transparent 1px),linear-gradient(90deg,var(--grid) 1px, transparent 1px); background-size:11.25px 11.25px; }}
.big .i {{ width:90px; height:90px; display:block; }}
.small {{ display:flex; gap:10px; align-items:end; margin-top:8px; height:28px; }}
.small span {{ width:var(--s); height:var(--s); }} .small .i {{ width:100%; height:100%; display:block; }}
figcaption {{ margin-top:6px; font-size:12px; }} figcaption b {{ font-weight:600; display:block; }} figcaption small {{ color:var(--muted); overflow-wrap:anywhere; }}
.toggle {{ float:right; font:inherit; background:var(--card); color:var(--fg); border:1px solid var(--line); border-radius:8px; padding:4px 10px; }}
</style></head><body><main>
<button class="toggle" onclick="const r=document.documentElement;r.dataset.theme=r.dataset.theme==='dark'?'light':'dark'">Light / dark</button>
<h1>Symbol drafts</h1>
<p class="lead">Each tile: the glyph at 90 px on the grid (one square = 4 steps of 1.56), then at 28 and 24 points. Answer with the letter.</p>
{"".join(body)}
</main></body></html>'''
    path = os.path.join(out_dir, "gallery.html")
    open(path, "w").write(page)
    return path


# ------------------------------------------------------------------ commands

def render(draft, out_dir, compare):
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import symbols as lib  # the draft registers into the imported module, not __main__
    runpy.run_path(draft, run_name="__draft__")
    icons = lib.ICONS
    os.makedirs(out_dir, exist_ok=True)
    for f in os.listdir(out_dir):
        if f.endswith(".svg"):
            os.remove(os.path.join(out_dir, f))
    for name, f in icons.items():
        open(os.path.join(out_dir, name + ".svg"), "w").write(svg(f()))
    names = list(icons)
    paths = [os.path.join(IMAGES, c + ".svg") for c in compare] + [os.path.join(out_dir, x + ".svg") for x in names]
    sheet(paths, os.path.join(out_dir, "sheet.png"))
    print(f"{len(names)} drafts")
    print("sheet:", os.path.join(out_dir, "sheet.png"))
    print("gallery:", gallery(out_dir, names, compare))


def regenerate_constants():
    names = sorted(f[:-4] for f in os.listdir(IMAGES) if f.endswith(".svg"))
    s = open(CONSTANTS).read()
    start, end = s.index("{\n", s.index("class SpineIcons")) + 2, s.rindex("}")
    consts = "".join(f'    /// <summary>The <c>{x}.svg</c> icon.</summary>\n    public const string {x} = "{x}.svg";\n' for x in names)
    every = "".join(f"        {x},\n" for x in names)
    s = s[:start] + consts + "\n    /// <summary>Every icon in the package, in ordinal order.</summary>\n    public static IReadOnlyList<string> All { get; } =\n    [\n" + every + "    ];\n" + s[end:]
    open(CONSTANTS, "w").write(s)
    return len(names)


def update_counts(count):
    changed = []
    for rel in COUNT_FILES:
        path = os.path.join(REPO, rel)
        if not os.path.exists(path):
            continue
        s = t = open(path).read()
        for pat in COUNT_PATTERNS:
            t = re.sub(pat, lambda m: m.group(0).replace(next(x for x in m.groups() if x.isdigit()), str(count)), t)
        if t != s:
            open(path, "w").write(t)
            changed.append(rel)
    return changed


def install(src, name, framework):
    if "." in name or not re.fullmatch(r"[A-Z][A-Za-z0-9]*", name):
        sys.exit(f"{name}: use a PascalCase name without dots (a dot becomes a culture suffix)")
    target = os.path.join(IMAGES, name + ".svg")
    replaced = os.path.exists(target)
    if not (replaced and os.path.samefile(src, target)):
        shutil.copyfile(src, target)
    if framework:
        shutil.copyfile(src, os.path.join(FRAMEWORK_SVG, name.lower() + ".svg"))
    count = regenerate_constants()
    changed = update_counts(count)
    print(f"{'replaced' if replaced else 'added'} {os.path.relpath(target, REPO)}; the set has {count}")
    if framework:
        print("also", os.path.relpath(os.path.join(FRAMEWORK_SVG, name.lower() + ".svg"), REPO))
    print("count updated in:", ", ".join(changed) or "nothing (check the list in COUNT_FILES)")


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        sys.exit(__doc__)
    cmd = args.pop(0)
    compare = []
    if "--compare" in args:
        i = args.index("--compare")
        compare = [c for c in args[i + 1].split(",") if c]
        del args[i:i + 2]
    if cmd == "render":
        render(args[0], args[1], compare)
    elif cmd == "install":
        install(args[0], args[1], "--framework" in args)
    elif cmd == "sheet":
        *inputs, png = args
        files = [os.path.join(i, f) for i in inputs if os.path.isdir(i) for f in sorted(os.listdir(i)) if f.endswith(".svg")]
        files += [i for i in inputs if i.endswith(".svg")]
        sheet(files, png)
    else:
        sys.exit(__doc__)
