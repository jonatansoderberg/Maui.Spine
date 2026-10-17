#!/bin/zsh
# lab-face.sh OUTDIR PREFIX : with a package already loaded in the running lab (after lab-cards.sh), captures the face poses
# open / half blink / blink at yaw 0, visemes aa(10) O(13) PP(1) FF(2) E(11) at yaw 0, and blink at yaw 60 and -60, into
# OUTDIR/PREFIX-<pose>.png; prints the iris-blue pixels in the eye band (closed eyes should be ~0) and each viseme's change.
L=/tmp/spine-avatar-lab; OUT=$1; P=$2; mkdir -p $OUT
shots=( "0|blink 0;viseme off|open" "0|blink 1;viseme off|blink" "0|blink 0.5;viseme off|halfblink" "0|blink 0;viseme 10|aa" "0|blink 0;viseme 13|O" "0|blink 0;viseme 1|PP" "0|blink 0;viseme 2|FF" "0|blink 0;viseme 11|E" "60|blink 1;viseme off|blink-y60" "-60|blink 1;viseme off|blink-y-60" )
for s in "${shots[@]}"; do yaw=${s%%|*}; rest=${s#*|}; cmds=${rest%%|*}; name=${rest##*|}; printf "yaw $yaw\n" >> $L/cmds; for c in ${(s:;:)cmds}; do printf "$c\n" >> $L/cmds; done; sleep 2.5; printf "shotview $P-$name\n" >> $L/cmds; sleep 2; done
printf "blink off\nviseme off\nyaw 0\n" >> $L/cmds; sleep 2
/Users/jonatansoderberg/.cache/spine-avatar-lab/pixal3d/.venv/bin/python - $L/out $OUT $P <<'PY'
import base64, io, sys, numpy as np
from PIL import Image
txt = open(sys.argv[1], errors='ignore').read().replace('\0', ''); out, P = sys.argv[2], sys.argv[3]
d = {l.split()[1]: l.split()[2] for l in txt.splitlines() if l.startswith('png ') and len(l.split()) > 2 and l.split()[1].startswith(P + '-')}
for k, v in d.items():
    im = Image.open(io.BytesIO(base64.b64decode(v))).convert('RGBA'); bg = Image.new('RGBA', im.size, (240, 242, 246, 255)); bg.alpha_composite(im); bg.convert('RGB').save(f'{out}/{k}.png')
def iris(n):
    a = np.array(Image.open(f'{out}/{P}-{n}.png').convert('RGB')).astype(int)[880:1080, 780:1660]; r, g, b = a[..., 0], a[..., 1], a[..., 2]; return int(((b > r + 40) & (b > g + 30) & (b > 120)).sum())
print('iris-blue px in the eye band: open', iris('open'), '| half', iris('halfblink'), '| closed', iris('blink'))
ref = np.array(Image.open(f'{out}/{P}-open.png').convert('RGB')).astype(int)
for n in ('aa', 'O', 'PP', 'FF', 'E'):
    a = np.array(Image.open(f'{out}/{P}-{n}.png').convert('RGB')).astype(int); print(f'viseme {n:3s} changed px {int((np.abs(a - ref).sum(-1) > 30).sum())}')
PY
