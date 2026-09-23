namespace MauiSpineSampleApp.Pages.Lifetime;

public partial class LifetimePageViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial int Ticks { get; set; }

    [ObservableProperty]
    public partial string LastTick { get; set; } = "not yet";

    [ObservableProperty]
    public partial string Download { get; set; } = "idle";

    [ObservableProperty]
    public partial string Network { get; set; } = Connectivity.Current.NetworkAccess.ToString();

    public LifetimePageViewModel()
    {
        // Runs every second while the page shows; pauses in the background and when the page is
        // left, and runs again at once when it is back. No timer, no token, no unsubscribe.
        Poll(TimeSpan.FromSeconds(1), _ =>
        {
            Ticks++;
            LastTick = DateTime.Now.ToString("HH:mm:ss");
            return Task.CompletedTask;
        });

        // Subscribed while the page shows; the handler arrives on the UI thread.
        WhileVisible<ConnectivityChangedEventArgs>(
            h => Connectivity.Current.ConnectivityChanged += h,
            h => Connectivity.Current.ConnectivityChanged -= h,
            (_, e) => Network = e.NetworkAccess.ToString());
    }

    // A load that should not outlive the page: PageLifetime cancels it when the page is left.
    [RelayCommand]
    private async Task StartDownload()
    {
        try
        {
            for (var i = 1; i <= 10; i++)
            {
                await Task.Delay(1000, PageLifetime);
                Download = $"{i * 10} %";
            }

            Download = "done";
        }
        catch (OperationCanceledException)
        {
            Download = "cancelled: the page went away";
        }
    }
}
