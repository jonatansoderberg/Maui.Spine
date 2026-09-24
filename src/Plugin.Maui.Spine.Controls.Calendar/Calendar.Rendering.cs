using System.Globalization;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Controls;

public partial class Calendar
{
    // A month grid starts up to six days before the 1st and runs 42 days, so neither January of
    // year 1 nor December of 9999 can be drawn without leaving DateTime's range.
    private const int MinYear = 2;
    private const int MaxYear = 9998;

    // ── Navigation ──────────────────────────────────────────────────────────────

    /// <summary>The first of the month <paramref name="delta"/> pages away in the active view.</summary>
    private DateTime TargetDate(int delta) => _viewMode switch
    {
        CalendarViewMode.Month => WithMonthIndex(DisplayDate.Year * 12 + DisplayDate.Month - 1 + delta),
        CalendarViewMode.Year => WithYear(DisplayDate.Year + delta),
        _ => WithYear(DisplayDate.Year + delta * 10),
    };

    private bool IsDisplayed(DateTime date) => date.Year == DisplayDate.Year && date.Month == DisplayDate.Month;

    private DateTime WithYear(int year) => new(Math.Clamp(year, MinYear, MaxYear), DisplayDate.Month, 1);

    /// <summary>The first of the month <paramref name="index"/> (year × 12 + month − 1), clamped to the supported years.</summary>
    private static DateTime WithMonthIndex(int index)
    {
        index = Math.Clamp(index, MinYear * 12, MaxYear * 12 + 11);
        return new DateTime(index / 12, index % 12 + 1, 1);
    }

    /// <summary>A tap on the title drills up: month → year (months) → decade (years).</summary>
    private void OnTitleTapped()
    {
        if (_viewMode == CalendarViewMode.Decade)
            return;

        SetViewMode(_viewMode == CalendarViewMode.Month ? CalendarViewMode.Year : CalendarViewMode.Decade);
    }

    private void SetViewMode(CalendarViewMode mode)
    {
        CancelSlide();

        _viewMode = mode;
        EnsurePickerPages(mode);
        ResetPages();

        RenderActiveView();
    }

    /// <summary>Builds the pages of the year or decade view the first time that view opens.</summary>
    private void EnsurePickerPages(CalendarViewMode mode)
    {
        if (_contentHost is null)
            return;

        var style = CalendarStyleOptions.Resolve(StyleOptions);

        switch (mode)
        {
            case CalendarViewMode.Year when _year is null:
                _year = AddPickerPage(style);
                _yearPeek = AddPickerPage(style);
                break;
            case CalendarViewMode.Decade when _decade is null:
                _decade = AddPickerPage(style);
                _decadePeek = AddPickerPage(style);
                break;
        }
    }

    private PickerPage AddPickerPage(CalendarStyleOptions style)
    {
        var page = BuildPickerPage(style);
        _contentHost!.Add(page.Grid);
        return page;
    }

    private void OnMonthTapped(int month)
    {
        SetDisplayMonth(new DateTime(DisplayDate.Year, month, 1));
        SetViewMode(CalendarViewMode.Month);
    }

    private void OnDecadeYearTapped(int index)
    {
        SetDisplayMonth(WithYear(DisplayDate.Year - DisplayDate.Year % 10 + index));
        SetViewMode(CalendarViewMode.Year);
    }

    private void OnDayTapped(DayCell cell)
    {
        if (cell.IsTrailing && !ShowTrailingDays)
            return;

        SelectedDate = cell.Date;
    }

    /// <summary>Writes <see cref="DisplayDate"/> once, as the 1st of the month, when the month changes.</summary>
    private void SetDisplayMonth(DateTime firstOfMonth)
    {
        if (IsDisplayed(firstOfMonth))
            return;

        DisplayDate = firstOfMonth;
    }

    // ── Property changes ────────────────────────────────────────────────────────

    private void OnSelectedDateChanged()
    {
        // Selecting a day, by a tap on a trailing day or from a binding, brings its month into view.
        if (SelectedDate != DateTime.MinValue)
            SetDisplayMonth(FirstOfMonth(SelectedDate));

        RefreshDayVisuals();
    }

    private void OnDisplayDateChanged()
    {
        // Cheap when the month is unchanged; keeps the month current behind the pickers.
        RenderCurrentMonth();

        if (_viewMode == CalendarViewMode.Year && _year is not null) RenderYear(_year, DisplayDate);
        else if (_viewMode == CalendarViewMode.Decade && _decade is not null) RenderDecade(_decade, DisplayDate);

        UpdateHeader();
    }

    private void OnTodayChanged()
    {
        // Today's description differs, so the text is written again along with the ring.
        InvalidateText();
    }

    private void OnShowWeekNumbersChanged()
    {
        foreach (var page in new[] { _month, _monthPeek })
        {
            if (page is null)
                continue;

            page.Grid.ColumnDefinitions[0].Width = new GridLength(ShowWeekNumbers ? WeekColumnWidth : 0);
            page.WeekColumnBackground.IsVisible = ShowWeekNumbers;
            page.WeekHeader.IsVisible = ShowWeekNumbers;
            foreach (var weekLabel in page.WeekLabels)
                weekLabel.IsVisible = ShowWeekNumbers;
        }
    }

    /// <summary>Makes the month pages write their text again, then renders the view on screen.</summary>
    private void InvalidateText()
    {
        foreach (var page in new[] { _month, _monthPeek })
        {
            if (page is null)
                continue;

            page.Year = -1;
            page.Month = -1;
        }

        RenderActiveView();
    }

    // ── Rendering ───────────────────────────────────────────────────────────────

    private void RenderActiveView()
    {
        switch (_viewMode)
        {
            case CalendarViewMode.Month: RenderCurrentMonth(); break;
            case CalendarViewMode.Year when _year is not null: RenderYear(_year, DisplayDate); break;
            case CalendarViewMode.Decade when _decade is not null: RenderDecade(_decade, DisplayDate); break;
        }

        UpdateHeader();
    }

    private void RenderCurrentMonth()
    {
        if (_month is not null)
            RenderMonth(_month, DisplayDate.Year, DisplayDate.Month);
    }

    private void RenderMonth(MonthPage page, int year, int month)
    {
        if (year != page.Year || month != page.Month)
        {
            page.Year = year;
            page.Month = month;

            var culture = EffectiveCulture;
            var names = culture.DateTimeFormat.AbbreviatedDayNames;
            for (int col = 0; col < 7; col++)
            {
                var day = (DayOfWeek)(((int)FirstDayOfWeek + col) % 7);
                page.DayOfWeekLabels[col].Text = Capitalize(names[(int)day].TrimEnd('.'), culture);
                SemanticProperties.SetDescription(page.DayOfWeekLabels[col], culture.DateTimeFormat.GetDayName(day));
            }

            var strings = SpineStrings.Current;
            page.WeekHeader.Text = strings["Calendar.WeekHeader"];

            var gridStart = GridStart(year, month);

            for (int i = 0; i < 42; i++)
            {
                var cell = page.DayCells[i];
                cell.Date = gridStart.AddDays(i);
                cell.IsTrailing = cell.Date.Month != month;
                cell.Label.Text = cell.Date.Day.ToString(culture);
            }

            // ISO 8601 week of each row, from the row's Thursday: right whatever the first day of the
            // week is, also for a row that straddles two ISO weeks.
            for (int row = 0; row < 6; row++)
            {
                var week = ISOWeek.GetWeekOfYear(gridStart.AddDays(row * 7 + 3));
                page.WeekLabels[row].Text = week.ToString(culture);
                SemanticProperties.SetDescription(page.WeekLabels[row], strings.Get("Calendar.Week", week));
            }
        }

        EnsureMarks(page);
        RefreshDayVisuals(page);
    }

    private void RefreshDayVisuals()
    {
        if (_month is not null)
            RefreshDayVisuals(_month);
    }

    private void RefreshDayVisuals(MonthPage page)
    {
        if (page.Year < 0)
            return;

        var style = CalendarStyleOptions.Resolve(StyleOptions);
        var culture = EffectiveCulture;
        var strings = SpineStrings.Current;
        var today = Today.Date;
        var selected = SelectedDate.Date;
        var marks = page.Marks;
        bool dots = style.MarkStyle == CalendarMarkStyle.Dot;

        foreach (var cell in page.DayCells)
        {
            if (cell.IsTrailing && !ShowTrailingDays)
            {
                cell.Container.IsVisible = false;
                continue;
            }
            cell.Container.IsVisible = true;

            var date = cell.Date;
            bool isToday = date == today;
            bool isSelected = ShowSelectedDate && selected != DateTime.MinValue.Date && date == selected;
            bool isMarked = marks?.Contains(DateOnly.FromDateTime(date)) == true;
            bool markFill = isMarked && !dots;

            // Today and selected: the fill stays clear, and the ring and the inner fill make the look.
            // Today and marked: the mark fill inside the ring.
            cell.Fill.Color =
                isSelected ? (isToday ? Colors.Transparent : style.AccentColor)
                : markFill ? (cell.IsTrailing ? style.TrailingMarkFillColor : style.MarkFillColor)
                : Colors.Transparent;
            cell.TodayRing.IsVisible = isToday;
            cell.InnerFill.IsVisible = isToday && isSelected;

            // On a mark fill the number takes the marked colour even on today: the accent-coloured
            // today number reads poorly on an accent-tinted fill, and the ring still says "today".
            cell.Label.TextColor =
                isSelected ? style.SelectedTextColor
                : cell.IsTrailing ? style.TrailingTextColor
                : markFill ? style.MarkedTextColor
                : isToday ? style.TodayTextColor
                : style.DayTextColor;

            cell.Label.FontAttributes = isToday || isSelected ? FontAttributes.Bold : FontAttributes.None;

            if (isMarked && dots)
            {
                var dot = cell.Dot ??= AddDot(cell);
                dot.Color = isSelected ? style.SelectedTextColor
                    : cell.IsTrailing ? style.TrailingTextColor
                    : style.AccentColor;
                dot.IsVisible = true;
            }
            else if (cell.Dot is not null)
            {
                cell.Dot.IsVisible = false;
            }

            var description = date.ToString("D", culture);
            if (isToday)
                description += ", " + strings["Calendar.Today"];
            if (isSelected)
                description += ", " + strings["Calendar.Selected"];
            if (isMarked)
                description += ", " + strings["Calendar.Marked"];
            SemanticProperties.SetDescription(cell.Label, description);
        }
    }

    /// <summary>Year view: the 12 months of <paramref name="display"/>'s year.</summary>
    private void RenderYear(PickerPage page, DateTime display)
    {
        var style = CalendarStyleOptions.Resolve(StyleOptions);
        var culture = EffectiveCulture;
        var today = Today;

        for (int month = 1; month <= 12; month++)
        {
            var (_, fill, label) = page.Cells[month - 1];
            label.Text = Capitalize(culture.DateTimeFormat.GetMonthName(month), culture);
            SemanticProperties.SetDescription(label, Capitalize(new DateTime(display.Year, month, 1).ToString("Y", culture), culture));

            bool isDisplayed = month == display.Month;
            bool isCurrent = display.Year == today.Year && month == today.Month;

            fill.Color = isDisplayed ? style.AccentColor
                : isCurrent ? style.CurrentHighlightColor
                : Colors.Transparent;
            label.TextColor = isDisplayed ? style.SelectedTextColor : style.DayTextColor;
        }
    }

    /// <summary>Decade view: the 10 years of <paramref name="display"/>'s decade and the next two, dimmed.</summary>
    private void RenderDecade(PickerPage page, DateTime display)
    {
        var style = CalendarStyleOptions.Resolve(StyleOptions);
        var culture = EffectiveCulture;
        int decadeStart = display.Year - display.Year % 10;

        for (int index = 0; index < 12; index++)
        {
            var (_, fill, label) = page.Cells[index];
            int year = decadeStart + index;
            bool isTrailing = index >= 10;
            bool isDisplayed = year == display.Year;
            bool isCurrent = year == Today.Year;

            label.Text = year.ToString(culture);
            fill.Color = isDisplayed ? style.AccentColor
                : isCurrent ? style.CurrentHighlightColor
                : Colors.Transparent;
            label.TextColor = isDisplayed ? style.SelectedTextColor
                : isTrailing ? style.TrailingTextColor
                : style.DayTextColor;
        }
    }

    /// <summary>The title, and what the arrows and the title say to a screen reader, for the view on screen.</summary>
    private void UpdateHeader()
    {
        if (_titleLabel is null)
            return;

        var culture = EffectiveCulture;
        var strings = SpineStrings.Current;
        int decadeStart = DisplayDate.Year - DisplayDate.Year % 10;

        _titleLabel.Text = _viewMode switch
        {
            CalendarViewMode.Year => DisplayDate.Year.ToString(culture),
            CalendarViewMode.Decade => $"{decadeStart.ToString(culture)}–{(decadeStart + 9).ToString(culture)}",
            _ => Capitalize(DisplayDate.ToString("Y", culture), culture),
        };

        SemanticProperties.SetHint(_titleLabel, _viewMode switch
        {
            CalendarViewMode.Month => strings["Calendar.ChooseMonth"],
            CalendarViewMode.Year => strings["Calendar.ChooseYear"],
            _ => "",
        });

        var (previous, next) = _viewMode switch
        {
            CalendarViewMode.Year => ("Calendar.PreviousYear", "Calendar.NextYear"),
            CalendarViewMode.Decade => ("Calendar.PreviousDecade", "Calendar.NextDecade"),
            _ => ("Calendar.PreviousMonth", "Calendar.NextMonth"),
        };

        if (_previousButton is not null)
            SemanticProperties.SetDescription(_previousButton, strings[previous]);
        if (_nextButton is not null)
            SemanticProperties.SetDescription(_nextButton, strings[next]);
    }

    private static string Capitalize(string text, CultureInfo culture) =>
        text.Length == 0 || char.IsUpper(text[0]) ? text : char.ToUpper(text[0], culture) + text[1..];
}
