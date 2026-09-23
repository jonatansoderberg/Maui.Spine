namespace MauiSpineSampleApp.Pages.Typography;

public partial class TypographyPageViewModel : ViewModelBase
{
    private CancellationTokenSource? _ticking;

    [ObservableProperty]
    public partial string Clock { get; set; } = "00:00:00.0";

    [ObservableProperty]
    public partial string Score { get; set; } = "1 – 1";

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        _ticking = new CancellationTokenSource();
        _ = TickAsync(_ticking.Token);
        return base.OnAppearingAsync(navigationDirection);
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _ticking?.Cancel();
        return base.OnDisappearingAsync(navigationDirection);
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var random = new Random();

        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            var now = DateTime.Now;
            var score = $"{random.Next(0, 12)} – {random.Next(0, 12)}";

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Clock = now.ToString("HH:mm:ss.f");
                if (now.Second % 3 == 0 && now.Millisecond < 100)
                    Score = score;
            });
        }
    }
}
