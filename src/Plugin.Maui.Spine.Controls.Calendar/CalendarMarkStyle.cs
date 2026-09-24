namespace Plugin.Maui.Spine.Controls;

/// <summary>How a <see cref="Calendar"/> draws a day that its <see cref="Calendar.MarkSource"/> marks.</summary>
public enum CalendarMarkStyle
{
    /// <summary>A soft accent circle behind the number (<see cref="CalendarStyleOptions.MarkFillColor"/>).</summary>
    Fill,

    /// <summary>A small accent dot under the number, which stays visible on today and the selected day.</summary>
    Dot,
}
