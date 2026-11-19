namespace MauiSpineSampleApp.Pages.HeaderRow;

public sealed record Week(string Name, string Detail);

public partial class HeaderRowPageViewModel : SampleViewModel
{
    private static readonly string[] Periods = ["7 days", "14 days", "30 days", "90 days"];

    [ObservableProperty] public partial int PeriodIndex { get; set; }
    [ObservableProperty] public partial IReadOnlyList<Week> Weeks { get; set; } = Make(0);

    partial void OnPeriodIndexChanged(int value) => Weeks = Make(value);

    private static IReadOnlyList<Week> Make(int period) =>
        [.. Enumerable.Range(1, 30).Select(i => new Week($"Card {i}", $"Figures for the last {Periods[Math.Clamp(period, 0, Periods.Length - 1)]}"))];
}
