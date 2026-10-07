# Shared Elements and Zoom

`Transition.Tag` links a view on one page to a view on the page navigated to. When Spine pushes or pops between them, it carries the view from one page to the other: as a **shared element** that flies between them while the pages slide, or as a **zoom** in which the page itself grows out of the view and shrinks back into it. It needs one attached property on each side and nothing to register. The attached property is in `Plugin.Maui.Spine.Extensions`.

---

## Shared element

Give the view on each page the same tag. A list matches the row whose tag the page arriving has, so make the tag unique per item, such as the item's id:

```xml
<!-- The list -->
<CollectionView ItemsSource="{Binding Posters}">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="Poster">
            <Image Source="{Binding Url}" Transition.Tag="{Binding Key}" />
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>

<!-- The page it opens -->
<Image Source="{Binding Poster.Url}" Transition.Tag="{Binding Poster.Key}" />
```

On the push, a picture of the view flies from its place in the list to its place on the page, while the pages move as `ISpineTransitions` moves them. The pop flies it back. If the list scrolled meanwhile, the view is found again by its tag.

- The picture of the view as it lands fades in over the picture it left as, and its corners turn from the one view's into the other's. A `Border` with a `RoundRectangle` stroke shape gives its radius, and its background fills the corners its shape cuts off.
- The flight uses the duration and easing of the region's `ISpineTransitions` (`InteractiveGestureDuration`, `InteractiveGestureEasing`), so a custom transition keeps its pace.
- The interactive back-swipe slides the page away as usual, without a flight.

## Zoom

Put the tag on the page itself, and the whole page grows out of the view with that tag on the page under it:

```xml
<!-- The grid -->
<Border Transition.Tag="{Binding Key}" ... />

<!-- The page it opens -->
<SpinePage ...
    Transition.Tag="{Binding Tile.Key}">

    <!-- The same tag on a view inside the page: its focus -->
    <Border Transition.Tag="{Binding Tile.Key}" ... />
```

- **Push:** the page starts cut to the view's place and corners and grows to the full page with a no-bounce spring curve (450 ms). The page under it stays where it is and dims, and the header bar fades in.
- **Pop** (back button or `BackAsync`): the page shrinks back into the view, found again by its tag.
- **Back-swipe:** the page shrinks and rounds its corners as the finger moves, and follows it. Let go past a third of the width and it zooms into the view; otherwise it springs back to full size.
- **Focus:** the same tag on a view inside the page (a poster, a card) makes that view the part of the page that lines up with the other view. The page grows out of it and shrinks into it as that view, and the view's picture travels with it, fading over the whole zoom, so the little that differs between the two (their padding, their text) never shows as a jump. Without a focus, the page's middle lines up and the view's picture fades in only as the page lands. Prefer a focus whenever the page shows what was tapped.

## When the usual transition plays

- **The other view is not on screen**, for example a list row scrolled out of sight. The page moves as usual rather than flying or zooming into a place that is not there.
- **Reduce Motion** is on.
- **The page opens as a sheet, or the user switches tabs.** Tags pair up within one navigation stack: a region, a tab or a sheet's own stack.
- **`SetRootAsync`, or `PopToAsync` across several pages.**

---

## Platforms

| | iOS / Mac Catalyst | Android | Windows |
|---|---|---|---|
| Shared element | Pictures from the views, moved with Core Animation | Bitmaps from the views, moved with a `ValueAnimator` | The usual transition |
| Zoom | The front layer's content scaled with its `sublayerTransform`, cut by a `CALayer` mask | The front layer scaled about its centre, cut by a rounded outline | The usual transition |
| Back-swipe zoom | Yes | Yes | — |

Spine pages are views in one region, not view controllers or fragments, so the platforms' own page transitions have nothing to attach to. Spine moves the views itself, and it is the same on iOS 17 as on 26.

On a push, Spine waits for the page arriving to settle before it measures it: one turn of the main queue on iOS, the next layout pass on Android. A list's inset under the header bar comes from its `Loaded` event, which moves every view in it. The page's layer is hidden meanwhile, so the page that is still showing is all there is to see.

## See also

- [Custom Transitions](custom-transitions.md): the page motion a shared element flies over, and how to replace it.
- [Regions](regions.md#interactive-back-swipe-gesture): the back-swipe gesture.
