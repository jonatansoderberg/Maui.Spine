# Issue #305 — Feature idea: Spine.Images — native image cache with prefetch, downsampling and blurhash placeholders

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/305
**Branch:** issue/305-feature-idea-spineimages-native-image-cache-with-p
**Status:** Completed

## Plan

The design is the study `docs/proposals/spine-images.md` and its "Decisions (2026-09-30)": route D (C# core + ImageIO + `NSCache`), no Nuke, BlurHash first, `Downsample` on by default, `IImageCache` injected, `BlurHash.Encode` in Common, widgets get a copy, `FadeIn` only if the placeholder holds.

Delivered here (study §9 steps 1–5 and 7; step 6 partly):

1. **Check the study against MAUI 10.0.50** (decompiled from the package, not `main`).
2. **`Plugin.Maui.Spine.Common`: `BlurHash`** — `Encode` (for a server) and `Decode` (for the placeholder), pure C#, with tests against vectors from Wolt's own TypeScript implementation.
3. **New package `Plugin.Maui.Spine.Images`** (`net10.0` for the core and tests, plus the MAUI TFMs), a `SpineModule` so `UseSpine()` registers it:
   - shared core: a disk cache (SHA-256 file names, `CacheValidity` honoured with a stale fallback when offline, LRU trim to `DiskCacheSize`), coalescing of concurrent downloads, `MaxConcurrentDownloads`, prefetch;
   - `IImageCache` (`PrefetchAsync`, `ContainsAsync`, `LoadPngAsync`, `ClearAsync`), injected;
   - iOS / Mac Catalyst: `SpineUriImageSourceService` for `UriImageSource` (concrete type wins over MAUI's `IUriImageSource` service), `NSCache` memory cache, ImageIO `CreateThumbnail` off the main thread, animated images handed to MAUI's stream service;
   - Windows: the same service over `BitmapImage` with `DecodePixelWidth`/`Height` (compiled only);
   - Android: MAUI's Glide stays; `IImageCache` on Glide (`downloadOnly()` with the same `android.net.Uri` model MAUI uses, `onlyRetrieveFromCache`, `clearMemory`/`clearDiskCache`, `asBitmap()` for PNG);
   - `ImageOptions.BlurHash` (placeholder) and `ImageOptions.Downsample` (default on) through a `ModifyMapping` on `ImageHandler`'s `Source`.
4. **Showcase page "Images"**: a grid of large remote photos with BlurHash placeholders, prefetch and clear.
5. **Measure** on the iPhone 17 Pro simulator: MAUI's loader vs Spine.Images (memory footprint, frame drops while scrolling, second launch from disk); check Android on emulator-5556.
6. **Docs**: `docs/wiki/images.md` (incl. the widget copy pattern), README/packages, the skill, the study's status.

## Open Questions

None blocking; choices made overnight are under Decisions.

## Changes

- Checked the study against the MAUI 10.0.50 assemblies (decompiled): iOS `UriImageSourceService` keeps a file per URL for as long as it exists and decodes the whole image on the main thread at scale 1; Windows caches nothing; Android loads through Glide with an `android.net.Uri` model. One correction: MAUI Controls registers its own service for the concrete `UriImageSource` in `UseMauiApp`, so Spine's registration replaces it by coming later, not because a concrete type beats an interface. MAUI does not clear the view before a load on iOS (the placeholder stays); on Android Glide clears it when the request starts.
- `Plugin.Maui.Spine.Common`: `BlurHash.Encode` / `Decode` / `IsValid` (`Images/BlurHash.cs`), tested against vectors from Wolt's TypeScript package.
- New package `Plugin.Maui.Spine.Images` (`net10.0` core + MAUI TFMs, `SpineModule` → `UseSpineImages`):
  - `ImageFileCache`: SHA-256 file names, `CacheValidity` with a stale fallback when offline, shared downloads per URL, `MaxConcurrentDownloads`, streamed to a temporary file, LRU trim to three quarters of `DiskCacheSize`, abandoned temporary files removed;
  - `IImageCache` (`PrefetchAsync`, `ContainsAsync`, `LoadPngAsync`, `ClearAsync`), `ImageCacheScope`, `SpineImagesOptions`;
  - iOS / Mac Catalyst: `SpineUriImageSourceService` (memory hit returned synchronously, ImageIO `CreateThumbnail` with `ShouldCacheImmediately` on a background thread, `NSCache` with byte cost, animated images to MAUI's `StreamImageSourceService` from the cached file);
  - Windows: the same service over `BitmapImage.DecodePixelWidth/Height` (compiled only);
  - Android: `IImageCache` on Glide (`downloadOnly()` / `onlyRetrieveFromCache` with MAUI's model, `asBitmap()` + `CenterInside` for PNG, `clearMemory` / `clearDiskCache`);
  - `ImageOptions.BlurHash` and `ImageOptions.Downsample` (default on) through `ModifyMapping` on `ImageHandler`'s `Source`; the box goes to the service through a thread-static the mapping sets around MAUI's synchronous call.
- Tests: `tests/Plugin.Maui.Spine.Images.Tests` (BlurHash vectors, the disk cache: hits, coalescing, validity, stale fallback, errors, prefetch, concurrency limit, LRU trim, temp cleanup, clear).
- Showcase page **Remote images** (`Pages/Images`): 200 picsum photos with hashes computed by `BlurHash.Encode`, prefetch and clear, the widget pattern as code.
- Docs: `docs/wiki/images.md` (with measurements and a screenshot), a "Pictures from the web" section in `widgets.md`, README and `packages.md` rows, `/spine-setup` and `/spine-controls`, the study's status line, the package icon.
- Follow-up to PR #476 (2026-10-08): `UseSpineImages` no longer depends on coming after `UseMauiApp`. `ImageSourceServiceOverride` wraps MAUI's factory for `IImageSourceServiceCollection`, so `SpineUriImageSourceService` is added after every `ConfigureImageSources` delegate has run; an `IMauiInitializeService` checks at `Build()` that `UriImageSource` resolves to Spine's service and throws, naming the fix, if not. Unit tests on MAUI's net10.0 hosting cover both orders, a builder without defaults, a later `ConfigureImageSources`, the failing check, and the old behaviour as a control. Wiki, README, `packages.md`, `/spine-setup` and the study no longer say "after `UseMauiApp`".

## Decisions

- **`ContainsAsync` instead of `bool Contains`.** On Android the only way to ask Glide is a cache-only request on a background thread.
- **`BlurHash.Decode` lives in Common next to `Encode`.** One file per algorithm, as the study proposed; the app gets it through the Images package's reference.
- **Encoder takes the largest absolute AC value** (Wolt's C and Swift encoders), not the TypeScript package's largest signed value; decoders read either.
- **An image measures as MAUI's would**: the decoded `UIImage` gets a scale so that it is one point per original pixel. Pages lay out identically with the package; only the pixels behind change.
- **An image whose size comes from its layout waits for that layout** (one pass, as Glide does) instead of being decoded for the screen. A view laid out at zero gets the screen's size; `Downsample = false` never waits.
- **The placeholder is decoded at 32 px on the long side in the view's proportions**, so `AspectFill` does not crop the hash's colours, and is skipped when the image is in memory.
- **No dispose action on the returned `UIImage`**: MAUI disposes the previous result on the next load, and the same image sits in `NSCache` and other views.
- **`MemoryCacheSize` default 100 MB** (a fixed figure rather than a share of RAM, which the simulator reports as the Mac's).
- **Downloads are streamed to a file** rather than read into an array, to keep large-object allocations out of a scrolling list.
- **Windows keeps no memory cache** of decoded images in v1: sharing a `BitmapImage` between views is unverified (study §8.7).
- **`FadeIn` and ThumbHash are left for later.** The placeholder holds on both platforms, so `FadeIn` is possible on iOS (a `CATransition` before MAUI sets the image), but Android would need a transition of its own on MAUI's Glide target; one option on one platform was not worth it tonight.
- **`HeroCollectionView` still downloads its own copy** for the colour analysis; moving it to an optional `IImageCache` is a follow-up.
- **Measured against MAUI's loader only**, not Sharpnado.Maui.Nuke (MAUI 9 package). Route D removes MAUI's main-thread decode and keeps memory at the grid's own level, so the Nuke bridge (route B) was not pursued.
- **Order-independent registration by wrapping MAUI's collection factory** (Jonatan, 2026-10-08: Spine must win whatever the order, or fail loudly). MAUI runs the `ConfigureImageSources` delegates in DI registration order when `IImageSourceServiceCollection` is first resolved, and the last one wins. Moving Spine's delegate to the end of `builder.Services` would not survive a later `UseMauiApp`, and changing services after the provider exists depends on MAUI's caching. Wrapping the factory adds the service after all delegates, using only public API (`ServiceDescriptor.ImplementationFactory`, `AddService`). The startup check catches the one path left: another library replacing the collection after Spine. It throws rather than logging, so the app cannot quietly fall back to MAUI's loader.
