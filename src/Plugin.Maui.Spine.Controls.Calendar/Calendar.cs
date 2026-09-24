using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A month calendar built from plain MAUI views: a 7×6 day grid with ISO week numbers, a year view
/// and a decade view reached by tapping the title, and horizontal swipes that follow the finger.
/// </summary>
/// <remarks>
/// <para>
/// Day cells: <i>selected</i> is a filled accent circle, <i>today</i> an accent ring that is always
/// drawn, and <i>today and selected</i> the ring with a smaller fill inside it.
/// </para>
/// <para>
/// Every view has two pages, the one on screen and the neighbour that slides in beside it; all 42 day
/// cells of a month page are built once, and navigating or selecting only restyles them. Colours are
/// assigned in code, so the calendar repaints through <see cref="SpineTheme.Track"/> after a theme or
/// culture change.
/// </para>
/// </remarks>
public partial class Calendar : ContentView
{
    public static readonly BindableProperty SelectedDateProperty = BindableProperty.Create(
        nameof(SelectedDate), typeof(DateTime), typeof(Calendar), DateTime.MinValue, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((Calendar)b).OnSelectedDateChanged());

    public static readonly BindableProperty DisplayDateProperty = BindableProperty.Create(
        nameof(DisplayDate), typeof(DateTime), typeof(Calendar), default(DateTime), BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((Calendar)b).OnDisplayDateChanged(),
        defaultValueCreator: _ => FirstOfMonth(DateTime.Today));

    public static readonly BindableProperty TodayProperty = BindableProperty.Create(
        nameof(Today), typeof(DateTime), typeof(Calendar), default(DateTime),
        propertyChanged: (b, _, _) => ((Calendar)b).OnTodayChanged(),
        defaultValueCreator: _ => DateTime.Today);

    public static readonly BindableProperty ShowSelectedDateProperty = BindableProperty.Create(
        nameof(ShowSelectedDate), typeof(bool), typeof(Calendar), true,
        propertyChanged: (b, _, _) => ((Calendar)b).RefreshDayVisuals());

    public static readonly BindableProperty ShowTrailingDaysProperty = BindableProperty.Create(
        nameof(ShowTrailingDays), typeof(bool), typeof(Calendar), true,
        propertyChanged: (b, _, _) => ((Calendar)b).RefreshDayVisuals());

    public static readonly BindableProperty ShowWeekNumbersProperty = BindableProperty.Create(
        nameof(ShowWeekNumbers), typeof(bool), typeof(Calendar), true,
        propertyChanged: (b, _, _) => ((Calendar)b).OnShowWeekNumbersChanged());

    public static readonly BindableProperty FirstDayOfWeekProperty = BindableProperty.Create(
        nameof(FirstDayOfWeek), typeof(DayOfWeek), typeof(Calendar), DayOfWeek.Monday,
        propertyChanged: (b, _, _) => ((Calendar)b).InvalidateText());

    public static readonly BindableProperty CultureProperty = BindableProperty.Create(
        nameof(Culture), typeof(CultureInfo), typeof(Calendar), null,
        propertyChanged: (b, _, _) => ((Calendar)b).InvalidateText());

    public static readonly BindableProperty StyleOptionsProperty = BindableProperty.Create(
        nameof(StyleOptions), typeof(CalendarStyleOptions), typeof(Calendar), null,
        propertyChanged: (b, _, _) => ((Calendar)b).BuildVisualTree());

    /// <summary>The selected day. <see cref="DateTime.MinValue"/> = none. Setting it shows its month.</summary>
    public DateTime SelectedDate
    {
        get => (DateTime)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>
    /// Any day of the month on screen. Navigation writes the 1st of the new month, so a host can load
    /// that month's data when it changes.
    /// </summary>
    public DateTime DisplayDate
    {
        get => (DateTime)GetValue(DisplayDateProperty);
        set => SetValue(DisplayDateProperty, value);
    }

    /// <summary>The day drawn with the today ring. Defaults to <see cref="DateTime.Today"/> when the calendar is created.</summary>
    public DateTime Today
    {
        get => (DateTime)GetValue(TodayProperty);
        set => SetValue(TodayProperty, value);
    }

    /// <summary><see langword="false"/> = taps still set <see cref="SelectedDate"/>, but no selection is drawn.</summary>
    public bool ShowSelectedDate
    {
        get => (bool)GetValue(ShowSelectedDateProperty);
        set => SetValue(ShowSelectedDateProperty, value);
    }

    /// <summary>Shows the days of the adjacent months, dimmed, in the first and last rows.</summary>
    public bool ShowTrailingDays
    {
        get => (bool)GetValue(ShowTrailingDaysProperty);
        set => SetValue(ShowTrailingDaysProperty, value);
    }

    /// <summary>Shows the ISO 8601 week-number column.</summary>
    public bool ShowWeekNumbers
    {
        get => (bool)GetValue(ShowWeekNumbersProperty);
        set => SetValue(ShowWeekNumbersProperty, value);
    }

    public DayOfWeek FirstDayOfWeek
    {
        get => (DayOfWeek)GetValue(FirstDayOfWeekProperty);
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    /// <summary>Culture of the weekday and month names. <see langword="null"/> = <see cref="SpineStrings.Culture"/>, followed live.</summary>
    public CultureInfo? Culture
    {
        get => (CultureInfo?)GetValue(CultureProperty);
        set => SetValue(CultureProperty, value);
    }

    /// <summary>Fonts and colours for this calendar; see <see cref="CalendarStyleOptions"/>.</summary>
    public CalendarStyleOptions? StyleOptions
    {
        get => (CalendarStyleOptions?)GetValue(StyleOptionsProperty);
        set => SetValue(StyleOptionsProperty, value);
    }

    private const double WeekColumnWidth = 30;
    private const double DayOfWeekRowHeight = 32;
    private const double DayRowHeight = 40;
    private const double DayCircleSize = 36;
    private const double InnerCircleSize = 28;
    private const double PickerRowHeight = 64;

    // Every BoxView sets its background: the MAUI template styles BoxView.BackgroundColor app-wide,
    // which would paint a square behind each round fill.
    private sealed class DayCell
    {
        public required Grid Container;
        public required BoxView Fill;
        public required Ellipse TodayRing;
        public required BoxView InnerFill;
        public required Label Label;
        public DateTime Date;
        public bool IsTrailing;
    }

    /// <summary>One month: 42 day cells, the week-number column and the weekday row.</summary>
    private sealed class MonthPage
    {
        public required Grid Grid;
        public required Border WeekColumnBackground;
        public required Label WeekHeader;
        public readonly List<DayCell> DayCells = new(42);
        public readonly List<Label> WeekLabels = new(6);
        public readonly List<Label> DayOfWeekLabels = new(7);

        // The month the cells show; -1 makes the next render write the text again.
        public int Year = -1;
        public int Month = -1;
    }

    /// <summary>A 3×4 picker: the year view's months or the decade view's years.</summary>
    private sealed class PickerPage
    {
        public required Grid Grid;
        public readonly List<(Grid Cell, BoxView Fill, Label Label)> Cells = new(12);
    }

    private enum CalendarViewMode { Month, Year, Decade }

    private Grid? _root;
    private Grid? _contentHost;
    private Label? _titleLabel;
    private Grid? _previousButton;
    private Grid? _nextButton;
    private Microsoft.Maui.Controls.Shapes.Path? _previousChevron;
    private Microsoft.Maui.Controls.Shapes.Path? _nextChevron;

    // Every view has two pages: the one on screen and the neighbour that slides in beside it. A
    // committed slide swaps them, so the page that slid in is not rendered again.
    private MonthPage? _month;
    private MonthPage? _monthPeek;
    private PickerPage? _year;
    private PickerPage? _yearPeek;
    private PickerPage? _decade;
    private PickerPage? _decadePeek;

    private CalendarViewMode _viewMode = CalendarViewMode.Month;

    private CultureInfo EffectiveCulture => Culture ?? SpineStrings.Current.Culture;

    public Calendar()
    {
        BuildVisualTree();

        // A theme or culture change repaints the views that exist rather than building new ones.
        SpineTheme.Track(this, Repaint);
    }

    private static DateTime FirstOfMonth(DateTime date) => new(date.Year, date.Month, 1);

    // ── Visual tree ─────────────────────────────────────────────────────────────

    private void BuildVisualTree()
    {
        CancelSlide();

        var style = CalendarStyleOptions.Resolve(StyleOptions);

        _root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            RowSpacing = 0,
        };

        _root.Add(BuildHeader(style), 0, 0);

        _month = BuildMonthPage(style);
        _monthPeek = BuildMonthPage(style);

        // The picker pages are built the first time their view opens (EnsurePickerPages): most
        // calendars are never drilled up, and the four of them are some 150 views.
        _year = _yearPeek = _decade = _decadePeek = null;

        // Clipped, so the neighbour waiting beside the page never draws outside the calendar.
        _contentHost = new Grid { IsClippedToBounds = true, BackgroundColor = Colors.Transparent };
        foreach (var page in AllPages())
            _contentHost.Add(page);
        AttachGestures(_contentHost);
        _root.Add(_contentHost, 0, 1);

        Content = _root;

        // A rebuild while picking a year stays in the year view.
        SetViewMode(_viewMode);
    }

    private IEnumerable<Grid> AllPages()
    {
        if (_month is not null) yield return _month.Grid;
        if (_monthPeek is not null) yield return _monthPeek.Grid;
        if (_year is not null) yield return _year.Grid;
        if (_yearPeek is not null) yield return _yearPeek.Grid;
        if (_decade is not null) yield return _decade.Grid;
        if (_decadePeek is not null) yield return _decadePeek.Grid;
    }

    /// <summary>
    /// Applies the resolved colours to the views that exist and writes the text again. The
    /// <see cref="SpineTheme.Track"/> callback: it runs after a theme change and after a culture change.
    /// </summary>
    /// <remarks>
    /// Fonts are left alone; they come from <see cref="StyleOptions"/>, and a change of that rebuilds.
    /// Rebuilding for a colour change would re-create some 300 views.
    /// </remarks>
    private void Repaint()
    {
        if (_root is null)
            return;

        var style = CalendarStyleOptions.Resolve(StyleOptions);

        if (_titleLabel is not null) _titleLabel.TextColor = style.HeaderTextColor;
        if (_previousChevron is not null) _previousChevron.Stroke = new SolidColorBrush(style.AccentColor);
        if (_nextChevron is not null) _nextChevron.Stroke = new SolidColorBrush(style.AccentColor);

        foreach (var page in new[] { _month, _monthPeek })
        {
            if (page is null)
                continue;

            page.WeekColumnBackground.BackgroundColor = style.WeekNumberBackgroundColor;
            page.WeekHeader.TextColor = style.WeekNumberTextColor;

            foreach (var weekLabel in page.WeekLabels)
                weekLabel.TextColor = style.WeekNumberTextColor;

            foreach (var dayOfWeekLabel in page.DayOfWeekLabels)
                dayOfWeekLabel.TextColor = style.DayOfWeekTextColor;

            foreach (var cell in page.DayCells)
            {
                cell.TodayRing.Stroke = new SolidColorBrush(style.AccentColor);
                cell.InnerFill.Color = style.AccentColor;
            }
        }

        // The culture may have changed with it (Culture null follows SpineStrings), and the rest of
        // the colours depend on state and are worked out on each render.
        InvalidateText();
    }

    private Grid BuildHeader(CalendarStyleOptions style)
    {
        var header = new Grid
        {
            Padding = new Thickness(4, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        (_previousButton, _previousChevron) = BuildNavButton(style, "M 8 1 L 2 8 L 8 15", () => Navigate(-1));
        header.Add(_previousButton, 0, 0);

        _titleLabel = new Label
        {
            FontAttributes = FontAttributes.Bold,
            FontSize = style.HeaderFontSize,
            FontFamily = style.FontFamily,
            TextColor = style.HeaderTextColor,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        var titleTap = new TapGestureRecognizer();
        titleTap.Tapped += (_, _) => OnTitleTapped();
        _titleLabel.GestureRecognizers.Add(titleTap);
        header.Add(_titleLabel, 1, 0);

        (_nextButton, _nextChevron) = BuildNavButton(style, "M 2 1 L 8 8 L 2 15", () => Navigate(1));
        header.Add(_nextButton, 2, 0);

        return header;
    }

    private static (Grid Button, Microsoft.Maui.Controls.Shapes.Path Chevron) BuildNavButton(CalendarStyleOptions style, string data, Action onTapped)
    {
        var chevron = new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(data)!,
            Stroke = new SolidColorBrush(style.AccentColor),
            StrokeThickness = 2.2,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            WidthRequest = 10,
            HeightRequest = 16,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };

        var button = new Grid { WidthRequest = 44, HeightRequest = 44, BackgroundColor = Colors.Transparent };
        button.Add(chevron);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTapped();
        button.GestureRecognizers.Add(tap);

        return (button, chevron);
    }

    private MonthPage BuildMonthPage(CalendarStyleOptions style)
    {
        var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };

        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(ShowWeekNumbers ? WeekColumnWidth : 0)));
        for (int col = 0; col < 7; col++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        grid.RowDefinitions.Add(new RowDefinition(new GridLength(DayOfWeekRowHeight)));
        for (int row = 0; row < 6; row++)
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(DayRowHeight)));

        // One rounded band behind all six week numbers.
        var weekColumnBackground = new Border
        {
            BackgroundColor = style.WeekNumberBackgroundColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Margin = new Thickness(0, 2),
            IsVisible = ShowWeekNumbers,
        };
        grid.Add(weekColumnBackground, 0, 1);
        Grid.SetRowSpan(weekColumnBackground, 6);

        var weekHeader = new Label
        {
            FontSize = style.WeekNumberFontSize,
            FontFamily = style.FontFamily,
            TextColor = style.WeekNumberTextColor,
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = ShowWeekNumbers,
        };
        grid.Add(weekHeader, 0, 0);

        var page = new MonthPage { Grid = grid, WeekColumnBackground = weekColumnBackground, WeekHeader = weekHeader };

        for (int row = 0; row < 6; row++)
        {
            var weekLabel = new Label
            {
                FontSize = style.WeekNumberFontSize,
                FontFamily = style.FontFamily,
                TextColor = style.WeekNumberTextColor,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                IsVisible = ShowWeekNumbers,
            };
            page.WeekLabels.Add(weekLabel);
            grid.Add(weekLabel, 0, row + 1);
        }

        for (int col = 0; col < 7; col++)
        {
            var dayOfWeekLabel = new Label
            {
                FontAttributes = FontAttributes.Bold,
                FontSize = style.DayOfWeekFontSize,
                FontFamily = style.FontFamily,
                TextColor = style.DayOfWeekTextColor,
                LineBreakMode = LineBreakMode.NoWrap,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            page.DayOfWeekLabels.Add(dayOfWeekLabel);
            grid.Add(dayOfWeekLabel, col + 1, 0);
        }

        for (int row = 0; row < 6; row++)
        {
            for (int col = 0; col < 7; col++)
            {
                var cell = BuildDayCell(style);
                page.DayCells.Add(cell);
                grid.Add(cell.Container, col + 1, row + 1);
            }
        }

        return page;
    }

    private static DayCell BuildDayCell(CalendarStyleOptions style)
    {
        var fill = new BoxView
        {
            CornerRadius = DayCircleSize / 2,
            HeightRequest = DayCircleSize,
            WidthRequest = DayCircleSize,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Color = Colors.Transparent,
            BackgroundColor = Colors.Transparent,
        };

        var todayRing = new Ellipse
        {
            Fill = Brush.Transparent,
            HeightRequest = DayCircleSize,
            WidthRequest = DayCircleSize,
            Stroke = new SolidColorBrush(style.AccentColor),
            StrokeThickness = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
        };

        // Only on today when it is selected: the gap between it and the ring is the ring-and-fill look.
        var innerFill = new BoxView
        {
            CornerRadius = InnerCircleSize / 2,
            HeightRequest = InnerCircleSize,
            WidthRequest = InnerCircleSize,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Color = style.AccentColor,
            BackgroundColor = Colors.Transparent,
            IsVisible = false,
        };

        var label = new Label
        {
            FontSize = style.DayFontSize,
            FontFamily = style.FontFamily,
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        // No gesture recognizer of its own: the content host resolves a tap to the cell under it.
        var container = new Grid { BackgroundColor = Colors.Transparent };
        container.Add(fill);
        container.Add(todayRing);
        container.Add(innerFill);
        container.Add(label);

        return new DayCell { Container = container, Fill = fill, TodayRing = todayRing, InnerFill = innerFill, Label = label };
    }

    private static PickerPage BuildPickerPage(CalendarStyleOptions style)
    {
        var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0, Padding = new Thickness(0, 8), IsVisible = false };

        for (int col = 0; col < 3; col++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int row = 0; row < 4; row++)
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(PickerRowHeight)));

        var page = new PickerPage { Grid = grid };

        for (int index = 0; index < 12; index++)
        {
            var fill = new BoxView
            {
                CornerRadius = 18,
                HeightRequest = 36,
                Margin = new Thickness(8, 0),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Center,
                Color = Colors.Transparent,
            BackgroundColor = Colors.Transparent,
            };

            var label = new Label
            {
                FontAttributes = FontAttributes.Bold,
                FontSize = style.PickerFontSize,
                FontFamily = style.FontFamily,
                LineBreakMode = LineBreakMode.NoWrap,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };

            var cell = new Grid { BackgroundColor = Colors.Transparent };
            cell.Add(fill);
            cell.Add(label);

            page.Cells.Add((cell, fill, label));
            grid.Add(cell, index % 3, index / 3);
        }

        return page;
    }
}
