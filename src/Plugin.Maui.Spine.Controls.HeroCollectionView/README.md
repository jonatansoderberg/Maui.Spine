# Plugin.Maui.Spine.Controls.HeroCollectionView

`HeroCollectionView` is a `CollectionView` for .NET MAUI with a collapsing sticky header, an optional title overlay, and an adaptive colour-sampling overlay for dynamic theming. On Windows it doubles as the drag region for custom title-bar windows.

```bash
dotnet add package Plugin.Maui.Spine.Controls.HeroCollectionView
```

No registration is needed, with or without `UseSpine()`.

```xml
<HeroCollectionView
    ItemsSource="{Binding Items}"
    HeaderImageSource="header_bg.png"
    HeaderTitle="My Collection"
    HeaderTitleColor="White"
    HeaderMaxHeight="230"
    HeaderMinHeight="42">
    <HeroCollectionView.ItemTemplate>
        <DataTemplate x:DataType="vm:ItemViewModel">
            <Label Text="{Binding Name}" Padding="16,8" />
        </DataTemplate>
    </HeroCollectionView.ItemTemplate>
</HeroCollectionView>
```

`EnableAdaptiveOverlay` samples the header image as it scrolls and switches the overlay between `AdaptiveLightColor` and `AdaptiveDarkColor`.

Platforms: Android, iOS, Mac Catalyst, Windows.

## Documentation

- [HeroCollectionView](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/hero-collection-view.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
