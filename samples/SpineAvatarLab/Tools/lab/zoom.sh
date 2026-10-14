#!/bin/zsh
# Renders an avatar at 640 pt after each step and stacks crops of one region, for close inspection.
# Usage: REGION=x0,y0,x1,y1 zoom.sh <avatar-id> <out.png> "cmd;cmd" "cmd" …
#   REGION as fractions of the view (default: the eyes); each argument is one step's harness commands.
LAB_DIR=${LAB_DIR:-/tmp/spine-avatar-lab}
id=$1; out=$2; shift 2
"$(dirname "$0")/start.sh" >/dev/null
printf "renderer ${RENDERER:-native}\nfollow off\ntheme light\nsize 640\nload $id\n" >> $LAB_DIR/cmds; sleep 7
printf "lookat 320 320\n" >> $LAB_DIR/cmds; sleep 1
i=0
for spec in "$@"; do
  for c in ${(s:;:)spec}; do printf "$c\n" >> $LAB_DIR/cmds; done
  sleep ${WAIT:-1.5}; printf "shotview z$i\n" >> $LAB_DIR/cmds; sleep 0.8; i=$((i+1))
done
sleep 1
python3 - "$LAB_DIR/out" "$out" "$#" "${REGION:-0.28,0.36,0.72,0.52}" <<'PY'
import base64, io, sys
from PIL import Image
outfile, dest, n, reg = sys.argv[1], sys.argv[2], int(sys.argv[3]), [float(v) for v in sys.argv[4].split(',')]
d = {l.split()[1]: l.split()[2] for l in open(outfile, errors='ignore').read().replace('\0', '').splitlines() if l.startswith('png ')}
tiles = []
for i in range(n):
    if f'z{i}' not in d: continue
    im = Image.open(io.BytesIO(base64.b64decode(d[f'z{i}']))).convert('RGBA'); bg = Image.new('RGBA', im.size, (240, 242, 246, 255)); bg.alpha_composite(im)
    w, h = bg.size; tiles.append(bg.convert('RGB').crop((int(w * reg[0]), int(h * reg[1]), int(w * reg[2]), int(h * reg[3]))))
tw = 600; th = int(tiles[0].height * tw / tiles[0].width)
o = Image.new('RGB', (tw, th * len(tiles)), 'white')
for i, t in enumerate(tiles): o.paste(t.resize((tw, th)), (0, i * th))
o.save(dest); print(dest)
PY
