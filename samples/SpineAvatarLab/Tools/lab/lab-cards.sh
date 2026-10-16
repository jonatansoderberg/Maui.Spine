#!/bin/zsh
# lab-cards.sh PKG NAME [OUTDIR] : swaps PKG into the lab app bundle, starts the lab, loads kairo and captures shotview at
# yaw 0 / 60 / -60 / 180 (size 1200 -> 2400 px, motion reduced, head straight) into OUTDIR/NAME-y<yaw>.png; NAME.done at the end.
LAB_DIR=/tmp/spine-avatar-lab; PKG=$1; NAME=$2; OUT=${3:-/tmp/kairo-shots/cards}; mkdir -p $OUT
APP_DIR="/Users/jonatansoderberg/Code/GitHub/Maui.Spine/.claude/worktrees/avatar-lab/samples/SpineAvatarLab"
cp "$PKG" "$APP_DIR/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/Avatar Lab.app/Contents/Resources/Avatars/kairo.spineavatar"
"$APP_DIR/Tools/lab/start.sh" > $OUT/$NAME-start.log 2>&1
printf "renderer native\nfollow off\ntheme light\nmotion reduced\nsize 1200\nload kairo\n" >> $LAB_DIR/cmds; sleep 12
printf "dump\n" >> $LAB_DIR/cmds; sleep 1
printf "head 0 0\nlookat 600 600\nstate idle\nblink 0\nviseme off\n" >> $LAB_DIR/cmds; sleep 1.5
for v in 0 60 -60 180; do printf "yaw $v\n" >> $LAB_DIR/cmds; sleep 1.5; printf "shotview y$v\n" >> $LAB_DIR/cmds; sleep 2; done
sleep 3
python3 - $LAB_DIR/out $OUT $NAME <<'PY'
import base64, io, sys
from PIL import Image
txt = open(sys.argv[1], errors='ignore').read().replace('\0', '')
d = {l.split()[1]: l.split()[2] for l in txt.splitlines() if l.startswith('png ') and len(l.split()) > 2}
for k in ('y0', 'y60', 'y-60', 'y180'):
    if k not in d: print('missing', k); continue
    im = Image.open(io.BytesIO(base64.b64decode(d[k]))).convert('RGBA'); bg = Image.new('RGBA', im.size, (240, 242, 246, 255)); bg.alpha_composite(im)
    bg.convert('RGB').save(f'{sys.argv[2]}/{sys.argv[3]}-{k}.png'); print('saved', k, im.size)
for l in txt.splitlines():
    if 'Last load' in l or 'failed' in l.lower() or 'loaded' in l.lower(): print('LAB:', l[:200])
PY
touch $OUT/$NAME.done
