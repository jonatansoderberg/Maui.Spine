using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;
using System.Reflection;

namespace Plugin.Maui.Spine.BackgroundTasks.Services;

/// <summary>
/// The tasks: <see cref="BackgroundTaskAttribute"/> classes found in the Spine assemblies, the way
/// <c>WidgetRegistry</c> finds widgets, and the built-in tasks other Spine packages contribute.
/// </summary>
internal sealed class BackgroundTaskRegistry
{
    private readonly Dictionary<string, BackgroundTaskDescriptor> _tasks = new(StringComparer.Ordinal);

    public BackgroundTaskRegistry(SpineOptions spineOptions, IEnumerable<SpineBackgroundTaskRegistration> builtIn, ILogger<BackgroundTaskRegistry> logger)
    {
        foreach (var assembly in spineOptions.Assemblies)
        foreach (var type in SafeTypes(assembly))
        {
            if (!type.IsClass || type.IsAbstract) continue;
            if (type.GetCustomAttribute<BackgroundTaskAttribute>() is not { } task) continue;

            if (!typeof(IBackgroundTask).IsAssignableFrom(type))
            {
                logger.LogWarning("{Type} is decorated with [BackgroundTask(\"{Name}\")] but does not implement IBackgroundTask; ignored.", type.FullName, task.Name);
                continue;
            }

            if (string.IsNullOrWhiteSpace(task.Name) || task.IntervalMinutes < 0)
            {
                logger.LogWarning("[BackgroundTask] on {Type} needs a name and an IntervalMinutes of 0 or more; ignored.", type.FullName);
                continue;
            }

            // A run happens with no window: a task that navigates would do so on a page nobody sees.
            if (type.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => p.ParameterType == typeof(INavigationService)))
                logger.LogWarning("Background task \"{Name}\" ({Type}) takes INavigationService. A background run may happen with no window; do not navigate from it.", task.Name, type.FullName);

            var interval = TimeSpan.FromMinutes(task.IntervalMinutes);
            Add(new BackgroundTaskDescriptor(task.Name, type, () => interval, (services, run, cancellationToken) => RunTypeAsync(type, services, run, cancellationToken))
            {
                RequiresNetwork = task.RequiresNetwork,
                RequiresCharging = task.RequiresCharging,
                Long = task.Long,
                Widgets = task.Widgets,
            }, logger);
        }

        foreach (var registration in builtIn)
            Add(new BackgroundTaskDescriptor(registration.Name, null, registration.Interval, registration.RunAsync) { RequiresNetwork = registration.RequiresNetwork }, logger);

        Tasks = [.. _tasks.Values.OrderBy(t => t.Name, StringComparer.Ordinal)];
        Names = [.. Tasks.Select(t => t.Name)];
    }

    public IReadOnlyList<BackgroundTaskDescriptor> Tasks { get; }

    public IReadOnlyList<string> Names { get; }

    public BackgroundTaskDescriptor? Find(string name) => _tasks.GetValueOrDefault(name);

    public BackgroundTaskDescriptor? Find(Type taskType) => Tasks.FirstOrDefault(t => t.TaskType == taskType);

    private void Add(BackgroundTaskDescriptor task, ILogger logger)
    {
        if (!_tasks.TryAdd(task.Name, task))
            logger.LogWarning("Background task \"{Name}\" is declared by both {First} and {Second}; the first wins.", task.Name, _tasks[task.Name].TaskType?.FullName ?? "Spine", task.TaskType?.FullName ?? "Spine");
    }

    private static async Task RunTypeAsync(Type type, IServiceProvider services, BackgroundTaskRun run, CancellationToken cancellationToken)
    {
        // Made with the run's scoped services but not owned by the scope, so disposed here.
        var task = (IBackgroundTask)ActivatorUtilities.CreateInstance(services, type);
        try
        {
            await task.RunAsync(run, cancellationToken);
        }
        finally
        {
            if (task is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else if (task is IDisposable disposable) disposable.Dispose();
        }
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t is not null)!; }
    }
}
