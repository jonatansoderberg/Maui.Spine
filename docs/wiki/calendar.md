# Calendar

`Calendar` (package `Plugin.Maui.Spine.Controls.Calendar`) is a month calendar built from plain MAUI
views. Swipe or tap the arrows to change month; tap the title for the months of the year, and again
for the years of the decade. ISO 8601 week numbers run down the side, colours follow the app's
theme, and month and weekday names follow the culture.

## Setup

No registration is needed. The first calendar the app creates registers the calendar's own text
(the week column header and the words a screen reader speaks) with [strings](strings.md). The
package depends on `Plugin.Maui.Spine`.

In XAML the control is in the `Plugin.Maui.Spine.Controls` namespace, assembly
`Plugin.Maui.Spine.Controls.Calendar`; add it to the global namespace like the other controls:

```csharp
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global",
    "Plugin.Maui.Spine.Controls", AssemblyName = "Plugin.Maui.Spine.Controls.Calendar")]
```

In C#, a file that also imports `System.Globalization` sees two `Calendar` types. Alias the one you
mean:

```csharp
using Calendar = Plugin.Maui.Spine.Controls.Calendar;
```

## Usage

```xml
<ScrollView>
    <VerticalStackLayout Padding="20">
        <Calendar SelectedDate="{Binding Date}"
                  DisplayDate="{Binding Month}"
                  ShowWeekNumbers="True"
                  FirstDayOfWeek="Monday" />
    </VerticalStackLayout>
</ScrollView>
```

A calendar inside a `ScrollView` does not take the vertical scroll: a drag that starts on it scrolls
the page unless it is more horizontal than vertical.

## Properties

| Property | Type | Default | Notes |
|---|---|---|---|
| `SelectedDate` | `DateTime` | `MinValue` | TwoWay. `MinValue` = no selection. Setting it, by a tap or from a binding, shows its month. |
| `DisplayDate` | `DateTime` | the 1st of this month | TwoWay. Any day of the month on screen; navigation writes the 1st of the new month, so a host can load that month's data on change. |
| `Today` | `DateTime` | `DateTime.Today` when created | The day with the today ring. Bind it when the app defines "today" differently (UTC, a server clock) or runs past midnight. |
| `ShowSelectedDate` | `bool` | `true` | `false` = taps still set `SelectedDate`, but no selection is drawn. |
| `ShowTrailingDays` | `bool` | `true` | Days of the adjacent months, dimmed. `false` hides them and ignores taps there. |
| `ShowWeekNumbers` | `bool` | `true` | The ISO 8601 week column, taken from each row's Thursday, so it is right whatever `FirstDayOfWeek` is. |
| `FirstDayOfWeek` | `DayOfWeek` | `Monday` | |
| `Culture` | `CultureInfo?` | `null` | Month and weekday names. `null` = `SpineStrings.Current.Culture`, followed live: switching the app's language re-labels the calendar. |
| `StyleOptions` | `CalendarStyleOptions?` | `null` | Fonts and colours for this calendar; see Styling. |

## Look

- **Selected** — a filled accent circle with contrasting bold text.
- **Today** — an accent ring, always drawn, with bold accent text.
- **Today and selected** — the ring with a smaller fill inside it.
- **Year view** — the displayed month in an accent pill, the current month in a quieter highlight.
- **Decade view** — the ten years and the next two, dimmed; the displayed year in an accent pill,
  the current year highlighted.

## Styling

`CalendarStyleOptions` holds every font and colour. The calendar looks it up on every render:

1. the `StyleOptions` set on the calendar,
2. an application resource keyed `DefaultCalendarStyleOptions`,
3. the code defaults.

A colour left unset (`null`) comes from the next step and in the end from the theme, so setting one
colour on one calendar keeps the rest themed.

```xml
<!-- App.xaml: every calendar in the app -->
<CalendarStyleOptions x:Key="DefaultCalendarStyleOptions" AccentColor="#E4572E" DayFontSize="15" />

<!-- One calendar -->
<Calendar>
    <Calendar.StyleOptions>
        <CalendarStyleOptions WeekNumberBackgroundColor="#FFF1E6" />
    </Calendar.StyleOptions>
</Calendar>
```

| Property | Default | Used for |
|---|---|---|
| `FontFamily` | app default | every label |
| `DayFontSize` / `DayOfWeekFontSize` / `HeaderFontSize` / `PickerFontSize` / `WeekNumberFontSize` | 14 / 13 / 18 / 16 / 12 | |
| `AccentColor` | the app's accent (`SpineTheme.GetAccent`: `IThemeService.Accent`, else the `Primary` resource, `PrimaryDark` in dark mode when present), else system blue | arrows, today ring, selected fill, displayed month/year pill |
| `HeaderTextColor` | black / white | title |
| `DayTextColor` | black / white | day numbers, picker cells |
| `TodayTextColor` | accent | today's number |
| `SelectedTextColor` | white, or black on a light accent | text on the accent fill |
| `TrailingTextColor` | light grey / dark grey | adjacent-month days, next decade's years |
| `DayOfWeekTextColor` | system grey | weekday row |
| `CurrentHighlightColor` | accent blended into the background (opaque) | current month/year in the pickers |
| `WeekNumberBackgroundColor` / `WeekNumberTextColor` | system grouped grey / secondary text | week column |

Colours are assigned in code, so `AppThemeBinding` does not reach them. The calendar repaints
through `SpineTheme.Track` (see [theming](theming.md)) after every theme change and every culture
change; a `DefaultCalendarStyleOptions` resource with fixed colours stays fixed in both themes, so
leave colours unset where they should follow the theme. A change of `StyleOptions` itself rebuilds
the calendar, which is how fonts change.

## Text and accessibility

Month and weekday names come from the culture. The rest is in [strings](strings.md) under the
`Calendar.` prefix, in English and Swedish; the app overrides any key by defining it itself:

| Key | English |
|---|---|
| `Calendar.WeekHeader` | Wk |
| `Calendar.Week` | Week {0} |
| `Calendar.Today` / `Calendar.Selected` | Today / Selected |
| `Calendar.PreviousMonth` / `NextMonth` / `PreviousYear` / `NextYear` / `PreviousDecade` / `NextDecade` | the arrows, per view |
| `Calendar.ChooseMonth` / `Calendar.ChooseYear` | the title's hint in the month and year views |

These follow `SpineStrings.Current.Culture`, also when `Culture` pins the calendar to another culture.

A screen reader hears each day as its full date (the culture's long date pattern) plus "Today" and
"Selected" where they apply, week numbers as "Week 39", weekday headers by their full names, and
picker months with their year.

## Behaviour

- **One tap and one pan recognizer**, both on the grid host and none on the cells. A tap is resolved
  to the cell under it by position. Recognizers on the cells made a swipe from a day move two months
  on iOS and kept swipes from reaching the host on Android.
- **Axis lock after 10 units.** A drag that turns out vertical is left alone for the rest of the
  gesture. On iOS and Mac Catalyst the native pan refuses to begin unless the drag is more horizontal
  than vertical, because once it begins the scroll view around it never gets the touch.
- **Swipes follow the finger** with the neighbouring month beside the page, commit past a third of
  the width or on a fling, and a fling back cancels. The arrows play the same slide.
- **Cheap re-rendering.** Each view has two pages, the one on screen and its neighbour; a committed
  slide swaps them. The 42 day cells of a month page are built once. The year and decade pages are
  built the first time their view opens.
- **Supported years** run from 2 to 9998, so a month grid never leaves `DateTime`'s range.

## Platforms

Android, iOS, Mac Catalyst, Windows. On Mac Catalyst a click-drag swipes the month and wheel or
trackpad scrolling reaches the scroll view as usual. On Windows a mouse wheel scrolls the page as usual;
touch has not been verified there. MAUI's pan recognizer sets the element's manipulation mode on
Windows, which can keep a touch drag that starts on the calendar from scrolling a parent `ScrollView`.

## Not included

Minimum/maximum or disabled dates, range selection and event markers.
