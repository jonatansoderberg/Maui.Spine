using System.Collections.ObjectModel;
using MauiSpineSampleApp.Push;

namespace MauiSpineSampleApp.Pages.Push;

public partial class PushLogPageViewModel(PushLog _log) : ViewModelBase
{
    /// <summary>Everything the handler, the widgets and the pages noted, newest first.</summary>
    public ObservableCollection<PushLogEntry> Entries => _log.Entries;

    [PageAction("Clear")]
    [RelayCommand]
    private void Clear() => _log.Clear();
}
