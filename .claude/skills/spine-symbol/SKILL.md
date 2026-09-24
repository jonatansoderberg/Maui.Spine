---
name: spine-symbol
description: Draw new symbols for Plugin.Maui.Spine.Svg.Icons from a name or a description of what they are for. Drafts a few alternatives in the set's exact SVG format, renders them for review, lets the user pick, then installs the pick, regenerates SpineIcons and updates the icon count. Invoke as /spine-symbol <name or description>.
---

You are drawing symbols for the icon set in `src/Plugin.Maui.Spine.Svg.Icons/Images/`. The user names a symbol ("Wallet") or describes what it is for ("something for the offline banner"). You draw a few alternatives, show them, and install only the one they pick.

`$ARGUMENTS` holds the request. It can name several symbols; draft each of them.

All tooling is in `.claude/skills/spine-symbol/symbols.py`, relative to the repo root. Write drafts and previews to the session's scratchpad, never into the repo.

---

## Step 1 — Check what exists

```bash
ls src/Plugin.Maui.Spine.Svg.Icons/Images
```

If something close already exists (`Bell` for "notification", `Clock` for "time"), say so and ask whether a new symbol is still wanted, or a variant of the existing one (`BellBadge` builds on `Bell`).

Read two or three existing icons that are similar in kind, so the new ones match their proportions. A round symbol should match `Clock`/`Info`, a framed one `Image`/`Mail`, a line glyph `Menu`/`ChevronRight`.

## Step 2 — Draft 2–4 alternatives

Write a draft file, e.g. `<scratchpad>/symbols/draft.py`:

```python
from symbols import *

@icon("Wallet@a")          # base name @ variant letter
def _():
    return [S(rrect(6.25, 12.5, 43.75, 40.63, 3.13)), S(line((31.25, 21.88), (43.75, 21.88))), dot(35.94, 26.56)]

@icon("Wallet@b")
def _():
    ...
```

Helpers (coordinates are viewBox units, 0–50):
- `S(path, w=2)` is a stroked part. Use `w=1` only for secondary detail. `F(path)` is a filled part, `dot(cx, cy, r=1.56)` a filled dot and `sq(x0, y0, x1, y1)` a filled square.
- `line(*points, closed=False, tf=None)`, `circle(cx, cy, r)`, `rrect(x0, y0, x1, y1, r)`.
- `P()` is a free path: `.M .L .C .Z`, plus `.arc(cx, cy, r, a0, a1)` with degrees in SVG orientation (0 = right, 90 = down), emitted as cubics.
- `rot(cx, cy, deg)` gives a transform to pass as `tf=`, for rotated shapes such as the links in `Link`.
- `meet(c1, r1, c2, r2)` and `angle(c, p)` build outlines from overlapping circles, as in `Cloud`.
- `existing("Bell")` returns an existing icon's parts, to add a badge or a slash on top.
- `g(steps)` is a coordinate on the grid (`g(4)` = 6.25).

Make the alternatives genuinely different (outline vs. filled detail, a different metaphor, a different composition), not the same drawing with slightly moved points.

### The style

These rules are measured from the set; follow them exactly:
- The viewBox is 50×50 and contains only `<path>` elements in one `<g>`, using M/L/C/Z. The script writes the attribute format; never hand-edit it.
- Put points on the grid of 50/32 = 1.5625: 1.56, 3.13, 4.69, 6.25, 7.81, 9.38, 10.94, 12.5, 14.06, 15.63, 17.19, 18.75, 20.31, 21.88, 23.44, 25 and mirrored. Content stays within about 4.69–45.31, which leaves 5 points of margin.
- Stroke 2 for the glyph and 1 for secondary detail. Filled parts are only dots, modules, levels and badges. There is no `stroke-linejoin` or `stroke-linecap`; the set uses the defaults.
- Sizes to reuse:
  - A full circle is centred at (25, 25) with r 20.31 (`Clock`, `Info`, `Globe`).
  - Frames have corner radius 3.13, and small squares 1.56.
  - Dots are r 1.56 for detail and 2.34 for grips and bullets.
  - Chevrons span 12.5 × 25 centred on 25.
  - Full-width lines run 7.81–42.19.
- Keep the glyph single-colour, because Spine tints the whole thing. One concept per icon, and it has to read at 24 points.
- Name files after the symbol in PascalCase with no dots (`.run` would become a culture suffix). Numbers are fine: `Battery25`.
- Use existing icons and sites such as phosphoricons.com only as inspiration for the idea. Draw the shape yourself in this style; never copy path data.

## Step 3 — Render and check it yourself

```bash
python3 .claude/skills/spine-symbol/symbols.py render <scratchpad>/symbols/draft.py <scratchpad>/symbols/out --compare Clock,Mail,Menu
```

This writes one SVG per draft, `sheet.png` (a contact sheet with the compared existing icons first) and `gallery.html` (each glyph at 90 px on the grid and at 28 and 24 pt, in light and dark).

**Read `sheet.png` before showing anything.** It is rendered with Quick Look, because the browser pane cannot screenshot local files. Look for:
- parts that overlap or touch where they should not (QR modules, letters),
- ends that leave a gap against a frame (the V in `Mail` has to start in the frame's corner),
- glyphs off centre, or visibly larger or smaller than the compared icons,
- detail that clogs up at 24 pt.

Fix the draft and render again until it holds up.

## Step 4 — Show the alternatives

Send `gallery.html` with SendUserFile (`display: "render"`). You can send `sheet.png` alongside it for a quick overview. In the chat, list the alternatives with their letters and one line each on what makes them different. Ask which one to use. Also ask about the name if it is not obvious.

The user can pick a letter, ask for changes ("A, but the gap in the corners"), ask for more variants, or ask to keep several under their own names ("B = Stack"). Iterate from Step 2 until they have picked.

## Step 5 — Install the pick

```bash
python3 .claude/skills/spine-symbol/symbols.py install <scratchpad>/symbols/out/Wallet@b.svg Wallet
```

This copies the file to `Images/Wallet.svg` and regenerates the constants in `SpineIcons.cs` (ordinal order). It also updates the icon count in README.md, the wiki, the package README, the `spine-controls`/`spine-setup` skills and the SVG sample.

- Add `--framework` when Spine itself draws the symbol. This also copies the file in lower case to `src/Plugin.Maui.Spine/Resources/Svg/`, where the back button's `chevronleft.svg` and the sheet's `close.svg` live, so it resolves without the icon package.
- When a new symbol **replaces** an existing one, delete the old file and point every use at the new name. Search for uses first:

  ```bash
  grep -rni "<old>.svg\|SpineIcons.<Old>" --exclude-dir=bin --exclude-dir=obj src samples docs .claude
  ```

  Then run `install` again (or edit `SpineIcons.cs`), so that the constants and the count match the files.
- If the icon list in the package README or `docs/wiki/svg.md` names the kinds of glyph, add the new kind when it is a new category.

Build the package to confirm it still embeds everything, using the SDK with the MAUI workloads:

```bash
/usr/local/share/dotnet/dotnet build src/Plugin.Maui.Spine.Svg.Icons/Plugin.Maui.Spine.Svg.Icons.csproj -v q
```

When this runs as part of an issue, record the new symbols and any replaced ones in the issue's changelog (`issues/<id>-<slug>.md`). Stage explicit paths when committing, never `git add -A`.
