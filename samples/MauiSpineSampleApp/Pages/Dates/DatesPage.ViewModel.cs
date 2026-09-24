using System.Globalization;
using MauiSpineSampleApp.Pages.Theme;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Pages.Dates;

public partial class DatesPageViewModel(IThemeService _theme, ISpineStrings _strings) : ViewModelBase
{
    // Both repaint the calendar while it is on screen, through SpineTheme.Track.
    [ObservableProperty]
    public partial string ThemeName { get; set; } = ThemePageViewModel.ThemeToName(_theme.Current);

    [ObservableProperty]
    public partial string Language { get; set; } = _strings.Culture.TwoLetterISOLanguageName == "sv" ? "sv" : "en";

    partial void OnThemeNameChanged(string value) => _theme.Current = ThemePageViewModel.NameToTheme(value);

    partial void OnLanguageChanged(string value) => _strings.Culture = CultureInfo.GetCultureInfo(value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedText))]
    public partial DateTime SelectedDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    public partial DateTime DisplayDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial bool ShowWeekNumbers { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowTrailingDays { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowSelectedDate { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstDayOfWeek))]
    public partial string FirstDay { get; set; } = nameof(DayOfWeek.Monday);

    public DayOfWeek FirstDayOfWeek => Enum.Parse<DayOfWeek>(FirstDay);

    /// <summary>"App" follows SpineStrings (the Strings page's language switch); the rest pin a culture.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Culture))]
    public partial string CultureName { get; set; } = "App";

    public CultureInfo? Culture => CultureName == "App" ? null : CultureInfo.GetCultureInfo(CultureName);

    public string SelectedText => SelectedDate == DateTime.MinValue ? "No selection" : $"Selected: {SelectedDate:yyyy-MM-dd}";

    public string DisplayText => $"Showing: {DisplayDate:yyyy-MM}";

    [RelayCommand] private void GoToToday() => SelectedDate = DateTime.Today;

    [RelayCommand] private void ClearSelection() => SelectedDate = DateTime.MinValue;

    // ── Marked days ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkSource))]
    public partial bool ShowMarks { get; set; } = true;

    public ICalendarMarkSource? MarkSource => ShowMarks ? Events : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalendarStyle))]
    public partial string MarkStyleName { get; set; } = nameof(CalendarMarkStyle.Fill);

    // Null keeps the defaults; a StyleOptions change rebuilds the calendar.
    public CalendarStyleOptions? CalendarStyle =>
        MarkStyleName == nameof(CalendarMarkStyle.Dot) ? new CalendarStyleOptions { MarkStyle = CalendarMarkStyle.Dot } : null;

    [ObservableProperty]
    public partial string MarkQueryText { get; set; } = "The service has not been asked yet.";

    [RelayCommand] private void ChangeEvents() => Events.Reshuffle();

    // The service lives as long as this view model, so the counter needs no unsubscribing.
    private FakeEventService Events => field ??= new FakeEventService((first, last, count) =>
        MainThread.BeginInvokeOnMainThread(() => MarkQueryText = $"Asked {count}×, last for {first:MMM d} – {last:MMM d}"));

    /// <summary>
    /// Stands in for an app's own service: it knows the "events", the calendar only asks which days
    /// have one. Pseudo-random days per month, answered after a short delay like a network call.
    /// </summary>
    private sealed class FakeEventService(Action<DateOnly, DateOnly, int> asked) : ICalendarMarkSource
    {
        private int _seed = 1;
        private int _count;

        public event EventHandler? Changed;

        public async ValueTask<IReadOnlyCollection<DateOnly>> GetMarkedDatesAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken)
        {
            asked(first, last, Interlocked.Increment(ref _count));
            await Task.Delay(300, cancellationToken);

            // Today always has something, so the today-and-marked look is on show.
            var today = DateOnly.FromDateTime(DateTime.Today);
            var marked = new List<DateOnly>();
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                if (day == today || Scatter(day.DayNumber, _seed) % 5 == 0)
                    marked.Add(day);
            }
            return marked;
        }

        /// <summary>New data: the calendar asks again for the months it shows.</summary>
        public void Reshuffle()
        {
            _seed++;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        // Stable across runs, unlike HashCode.Combine.
        private static uint Scatter(int day, int seed)
        {
            var h = (uint)day * 2654435761u ^ (uint)seed * 40503u;
            h ^= h >> 15;
            h *= 2246822519u;
            return h ^ (h >> 13);
        }
    }
}
