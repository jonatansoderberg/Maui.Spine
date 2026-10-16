# Issue #579 — Spine.Images: the app crashes at launch in a Release build on an iPhone (AOT skips ImageTarget)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/579
**Branch:** issue/579-images-aot-imagetarget
**Status:** In review

## Plan

The Showcase, built in Release for the owner's iPhone on 2026-10-16, aborted at launch. The console (`devicectl … --console`) showed:

```
System.ExecutionEngineException: Attempting to JIT compile method
'void Plugin.Maui.Spine.Images.ImageOptions:MapSource (IImageHandler, IImage, Action<IElementHandler, IElement>)'
while running in aot-only mode.
```

`MapSource` is the `Image.Source` mapping, so the first image on screen took the app down. Fix whatever makes the AOT compiler drop the method.

## Open Questions

## Changes

- `ImageTarget` is a `sealed record` (a class) instead of a `readonly record struct`, with a comment saying why.

## How it was found

- Clean obj and bin made no difference. The build's AOT step reported `Compiled: 409/427` for `Plugin.Maui.Spine.Images` and gave no warning. Every other Spine assembly compiled in full.
- `nm` on the AOT object file had no `ImageOptions_MapSource`, and none of `ImageTarget`'s `For`, `Full`, `IsFull`, `Screen`, `MaxPixelSize`, `Key` or its record members.
- `mono-aot-cross` on the Images assembly alone reproduced it, with untrimmed IL too, so the trimmer is not involved.
- Bisecting `MapSource`'s body: removing the `#if` block, the `try/finally` or the `SetPending` calls made no difference, and neither did `[MethodImpl(NoInlining)]` on `SetPending`. Removing the call to `ImageTarget.For` made it compile.
- With `ImageTarget` as a class, the assembly compiles 2196 of 2196 methods, `MapSource` included.

## Verification

- `Plugin.Maui.Spine.Images.Tests`: 33 passed.
- A clean Release build for `ios-arm64` has `ImageOptions_MapSource` in the AOT output. The Showcase starts on the iPhone (iOS 27.0) with the console attached.

## Decisions

- A class rather than a fix to the struct. **Why:** Mono gives no reason for skipping, and the struct buys nothing here: one small allocation per image load, next to a decode. The `[ThreadStatic]` nullable field keeps its meaning with a reference type.
