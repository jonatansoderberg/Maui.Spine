# Issue #371 — SVG names resolve by suffix: lock.svg can load Clock, SmartClock or Unlock

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/371
**Branch:** issue/371-svg-exact-name
**Status:** Completed

## Plan
Match the whole file name: a resource matches when its name equals the file name or ends with `.` + file name. Keep the pure matching in a MAUI-free helper so the net10.0 test project can compile it in, like `SvgRasterizer`.

## Changes
- `SvgResourceMatch` (new, internal): whole-name match; several matches resolve to the shortest (then ordinal) name and report ambiguity.
- `ResourceNameCache.Resolve`/`OpenStream` use it, with a per-name cache cleared when assemblies are added (the old code scanned every resource name on every lookup).
- `SvgIconService`'s standalone fallback scan uses the same rule.
- Tests: `SvgResourceMatchTests` (Lock/Clock/SmartClock/Unlock, folder paths, duplicates).
- Rows sample: Privacy back to `lock.svg` (it was `eye.svg` as a workaround); `docs/wiki/svg.md` states the rule.

## Decisions
- An ambiguous name is written to the debug output rather than thrown: the same icon in two assemblies (an app overriding a package icon) is legitimate, and the choice is now deterministic.
- Verified on the iPhone 17 simulator: the Rows page's Privacy row shows the padlock.
