using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Fonts, sizes, paddings, colours and row heights of a <see cref="DataGrid"/>. Resolved per render
/// through <see cref="SpineStyleOptions{TSelf}"/>: the grid's <see cref="DataGrid.StyleOptions"/>,
/// then an application resource keyed <c>DefaultDataGridStyleOptions</c>, then these defaults.
/// A colour left <see langword="null"/> follows the light or dark theme.
/// </summary>
public class DataGridStyleOptions : SpineStyleOptions<DataGridStyleOptions>
{
    private static bool IsPhone => DeviceInfo.Idiom == DeviceIdiom.Phone;

    /// <summary>Cell font; null is the platform font.</summary>
    public string? FontFamily { get; set; }

    /// <summary>Header font; null uses <see cref="FontFamily"/>.</summary>
    public string? HeaderFontFamily { get; set; }

    public double FontSize { get; set; } = IsPhone ? 14 : 15;

    public double HeaderFontSize { get; set; } = IsPhone ? 13 : 14;

    public FontAttributes HeaderFontAttributes { get; set; } = FontAttributes.Bold;

    // Row heights follow the Material list-item scale (one, two and three lines = 56, 72, 88), which
    // also clears Apple's 44 pt touch target.

    /// <summary>Row height of a one-row layout without an explicit <see cref="DataGridLayout.ItemHeight"/>.</summary>
    public double ItemHeight { get; set; } = 56;

    /// <summary>Added per extra sub-row: two rows are 72, three are 88.</summary>
    public double MultiRowItemExtraHeight { get; set; } = 16;

    /// <summary>Minimum height of a header row; sortable headers are touch targets.</summary>
    public double HeaderRowHeight { get; set; } = 48;

    public Thickness CellPadding { get; set; } = IsPhone ? new Thickness(10, 1, 4, 1) : new Thickness(12, 1, 4, 1);

    public Thickness HeaderCellPadding { get; set; } = IsPhone ? new Thickness(10, 6, 4, 6) : new Thickness(12, 6, 4, 6);

    public double StatusFontSize { get; set; } = IsPhone ? 12 : 13;

    public double CheckboxSize { get; set; } = 24;

    /// <summary>Size of the sort caret drawn after the sorted column's header.</summary>
    public double SortIndicatorSize { get; set; } = 10;

    // Header tooltip (long-press on a header) and the copied confirmation.

    /// <summary>How long a header or cell must be held. 500 ms is Android's own long-press timeout.</summary>
    public TimeSpan LongPressDuration { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How long the bubble stays up.</summary>
    public TimeSpan TooltipVisibleDuration { get; set; } = TimeSpan.FromSeconds(3);

    public double TooltipFontSize { get; set; } = IsPhone ? 13 : 14;

    public double TooltipCornerRadius { get; set; } = 8;

    public Thickness TooltipPadding { get; set; } = new(12, 8);

    // Grouping.

    public double GroupHeaderHeight { get; set; } = 48;

    public string? GroupHeaderFontFamily { get; set; }

    public double GroupHeaderFontSize { get; set; } = IsPhone ? 14 : 15;

    public FontAttributes GroupHeaderFontAttributes { get; set; } = FontAttributes.Bold;

    // Colours: null follows the theme.

    public Color? HeaderBackgroundColor { get; set; }
    public Color? HeaderTextColor { get; set; }
    public Color? RowBackgroundColor { get; set; }

    /// <summary>Every other row; striping separates the rows instead of lines.</summary>
    public Color? AlternatingRowBackgroundColor { get; set; }

    public Color? TextColor { get; set; }
    public Color? MutedTextColor { get; set; }
    public Color? LinkColor { get; set; }

    /// <summary>Checkboxes, the refresh and loading spinners.</summary>
    public Color? AccentColor { get; set; }

    public Color? SortIndicatorColor { get; set; }
    public Color? StatusTextColor { get; set; }
    public Color? SwipeActionBackgroundColor { get; set; }
    public Color? SwipeActionTextColor { get; set; }
    public Color? TooltipBackgroundColor { get; set; }
    public Color? TooltipTextColor { get; set; }
    public Color? GroupHeaderBackgroundColor { get; set; }
    public Color? GroupHeaderTextColor { get; set; }
    public Color? GroupChevronColor { get; set; }

    protected override void InheritColorsFrom(DataGridStyleOptions source)
    {
        HeaderBackgroundColor ??= source.HeaderBackgroundColor;
        HeaderTextColor ??= source.HeaderTextColor;
        RowBackgroundColor ??= source.RowBackgroundColor;
        AlternatingRowBackgroundColor ??= source.AlternatingRowBackgroundColor;
        TextColor ??= source.TextColor;
        MutedTextColor ??= source.MutedTextColor;
        LinkColor ??= source.LinkColor;
        AccentColor ??= source.AccentColor;
        SortIndicatorColor ??= source.SortIndicatorColor;
        StatusTextColor ??= source.StatusTextColor;
        SwipeActionBackgroundColor ??= source.SwipeActionBackgroundColor;
        SwipeActionTextColor ??= source.SwipeActionTextColor;
        TooltipBackgroundColor ??= source.TooltipBackgroundColor;
        TooltipTextColor ??= source.TooltipTextColor;
        GroupHeaderBackgroundColor ??= source.GroupHeaderBackgroundColor;
        GroupHeaderTextColor ??= source.GroupHeaderTextColor;
        GroupChevronColor ??= source.GroupChevronColor;
    }

    protected override void ApplyThemeDefaults(AppTheme theme)
    {
        var dark = theme == AppTheme.Dark;

        // Neutral greys close to the platform's grouped-list surfaces, and the system blue.
        var accent = dark ? Color.FromArgb("#0A84FF") : Color.FromArgb("#007AFF");
        var text = dark ? Colors.White : Colors.Black;
        var muted = dark ? Color.FromArgb("#98989F") : Color.FromArgb("#6C6C70");

        HeaderBackgroundColor ??= dark ? Color.FromArgb("#2C2C2E") : Color.FromArgb("#E9E9EE");
        HeaderTextColor ??= text;
        RowBackgroundColor ??= dark ? Color.FromArgb("#1C1C1E") : Colors.White;
        AlternatingRowBackgroundColor ??= dark ? Color.FromArgb("#242426") : Color.FromArgb("#F5F5F8");
        TextColor ??= text;
        MutedTextColor ??= muted;
        LinkColor ??= accent;
        AccentColor ??= accent;
        SortIndicatorColor ??= text;
        StatusTextColor ??= muted;
        SwipeActionBackgroundColor ??= accent;
        SwipeActionTextColor ??= Colors.White;
        TooltipBackgroundColor ??= dark ? Color.FromArgb("#E5E5EA") : Color.FromArgb("#3A3A3C");
        TooltipTextColor ??= dark ? Colors.Black : Colors.White;
        // Darker than the column header, so a group header flush against it stays a separate band.
        GroupHeaderBackgroundColor ??= dark ? Color.FromArgb("#3A3A3C") : Color.FromArgb("#D8D8DE");
        GroupHeaderTextColor ??= text;
        GroupChevronColor ??= text;
    }
}
