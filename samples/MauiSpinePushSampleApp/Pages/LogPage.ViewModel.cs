using System.Collections.ObjectModel;
using MauiSpinePushSampleApp.Services;

namespace MauiSpinePushSampleApp.Pages;

public partial class LogPageViewModel(PushLog _log, INavigationService _navigation) : ViewModelBase
{
    /// <summary>Everything the handler has seen, newest first.</summary>
    public ObservableCollection<PushLogEntry> Entries => _log.Entries;

    [RelayCommand]
    private void Clear() => _log.Clear();

    /// <summary>
    /// Tapping a row goes where the message said to. It is the same code path
    /// <see cref="SamplePushHandler.OnOpenedAsync"/> takes, only triggered by hand.
    /// </summary>
    [RelayCommand]
    private async Task Open(PushLogEntry? entry)
    {
        if (entry?.Route is "log") await _navigation.NavigateToAsync<LogPage>();
    }
}
