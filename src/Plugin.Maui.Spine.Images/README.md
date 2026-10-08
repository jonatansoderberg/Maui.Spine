# Plugin.Maui.Spine.Images

Remote images for .NET MAUI that stay light in a long list. Every `UriImageSource` goes through it once the package is installed, with no change to the markup:

- **iOS and Mac Catalyst:** a memory cache (`NSCache`) and a disk cache, and decoding with ImageIO at the size the view shows, off the main thread. MAUI decodes every image at full size on the main thread and keeps no memory cache.
- **Windows:** a disk cache and decoding at the view's size. MAUI caches nothing there.
- **Android:** MAUI's Glide cache stays; the package adds prefetching and clearing on it.
- **Everywhere:** `IImageCache` to prefetch, check and clear, and BlurHash placeholders that show at once while an image loads.

```bash
dotnet add package Plugin.Maui.Spine.Images
```

`UseSpine()` registers it. Without Spine's core, call `builder.UseSpineImages()`; before or after `UseMauiApp`, the order does not matter.

```xml
<Image Source="{Binding PhotoUrl}"
       ImageOptions.BlurHash="{Binding PhotoHash}"
       Aspect="AspectFill" HeightRequest="120" />
```

```csharp
public class GamesViewModel(IImageCache images)
{
    // Tomorrow's logos, on disk before the list needs them
    Task Prefetch(IEnumerable<Game> games) => images.PrefetchAsync(games.Select(g => g.LogoUrl));
}
```

`BlurHash.Encode` and `BlurHash.Decode` live in [Plugin.Maui.Spine.Common](https://www.nuget.org/packages/Plugin.Maui.Spine.Common), so a server can compute the hash on upload without an image library of its own.

Platforms: Android, iOS, Mac Catalyst and Windows; `net10.0` holds only the platform-neutral disk cache, for tests.

## Documentation

- [Images](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/images.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
