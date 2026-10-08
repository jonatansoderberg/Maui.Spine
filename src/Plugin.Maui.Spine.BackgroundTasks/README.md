# Plugin.Maui.Spine.BackgroundTasks

Background tasks for .NET MAUI: a class with `[BackgroundTask]` is found at startup, like a Spine page or widget, and run by the platform's scheduler while the app is not on screen. That is `BGTaskScheduler` on iOS and `JobScheduler` on Android. On Windows and Mac Catalyst a timer runs the tasks while the app is open. After a run, the widgets the task names are rebuilt, so a home-screen widget stays fresh without the app being opened.

```bash
dotnet add package Plugin.Maui.Spine.BackgroundTasks
```

```csharp
builder.UseSpine(options => options.AddAssembly(typeof(MauiProgram).Assembly));   // registers BackgroundTasks too
// builder.UseSpineBackgroundTasks(o => o.Interval("standings", TimeSpan.FromHours(1)));   // only to change the options
```

```csharp
[BackgroundTask("standings", IntervalMinutes = 30, RequiresNetwork = true, Widgets = ["team"])]
public sealed class StandingsTask(IStandingsApi api, StandingsStore store) : IBackgroundTask
{
    public async Task RunAsync(BackgroundTaskRun run, CancellationToken cancellationToken) =>
        await store.SaveAsync(await api.FetchAsync(cancellationToken), cancellationToken);
}

await tasks.RequestAsync("standings");            // IBackgroundTasks: run it now
var status = tasks.StatusOf("standings");         // last run, outcome, error, next due
```

The platform decides when a scheduled task runs. iOS runs it when it chooses, never on a clock, and not after the user swipes the app away. Android runs it at most every 15 minutes. Windows and Mac Catalyst run it only while the app runs (`IBackgroundTasks.RunsWhileClosed` is `false` there).

On iOS the build adds `UIBackgroundModes: fetch` and one task identifier, `<ApplicationId>.spine.refresh`, to `Info.plist`. Spine runs every task under that one identifier, because iOS allows one pending refresh request per app. With `Plugin.Maui.Spine.Widgets` in the app, the widgets' background refresh runs as the built-in task `spine.widgets`.

## Documentation

- [Background tasks](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/background-tasks.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
