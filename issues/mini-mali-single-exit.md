# Mini: only the first shape drew on a Mali GPU

**GitHub:** no issue; found in TV:Dash on the Chromecast with Google TV (Mali-G31, Android 14) on 2026-11-01.
**Branch:** fix/mini-outline-single-exit
**Status:** Completed

## What happened

With the GL view (#732) on, the ball drew crisp on the box but the marshmallow drew nothing at all: the card stayed
empty. The Google TV emulator (SwiftShader), Metal and the CPU path drew every shape. The shader's `outline()` picked
the shape with a row of `if` blocks, each ending in `return`; Mali's compiler got only the first one right.

## Changes

- `samples/SpineAvatarLab/Tools/mini_avatars.py`, `mini_avatars.sksl`: `outline()` assigns one result in an
  `if / else if / else` chain and returns it once at the end.
- `samples/Avatars/mini.spineavatar` regenerated and packed (`--strip --size 192`, 34 073 bytes). MiniTests 83/83.
- `docs/wiki/avatars.md`: the caveat, next to the 32-bit one.

## Decisions

- The shader, not the renderer: the GL view is right; the driver is what it is, and one exit per function is cheap.
