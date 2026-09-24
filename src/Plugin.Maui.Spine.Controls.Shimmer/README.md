# Plugin.Maui.Spine.Controls.Shimmer

Skeleton loading for .NET MAUI. `Shimmer` sweeps a wave across a placeholder layout; `Skeleton.IsActive` turns the real layout into its own skeleton while its data loads, so nothing jumps when the content arrives. Placeholder and wave colours follow the light/dark theme, and Reduce Motion gives static placeholders.

```bash
dotnet add package Plugin.Maui.Spine.Controls.Shimmer
```

No registration is needed: the controls register their default text the first time they are used.

```xml
<Shimmer IsLoading="{Binding IsLoading}" WaveWidth="0.4" WaveOpacity="0.25">
    <VerticalStackLayout Spacing="10">
        <Border StrokeThickness="0" HeightRequest="22" WidthRequest="300" HorizontalOptions="Start" />
        <Border StrokeThickness="0" HeightRequest="16" WidthRequest="200" HorizontalOptions="Start" />
    </VerticalStackLayout>
</Shimmer>

<VerticalStackLayout Skeleton.IsActive="{Binding IsLoading}">
    <Label Text="{Binding Name}" FontSize="20" />
    <Label Text="{Binding Bio}" Skeleton.Lines="3" />
</VerticalStackLayout>
```

Platforms: Android, iOS, Mac Catalyst, Windows.

## Documentation

- [Shimmer and Skeleton](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/shimmer.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
