using System.Diagnostics;
using Plugin.Maui.Spine.Images;

namespace MauiSpineSampleApp.Pages.Images;

public partial class ImagesPageViewModel(IImageCache _images) : SampleViewModel
{
    [ObservableProperty]
    public partial IReadOnlyList<RemotePhoto> Photos { get; set; } = RemotePhoto.All;

    [ObservableProperty]
    public partial string CacheStatus { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrefetchCommand), nameof(ClearCommand))]
    public partial bool IsBusy { get; set; }

    private bool CanRun => !IsBusy;

    public override Task OnAppearingAsync(NavigationDirection navigationDirection) => CountAsync();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Prefetch()
    {
        IsBusy = true;
        try
        {
            var watch = Stopwatch.StartNew();
            await _images.PrefetchAsync(RemotePhoto.All.Select(p => p.Url));
            await CountAsync($"Prefetched in {watch.Elapsed.TotalSeconds:F1} s. ");
        }
        catch (Exception exception)
        {
            CacheStatus = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Clear()
    {
        IsBusy = true;
        try
        {
            await _images.ClearAsync();
            // A new list makes every cell load again, so the placeholders show.
            Photos = [.. RemotePhoto.All];
            await CountAsync("Cleared. ");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CountAsync(string prefix = "")
    {
        var cached = 0;
        foreach (var photo in RemotePhoto.All)
            if (await _images.ContainsAsync(photo.Url))
                cached++;
        CacheStatus = $"{prefix}{cached} of {RemotePhoto.All.Count} photos on disk.";
    }
}
