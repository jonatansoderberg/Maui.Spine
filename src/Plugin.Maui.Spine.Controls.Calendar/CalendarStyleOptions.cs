using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Fonts and colours of a <see cref="Calendar"/>. Set on one calendar through
/// <see cref="Calendar.StyleOptions"/>, or app-wide as a resource keyed
/// <c>DefaultCalendarStyleOptions</c>; see <see cref="SpineStyleOptions{TSelf}"/> for the chain.
/// </summary>
/// <remarks>
/// A colour left <see langword="null"/> follows the theme. The accent defaults to the app's accent
/// (<see cref="SpineTheme.GetAccent"/>: <see cref="IThemeService.Accent"/>, else the <c>Primary</c> /
/// <c>PrimaryDark</c> colour resources) and to the system blue when the app has none. The mark
/// fills are the accent blended into the background, opaque, so they follow an accent change.
/// </remarks>
public class CalendarStyleOptions : SpineStyleOptions<CalendarStyleOptions>
{
    /// <summary>Font of every label in the calendar. <see langword="null"/> = the app's default font.</summary>
    public string? FontFamily { get; set; }

    public double DayFontSize { get; set; } = 14;
    public double DayOfWeekFontSize { get; set; } = 13;
    public double HeaderFontSize { get; set; } = 18;
    public double PickerFontSize { get; set; } = 16;
    public double WeekNumberFontSize { get; set; } = 12;

    /// <summary>Navigation arrows, the today ring, the selected-day fill and the displayed month or year in the pickers.</summary>
    public Color? AccentColor { get; set; }

    /// <summary>The month, year or decade title.</summary>
    public Color? HeaderTextColor { get; set; }

    /// <summary>Day numbers that are neither today nor selected, and the picker cells.</summary>
    public Color? DayTextColor { get; set; }

    /// <summary>Today's number when it is neither selected nor on a mark fill.</summary>
    public Color? TodayTextColor { get; set; }

    /// <summary>Text on the accent fill: the selected day and the displayed month or year.</summary>
    public Color? SelectedTextColor { get; set; }

    /// <summary>Days of the adjacent months, and the two years of the next decade in the decade view.</summary>
    public Color? TrailingTextColor { get; set; }

    public Color? DayOfWeekTextColor { get; set; }

    /// <summary>The pill behind the current month (year view) and the current year (decade view).</summary>
    public Color? CurrentHighlightColor { get; set; }

    public Color? WeekNumberBackgroundColor { get; set; }
    public Color? WeekNumberTextColor { get; set; }

    /// <summary>How a day that <see cref="Calendar.MarkSource"/> marks is drawn.</summary>
    public CalendarMarkStyle MarkStyle { get; set; } = CalendarMarkStyle.Fill;

    /// <summary>The circle behind a marked day of the displayed month (<see cref="CalendarMarkStyle.Fill"/>).</summary>
    public Color? MarkFillColor { get; set; }

    /// <summary>The circle behind a marked day of an adjacent month: fainter, like the day's text.</summary>
    public Color? TrailingMarkFillColor { get; set; }

    /// <summary>The number of a marked day on its fill (today too, inside its ring), unless it is selected.</summary>
    public Color? MarkedTextColor { get; set; }

    protected override void InheritColorsFrom(CalendarStyleOptions source)
    {
        AccentColor ??= source.AccentColor;
        HeaderTextColor ??= source.HeaderTextColor;
        DayTextColor ??= source.DayTextColor;
        TodayTextColor ??= source.TodayTextColor;
        SelectedTextColor ??= source.SelectedTextColor;
        TrailingTextColor ??= source.TrailingTextColor;
        DayOfWeekTextColor ??= source.DayOfWeekTextColor;
        CurrentHighlightColor ??= source.CurrentHighlightColor;
        WeekNumberBackgroundColor ??= source.WeekNumberBackgroundColor;
        WeekNumberTextColor ??= source.WeekNumberTextColor;
        MarkFillColor ??= source.MarkFillColor;
        TrailingMarkFillColor ??= source.TrailingMarkFillColor;
        MarkedTextColor ??= source.MarkedTextColor;
    }

    protected override void ApplyThemeDefaults(AppTheme theme)
    {
        bool dark = theme == AppTheme.Dark;

        var accent = AccentColor ??= SpineTheme.GetAccent(theme) ?? Color.FromArgb(dark ? "#0A84FF" : "#007AFF");
        var text = dark ? Colors.White : Colors.Black;

        HeaderTextColor ??= text;
        DayTextColor ??= text;
        TodayTextColor ??= accent;
        SelectedTextColor ??= SpineAccent.TextOn(accent);
        TrailingTextColor ??= Color.FromArgb(dark ? "#5A5A5E" : "#C7C7CC");
        DayOfWeekTextColor ??= Color.FromArgb("#8E8E93");

        // Opaque on purpose: a BoxView with an alpha colour paints over black on iOS.
        CurrentHighlightColor ??= Blend(accent, dark ? Colors.Black : Colors.White, dark ? 0.35 : 0.18);

        // Blended into a raised dark grey rather than black, so in dark mode a mark reads lighter
        // than the surface it sits on, not as a hole in it.
        var markBackground = dark ? Color.FromArgb("#2C2C2E") : Colors.White;
        MarkFillColor ??= Blend(accent, markBackground, dark ? 0.42 : 0.18);
        TrailingMarkFillColor ??= Blend(accent, markBackground, dark ? 0.15 : 0.08);
        MarkedTextColor ??= text;

        WeekNumberBackgroundColor ??= Color.FromArgb(dark ? "#2C2C2E" : "#F2F2F7");
        WeekNumberTextColor ??= Color.FromArgb(dark ? "#AEAEB2" : "#6C6C70");
    }

    private static Color Blend(Color color, Color background, double amount) => new(
        (float)(background.Red + (color.Red - background.Red) * amount),
        (float)(background.Green + (color.Green - background.Green) * amount),
        (float)(background.Blue + (color.Blue - background.Blue) * amount));
}
