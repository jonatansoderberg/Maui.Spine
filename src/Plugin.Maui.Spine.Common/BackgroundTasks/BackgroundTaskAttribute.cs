namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Declares an <see cref="IBackgroundTask"/> to <c>Plugin.Maui.Spine.BackgroundTasks</c>, which finds it
/// in the Spine assemblies at startup, the same way pages and widgets are found.
/// </summary>
/// <param name="name">Stable name of the task; what <see cref="IBackgroundTasks.RequestAsync(string, CancellationToken)"/> takes.</param>
/// <remarks>
/// The platform decides when a scheduled task actually runs. iOS runs it when it judges the app worth
/// it, never on a clock, and not at all after the user force-quits the app or turns Background App
/// Refresh off; Android runs it at most every 15 minutes; Windows and Mac Catalyst only while the app
/// is running.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BackgroundTaskAttribute(string name) : Attribute
{
    /// <summary>Stable name of the task. Keep it once shipped: the schedule and its status are stored under it.</summary>
    public string Name { get; } = name;

    /// <summary>
    /// Minutes between scheduled runs, a floor rather than a time. <c>0</c> (default) runs the task only
    /// when requested. Android's floor is 15; the app can change the interval at run time through the
    /// options of <c>UseSpineBackgroundTasks</c>.
    /// </summary>
    public int IntervalMinutes { get; set; }

    /// <summary>A scheduled run waits for a network connection.</summary>
    public bool RequiresNetwork { get; set; }

    /// <summary>A scheduled run waits for the device to charge. Android, and long tasks on iOS.</summary>
    public bool RequiresCharging { get; set; }

    /// <summary>
    /// Minutes of work rather than seconds: a <c>BGProcessingTask</c> on iOS (it runs only while the device
    /// is idle, and needs <c>SpineBackgroundTasksProcessing=true</c>), a normal job on Android.
    /// </summary>
    public bool Long { get; set; }

    /// <summary>Widget kinds rebuilt after a run that completed, through <see cref="IWidgetService"/>.</summary>
    public string[] Widgets { get; set; } = [];
}
