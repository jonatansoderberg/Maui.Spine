using MauiSpineSampleApp.BackgroundTasks;
using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Pages.BackgroundTasks;

public partial class BackgroundTasksPageViewModel : SampleViewModel
{
    private readonly IBackgroundTasks _tasks;

    public BackgroundTasksPageViewModel(IBackgroundTasks tasks)
    {
        _tasks = tasks;
        Tasks = [.. tasks.Names.Select(name => new BackgroundTaskRow(name, tasks))];
        foreach (var row in Tasks) row.Update();
        Platform = !tasks.RunsWhileClosed
            ? $"On {DeviceInfo.Platform} nothing launches the app for them: they run while it is open, checked every minute."
            : DeviceInfo.Platform == DevicePlatform.Android
                ? "On Android JobScheduler runs these while the app is closed, at most every 15 minutes, and again after a restart."
                : "On iOS the system runs these while the app is closed, when it judges the app worth it.";

        // Runs start and end on the thread pool; the handler is brought to the UI thread.
        WhileVisible<string>(h => _tasks.StatusChanged += h, h => _tasks.StatusChanged -= h, name =>
        {
            Tasks.FirstOrDefault(t => t.Name == name)?.Update();
            SyncRuns = Preferences.Default.Get(SampleSyncTask.RunsKey, 0);
        });
        // "Next in 12 min" counts down.
        Poll(TimeSpan.FromSeconds(15), _ =>
        {
            foreach (var row in Tasks) row.Update();
            SyncRuns = Preferences.Default.Get(SampleSyncTask.RunsKey, 0);
            return Task.CompletedTask;
        });
    }

    public IReadOnlyList<BackgroundTaskRow> Tasks { get; }

    public string Platform { get; }

    [ObservableProperty]
    public partial int SyncRuns { get; set; }

    [RelayCommand]
    private Task Run(BackgroundTaskRow row) => _tasks.RequestAsync(row.Name);
}

/// <summary>One task's status, as <see cref="IBackgroundTasks.StatusOf"/> reports it.</summary>
public partial class BackgroundTaskRow(string name, IBackgroundTasks tasks) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial string Schedule { get; set; } = "";

    [ObservableProperty]
    public partial string LastRun { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    public void Update()
    {
        var status = tasks.StatusOf(Name);
        IsRunning = status.IsRunning;
        Error = status.LastOutcome == BackgroundTaskOutcome.Failed ? status.LastError : null;

        Schedule = status.Interval <= TimeSpan.Zero
            ? "On request only"
            : $"Every {status.Interval.TotalMinutes:0} min · {Due(status.NextDue)}";

        LastRun = status.IsRunning ? "Running…"
            : status.LastEnded is not { } ended ? "Not run yet"
            : $"{status.LastOutcome} {ended.ToLocalTime():HH:mm:ss} · {status.LastTrigger}";
    }

    private static string Due(DateTimeOffset? at)
    {
        if (at is not { } due) return "";
        var left = due - DateTimeOffset.UtcNow;
        return left <= TimeSpan.Zero ? "due now" : $"next in {Math.Ceiling(left.TotalMinutes):0} min";
    }
}
