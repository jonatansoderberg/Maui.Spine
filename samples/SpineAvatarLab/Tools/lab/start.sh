#!/bin/zsh
# Starts the Mac lab with the stdin harness. Commands go to $LAB_DIR/cmds (append lines), results come
# back on $LAB_DIR/out (strip NULs before grepping: tr -d '\000'). Build the Mac app first.
# Usage: start.sh   (LAB_DIR defaults to /tmp/spine-avatar-lab)
LAB_DIR=${LAB_DIR:-/tmp/spine-avatar-lab}
APP="$(cd "$(dirname "$0")/../.." && pwd)/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/Avatar Lab.app/Contents/MacOS/SpineAvatarLab"
mkdir -p $LAB_DIR
pkill -f "MacOS/SpineAvatarLab"; pkill -f "tail -f $LAB_DIR/cmds"; sleep 0.5
: > $LAB_DIR/cmds; : > $LAB_DIR/out
(tail -f $LAB_DIR/cmds | AVATARLAB_HARNESS=stdin "$APP" > $LAB_DIR/out 2>$LAB_DIR/err &)
for i in {1..40}; do grep -q "harness ready" $LAB_DIR/out && break; sleep 0.5; done
grep -q "harness ready" $LAB_DIR/out && echo "lab ready ($LAB_DIR)" || echo "lab did not start; see $LAB_DIR/err"
