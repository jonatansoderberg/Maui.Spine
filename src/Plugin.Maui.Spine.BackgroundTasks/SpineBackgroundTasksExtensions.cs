using AsyncAwaitBestPractices;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.BackgroundTasks.Services;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.BackgroundTasks;

/// <summary>Registers Spine background tasks with the MAUI application builder.</summary>
public static partial class SpineBackgroundTasksExtensions
{
    /// <summary>
    /// Adds <see cref="IBackgroundTasks"/>, discovers <see cref="BackgroundTaskAttribute"/> classes in the
    /// Spine assemblies and hooks the platform's scheduler into the app's launch. <c>UseSpine()</c> calls it
    /// for an app that references this package; call it yourself only to configure the options, before or
    /// after <c>UseSpine()</c>. The first call registers the services; every call applies its
    /// <paramref name="configure"/> to the same options instance.
    /// </summary>
    public static MauiAppBuilder UseSpineBackgroundTasks(this MauiAppBuilder builder, Action<SpineBackgroundTasksOptions>? configure = null)
    {
        var services = builder.Services;

        if (services.FirstOrDefault(static d => d.ServiceType == typeof(SpineBackgroundTasksOptions) && !d.IsKeyedService)?.ImplementationInstance
            is not SpineBackgroundTasksOptions options)
        {
            options = new SpineBackgroundTasksOptions();
            services.AddSingleton(options);
            services.AddSingleton<BackgroundTaskRegistry>();
            services.AddSingleton<BackgroundTaskService>();
            services.AddSingleton<IBackgroundTasks>(static sp => sp.GetRequiredService<BackgroundTaskService>());

            ConfigurePlatform(builder, options);
        }

        configure?.Invoke(options);
        return builder;
    }

    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineBackgroundTasksOptions options);

    private static IServiceProvider Services() => IPlatformApplication.Current?.Services
        ?? throw new InvalidOperationException("The MAUI application has not started.");

    /// <summary>Runs the due tasks without blocking the caller, when the options allow it.</summary>
    private static void CatchUp(SpineBackgroundTasksOptions options)
    {
        if (!options.CatchUp || IPlatformApplication.Current?.Services is not { } services) return;
        var tasks = services.GetRequiredService<BackgroundTaskService>();
        tasks.RunDueAsync(static _ => true, BackgroundTaskTrigger.CatchUp, CancellationToken.None)
            .SafeFireAndForget(e => services.GetRequiredService<ILogger<IBackgroundTasks>>().LogError(e, "The catch-up run of the background tasks failed."));
    }
}
