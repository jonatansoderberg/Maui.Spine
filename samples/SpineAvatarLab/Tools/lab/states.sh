#!/bin/zsh
# Renders all seven states of an avatar in light and dark next to the concept sheet's cells and writes
# a comparison sheet. The concept sheet must be a 7-column grid (idle … muted), light row then dark row.
# Usage: states.sh <avatar-id> <concept.png> <out.png> [renderer] ["x0,x1;…" "ly0,ly1;dy0,dy1"]
#   the last two describe the cells in the concept image (column x ranges, light and dark row y ranges)
LAB_DIR=${LAB_DIR:-/tmp/spine-avatar-lab}
id=$1; concept=$2; out=$3; r=${4:-native}
cols=${5:-"66,178;186,304;312,430;438,560;568,690;700,822;830,952"}; rows=${6:-"64,232;266,438"}
"$(dirname "$0")/start.sh" >/dev/null
printf "renderer $r\nfollow off\nlookat 160 160\n" >> $LAB_DIR/cmds
for theme in light dark; do
  printf "theme $theme\nload $id\n" >> $LAB_DIR/cmds; sleep 5
  for st in idle connecting listening thinking speaking interrupted muted; do
    [[ $st == muted ]] && printf "muted on\n" >> $LAB_DIR/cmds
    printf "state $st\n" >> $LAB_DIR/cmds; if [[ $st == interrupted ]]; then sleep 0.25; else sleep 1.4; fi
    [[ $st == connecting ]] && sleep 0.5
    printf "shotview $theme-$st\n" >> $LAB_DIR/cmds; sleep 0.5
    [[ $st == muted ]] && printf "muted off\n" >> $LAB_DIR/cmds
  done
  printf "state idle\n" >> $LAB_DIR/cmds
done
sleep 1.5
python3 - "$LAB_DIR/out" "$concept" "$out" "$cols" "$rows" <<'PY'
import base64, io, sys
from PIL import Image
outfile, concept, dest, cols, rows = sys.argv[1:6]
d = {l.split()[1]: l.split()[2] for l in open(outfile, errors='ignore').read().replace('\0', '').splitlines() if l.startswith('png ')}
xs = [tuple(map(int, c.split(','))) for c in cols.split(';')]; ys = [tuple(map(int, c.split(','))) for c in rows.split(';')]
states = "idle connecting listening thinking speaking interrupted muted".split()
sheet = Image.open(concept).convert('RGB'); cw = 200
o = Image.new('RGB', (7 * cw, 4 * cw), 'white')
for r, theme in enumerate(["light", "dark"]):
    bg = (247, 248, 250) if theme == "light" else (14, 18, 28)
    for i, st in enumerate(states):
        o.paste(sheet.crop((xs[i][0], ys[r][0], xs[i][1], ys[r][1])).resize((cw, cw)), (i * cw, r * 2 * cw))
        cell = Image.new('RGBA', (cw, cw), bg + (255,))
        if f"{theme}-{st}" in d:
            cell.alpha_composite(Image.open(io.BytesIO(base64.b64decode(d[f"{theme}-{st}"]))).convert('RGBA').resize((cw, cw)))
        o.paste(cell.convert('RGB'), (i * cw, (r * 2 + 1) * cw))
o.save(dest); print(dest)
PY
