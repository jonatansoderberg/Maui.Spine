# Plugin.Maui.Spine.Controls.Calendar

`Calendar` is a month calendar for .NET MAUI built from plain MAUI views: swipe or tap the arrows to change month, tap the title for a year and a decade picker, ISO 8601 week numbers, days marked from your own source (`MarkSource`), and colours that follow the app's theme. It is part of [Spine](https://github.com/jonatansoderberg/Maui.Spine) and needs `Plugin.Maui.Spine`.

```bash
dotnet add package Plugin.Maui.Spine.Controls.Calendar
```

No registration is needed: the calendar registers its own text the first time it is used.

```xml
<Calendar SelectedDate="{Binding Date}" ShowWeekNumbers="True" FirstDayOfWeek="Monday" />
```

Platforms: Android, iOS, Mac Catalyst, Windows.

## Documentation

- [Calendar](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/calendar.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
