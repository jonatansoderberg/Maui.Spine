# Plugin.Maui.Spine.Controls.Rows

`SpineRow` is a settings and key/value row for .NET MAUI: an SVG icon, a title and detail, a value, an accessory (a switch, a button) and a chevron. A tap anywhere runs its command with the platform's own press feedback (highlight on iOS, ripple on Android), and a screen reader reads the row as one element. It is part of [Spine](https://github.com/jonatansoderberg/Maui.Spine) and builds on `Tap.Command` and `Semantic.Merge` from `Plugin.Maui.Spine`.

```bash
dotnet add package Plugin.Maui.Spine.Controls.Rows
```

No registration is needed.

```xml
<SpineRow Icon="lamp.svg" Title="Appearance" Value="{Binding Theme}" Command="{Binding PickThemeCommand}" />

<SpineRow Icon="bell.svg" Title="Notifications">
    <SpineRow.Accessory>
        <Switch IsToggled="{Binding Notify}" />
    </SpineRow.Accessory>
</SpineRow>

<SpineRow Title="Version" Value="1.0" />
```

Platforms: Android, iOS, Mac Catalyst, Windows.

## Documentation

- [Rows and taps](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/rows.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
