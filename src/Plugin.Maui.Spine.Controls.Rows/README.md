# Plugin.Maui.Spine.Controls.Rows

`SpineRow` is a settings and key/value row for .NET MAUI: an SVG icon (optionally on a coloured badge), a title and detail, a value, an accessory (a switch, a button), a check mark for a choice, a chevron, a pop-up menu (`Menu`), a status mark (`Status`) and a red title for what deletes (`IsDestructive`). `SpineEntryRow` is a text field as a row, without a frame of its own, with an eye button for a password. `SpineSection` groups rows with a header and footer the way each platform's Settings does: an inset group with hairline separators on iOS, plain rows under an accent header on Android. A tap anywhere runs its command with the platform's own press feedback (highlight on iOS, ripple on Android), and a screen reader reads the row as one element. It is part of [Spine](https://github.com/jonatansoderberg/Maui.Spine) and builds on `Tap.Command` and `Semantic.Merge` from `Plugin.Maui.Spine`.

```bash
dotnet add package Plugin.Maui.Spine.Controls.Rows
```

No registration is needed.

```xml
<SpineSection Header="General" Footer="Changes apply at once.">
    <SpineRow Icon="lamp.svg" IconBackground="#5856D6" Title="Appearance" Value="{Binding Theme}"
              Command="{Binding PickThemeCommand}" />

    <SpineRow Icon="bell.svg" IconBackground="#FF3B30" Title="Notifications">
        <SpineRow.Accessory>
            <Switch IsToggled="{Binding Notify}" />
        </SpineRow.Accessory>
    </SpineRow>
</SpineSection>

<SpineSection>
    <SpineRow Title="Version" Value="1.0" ValuePlacement="Trailing" />
</SpineSection>

<SpineSection Header="Account">
    <SpineEntryRow Text="{Binding Email}" Placeholder="Email" Keyboard="Email" />
    <SpineEntryRow Text="{Binding Password}" Placeholder="Password" IsPassword="True"
                   ReturnCommand="{Binding SignInCommand}" />
</SpineSection>
```

Platforms: Android, iOS, Mac Catalyst, Windows.

## Documentation

- [Rows and taps](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/rows.md)
- [Settings pages](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/settings-pages.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
