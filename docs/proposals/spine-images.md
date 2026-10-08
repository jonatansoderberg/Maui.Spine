# Spine.Images — image cache, prefetching, downsampling and blurhash (study, rev 1)

**Status:** Study, with the owner's decisions from 2026-09-30 in the [Decisions](#decisions-2026-09-30) section. Steps 1–5 and 7 of the [delivery plan](#9-delivery-plan) and the `BlurHash` half of step 6 are implemented in #305 (`Plugin.Maui.Spine.Images`, `docs/wiki/images.md`); the measurement in step 1 (against MAUI's own loader; Sharpnado.Maui.Nuke was not tried) kept route D. Checked against MAUI 10.0.50: MAUI Controls registers its own service for the concrete `UriImageSource`, and Spine's replaces it by being registered later (not by a concrete type winning over an interface, as §2.1 says). Issue: [#305](https://github.com/jonatansoderberg/Maui.Spine/issues/305), prioritized as P2 in [#317](https://github.com/jonatansoderberg/Maui.Spine/issues/317). The study was done in a Linux container with no Mac and no device: nothing in it has been built or run. It rests on source code (Spine, MAUI `main`, Nuke 13.2.0, Glide 4.16.0), package contents from nuget.org and documentation.
**Question:** #317 asks two questions. How should Nuke be bound? Can the widget extension share the image cache with the app? Behind them lies a third question: what is actually missing in MAUI, platform by platform?
**Answer:** Nuke should not be bound in v1, and the cache should not be shared. What iOS lacks is a memory cache, decoding at display size and decoding off the main thread. All of that exists in Microsoft.iOS (`NSCache`, ImageIO's `CGImageSource.CreateThumbnail`) and can be written in C#. The same C# core (disk cache, coalescing of concurrent downloads, prefetching) is needed on Windows anyway, where MAUI does not cache at all. Android already has Glide through MAUI and only needs prefetching and clearing. The widget extension gets a **copy** of the image, downsampled and written to the asset store it already reads (`IWidgetService.StoreAssetAsync`), and no shared cache. If measurements on a device show that the C# route is not enough, Nuke becomes plan B. It should then be built as a Swift bridge of our own with Nuke's sources, compiled with `swiftc` at app build time the same way Widgets does. It should never be built through `ImageCaching.Nuke`.

---

## 1. The conclusion in short

| Question | Answer | Evidence |
|---|---|---|
| What is missing in MAUI on iOS? | **Memory cache, downsampling and cache validity.** `UriImageSourceService` on iOS saves the whole file in `Caches/com.microsoft.maui/MauiUriImages` and counts it as cached for as long as the file exists: `CacheValidity` is never read. Every display decodes the whole image with `CGImageSource.CreateImage(0)` at scale 1. | MAUI `main`: `UriImageSourceService.iOS.cs:46–66, 104–107`, `ImageSourceExtensions.cs:147–172` (iOS) |
| On Android? | **Almost nothing.** MAUI loads URIs with Glide (4.16, `Xamarin.Android.Glide` 4.16.0.14) and gets a memory and disk cache (250 MB). Glide also measures the `ImageView` and decodes at the view's size. What is missing is prefetching and clearing. | `PlatformInterop.java:308–355`, Glide `DiskCache.java:14`, `CustomViewTarget.java:438–460` |
| On Windows? | **Everything.** MAUI's `UriImageSource` creates a new `HttpClient` per load, and the cache branch is commented out (`// TODO: CACHING`). The image is decoded at full size in a `BitmapImage`. | MAUI `main`: `Controls/src/Core/UriImageSource.cs:89–122`, `UriImageSourceService.Windows.cs` |
| Should Spine bind Nuke? | **Not in v1.** The C# route with ImageIO and `NSCache` solves what the issue describes and can be shared with Windows. Nuke adds progressive decoding, prioritization and a mature LRU disk cache. It costs a Swift build of 55 source files and a bridge with callbacks, and only helps iOS. | §4 |
| If Nuke is needed after all, how? | **Our own `@objc` bridge and Nuke 13.2.0 from source, compiled with `swiftc` at app build time** (the same pattern as `SpineWidgetBridge`). `ImageCaching.Nuke` 5.0.0 is no way out: its xcframework has no Mac Catalyst slice, and the proxy's API has no downsampling and no choice of cache directory. A prebuilt xcframework cannot be built in CI, which runs on `windows-latest`. | §4, `unzip -l imagecaching.nuke.5.0.0.nupkg`, `.github/workflows/ci.yml:13` |
| Can the widget extension share the cache? | **No. It gets a copy.** The widget renderer only reads stored assets (`UIImage(contentsOfFile:)` under `spine-widgets/assets/`) and has no URL images. An image cache is LRU-driven and can be cleared by the system, but a widget image must not disappear. Nuke itself advises against two `DataCache` instances on the same directory, and two processes give exactly that. | `SpineWidgetRenderer.swift:43–45`, `WidgetPlatform.Apple.cs:138–144`, Nuke `DataCache.swift:34–35` |
| Blurhash or ThumbHash? | **Both can be decoded in pure C#** with no dependencies. Both algorithms are short, and MIT-licensed ports exist (`Blurhash.Core` 4.2.1, `ThumbHash` 2.1.1). Which one should be primary is the owner's decision (§3.4). | nuspec on nuget.org |

---

## 2. What already exists

### 2.1 MAUI's image loading per platform

The source is `dotnet/maui` `main` (2026-09-28). The repo pins `Microsoft.Maui.Controls` 10.0.50 (`Directory.Packages.props:10`), but the difference from 10.0.50 has not been checked file by file.

| | iOS / Mac Catalyst | Android | Windows |
|---|---|---|---|
| Loader | `UriImageSourceService.iOS.cs`, own code | `PlatformInterop.loadImageFromUri` → Glide | `UriImageSourceService.Windows.cs` → `BitmapImage.SetSourceAsync` |
| Memory cache | None | Glide's (`LruResourceCache`) | None |
| Disk cache | The whole file, CRC64 of the URL as its name, no expiry (`IsImageCached` = `File.Exists`) | Glide's, 250 MB in `image_manager_disk_cache` | None |
| `CachingEnabled=false` | Skips the file | `DiskCacheStrategy.NONE` + `skipMemoryCache` | No difference |
| `CacheValidity` | Not read | Not read | Not read |
| Downsampling | No, full decoding at scale 1 | Yes, to the view's size. With `wrap_content` the screen's largest dimension is used. | No (but `DecodePixelWidth` exists, §3.3) |
| Animated GIF | Yes (`ImageAnimationHelper`) | Yes (Glide) | Yes (WinUI) |
| Replaceable | Yes: `ConfigureImageSources` + `AddService<UriImageSource, …>`. *Correction (2026-10-08):* MAUI Controls registers its own service for the concrete `UriImageSource` in `UseMauiApp`, so the concrete type alone does not win; the last registration does. Spine wraps MAUI's `IImageSourceServiceCollection` factory so its service is added after every registration, whatever the order (follow-up to PR #476). | The same, but Glide's request is built in Java and cannot be changed from outside | The same |

The Glide configuration cannot be reached the way the issue assumes. MAUI owns the app's only `AppGlideModule` (`MauiGlideModule`), and it only sets the log level in `applyOptions`. A `LibraryGlideModule` from Spine can register components but cannot change the `GlideBuilder` (disk cache size, memory size). The only way there is `Glide.init(Context, GlideBuilder)` before the first load. The method is public but marked `@VisibleForTesting` in 4.16.0, so it should not be used in v1.

### 2.2 In Spine

| Part | Where | What it means for images |
|---|---|---|
| A custom `IImageSourceService` | `Svg/MauiAppBuilderExtensions.cs:46–47`, `SvgBitmapImageSourceService.Apple.cs:12,31`, `SvgBitmapImageSourceService.Android.cs:13,33` | The pattern exists and already works: one service per platform, registered with `ConfigureImageSources`, that decodes at the right scale. A service for `UriImageSource` has the same shape. |
| Attached properties + mapping | `SvgImageSource.cs:20` (static class, `CreateAttached` from line 26), `Extensions/Material.cs:97`, `Extensions/GlassExtensions.cs:53` | `ImageOptions.BlurHash` / `.Downsample` / `.FadeIn` follow the same shape as `Material.Kind` and `Glass.Style`. |
| Rendering at the view's size | `SvgImageSourceBehavior` (listens to `SizeChanged`) | The same way to get the target size for downsampling. |
| Widget images | `IWidgetService.StoreAssetAsync` (`IWidgetService.cs:25`), `StorePackageAssetAsync` (`:35`), `WidgetAsset.Rolling` (`WidgetAsset.cs:15`), `W.Image(assetId)` (`W.cs:54`) | Widgets only show bitmaps that the app has stored under an asset id. iOS writes them to the App Group directory `spine-widgets/assets/` (`WidgetPlatform.Apple.cs:49,138–144`). Android writes to `FilesDir/spine-widgets/assets` (`WidgetStore.cs:22`). |
| The widget renderer | `SpineWidgetRenderer.swift:30–46` (`Store.image(asset:)`), `:434–445` (remote source) | The extension reads assets synchronously from the file. `RemoteSource` (`WidgetTimeline.cs:151`) only fetches the JSON document. A backend cannot point to a new image, only to an asset id that the app has already written. |
| Swift in packages | `spine-widgets-build.sh:312` (`swiftc -emit-library -module-name SpineWidgetBridge`), `Plugin.Maui.Spine.Widgets.targets:128` (`NativeReference`), `WidgetPlatform.Apple.cs:22,230` (`objc_msgSend`, no binding) | This is how a Nuke bridge would be built (§4). The issue says "xcframework in `native/`", but Widgets and Push ship Swift **sources** that are compiled at app build time. They ship no prebuilt binaries. |
| Push images | `SpinePushNotificationService.swift:35` (`URLSession.downloadTask`) | The Notification Service Extension downloads its image itself. It should not share a cache either. The reasoning is the same as for widgets. |
| Its own download | `HeroCollectionView.AdaptiveOverlay.cs:217–220` (`new HttpClient()` for `UriImageSource`) | Loads the image a second time for the color analysis. It should go through `IImageCache` once that exists (§5). |
| Skeleton | `Plugin.Maui.Spine.Controls.Shimmer` (`Skeleton.cs`, `SkeletonDrawable.cs`) | The gray alternative to blurhash. They do not compete: the skeleton is for the page while data loads, blurhash is for an image whose URL is already known. |
| The server | `Plugin.Maui.Spine.Server` (`net10.0`, push only: `Azure.Data.Tables`, `FirebaseAdmin`) | Has no image decoder. A blurhash helper there would pull in SkiaSharp or ImageSharp (§3.4). |
| Minimum versions | `Directory.Build.props:22–25`: iOS/Catalyst 15.0, Android 21, Windows 10.0.17763 | Nuke 13.2.0 requires iOS 15 and fits. Nuke's `main` requires iOS 16. |

---

## 3. The platforms' building blocks

### 3.1 iOS and Mac Catalyst

**In Microsoft.iOS, without Swift:**
- `CGImageSource.CreateThumbnail(0, new CGImageThumbnailOptions { MaxPixelSize = …, CreateThumbnailFromImageAlways = true, ShouldCacheImmediately = true })` decodes straight to the target size without the whole bitmap passing through memory. `ShouldCacheImmediately` forces the decoding onto the calling thread, so it can be moved off the main thread. This is ImageIO's route. Nuke uses the same route for `ImageRequest.thumbnail`.
- `NSCache` with `TotalCostLimit` and cost = bytes in the bitmap. The cache empties itself on a memory warning, and the bitmaps are held on the native side, so .NET's GC does not need to see them.
- `NSUrlSession` or `HttpClient` (which in .NET for iOS goes through `NSUrlSessionHandler`) for the download, and files under `FileSystem.CacheDirectory`.

**Nuke 13.2.0** (MIT, latest release, iOS 15, `swift-tools-version:6.0`, 55 Swift files in `Sources/Nuke`):
`ImagePipeline` with a memory cache (`ImageCache`), an LRU disk cache (`DataCache`, 150 MB by default, `init(path:)` for any directory), `ImagePrefetcher`, `ImageProcessors.Resize`, `ImageRequest.ThumbnailOptions`, `isProgressiveDecodingEnabled`, coalescing of identical requests and prioritization. Transitions and placeholders for `UIImageView` are in the `NukeExtensions` target.

**Existing .NET packages:**

| Package | Latest | Targets | What it is |
|---|---|---|---|
| `ImageCaching.Nuke` | 5.0.0 (2025-11-13) | `net9.0-ios18.0`, `net9.0-maccatalyst18.0`, `net10.0-ios26.0`, `net10.0-maccatalyst26.0` | NukeProxy: a prebuilt `NukeProxy.xcframework` with Nuke built in, bound with a thin `@objc` surface. The package has no license expression. |
| `Sharpnado.Maui.Nuke` | 12.8.3 (2025-11-22) | Only `net9.0-*` (MAUI 9.0.82) | Replaces MAUI's image services on iOS with the above. MIT. |

Two things in `ImageCaching.Nuke` 5.0.0 settle the question. They were checked by unpacking the package:
1. **No Mac Catalyst slice.** Both `lib/net10.0-maccatalyst26.0/…resources.zip` and `net9.0-maccatalyst18.0` contain only `ios-arm64` and `ios-arm64_x86_64-simulator`. That should give link errors on Catalyst. It has not been tried.
2. **The proxy's API** (`NukeProxy.swiftinterface`): `ImagePipeline.setupWithDataCache()`, `loadImage(url:onCompleted:)`, `loadImage(url:placeholder:errorImage:into:)`, `isCached`, `removeAllCaches`, `Prefetcher(destination:maxConcurrentRequestCount:)`. There is no size, no processor, no thumbnail and no way to choose the cache directory. So downsampling and a directory of our own cannot be had through the package.

### 3.2 Android

Glide 4.16 is in every MAUI app, and the C# binding `Bumptech.Glide` comes transitively through `Xamarin.Android.Glide`:
- Prefetching: `Glide.With(context).Load(uri).Preload()`. It loads at original size into memory and onto disk. `DiskCacheStrategy.AUTOMATIC` saves remote data as the original (DATA), so a later load at the view's size hits the disk cache. It has not been tried whether the cache key comes out the same when MAUI loads with an `android.net.Uri` and Spine prefetches with a string. That must be verified.
- Clearing: `Glide.Get(context).ClearMemory()` on the main thread and `ClearDiskCache()` on a background thread.
- Placeholder and fade-in: MAUI's request is built in Java without `placeholder`/`transition`. `MauiCustomViewTarget` does not override `onResourceLoading`, so a drawable set on the `ImageView` before the load should stay in place until `onResourceReady` replaces it. This is read from the source and has not been run.

Coil is not an option. It would be a second image loader next to MAUI's Glide.

### 3.3 Windows

`BitmapImage.DecodePixelWidth`/`DecodePixelHeight` (with `DecodePixelType.Logical`) decodes at the target size. The issue says "no native downsampling", but that is not correct. Cache, request coalescing and prefetching have to be written in C#, and it is the same code as the iOS route in §4 D. It has not been checked whether a `BitmapImage` can be shared between several `Image` elements, which a memory cache would need.

### 3.4 Blurhash and ThumbHash

| | BlurHash | ThumbHash |
|---|---|---|
| Form | Base83 string, 20–30 characters | Bytes (~25), in practice base64 |
| According to its author | Configurable number of components (4×3 is common) | "Encodes more detail in the same space", aspect ratio, alpha, no parameters |
| Adoption | Wide: Unsplash and Mastodon, among others, deliver ready-made hashes | Smaller |
| .NET | `Blurhash.Core` 4.2.1 (MIT, netstandard2.0, `Blurhasher.Encode/Decode` on `Pixel[,]`). `Blurhash.SkiaSharp` 2.0.0 pulls in SkiaSharp **2.88**, while Spine runs 3.119. | `ThumbHash` 2.1.1 (MIT, net6.0/netstandard2.0, `ThumbHash.FromImage`, `ToImage()` → RGBA) |

The recommendation is our own decoding in the package: one file per algorithm, RGBA to a 32×32 bitmap that the platform scales up smoothly. The algorithms are short, and a dependency for a hundred lines is not worth it. It should not go through PNG, because a placeholder has to show in the same layout pass (cf. `SvgBitmapImageSourceService.Android.cs`, which decodes synchronously for exactly that reason, #345). The decoding should take less than a millisecond. That has not been measured.

The encoding does not belong in the app. The server needs an image decoder to be able to compute the hash. Proposal: a `BlurHash.Encode(ReadOnlySpan<byte> rgba, int width, int height)` in `Plugin.Maui.Spine.Common` (`net10.0`, no dependencies). The server decodes the upload with the library it already has and calls it. The server package then gets no image library.

---

## 4. The alternatives for iOS

| | A. `ImageCaching.Nuke` / Sharpnado | B. Our own bridge + Nuke from source | C. Our own bridge + prebuilt xcframework | D. C# core + ImageIO + `NSCache` |
|---|---|---|---|---|
| Memory and disk cache | Yes | Yes | Yes | Yes (own LRU for disk) |
| Downsampling | **No** (not in the proxy) | Yes (`thumbnail`) | Yes | Yes (`CreateThumbnail`) |
| Progressive JPEG | No in the proxy | Yes | Yes | No |
| Mac Catalyst | **No slice** | Yes, `spine-widgets-build.sh` already builds `-macabi` | Requires a slice of its own | Yes |
| Build | NuGet | `swiftc` of ~55 files + bridge per RID at app build time. The time has not been measured, but the widget build takes ~10 s for 5 files. | Requires a Mac to produce the package. CI runs `windows-latest` (`ci.yml:13`, `release.yml:18`). | None |
| Bridge to C# | Ready-made | `@objc` + `objc_msgSend`, but loading is asynchronous and requires block callbacks (`BlockLiteral`). That has not been tried in the repo. | The same | None |
| Windows | Does not help | Does not help | Does not help | **The same core** (disk, request coalescing, prefetching). Only the decoding differs. |
| Binary size | NukeProxy ios-arm64: 717 kB | Similar | Similar | ~0 |
| Maintenance | Two external maintainers, four TFMs behind | Nuke's upgrades as source code | The same + binaries in git | Our own |

**Recommendation: D in v1.** The issue complains about three things: no memory cache, no downsampling and no prefetching. All three are solved with APIs that are already bound, and part of the code (disk, request coalescing, prefetching) is shared with Windows. Nuke's real advantages are progressive decoding, prioritization and a well-proven LRU disk cache. None of the apps' uses need them: club badges, team logos and theme backgrounds are small or few.

**B is plan B.** It applies if D on a device does not give smooth scrolling in a list of ~200 images, compared with Sharpnado.Maui.Nuke in the same list (§9 step 1). In that case Nuke 13.2.0 is vendored in `native/ios/Nuke/` with its license, built together with the bridge into `SpineImages.framework` with the same script and pattern as `SpineWidgetBridge`, and C# calls an `@objc` class with a URL, a target size in pixels and a callback. A is ruled out because of Catalyst and the missing size API. C is ruled out because of CI.

---

## 5. Proposed API surface

The package is called `Plugin.Maui.Spine.Images` and registers itself as a `SpineModule` (cf. `Plugin.Maui.Spine.Widgets.props:4`), so that `UseSpine()` picks it up.

```csharp
namespace Plugin.Maui.Spine.Images;

/// <summary>The app's remote-image cache. Every <see cref="UriImageSource"/> goes through it once the package is installed.</summary>
public interface IImageCache
{
    /// <summary>Downloads into the disk cache so the images show later without the network.</summary>
    Task PrefetchAsync(IEnumerable<Uri> uris, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="uri"/> is on disk; nothing is downloaded.</summary>
    bool Contains(Uri uri);

    /// <summary>
    /// The image at most <paramref name="maxPixelSize"/> pixels on its longest side, as PNG, from the cache
    /// or the network. For handing a picture to something outside the app's views, such as
    /// <c>IWidgetService.StoreAssetAsync</c>.
    /// </summary>
    Task<Stream> LoadPngAsync(Uri uri, int maxPixelSize, CancellationToken cancellationToken = default);

    Task ClearAsync(ImageCacheScope scope = ImageCacheScope.All);
}

[Flags]
public enum ImageCacheScope { Memory = 1, Disk = 2, All = Memory | Disk }

public sealed class SpineImagesOptions
{
    /// <summary>iOS and Windows; on Android Glide's own limit (250 MB) applies.</summary>
    public long DiskCacheSize { get; set; } = 150 * 1024 * 1024;
    public int MaxConcurrentDownloads { get; set; } = 4;
}

/// <summary>Per-image options for an <see cref="Image"/> with a remote source.</summary>
public static class ImageOptions
{
    public static readonly BindableProperty BlurHashProperty =
        BindableProperty.CreateAttached("BlurHash", typeof(string), typeof(ImageOptions), null,
            propertyChanged: static (b, _, _) => (b as Image)?.Handler?.UpdateValue(nameof(IImage.Source)));

    public static readonly BindableProperty ThumbHashProperty = /* same, string (base64) */;

    /// <summary>Decode at the view's size instead of the image's. iOS and Windows; Android always does.</summary>
    public static readonly BindableProperty DownsampleProperty = /* bool, false */;

    public static readonly BindableProperty FadeInProperty = /* bool, false */;

    public static string? GetBlurHash(BindableObject view) => (string?)view.GetValue(BlurHashProperty);
    public static void SetBlurHash(BindableObject view, string? value) => view.SetValue(BlurHashProperty, value);
    // ...
}
```

```xml
<Image Source="{Binding BadgeUrl}"
       ImageOptions.BlurHash="{Binding BadgeHash}"
       ImageOptions.Downsample="True"
       ImageOptions.FadeIn="True"
       WidthRequest="40" HeightRequest="40" />
```

**Why an interface and not a static `ImageCache`.** Spine's services are injected (`IWidgetService`, `IPushService`, `ILocalNotificationService`). The issue's static sketch breaks with that without gaining anything.

**Why no `Widgets` reference.** The widget case is four lines in the app, and the packages stay independent of each other:

```csharp
foreach (var team in teams)
{
    await using var png = await _images.LoadPngAsync(team.LogoUrl, maxPixelSize: 120);
    await _widgets.StoreAssetAsync($"logos/{team.Id}.png", png);
}
await _widgets.RefreshAsync<GamesWidget>();
```

**Two layers, two mechanisms:**
1. *The service.* `AddService<UriImageSource, SpineUriImageSourceService>` on iOS and Windows gives a memory and disk cache and decoding off the main thread for **all** remote images, with no change to the markup. An animated image (`ImageCount > 1`) is handed to MAUI's own `UriImageSourceService`, which is public, so that GIF keeps working.
2. *The options.* `ImageOptions.*` requires the view's size to be known, but `IImageSourceService.GetImageAsync` is given no size. So it becomes a `ModifyMapping` on `ImageHandler`'s `Source`. When any option is set, the placeholder is put in the native view right away. Then MAUI's mapping runs, and the target size is taken from `WidthRequest`/`HeightRequest` or the first `SizeChanged`, as in `SvgImageSourceBehavior`. An image with no known size is scaled to at most the screen width times the density. On Android `Downsample` means nothing, since Glide already scales down.

`HeroCollectionView`'s own `HttpClient` download (`AdaptiveOverlay.cs:217`) is replaced with `IImageCache` when the package is present. That can be done with an optional service, so that the controls package does not depend on Images.

---

## 6. The widget extension and the cache

| Route | Outcome |
|---|---|
| Nuke's or Spine's disk cache in the App Group directory, read by both processes | **No.** Two cache instances on the same directory sweep and write independently of each other. Nuke: "It's possible to have more than one instance of `DataCache` with the same path but it is not recommended." An LRU sweep can also remove an image that a widget is showing. |
| The extension links the same cache library | **No.** That gives a larger appex and more memory in a process with a tight memory limit. `IWidgetService` itself says "the renderer runs under a tight memory limit". Apple gives no figure. |
| **Copy to the asset store** (`LoadPngAsync` → `StoreAssetAsync`) | **Yes.** It uses the route that already exists and already works for Live Activities (Puckkoll's team logos). The image is written at the size the widget shows and lives outside `Library/Caches`, so it is not cleared. `WidgetAsset.Rolling` keeps the store bounded for dated images. Android works the same way (`WidgetStore.AssetsDirectory`). |
| Remote documents that point to image URLs (`RemoteSource` without the app) | **Later, a step of its own.** It requires `SpineWidgetRenderer.swift` to download images while the timeline is being built (it already fetches JSON there, `:442`) into a directory of its own, for example `spine-widgets/remote/`, with a simple cleanup against the document's current URLs. That is widget code and not image cache code. It is done when an app needs it. |

According to Apple, iOS only creates `Library/Caches` automatically in the group directory. It is not documented whether the system empties that particular one when storage runs low. It does not matter here, since assets live in `spine-widgets/`.

---

## 7. Platforms

| Platform | Cache | Downsampling | Prefetching | Placeholder / fade-in |
|---|---|---|---|---|
| iOS 15+ | Own: `NSCache` + LRU files | ImageIO `CreateThumbnail` | `IImageCache`, own queue | In the `Source` mapping, `UIImageView` |
| Mac Catalyst 15+ | The same code | The same | The same | The same. Not verified. |
| Android 21+ | Glide through MAUI (unchanged) | Glide, already today | `Glide.Load(uri).Preload()` | Drawable before MAUI's load. Fade-in needs a transition of its own (§8). |
| Windows | Own: memory + LRU files (the same core as iOS) | `DecodePixelWidth` | The same queue as iOS | The `Source` mapping, WinUI `Image` |

The issue calls Windows "Partial", but that is no longer correct. Windows gets the same surface as iOS.

---

## 8. What cannot be done, and what is not verified

1. **Nothing has been run.** The study was done without a Mac, simulator or device. The claims about MAUI, Glide and Nuke come from the source code and the claims about the packages from their contents. No code has been tried.
2. **D against Nuke has not been measured.** The recommendation rests on what the issue says is missing, not on a profile. Step 1 in the delivery plan is exactly such a measurement, and B remains as a fully described alternative.
3. **`ImageCaching.Nuke` on Catalyst.** That the slice is missing has been checked in the package. That it gives link errors is a conclusion, not a run result.
4. **Placeholder and fade-in in the `Source` mapping.** It is unknown whether MAUI's `ImageSourcePartLoader` clears the native image when a new load begins. The placeholder would then disappear at once. Fade-in requires getting at the moment the image is set, and MAUI owns that. Both need a spike per platform. If they do not hold, `FadeIn` is pushed to v2.
5. **Glide's cache key** for a `Uri` compared with a string (§3.2) is not verified. The prefetch must be loaded with exactly the model type MAUI uses.
6. **Glide's disk cache size** cannot be changed without the `@VisibleForTesting` API. `DiskCacheSize` therefore does not apply to Android, and the documentation must say so.
7. **`BitmapImage` shared between elements** on Windows has not been checked. If it cannot be done, the memory cache on Windows becomes a cache of bytes and not of decoded images.
8. **GC and native memory.** `NSCache` holds `UIImage` on the native side, but every `UIImage` that a view shows also has a .NET reference. Whether that puts pressure on the GC or leaks during fast scrolling will only show in Instruments.
9. **Measuring an image with no known size.** An `Image` that gets its size from the image cannot be scaled down to the view. The screen-size cap is a guess at what is reasonable.

---

## 9. Delivery plan

1. **Spike and measurement (iOS, device).** A list of ~200 remote images in `MauiSpineSampleApp`: MAUI as it is, Sharpnado.Maui.Nuke (the MAUI 9 package tried against 10) and a minimal D service. Measure scrolling, peak memory and time to first image. This is where D or B is decided.
2. **The core and the iOS/Windows service.** `IImageCache`, disk LRU, request coalescing, prefetch queue, `SpineUriImageSourceService` for iOS, Catalyst and Windows with a GIF fallback, `SpineImagesOptions`, `UnsupportedImageCache` where nothing exists.
3. **Android.** `IImageCache` on top of Glide: prefetching, `Contains` (if Glide allows it without loading), clearing and `LoadPngAsync`.
4. **`ImageOptions`.** BlurHash and ThumbHash decoders with tests against the reference vectors from the original repos, `Downsample`, the placeholder. `FadeIn` only if the spike in §8 item 4 holds.
5. **Widgets.** The pattern in §5 in `docs/wiki/widgets.md` and in the sample: `LoadPngAsync` → `StoreAssetAsync`. No code in Widgets.
6. **`BlurHash.Encode` in Common** and an example in `MauiSpinePushNotificationsSampleApp.Server` that computes the hash on upload.
7. **Wiki** `docs/wiki/images.md`, with the limits in §8 items 5–6 and §6.
8. **Later, if needed:** image URLs in remote documents for widgets (§6 last row) and the Nuke route B if step 1 requires it.

**Decisions the owner needs to make:** D or B after step 1. BlurHash, ThumbHash or both in v1. Whether `Downsample` should be on by default on iOS, which would be in line with what Android already does. Whether `HeroCollectionView` should know about `IImageCache`.

---

## Decisions (2026-09-30)

Jonatan went through the study's questions on 2026-09-30 and followed the recommendations. Rows marked **Proposal** had no recommendation in the study; they carry a proposal with reasons, which applies until he says otherwise.

- **The iOS route.** C# core with ImageIO and `NSCache` (D) in v1. The Nuke bridge (B) only if the measurement in step 1 shows that D does not scroll smoothly with about 200 images.
- **Placeholder** — **Proposal.** BlurHash in v1, ThumbHash later behind the same `ImageOptions` surface. BlurHash is what image services and servers already deliver (§3.4), and one decoder with test vectors is enough for the first version. ThumbHash is added when an app has images of its own with alpha or wants the aspect ratio from the hash.
- **`Downsample` on iOS** — **Proposal.** On by default. Android already scales down to the view's size, so the same default gives the same memory footprint on both platforms, and decoding at display size is Apple's own recommendation for lists of images. Anyone who wants full resolution turns it off per image.
- **`HeroCollectionView`.** May know about `IImageCache` through an optional service, so that the controls package does not depend on Images.
- **The widget extension.** Gets a copy through `LoadPngAsync` → `StoreAssetAsync`, no shared cache (§6).
- **`IImageCache`.** Injected. No static `ImageCache` as in the issue's sketch.
- **`BlurHash.Encode`.** Lives in `Plugin.Maui.Spine.Common`, so that the Server package is spared an image library.
- **`FadeIn`.** Only if the placeholder spike holds (§8 item 4), otherwise v2.
- **Image URLs in remote documents for widgets.** A later step of its own, when an app needs it.

---

## 10. References

- Nuke (MIT, 13.2.0): https://github.com/kean/Nuke — `Package.swift` at the tag `13.2.0`, `Sources/Nuke/Caching/DataCache.swift`, `Sources/Nuke/ImageRequest.swift` (`ThumbnailOptions`), `Sources/Nuke/Pipeline/ImagePipeline+Configuration.swift`
- NuGet, `ImageCaching.Nuke` 5.0.0: https://www.nuget.org/packages/ImageCaching.Nuke — source https://github.com/roubachof/NukeProxy
- NuGet, `Sharpnado.Maui.Nuke` 12.8.3: https://www.nuget.org/packages/Sharpnado.Maui.Nuke — source https://github.com/roubachof/Maui.Nuke
- dotnet/maui `main`: `src/Core/src/ImageSources/UriImageSourceService/UriImageSourceService.{iOS,Android,Windows}.cs`, `src/Core/src/ImageSources/iOS/ImageSourceExtensions.cs`, `src/Controls/src/Core/UriImageSource.cs`, `src/Core/src/Hosting/ImageSources/ImageSourceToImageSourceServiceTypeMapping.cs`, `src/Core/AndroidNative/maui/src/main/java/com/microsoft/maui/PlatformInterop.java`, `…/glide/MauiGlideModule.java`, `…/glide/MauiCustomViewTarget.java` — https://github.com/dotnet/maui
- NuGet, `Microsoft.Maui.Core` 10.0.50 (dependency `Xamarin.Android.Glide` 4.16.0.14): https://www.nuget.org/packages/Microsoft.Maui.Core/10.0.50
- Glide 4.16.0: `Glide.java` (`init(Context, GlideBuilder)`, `@VisibleForTesting`), `DiskCache.java`, `RequestBuilder.java` (`preload`, `submit`), `CustomViewTarget.java` — https://github.com/bumptech/glide/tree/v4.16.0/library/src/main/java/com/bumptech/glide
- Apple, `containerURL(forSecurityApplicationGroupIdentifier:)`: https://developer.apple.com/documentation/foundation/filemanager/containerurl(forsecurityapplicationgroupidentifier:)
- Microsoft, `BitmapImage.DecodePixelWidth`: https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.media.imaging.bitmapimage.decodepixelwidth (the UWP page. The WinUI counterpart in `Microsoft.UI.Xaml.Media.Imaging` was not opened from here.)
- BlurHash (Wolt): https://github.com/woltapp/blurhash — .NET: https://github.com/MarkusPalcer/blurhash.net, https://www.nuget.org/packages/Blurhash.Core, https://www.nuget.org/packages/Blurhash.SkiaSharp
- ThumbHash (Evan Wallace): https://github.com/evanw/thumbhash — .NET: https://github.com/jzebedee/ThumbHash, https://www.nuget.org/packages/ThumbHash
