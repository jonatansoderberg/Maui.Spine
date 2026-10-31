# Mini: a held look replaces the shape's resting direction

**GitHub:** no issue; found in TV:Dash (a Spine app on a television) on 2026-10-30.
**Branch:** fix/mini-held-look
**Status:** Completed

## What happened

Each Mini shape has its own resting direction (`home` in `mini_avatars.sksl`): the ball looks left and down, the cube
right and down, and only listening and speaking turn the figure toward the viewer. A held look (`AvatarView.LookAt`)
was added on top of that direction, so an app that asked the figure to look at the room still got a figure looking
aside whenever it was idle or thinking. On a television that reads as not looking at anyone.

## Changes

- `samples/SpineAvatarLab/Tools/mini_avatars.sksl`: a held look replaces the resting direction (`home` is dropped
  while `look` is not zero); the scheduler's saccades and the state behaviour stay.
- `samples/Avatars/mini.spineavatar` regenerated with `mini_avatars.py` and packed with `spine-avatar pack … --strip
  --size 256` (posters rendered again).

## Decisions

- A look of exactly zero counts as no look, as before: an app that wants the figure straight ahead holds a look a
  hair off centre (`LookAt` with a point half a pixel from the view's centre). An explicit "held" flag would need a
  new uniform in the shader contract; not worth it for one avatar.
