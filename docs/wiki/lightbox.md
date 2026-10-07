# Lightbox

A lightbox shows photos full screen on black, as Photos does: swipe sideways between them, pinch or double-tap to zoom, tap to hide the bars, and drag down to close. It opens out of the thumbnail you tapped and closes into the thumbnail of the photo showing, even after paging to another one. Share and Save sit in a toolbar under the photo, next to the page's own actions.

It is a page of its own kind, `[NavigableLightbox]`, with a `Lightbox` on it. Both are in the core package.

---

## A lightbox page

```csharp
[NavigableLightbox]
public partial class PhotoViewerPage : INavigableWithParameter<PhotoSet>
{
    public PhotoViewerPage() => InitializeComponent();
}

public partial class PhotoViewerPageViewModel : ViewModelBase, IReceivesNavigationParameter<PhotoSet>
{
    [ObservableProperty]
    public partial IReadOnlyList<LightboxImage> Images { get; set; } = [];

    [ObservableProperty]
    public partial int Index { get; set; }

    public Task OnNavigationParameterAsync(PhotoSet param)
    {
        Images = param.Images;
        Index = param.Index;
        return Task.CompletedTask;
    }

    partial void OnIndexChanged(int value) => Title = $"{value + 1} of {Images.Count}";
}
```

```xml
<SpinePage ... x:TypeArguments="PhotoViewerPageViewModel">
    <Lightbox ItemsSource="{Binding Images}" Position="{Binding Index}" />
</SpinePage>
```

Open it like any page:

```csharp
await navigation.NavigateToAsync<PhotoViewerPage, PhotoSet>(new(
    Photos.Select(p => new LightboxImage(p.Url, Tag: p.Key, Caption: p.Title)).ToList(),
    index));
```

- **`LightboxImage(Source, Tag, Caption)`:** `Source` is any `ImageSource` (a file, a URI, a stream). `Tag` is the thumbnail's `Transition.Tag`. `Caption` is shown under the photo.
- **`Position`** is two-way: it is the photo showing, and setting it pages to another one.
- **`IsChromeVisible`** is two-way: a tap on the photo hides the header bar, the title, the caption, the toolbar and the status bar, and the next tap brings them back. Bind to it to fade something of your own with them.

The attribute fixes how the page looks: the header bar lies over the photo, transparent with a white foreground; the status bar is light; the back button is an X. The page has no edge back-swipe, because a swipe from the left edge pages to the previous photo.

## Opening from the thumbnail

Give each thumbnail a tag of its own, and each `LightboxImage` the same tag:

```xml
<CollectionView ItemsSource="{Binding Photos}">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="Photo">
            <Image Source="{Binding Url}" Aspect="AspectFill" Transition.Tag="{Binding Key}" ... />
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

Spine makes the lightbox a [zoom](transitions.md#zoom) whose tag is the tag of the photo showing, and whose focus is the photo itself. So:

- **Opening:** the photo grows out of the thumbnail, the rest of it appearing around the square crop as it does, while the black fades in.
- **Paging:** the tag follows the photo, so closing lands on the thumbnail of the photo showing.
- **Closing** with the X, the system back or a drag down: the photo shrinks back into that thumbnail and the black fades out.
- **A thumbnail scrolled out of sight**, or photos without tags: the lightbox fades in and out instead, and a drag down carries the photo on down as it fades.

## Drag down to close

Drag a photo down and it follows the finger, the point you took hold of staying under it, and shrinks as it goes while the black fades. Let go past 100 points, or with a flick, and it flies into its thumbnail; otherwise it springs back. A photo zoomed in pans instead, and a pinch never closes.

## Share, Save and your own actions

The toolbar under the photo has **Share** at its leading edge and **Save** in the middle; the page's own `PageActionPlacement.Secondary` actions follow, at the trailing edge, as Photos has them:

```csharp
[PageAction(Svg = "info.svg")]
[RelayCommand]
private Task ShowInfo() => ...;
```

Turn the built-in ones off on the attribute: `[NavigableLightbox(Share = false, Save = false)]`.

- **Share** writes the photo showing to a file and opens the system's share sheet with it; on iOS the sheet shows the photo and its caption at the top, and has "Save Image".
- **Save** adds the photo showing to the photo library: a tick and a success haptic when it is in, an alert when it could not be saved.
  - **iOS and Mac Catalyst** ask for add-only access the first time. The app needs `NSPhotoLibraryAddUsageDescription` in its Info.plist; without it Spine leaves the button out and prints a `[Spine] Lightbox:` line once.

    ```xml
    <key>NSPhotoLibraryAddUsageDescription</key>
    <string>Saves the photo you are looking at to your library.</string>
    ```

  - **Android** writes to `Pictures/` through MediaStore, which needs no permission from Android 10. The button is not offered before that.

## In a tab, and in a sheet

- **In a tab**, the tab bar goes while the lightbox is in front and comes back as it closes.
- **From a sheet**, the same page covers the whole screen, the sheet too, as a photo opened from a sheet does in the system's own apps. It opens out of the thumbnail in the sheet, and the drag down, the X and the system back close it back into it, with the sheet still up. Nothing changes in the app: open it with `NavigateToAsync` from the sheet's view model as from any page.

---

## Platforms

| | iOS / Mac Catalyst | Android | Windows |
|---|---|---|---|
| Pages | A paging `UIScrollView` | A `RecyclerView` with a `PagerSnapHelper` | A `FlipView` |
| Zoom | A `UIScrollView` per photo | An `ImageView` with a matrix: pinch, double tap, pan, fling | A `ScrollViewer` per photo |
| Open and close | Zoom from and into the thumbnail | Zoom from and into the thumbnail | Fade |
| Drag down to close | Yes | Yes | — |
| Share / Save | Share sheet / Photos | Share sheet / MediaStore | — |
| Tab bar | Slides away | Goes, and fades back | — |

## See also

- [Shared Elements and Zoom](transitions.md): the zoom a lightbox is built on.
- [Page Actions](page-actions.md): the actions that go in the toolbar.
- [Navigation Parameters](navigation-parameters.md): handing the photos to the page.
